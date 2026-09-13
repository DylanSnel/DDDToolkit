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

    /// <summary>Whether the root holds <paramref name="child"/> in a field or property of its own.</summary>
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

        return false;
    }

    private static bool IsAggregateRoot(INamedTypeSymbol type)
        => DefinitionFactory.HasAttribute(type, KnownTypes.AggregateRootAttribute);

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
