using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// The aggregate boundary rule behind <see cref="DiagnosticDescriptors.ReferenceOtherAggregatesById"/>
/// (DDD00021): one aggregate refers to another by its id, never by a reference to its root.
/// <para>
/// The check is deliberately narrow. It looks at <em>stored state</em> only (see
/// <see cref="StoredState"/>), because that is what Entity Framework turns into a navigation.
/// </para>
/// <para>
/// Like the name check behind DDD00007, this reads members of a type the generator did not start from.
/// That is a deliberate trade: the answer can go stale in an IDE session until the declaring file is
/// touched again, and it is recomputed on every real build.
/// </para>
/// </summary>
internal static class AggregateBoundary
{
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

            var type = StoredState.StoredTypeOf(member);
            if (type is null)
            {
                continue;
            }

            var reference = StoredState.Find(type, IsAggregateRoot);
            if (reference is null)
            {
                continue;
            }

            var root = reference.Value.Type;

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
    /// Whether the root holds <paramref name="child"/> in a field or property of its own, or, for a root
    /// declared with a package's template, in one of its parent's. The parent holds the application's class
    /// as a type parameter, <c>IReadOnlyList&lt;TUnit&gt;</c> where <c>TUnit : OrganizationUnitEntity&lt;TUnitId&gt;</c>,
    /// and a child declared with that parent's template is what fills it. The compilation cannot show the
    /// parent as the root's base class, because a generator writes it.
    /// </summary>
    private static bool Mentions(INamedTypeSymbol root, INamedTypeSymbol child, CancellationToken cancellationToken)
    {
        foreach (var member in root.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var type = StoredState.StoredTypeOf(member);
            if (type is not null && StoredState.Find(type, candidate => SymbolEqualityComparer.Default.Equals(candidate, child)) is not null)
            {
                return true;
            }
        }

        if (EntityDeclarations.TemplateParentOf(root) is not { } parent)
        {
            return false;
        }

        var childParent = EntityDeclarations.TemplateParentOf(child);
        foreach (var member in parent.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (StoredState.StoredTypeOf(member) is { } type && Holds(type, child, childParent, depth: 0))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a parent's stored type holds the child: the child itself, or a type parameter the child's own parent satisfies.</summary>
    private static bool Holds(ITypeSymbol type, INamedTypeSymbol child, INamedTypeSymbol? childParent, int depth) => depth <= 5 && type switch
    {
        ITypeParameterSymbol parameter => childParent is not null
            && parameter.ConstraintTypes.Any(constraint => SymbolEqualityComparer.Default.Equals(constraint.OriginalDefinition, childParent)),
        IArrayTypeSymbol array => Holds(array.ElementType, child, childParent, depth + 1),
        INamedTypeSymbol named => SymbolEqualityComparer.Default.Equals(named, child)
            || named.TypeArguments.Any(argument => Holds(argument, child, childParent, depth + 1)),
        _ => false,
    };

    private static bool IsAggregateRoot(INamedTypeSymbol type)
        => EntityDeclarations.IsAggregateRoot(type);

    /// <summary>
    /// The name of the id to hold instead. <c>[AggregateRoot&lt;CustomerId&gt;]</c> names it;
    /// <c>[AggregateRoot&lt;Guid&gt;]</c> names a raw value, and the id generated from it is called
    /// <c>CustomerId</c> all the same.
    /// </summary>
    private static string IdNameOf(INamedTypeSymbol root)
        => EntityDeclarations.IdArgumentOf(root) is { } argument && DefinitionFactory.IsEntityId(argument)
            ? argument.Name
            : Identifiers.IdNameFor(root.Name);
}
