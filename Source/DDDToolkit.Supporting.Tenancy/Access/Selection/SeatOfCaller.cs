using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>
/// A person's seat in one tenant, as the directory finds it for their verified identity when the caller's seat is
/// selected (<see cref="ISeatDirectory{TTenantId, TSeatId}.FindAsync"/>): the tenant and the seat, each by its id and
/// status, which is all the selection decides by. A tenant picker is answered the application's own seats instead
/// (<see cref="ISeatDirectory{TTenantId, TSeatId}.AllOfAsync{TSeat}"/>).
/// </summary>
/// <param name="Tenant">The tenant's id.</param>
/// <param name="Slug">The slug the tenant is selected by.</param>
/// <param name="OrganizationName">The tenant's name.</param>
/// <param name="TenantStatus">Where the tenant is in its life.</param>
/// <param name="Seat">The seat's id.</param>
/// <param name="SeatStatus">Whether the seat counts.</param>
public sealed record SeatOfCaller<TTenantId, TSeatId>(
    TTenantId Tenant,
    string Slug,
    string OrganizationName,
    TenantStatus TenantStatus,
    TSeatId Seat,
    SeatStatus SeatStatus)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
