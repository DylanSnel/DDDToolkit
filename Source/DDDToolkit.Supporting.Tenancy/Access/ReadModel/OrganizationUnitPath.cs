using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// One pair of a unit and a unit at or below it, and how many levels apart they are: the closure of the
/// organization's tree, so "every unit below this one" is a join rather than a walk. Every unit is paired
/// with itself at distance 0. Written by <see cref="TenancyProjection.ClosureOf{TTenantId, TUnit, TUnitId}"/>.
/// </summary>
public sealed class OrganizationUnitPath<TTenantId, TUnitId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
{
    /// <summary>The organization's tenant.</summary>
    public TTenantId TenantId { get; set; }

    /// <summary>The unit higher up, or the unit itself.</summary>
    public TUnitId AncestorId { get; set; }

    /// <summary>The unit at or below <see cref="AncestorId"/>.</summary>
    public TUnitId DescendantId { get; set; }

    /// <summary>How many levels apart they are: 0 for a unit and itself, 1 for a unit and its parent.</summary>
    public int Distance { get; set; }
}
