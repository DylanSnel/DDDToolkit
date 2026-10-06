using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// The roles one seat of the caller's tenant holds, where and for which period: another person's access, as the
/// tenant's administration reads it before it changes any.
/// </summary>
/// <remarks>
/// For whoever holds <see cref="RequiredKey"/> for the whole tenant: it shows a person's roles in every unit, so a
/// key held at one unit does not read it. A seat reads its own roles in its overview, <see cref="OverviewOfMine"/>,
/// which needs no key. A seat of another tenant, or of nobody, has no roles here: the storage keeps a read to the
/// caller's tenant, so the answer never says which.
/// </remarks>
/// <param name="Seat">The seat whose roles are read.</param>
public sealed record SeatGrants(SeatId Seat) : IQuery<IReadOnlyList<SeatGrant>>, ITenantsRequest
{
    /// <summary>The key the caller holds for the whole tenant.</summary>
    public const string RequiredKey = TenancyKeys.SeatsManage;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.ForTheWholeTenant(RequiredKey);
}

/// <summary>A role a seat holds at a unit, for a period: what <see cref="SeatGrants"/> answers, one per grant.</summary>
/// <param name="UnitId">Where the role is held: the unit, and every unit below it.</param>
/// <param name="UnitPath">That unit's path from the root, its name last.</param>
/// <param name="RoleId">The role.</param>
/// <param name="Role">Its name.</param>
/// <param name="StartsAt">The first moment the grant applies.</param>
/// <param name="EndsAt">The first moment it no longer applies, or <see langword="null"/> when it has no end.</param>
/// <param name="AppliesNow">Whether it applies now.</param>
public sealed record SeatGrant(OrganizationUnitId UnitId, string UnitPath, RoleId RoleId, string Role, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool AppliesNow);

/// <summary>Answers <see cref="SeatGrants"/> from Tenancy's seats, on storage of the read's own.</summary>
/// <param name="reads">Where Tenancy is read.</param>
public sealed class SeatGrantsHandler(ITenancyReads reads) : IQueryHandler<SeatGrants, IReadOnlyList<SeatGrant>>
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SeatGrant>> Handle(SeatGrants query, CancellationToken cancellationToken)
        => await reads.GrantsOfAsync(query.Seat, cancellationToken);
}
