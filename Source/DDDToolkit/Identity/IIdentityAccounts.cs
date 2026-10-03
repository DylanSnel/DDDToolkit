namespace DDDToolkit.Identity;

/// <summary>
/// The accounts people sign in with, at whichever identity provider the host uses, for the few things an
/// application's own server code does with them: make one for a person who has not signed in yet, have the
/// provider invite an address or an account, see whether an account was ever used, and delete one.
/// </summary>
/// <remarks>
/// <para>
/// It is a port. The application asks through it and one adapter per identity provider answers:
/// <c>DDDToolkit.Auth.Supabase</c> ships the one for Supabase Auth, and a host on another provider, or a
/// development host without one, writes its own. Whatever is behind it works with the provider's
/// administrative key, so it belongs in server code and never in a browser.
/// </para>
/// <para>
/// <b>An account is known by its id, never by its address.</b> Nothing here looks an account up by an
/// address and no answer carries one, because an address proves nothing about who is asking: an
/// application gives a person access by the id of the account they signed in with, and a caller that
/// needs the address already has it.
/// </para>
/// <para>
/// <b>An id that comes back is the id of an account the call made.</b>
/// <see cref="IdentityAccountOutcome.Created"/> says that the call made the account, just now, and never
/// that it found one. An account that was there already can be somebody else's: where anybody can sign up
/// at the provider, a stranger can have registered the person's address and chosen its password. So the
/// only ids an application ever holds are those of accounts it made itself, and access given to one of
/// them is given to whoever proves the address is theirs. An adapter keeps to that when two calls for one
/// address arrive together as well: one of them answers <see cref="IdentityAccountOutcome.Created"/>, the
/// other <see cref="IdentityAccountOutcome.AddressTaken"/>.
/// </para>
/// <para>
/// <b>Two ways to invite, by who chooses the id.</b> <see cref="InviteByEmailAsync"/> is one call, for an
/// application that takes the id the provider gives. An application that refers to a person by an id of
/// its own, from an import or a seeding, or that wants the id on record before the provider is asked so
/// that it can pick up after a failure, makes the account with <see cref="CreateAsync"/> and has it mailed
/// with <see cref="InviteAccountAsync"/>. That second call also mails an account again whose invitation
/// went unanswered, whichever way it was made.
/// </para>
/// <para>
/// <b>Whether an address has an account is not for whoever asked to add the person.</b> A taken address
/// comes back as an ordinary answer, <see cref="IdentityAccountOutcome.AddressTaken"/>, not as an error,
/// so the application can go on in its own way, for instance with an invitation it mails itself. What it
/// tells the person who asked must be the same for a new address and a taken one: anyone allowed to add
/// people could otherwise find out which addresses have accounts, one guess at a time.
/// </para>
/// <para>
/// An adapter throws when the provider cannot be reached or refuses, and no exception it throws and no
/// line it logs carries an address.
/// </para>
/// </remarks>
public interface IIdentityAccounts
{
    /// <summary>
    /// Makes an account for <paramref name="address"/> under the id the caller chose, and mails nobody. It
    /// is how an application that already knows which id a person will have, from an import or a seeding,
    /// gives them access before they exist at the provider. The account is not yet proven to be the
    /// person's: they prove the address the first time they sign in, for instance after
    /// <see cref="InviteAccountAsync"/>. An id that is already in use is an error, not an outcome, so work
    /// that may run twice asks <see cref="FindAsync"/> first.
    /// </summary>
    /// <param name="identity">The id the account gets; the <c>sub</c> of every token it later signs in with.</param>
    /// <param name="address">The person's e-mail address.</param>
    /// <param name="cancellationToken">Stops waiting for the provider.</param>
    /// <returns>
    /// <see cref="IdentityAccountOutcome.Created"/> with <paramref name="identity"/>, or
    /// <see cref="IdentityAccountOutcome.AddressTaken"/> when the address already has an account, whether
    /// anyone ever used it or not, or gets one from another call at the same moment.
    /// </returns>
    Task<IdentityAccountOutcome> CreateAsync(Guid identity, string address, CancellationToken cancellationToken);

    /// <summary>
    /// Makes an account for <paramref name="address"/> under an id the provider chooses, and has the
    /// provider mail the person a link of its own that lets them in. The id comes back at once, so the
    /// application can give the account access before they first sign in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The account is made first and mailed after, so the account the mail leads to is the one this call
    /// made. An address that has any account answers <see cref="IdentityAccountOutcome.AddressTaken"/> and
    /// is not mailed, also when nobody ever used that account and also when the application made it
    /// itself: to have an account of its own mailed again, the application asks
    /// <see cref="InviteAccountAsync"/> with the id it kept.
    /// </para>
    /// <para>
    /// When the mail cannot be sent, the account is taken away again before the failure is thrown, so the
    /// call can be repeated. Should that fail as well, what is thrown is an
    /// <see cref="IdentityAccountLeftBehindException"/> with the account's id.
    /// </para>
    /// <para>
    /// A failure while the account is being made is thrown as it is. The provider chooses the id, so it
    /// can have made the account and lost its answer on the way, and then nobody has the id: the account
    /// stays, and the address answers <see cref="IdentityAccountOutcome.AddressTaken"/> from then on. An
    /// application that has to pick up after that failure as well keeps the id itself, with
    /// <see cref="CreateAsync"/> and <see cref="InviteAccountAsync"/>.
    /// </para>
    /// </remarks>
    /// <param name="address">The person's e-mail address.</param>
    /// <param name="signInRedirect">Where the link in the mail takes the person, or <see langword="null"/> for the provider's own default.</param>
    /// <param name="cancellationToken">Stops waiting for the provider.</param>
    /// <returns>
    /// <see cref="IdentityAccountOutcome.Created"/> with the id of the account this call made, which now
    /// waits for the person, or <see cref="IdentityAccountOutcome.AddressTaken"/> when the address has an
    /// account already, or gets one from another call at the same moment; this call then mailed nobody.
    /// </returns>
    /// <exception cref="IdentityAccountLeftBehindException">
    /// The account was made, the mail could not be sent, and the account could not be taken away again.
    /// </exception>
    Task<IdentityAccountOutcome> InviteByEmailAsync(string address, Uri? signInRedirect, CancellationToken cancellationToken);

    /// <summary>
    /// Has the provider mail the account with this id a link that lets its person in: the invitation for an
    /// account the application made itself with <see cref="CreateAsync"/>, and a new one for an account
    /// whose earlier invitation went unanswered. The application names the account by its id and the
    /// provider knows the address, so the mail cannot reach another account than the one that was meant.
    /// </summary>
    /// <param name="identity">The account's id, which the application has because it made the account.</param>
    /// <param name="signInRedirect">Where the link in the mail takes the person, or <see langword="null"/> for the provider's own default.</param>
    /// <param name="cancellationToken">Stops waiting for the provider.</param>
    /// <returns>
    /// <see cref="IdentityInvitation.Sent"/> when the account was mailed,
    /// <see cref="IdentityInvitation.AlreadyProven"/> when its address is proven already and nobody was
    /// mailed, or <see cref="IdentityInvitation.NoSuchAccount"/>.
    /// </returns>
    Task<IdentityInvitation> InviteAccountAsync(Guid identity, Uri? signInRedirect, CancellationToken cancellationToken);

    /// <summary>
    /// The account with this id, or <see langword="null"/> when the provider has none. "Waiting for the
    /// first sign-in" is read from here when it is shown, rather than kept as a status of the
    /// application's own that could drift from what the provider knows.
    /// </summary>
    /// <param name="identity">The account's id.</param>
    /// <param name="cancellationToken">Stops waiting for the provider.</param>
    Task<IdentityAccount?> FindAsync(Guid identity, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the account, for good: what erasing a person ends with, and what cleans up an account that
    /// was made for somebody who never came. An account that is already gone is not an error, so the work
    /// that calls this can be run again after a failure.
    /// </summary>
    /// <param name="identity">The account's id.</param>
    /// <param name="cancellationToken">Stops waiting for the provider.</param>
    /// <returns><see langword="true"/> when the account was deleted, <see langword="false"/> when there was none.</returns>
    Task<bool> DeleteAsync(Guid identity, CancellationToken cancellationToken);
}

/// <summary>What an identity provider knows about an account that an application may act on. It carries no address.</summary>
/// <param name="Id">The account's id, the <c>sub</c> of its tokens.</param>
/// <param name="HasSignedIn">Whether anyone signed in with it yet. An account nobody used can be deleted without taking anything from a person.</param>
public sealed record IdentityAccount(Guid Id, bool HasSignedIn);

/// <summary>
/// What came of asking an identity provider for an account for an address:
/// <see cref="Created"/> or <see cref="AddressTaken"/>.
/// </summary>
/// <remarks>
/// It is for the application's own next step and never for the person who asked to add somebody: see
/// <see cref="IIdentityAccounts"/> for why both cases have to look the same to them.
/// </remarks>
public abstract record IdentityAccountOutcome
{
    // Closed: an adapter answers with one of the two below, so a switch over them is complete.
    private IdentityAccountOutcome()
    {
    }

    /// <summary>
    /// The call made an account for the address, just now, and it waits for the person. It is never the
    /// answer for an account that was there before the call, whoever made that one.
    /// </summary>
    /// <param name="Identity">Its id, which the application can give access to at once.</param>
    public sealed record Created(Guid Identity) : IdentityAccountOutcome;

    /// <summary>
    /// The address has an account already, so the call made none and mailed nobody. That is all it says:
    /// not which account, nor since when, nor who made it, because nothing here looks an account up by its
    /// address.
    /// </summary>
    public sealed record AddressTaken : IdentityAccountOutcome;
}

/// <summary>
/// What came of asking an identity provider to mail an account its invitation, with
/// <see cref="IIdentityAccounts.InviteAccountAsync"/>.
/// </summary>
public enum IdentityInvitation
{
    /// <summary>The provider has no account with that id, so nobody was mailed.</summary>
    NoSuchAccount,

    /// <summary>
    /// The account's address is proven already, so the provider mailed no invitation: its person signs in
    /// the ordinary way.
    /// </summary>
    AlreadyProven,

    /// <summary>The provider mailed the account's address a link that lets the person in.</summary>
    Sent,
}

/// <summary>
/// <see cref="IIdentityAccounts.InviteByEmailAsync"/> made an account, could not have it mailed, and could
/// not take it away again either, so the provider now has an account nobody was told about.
/// </summary>
/// <remarks>
/// The address stays taken while that account is there, and nothing looks an account up by its address, so
/// this exception is the one place its id is known. Once the provider answers again, delete the account
/// with <see cref="IIdentityAccounts.DeleteAsync"/>, or have it mailed with
/// <see cref="IIdentityAccounts.InviteAccountAsync"/> and go on as if the invitation had answered
/// <see cref="IdentityAccountOutcome.Created"/> with it. The message names the id and no address.
/// </remarks>
public sealed class IdentityAccountLeftBehindException : Exception
{
    /// <summary>An exception for an account that was left behind.</summary>
    /// <param name="identity">The id of the account that was made.</param>
    /// <param name="mailFailure">Why the account could not be mailed.</param>
    /// <param name="cleanupFailure">Why it could not be taken away again.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mailFailure"/> or <paramref name="cleanupFailure"/> is null.</exception>
    public IdentityAccountLeftBehindException(Guid identity, Exception mailFailure, Exception cleanupFailure)
        : base(
            $"The identity provider made account {identity} for an invitation, could not mail it, and could not delete it again. " +
            "The address stays taken while the account is there: delete the account by this id, or invite it by this id, once the provider answers again.",
            new AggregateException(
                mailFailure ?? throw new ArgumentNullException(nameof(mailFailure)),
                cleanupFailure ?? throw new ArgumentNullException(nameof(cleanupFailure))))
    {
        Identity = identity;
    }

    /// <summary>The id of the account that was made and is still there.</summary>
    public Guid Identity { get; }
}
