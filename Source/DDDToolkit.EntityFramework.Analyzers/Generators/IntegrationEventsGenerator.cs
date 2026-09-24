using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.EntityFramework.Analyzers;

/// <summary>
/// Writes a module's integration event registration: one <c>Add{Module}IntegrationEvents()</c> for each of
/// the three places that need to know what the module sends and receives.
/// <list type="bullet">
///   <item><description>On the outbox: every domain event in the assembly, under its stable name and version,
///   and every <c>IOutboundIntegrationEvent&lt;TDomainEvent, TContract&gt;</c>, with the contract's published
///   name and version.</description></item>
///   <item><description>On the contract registry: every contract the module's handlers read.</description></item>
///   <item><description>On the module's consumers: every <c>IIntegrationEventHandler&lt;TContract&gt;</c>, under
///   its consumer name and its contract's published name.</description></item>
/// </list>
/// <para>
/// Everything the run-time registration would read off attributes or find by scanning an assembly is read
/// here, when the module compiles, and written into the generated code as literals. The classes are built
/// with <c>new</c>, each constructor parameter taken from the scope the message is delivered in. Nothing
/// is found, read or created by reflection when the application runs.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class IntegrationEventsGenerator : IIncrementalGenerator
{
    private const string IntegrationNamespace = "DDDToolkit.EntityFramework.Integration";
    private const string OutboxOptionsMetadataName = "DDDToolkit.EntityFramework.Options.OutboxOptions";
    private const string DomainEventNameAttribute = KnownTypes.AttributesNamespace + ".DomainEventNameAttribute";
    private const string ConsumerAttribute = IntegrationNamespace + ".IntegrationEventConsumerAttribute";

    private const string Services = "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var enabled = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName(OutboxOptionsMetadataName) is not null);

        var found = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null } or RecordDeclarationSyntax { BaseList: not null },
                transform: static (syntaxContext, cancellationToken) => Describe(syntaxContext, cancellationToken))
            .Where(static type => type is not null)
            .Select(static (type, _) => type!)
            .Collect();

        var registration = found
            .Combine(enabled)
            .Combine(context.GetDDDOptions())
            .Combine(context.AssemblyName());

        context.RegisterSourceOutput(registration, static (production, data) =>
        {
            var (((types, isEnabled), options), assemblyName) = data;

            if (!isEnabled || types.IsDefaultOrEmpty)
            {
                return;
            }

            foreach (var type in types)
            {
                type.Problem?.Report(production);
            }

            production.AddSource(
                "IntegrationEventExtensions.g.cs",
                SourceText.From(Write(types, options.ResolveModuleName(assemblyName), assemblyName), Encoding.UTF8));
        });
    }

    private static FoundType? Describe(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
    {
        if (syntaxContext.SemanticModel.GetDeclaredSymbol(syntaxContext.Node, cancellationToken) is not INamedTypeSymbol type)
        {
            return null;
        }

        // A partial type has several declarations; describe it once, from the first.
        if (type.DeclaringSyntaxReferences.Length > 1
            && type.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) != syntaxContext.Node)
        {
            return null;
        }

        var compilation = syntaxContext.SemanticModel.Compilation;

        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || IsGeneric(type)
            || !compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
        {
            return null;
        }

        var isDomainEvent = type.AllInterfaces.Any(static candidate => Is(candidate, "DDDToolkit.Interfaces", "IDomainEvent", 0));

        var outbound = type.AllInterfaces
            .Where(static candidate => Is(candidate, IntegrationNamespace, "IOutboundIntegrationEvent", 2))
            .Select(candidate =>
            {
                var contract = ContractOf(candidate.TypeArguments[1]);
                return new Outbound(Name(candidate.TypeArguments[0]), Name(candidate.TypeArguments[1]), contract.Name, contract.Version);
            })
            .OrderBy(static entry => entry.DomainEvent, StringComparer.Ordinal)
            .ToList();

        var handlers = type.AllInterfaces
            .Where(static candidate => Is(candidate, IntegrationNamespace, "IIntegrationEventHandler", 1))
            .Select(candidate =>
            {
                var contract = ContractOf(candidate.TypeArguments[0]);
                return new Handler(Name(candidate.TypeArguments[0]), contract.Name, contract.Version, ConsumerOf(type));
            })
            .OrderBy(static entry => entry.Contract, StringComparer.Ordinal)
            .ToList();

        if (!isDomainEvent && outbound.Count == 0 && handlers.Count == 0)
        {
            return null;
        }

        var (eventName, eventVersion) = isDomainEvent ? DomainEventOf(type) : (null, 0);

        EquatableArray<string> arguments = default;
        DiagnosticInfo? problem = null;

        if (outbound.Count > 0 || handlers.Count > 0)
        {
            if (ConstructorArguments(type, compilation, out var resolved) is { } reason)
            {
                problem = DiagnosticInfo.Create(DiagnosticDescriptors.IntegrationEventClassNotConstructible, LocationInfo.From(type), type.ToDisplayString(), reason);
                outbound.Clear();
                handlers.Clear();
            }
            else
            {
                arguments = resolved.ToEquatableArray();
            }
        }

        return new FoundType(
            Name(type),
            eventName,
            eventVersion,
            outbound.ToEquatableArray(),
            handlers.ToEquatableArray(),
            arguments,
            problem);
    }

    /// <summary>
    /// The expression for each constructor argument, or why there is none. The constructor with the most
    /// parameters wins, as it does for the container, and a tie is a question the generator does not guess
    /// the answer to.
    /// </summary>
    private static string? ConstructorArguments(INamedTypeSymbol type, Compilation compilation, out List<string> arguments)
    {
        arguments = [];

        var constructors = type.InstanceConstructors
            .Where(constructor => compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly))
            .OrderByDescending(static constructor => constructor.Parameters.Length)
            .ToList();

        if (constructors.Count == 0)
        {
            return "it has no constructor this assembly can call";
        }

        if (constructors.Count > 1 && constructors[0].Parameters.Length == constructors[1].Parameters.Length)
        {
            return $"it has more than one constructor with {constructors[0].Parameters.Length} parameters, and the registration would have to guess";
        }

        foreach (var parameter in constructors[0].Parameters)
        {
            if (parameter.RefKind != RefKind.None || parameter.IsParams)
            {
                return $"its constructor parameter '{parameter.Name}' is ref, out or params";
            }

            if (!compilation.IsSymbolAccessibleWithin(parameter.Type, compilation.Assembly))
            {
                return $"this assembly cannot see the type of its constructor parameter '{parameter.Name}'";
            }

            var parameterType = Name(parameter.Type);

            if (parameterType == "global::System.IServiceProvider")
            {
                arguments.Add("services");
            }
            else if (parameter.HasExplicitDefaultValue && parameter.Type.IsReferenceType)
            {
                arguments.Add($"{Services}.GetService<{parameterType}>(services)");
            }
            else
            {
                arguments.Add($"{Services}.GetRequiredService<{parameterType}>(services)");
            }
        }

        return null;
    }

    /// <summary>The stable name and version the outbox stores the event under, as <c>DomainEventName.Of</c> and <c>IntegrationEventContract.VersionOf</c> read them.</summary>
    private static (string Name, int Version) DomainEventOf(INamedTypeSymbol type)
    {
        var name = StringArgument(Attribute(type, DomainEventNameAttribute)) ?? type.MetadataName;
        return (name, VersionArgument(Attribute(type, KnownTypes.IntegrationEventAttribute)));
    }

    /// <summary>
    /// The published name and version of a contract, with the fallbacks <c>IntegrationEventContract</c> uses:
    /// <c>[IntegrationEvent]</c>, otherwise <c>[DomainEventName]</c> at version 1, otherwise the class name at
    /// version 1.
    /// </summary>
    private static (string Name, int Version) ContractOf(ITypeSymbol contract)
    {
        if (Attribute(contract, KnownTypes.IntegrationEventAttribute) is { } published && StringArgument(published) is { } name)
        {
            return (name, VersionArgument(published));
        }

        return (StringArgument(Attribute(contract, DomainEventNameAttribute)) ?? contract.MetadataName, 1);
    }

    /// <summary>The consumer name the inbox keys on: <c>[IntegrationEventConsumer]</c>, otherwise the full CLR type name.</summary>
    private static string ConsumerOf(INamedTypeSymbol handler)
    {
        if (StringArgument(Attribute(handler, ConsumerAttribute)) is { } name)
        {
            return name;
        }

        var names = new Stack<string>();
        for (var current = handler; current is not null; current = current.ContainingType)
        {
            names.Push(current.MetadataName);
        }

        var prefix = handler.ContainingNamespace.IsGlobalNamespace ? string.Empty : handler.ContainingNamespace.ToDisplayString() + ".";
        return prefix + string.Join("+", names);
    }

    private static AttributeData? Attribute(ITypeSymbol type, string metadataName)
        => type.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == metadataName);

    private static string? StringArgument(AttributeData? attribute)
        => attribute is { ConstructorArguments.Length: > 0 } && attribute.ConstructorArguments[0].Value is string value ? value : null;

    private static int VersionArgument(AttributeData? attribute)
    {
        if (attribute is null)
        {
            return 1;
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Version" && argument.Value.Value is int version)
            {
                return version;
            }
        }

        return 1;
    }

    private static bool Is(INamedTypeSymbol candidate, string @namespace, string name, int arity)
        => candidate.Name == name
           && candidate.Arity == arity
           && candidate.ContainingNamespace.ToDisplayString() == @namespace;

    private static bool IsGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                return true;
            }
        }

        return false;
    }

    private static string Name(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Write(ImmutableArray<FoundType> found, string module, string? assemblyName)
    {
        // Sorted, so the file does not change when the compiler hands the declarations over in another order.
        var types = found
            .GroupBy(static type => type.Type, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static type => type.Type, StringComparer.Ordinal)
            .ToList();

        var method = "Add" + module + "IntegrationEvents";
        const string Outbox = "global::DDDToolkit.EntityFramework.Options.OutboxOptions";
        const string Contracts = "global::" + IntegrationNamespace + ".IntegrationEventContractRegistry";
        const string Module = "global::" + IntegrationNamespace + ".ModuleIntegrationEvents<TContext>";

        var writer = new CodeWriter().Header();
        writer.Line("namespace " + Identifiers.NamespaceFrom(assemblyName) + ".IntegrationEvents;");
        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// What this module sends and receives, as its compiler found it: the registration the outbox, the contract");
        writer.Line("/// registry and the module's consumers need, with every name and version written out. Nothing here scans an");
        writer.Line("/// assembly, reads an attribute or creates a class by reflection.");
        writer.Line("/// </summary>");

        using (writer.Block("public static class IntegrationEventExtensions"))
        {
            writer.Line("/// <summary>");
            writer.Line("/// Every domain event of this module under the name and version the outbox stores it as, and every outbound");
            writer.Line("/// class with the published name and version of the contract it makes.");
            writer.Line("/// </summary>");
            using (writer.Block($"public static {Outbox} {method}(this {Outbox} outbox)"))
            {
                writer.Line("global::System.ArgumentNullException.ThrowIfNull(outbox);");

                foreach (var type in types.Where(static type => type.EventName is not null))
                {
                    writer.Line($"outbox.RegisterEvent<{type.Type}>({Literal(type.EventName!)}, {type.EventVersion});");
                }

                foreach (var type in types)
                {
                    foreach (var outbound in type.Outbound)
                    {
                        writer.Line(
                            $"outbox.PublishWith<{outbound.DomainEvent}, {outbound.Contract}>({Literal(outbound.ContractName)}, {outbound.ContractVersion}, " +
                            $"static services => new {type.Type}({string.Join(", ", type.Arguments)}));");
                    }
                }

                writer.Line("return outbox;");
            }

            writer.Line();
            writer.Line("/// <summary>Every contract this module's handlers read, under its published name and version.</summary>");
            using (writer.Block($"public static {Contracts} {method}(this {Contracts} contracts)"))
            {
                writer.Line("global::System.ArgumentNullException.ThrowIfNull(contracts);");

                var read = types
                    .SelectMany(static type => type.Handlers)
                    .GroupBy(static handler => handler.Contract, StringComparer.Ordinal)
                    .Select(static group => group.First())
                    .OrderBy(static handler => handler.Contract, StringComparer.Ordinal);

                foreach (var handler in read)
                {
                    writer.Line($"contracts.Register<{handler.Contract}>({Literal(handler.ContractName)}, {handler.ContractVersion});");
                }

                writer.Line("return contracts;");
            }

            writer.Line();
            writer.Line("/// <summary>");
            writer.Line("/// Every handler of this module, under the consumer name its inbox rows carry and the published name of the");
            writer.Line("/// contract it handles, built from the scope each message is delivered in.");
            writer.Line("/// </summary>");
            using (writer.Block($"public static {Module} {method}<TContext>(this {Module} module) where TContext : global::Microsoft.EntityFrameworkCore.DbContext"))
            {
                writer.Line("global::System.ArgumentNullException.ThrowIfNull(module);");

                foreach (var type in types)
                {
                    foreach (var handler in type.Handlers)
                    {
                        writer.Line(
                            $"module.Handle<{handler.Contract}, {type.Type}>({Literal(handler.ContractName)}, {Literal(handler.Consumer)}, " +
                            $"static services => new {type.Type}({string.Join(", ", type.Arguments)}));");
                    }
                }

                writer.Line("return module;");
            }
        }

        return writer.ToString();
    }

    private static string Literal(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private sealed record FoundType(
        string Type,
        string? EventName,
        int EventVersion,
        EquatableArray<Outbound> Outbound,
        EquatableArray<Handler> Handlers,
        EquatableArray<string> Arguments,
        DiagnosticInfo? Problem);

    private sealed record Outbound(string DomainEvent, string Contract, string ContractName, int ContractVersion);

    private sealed record Handler(string Contract, string ContractName, int ContractVersion, string Consumer);
}
