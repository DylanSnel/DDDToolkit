using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using DDDToolkit.Identity;
using FluentAssertions;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// Supabase Auth behind the identity port, which is all application code sees: an account before its first
/// sign-in, an invitation by e-mail, an invitation of an account by its id, whether an account was ever
/// used, and its deletion. What Auth itself does with these calls, the race of two invitations among it, is
/// asked of Auth in <see cref="SupabaseIdentityAccountsOnAuthTests"/>.
/// </summary>
public sealed class SupabaseIdentityAccountsTests
{
    private const string Address = "ada.lindqvist@example.test";
    private const string InvitedAt = "2026-03-02T09:15:54.013489931Z";

    private static readonly Guid Ada = Guid.Parse("8051a7e8-8599-4ad8-b169-68dc96beb0c5");
    private static readonly Guid SomebodyElse = Guid.Parse("3d0f4c0e-6f0b-4a43-9a53-0c3f0f7f2f11");

    private static readonly string UsersUrl = StubAuthServer.Url + "/admin/users";
    private static readonly string InviteUrl = StubAuthServer.Url + "/invite";
    private static readonly string AdaUrl = UsersUrl + "/" + Ada;

    private readonly StubAuthServer _auth = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private IIdentityAccounts Accounts => new SupabaseIdentityAccounts(_auth.Admin());

    // ---- An invitation of an address ------------------------------------------------------------------------

    [Fact]
    public async Task An_invitation_makes_the_account_first_and_has_it_mailed_after()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(Ada, Address, invitedAt: InvitedAt));

        var outcome = await Accounts.InviteByEmailAsync(Address, new Uri("https://crews.example.test/welcome"), Cancellation);

        outcome.Should().Be(new IdentityAccountOutcome.Created(Ada), "the application can give this id access before the person signs in");
        _auth.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri)).Should().Equal(
            (HttpMethod.Post, UsersUrl),
            (HttpMethod.Post, InviteUrl + "?redirect_to=https%3A%2F%2Fcrews.example.test%2Fwelcome"));

        // Making a user is the one call Auth refuses for an address that has any user, so it goes first. It
        // carries no id, which leaves the id to Auth, and no password: the link in the mail is the way in.
        _auth.Requests[0].Members.Should().Equal("email");
        _auth.Requests[1].Members.Should().Equal("email");
    }

    [Fact]
    public async Task An_invitation_without_a_redirect_leaves_the_landing_page_to_auth()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(Ada, Address, invitedAt: InvitedAt));

        await Accounts.InviteByEmailAsync(Address, signInRedirect: null, Cancellation);

        _auth.Requests[1].Uri.Should().Be(new Uri(InviteUrl));
    }

    /// <summary>
    /// This test used to say the opposite: an account that was made earlier and never proved its address was
    /// invited again and answered <see cref="IdentityAccountOutcome.Created"/> under the id it already had,
    /// because that is what Auth's own invitation does. But "made earlier" does not say by whom. In a project
    /// that lets anybody sign up, a stranger can have registered the address and chosen its password, and an
    /// application that gave the answered id access would have given it to the stranger. So an invitation
    /// now answers <see cref="IdentityAccountOutcome.Created"/> only for an account it made itself, a moment
    /// ago, and an account that was there already is a taken address, whoever made it.
    /// </summary>
    [Fact]
    public async Task An_account_that_was_there_already_is_a_taken_address_and_is_not_mailed()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(422, AuthAnswers.Refusal(422, "email_exists", AuthAnswers.AddressExistsText));
        var accounts = Accounts;

        var created = await accounts.CreateAsync(Ada, Address, Cancellation);
        var invited = await accounts.InviteByEmailAsync(Address, signInRedirect: null, Cancellation);

        created.Should().Be(new IdentityAccountOutcome.Created(Ada));
        invited.Should().BeOfType<IdentityAccountOutcome.AddressTaken>("also for an account the application made itself: it invites that one by its id");
        _auth.Requests.Select(request => request.Uri.AbsoluteUri).Should().Equal([UsersUrl, UsersUrl], "Auth's invitation, which would mail whoever is there, was never asked");
    }

    [Fact]
    public async Task A_taken_address_is_an_ordinary_answer_that_says_nothing_about_the_account()
    {
        _auth.Answers(422, AuthAnswers.Refusal(422, "email_exists", AuthAnswers.AddressExistsText));
        _auth.Answers(422, AuthAnswers.Refusal(422, "email_exists", AuthAnswers.AddressExistsText));
        var accounts = Accounts;

        // No exception for either: the application goes on in its own way, and what it tells the person
        // who asked is the same as for a new address.
        var invited = await accounts.InviteByEmailAsync(Address, signInRedirect: null, Cancellation);
        var created = await accounts.CreateAsync(Ada, Address, Cancellation);

        invited.Should().BeOfType<IdentityAccountOutcome.AddressTaken>();
        created.Should().BeOfType<IdentityAccountOutcome.AddressTaken>();
        typeof(IdentityAccountOutcome.AddressTaken).GetProperties().Should().BeEmpty("not which account, nor since when: nothing looks an account up by its address");
        invited.ToString().Should().NotContain("lindqvist");
        _auth.Requests.Should().HaveCount(2, "and nothing went back to Auth to find out more");
    }

    [Fact]
    public async Task An_invitation_that_loses_the_race_for_its_address_finds_it_taken()
    {
        // What Auth answers the second of two calls that both found the address free: the database's refusal
        // of a duplicate, not its own "email_exists".
        _auth.Answers(500, AuthAnswers.DatabaseRefusal("23505", "duplicate key value violates unique constraint \"users_email_partial_key\"", $"Key (email)=({Address}) already exists."));

        var outcome = await Accounts.InviteByEmailAsync(Address, signInRedirect: null, Cancellation);

        outcome.Should().BeOfType<IdentityAccountOutcome.AddressTaken>("the other call made the account, and this one has nothing of its own to give access to");
        _auth.Request.Uri.AbsoluteUri.Should().Be(UsersUrl, "the account the other call made is the other call's to mail");
    }

    [Fact]
    public async Task An_invitation_that_mailed_another_account_than_the_one_it_made_answers_no_id()
    {
        // The account this made was removed and the address registered anew between the two requests. Auth
        // mailed whoever is there now, and that is not an account this call can vouch for.
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(SomebodyElse, Address, invitedAt: InvitedAt));

        var outcome = await Accounts.InviteByEmailAsync(Address, signInRedirect: null, Cancellation);

        outcome.Should().BeOfType<IdentityAccountOutcome.AddressTaken>();
        _auth.Requests.Should().HaveCount(2, "the other account is not this call's to delete either");
    }

    [Fact]
    public async Task An_invitation_whose_account_was_proven_in_between_answers_no_id()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(422, AuthAnswers.Refusal(422, "email_exists", AuthAnswers.AddressExistsText));

        var outcome = await Accounts.InviteByEmailAsync(Address, signInRedirect: null, Cancellation);

        outcome.Should().BeOfType<IdentityAccountOutcome.AddressTaken>("nobody was mailed, and an account somebody is in is not deleted");
        _auth.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_failure_while_the_account_is_made_is_thrown_as_it_is_and_nothing_more_is_asked()
    {
        // Auth chooses the id, so without an answer to the first request there is no id to mail or to delete
        // by, whether Auth made the user or not.
        _auth.Fails(new HttpRequestException("Connection reset (project.example.test:443)"));

        await Accounts.Invoking(a => a.InviteByEmailAsync(Address, signInRedirect: null, Cancellation))
            .Should().ThrowAsync<HttpRequestException>().WithMessage("Connection reset*");

        _auth.Request.Uri.AbsoluteUri.Should().Be(UsersUrl, "neither an invitation nor a deletion follows a user nobody has the id of");
    }

    // ---- An invitation whose mail could not be sent ---------------------------------------------------------

    [Theory]
    [InlineData(429, "over_email_send_rate_limit")]
    [InlineData(500, "unexpected_failure")]
    public async Task An_account_that_could_not_be_mailed_is_taken_away_again(int status, string code)
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(status, AuthAnswers.Refusal(status, code, "Error sending invite email"));
        _auth.Answers(200, "{}");

        var refused = await Accounts.Invoking(a => a.InviteByEmailAsync(Address, signInRedirect: null, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>("the failure is the caller's, as Auth gave it");

        refused.Which.Status.Should().Be(status);
        refused.Which.AuthErrorCode.Should().Be(code);

        // Left in place, the account would keep the address taken under an id nobody has, and the invitation
        // could never be tried again.
        _auth.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri)).Should().Equal(
            (HttpMethod.Post, UsersUrl),
            (HttpMethod.Post, InviteUrl),
            (HttpMethod.Delete, AdaUrl));
    }

    [Fact]
    public async Task A_connection_lost_while_mailing_takes_the_account_away_again()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Fails(new HttpRequestException("Connection reset (project.example.test:443)"));
        _auth.Answers(200, "{}");

        await Accounts.Invoking(a => a.InviteByEmailAsync(Address, signInRedirect: null, Cancellation))
            .Should().ThrowAsync<HttpRequestException>();

        _auth.Requests[^1].Method.Should().Be(HttpMethod.Delete);
        _auth.Requests[^1].Uri.AbsoluteUri.Should().Be(AdaUrl);
    }

    [Fact]
    public async Task A_caller_that_stops_waiting_while_mailing_still_has_the_account_taken_away()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Fails(() =>
        {
            caller.Cancel();
            return new OperationCanceledException(caller.Token);
        });
        _auth.Answers(200, "{}");

        await Accounts.Invoking(a => a.InviteByEmailAsync(Address, signInRedirect: null, caller.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        // The stand-in refuses a request whose token is cancelled, as a real connection does, so the
        // deletion arriving at all says it did not wait on the caller's token.
        _auth.Requests.Should().HaveCount(3);
        _auth.Requests[2].Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task An_account_that_could_be_neither_mailed_nor_taken_away_is_reported_with_its_id()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Fails(new HttpRequestException("Connection reset (project.example.test:443)"));
        _auth.Fails(new HttpRequestException("Connection refused (project.example.test:443)"));

        var left = await Accounts.Invoking(a => a.InviteByEmailAsync(Address, signInRedirect: null, Cancellation))
            .Should().ThrowAsync<IdentityAccountLeftBehindException>();

        // Nothing looks an account up by its address, so the id is known here and nowhere else: with it the
        // application deletes the account, or invites it, once Auth answers again.
        left.Which.Identity.Should().Be(Ada);
        left.Which.Message.Should().Contain(Ada.ToString());
        left.Which.InnerException.Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Select(inner => inner.Message).Should().Equal(
                "Connection reset (project.example.test:443)",
                "Connection refused (project.example.test:443)");
        left.Which.ToString().Should().NotContain("lindqvist", "the address is in no message on the way");
    }

    // ---- An account under an id of the application's own ----------------------------------------------------

    [Fact]
    public async Task An_account_is_made_under_the_given_id_and_mails_nobody()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));

        var outcome = await Accounts.CreateAsync(Ada, Address, Cancellation);

        outcome.Should().Be(new IdentityAccountOutcome.Created(Ada));
        _auth.Request.Uri.Should().Be(new Uri(UsersUrl), "making a user through the admin API sends no mail; an invitation does");
        _auth.Request.Members.Should().Equal(["email", "id"], "no password, and the address stays unproven until the person signs in");
        _auth.Request.Json.GetProperty("id").GetGuid().Should().Be(Ada);
    }

    [Fact]
    public async Task A_taken_id_is_an_error_and_not_a_taken_address()
    {
        _auth.Answers(500, AuthAnswers.DatabaseRefusal("23505", "duplicate key value violates unique constraint \"users_pkey\"", $"Key (id)=({Ada}) already exists."));
        _auth.Answers(200, AuthAnswers.User(Ada, "somebody.else@example.test"));

        var refused = await Accounts.Invoking(a => a.CreateAsync(Ada, Address, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>("two people under one id is a mistake of the caller's, not something to carry on from");

        refused.Which.AuthErrorCode.Should().Be("23505");
        _auth.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri)).Should().Equal(
            [(HttpMethod.Post, UsersUrl), (HttpMethod.Get, AdaUrl)],
            "the database refuses a duplicate id and a duplicate address alike, so the id is asked after");
    }

    [Fact]
    public async Task An_account_that_loses_the_race_for_its_address_finds_it_taken()
    {
        _auth.Answers(500, AuthAnswers.DatabaseRefusal("23505", "duplicate key value violates unique constraint \"users_email_partial_key\"", $"Key (email)=({Address}) already exists."));
        _auth.Answers(404, AuthAnswers.Refusal(404, "user_not_found", "User not found"));

        var outcome = await Accounts.CreateAsync(Ada, Address, Cancellation);

        outcome.Should().BeOfType<IdentityAccountOutcome.AddressTaken>("the id has no user, so the duplicate was the address: another call registered it at the same moment");
    }

    [Fact]
    public async Task An_account_of_the_applications_own_is_invited_by_its_id()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(Ada, Address, invitedAt: InvitedAt));

        var outcome = await Accounts.InviteAccountAsync(Ada, new Uri("https://crews.example.test/welcome"), Cancellation);

        outcome.Should().Be(IdentityInvitation.Sent);
        _auth.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri)).Should().Equal(
            (HttpMethod.Get, AdaUrl),
            (HttpMethod.Post, InviteUrl + "?redirect_to=https%3A%2F%2Fcrews.example.test%2Fwelcome"));
        _auth.Requests[1].Json.GetProperty("email").GetString().Should().Be(Address, "the application gave an id, and Auth is told the address it has for that id");
    }

    [Fact]
    public async Task Inviting_an_account_that_is_not_there_mails_nobody()
    {
        _auth.Answers(404, AuthAnswers.Refusal(404, "user_not_found", "User not found"));

        var outcome = await Accounts.InviteAccountAsync(Ada, signInRedirect: null, Cancellation);

        outcome.Should().Be(IdentityInvitation.NoSuchAccount);
        _auth.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Inviting_an_account_whose_address_is_proven_mails_nobody()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address, emailConfirmedAt: "2026-03-03T08:00:00.101216Z", lastSignInAt: "2026-03-04T07:30:15.182151Z"));
        _auth.Answers(422, AuthAnswers.Refusal(422, "email_exists", AuthAnswers.AddressExistsText));

        var outcome = await Accounts.InviteAccountAsync(Ada, signInRedirect: null, Cancellation);

        outcome.Should().Be(IdentityInvitation.AlreadyProven, "its person signs in the ordinary way");
    }

    [Fact]
    public async Task Inviting_an_account_never_answers_for_another_account_at_its_address()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(SomebodyElse, Address, invitedAt: InvitedAt));

        var refused = await Accounts.Invoking(a => a.InviteAccountAsync(Ada, signInRedirect: null, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>("the application asked for one account and another was mailed: that is not Sent");

        refused.Which.Message.Should().Contain(Ada.ToString()).And.NotContain(SomebodyElse.ToString()).And.NotContain("lindqvist");
    }

    // ---- Refusals, reading and deleting ---------------------------------------------------------------------

    [Theory]
    [InlineData(429, "over_email_send_rate_limit")]
    [InlineData(400, "validation_failed")]
    [InlineData(403, "not_admin")]
    public async Task Any_other_refusal_stays_an_error(int status, string code)
    {
        _auth.Answers(status, AuthAnswers.Refusal(status, code, "No"));
        _auth.Answers(status, AuthAnswers.Refusal(status, code, "No"));
        _auth.Answers(status, AuthAnswers.Refusal(status, code, "No"));
        var accounts = Accounts;

        await accounts.Invoking(a => a.InviteByEmailAsync(Address, signInRedirect: null, Cancellation)).Should().ThrowAsync<SupabaseAuthAdminException>();
        await accounts.Invoking(a => a.CreateAsync(Ada, Address, Cancellation)).Should().ThrowAsync<SupabaseAuthAdminException>();
        await accounts.Invoking(a => a.InviteAccountAsync(Ada, signInRedirect: null, Cancellation)).Should().ThrowAsync<SupabaseAuthAdminException>();

        _auth.Requests.Should().HaveCount(3, "each stopped at the request that was refused");
    }

    [Fact]
    public async Task An_account_says_whether_it_was_ever_used()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address, invitedAt: InvitedAt));
        _auth.Answers(200, AuthAnswers.User(Ada, Address, emailConfirmedAt: "2026-03-03T08:00:00.101216Z", lastSignInAt: "2026-03-04T07:30:15.182151Z"));
        var accounts = Accounts;

        var waiting = await accounts.FindAsync(Ada, Cancellation);
        var used = await accounts.FindAsync(Ada, Cancellation);

        waiting.Should().Be(new IdentityAccount(Ada, HasSignedIn: false), "an invitation that was sent is not a sign-in");
        used.Should().Be(new IdentityAccount(Ada, HasSignedIn: true));
    }

    [Fact]
    public async Task A_missing_account_is_null()
    {
        _auth.Answers(404, AuthAnswers.Refusal(404, "user_not_found", "User not found"));

        (await Accounts.FindAsync(Ada, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task Deleting_an_account_is_safe_to_repeat()
    {
        _auth.Answers(200, "{}");
        _auth.Answers(404, AuthAnswers.Refusal(404, "user_not_found", "User not found"));
        var accounts = Accounts;

        var first = await accounts.DeleteAsync(Ada, Cancellation);
        var again = await accounts.DeleteAsync(Ada, Cancellation);

        first.Should().BeTrue();
        again.Should().BeFalse("an erasure that is run again finds the account gone, and that is not an error");
        _auth.Requests.Should().AllSatisfy(request => request.Method.Should().Be(HttpMethod.Delete));
    }

    // ---- What is refused before anything is sent ------------------------------------------------------------

    [Fact]
    public async Task The_page_an_invitation_leads_to_has_to_be_an_absolute_url()
    {
        var accounts = Accounts;
        var relative = new Uri("/welcome", UriKind.Relative);

        (await accounts.Invoking(a => a.InviteByEmailAsync(Address, relative, Cancellation)).Should().ThrowAsync<ArgumentException>())
            .WithParameterName("signInRedirect");
        (await accounts.Invoking(a => a.InviteAccountAsync(Ada, relative, Cancellation)).Should().ThrowAsync<ArgumentException>())
            .WithParameterName("signInRedirect");

        _auth.Requests.Should().BeEmpty("least of all the request that makes the account");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_id_or_address_is_refused_by_its_own_name_before_anything_is_sent(string address)
    {
        var accounts = Accounts;

        (await accounts.Invoking(a => a.CreateAsync(Guid.Empty, Address, Cancellation)).Should().ThrowAsync<ArgumentException>())
            .WithParameterName("identity", "left to Auth, an empty id would be one it chooses itself");
        (await accounts.Invoking(a => a.CreateAsync(Ada, address, Cancellation)).Should().ThrowAsync<ArgumentException>())
            .WithParameterName("address");
        (await accounts.Invoking(a => a.InviteByEmailAsync(address, signInRedirect: null, Cancellation)).Should().ThrowAsync<ArgumentException>())
            .WithParameterName("address");
        (await accounts.Invoking(a => a.InviteAccountAsync(Guid.Empty, signInRedirect: null, Cancellation)).Should().ThrowAsync<ArgumentException>())
            .WithParameterName("identity");

        _auth.Requests.Should().BeEmpty();
    }

    // ---- The port's shape -----------------------------------------------------------------------------------

    [Fact]
    public void The_port_carries_no_address_back()
    {
        typeof(IdentityAccount).GetProperties().Select(property => property.Name).Should().BeEquivalentTo("Id", "HasSignedIn");
        typeof(IdentityAccountOutcome.Created).GetProperties().Select(property => property.Name).Should().BeEquivalentTo("Identity");
        typeof(IIdentityAccounts).GetMethod(nameof(IIdentityAccounts.InviteAccountAsync))!.GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Equal([typeof(Guid), typeof(Uri), typeof(CancellationToken)], "an account is invited by its id, and the provider knows the address");
    }

    [Fact]
    public void The_outcome_is_one_of_two()
    {
        // Closed, so application code that switches over the two has covered every case an adapter can answer.
        typeof(IdentityAccountOutcome).IsAbstract.Should().BeTrue();
        typeof(IdentityAccountOutcome).GetConstructors().Should().BeEmpty();
        typeof(IdentityAccountOutcome).GetNestedTypes().Should().BeEquivalentTo([typeof(IdentityAccountOutcome.Created), typeof(IdentityAccountOutcome.AddressTaken)]);
    }

    [Fact]
    public void An_invitation_of_an_account_that_was_never_decided_reads_as_nobody_mailed()
    {
        Enum.GetValues<IdentityInvitation>().Should().Equal(IdentityInvitation.NoSuchAccount, IdentityInvitation.AlreadyProven, IdentityInvitation.Sent);
        default(IdentityInvitation).Should().Be(IdentityInvitation.NoSuchAccount, "a value nobody set must not read as a mail that was sent");
    }
}
