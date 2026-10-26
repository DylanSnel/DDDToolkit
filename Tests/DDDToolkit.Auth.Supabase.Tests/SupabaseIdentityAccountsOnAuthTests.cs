using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using DDDToolkit.Identity;
using FluentAssertions;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// The identity port against Supabase's own Auth server, in a project where anybody can sign up: the
/// questions whose answers are Auth's and that a stand-in would only repeat back. Who gets in after an
/// invitation, what an address a stranger registered first is answered with, which of several invitations
/// for one address makes the account, and what a failed mail leaves behind.
/// </summary>
/// <remarks>
/// Every test uses addresses of its own, so the tests share one Auth server and none of them sees
/// another's accounts or mails.
/// </remarks>
public sealed class SupabaseIdentityAccountsOnAuthTests(SupabaseAuthStack stack)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ---- An invitation of an address ------------------------------------------------------------------------

    [Fact]
    public async Task An_invitation_answers_the_id_of_the_account_it_made_and_the_person_gets_in_under_it()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var accounts = SupabaseAuthServer.Accounts(admin);
        var address = NewAddress("invited");

        var created = (await accounts.InviteByEmailAsync(address, signInRedirect: null, Cancellation))
            .Should().BeOfType<IdentityAccountOutcome.Created>().Subject;

        (await accounts.FindAsync(created.Identity, Cancellation)).Should().Be(new IdentityAccount(created.Identity, HasSignedIn: false), "the account is there at once and waits for its person");

        var mail = (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle("making the account mails nobody, and inviting it mails once").Subject;
        (await auth.FollowLinkAsync(mail, Cancellation)).Should().Be(created.Identity, "access given to the answered id is access for whoever opens the mail");
        (await accounts.FindAsync(created.Identity, Cancellation)).Should().Be(new IdentityAccount(created.Identity, HasSignedIn: true));

        // From here on the address is somebody's account in use, and is taken like any other.
        (await accounts.InviteByEmailAsync(address, signInRedirect: null, Cancellation)).Should().BeOfType<IdentityAccountOutcome.AddressTaken>();
        (await auth.Mail.SentToAsync(address, atLeast: 0, Cancellation)).Should().ContainSingle("nobody is mailed about an account they are in");
    }

    [Fact]
    public async Task An_address_a_stranger_registered_first_is_taken_and_is_not_mailed_an_invitation()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var accounts = SupabaseAuthServer.Accounts(admin);
        var address = NewAddress("registered-first");

        // Somebody who is not the person signs up with the person's address and a password they chose. They
        // cannot get in, since the address is not theirs to prove, but the account is there and waits.
        await auth.SignUpAsync(address, NewPassword(), Cancellation);

        var outcome = await accounts.InviteByEmailAsync(address, signInRedirect: null, Cancellation);

        outcome.Should().BeOfType<IdentityAccountOutcome.AddressTaken>("the account that waits was not made by this call, so it has no id to give access to");
        (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle("the sign-up's own mail and no invitation")
            .Which.Text.Should().Contain("type=signup");
    }

    /// <summary>
    /// Why the adapter makes the account itself and does not leave it to Auth's invitation. Auth invites a
    /// user who was there already and never proved the address, under the id that user had; the person
    /// proves the address by following the link; and the password the stranger chose at the sign-up then
    /// opens the very account the application was told to give access to.
    /// </summary>
    [Fact]
    public async Task Auths_own_invitation_of_an_address_a_stranger_registered_first_lets_the_stranger_in()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var address = NewAddress("taken-over");
        var strangersPassword = NewPassword();
        await auth.SignUpAsync(address, strangersPassword, Cancellation);

        var sent = (await admin.InviteByEmailAsync(address, options: null, Cancellation))
            .Should().BeOfType<SupabaseInvitationResult.Sent>("to Auth this is a user who still waits").Subject;
        var mails = await auth.Mail.SentToAsync(address, atLeast: 2, Cancellation);
        var person = await auth.FollowLinkAsync(mails[0], Cancellation);

        person.Should().Be(sent.UserId);
        (await auth.SignInAsync(address, strangersPassword, Cancellation)).Should().Be(sent.UserId, "the stranger's password was kept, and is good once the person has proven the address for them");
    }

    [Fact]
    public async Task A_strangers_sign_up_after_the_invitation_does_not_let_them_in()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var accounts = SupabaseAuthServer.Accounts(admin);
        var address = NewAddress("signed-up-after");
        var strangersPassword = NewPassword();

        var created = (await accounts.InviteByEmailAsync(address, signInRedirect: null, Cancellation))
            .Should().BeOfType<IdentityAccountOutcome.Created>().Subject;

        // Auth answers the stranger as it answers any sign-up, and mails the address again, but it does not
        // take the password: the account is not the stranger's to change.
        await auth.SignUpAsync(address, strangersPassword, Cancellation);
        var mails = await auth.Mail.SentToAsync(address, atLeast: 2, Cancellation);
        mails.Should().HaveCount(2);

        (await auth.FollowLinkAsync(mails[0], Cancellation)).Should().Be(created.Identity, "the person proves the address with the newest mail, and is in under the id the application was given");
        (await auth.SignInAsync(address, strangersPassword, Cancellation)).Should().BeNull("the password the stranger chose was never the account's");
    }

    [Fact]
    public async Task An_account_the_application_invited_is_a_taken_address_to_a_second_invitation()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var accounts = SupabaseAuthServer.Accounts(admin);
        var address = NewAddress("invited-twice");

        var created = (await accounts.InviteByEmailAsync(address, signInRedirect: null, Cancellation))
            .Should().BeOfType<IdentityAccountOutcome.Created>().Subject;
        var again = await accounts.InviteByEmailAsync(address, signInRedirect: null, Cancellation);

        again.Should().BeOfType<IdentityAccountOutcome.AddressTaken>("an address says nothing about who made its account, so even the application's own is not answered a second time");
        (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle();

        // The application kept the id, and that is what it mails the account again by.
        (await accounts.InviteAccountAsync(created.Identity, signInRedirect: null, Cancellation)).Should().Be(IdentityInvitation.Sent);
        (await auth.Mail.SentToAsync(address, atLeast: 2, Cancellation)).Should().HaveCount(2);
    }

    // ---- Several calls for one address at the same moment ---------------------------------------------------

    [Fact]
    public async Task Of_several_invitations_for_one_address_at_once_one_makes_the_account_and_the_others_find_it_taken()
    {
        const int Rounds = 8;
        const int AtOnce = 5;

        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var accounts = SupabaseAuthServer.Accounts(admin);

        for (var round = 0; round < Rounds; round++)
        {
            var address = NewAddress("raced");

            var outcomes = await Task.WhenAll(Enumerable.Range(0, AtOnce)
                .Select(_ => Task.Run(() => accounts.InviteByEmailAsync(address, signInRedirect: null, Cancellation), Cancellation)));

            var created = outcomes.OfType<IdentityAccountOutcome.Created>().Should().ContainSingle("one address is one account, and one call made it").Subject;
            outcomes.OfType<IdentityAccountOutcome.AddressTaken>().Should().HaveCount(AtOnce - 1, "losing the race is a taken address, not an error and not a second id");

            // Auth mails before it answers, so every mail of these calls is there by now.
            var mail = (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle("only the call that made the account has it mailed").Subject;
            (await auth.FollowLinkAsync(mail, Cancellation)).Should().Be(created.Identity, "and the one mail leads into the one account whose id was answered");
        }
    }

    /// <summary>
    /// What the adapter's answer for the loser of a race rests on. Auth looks for the address before it
    /// writes, so of several calls that all found nothing the database lets one write, and Auth passes the
    /// database's refusal of the others on with its code. A call that arrives a moment later finds the user
    /// instead, and Auth's own invitation then mails that user again under the same id.
    /// </summary>
    [Fact]
    public async Task Of_several_of_auths_own_invitations_at_once_the_database_refuses_the_late_writers_as_duplicates()
    {
        const int Rounds = 8;
        const int AtOnce = 5;

        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var refusals = new List<SupabaseAuthAdminException>();

        for (var round = 0; round < Rounds; round++)
        {
            var address = NewAddress("raced-at-auth");

            var answers = await Task.WhenAll(Enumerable.Range(0, AtOnce).Select(_ => Task.Run(
                async () =>
                {
                    try
                    {
                        return (Sent: await admin.InviteByEmailAsync(address, options: null, Cancellation) as SupabaseInvitationResult.Sent, Refused: null as SupabaseAuthAdminException);
                    }
                    catch (SupabaseAuthAdminException refused)
                    {
                        return (Sent: null, Refused: refused);
                    }
                },
                Cancellation)));

            answers.Where(answer => answer.Sent is not null).Select(answer => answer.Sent!.UserId).Distinct()
                .Should().ContainSingle("one address is one user, however many calls asked");
            refusals.AddRange(answers.Where(answer => answer.Refused is not null).Select(answer => answer.Refused!));
        }

        refusals.Should().NotBeEmpty("calls that arrive together cannot all have made the user")
            .And.AllSatisfy(refused =>
            {
                refused.DuplicateKey.Should().BeTrue();
                refused.AddressAlreadyRegistered.Should().BeFalse("Auth did not find the address: the database refused the second write");
            });
    }

    [Fact]
    public async Task Of_several_accounts_made_for_one_address_at_once_one_is_made_and_the_others_find_it_taken()
    {
        const int Rounds = 8;
        const int AtOnce = 5;

        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var accounts = SupabaseAuthServer.Accounts(admin);

        for (var round = 0; round < Rounds; round++)
        {
            var address = NewAddress("made-at-once");
            var ids = Enumerable.Range(0, AtOnce).Select(_ => Guid.NewGuid()).ToArray();

            var outcomes = await Task.WhenAll(ids
                .Select(id => Task.Run(() => accounts.CreateAsync(id, address, Cancellation), Cancellation)));

            var created = outcomes.OfType<IdentityAccountOutcome.Created>().Should().ContainSingle().Subject;
            outcomes.OfType<IdentityAccountOutcome.AddressTaken>().Should().HaveCount(AtOnce - 1);
            ids.Should().Contain(created.Identity);

            foreach (var id in ids)
            {
                var found = await accounts.FindAsync(id, Cancellation);
                (found is not null).Should().Be(id == created.Identity, "only the id that was answered has an account");
            }

            (await auth.Mail.SentToAsync(address, atLeast: 0, Cancellation)).Should().BeEmpty("making an account mails nobody");
        }
    }

    // ---- An account under an id of the application's own ----------------------------------------------------

    [Fact]
    public async Task An_account_made_under_an_id_of_the_applications_own_is_invited_by_that_id()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var accounts = SupabaseAuthServer.Accounts(admin);
        var address = NewAddress("own-id");
        var id = Guid.NewGuid();

        (await accounts.CreateAsync(id, address, Cancellation)).Should().Be(new IdentityAccountOutcome.Created(id));
        (await auth.Mail.SentToAsync(address, atLeast: 0, Cancellation)).Should().BeEmpty();

        (await accounts.InviteAccountAsync(id, signInRedirect: null, Cancellation)).Should().Be(IdentityInvitation.Sent);
        (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle();

        // An invitation that went unanswered is sent again the same way, for as long as the account waits.
        (await accounts.InviteAccountAsync(id, signInRedirect: null, Cancellation)).Should().Be(IdentityInvitation.Sent);
        var mails = await auth.Mail.SentToAsync(address, atLeast: 2, Cancellation);
        mails.Should().HaveCount(2);

        (await auth.FollowLinkAsync(mails[0], Cancellation)).Should().Be(id, "the person is in under the id the application chose before they existed at Auth");

        (await accounts.InviteAccountAsync(id, signInRedirect: null, Cancellation)).Should().Be(IdentityInvitation.AlreadyProven);
        (await auth.Mail.SentToAsync(address, atLeast: 0, Cancellation)).Should().HaveCount(2, "nobody is mailed an invitation to an account they are in");
    }

    [Fact]
    public async Task A_strangers_sign_up_between_making_an_account_and_inviting_it_does_not_let_them_in()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var accounts = SupabaseAuthServer.Accounts(admin);
        var address = NewAddress("own-id-signed-up");
        var strangersPassword = NewPassword();
        var id = Guid.NewGuid();

        (await accounts.CreateAsync(id, address, Cancellation)).Should().Be(new IdentityAccountOutcome.Created(id));
        await auth.SignUpAsync(address, strangersPassword, Cancellation);

        (await accounts.InviteAccountAsync(id, signInRedirect: null, Cancellation)).Should().Be(IdentityInvitation.Sent);
        var mails = await auth.Mail.SentToAsync(address, atLeast: 2, Cancellation);

        (await auth.FollowLinkAsync(mails[0], Cancellation)).Should().Be(id);
        (await auth.SignInAsync(address, strangersPassword, Cancellation)).Should().BeNull("a sign-up does not change an account the application made");
    }

    [Fact]
    public async Task Inviting_an_id_auth_has_no_account_for_mails_nobody()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();

        (await SupabaseAuthServer.Accounts(admin).InviteAccountAsync(Guid.NewGuid(), signInRedirect: null, Cancellation))
            .Should().Be(IdentityInvitation.NoSuchAccount);
    }

    // ---- A mail that could not be sent ----------------------------------------------------------------------

    [Fact]
    public async Task An_invitation_whose_mail_could_not_be_sent_leaves_the_address_free_to_invite_again()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var connection = new LosesTheFirstInvitation();
        using var admin = auth.Admin(connection);
        var accounts = SupabaseAuthServer.Accounts(admin);
        var address = NewAddress("mail-failed");

        await accounts.Invoking(a => a.InviteByEmailAsync(address, signInRedirect: null, Cancellation))
            .Should().ThrowAsync<HttpRequestException>("the failure is the caller's, who decides whether to try again");
        (await auth.Mail.SentToAsync(address, atLeast: 0, Cancellation)).Should().BeEmpty();

        // Had the account of the first attempt stayed, this would answer that the address is taken, for good.
        var created = (await accounts.InviteByEmailAsync(address, signInRedirect: null, Cancellation))
            .Should().BeOfType<IdentityAccountOutcome.Created>().Subject;

        var mail = (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle().Subject;
        (await auth.FollowLinkAsync(mail, Cancellation)).Should().Be(created.Identity);
    }

    // ---- An answer that never arrived -----------------------------------------------------------------------

    /// <summary>
    /// What the invitation of an address cannot make good: Auth chose the id and the answer that carried it
    /// is gone, so nobody can mail the user or delete it. The address is then taken like any other, and the
    /// application goes on as it does for an address somebody else registered.
    /// </summary>
    [Fact]
    public async Task An_account_auth_made_without_its_answer_arriving_keeps_the_address_taken()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var connection = new LosesTheAnswerToTheFirstUserMade();
        using var admin = auth.Admin(connection);
        var accounts = SupabaseAuthServer.Accounts(admin);
        var address = NewAddress("answer-lost");

        await accounts.Invoking(a => a.InviteByEmailAsync(address, signInRedirect: null, Cancellation))
            .Should().ThrowAsync<HttpRequestException>("the failure is the caller's, as it happened");

        (await accounts.InviteByEmailAsync(address, signInRedirect: null, Cancellation))
            .Should().BeOfType<IdentityAccountOutcome.AddressTaken>("Auth made the user before the answer was lost, and no id came back to take it away by");
        (await auth.Mail.SentToAsync(address, atLeast: 0, Cancellation)).Should().BeEmpty("neither attempt got as far as the request that mails");
    }

    /// <summary>
    /// The way out of the test above, for an application that has to pick up after any failure: it puts an
    /// id on record before Auth is asked, and the id finds the account whatever became of the answer.
    /// </summary>
    [Fact]
    public async Task An_application_that_kept_the_id_finds_and_invites_an_account_whose_making_was_never_answered()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var connection = new LosesTheAnswerToTheFirstUserMade();
        using var admin = auth.Admin(connection);
        var accounts = SupabaseAuthServer.Accounts(admin);
        var address = NewAddress("own-id-answer-lost");
        var id = Guid.NewGuid();

        await accounts.Invoking(a => a.CreateAsync(id, address, Cancellation))
            .Should().ThrowAsync<HttpRequestException>();

        (await accounts.FindAsync(id, Cancellation)).Should().Be(new IdentityAccount(id, HasSignedIn: false), "the id says how far the work got: the account is there");
        (await accounts.InviteAccountAsync(id, signInRedirect: null, Cancellation)).Should().Be(IdentityInvitation.Sent);

        var mail = (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle().Subject;
        (await auth.FollowLinkAsync(mail, Cancellation)).Should().Be(id);
    }

    // ---- Helpers --------------------------------------------------------------------------------------------

    /// <summary>An address nobody else in this run uses, on a domain that can never receive mail.</summary>
    private static string NewAddress(string who) => $"{who}-{Guid.NewGuid():N}@example.test";

    private static string NewPassword() => "Pw-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// A connection to Auth that breaks once, at the first request that asks for an invitation, before
    /// anything of it is sent: what a network does between the request that makes a user and the one that
    /// has it mailed.
    /// </summary>
    private sealed class LosesTheFirstInvitation() : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        private int _lost;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => request.RequestUri!.AbsolutePath.EndsWith("/invite", StringComparison.Ordinal) && Interlocked.Exchange(ref _lost, 1) == 0
                ? throw new HttpRequestException("The connection was lost before the invitation was asked for.")
                : base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// A connection to Auth that breaks once, after Auth has made a user and before its answer is back: the
    /// first request that makes a user reaches Auth, is answered, and the answer is thrown away.
    /// </summary>
    private sealed class LosesTheAnswerToTheFirstUserMade() : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        private int _lost;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var answer = await base.SendAsync(request, cancellationToken);

            // Only an answer that says the user was made is lost: a refusal is passed on, so a test that
            // expected a user to be there fails on what Auth said.
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath.EndsWith("/admin/users", StringComparison.Ordinal)
                && answer.IsSuccessStatusCode
                && Interlocked.Exchange(ref _lost, 1) == 0)
            {
                answer.Dispose();
                throw new HttpRequestException("The connection was lost before the answer arrived.");
            }

            return answer;
        }
    }
}
