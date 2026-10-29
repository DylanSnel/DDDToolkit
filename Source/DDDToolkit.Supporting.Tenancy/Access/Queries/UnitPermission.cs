using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// A unit and a permission the caller holds there now: one answer of
/// <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.UnitsWhereIHold(IReadOnlyCollection{string})"/>, which answers such a pair for
/// every unit a permission reaches, the units it is held at and every unit below them.
/// </summary>
/// <param name="Unit">The unit.</param>
/// <param name="Permission">The permission held there.</param>
public readonly record struct UnitPermission<TUnitId>(TUnitId Unit, string Permission)
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>;
