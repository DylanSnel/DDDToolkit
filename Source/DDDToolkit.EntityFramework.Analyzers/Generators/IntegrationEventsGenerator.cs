using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
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
/// <para>
/// A module's domain project need not reference Entity Framework, and then writes no registration of its own.
/// Its domain events are registered by the module's projects that do: every concrete, non-generic domain event
/// of a referenced assembly with the same <c>[Module]</c> that does not reference DDDToolkit.EntityFramework,
/// under the name and version its own registration would have given it, which the declaring assembly's module
/// decides. Such an event this project cannot see is DDD00033 on the <c>[assembly: Module]</c> attribute, rather
/// than an event found out at run time: the outbox would still store it when it is raised, under the same name,
/// and the processor would never deliver it, recording the missing registration on its row at every attempt. An
/// assembly that references Entity Framework writes its own registration, so its events are not registered twice,
/// and one that declares no module is a package's.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class IntegrationEventsGenerator : IIncrementalGenerator
{
    private const string IntegrationNamespace = "DDDToolkit.EntityFramework.Integration";
    private const string OutboxOptionsMetadataName = "DDDToolkit.EntityFramework.Options.OutboxOptions";
    private const string ConsumerAttribute = IntegrationNamespace + ".IntegrationEventConsumerAttribute";
    private const string EntityFrameworkAssembly = "DDDToolkit.EntityFramework";

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

        // Read off the compilation, so it runs again on every edit; the walk of each referenced assembly is cached,
        // and what comes out compares equal when nothing it names changed.
        var referenced = context.CompilationProvider
            .Combine(context.ProjectFile())
            .Select(static (pair, cancellationToken) => ModuleEvents(pair.Left, pair.Right, cancellationToken));

        var registration = found
            .Combine(referenced)
            .Combine(enabled)
            .Combine(context.RegistrationName())
            .Combine(context.AssemblyName());

        context.RegisterSourceOutput(registration, static (production, data) =>
        {
            var ((((types, others), isEnabled), moduleName), assemblyName) = data;

            if (!isEnabled)
            {
                return;
            }

            others.Problems.ReportAll(production);

            var own = types.IsDefault ? ImmutableArray<FoundType>.Empty : types;
            if (own.IsEmpty && others.Events.Count == 0)
            {
                return;
            }

            foreach (var type in own)
            {
                type.Problem?.Report(production);
            }

            production.AddSource(
                "IntegrationEventExtensions.g.cs",
                SourceText.From(Write(own.AddRange(others.Events), moduleName, assemblyName), Encoding.UTF8));
        });
    }

    // ------------------------------------------------------------------ the module's other projects

    /// <summary>
    /// The domain events of this module's referenced projects that have no registration of their own, and DDD00033
    /// for each one this project cannot see. Nothing when this project declares no module.
    /// </summary>
    /// <param name="compilation">The project.</param>
    /// <param name="projectFile">Where DDD00033 is reported when no <c>[assembly: Module]</c> in a file of the project says which module it is.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    private static ReferencedEvents ModuleEvents(Compilation compilation, LocationInfo? projectFile, CancellationToken cancellationToken)
    {
        if (ModuleBoundary.ModuleOf(compilation.Assembly) is not { } module)
        {
            return ReferencedEvents.None;
        }

        var events = new List<FoundType>();
        var problems = new List<DiagnosticInfo>();
        LocationInfo? moduleAttribute = null;
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(ModuleBoundary.ModuleOf(assembly), module, StringComparison.Ordinal) || RegistersItsOwn(assembly))
            {
                continue;
            }

            foreach (var type in DomainEventsOf(assembly, cancellationToken))
            {
                if (compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
                {
                    var (name, version) = EventNaming.DomainEventOf(type);
                    events.Add(new FoundType(Name(type), name, version, EquatableArray<Outbound>.Empty, EquatableArray<Handler>.Empty, EquatableArray<string>.Empty, null));
                    continue;
                }

                moduleAttribute ??= ModuleBoundary.WhereTheModuleIsDeclared(compilation, projectFile, cancellationToken);
                problems.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.IntegrationEventClassNotConstructible,
                    moduleAttribute,
                    type.ToDisplayString(),
                    "a domain event of " + assembly.Identity.Name + ", a project of this module, that this project cannot see; make it public"));
            }
        }

        return new ReferencedEvents(events.ToEquatableArray(), problems.ToEquatableArray());
    }

    /// <summary>
    /// Whether an assembly references DDDToolkit.EntityFramework, and so wrote a registration of its own when it was
    /// built. The compiler records only the references an assembly uses, and a written registration uses this one.
    /// </summary>
    private static bool RegistersItsOwn(IAssemblySymbol assembly)
        => assembly.Modules.Any(static part => part.ReferencedAssemblies.Any(static reference => reference.Name == EntityFrameworkAssembly));

    /// <summary>
    /// The concrete, non-generic domain events of a referenced assembly, public or not: whether this project can see
    /// one is a question for each compilation. Walked once per assembly symbol, which the compiler keeps for as long
    /// as the reference does not change.
    /// </summary>
    private static INamedTypeSymbol[] DomainEventsOf(IAssemblySymbol assembly, CancellationToken cancellationToken)
    {
        if (DomainEventsByAssembly.TryGetValue(assembly, out var known))
        {
            return known;
        }

        var found = DefinitionFactory.TypesIn(assembly.GlobalNamespace, cancellationToken)
            .Where(static type => type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsStatic && !IsGeneric(type) && EventNaming.IsDomainEvent(type))
            .ToArray();

        return DomainEventsByAssembly.GetValue(assembly, _ => found);
    }

    private static readonly ConditionalWeakTable<IAssemblySymbol, INamedTypeSymbol[]> DomainEventsByAssembly = new();

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

        var isDomainEvent = EventNaming.IsDomainEvent(type);

        var outbound = type.AllInterfaces
            .Where(static candidate => Is(candidate, IntegrationNamespace, "IOutboundIntegrationEvent", 2))
            .Select(candidate =>
            {
                var contract = EventNaming.ContractOf(candidate.TypeArguments[1]);
                return new Outbound(Name(candidate.TypeArguments[0]), Name(candidate.TypeArguments[1]), contract.Name, contract.Version);
            })
            .OrderBy(static entry => entry.DomainEvent, StringComparer.Ordinal)
            .ToList();

        var handlers = type.AllInterfaces
            .Where(static candidate => Is(candidate, IntegrationNamespace, "IIntegrationEventHandler", 1))
            .Select(candidate =>
            {
                var contract = EventNaming.ContractOf(candidate.TypeArguments[0]);
                return new Handler(Name(candidate.TypeArguments[0]), contract.Name, contract.Version, ConsumerOf(type));
            })
            .OrderBy(static entry => entry.Contract, StringComparer.Ordinal)
            .ToList();

        if (!isDomainEvent && outbound.Count == 0 && handlers.Count == 0)
        {
            return null;
        }

        var (eventName, eventVersion) = isDomainEvent ? EventNaming.DomainEventOf(type) : (null, 0);

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

    /// <summary>What the module's other projects add: their domain events, and why any were left out.</summary>
    private sealed record ReferencedEvents(EquatableArray<FoundType> Events, EquatableArray<DiagnosticInfo> Problems)
    {
        public static readonly ReferencedEvents None = new(EquatableArray<FoundType>.Empty, EquatableArray<DiagnosticInfo>.Empty);
    }
}
