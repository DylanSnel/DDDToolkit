namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// One of the signed-in person's seats, beside the slug, the name and the status of its tenant (<c>GET /me/seats</c>):
/// the tenant picker's rows.
/// </summary>
public sealed record SeatOfMine(SeatInfo Seat, string Slug, string OrganizationName, string TenantStatus);
