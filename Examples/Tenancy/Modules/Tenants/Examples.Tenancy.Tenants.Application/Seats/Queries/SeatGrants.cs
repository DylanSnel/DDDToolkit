using DDDToolkit.Supporting.Tenancy.Catalogue;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Seats.Queries;

/// <summary>
/// The roles one seat of the caller's tenant holds, where and for which period, at every unit or at one: another
/// person's access, as the tenant's administration reads it before it changes any.
/// </summary>
/// <remarks>
/// Who may read another person's access is this application's choice, and this request is where it makes it: the
/// package's overview of a seat asks no key, as no question of its directory does. This application lets whoever
/// holds <see cref="RequiredKey"/> for the whole tenant read the roles a person holds in every unit, and whoever holds
/// it at a unit, or above it, read the roles held at that unit, <see cref="At"/>. Which of the two the request asks
/// depends on its value, so it answers <see cref="IRequireAccess.RequiredAccess"/> itself rather than with an
/// attribute: no attribute says one case or the other. On Postgres Tenancy's policies ask again, and give a seat
/// another seat's grants only at the units where it manages grants, seats or units, and all of them to a seat that
/// manages roles for the whole tenant; where no policy applies, on another database, this requirement is the only
/// rule. A seat reads its own roles in its overview, <see cref="OverviewOfMine"/>, which needs no key.
/// </remarks>
/// <param name="Seat">The seat whose roles are read.</param>
/// <param name="At">
/// A unit, to read the roles held there, for a caller who holds the key there; <see langword="null"/> for every unit,
/// which takes the key for the whole tenant.
/// </param>
public sealed record SeatGrants(SeatId Seat, OrganizationUnitId? At = null) : IQuery<IReadOnlyList<SeatGrant>>, ITenantsRequest
{
    /// <summary>The key the caller holds, for the whole tenant or at the unit asked about: this application's choice.</summary>
    public const string RequiredKey = TenancyKeys.SeatsManage;

    /// <summary>The key at the unit asked about, there or above it; for the whole tenant where every unit is.</summary>
    AccessRequirement IRequireAccess.RequiredAccess => At is { } unit
        ? TenancyAccess.AtUnit(RequiredKey, unit)
        : TenancyAccess.ForTheWholeTenant(RequiredKey);
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

/// <summary>
/// Answers <see cref="SeatGrants"/> from the Tenancy package's directory: the overview of the seat asked about, the
/// record a seat's own overview is made from, selected into one row per grant, by the unit's path and then the role's
/// name.
/// </summary>
/// <remarks>
/// The directory reads as the caller, so on Postgres the policies decide which of the seat's grants come back: all
/// of them to the callers this request admits, and to a caller that reached the handler past it only those it may
/// read.
/// </remarks>
/// <param name="reads">Where Tenancy is read: the directory, in a scope of this query's own.</param>
public sealed class SeatGrantsHandler(ITenancyReads reads) : IQueryHandler<SeatGrants, IReadOnlyList<SeatGrant>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>tenancy.seat-not-found</c> for a seat of another tenant, or of nobody, which the answer does not tell apart.
    /// </exception>
    public async ValueTask<IReadOnlyList<SeatGrant>> Handle(SeatGrants query, CancellationToken cancellationToken)
    {
        var overview = await reads.AskDirectoryAsync(directory => directory.SeatOverviewAsync(query.Seat, cancellationToken));

        // The placements and grants are the seat's own; the path and the role's name are what the package read beside
        // them, and whether a grant applies is asked at the moment the overview holds for. A role a filter on the role
        // class would hide has no name, and its grant is shown all the same. Asked at a unit, the roles held there.
        return
        [
            .. overview.Seat.Placements
                .Where(placement => query.At is not { } unit || placement.UnitId == unit)
                .SelectMany(placement => placement.Grants.Select(grant => new SeatGrant(
                    placement.UnitId,
                    overview.UnitOf(placement.UnitId).Path,
                    grant.RoleId,
                    overview.RoleOf(grant.RoleId)?.Name ?? string.Empty,
                    grant.StartsAt,
                    grant.EndsAt,
                    grant.AppliesAt(overview.AsOf))))
                .OrderBy(grant => grant.UnitPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(grant => grant.Role, StringComparer.OrdinalIgnoreCase),
        ];
    }
}
