using DDDToolkit.Identity;
using Examples.Tenancy.Tenants.Domain.Aggregates.Invitations;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Invitations.Commands;

/// <summary>
/// Cancels an open invitation, which revokes it: its token no longer works. It forgets the address it was for,
/// and the account the identity provider made for that address is deleted when nobody ever used it.
/// </summary>
/// <remarks>
/// It requires a caller who works in the tenant. The key is for the package's use case to ask, since only it reads
/// which unit the invitation is for: <c>tenancy.seats.manage</c> at that unit, whoever issued it. It answers anyone
/// else that there is no such invitation, as it is not among the ones they list.
/// </remarks>
/// <param name="Invitation">The invitation.</param>
public sealed record CancelInvitation(InvitationId Invitation) : ICommand, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
}

/// <summary>
/// Handles <see cref="CancelInvitation"/> with the Tenancy package's use case, which checks the caller, decides
/// and saves; and then takes away the account the invitation was the only reason for.
/// </summary>
/// <remarks>
/// Inviting kept the id of the account the identity provider made for the address
/// (<see cref="Invitation.InvitedAccount"/>). The invitation forgets it as it forgets the address, in the one
/// save the package makes, and nothing is deleted before that save has passed the package's check. After it:
/// <list type="bullet">
/// <item>an account nobody ever signed in with is deleted. It was made for a person who never came, it holds
/// nothing, and left alone it would keep the address taken at the provider for good;</item>
/// <item>an account somebody signed in with stays. It is a person's by then, whatever became of this
/// invitation;</item>
/// <item>an account another open invitation of the tenant still keeps stays too: inviting the address again kept
/// the same account there.</item>
/// </list>
/// The provider is asked after the save, so a provider that cannot be reached fails the request and leaves the
/// invitation cancelled, which is what was asked for; the account is then left for whoever looks after unused
/// accounts.
/// </remarks>
/// <param name="invitations">The package's use cases for invitations.</param>
/// <param name="kept">Where an invitation is found, to read and forget the account it kept.</param>
/// <param name="accounts">The accounts people sign in with, or <see langword="null"/> in a host that makes none.</param>
public sealed class CancelInvitationHandler(
    SampleInvitations invitations,
    SampleTenancy.IInvitationStore<Invitation, InvitationId> kept,
    IIdentityAccounts? accounts = null)
    : ICommandHandler<CancelInvitation>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<Unit> Handle(CancelInvitation command, CancellationToken cancellationToken)
    {
        // The store keeps to the caller's tenant, and what is read here is told to nobody: whether the caller may
        // cancel this invitation at all is the package's to say, below.
        var invitation = await kept.FindAsync(command.Invitation, cancellationToken);
        var account = invitation?.InvitedAccount;

        // Forgotten in the unit of work, so the package's own save writes it with the cancellation. A refusal
        // saves nothing, and then the unit of work holds again what the database does.
        invitation?.ForgetAccount();
        try
        {
            await invitations.CancelAsync(command.Invitation, cancellationToken);
        }
        catch when (invitation is not null && account is { } still)
        {
            invitation.KeepAccount(still);
            throw;
        }

        if (accounts is not null && account is { } unused && !await IsKeptByAnotherAsync(unused, cancellationToken)
            && await accounts.FindAsync(unused, cancellationToken) is { HasSignedIn: false })
        {
            await accounts.DeleteAsync(unused, cancellationToken);
        }

        return Unit.Value;
    }

    /// <summary>Whether an invitation of the tenant that can still be accepted keeps <paramref name="account"/> as well.</summary>
    private async Task<bool> IsKeptByAnotherAsync(Guid account, CancellationToken cancellationToken)
    {
        foreach (var open in await invitations.ListOpenAsync(cancellationToken))
        {
            if ((await kept.FindAsync(open.Id, cancellationToken))?.InvitedAccount == account)
            {
                return true;
            }
        }

        return false;
    }
}
