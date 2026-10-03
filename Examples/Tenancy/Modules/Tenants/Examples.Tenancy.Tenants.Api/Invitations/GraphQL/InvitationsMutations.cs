using Examples.Tenancy.Tenants.Application.Invitations.Commands;
using HotChocolate;
using HotChocolate.Types;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Invitations.GraphQL;

/// <summary>
/// Inviting a person by address, cancelling an invitation and accepting one. A mutation sends the command its
/// route sends and answers what the route answers; a refusal arrives in the payload's <c>errors</c>.
/// </summary>
/// <remarks>
/// An invitation's token travels as the routes carry it: in the answer of the mutation that invites, once, and
/// in the input of the one that accepts. A client sends it as a variable, not written into the document, which a
/// server may log.
/// </remarks>
internal static class InvitationsMutations
{
    /// <summary>
    /// Invites a person by their address: a seat placed in a unit, with a role there until
    /// <paramref name="until"/> or for good, for whoever accepts. This is the one answer that carries the
    /// invitation's token, and it says nothing of whether the address had an account.
    /// </summary>
    [Mutation]
    public static async Task<SampleTenancy.IssuedInvitation<InvitationId>> PersonInviteAsync(
        string address,
        OrganizationUnitId unitId,
        RoleId roleId,
        DateTimeOffset? until,
        string? displayName,
        [Service] ISender sender,
        CancellationToken cancellationToken)
        => await sender.Send(new InvitePerson(address, unitId, roleId, until, displayName), cancellationToken);

    /// <summary>
    /// Cancels an open invitation: its token no longer works. The answer is the id that was asked, since a
    /// cancelled invitation is not among the open ones to read.
    /// </summary>
    [Mutation]
    [UseMutationConvention(PayloadFieldName = "invitationId")]
    public static async Task<InvitationId> InvitationCancelAsync(InvitationId id, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new CancelInvitation(id), cancellationToken);
        return id;
    }

    /// <summary>
    /// Accepts the invitation a token is for, as the signed-in person who sends it. Like <c>seatsOfMine</c> it
    /// needs no seat and no <c>Tenant</c> header: whoever accepts has no seat there yet, and the token says which
    /// tenant. The answer is the id of the seat they now have, as the route's is; <c>seatsOfMine</c> then lists
    /// it with its tenant.
    /// </summary>
    [Mutation]
    [UseMutationConvention(PayloadFieldName = "seatId")]
    public static async Task<SeatId> InvitationAcceptAsync(string token, string? displayName, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new AcceptInvitation(token, displayName), cancellationToken);
}
