namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// The answer to accepting an invitation (<c>POST /invitations/accept</c>): the seat the person now has. Which
/// tenant it is in, their own seats say (<c>GET /me/seats</c>).
/// </summary>
public sealed record AcceptedInvitationInfo(Guid SeatId);
