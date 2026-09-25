using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using DDDToolkit.BaseTypes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Writes <c>{Module}EventNames</c>: a constant for every name this assembly's events are stored or published
/// under, so a name written by hand (a topic binding, a test, a pin before a rename) is written once, by the
/// compiler. <c>ordering.order-placed</c> becomes <c>OrderingEventNames.OrderPlaced</c>.
/// <para>
/// The same pass checks the names, because it is the pass that works them out (<see cref="EventNaming"/>):
/// </para>
/// <list type="bullet">
///   <item><description>DDD00034: the class name's version suffix and <c>[IntegrationEvent(Version = n)]</c> disagree, and the suffix is ignored.</description></item>
///   <item><description>DDD00035: the class name ends in a <c>V</c> and digits that cannot be a version.</description></item>
///   <item><description>DDD00036: two domain events, or two contracts, of this assembly share a name and version.</description></item>
///   <item><description>DDD00037: two different names would give one constant, which code could then use for the wrong event.</description></item>
/// </list>
/// <para>
/// Only this assembly's own events are named here. A contract another module publishes gets its constant
/// in that module, and two assemblies of one module that do not reference each other are only compared at
/// start-up, by the registries.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EventNamesGenerator : IIncrementalGenerator
{
    /// <summary>The diagnostic property holding the name a DDD00036 fix pins.</summary>
    public const string SuggestedNameProperty = "SuggestedName";

    /// <summary>The diagnostic property holding the attribute a DDD00036 fix pins it with: <c>DomainEventName</c> or <c>IntegrationEvent</c>.</summary>
    public const string PinWithProperty = "PinWith";

    /// <summary>The diagnostic property holding the class name a DDD00034 fix renames the event to, <c>OrderPlacedV3</c>.</summary>
    public const string RenameToProperty = "RenameTo";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var events = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax declaration
                    && declaration is not InterfaceDeclarationSyntax
                    && (declaration.BaseList is not null || declaration.AttributeLists.Count > 0),
                transform: static (syntaxContext, cancellationToken) => Describe(syntaxContext, cancellationToken))
            .Where(static found => found is not null)
            .Select(static (found, _) => found!)
            .Collect();

        var module = context.CompilationProvider.Select(static (compilation, _) => ModuleBoundary.ModuleOf(compilation.Assembly));

        var input = events
            .Combine(module)
            .Combine(context.GetDDDOptions())
            .Combine(context.AssemblyName());

        context.RegisterSourceOutput(input, static (production, data) =>
        {
            var (((found, moduleName), options), assemblyName) = data;
            if (found.IsDefaultOrEmpty)
            {
                return;
            }

            // Sorted and one per type, so neither the diagnostics nor the file depend on the order the
            // compiler handed the declarations over in.
            var types = found
                .GroupBy(static type => type.Type, StringComparer.Ordinal)
                .Select(static group => group.First())
                .OrderBy(static type => type.Type, StringComparer.Ordinal)
                .ToList();

            foreach (var type in types)
            {
                type.Diagnostics.ReportAll(production);
            }

            ReportTakenNames(production, types.Where(static type => type.IsDomainEvent), static type => type.StoredName!);
            ReportTakenNames(production, types.Where(static type => type.IsContract), static type => type.PublishedName!);

            if (Write(production, types, moduleName, options.ResolveModuleName(assemblyName), assemblyName) is { } source)
            {
                production.AddSource("EventNames.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    private static FoundEvent? Describe(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
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

        if (type.IsAbstract || type.IsStatic || type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || IsGeneric(type))
        {
            return null;
        }

        var isDomainEvent = EventNaming.IsDomainEvent(type);
        var published = EventNaming.IntegrationEventAttributeOf(type);
        var pinnedStored = EventNaming.PinnedDomainEventName(type);

        if (!isDomainEvent && published is null && pinnedStored is null)
        {
            return null;
        }

        var displayName = type.ToDisplayString();
        var location = LocationInfo.From(type);
        var diagnostics = new List<DiagnosticInfo>();

        var suffix = EventNameConvention.Split(type.MetadataName);
        if (suffix.Malformed)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.EventVersionSuffixIsNotAVersion,
                location,
                displayName,
                type.MetadataName.Substring(type.MetadataName.LastIndexOf('V') + 1)));
        }

        if (suffix.Version is { } named && EventNaming.ExplicitVersion(published) is { } stated && named != stated)
        {
            var statedText = stated.ToString(System.Globalization.CultureInfo.InvariantCulture);

            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.EventVersionDisagreesWithItsName,
                VersionArgumentLocation(published!, cancellationToken) ?? location,
                displayName,
                named.ToString(System.Globalization.CultureInfo.InvariantCulture),
                statedText) with
            {
                // The name the class would have if its name said the version it is: the fix renames it to that.
                Properties = new EquatableArray<string>([RenameToProperty, suffix.Name + "V" + statedText]),
            });
        }

        var (storedName, version) = EventNaming.DomainEventOf(type);
        var publishedName = EventNaming.ContractOf(type).Name;
        var namePinned = pinnedStored is not null || EventNaming.PinnedContractName(type) is not null;

        return new FoundEvent(
            Type: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            DisplayName: displayName,
            StoredName: isDomainEvent || pinnedStored is not null ? storedName : null,
            PublishedName: published is not null ? publishedName : null,
            Version: version,
            IsDomainEvent: isDomainEvent,
            IsContract: published is not null,
            SuggestedName: namePinned ? null : SuggestedNameFor(type, suffix.Name),
            IsPublished: ModuleBoundary.IsPublished(type),
            Location: location,
            Diagnostics: diagnostics.ToEquatableArray());
    }

    /// <summary>
    /// A name for the DDD00036 fix to pin: the conventional name with the class's nearest scope in front of
    /// the class name. <c>Ordering.Domain.Returns.OrderPlaced</c> gets <c>ordering.returns-order-placed</c>,
    /// and a class nested in <c>Refund</c> gets <c>ordering.refund-order-placed</c>.
    /// </summary>
    private static string? SuggestedNameFor(INamedTypeSymbol type, string className)
    {
        var scope = type.ContainingType?.Name
            ?? (type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.Name : null);

        if (scope is null)
        {
            return null;
        }

        return EventNameConvention.NameFor(scope + "_" + className, ModuleBoundary.ModuleOf(type.ContainingAssembly));
    }

    /// <summary>Where the attribute says <c>Version = n</c>, so the error points at the number that disagrees.</summary>
    private static LocationInfo? VersionArgumentLocation(AttributeData attribute, CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not AttributeSyntax syntax)
        {
            return null;
        }

        var argument = syntax.ArgumentList?.Arguments.FirstOrDefault(static argument => argument.NameEquals?.Name.Identifier.ValueText == "Version");
        return LocationInfo.From((SyntaxNode?)argument ?? syntax);
    }

    /// <summary>DDD00036 on every type that shares its name and version with another of its kind.</summary>
    private static void ReportTakenNames(SourceProductionContext production, IEnumerable<FoundEvent> types, Func<FoundEvent, string> nameOf)
    {
        foreach (var group in types.GroupBy(type => (Name: nameOf(type), type.Version)).Where(static group => group.Count() > 1))
        {
            var members = group.ToList();

            foreach (var type in members)
            {
                var other = members.First(candidate => !ReferenceEquals(candidate, type));
                var pinWith = type.IsDomainEvent ? "DomainEventName" : "IntegrationEvent";
                var fix = type.SuggestedName is { } suggestion ? $"[{pinWith}(\"{suggestion}\")]" : $"[{pinWith}(\"...\")]";

                var diagnostic = DiagnosticInfo.Create(
                    DiagnosticDescriptors.EventNameTaken,
                    type.Location,
                    type.DisplayName,
                    other.DisplayName,
                    group.Key.Name,
                    group.Key.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    fix);

                if (type.SuggestedName is not null)
                {
                    diagnostic = diagnostic with
                    {
                        Properties = new EquatableArray<string>([SuggestedNameProperty, type.SuggestedName, PinWithProperty, pinWith]),
                    };
                }

                diagnostic.Report(production);
            }
        }
    }

    private static string? Write(SourceProductionContext production, List<FoundEvent> types, string? module, string fallbackModule, string? assemblyName)
    {
        // Every name, with the types stored or published under it: a domain event and the contract it is
        // published as usually share one, and so does every version of one event.
        var names = types
            .SelectMany(static type => new[] { type.StoredName, type.PublishedName }
                .Where(static name => name is not null)
                .Distinct(StringComparer.Ordinal)
                .Select(name => (Name: name!, Type: type)))
            .GroupBy(static entry => entry.Name, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToList();

        var constants = new List<(string Identifier, string Name, List<FoundEvent> Types)>();
        var taken = new Dictionary<string, (string Name, List<FoundEvent> Types)>(StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var className = ClassNameFor(module, fallbackModule);

        foreach (var group in names)
        {
            // The class is named after the module, so the constant leaves it out: SalesEventNames.OrderPlaced,
            // not SalesEventNames.SalesOrderPlaced. Outside a module that is the DDD_Module the class is named after.
            if (EventNaming.ConstantNameFor(group.Key, module ?? fallbackModule) is not { } identifier)
            {
                continue;
            }

            var holders = group.Select(static entry => entry.Type).ToList();

            if (taken.TryGetValue(identifier, out var first))
            {
                // Neither name is more wrong than the other, so the events of both are reported. The first keeps
                // the constant only so that code already using it does not pile more errors onto this one.
                if (reported.Add(first.Name))
                {
                    ReportConstantTaken(production, first.Types, identifier, first.Name, group.Key, className);
                }

                ReportConstantTaken(production, holders, identifier, group.Key, first.Name, className);
                continue;
            }

            taken[identifier] = (group.Key, holders);
            constants.Add((identifier, group.Key, holders));
        }

        if (constants.Count == 0)
        {
            return null;
        }

        var writer = new CodeWriter().Header();
        writer.Line("namespace " + Identifiers.NamespaceFrom(assemblyName) + ";");
        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// The names this assembly's events are stored and published under, as the compiler worked them out: the");
        writer.Line("/// name <c>[DomainEventName]</c> or <c>[IntegrationEvent]</c> pins, otherwise the module and the class name in");
        writer.Line("/// kebab case. Write a name with these wherever it is written by hand: a topic binding, a test, or the");
        writer.Line("/// <c>[DomainEventName]</c> that keeps a name when its class is renamed.");
        writer.Line("/// </summary>");

        // Published when everything it names is: a contracts assembly's names are there for other modules
        // to bind to, a domain assembly's are the module's own business.
        if (module is not null && constants.All(static constant => constant.Types.All(static type => type.IsPublished)))
        {
            writer.Line("[global::DDDToolkit.Abstractions.Attributes.ModuleContract]");
        }

        using (writer.Block("public static class " + className))
        {
            for (var i = 0; i < constants.Count; i++)
            {
                var (identifier, name, holders) = constants[i];
                if (i > 0)
                {
                    writer.Line();
                }

                var described = string.Join(", ", holders
                    .OrderBy(static type => type.Version)
                    .ThenBy(static type => type.Type, StringComparer.Ordinal)
                    .Select(static type => $"<see cref=\"{type.Type}\"/> (version {type.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)})"));

                writer.Line($"/// <summary><c>{name}</c>: {described}.</summary>");
                writer.Line($"public const string {identifier} = {Literal(name)};");
            }
        }

        return writer.ToString();
    }

    /// <summary>DDD00037 on every event stored or published under <paramref name="name"/>.</summary>
    private static void ReportConstantTaken(SourceProductionContext production, List<FoundEvent> holders, string identifier, string name, string other, string className)
    {
        foreach (var type in holders)
        {
            DiagnosticInfo.Create(DiagnosticDescriptors.EventNameConstantTaken, type.Location, identifier, name, other, className).Report(production);
        }
    }

    /// <summary><c>{Module}EventNames</c>, after <c>[assembly: Module]</c>, otherwise <c>DDD_Module</c> or the assembly name.</summary>
    private static string ClassNameFor(string? module, string fallbackModule)
    {
        var prefix = EventNaming.Pascal(module ?? fallbackModule);
        if (prefix.Length == 0 || char.IsDigit(prefix[0]))
        {
            prefix = "_" + prefix;
        }

        return prefix + "EventNames";
    }

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

    private static string Literal(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <param name="Type">The fully qualified name, for the generated code.</param>
    /// <param name="DisplayName">The name as a reader writes it, for diagnostics.</param>
    /// <param name="StoredName">The name the outbox stores it under, for a domain event or a type with <c>[DomainEventName]</c>.</param>
    /// <param name="PublishedName">The name it is published under, for a type with <c>[IntegrationEvent]</c>.</param>
    /// <param name="SuggestedName">The name a DDD00036 fix pins, or null when the type already pins one.</param>
    /// <param name="IsPublished">Whether the type is part of its module's published contract.</param>
    private sealed record FoundEvent(
        string Type,
        string DisplayName,
        string? StoredName,
        string? PublishedName,
        int Version,
        bool IsDomainEvent,
        bool IsContract,
        string? SuggestedName,
        bool IsPublished,
        LocationInfo? Location,
        EquatableArray<DiagnosticInfo> Diagnostics);
}
