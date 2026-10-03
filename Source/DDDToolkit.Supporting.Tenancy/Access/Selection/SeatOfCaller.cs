using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy.Access;

/// <summary>A person's seat in one tenant, as the directory finds it for their verified identity.</summary>
/// <param name="Tenant">The tenant's id.</param>
/// <param name="Slug">The slug the tenant is selected by.</param>
/// <param name="OrganizationName">The tenant's name.</param>
/// <param name="TenantStatus">Where the tenant is in its life.</param>
/// <param name="Seat">The seat's id.</param>
/// <param name="DisplayName">The name the seat is shown by.</param>
/// <param name="SeatStatus">Whether the seat counts.</param>
public sealed record SeatOfCaller<TTenantId, TSeatId>(
    TTenantId Tenant,
    string Slug,
    string OrganizationName,
    TenantStatus TenantStatus,
    TSeatId Seat,
    string DisplayName,
    SeatStatus SeatStatus)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>;
