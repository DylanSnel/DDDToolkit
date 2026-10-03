using Examples.Tenancy.Ui.Api;
using Examples.Tenancy.Ui.Api.Wire;
using Examples.Tenancy.Ui.Session;

namespace Examples.Tenancy.Ui.Auth;

/// <summary>
/// What a tab does with the sign-in a link carries: takes it when nobody is signed in, and asks first when
/// somebody else is.
/// </summary>
/// <remarks>
/// A link to the accept page can carry a sign-in after its <c>#</c>: Supabase Auth's mail leads there with one.
/// But anybody can make such a link, with the access token of an account of their own, and send it to somebody
/// who is signed in. A tab that took it without a word would change hands: its person would go on as the other
/// account, and a password chosen next would be that account's. So a link's sign-in replaces nobody without that
/// person's say:
/// <list type="bullet">
/// <item>a tab that is signed out takes it, once Auth has said whose it is: that is what the mail is for;</item>
/// <item>a tab signed in as the very person the link names takes it too, since nobody changes;</item>
/// <item>a tab signed in as anybody else keeps its session and is told whom the link would sign in. The page asks,
/// and only <see cref="ConfirmAsync"/> goes on.</item>
/// </list>
/// What the link carried is kept here, in the circuit's memory, until the person answers, and dropped when they
/// decline or another link arrives.
/// </remarks>
/// <param name="login">The UI's sign-in at Supabase Auth, which says whose a link's access token is.</param>
/// <param name="api">The host's API, asked which seats that person has.</param>
/// <param name="session">The tab's session.</param>
public sealed class LinkSignIn(SupabaseLoginClient login, SampleApi api, UiSession session)
{
    private (string Email, TokenAnswer Answer, string? Kind)? _waiting;

    /// <summary>
    /// A link arrived with a sign-in. Auth is asked whose it is; then it is taken, or, in a tab that is signed in
    /// as somebody else, kept waiting for <see cref="ConfirmAsync"/>.
    /// </summary>
    /// <param name="link">What the address carried.</param>
    /// <param name="cancellationToken">Stops waiting for Auth and the API.</param>
    public async Task<LinkSignInOutcome> ArriveAsync(AuthLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        // Whatever an earlier link left waiting is no longer asked about.
        _waiting = null;

        // What the address said starts no session by itself: Auth says whose the sign-in is.
        var signedIn = await login.SignInWithLinkAsync(link, cancellationToken);
        if (signedIn.Answer is not { User.Email: { } email } answer)
        {
            return new LinkSignInOutcome(Refusal: signedIn.Refusal);
        }

        if (session.IsSignedIn && !string.Equals(session.Person, email, StringComparison.OrdinalIgnoreCase))
        {
            _waiting = (email, answer, link.Kind);
            return new LinkSignInOutcome(AsksToSignInAs: email);
        }

        return await TakeAsync(email, answer, link.Kind, cancellationToken);
    }

    /// <summary>The person said yes: the sign-in that was kept waiting replaces the tab's session. Nothing when none waits.</summary>
    /// <param name="cancellationToken">Stops waiting for the API.</param>
    public async Task<LinkSignInOutcome> ConfirmAsync(CancellationToken cancellationToken = default)
    {
        if (_waiting is not var (email, answer, kind))
        {
            return new LinkSignInOutcome();
        }

        _waiting = null;
        return await TakeAsync(email, answer, kind, cancellationToken);
    }

    /// <summary>The person said no: the sign-in that was kept waiting is dropped, and the tab's session stays as it is.</summary>
    public void Decline() => _waiting = null;

    // The API takes the token, as after a sign-in with a password. Whoever was signed in in this tab before is not
    // any more.
    private async Task<LinkSignInOutcome> TakeAsync(string email, TokenAnswer answer, string? kind, CancellationToken cancellationToken)
    {
        var seats = await api.SeatsOfMineAsync(CallAs.Person(email, answer.AccessToken), cancellationToken);
        if (seats.Value is not { } mine)
        {
            return new LinkSignInOutcome(TokenRefused: seats);
        }

        session.SignIn(email, email, answer.AccessToken, answer.Expires, UiSession.TenantToStartIn(mine));

        // Whoever an invitation's mail signed in has no password yet, and the link works once.
        return new LinkSignInOutcome(SignedIn: true, ChoosesPassword: kind == AuthLink.Invitation);
    }
}
