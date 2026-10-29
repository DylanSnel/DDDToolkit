using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// The property an aggregate root marks <c>[Owner]</c>, the fact a request about the owner's own is checked against:
/// checked where it is declared (DDD00087, DDD00088), and found for a request that marks the aggregate's id
/// <c>[Own]</c> or <c>[OrOwn]</c>, in the project and in those it references.
/// </summary>
/// <remarks>
/// An aggregate root is recognised by its attributes, as everywhere (<see cref="EntityDeclarations"/>): in a referenced
/// assembly its id is read off the same attribute, so a domain in a project of its own is seen from the requests above it.
/// </remarks>
internal static class OwnerDeclarations
{
    private static readonly ConditionalWeakTable<Compilation, Dictionary<string, List<OwnedAggregate>>> InCompilation = new();

    private static readonly ConditionalWeakTable<IAssemblySymbol, List<OwnedAggregate>> InAssembly = new();

    /// <summary>
    /// The aggregate roots whose id is <paramref name="id"/> that the project sees, its own and those of the projects it
    /// references, each with the property it marks <c>[Owner]</c>, or none. Read once per compilation, and a referenced
    /// assembly once.
    /// </summary>
    public static List<OwnedAggregate> AggregatesWithId(ITypeSymbol id, Compilation compilation)
    {
        var all = InCompilation.GetValue(compilation, static compilation =>
        {
            var found = new Dictionary<string, List<OwnedAggregate>>(StringComparer.Ordinal);
            var owner = compilation.GetTypeByMetadataName(KnownTypes.OwnerAttribute);
            var aggregates = AggregatesIn(compilation.Assembly).AsEnumerable();
            if (owner is not null)
            {
                // Only an assembly that references the attribute's own can mark an owner, or declare an aggregate root
                // with the toolkit's attributes; the framework's are not looked into.
                foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
                {
                    if (assembly.Modules.Any(module => module.ReferencedAssemblySymbols.Contains(owner.ContainingAssembly, SymbolEqualityComparer.Default)))
                    {
                        aggregates = aggregates.Concat(InAssembly.GetValue(assembly, static each => AggregatesIn(each)));
                    }
                }
            }

            foreach (var aggregate in aggregates)
            {
                var key = aggregate.Id.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!found.TryGetValue(key, out var list))
                {
                    found.Add(key, list = []);
                }

                list.Add(aggregate);
            }

            return found;
        });

        return all.TryGetValue(id.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), out var withId) ? withId : [];
    }

    /// <summary>
    /// The properties of <paramref name="type"/> marked <c>[Owner]</c>, its own and those of the classes it derives from
    /// that the compilation shows, each once, the nearest first. A class declared with a template derives from a parent
    /// the generator writes the base of, so that parent is asked as well.
    /// </summary>
    public static List<IPropertySymbol> OwnersOf(INamedTypeSymbol type)
    {
        var found = new List<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<INamedTypeSymbol>();
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            chain.Add(current);
        }

        if (EntityDeclarations.TemplateParentOf(type) is { } parent && !chain.Any(each => SymbolEqualityComparer.Default.Equals(each.OriginalDefinition, parent)))
        {
            chain.Add(parent);
        }

        foreach (var current in chain)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.IsIndexer && seen.Add(property.Name) && DefinitionFactory.HasAttribute(property, KnownTypes.OwnerAttribute))
                {
                    found.Add(property);
                }
            }
        }

        return found;
    }

    /// <summary>The type of an owner, without the <c>?</c> of a nullable one.</summary>
    public static ITypeSymbol Unwrapped(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    /// <summary>
    /// Whether no handle could ever say the caller's own of <paramref name="type"/>, whatever the project sees: the caller's
    /// own is a value that equals itself, a struct that is <c>IEquatable</c> of itself, and a class, a string, an interface or
    /// an enum is none. A struct declared in parts in the project may get its equality from a generator this one does not
    /// see, so only one it can read whole is told.
    /// </summary>
    public static bool CannotHaveOwn(ITypeSymbol type)
    {
        if (type.TypeKind is TypeKind.Error or TypeKind.TypeParameter)
        {
            return false;
        }

        if (!type.IsValueType || type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        var equatable = type.AllInterfaces.Any(each => each is { Name: "IEquatable", TypeArguments.Length: 1 }
            && each.ContainingNamespace?.ToDisplayString() == "System"
            && SymbolEqualityComparer.Default.Equals(each.TypeArguments[0], type));
        var inParts = type.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
        return !equatable && !inParts;
    }

    /// <summary>
    /// DDD00087 and DDD00088 for a property marked <c>[Owner]</c> where it is declared: an instance property of an aggregate
    /// root, the one it marks, of a type a caller could have an own of. <see langword="null"/> where it is all of these.
    /// Whether something says the caller's own of its type is the requests' project's to tell, and is not asked here.
    /// </summary>
    public static DiagnosticInfo? Check(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IPropertySymbol { ContainingType: { } type } property)
        {
            return null;
        }

        var location = LocationInfo.From(((PropertyDeclarationSyntax)context.TargetNode).Identifier);
        var at = type.Name + "." + property.Name;
        if (property.IsStatic)
        {
            return DiagnosticInfo.Create(DiagnosticDescriptors.OwnerOutOfPlace, location, at,
                "is static, and marked [Owner]: the owner is a column of each row of the aggregate, so mark the instance property that names it");
        }

        if (!EntityDeclarations.IsAggregateRoot(type) && AggregateIdOf(type) is null)
        {
            return DiagnosticInfo.Create(DiagnosticDescriptors.OwnerOutOfPlace, location, at, EntityDeclarations.IsEntity(type)
                ? "is marked [Owner], and '" + type.Name + "' is an entity inside an aggregate: a request names its root by the root's id, and the check reads the root's row, so mark the property of the root that names the owner"
                : "is marked [Owner], and '" + type.Name + "' is no aggregate root: a request names an aggregate by its id, and the check reads the row of that aggregate root, so mark the owner on an [AggregateRoot<TId>]");
        }

        // An aggregate has one owner. The first of them in the order they are written is the one, and the others are told.
        var owners = OwnersOf(type);
        if (owners.Count > 1 && !SymbolEqualityComparer.Default.Equals(First(owners), property))
        {
            return DiagnosticInfo.Create(DiagnosticDescriptors.OwnerOutOfPlace, location, at,
                "is marked [Owner] beside " + DefinitionFactory.Listed(owners.Where(each => !SymbolEqualityComparer.Default.Equals(each, property)).Select(static each => "'" + each.Name + "'"))
                + ": an aggregate has one owner, so keep the one that names it; several owners are members, a resource with members, which Membership keeps");
        }

        var ownerType = Unwrapped(property.Type);
        if (CannotHaveOwn(ownerType))
        {
            var written = ownerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return DiagnosticInfo.Create(DiagnosticDescriptors.OwnerOfATypeWithoutOwn, location, at,
                "is marked [Owner], and is a '" + written + "': the owner is compared with the caller's own of its type, a struct that is IEquatable of itself, such as a Guid or an id declared with [EntityId], "
                + "so nothing can say the caller's own " + written + "; mark the property that names the owner's id");
        }

        return null;
    }

    /// <summary>
    /// DDD00097 for a property marked <c>[At]</c> where it is declared: an instance property of an aggregate root, the
    /// one it marks, that is never empty. <see langword="null"/> where it is all of these. The marker is the package's
    /// that declares it, named by <paramref name="marker"/>, so the toolkit's own generators name no package.
    /// </summary>
    /// <param name="context">The marked property.</param>
    /// <param name="marker">The marker's metadata name.</param>
    public static DiagnosticInfo? CheckPlace(GeneratorAttributeSyntaxContext context, string marker)
    {
        if (context.TargetSymbol is not IPropertySymbol { ContainingType: { } type } property)
        {
            return null;
        }

        var location = LocationInfo.From(((PropertyDeclarationSyntax)context.TargetNode).Identifier);
        var at = type.Name + "." + property.Name;
        if (property.IsStatic)
        {
            return DiagnosticInfo.Create(DiagnosticDescriptors.PlaceMisdeclared, location, at,
                "is static, and marked [At]: where an aggregate hangs is a column of each row of it, so mark the instance property that says it");
        }

        if (!EntityDeclarations.IsAggregateRoot(type) && AggregateIdOf(type) is null)
        {
            return DiagnosticInfo.Create(DiagnosticDescriptors.PlaceMisdeclared, location, at,
                "is marked [At], and '" + type.Name + "' is no aggregate root: what is reached from above is an aggregate, by the place its root hangs at, so mark the property on an [AggregateRoot<TId>]");
        }

        var marked = MarkedIn(type, marker);
        if (marked.Count > 1 && !SymbolEqualityComparer.Default.Equals(First(marked), property))
        {
            return DiagnosticInfo.Create(DiagnosticDescriptors.PlaceMisdeclared, location, at,
                "is marked [At] beside " + DefinitionFactory.Listed(marked.Where(each => !SymbolEqualityComparer.Default.Equals(each, property)).Select(static each => "'" + each.Name + "'"))
                + ": an aggregate hangs at one place, so keep the one that says it");
        }

        if (property.Type.NullableAnnotation == NullableAnnotation.Annotated || property.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
        {
            return DiagnosticInfo.Create(DiagnosticDescriptors.PlaceMisdeclared, location, at,
                "is marked [At], and may be empty: an aggregate that hangs nowhere is reached from above by nobody, which is what [At] is for. Mark the property of where it always hangs, of a type that is never empty");
        }

        return null;
    }

    /// <summary>The properties of <paramref name="type"/> and the classes it derives from marked with <paramref name="attribute"/>, each once.</summary>
    private static List<IPropertySymbol> MarkedIn(INamedTypeSymbol type, string attribute)
    {
        var found = new List<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (!property.IsIndexer && seen.Add(property.Name) && DefinitionFactory.HasAttribute(property, attribute))
                {
                    found.Add(property);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The id of <paramref name="type"/> where it is an aggregate root: declared one with an attribute, in any of the ways
    /// <see cref="EntityDeclarations"/> knows, or deriving from the toolkit's <c>AggregateRoot&lt;TId&gt;</c> by hand.
    /// <see langword="null"/> for any other type, a child entity among them.
    /// </summary>
    public static ITypeSymbol? AggregateIdOf(INamedTypeSymbol type)
    {
        if (EntityDeclarations.IsAggregateRoot(type))
        {
            return EntityDeclarations.IdArgumentOf(type);
        }

        if (EntityDeclarations.IsEntityOrAggregateRoot(type))
        {
            return null;
        }

        for (var current = type.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (current is { Name: "AggregateRoot", TypeArguments.Length: 1 } && current.ContainingNamespace?.ToDisplayString() == "DDDToolkit.BaseTypes")
            {
                return current.TypeArguments[0] is { TypeKind: not TypeKind.Error } id ? id : null;
            }
        }

        return null;
    }

    /// <summary>The first of <paramref name="owners"/> as they are written: by the file, then by the place in it.</summary>
    private static IPropertySymbol First(List<IPropertySymbol> owners)
        => owners
            .OrderBy(static each => each.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static each => each.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0)
            .First();

    /// <summary>Every aggregate root <paramref name="assembly"/> declares, with its id and the property it marks <c>[Owner]</c>.</summary>
    private static List<OwnedAggregate> AggregatesIn(IAssemblySymbol assembly)
    {
        var found = new List<OwnedAggregate>();
        foreach (var type in DefinitionFactory.TypesIn(assembly.GlobalNamespace, CancellationToken.None))
        {
            if (type.TypeKind != TypeKind.Class || EntityDeclarations.IsBase(type) || AggregateIdOf(type) is not { } id)
            {
                continue;
            }

            var owners = OwnersOf(type);
            found.Add(new OwnedAggregate(type, id, owners.Count == 1 ? owners[0] : null));
        }

        return found;
    }
}

/// <summary>An aggregate root the project sees, with its id and the property it marks <c>[Owner]</c>, or none.</summary>
/// <param name="Aggregate">The aggregate root.</param>
/// <param name="Id">Its id.</param>
/// <param name="Owner">The one property it marks <c>[Owner]</c>; <see langword="null"/> where it marks none, or more than one.</param>
internal sealed record OwnedAggregate(INamedTypeSymbol Aggregate, ITypeSymbol Id, IPropertySymbol? Owner);
