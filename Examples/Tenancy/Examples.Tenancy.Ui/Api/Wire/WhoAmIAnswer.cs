namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>Who the calling seat is, and what it may do where (<c>GET /me</c>).</summary>
public sealed record WhoAmIAnswer(
    TenantInfo Tenant,
    SeatInfo Seat,
    IReadOnlyList<PlacementInfo> Placements,
    IReadOnlyList<RoleInfo> Roles,
    IReadOnlyList<KeyInfo> Keys);
