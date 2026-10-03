namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>One of the signed-in person's seats, with its tenant (<c>GET /me/seats</c>): the tenant picker's rows.</summary>
public sealed record SeatOfMine(TenantOfSeat Tenant, SeatInfo Seat);
