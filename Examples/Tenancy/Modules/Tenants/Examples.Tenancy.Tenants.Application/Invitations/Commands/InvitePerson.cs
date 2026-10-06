using DDDToolkit.Identity;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Tenants.Domain.Aggregates.Invitations;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Invitations.Commands;

/// <summary>
/// Invites a person by their address: a seat placed in a unit, with a role there until a moment or for good, for
/// whoever accepts with the token this answers. No seat is made here; accepting makes it.
/// </summary>
/// <remarks>
/// It requires <c>tenancy.seats.manage</c> for the whole tenant, the first thing the package's use case asks. The
/// use case asks for that key again, then for <c>tenancy.grants.manage</c> at the unit, and holds the role to the
/// rule every grant is held to, so nobody offers by invitation what they could not give by hand.
/// <para>
/// The token is answered this once, and the application keeps only its digest: it writes the token to no log, no
/// event and no refusal of its own. Whoever invites can hand it to the person, as the link the UI shows. That is
/// why this command answers more than the id of what it made: no query could answer the token later.
/// </para>
/// </remarks>
/// <param name="Address">The person's e-mail address.</param>
/// <param name="Unit">The unit the seat is placed in.</param>
/// <param name="Role">The role the seat holds there, which must be active.</param>
/// <param name="Until">When that role ends, later than the invitation itself, or <see langword="null"/> for no end.</param>
/// <param name="DisplayName">A name suggested for the seat, or <see langword="null"/>.</param>
public sealed record InvitePerson(string Address, OrganizationUnitId Unit, RoleId Role, DateTimeOffset? Until, string? DisplayName)
    : ICommand<TenantsTenancy.IssuedInvitation<InvitationId>>, ITenantsRequest
{
    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.ForTheWholeTenant(TenancyKeys.SeatsManage);
}

/// <summary>
/// Handles <see cref="InvitePerson"/>: the package's use case issues the invitation, and then the identity
/// provider is asked for an account at the address, so the person has something to sign in with.
/// </summary>
/// <remarks>
/// The invitation comes first, because it is the package that says whether the caller may invite at all: nobody
/// who may not gets the provider to make an account or to send a mail.
/// <para>
/// What the provider answers stays here. An address that had an account already and one that got its account just
/// now are answered alike, so inviting tells nobody which addresses have accounts. The account is given nothing:
/// the seat is made when the person accepts, for the verified identity that then holds the token and the address.
/// </para>
/// <para>
/// <b>The account the provider made is kept with the invitation</b>, by its id
/// (<see cref="Invitation.InvitedAccount"/>). The provider's mail lets a person in once, and for a while. A
/// person whose mail was lost, or who came to it too late, has an account and no way into it, and the provider
/// then answers that the address is taken and mails nobody. So an address that is taken is looked for among the
/// tenant's open invitations: when an earlier one kept an account for it that nobody ever signed in with, that
/// account is the application's own, still waiting, and the provider mails it again, by its id, with this
/// invitation's token. An account somebody signed in with is not mailed: its address is proven, and the provider
/// invites no such account. Whoever invited then hands the link over, and the person signs in the ordinary way.
/// </para>
/// <para>
/// <b>The mail carries the token, when the host says where invitations are accepted</b>
/// (<see cref="InvitationPage"/>). The provider is asked to send the person on to that page, with the token after
/// the <c>#</c> of its address, a part a browser sends to no server. On the way to the person the token is not
/// kept from everyone, though: the page's address travels to the provider in the address of the request that asks
/// for the mail, where the provider's request log and any gateway's in front of it keep it, and it is in the mail
/// itself. Whoever reads those logs, or that mailbox, holds the token of an open invitation. So where the mail
/// carries the token, an invitation rests on the other thing accepting takes: a sign-in with the invited
/// address, which the provider alone gives. Keeping the token from the provider takes a mail of the application's
/// own, which this sample does not send. An address that is not mailed gets no token this way, so there whoever
/// invited passes it on, as in a host that names no page.
/// </para>
/// <para>
/// When the provider cannot be asked, the invitation is cancelled again before the failure is thrown: its token
/// would be answered to nobody, and an invitation nobody can accept should not stay open.
/// </para>
/// </remarks>
/// <param name="invitations">The package's use cases for invitations.</param>
/// <param name="kept">Where an invitation is found again, to keep the account the provider made for it.</param>
/// <param name="store">The unit of work the invitation is saved with, which the package's use cases save through.</param>
/// <param name="accounts">
/// The accounts people sign in with, or <see langword="null"/> in a host that makes none: there a person signs up
/// by themself, and the invitation is all there is.
/// </param>
/// <param name="page">Where an invitation is accepted, or <see langword="null"/> in a host that names no page.</param>
public sealed class InvitePersonHandler(
    TenantsTenancy.InvitationCommands<Invitation, InvitationId> invitations,
    TenantsTenancy.IInvitationStore<Invitation, InvitationId> kept,
    TenantsTenancy.IStore store,
    IIdentityAccounts? accounts = null,
    InvitationPage? page = null)
    : ICommandHandler<InvitePerson, TenantsTenancy.IssuedInvitation<InvitationId>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">What the package's use case refuses, with its code.</exception>
    public async ValueTask<TenantsTenancy.IssuedInvitation<InvitationId>> Handle(InvitePerson command, CancellationToken cancellationToken)
    {
        var issued = await invitations.IssueAsync(command.Address, command.Unit, command.Role, command.Until, command.DisplayName, lifetime: null, cancellationToken);

        if (accounts is not null)
        {
            try
            {
                // The mail leads to the page for accepting, with the token after the '#', where the host named one.
                var address = command.Address.Trim();
                var leadsTo = page?.With(issued.Token);

                // Made just now, with a mail from the provider that lets the person in, or there already. Either
                // way the answer below is the same.
                var account = await accounts.InviteByEmailAsync(address, leadsTo, cancellationToken) is IdentityAccountOutcome.Created made
                    ? made.Identity
                    : await MailedAgainAsync(accounts, issued.Id, address, leadsTo, cancellationToken);

                // The package saved the invitation when it issued it; what the application adds is saved after.
                if (account is { } own && await kept.FindAsync(issued.Id, cancellationToken) is { } invitation)
                {
                    invitation.KeepAccount(own);
                    await store.SaveAsync(cancellationToken);
                }
            }
            catch
            {
                // Not on the request's token: a caller that stopped waiting still leaves no invitation behind.
                await invitations.CancelAsync(issued.Id, CancellationToken.None);
                throw;
            }
        }

        return issued;
    }

    /// <summary>
    /// For an address the provider says is taken: the account an earlier open invitation of this tenant kept for
    /// it, mailed again with this invitation's link, when nobody ever signed in with it. <see langword="null"/>
    /// when there is no such account, when somebody did, or when the provider mailed nobody.
    /// </summary>
    /// <remarks>
    /// Only an account this application made is mailed, by the id it kept: the provider knows the address of that
    /// account, so the mail reaches the account that was meant, and an account somebody else registered at the
    /// address is never asked about. The open invitations are the ones the caller may list, which for whoever
    /// may invite is all of them.
    /// </remarks>
    private async Task<Guid?> MailedAgainAsync(IIdentityAccounts provider, InvitationId issued, string address, Uri? leadsTo, CancellationToken cancellationToken)
    {
        foreach (var earlier in await invitations.ListOpenAsync(cancellationToken))
        {
            if (earlier.Id == issued
                || !string.Equals(earlier.Address, address, StringComparison.OrdinalIgnoreCase)
                || (await kept.FindAsync(earlier.Id, cancellationToken))?.InvitedAccount is not { } account)
            {
                continue;
            }

            // Still waiting for its first sign-in, as the provider knows it now.
            return await provider.FindAsync(account, cancellationToken) is { HasSignedIn: false }
                && await provider.InviteAccountAsync(account, leadsTo, cancellationToken) == IdentityInvitation.Sent
                    ? account
                    : null;
        }

        return null;
    }
}
