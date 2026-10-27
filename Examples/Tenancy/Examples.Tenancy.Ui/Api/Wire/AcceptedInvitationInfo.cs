namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// The answer to accepting an invitation (<c>POST /invitations/accept</c>): the tenant the invitation was for, whose
/// slug the next request names, and the seat the person now has there.
/// </summary>
public sealed record AcceptedInvitationInfo(TenantInfo Tenant, SeatInfo Seat);
