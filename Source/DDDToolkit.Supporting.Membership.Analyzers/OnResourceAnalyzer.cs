using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DDDToolkit.Supporting.Membership.Analyzers;

/// <summary>
/// What is said of a request's <c>[OnResource(permission)]</c> marker, for each marker on its own.
/// <list type="bullet">
/// <item>
/// DDD00093: the permission the marker carries is declared on the resource whose id the marked member is,
/// <c>[On&lt;TResource&gt;]</c>. A permission declared on another resource only, or with no level on a resource at all, is
/// one the rules of the marked resource never give, so the request would refuse everyone.
/// </item>
/// <item>
/// DDD00096: <c>[OrOwn]</c> on the member of an <c>[OnResource]</c>, beside a permission the owner of the resource holds
/// already.
/// <code>
/// public sealed record RenameBoard([OnResource(BoardPermissions.Edit), OrOwn] BoardId Board, string Name) : IBoardsRequest;
/// </code>
/// The permission is asked on the board, and the board's owner holds every permission declared on it with
/// <c>Owner = true</c> by owning it, so the owner is let through by the permission already: <c>[OrOwn]</c> says nothing
/// more. It stays meant for an aggregate with an <c>[Owner]</c> and no members, and for a permission the owner does not hold.
/// </item>
/// </list>
/// What is declared where is read by the permission's value, from the <c>[Permissions]</c> classes of the project and of
/// the projects it references. A permission declared in none of them is not judged.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OnResourceAnalyzer : DiagnosticAnalyzer
{
    private const string OrOwn = "DDDToolkit.Access.OrOwnAttribute";
    private const string OnResource = "DDDToolkit.Supporting.Membership.Access.OnResourceAttribute";
    private const string Permissions = "DDDToolkit.Access.PermissionsAttribute";
    private const string Level = "DDDToolkit.Access.PermissionLevelAttribute";
    private const string On = "global::DDDToolkit.Supporting.Membership.Access.OnAttribute<TResource>";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [DiagnosticDescriptors.PermissionLevelMisfits, DiagnosticDescriptors.OwnAlternativeRedundant];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(OnResource) is not { } onResource
                || start.Compilation.GetTypeByMetadataName(Permissions) is not { } marker
                || start.Compilation.GetTypeByMetadataName(Level) is not { } level)
            {
                return;
            }

            // Where each permission is declared, by its value: read once, the project's own classes and those of the projects
            // it references.
            var declared = new Lazy<Declared>(() => DeclaredIn(start.Compilation, marker, level));
            start.RegisterSymbolAction(symbol => Check(symbol, onResource, declared.Value), SymbolKind.NamedType);
        });
    }

    private static void Check(SymbolAnalysisContext context, INamedTypeSymbol onResource, Declared declared)
    {
        var request = (INamedTypeSymbol)context.Symbol;
        IEnumerable<(string Name, ITypeSymbol Type, ImmutableArray<AttributeData> Attributes)> members = request.GetMembers().OfType<IPropertySymbol>()
            .Select(static property => (property.Name, property.Type, property.GetAttributes()));
        foreach (var constructor in request.InstanceConstructors)
        {
            members = members.Concat(constructor.Parameters.Select(static parameter => (parameter.Name, parameter.Type, parameter.GetAttributes())));
        }

        foreach (var (name, type, attributes) in members)
        {
            foreach (var marked in attributes.Where(each => SymbolEqualityComparer.Default.Equals(each.AttributeClass, onResource)))
            {
                if (marked.ConstructorArguments.Length != 1 || marked.ConstructorArguments[0].Value is not string permission)
                {
                    continue;
                }

                var location = marked.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? request.Locations.FirstOrDefault();
                if (Misfit(declared, permission, type) is { } why)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.PermissionLevelMisfits, location, request.Name,
                        "marks '" + name + "' [OnResource] with '" + permission + "', " + why));
                    continue;
                }

                var orOwn = attributes.FirstOrDefault(static each => each.AttributeClass?.OriginalDefinition.ToDisplayString() is { } shown && shown.StartsWith(OrOwn, StringComparison.Ordinal));
                if (orOwn is not null
                    && declared.On.TryGetValue(permission, out var on)
                    && on.FirstOrDefault(each => each.Owner && SymbolEqualityComparer.Default.Equals(each.Id, type)) is { Resource: { } resource })
                {
                    var at = orOwn.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? request.Locations.FirstOrDefault();
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.OwnAlternativeRedundant, at, name, resource, permission));
                }
            }
        }
    }

    /// <summary>
    /// Why <paramref name="permission"/> does not fit a marker on <paramref name="id"/>, as a message goes on; null where it
    /// fits, where the member is no id of a resource a permission is declared on, or where the permission is declared nowhere
    /// the project sees.
    /// </summary>
    private static string? Misfit(Declared declared, string permission, ITypeSymbol id)
    {
        if (!declared.Resources.TryGetValue(id.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), out var resource))
        {
            return null;
        }

        if (declared.On.TryGetValue(permission, out var on))
        {
            return on.Any(each => SymbolEqualityComparer.Default.Equals(each.Id, id))
                ? null
                : "which is declared " + DefinitionFactory.Listed(on.Select(static each => "[On<" + each.Resource + ">]").Distinct(StringComparer.Ordinal))
                  + ": the rules of the " + resource + " give only the permissions declared [On<" + resource + ">], so this one lets nobody through. "
                  + "Name a permission declared on the " + resource + ", or mark the id of the resource the permission is declared on";
        }

        return declared.Elsewhere.Contains(permission)
            ? "which is declared with no level on a resource: the rules of the " + resource + " give only the permissions declared [On<" + resource + ">], so this one lets nobody through. "
              + "Name a permission declared on the " + resource + ", or require this one where it is asked"
            : null;
    }

    /// <summary>
    /// The permissions the project sees declared: for each declared on a resource with <c>[On&lt;TResource&gt;]</c>, the id of
    /// each such resource, its name and whether its owner holds the permission; the names of the resources by their ids; and
    /// the permissions declared with a level of another kind.
    /// </summary>
    private static Declared DeclaredIn(Compilation compilation, INamedTypeSymbol marker, INamedTypeSymbol level)
    {
        var on = new Dictionary<string, List<(ITypeSymbol Id, string Resource, bool Owner)>>(StringComparer.Ordinal);
        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        var elsewhere = new HashSet<string>(StringComparer.Ordinal);
        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };
        assemblies.AddRange(compilation.SourceModule.ReferencedAssemblySymbols.Where(assembly =>
            assembly.Modules.Any(module => module.ReferencedAssemblySymbols.Contains(marker.ContainingAssembly, SymbolEqualityComparer.Default))));
        foreach (var assembly in assemblies)
        {
            foreach (var type in DefinitionFactory.TypesIn(assembly.GlobalNamespace, default))
            {
                if (!type.GetAttributes().Any(each => SymbolEqualityComparer.Default.Equals(each.AttributeClass, marker)))
                {
                    continue;
                }

                foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
                {
                    if (!field.IsConst || field.ConstantValue is not string permission)
                    {
                        continue;
                    }

                    foreach (var attribute in field.GetAttributes())
                    {
                        if (attribute.AttributeClass is { TypeArguments.Length: 1 } onLevel
                            && onLevel.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == On
                            && onLevel.TypeArguments[0] is INamedTypeSymbol resource
                            && OwnerDeclarations.AggregateIdOf(resource) is { } id)
                        {
                            if (!on.TryGetValue(permission, out var list))
                            {
                                on[permission] = list = [];
                            }

                            list.Add((id, resource.Name, !attribute.NamedArguments.Any(static named => named.Key == "Owner" && named.Value.Value is false)));
                            resources[id.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = resource.Name;
                        }
                        else if (DerivesFrom(attribute.AttributeClass, level))
                        {
                            elsewhere.Add(permission);
                        }
                    }
                }
            }
        }

        return new Declared(on, resources, elsewhere);
    }

    private static bool DerivesFrom(INamedTypeSymbol? type, INamedTypeSymbol level)
    {
        for (var current = type?.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, level))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What the project sees declared, read once per compilation.</summary>
    /// <param name="On">For each permission declared on a resource, each such resource's id and name, and whether its owner holds it.</param>
    /// <param name="Resources">The names of the resources a permission is declared on, by their ids, from <c>global::</c> on.</param>
    /// <param name="Elsewhere">The permissions declared with a level that is not on a resource.</param>
    private sealed record Declared(
        Dictionary<string, List<(ITypeSymbol Id, string Resource, bool Owner)>> On,
        Dictionary<string, string> Resources,
        HashSet<string> Elsewhere);
}
