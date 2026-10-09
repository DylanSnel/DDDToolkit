using Examples.Tenancy.Tenants.Domain.Aggregates.Invitations;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Invitations.Commands;

/// <summary>
/// Accepts the invitation a token is for, as the signed-in person who sends it: in one save they get a seat in
/// the invitation's tenant, placed in its unit, with its role there.
/// </summary>
/// <remarks>
/// It requires a signed-in user, and nothing more, because it is sent before the person is in any tenant: there is
/// no seat yet to hold a key. The package's use case asks the rest: a verified identity that may hold a seat, read
/// from the caller's own token and never from the request, which holds the token of an invitation that is still
/// open. It then does its work as system work in the invitation's tenant, begun by the package itself: what the
/// person gets is the seat the invitation offers, and nothing of the system work's.
/// <para>
/// The application invites by address, so it also holds an invitation to its address: the address in the
/// caller's token must be the one the invitation was sent to. That only narrows. Nothing is found by an address,
/// and a caller whose token carries none accepts nothing. The address is worth what the identity provider's proof
/// of it is worth: where an account signs in before its address is proven, it adds nothing to holding the token.
/// </para>
/// <para>
/// It answers the seat it made, as a command does. Which tenant that seat is in is read from the seats the person has.
/// </para>
/// </remarks>
/// <param name="Token">The token, as inviting answered it.</param>
/// <param name="DisplayName">
/// The name the person is shown by in the invitation's tenant: this application's field on its seat, which its rule
/// requires (<c>tenants.seat.display-name</c>). Tenancy keeps none, and the invitation suggests none. Sent again for
/// an invitation the person accepted before, it changes nothing: the answer is the seat that acceptance made.
/// </param>
public sealed record AcceptInvitation(string Token, string? DisplayName) : ICommand<SeatId>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.SignedIn();

    /// <summary>Says that it accepts and not with which token: a record prints its members, and a token must not end up in a log.</summary>
    public override string ToString() => nameof(AcceptInvitation);
}

/// <summary>
/// Handles <see cref="AcceptInvitation"/> with the Tenancy package's use case, which checks the caller, decides and
/// saves; the seat it makes is named in the use case's callback, by this application's own rule.
/// </summary>
/// <param name="invitations">The package's use cases for invitations.</param>
/// <param name="callers">Who is calling, as the host verified it: where the caller's address is read.</param>
public sealed class AcceptInvitationHandler(TenancyUseCases.InvitationCommands<Invitation, InvitationId> invitations, ICallerAccessor callers) : ICommandHandler<AcceptInvitation, SeatId>
{
    /// <summary>The claim of an access token that carries the account's e-mail address.</summary>
    private const string AddressClaim = "email";

    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// What the package's use case refuses, with its code, and <c>tenants.seat.display-name</c> without a name for the
    /// seat, which leaves the invitation open.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrencyConflictException">Another change of access in the tenant, another acceptance included, was saved first.</exception>
    public async ValueTask<SeatId> Handle(AcceptInvitation command, CancellationToken cancellationToken)
    {
        // Never from the request. Without an address in the token there is nothing to hold against the
        // invitation's, and an empty one matches none: the package then refuses as it does another address.
        var address = callers.Current.Claim(AddressClaim) ?? string.Empty;

        // The name is this application's field: set on the new seat once the package found the invitation good, and
        // before anything is saved, so a name the seat refuses leaves the invitation open for another try.
        var accepted = await invitations.AcceptAsync(command.Token, address, cancellationToken, configure: seat => seat.Rename(command.DisplayName));
        return accepted.Seat;
    }
}
