using System.Net;
using Examples.Tenancy.Ui.Api;
using Examples.Tenancy.Ui.Auth;
using Examples.Tenancy.Ui.Session;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Ui;

/// <summary>
/// What the accept page does with the sign-in a link carries, as plain C# over stubs of Supabase Auth and of the
/// host's API. Anybody can make a link that carries the sign-in of an account of their own, so a tab that is
/// signed in is never handed to another account without its person's say.
/// </summary>
public sealed class LinkSignInTests : IDisposable
{
    /// <summary>A link as Auth's mail leads to the page: an invitation's token, and behind it a sign-in.</summary>
    private const string Landing = "http://localhost:5091/invitations/accept#the-token#access_token=the-other-accounts-token&expires_at=1790000000&type=invite";

    private readonly UiSession _session = new();

    // Auth says the link's access token is mallory's; the API says that person has no seat anywhere.
    private readonly StubApi _auth = StubApi.Answering(HttpStatusCode.OK, """{"id":"d0000000-0000-4000-8000-0000000000aa","email":"mallory@example.test"}""");
    private readonly StubApi _host = StubApi.Answering(HttpStatusCode.OK, "[]");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_tab_that_is_signed_out_takes_the_sign_in_of_the_link()
    {
        var outcome = await Arriving().ArriveAsync(AuthLink.Read(Landing), Cancellation);

        outcome.Should().Be(new LinkSignInOutcome(SignedIn: true, ChoosesPassword: true), "an invitation's mail signs in a person who has no password yet");
        (_session.Person, _session.AccessToken).Should().Be(("mallory@example.test", "the-other-accounts-token"));
        _auth.Requests.Should().ContainSingle().Which.Authorization.Should().Be("Bearer the-other-accounts-token", "Auth is asked whose the sign-in is before anything starts");
    }

    [Fact]
    public async Task A_tab_that_is_signed_in_keeps_its_session_and_asks_before_a_link_replaces_it()
    {
        _session.SignIn("enid@example.test", "Enid", "enids-token", DateTimeOffset.UtcNow.AddHours(1), "harbor");
        var arriving = Arriving();

        var outcome = await arriving.ArriveAsync(AuthLink.Read(Landing), Cancellation);

        // Nothing changed hands: the tab is told whom the link would sign in, and is still its own person's.
        outcome.Should().Be(new LinkSignInOutcome(AsksToSignInAs: "mallory@example.test"));
        (_session.Person, _session.AccessToken, _session.Tenant).Should().Be(("enid@example.test", "enids-token", "harbor"));
        _host.Requests.Should().BeEmpty("the API is not called with the link's token before the person has said yes");

        // No: the sign-in is dropped, and a yes that comes after it finds nothing to take.
        arriving.Decline();
        (await arriving.ConfirmAsync(Cancellation)).Should().Be(new LinkSignInOutcome());
        (_session.Person, _session.AccessToken).Should().Be(("enid@example.test", "enids-token"));
    }

    [Fact]
    public async Task A_yes_takes_the_sign_in_that_was_kept_waiting()
    {
        _session.SignIn("enid@example.test", "Enid", "enids-token", DateTimeOffset.UtcNow.AddHours(1), "harbor");
        var arriving = Arriving();
        await arriving.ArriveAsync(AuthLink.Read(Landing), Cancellation);

        var outcome = await arriving.ConfirmAsync(Cancellation);

        outcome.Should().Be(new LinkSignInOutcome(SignedIn: true, ChoosesPassword: true));
        (_session.Person, _session.AccessToken, _session.Tenant).Should().Be(("mallory@example.test", "the-other-accounts-token", null));

        // Once: there is nothing left to say yes to.
        (await arriving.ConfirmAsync(Cancellation)).Should().Be(new LinkSignInOutcome());
    }

    [Fact]
    public async Task A_link_of_the_person_who_is_signed_in_is_taken_without_a_question()
    {
        // The address as the session keeps it, in another case than Auth answers it in.
        _session.SignIn("Mallory@Example.test", "Mallory", "an-older-token", DateTimeOffset.UtcNow.AddHours(1), tenant: null);

        var outcome = await Arriving().ArriveAsync(AuthLink.Read(Landing), Cancellation);

        outcome.SignedIn.Should().BeTrue("nobody changes: the same person, with the token the link brought");
        _session.AccessToken.Should().Be("the-other-accounts-token");
    }

    [Fact]
    public async Task A_link_auth_does_not_vouch_for_signs_nobody_in_and_asks_nothing()
    {
        using var refusing = StubApi.Answering(HttpStatusCode.Unauthorized, """{"code":401,"msg":"invalid JWT"}""");
        _session.SignIn("enid@example.test", "Enid", "enids-token", DateTimeOffset.UtcNow.AddHours(1), "harbor");
        var arriving = new LinkSignIn(new SupabaseLoginClient(new HttpClient(refusing) { BaseAddress = new Uri("http://auth.test/auth/v1/") }, _session), new SampleApi(_host.Client(), _session), _session);

        var outcome = await arriving.ArriveAsync(AuthLink.Read(Landing), Cancellation);

        outcome.Refusal.Should().NotBeNullOrEmpty();
        (outcome.SignedIn, outcome.AsksToSignInAs).Should().Be((false, null));
        (await arriving.ConfirmAsync(Cancellation)).Should().Be(new LinkSignInOutcome(), "nothing was kept waiting");
        _session.Person.Should().Be("enid@example.test");
    }

    public void Dispose()
    {
        _auth.Dispose();
        _host.Dispose();
    }

    private LinkSignIn Arriving()
        => new(
            new SupabaseLoginClient(new HttpClient(_auth, disposeHandler: false) { BaseAddress = new Uri("http://auth.test/auth/v1/") }, _session),
            new SampleApi(_host.Client(), _session),
            _session);
}
