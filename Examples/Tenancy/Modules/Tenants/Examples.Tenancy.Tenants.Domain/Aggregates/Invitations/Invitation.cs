namespace Examples.Tenancy.Tenants.Domain.Aggregates.Invitations;

/// <summary>
/// The application's invitation: the package's, closed over <see cref="InvitationId"/> and the ids of the other
/// classes, with the one thing the application adds. It offers a person at an address a seat in one unit with one
/// role there, and no seat exists until they accept.
/// </summary>
/// <remarks>
/// It is the one class of the package's an application may leave out. Declaring it is what gives the module its
/// invitations: the infrastructure project maps it with <c>AddTenancyInvitations</c> and registers its use cases,
/// and the application project offers them as the <c>Invitations</c> feature.
/// <para>
/// What it adds is <see cref="InvitedAccount"/>, as the job title is added to a seat. The package invites an
/// address and knows no identity provider; this application has one make an account for the address, and has to
/// know which account that was to do anything further with it. An identity provider finds no account by its
/// address, so the id is kept here, where the invitation is.
/// </para>
/// </remarks>
[InvitationAggregate<InvitationId>]
public sealed partial class Invitation
{
    /// <summary>
    /// The account the identity provider made for the address this invitation was sent to, by its id; or
    /// <see langword="null"/> when it made none for it, because the address had an account already or the host
    /// has no provider.
    /// </summary>
    /// <remarks>
    /// Kept for two things. An account whose invitation went unanswered is mailed again by this id when the
    /// address is invited again, and an account nobody ever used is deleted by it when the invitation is
    /// cancelled. It is forgotten with the address when the invitation is cancelled. An accepted invitation keeps
    /// it: it is then the account behind the seat the acceptance made.
    /// </remarks>
    public Guid? InvitedAccount { get; private set; }

    /// <summary>Keeps the id of the account the identity provider made for this invitation's address.</summary>
    /// <param name="account">The account's id, as the provider answered it.</param>
    /// <exception cref="ArgumentException"><paramref name="account"/> is the empty id.</exception>
    public void KeepAccount(Guid account)
    {
        if (account == Guid.Empty)
        {
            throw new ArgumentException("An account is kept by the id the identity provider gave it, which is never the empty one.", nameof(account));
        }

        InvitedAccount = account;
    }

    /// <summary>Forgets the account, as cancelling forgets the address: an invitation that is over keeps nothing about the person it was sent to.</summary>
    public void ForgetAccount() => InvitedAccount = null;
}
