using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// One role a seat holds at one unit, for a period: what <see cref="TenancyProjection.RightsOf{TTenantId, TSeatId, TUnitId, TRoleId}"/>
/// needs of a grant, whether it comes from a seat in memory or from a query that did not load the seat.
/// </summary>
/// <param name="UnitId">The unit of the placement the grant belongs to.</param>
/// <param name="RoleId">The role granted.</param>
/// <param name="StartsAt">The first moment the grant applies.</param>
/// <param name="EndsAt">The first moment it no longer applies, or <see langword="null"/>.</param>
public sealed record GrantFact<TUnitId, TRoleId>(TUnitId UnitId, TRoleId RoleId, DateTimeOffset StartsAt, DateTimeOffset? EndsAt)
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>;
