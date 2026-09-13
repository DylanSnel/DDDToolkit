using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// The aggregate boundary rule behind <see cref="DiagnosticDescriptors.ReferenceOtherAggregatesById"/>
/// (DDD00021): one aggregate refers to another by its id, never by a reference to its root.
/// <para>
/// The check is deliberately narrow. It looks at <em>stored state</em> only, meaning the instance fields
/// and properties of a type carrying <c>[Entity&lt;T&gt;]</c> or <c>[AggregateRoot&lt;T&gt;]</c>, because
/// those are exactly what Entity Framework turns into a navigation and what a load therefore drags along.
/// Method parameters and return types are left alone: passing another root into a domain method is how
/// two aggregates are meant to cooperate, and nothing is stored by doing it.
/// </para>
/// <para>
/// Like the name check behind DDD00007, this reads members of a type the generator did not start from.
/// That is a deliberate trade: the answer can go stale in an IDE session until the declaring file is
/// touched again, and it is recomputed on every real build.
/// </para>
/// </summary>
internal static class AggregateBoundary
{
    /// <summary>How far to dig through arrays, nullables and collections before giving up.</summary>
    private const int MaxDepth = 5;

    /// <summary>
    /// Adds a diagnostic for every field or property of <paramref name="entity"/> that holds another
    /// aggregate root.
    /// </summary>
    /// <param name="entity">The type carrying <c>[Entity&lt;T&gt;]</c> or <c>[AggregateRoot&lt;T&gt;]</c>.</param>
    /// <param name="isAggregateRoot">Which of the two it carries. Only a child entity may navigate back to its owner.</param>
    public static void Check(
        INamedTypeSymbol entity,
        bool isAggregateRoot,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var member in entity.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var type = StoredTypeOf(member);
            if (type is null)
            {
                continue;
            }

            var reference = Find(type, viaCollection: false, depth: 0);
            if (reference is null)
            {
                continue;
            }

            var root = reference.Value.Root;

            // The one allowed reference: a child entity pointing back at the root that owns it. "Owns"
            // is not taken on trust - the root has to hold this child, through a collection or a single
            // property - so a child pointing at an unrelated root is still reported.
            if (!isAggregateRoot
                && !reference.Value.ViaCollection
                && Mentions(root, entity, cancellationToken))
            {
                continue;
            }

            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.ReferenceOtherAggregatesById,
                LocationInfo.From(member),
                entity.Name,
                member.Name,
                root.Name,
                IdNameOf(root)));
        }
    }

    /// <summary>
    /// The type a member stores, or null when the member stores nothing. Static members are state of the
    /// type rather than of one aggregate, and a compiler-generated field (the backing field of an auto
    /// property) would report the property a second time under a mangled name.
    /// </summary>
    private static ITypeSymbol? StoredTypeOf(ISymbol member)
    {
        if (member.IsStatic || member.IsImplicitlyDeclared)
        {
            return null;
        }

        return member switch
        {
            IPropertySymbol { IsIndexer: false, ExplicitInterfaceImplementations.IsEmpty: true } property => property.Type,
            IFieldSymbol { IsConst: false, AssociatedSymbol: null } field => field.Type,
            _ => null,
        };
    }

    /// <summary>An aggregate root reached from a member type, and whether it was reached through a collection.</summary>
    private readonly struct Reference
    {
        public Reference(INamedTypeSymbol root, bool viaCollection)
        {
            Root = root;
            ViaCollection = viaCollection;
        }

        public INamedTypeSymbol Root { get; }

        /// <summary>True when the member holds many of them. A back-navigation is always single valued.</summary>
        public bool ViaCollection { get; }
    }

    /// <summary>
    /// The aggregate root a member type holds, looking through arrays, <c>Nullable&lt;T&gt;</c> and
    /// anything enumerable, so <c>Customer</c>, <c>Customer?</c>, <c>Customer[]</c>,
    /// <c>IReadOnlyList&lt;Customer&gt;</c> and <c>Dictionary&lt;OrderId, Customer&gt;</c> all count.
    /// </summary>
    private static Reference? Find(ITypeSymbol type, bool viaCollection, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        if (type is IArrayTypeSymbol array)
        {
            return Find(array.ElementType, viaCollection: true, depth + 1);
        }

        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && named.TypeArguments.Length == 1)
        {
            return Find(named.TypeArguments[0], viaCollection, depth + 1);
        }

        if (IsAggregateRoot(named))
        {
            return new Reference(named, viaCollection);
        }

        if (named.TypeArguments.Length > 0 && IsEnumerable(named))
        {
            foreach (var argument in named.TypeArguments)
            {
                var found = Find(argument, viaCollection: true, depth + 1);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>Whether the root holds <paramref name="child"/> in a field or property of its own.</summary>
    private static bool Mentions(INamedTypeSymbol root, INamedTypeSymbol child, CancellationToken cancellationToken)
    {
        foreach (var member in root.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var type = StoredTypeOf(member);
            if (type is not null && Mentions(type, child, depth: 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Mentions(ITypeSymbol type, INamedTypeSymbol target, int depth)
    {
        if (depth > MaxDepth)
        {
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            return Mentions(array.ElementType, target, depth + 1);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(named, target))
        {
            return true;
        }

        if (named.TypeArguments.Length > 0
            && (IsEnumerable(named) || named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T))
        {
            return named.TypeArguments.Any(argument => Mentions(argument, target, depth + 1));
        }

        return false;
    }

    private static bool IsAggregateRoot(INamedTypeSymbol type)
        => DefinitionFactory.HasAttribute(type, KnownTypes.AggregateRootAttribute);

    private static bool IsEnumerable(INamedTypeSymbol type)
        => type.SpecialType == SpecialType.System_Collections_IEnumerable
           || type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
           || type.AllInterfaces.Any(@interface => @interface.SpecialType == SpecialType.System_Collections_IEnumerable);

    /// <summary>
    /// The name of the id to hold instead. <c>[AggregateRoot&lt;CustomerId&gt;]</c> names it;
    /// <c>[AggregateRoot&lt;Guid&gt;]</c> names a raw value, and the id generated from it is called
    /// <c>CustomerId</c> all the same.
    /// </summary>
    private static string IdNameOf(INamedTypeSymbol root)
    {
        foreach (var attribute in root.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "AggregateRootAttribute", TypeArguments.Length: 1 } attributeClass
                || attributeClass.ContainingNamespace.ToDisplayString() != KnownTypes.AttributesNamespace)
            {
                continue;
            }

            var argument = attributeClass.TypeArguments[0];
            return DefinitionFactory.IsEntityId(argument) ? argument.Name : Identifiers.IdNameFor(root.Name);
        }

        return Identifiers.IdNameFor(root.Name);
    }
}
