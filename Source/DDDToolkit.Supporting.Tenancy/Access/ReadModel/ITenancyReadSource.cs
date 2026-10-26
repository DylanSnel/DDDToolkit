using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Where the access questions read their rows. The storage supplies it over whichever context the question
/// is composed into, so a module asks Tenancy inside its own query, and the question becomes a subquery of
/// it rather than a second round trip.
/// <para>
/// A source may already keep every set to the current tenant; the questions keep to the caller's tenant
/// themselves as well.
/// </para>
/// </summary>
public interface ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The keys seats hold at units.</summary>
    IQueryable<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>> SeatRights { get; }

    /// <summary>The closure of every organization's tree.</summary>
    IQueryable<OrganizationUnitPath<TTenantId, TUnitId>> UnitPaths { get; }

    /// <summary>The units.</summary>
    IQueryable<OrganizationUnitRow<TTenantId, TUnitId>> Units { get; }

    /// <summary>The roles.</summary>
    IQueryable<RoleRow<TTenantId, TRoleId>> Roles { get; }

    /// <summary>Where seats are placed.</summary>
    IQueryable<PlacementRow<TSeatId, TUnitId>> Placements { get; }

    /// <summary>The seats.</summary>
    IQueryable<SeatRow<TTenantId, TSeatId>> Seats { get; }

    /// <summary>
    /// The seats that hold <paramref name="key"/> at <paramref name="unit"/> now, as the storage itself answers
    /// the calling seat, or <see langword="null"/> when it has no answer of its own and the question is worked
    /// out over the rows above. A storage that shows a seat only its own <see cref="SeatRights"/> has one: over
    /// those rows the question would find nobody but the seat itself, whoever asks. Its answer keeps to what
    /// <see cref="ITenancyQuestions{TTenantId, TSeatId, TUnitId, TRoleId}.SeatsHoldingAt"/> says a seat learns,
    /// and is a query of the same context, so it still composes into one statement.
    /// </summary>
    /// <param name="key">A live key of the catalogue.</param>
    /// <param name="unit">The unit.</param>
    IQueryable<TSeatId>? SeatsHoldingAt(string key, TUnitId unit) => null;
}
