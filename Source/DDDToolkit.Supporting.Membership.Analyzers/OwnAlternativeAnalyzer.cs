using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DDDToolkit.Supporting.Membership.Analyzers;

/// <summary>
/// DDD00096: <c>[OrOwn]</c> beside a key the owner of a resource with members holds already.
/// <code>
/// [RequiresKey(BoardPermissions.Edit)]                       // declared [On&lt;Board&gt;], Owner = true by default
/// public sealed record RenameBoard([OnResource, OrOwn] BoardId Board, string Name) : IBoardsRequest;
/// </code>
/// The key is asked on the board, and the board's owner holds every permission declared on it with <c>Owner = true</c>
/// by owning it, so the owner is let through by the key already: <c>[OrOwn]</c> says nothing more. It stays meant for
/// an aggregate with an <c>[Owner]</c> and no members, and for a key the owner does not hold.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OwnAlternativeAnalyzer : DiagnosticAnalyzer
{
    private const string RequiresKey = "DDDToolkit.Access.RequiresKeyAttribute";
    private const string OrOwn = "DDDToolkit.Access.OrOwnAttribute";
    private const string OnResource = "DDDToolkit.Supporting.Membership.Access.OnResourceAttribute";
    private const string Permissions = "DDDToolkit.Access.PermissionsAttribute";
    private const string On = "global::DDDToolkit.Supporting.Membership.Access.OnAttribute<TResource>";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [DiagnosticDescriptors.OwnAlternativeRedundant];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(OnResource) is null
                || start.Compilation.GetTypeByMetadataName(OrOwn) is null
                || start.Compilation.GetTypeByMetadataName(Permissions) is not { } marker)
            {
                return;
            }

            // Which keys the owner of which resource holds, by the resource's id: read once, the project's own classes
            // and those of the projects it references.
            var owners = new Lazy<Dictionary<string, List<(ITypeSymbol Id, string Resource)>>>(() => OwnerKeys(start.Compilation, marker));
            start.RegisterSymbolAction(symbol => Check(symbol, owners.Value), SymbolKind.NamedType);
        });
    }

    private static void Check(SymbolAnalysisContext context, Dictionary<string, List<(ITypeSymbol Id, string Resource)>> owners)
    {
        var request = (INamedTypeSymbol)context.Symbol;
        var key = request.GetAttributes()
            .Where(static each => each.AttributeClass?.ToDisplayString() == RequiresKey && each.ConstructorArguments.Length == 1)
            .Select(static each => each.ConstructorArguments[0].Value as string)
            .FirstOrDefault();
        if (key is null || !owners.TryGetValue(key, out var heldBy))
        {
            return;
        }

        IEnumerable<(string Name, ITypeSymbol Type, ImmutableArray<AttributeData> Attributes)> members = request.GetMembers().OfType<IPropertySymbol>()
            .Select(static property => (property.Name, property.Type, property.GetAttributes()));
        foreach (var constructor in request.InstanceConstructors)
        {
            members = members.Concat(constructor.Parameters.Select(static parameter => (parameter.Name, parameter.Type, parameter.GetAttributes())));
        }

        foreach (var (name, type, attributes) in members)
        {
            var orOwn = attributes.FirstOrDefault(static each => each.AttributeClass?.ToDisplayString() == OrOwn);
            if (orOwn is null || !attributes.Any(static each => each.AttributeClass?.ToDisplayString() == OnResource))
            {
                continue;
            }

            if (heldBy.FirstOrDefault(each => SymbolEqualityComparer.Default.Equals(each.Id, type)) is { Resource: { } resource })
            {
                var location = orOwn.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? request.Locations.FirstOrDefault();
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.OwnAlternativeRedundant, location, name, resource, key));
                return;
            }
        }
    }

    /// <summary>For each key declared on a resource with <c>Owner</c>, the ids of the resources whose owner holds it.</summary>
    private static Dictionary<string, List<(ITypeSymbol Id, string Resource)>> OwnerKeys(Compilation compilation, INamedTypeSymbol marker)
    {
        var found = new Dictionary<string, List<(ITypeSymbol, string)>>(StringComparer.Ordinal);
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
                    if (!field.IsConst || field.ConstantValue is not string key)
                    {
                        continue;
                    }

                    foreach (var attribute in field.GetAttributes())
                    {
                        if (attribute.AttributeClass is { TypeArguments.Length: 1 } level
                            && level.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == On
                            && !attribute.NamedArguments.Any(static named => named.Key == "Owner" && named.Value.Value is false)
                            && level.TypeArguments[0] is INamedTypeSymbol resource
                            && OwnerDeclarations.AggregateIdOf(resource) is { } id)
                        {
                            if (!found.TryGetValue(key, out var list))
                            {
                                found[key] = list = [];
                            }

                            list.Add((id, resource.Name));
                        }
                    }
                }
            }
        }

        return found;
    }
}
