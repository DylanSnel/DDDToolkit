using DDDToolkit.Identity;

namespace DDDToolkit.Auth.Supabase;

/// <summary>
/// Supabase Auth as the application's identity provider: <see cref="IIdentityAccounts"/> over
/// <see cref="SupabaseAuthAdmin"/>. Application code asks the port, so a host on another provider, or a
/// development host with none, swaps this for an adapter of its own and nothing else changes.
/// </summary>
/// <remarks>
/// <para>
/// <b>An invitation makes the user first.</b> Auth's own invitation of an address also mails a user who
/// was there already and never proved the address, and answers with that user's id. In a project that lets
/// anybody sign up, that user can be a stranger's, who chose its password and gets in with it once the
/// invited person has proven the address. So <see cref="InviteByEmailAsync"/> makes the user itself, which
/// Auth refuses for an address that has any user, proven or not, and only then has Auth mail it. The id it
/// answers is therefore the id of a user this adapter made, and a sign-up by somebody else afterwards does
/// not change that user. An address somebody registered first answers
/// <see cref="IdentityAccountOutcome.AddressTaken"/> and is not mailed.
/// </para>
/// <para>
/// <b>Two calls for one address at the same moment.</b> Auth looks for the address before it writes, and
/// of two calls that both found nothing the database lets one write. The other is refused as a duplicate
/// and answers <see cref="IdentityAccountOutcome.AddressTaken"/>, so one address never gets two answers
/// that say <see cref="IdentityAccountOutcome.Created"/>.
/// </para>
/// <para>
/// <b>A mail that could not be sent.</b> The invitation is two requests, and the second can fail when the
/// first did not: Auth's allowance of mails is used up, or the connection is lost. The user that was made
/// is then deleted again before the failure is thrown, so the invitation can be repeated. If Auth did
/// send the mail and only its answer was lost, that mail's link leads nowhere, and the mail of the next
/// invitation is the one that works.
/// </para>
/// <para>
/// <b>An answer that never arrived.</b> The first request has no such way back. When Auth made the user
/// and its answer was lost, because the connection broke or the caller stopped waiting, nobody learned the
/// id, so there is nothing to delete by. The user stays, and the address answers
/// <see cref="IdentityAccountOutcome.AddressTaken"/> from then on, like an address somebody else
/// registered. An application that has to pick up after that failure as well keeps the id itself:
/// <see cref="CreateAsync"/> under an id it stored first, then <see cref="InviteAccountAsync"/>.
/// </para>
/// <para>
/// <b>A user of the application's own is invited by its id.</b> <see cref="InviteAccountAsync"/> has Auth
/// mail the address it has for the user with that id, and checks that the user Auth mailed is that one.
/// </para>
/// </remarks>
/// <param name="admin">The admin client of the project's Auth.</param>
public sealed class SupabaseIdentityAccounts(SupabaseAuthAdmin admin) : IIdentityAccounts
{
    /// <summary>How long taking a user away again may take, when the mail for it could not be sent.</summary>
    private static readonly TimeSpan CleanupPatience = TimeSpan.FromSeconds(10);

    private readonly SupabaseAuthAdmin _admin = admin ?? throw new ArgumentNullException(nameof(admin));

    /// <inheritdoc />
    public async Task<IdentityAccountOutcome> CreateAsync(Guid identity, string address, CancellationToken cancellationToken)
    {
        // Left to Auth, an empty id would be one it chooses itself, and the caller asked for a given one.
        if (identity == Guid.Empty)
        {
            throw new ArgumentException("The id to make the account under is the empty one: give the id the application refers to the person by.", nameof(identity));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        try
        {
            var user = await _admin.CreateUserAsync(new SupabaseNewUser(address, identity), cancellationToken).ConfigureAwait(false);
            return new IdentityAccountOutcome.Created(user.Id);
        }
        catch (SupabaseAuthAdminException refused) when (refused.AddressAlreadyRegistered)
        {
            return new IdentityAccountOutcome.AddressTaken();
        }
        catch (SupabaseAuthAdminException refused) when (refused.DuplicateKey)
        {
            // The database refused a duplicate, which is the id or the address. An id that still has no
            // user leaves the address: another call registered it while this one was on its way. An id
            // that has a user is the caller's mistake, and stays the error it was.
            if (await _admin.FindUserAsync(identity, cancellationToken).ConfigureAwait(false) is null)
            {
                return new IdentityAccountOutcome.AddressTaken();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IdentityAccountOutcome> InviteByEmailAsync(string address, Uri? signInRedirect, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var options = InvitationTo(signInRedirect);

        // The user first, and under an id Auth chooses. Making one is refused for an address that has any
        // user, so what gets mailed below is a user that did not exist a moment ago and that nobody but
        // this call knows the id of.
        SupabaseAuthUser made;
        try
        {
            made = await _admin.CreateUserAsync(new SupabaseNewUser(address), cancellationToken).ConfigureAwait(false);
        }
        catch (SupabaseAuthAdminException refused) when (refused.AddressAlreadyRegistered || refused.DuplicateKey)
        {
            // Auth chose the id, so a duplicate can only be the address: another call registered it while
            // this one was on its way.
            return new IdentityAccountOutcome.AddressTaken();
        }

        SupabaseInvitationResult invited;
        try
        {
            invited = await _admin.InviteByEmailAsync(address, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception mailFailure)
        {
            // A user nobody was mailed about would keep the address taken, with an id nobody has: the
            // invitation could never be tried again. So it goes, and the failure is the caller's as it was.
            await TakeBackAsync(made.Id, mailFailure).ConfigureAwait(false);
            throw;
        }

        // Auth invites by address, and says which user it mailed. Only the user made above counts: any
        // other answer means that user is no longer the one at the address, and whoever is there instead
        // is not this call's to vouch for.
        return invited is SupabaseInvitationResult.Sent sent && sent.UserId == made.Id
            ? new IdentityAccountOutcome.Created(made.Id)
            : new IdentityAccountOutcome.AddressTaken();
    }

    /// <inheritdoc />
    public async Task<IdentityInvitation> InviteAccountAsync(Guid identity, Uri? signInRedirect, CancellationToken cancellationToken)
    {
        if (identity == Guid.Empty)
        {
            throw new ArgumentException("The id of the account to invite is the empty one: give the id the account was made under.", nameof(identity));
        }

        var options = InvitationTo(signInRedirect);

        return await _admin.InviteUserAsync(identity, options, cancellationToken).ConfigureAwait(false) switch
        {
            null => IdentityInvitation.NoSuchAccount,
            SupabaseInvitationResult.Sent => IdentityInvitation.Sent,
            _ => IdentityInvitation.AlreadyProven,
        };
    }

    /// <inheritdoc />
    public async Task<IdentityAccount?> FindAsync(Guid identity, CancellationToken cancellationToken)
        => await _admin.FindUserAsync(identity, cancellationToken).ConfigureAwait(false) is { } user
            ? new IdentityAccount(user.Id, user.HasSignedIn)
            : null;

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid identity, CancellationToken cancellationToken)
        => _admin.DeleteUserAsync(identity, cancellationToken);

    private static SupabaseInvitation? InvitationTo(Uri? signInRedirect) => signInRedirect switch
    {
        null => null,
        { IsAbsoluteUri: true } => new SupabaseInvitation(RedirectTo: signInRedirect.AbsoluteUri),
        _ => throw new ArgumentException("The page the invitation leads to has to be an absolute URL: the link is opened from a mail, where nothing says which site a relative one belongs to.", nameof(signInRedirect)),
    };

    /// <summary>
    /// Deletes the user an invitation made and could not mail. It does not wait on the caller's token,
    /// which may be the very reason the mail failed, but on a patience of its own.
    /// </summary>
    private async Task TakeBackAsync(Guid made, Exception mailFailure)
    {
        try
        {
            using var patience = new CancellationTokenSource(CleanupPatience);
            await _admin.DeleteUserAsync(made, patience.Token).ConfigureAwait(false);
        }
        catch (Exception cleanupFailure)
        {
            throw new IdentityAccountLeftBehindException(made, mailFailure, cleanupFailure);
        }
    }
}
