using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// Where Tenancy's queries read their rows. The storage supplies it over whichever context a query is composed
/// into, so a module asks Tenancy inside its own query, and Tenancy's query becomes a subquery of it rather than a
/// second round trip. Over an Entity Framework context, <c>db.Tenancy()</c> uses the package's own source. A storage
/// that is not Entity Framework implements this interface, and asks with the access manager's long form,
/// <see cref="TenancyAccessManager{TTenantId, TSeatId, TUnitId, TRoleId}.In"/>:
/// <code>
/// var queries = access.In(readSource, executor);
/// var editable = await executor.ListAsync(queries.UnitsWhereIHold("projects.edit"), cancellationToken);
/// </code>
/// <para>
/// A source may already keep every set to the current tenant. The queries keep to the caller's tenant
/// themselves as well.
/// </para>
/// </summary>
/// <typeparam name="TTenantId">The application's tenant id.</typeparam>
/// <typeparam name="TSeatId">The application's seat id.</typeparam>
/// <typeparam name="TUnitId">The application's unit id.</typeparam>
/// <typeparam name="TRoleId">The application's role id.</typeparam>
public interface ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <summary>The permissions seats hold at units.</summary>
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
    /// The seats that hold <paramref name="permission"/> at <paramref name="unit"/> now, as the storage itself answers
    /// the calling seat, or <see langword="null"/> when it has no answer of its own and the question is worked
    /// out over the rows above. A storage that shows a seat only its own <see cref="SeatRights"/> has one: over
    /// those rows the question would find nobody but the seat itself, whoever asks. Its answer keeps to what
    /// <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.SeatsHoldingPermissionAt"/> says a seat learns,
    /// and is a query of the same context, so it still composes into one statement.
    /// </summary>
    /// <param name="permission">A live permission of the catalogue.</param>
    /// <param name="unit">The unit.</param>
    IQueryable<TSeatId>? SeatsHoldingPermissionAt(string permission, TUnitId unit) => null;

    /// <summary>
    /// Whether <paramref name="seat"/> holds <paramref name="permission"/> at the root unit of <paramref name="tenant"/> at
    /// <paramref name="moment"/>, read by the storage with a query it keeps, or <see langword="null"/> when it has no such
    /// query and the question is worked out over the rows above. The answer is the one
    /// <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.HoldsAtTenantAsync"/> works out over
    /// <see cref="SeatRights"/> and <see cref="Units"/>. A storage that compiles its queries answers here, so a check of a
    /// permission for the whole tenant builds no query.
    /// </summary>
    /// <param name="tenant">The seat's tenant.</param>
    /// <param name="seat">The seat.</param>
    /// <param name="permission">A live permission of the catalogue.</param>
    /// <param name="moment">The moment a right must apply at.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    Task<bool>? HoldsAtTenantAsync(TTenantId tenant, TSeatId seat, string permission, DateTimeOffset moment, CancellationToken cancellationToken) => null;

    /// <summary>
    /// Whether <paramref name="seat"/> holds <paramref name="permission"/> at <paramref name="unit"/> or at a unit above it at
    /// <paramref name="moment"/>, read by the storage with a query it keeps, or <see langword="null"/> when it has no such query
    /// and the question is worked out over the rows above. The answer is the one
    /// <see cref="ITenancyQueries{TTenantId, TSeatId, TUnitId, TRoleId}.HoldsAtAsync"/> works out over
    /// <see cref="SeatRights"/> and <see cref="UnitPaths"/>.
    /// </summary>
    /// <param name="tenant">The seat's tenant.</param>
    /// <param name="seat">The seat.</param>
    /// <param name="permission">A live permission of the catalogue.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="moment">The moment a right must apply at.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    Task<bool>? HoldsAtAsync(TTenantId tenant, TSeatId seat, string permission, TUnitId unit, DateTimeOffset moment, CancellationToken cancellationToken) => null;

    /// <summary>
    /// Whether <paramref name="unit"/> is a unit of <paramref name="tenant"/>, read by the storage with a query it keeps, or
    /// <see langword="null"/> when it has no such query and the question is worked out over <see cref="Units"/>. System work
    /// in a tenant holds every permission at every unit of that tenant, so this is its check of a permission at a unit.
    /// </summary>
    /// <param name="tenant">The tenant.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    Task<bool>? HasUnitAsync(TTenantId tenant, TUnitId unit, CancellationToken cancellationToken) => null;
}
