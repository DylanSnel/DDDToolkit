using Examples.Tenancy.Ui.Api;
using Examples.Tenancy.Ui.Auth;
using Examples.Tenancy.Ui.Languages;
using Examples.Tenancy.Ui.Session;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Ui;

/// <summary>
/// A person nobody knows yet gets in through the UI, on Supabase's own Postgres and Auth images: invited from
/// the UI's page, mailed by Auth, signed in by the mail's link, given a password of their own choosing, seated
/// by accepting, and signed in again with that password.
/// </summary>
/// <remarks>
/// The UI's own classes do every step, as its pages call them, against the real host and the real Auth server;
/// the test is the browser, which follows the mail's link and hands the page the address it lands on. It needs
/// Docker and Supabase's images, so it carries the samples' traits, and the host makes the demonstration people
/// users of the stack's Auth server, so the class takes turns with the others that do.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
[Collection(SampleSupabaseStack.AuthUsers)]
public sealed class InvitedByMailTests(SampleSupabaseStack stack)
{
    /// <summary>
    /// Where the host is told the UI's page is. Nothing listens there: the test reads where Auth sends the
    /// browser. Auth sends it on to this address because its host is the one of the stack's site URL, by
    /// name: the same page under <c>127.0.0.1</c> is not on the site to Auth.
    /// </summary>
    private const string AcceptPage = "http://localhost:5091/invitations/accept";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task A_person_invited_by_mail_follows_its_link_chooses_a_password_accepts_and_signs_in_again()
    {
        await using var sample = await SampleOnPostgres.CreateAsync(
            stack,
            Cancellation,
            withAuth: true,
            settings: new Dictionary<string, string> { [SampleIdentityAccounts.AcceptPageSetting] = AcceptPage });
        _ = sample.Host.Server;
        await sample.WaitUntilTheDatabaseIsPastAsync(DateTimeOffset.UtcNow, Cancellation);
        var auth = sample.Auth!;
        var address = $"wren-{Guid.NewGuid():N}@example.test";
        var password = "Pw-" + Guid.NewGuid().ToString("N");

        // Tove invites an address Auth has never seen, from the invitations page.
        var (tove, tovesSession, _) = Ui(sample, auth);
        var toves = (await new DevLoginClient(tove).SignInAsync("tove", Cancellation)).Value!;
        tovesSession.SignIn("tove", "Tove", toves.AccessToken, toves.Expires, Meadow.Slug);
        var invited = await tove.InvitePersonAsync(address, Meadow.Root.Value.ToString(), Meadow.Roles[SampleCatalogue.Surveyor].Value.ToString(), until: null, Cancellation);
        var token = invited.Value!.Token;

        // Auth mails the address one link. It leads back to the page that accepts, with the token behind the '#'
        // of that page's address: nobody has to hand it over.
        var mail = (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle().Subject;
        mail.Text.Should().Contain("type=invite").And.Contain("%23" + token, "the page's address is one value of Auth's link, '#' and all");

        // The browser follows it. Auth proves the address and sends the browser on to the page, keeping what
        // was after the '#' and adding the sign-in behind a '#' of its own. Nothing of it is in a path or a
        // query, so the UI's server is sent none of it.
        var landing = await auth.LandingOfAsync(mail, Cancellation);
        landing.Should().StartWith(AcceptPage + "#" + token + "#access_token=");
        landing[..landing.IndexOf('#')].Should().Be(AcceptPage);

        // The page reads both off the address, and Auth says whose the sign-in is before a session starts.
        var link = AuthLink.Read(landing);
        (link.Token, link.Kind, link.Refusal).Should().Be((token, AuthLink.Invitation, null));
        var (api, session, login) = Ui(sample, auth);
        (await login.SignInWithLinkAsync(link with { AccessToken = link.AccessToken + "x" }, Cancellation))
            .Should().Be(PasswordSignIn.RefusedBecause(UiTexts.For("login.link.refused", "en")), "a sign-in Auth did not sign is nobody's");
        var arrived = (await login.SignInWithLinkAsync(link, Cancellation)).Answer!;
        arrived.User!.Email.Should().Be(address);
        (await api.SeatsOfMineAsync(CallAs.Person(address, arrived.AccessToken), Cancellation)).Value.Should().BeEmpty("the API takes the token, and the person has no seat yet");
        session.SignIn(address, address, arrived.AccessToken, arrived.Expires, tenant: null);

        // No password yet, so the login page would not let the person back in. They choose one; one Auth finds
        // too short is not taken, and is told in the UI's words.
        (await login.SignInAsync(address, password, Cancellation)).Answer.Should().BeNull();
        (await login.ChoosePasswordAsync(session.AccessToken, "abc", Cancellation)).Should().Be(UiTexts.For("login.password.not-taken", "en"));
        (await login.ChoosePasswordAsync(session.AccessToken, password, Cancellation)).Should().BeNull();

        // Then the invitation, with the token the link brought and the name she is shown by there: a seat in meadow.
        var accepted = await api.AcceptInvitationAsync(link.Token, displayName: "Wren", Cancellation);
        accepted.Succeeded.Should().BeTrue("the API answered {0}", accepted.RawBody);
        session.SignOut();

        // And back in by the login page, with the address and the password, to what a surveyor at meadow's root reads.
        var again = (await login.SignInAsync(address, password, Cancellation)).Answer!;
        var seats = (await api.SeatsOfMineAsync(CallAs.Person(address, again.AccessToken), Cancellation)).Value!;
        seats.Should().ContainSingle().Which.Seat.Id.Should().Be(accepted.Value!.SeatId);
        session.SignIn(address, address, again.AccessToken, again.Expires, UiSession.TenantToStartIn(seats));
        session.Tenant.Should().Be(Meadow.Slug);
        var me = (await api.WhoAmIAsync(Cancellation)).Value!;
        me.Seat.DisplayName.Should().Be("Wren");
        me.Placements.Should().ContainSingle().Which.Grants.Should().ContainSingle().Which.Role.Should().Be("Surveyor");
        (await api.VisibleProjectsAsync(cancellationToken: Cancellation)).Value!.Items.Select(project => project.Name).Should().Equal("Garden shed");

        // The mail's link works once. Followed again, Auth sends the browser to the page with its refusal and
        // nothing else: no sign-in, and no token either.
        var second = AuthLink.Read(await auth.LandingOfAsync(mail, Cancellation));
        (second.Token, second.AccessToken).Should().Be((null, null));
        second.Refusal.Should().NotBeNull();
        (await login.SignInWithLinkAsync(second, Cancellation)).Should().Be(PasswordSignIn.RefusedBecause(UiTexts.For("login.link.refused", "en")));
    }

    /// <summary>
    /// Auth's mail lets a person in once, and for a while. A person whose mail went unanswered has an account at
    /// Auth and no way into it, and Auth invites no address that has an account. The invitation kept the id of the
    /// account Auth made, so inviting the address again has Auth mail that account again, with the link of the
    /// new invitation, and the person gets in by that mail.
    /// </summary>
    [Fact]
    public async Task A_person_whose_mail_went_unanswered_is_mailed_again_when_invited_again_and_accepts_by_the_second_mail()
    {
        await using var sample = await SampleOnPostgres.CreateAsync(
            stack,
            Cancellation,
            withAuth: true,
            settings: new Dictionary<string, string> { [SampleIdentityAccounts.AcceptPageSetting] = AcceptPage });
        _ = sample.Host.Server;
        await sample.WaitUntilTheDatabaseIsPastAsync(DateTimeOffset.UtcNow, Cancellation);
        var auth = sample.Auth!;
        var address = $"lark-{Guid.NewGuid():N}@example.test";
        var (root, surveyor) = (Meadow.Root.Value.ToString(), Meadow.Roles[SampleCatalogue.Surveyor].Value.ToString());

        var (tove, tovesSession, _) = Ui(sample, auth);
        var toves = (await new DevLoginClient(tove).SignInAsync("tove", Cancellation)).Value!;
        tovesSession.SignIn("tove", "Tove", toves.AccessToken, toves.Expires, Meadow.Slug);

        // Tove invites the address. Auth makes the account and mails it; the mail is never opened.
        var first = (await tove.InvitePersonAsync(address, root, surveyor, until: null, Cancellation)).Value!;
        var unanswered = (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle().Subject;
        unanswered.Text.Should().Contain("%23" + first.Token);

        // She invites the address again. To her it is answered like the first time; Auth mailed the same account
        // again, and the new mail leads to the page with the token of the new invitation.
        var again = await tove.InvitePersonAsync(address, root, surveyor, until: null, Cancellation);
        again.Succeeded.Should().BeTrue("the API answered {0}", again.RawBody);
        var second = again.Value!;
        second.Token.Should().NotBe(first.Token);
        var mails = await auth.Mail.SentToAsync(address, atLeast: 2, Cancellation);
        mails.Should().HaveCount(2, "the account whose invitation went unanswered is mailed again");
        var mail = mails.Should().ContainSingle(sent => sent.Text.Contains("%23" + second.Token, StringComparison.Ordinal)).Subject;

        // The person follows the second mail, is signed in by it, and accepts with the token it brought.
        var link = AuthLink.Read(await auth.LandingOfAsync(mail, Cancellation));
        (link.Token, link.Kind, link.Refusal).Should().Be((second.Token, AuthLink.Invitation, null));
        var (api, session, login) = Ui(sample, auth);
        var arrived = (await login.SignInWithLinkAsync(link, Cancellation)).Answer!;
        arrived.User!.Email.Should().Be(address);
        session.SignIn(address, address, arrived.AccessToken, arrived.Expires, tenant: null);

        var accepted = await api.AcceptInvitationAsync(link.Token, displayName: "Lark", Cancellation);
        accepted.Succeeded.Should().BeTrue("the API answered {0}", accepted.RawBody);
        (await api.SeatsOfMineAsync(CallAs.Person(address, arrived.AccessToken), Cancellation)).Value!
            .Should().ContainSingle().Which.Seat.Id.Should().Be(accepted.Value!.SeatId);

        // The mail that went unanswered lets nobody in any more: Auth's newer link took its place.
        var stale = AuthLink.Read(await auth.LandingOfAsync(unanswered, Cancellation));
        (stale.AccessToken, stale.Refusal is not null).Should().Be((null, true));
    }

    /// <summary>
    /// What inviting again does not mend: a person who followed the mail has proven the address, and Auth invites
    /// no proven account. No second mail comes. Whoever invited hands the new link over, and the person accepts
    /// with the sign-in the first mail gave them, for as long as that lasts; the sample has no way back in after
    /// it for somebody who chose no password.
    /// </summary>
    [Fact]
    public async Task A_person_who_followed_the_mail_is_not_mailed_again_and_accepts_the_new_invitation_while_signed_in()
    {
        await using var sample = await SampleOnPostgres.CreateAsync(
            stack,
            Cancellation,
            withAuth: true,
            settings: new Dictionary<string, string> { [SampleIdentityAccounts.AcceptPageSetting] = AcceptPage });
        _ = sample.Host.Server;
        await sample.WaitUntilTheDatabaseIsPastAsync(DateTimeOffset.UtcNow, Cancellation);
        var auth = sample.Auth!;
        var address = $"lark-{Guid.NewGuid():N}@example.test";
        var (root, surveyor) = (Meadow.Root.Value.ToString(), Meadow.Roles[SampleCatalogue.Surveyor].Value.ToString());

        var (tove, tovesSession, _) = Ui(sample, auth);
        var toves = (await new DevLoginClient(tove).SignInAsync("tove", Cancellation)).Value!;
        tovesSession.SignIn("tove", "Tove", toves.AccessToken, toves.Expires, Meadow.Slug);

        // Invited, mailed, and the person follows the link: signed in, and no password chosen.
        (await tove.InvitePersonAsync(address, root, surveyor, until: null, Cancellation)).Succeeded.Should().BeTrue();
        var mail = (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle().Subject;
        var (api, session, login) = Ui(sample, auth);
        var arrived = (await login.SignInWithLinkAsync(AuthLink.Read(await auth.LandingOfAsync(mail, Cancellation)), Cancellation)).Answer!;
        session.SignIn(address, address, arrived.AccessToken, arrived.Expires, tenant: null);

        // Invited again: answered as before, and nobody is mailed. Auth mails before it answers, so a second
        // mail would be there by now.
        var again = await tove.InvitePersonAsync(address, root, surveyor, until: null, Cancellation);
        again.Succeeded.Should().BeTrue("the API answered {0}", again.RawBody);
        (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle("Auth invites no account whose address is proven");

        // The new token reaches the person from whoever invited, and the sign-in they still have accepts it.
        var accepted = await api.AcceptInvitationAsync(again.Value!.Token, displayName: "Lark", Cancellation);
        accepted.Succeeded.Should().BeTrue("the API answered {0}", accepted.RawBody);
    }

    /// <summary>The UI's client of the host's API, its session and its sign-in at the stack's Auth server, as one tab has them.</summary>
    private static (SampleApi Api, UiSession Session, SupabaseLoginClient Login) Ui(SampleOnPostgres sample, SupabaseAuthServer auth)
    {
        var session = new UiSession();
        return (
            new SampleApi(new HttpClient(sample.Host.Server.CreateHandler()) { BaseAddress = sample.Host.Server.BaseAddress }, session),
            session,
            new SupabaseLoginClient(new HttpClient { BaseAddress = auth.Url }, session));
    }
}
