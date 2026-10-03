using System.Net.Http.Headers;
using System.Text.Json;
using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// Each call of the admin client: what it sends to Auth, and what it makes of Auth's answer. A handler
/// stands in for Auth, so what is checked is the request as it would leave the machine.
/// </summary>
public sealed class SupabaseAuthAdminTests
{
    private const string Address = "ada.lindqvist@example.test";

    private static readonly Guid Ada = Guid.Parse("8051a7e8-8599-4ad8-b169-68dc96beb0c5");

    private readonly StubAuthServer _auth = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Inviting_posts_the_address_with_the_secret_key()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address, invitedAt: "2026-03-02T09:15:54.013489931Z"));

        var result = await _auth.Admin().InviteByEmailAsync(Address, options: null, Cancellation);

        result.Should().Be(new SupabaseInvitationResult.Sent(Ada), "Auth made the user and answers with its id");

        var request = _auth.Request;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri(StubAuthServer.Url + "/invite"));
        request.Headers["apikey"].Should().Be(StubAuthServer.SecretKey, "a project's gateway reads the key here");
        request.Headers["Authorization"].Should().Be("Bearer " + StubAuthServer.SecretKey, "Auth itself reads the key here");
        request.Headers["Content-Type"].Should().StartWith("application/json");
        request.Members.Should().Equal(["email"], "nothing goes along that was not asked for");
        request.Json.GetProperty("email").GetString().Should().Be(Address);
    }

    [Fact]
    public async Task An_invitation_carries_where_its_link_leads_and_the_new_users_metadata()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        var options = new SupabaseInvitation(
            RedirectTo: "https://crews.example.test/welcome?step=1&from=mail",
            Data: new Dictionary<string, object?> { ["language"] = "nl", ["newsletter"] = false });

        await _auth.Admin().InviteByEmailAsync(Address, options, Cancellation);

        var request = _auth.Request;
        request.Uri.AbsoluteUri.Should().Be(
            StubAuthServer.Url + "/invite?redirect_to=https%3A%2F%2Fcrews.example.test%2Fwelcome%3Fstep%3D1%26from%3Dmail",
            "the redirect is one value, so its own query must not become Auth's");
        request.Members.Should().Equal("email", "data");
        request.Json.GetProperty("data").GetProperty("language").GetString().Should().Be("nl");
        request.Json.GetProperty("data").GetProperty("newsletter").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("newer")]
    public async Task An_address_with_an_account_is_already_registered(string shape)
    {
        _auth.Answers(422, shape == "legacy"
            ? AuthAnswers.Refusal(422, "email_exists", AuthAnswers.AddressExistsText)
            : AuthAnswers.NewerRefusal("email_exists", AuthAnswers.AddressExistsText));

        var result = await _auth.Admin().InviteByEmailAsync(Address, options: null, Cancellation);

        result.Should().BeOfType<SupabaseInvitationResult.AlreadyRegistered>("it is an answer for the server to act on, not an error to pass on");
        _auth.Requests.Should().ContainSingle("nothing looks the account up afterwards");
    }

    [Fact]
    public async Task Already_registered_says_nothing_about_the_account()
    {
        typeof(SupabaseInvitationResult.AlreadyRegistered).GetProperties().Should().BeEmpty(
            "nothing here looks an account up by address, so there is nothing about it to carry");

        _auth.Answers(422, AuthAnswers.Refusal(422, "email_exists", AuthAnswers.AddressExistsText));
        var result = await _auth.Admin().InviteByEmailAsync(Address, options: null, Cancellation);

        result.ToString().Should().NotContain(Address).And.NotContain("example.test");
    }

    [Fact]
    public async Task Only_a_taken_address_is_already_registered()
    {
        // The same status with another code is a refusal like any other, and so is the same code elsewhere.
        _auth.Answers(422, AuthAnswers.Refusal(422, "phone_exists", "A user with this phone number has already been registered"));
        _auth.Answers(400, AuthAnswers.Refusal(400, "email_exists", AuthAnswers.AddressExistsText));
        var admin = _auth.Admin();

        var otherCode = await admin.Invoking(a => a.InviteByEmailAsync(Address, options: null, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();
        var otherStatus = await admin.Invoking(a => a.InviteByEmailAsync(Address, options: null, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        otherCode.Which.AuthErrorCode.Should().Be("phone_exists");
        otherStatus.Which.Status.Should().Be(400);
    }

    [Fact]
    public async Task Inviting_a_user_reads_the_address_auth_has_for_it_and_has_auth_invite_that()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(Ada, Address, invitedAt: "2026-03-02T09:15:54.013489931Z"));

        var result = await _auth.Admin().InviteUserAsync(Ada, new SupabaseInvitation(RedirectTo: "https://crews.example.test/welcome"), Cancellation);

        result.Should().Be(new SupabaseInvitationResult.Sent(Ada));
        _auth.Requests.Should().HaveCount(2);

        var read = _auth.Requests[0];
        read.Method.Should().Be(HttpMethod.Get);
        read.Uri.Should().Be(new Uri(StubAuthServer.Url + "/admin/users/8051a7e8-8599-4ad8-b169-68dc96beb0c5"));

        var invite = _auth.Requests[1];
        invite.Method.Should().Be(HttpMethod.Post);
        invite.Uri.AbsoluteUri.Should().Be(StubAuthServer.Url + "/invite?redirect_to=https%3A%2F%2Fcrews.example.test%2Fwelcome");
        invite.Members.Should().Equal("email");
        invite.Json.GetProperty("email").GetString().Should().Be(Address, "Auth invites by address, and the caller gave an id");
    }

    [Fact]
    public async Task Inviting_a_user_auth_does_not_have_is_null_and_mails_nobody()
    {
        _auth.Answers(404, AuthAnswers.Refusal(404, "user_not_found", "User not found"));

        var result = await _auth.Admin().InviteUserAsync(Ada, options: null, Cancellation);

        result.Should().BeNull();
        _auth.Requests.Should().ContainSingle("there is no address to invite");
    }

    [Fact]
    public async Task Inviting_a_user_who_has_proven_the_address_is_already_registered()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address, emailConfirmedAt: "2026-03-03T08:00:00.101216Z", lastSignInAt: "2026-03-04T07:30:15.182151Z"));
        _auth.Answers(422, AuthAnswers.Refusal(422, "email_exists", AuthAnswers.AddressExistsText));

        var result = await _auth.Admin().InviteUserAsync(Ada, options: null, Cancellation);

        result.Should().BeOfType<SupabaseInvitationResult.AlreadyRegistered>("whether a user still waits for an invitation is Auth's to say");
    }

    [Fact]
    public async Task Inviting_a_user_is_not_sent_when_auth_mailed_another()
    {
        // Between the two requests the user was removed and the address registered anew, or the user signs
        // in through single sign-on and the address is somebody else's as well.
        var somebodyElse = Guid.Parse("3d0f4c0e-6f0b-4a43-9a53-0c3f0f7f2f11");
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(somebodyElse, Address, invitedAt: "2026-03-02T09:15:54.013489931Z"));

        var refused = await _auth.Admin().Invoking(a => a.InviteUserAsync(Ada, options: null, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        refused.Which.Message.Should().Contain(Ada.ToString(), "it names the user that was asked for")
            .And.NotContain(somebodyElse.ToString(), "and not the one that was mailed: nothing is given to that one")
            .And.NotContain(Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_user_without_an_address_cannot_be_invited(string address)
    {
        // A user who signs in by phone has an empty address.
        _auth.Answers(200, AuthAnswers.User(Ada, address));

        var refused = await _auth.Admin().Invoking(a => a.InviteUserAsync(Ada, options: null, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        refused.Which.Message.Should().Contain("no e-mail address").And.Contain(Ada.ToString());
        _auth.Requests.Should().ContainSingle("an empty address is not sent to be invited");
    }

    [Fact]
    public async Task Reading_a_user_to_invite_that_answers_another_user_is_not_taken_for_it()
    {
        _auth.Answers(200, AuthAnswers.User(Guid.Parse("3d0f4c0e-6f0b-4a43-9a53-0c3f0f7f2f11"), Address));

        await _auth.Admin().Invoking(a => a.InviteUserAsync(Ada, options: null, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        _auth.Requests.Should().ContainSingle("the address of another user is not invited in this one's name");
    }

    [Fact]
    public async Task A_created_user_keeps_its_given_id()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address, emailConfirmedAt: "2026-03-02T09:15:54.216955316Z"));
        var user = new SupabaseNewUser(
            Address,
            Id: Ada,
            Password: "a-password-nobody-uses",
            EmailConfirmed: true,
            UserMetadata: new Dictionary<string, object?> { ["full_name"] = "Ada" },
            AppMetadata: new Dictionary<string, object?> { ["crews_role"] = "operator" });

        var made = await _auth.Admin().CreateUserAsync(user, Cancellation);

        made.Id.Should().Be(Ada);
        made.EmailConfirmedAt.Should().NotBeNull();
        made.HasSignedIn.Should().BeFalse("making a user is not a sign-in");

        var request = _auth.Request;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri(StubAuthServer.Url + "/admin/users"));
        request.Members.Should().Equal("email", "id", "password", "email_confirm", "user_metadata", "app_metadata");
        request.Json.GetProperty("id").GetGuid().Should().Be(Ada);
        request.Json.GetProperty("email").GetString().Should().Be(Address);
        request.Json.GetProperty("password").GetString().Should().Be("a-password-nobody-uses");
        request.Json.GetProperty("email_confirm").GetBoolean().Should().BeTrue();
        request.Json.GetProperty("user_metadata").GetProperty("full_name").GetString().Should().Be("Ada");
        request.Json.GetProperty("app_metadata").GetProperty("crews_role").GetString().Should().Be("operator");
    }

    [Fact]
    public async Task A_user_made_from_an_address_alone_sends_only_the_address()
    {
        var chosenByAuth = Guid.Parse("c03e074d-ae2f-44e1-a3d1-225a3986c1cf");
        _auth.Answers(200, AuthAnswers.User(chosenByAuth, Address));

        var made = await _auth.Admin().CreateUserAsync(new SupabaseNewUser("  " + Address + " "), Cancellation);

        made.Id.Should().Be(chosenByAuth, "without an id given, Auth chooses");
        _auth.Request.Members.Should().Equal(["email"], "an unproven address, no password and no id are Auth's defaults, and say so by being left out");
        _auth.Request.Json.GetProperty("email").GetString().Should().Be(Address, "the spaces around a pasted address are not part of it");
    }

    [Fact]
    public async Task An_auth_server_that_chooses_its_own_id_is_an_error()
    {
        // An Auth server from before an id could be given ignores it. Access given to the id that was
        // asked for would then belong to nobody.
        var chosenByAuth = Guid.Parse("c03e074d-ae2f-44e1-a3d1-225a3986c1cf");
        _auth.Answers(200, AuthAnswers.User(chosenByAuth, Address));

        var refused = await _auth.Admin().Invoking(a => a.CreateUserAsync(new SupabaseNewUser(Address, Id: Ada), Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        refused.Which.Message.Should().Contain(chosenByAuth.ToString(), "the user that was made is still there, and somebody has to know which");
        refused.Which.Message.Should().NotContain(Address);
    }

    [Fact]
    public async Task The_empty_id_is_refused_before_anything_is_sent()
    {
        await _auth.Admin().Invoking(a => a.CreateUserAsync(new SupabaseNewUser(Address, Id: Guid.Empty), Cancellation))
            .Should().ThrowAsync<ArgumentException>();

        _auth.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_user_is_found_by_id_with_what_auth_knows_of_its_use()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address, invitedAt: "2026-03-02T09:15:54.013489931Z"));
        _auth.Answers(200, AuthAnswers.User(Ada, Address,
            invitedAt: "2026-03-02T09:15:54.013489931Z",
            emailConfirmedAt: "2026-03-03T08:00:00.101216Z",
            lastSignInAt: "2026-03-04T07:30:15.182151Z"));
        var admin = _auth.Admin();

        var invited = await admin.FindUserAsync(Ada, Cancellation);
        var signedIn = await admin.FindUserAsync(Ada, Cancellation);

        _auth.Requests.Should().AllSatisfy(request =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.Uri.Should().Be(new Uri(StubAuthServer.Url + "/admin/users/8051a7e8-8599-4ad8-b169-68dc96beb0c5"));
            request.Body.Should().BeNull();
        });

        invited.Should().NotBeNull();
        invited!.Id.Should().Be(Ada);
        invited.InvitedAt.Should().Be(new DateTimeOffset(2026, 3, 2, 9, 15, 54, TimeSpan.Zero).AddTicks(134_899), "Auth writes nine digits of a second and .NET keeps seven");
        invited.EmailConfirmedAt.Should().BeNull();
        invited.LastSignInAt.Should().BeNull("the sign-in time of the identity inside is set when Auth makes it, and is not the user's");
        invited.HasSignedIn.Should().BeFalse();

        signedIn!.EmailConfirmedAt.Should().Be(new DateTimeOffset(2026, 3, 3, 8, 0, 0, TimeSpan.Zero).AddTicks(1_012_160));
        signedIn.LastSignInAt.Should().Be(new DateTimeOffset(2026, 3, 4, 7, 30, 15, TimeSpan.Zero).AddTicks(1_821_510));
        signedIn.HasSignedIn.Should().BeTrue();
    }

    [Fact]
    public void A_user_carries_no_address()
    {
        typeof(SupabaseAuthUser).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(["Id", "InvitedAt", "LastSignInAt", "EmailConfirmedAt", "HasSignedIn"],
                "a caller that needs the address already has it, and what is never read back cannot leak");
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("newer")]
    public async Task A_missing_user_is_null(string shape)
    {
        _auth.Answers(404, shape == "legacy"
            ? AuthAnswers.Refusal(404, "user_not_found", "User not found")
            : AuthAnswers.NewerRefusal("user_not_found", "User not found"));

        var user = await _auth.Admin().FindUserAsync(Ada, Cancellation);

        user.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(NotFoundsThatAreNotAboutTheUser))]
    public async Task A_not_found_that_is_not_about_the_user_is_an_error(string body, string mediaType)
    {
        // A wrong Auth URL answers 404 to everything. Read as "no such user", an erasure would take every
        // account for gone and a sweep would find nothing to do.
        _auth.Answers(404, body, mediaType);
        _auth.Answers(404, body, mediaType);
        var admin = _auth.Admin();

        var finding = await admin.Invoking(a => a.FindUserAsync(Ada, Cancellation)).Should().ThrowAsync<SupabaseAuthAdminException>();
        var deleting = await admin.Invoking(a => a.DeleteUserAsync(Ada, Cancellation)).Should().ThrowAsync<SupabaseAuthAdminException>();

        finding.Which.Status.Should().Be(404);
        deleting.Which.Status.Should().Be(404);
    }

    public static TheoryData<string, string> NotFoundsThatAreNotAboutTheUser => new()
    {
        { "<html><body>404 page not found</body></html>", "text/html" },
        { "404 page not found", "text/plain" },
        { """{"message":"no Route matched with those values"}""", "application/json" },
        { AuthAnswers.Refusal(404, "validation_failed", "user_id must be an UUID"), "application/json" },
        { "", "application/json" },
    };

    [Fact]
    public async Task Changing_a_user_puts_only_what_changes()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        var admin = _auth.Admin();

        await admin.UpdateUserAsync(Ada, new SupabaseUserChange(Password: "another-password-nobody-uses"), Cancellation);
        await admin.UpdateUserAsync(Ada, new SupabaseUserChange(AppMetadata: new Dictionary<string, object?> { ["crews_role"] = "operator" }), Cancellation);

        _auth.Requests.Should().AllSatisfy(request =>
        {
            request.Method.Should().Be(HttpMethod.Put);
            request.Uri.Should().Be(new Uri(StubAuthServer.Url + "/admin/users/8051a7e8-8599-4ad8-b169-68dc96beb0c5"));
        });
        _auth.Requests[0].Members.Should().Equal("password");
        _auth.Requests[0].Json.GetProperty("password").GetString().Should().Be("another-password-nobody-uses");
        _auth.Requests[1].Members.Should().Equal("app_metadata");
        _auth.Requests[1].Json.GetProperty("app_metadata").GetProperty("crews_role").GetString().Should().Be("operator");
    }

    [Fact]
    public async Task A_change_of_nothing_is_refused_before_anything_is_sent()
    {
        await _auth.Admin().Invoking(a => a.UpdateUserAsync(Ada, new SupabaseUserChange(), Cancellation))
            .Should().ThrowAsync<ArgumentException>();

        _auth.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_a_missing_user_is_an_error()
    {
        _auth.Answers(404, AuthAnswers.Refusal(404, "user_not_found", "User not found"));

        var refused = await _auth.Admin().Invoking(a => a.UpdateUserAsync(Ada, new SupabaseUserChange(Password: "another-password-nobody-uses"), Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>("a change to nobody means nothing, unlike a deletion of nobody");

        refused.Which.Status.Should().Be(404);
        refused.Which.AuthErrorCode.Should().Be("user_not_found");
    }

    [Fact]
    public async Task Deleting_a_user_deletes_by_id()
    {
        _auth.Answers(200, "{}");

        var deleted = await _auth.Admin().DeleteUserAsync(Ada, Cancellation);

        deleted.Should().BeTrue();
        _auth.Request.Method.Should().Be(HttpMethod.Delete);
        _auth.Request.Uri.Should().Be(new Uri(StubAuthServer.Url + "/admin/users/8051a7e8-8599-4ad8-b169-68dc96beb0c5"));
        _auth.Request.Body.Should().BeNull("without a body Auth deletes for good, which is what an erasure asks for");
    }

    [Fact]
    public async Task Deleting_a_user_who_is_gone_is_not_an_error()
    {
        _auth.Answers(404, AuthAnswers.Refusal(404, "user_not_found", "User not found"));

        var deleted = await _auth.Admin().DeleteUserAsync(Ada, Cancellation);

        deleted.Should().BeFalse("an erasure that failed halfway runs again, and finds the account already gone");
    }

    [Fact]
    public async Task A_success_that_is_not_auths_is_not_taken_for_done()
    {
        // Something in between that answers 200 to everything, as a proxy's own sign-in page or a wrong host does.
        const string Page = "<html><body>Sign in to continue</body></html>";
        var admin = _auth.Admin();
        var calls = new (string Name, Func<Task> Call)[]
        {
            ("invite", () => admin.InviteByEmailAsync(Address, options: null, Cancellation)),
            ("invite a user", () => admin.InviteUserAsync(Ada, options: null, Cancellation)),
            ("create", () => admin.CreateUserAsync(new SupabaseNewUser(Address), Cancellation)),
            ("find", () => admin.FindUserAsync(Ada, Cancellation)),
            ("update", () => admin.UpdateUserAsync(Ada, new SupabaseUserChange(Password: "another-password-nobody-uses"), Cancellation)),
            ("delete", () => admin.DeleteUserAsync(Ada, Cancellation)),
        };

        foreach (var (name, call) in calls)
        {
            _auth.Answers(200, Page, "text/html");

            var refused = await call.Should().ThrowAsync<SupabaseAuthAdminException>($"a page is no answer to {name}");

            refused.Which.Status.Should().Be(200);
            refused.Which.AuthErrorCode.Should().BeNull();
            refused.Which.Message.Should().Contain("Auth URL").And.NotContain("continue");
        }
    }

    [Fact]
    public async Task A_change_answered_with_another_user_is_not_taken_for_done()
    {
        _auth.Answers(200, AuthAnswers.User(Guid.Parse("c03e074d-ae2f-44e1-a3d1-225a3986c1cf"), Address));

        await _auth.Admin().Invoking(a => a.UpdateUserAsync(Ada, new SupabaseUserChange(Password: "another-password-nobody-uses"), Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();
    }

    [Theory]
    [InlineData("https://project.example.test/auth/v1", "https://project.example.test/auth/v1/invite")]
    [InlineData("https://project.example.test/auth/v1/", "https://project.example.test/auth/v1/invite")]
    [InlineData("  https://project.example.test/auth/v1  ", "https://project.example.test/auth/v1/invite")]
    [InlineData("http://localhost:9999", "http://localhost:9999/invite")]
    [InlineData("http://127.0.0.1:54321/auth/v1", "http://127.0.0.1:54321/auth/v1/invite")]
    [InlineData("http://[::1]:9999/", "http://[::1]:9999/invite")]
    public async Task The_auth_url_is_a_projects_or_a_bare_servers_with_or_without_a_slash(string authUrl, string expected)
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));

        await _auth.Admin(authUrl).InviteByEmailAsync(Address, options: null, Cancellation);

        _auth.Request.Uri.AbsoluteUri.Should().Be(expected);
    }

    [Theory]
    [InlineData("http://auth:9999/")]
    [InlineData("http://project.example.test/auth/v1")]
    [InlineData("HTTP://10.0.0.7:9999")]
    public async Task Plain_http_to_another_machine_is_refused_unless_the_host_says_the_network_is_its_own(string authUrl)
    {
        // Every call sends the secret key, as apikey and as the bearer token, before any answer could say no.
        var making = () => new SupabaseAuthAdmin(authUrl, StubAuthServer.SecretKey, _auth);
        making.Should().Throw<ArgumentException>().WithParameterName("authUrl")
            .Which.Message.Should().Contain("unencrypted").And.Contain("allowPlainHttp").And.NotContain("9999", "the message does not repeat the URL");

        using var http = new HttpClient(_auth) { BaseAddress = new Uri(authUrl) };
        var wrapping = () => new SupabaseAuthAdmin(http);
        wrapping.Should().Throw<ArgumentException>().WithParameterName("http");

        var registering = () => new ServiceCollection().AddSupabaseAuthAdmin(authUrl, StubAuthServer.SecretKey, _auth);
        registering.Should().Throw<ArgumentException>("the host does not start").WithParameterName("authUrl");

        _auth.Requests.Should().BeEmpty("nothing was sent");

        // An Auth server with no gateway in front of it, on a network of the host's own: the host says so.
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        using (var admin = _auth.Admin(authUrl, allowPlainHttp: true))
        {
            await admin.InviteByEmailAsync(Address, options: null, Cancellation);
        }

        using (var admin = new SupabaseAuthAdmin(http, allowPlainHttp: true))
        {
            await admin.FindUserAsync(Ada, Cancellation);
        }

        _auth.Requests.Should().HaveCount(2).And.OnlyContain(request => request.Uri.Scheme == Uri.UriSchemeHttp);
        new ServiceCollection().AddSupabaseAuthAdmin(authUrl, StubAuthServer.SecretKey, _auth, allowPlainHttp: true).Should().HaveCount(2);
    }

    [Fact]
    public void A_handler_that_follows_redirects_is_refused()
    {
        // The runtime's own handlers follow redirects unless told not to, and keep apikey on the request they
        // follow one with: a handler passed for a proxy, with nothing else set, would carry the key elsewhere.
        using var plain = new HttpClientHandler();
        using var sockets = new SocketsHttpHandler();
        using var wrappedInner = new HttpClientHandler();
        using var wrapped = new Passing(new Passing(wrappedInner));

        foreach (var handler in new HttpMessageHandler[] { plain, sockets, wrapped })
        {
            var making = () => new SupabaseAuthAdmin(StubAuthServer.Url, StubAuthServer.SecretKey, handler);
            making.Should().Throw<ArgumentException>(handler.GetType().Name).WithParameterName("handler")
                .Which.Message.Should().Contain("AllowAutoRedirect = false");

            var services = new ServiceCollection();
            var registering = () => services.AddSupabaseAuthAdmin(StubAuthServer.Url, StubAuthServer.SecretKey, handler);
            registering.Should().Throw<ArgumentException>("the host does not start").WithParameterName("handler");
            services.Should().BeEmpty();
        }

        // With redirects off, each is taken; and so is a handler of the host's own making, which is its own to answer for.
        using var off = new HttpClientHandler { AllowAutoRedirect = false };
        using var socketsOff = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var wrappedOff = new Passing(new HttpClientHandler { AllowAutoRedirect = false });
        foreach (var handler in new HttpMessageHandler[] { off, socketsOff, wrappedOff, _auth })
        {
            using var admin = new SupabaseAuthAdmin(StubAuthServer.Url, StubAuthServer.SecretKey, handler);
        }
    }

    /// <summary>A delegating handler that adds nothing: what a host wraps a handler in for a header or a log of its own.</summary>
    private sealed class Passing(HttpMessageHandler inner) : DelegatingHandler(inner);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("project.example.test/auth/v1")]
    [InlineData("/auth/v1")]
    [InlineData("ftp://project.example.test/auth/v1")]
    [InlineData("https://project.example.test/auth/v1?apikey=x")]
    [InlineData("https://project.example.test/auth/v1#top")]
    public void An_auth_url_that_is_not_one_is_refused(string authUrl)
    {
        var making = () => new SupabaseAuthAdmin(authUrl, StubAuthServer.SecretKey, _auth);

        making.Should().Throw<ArgumentException>().WithParameterName("authUrl");
    }

    [Fact]
    public async Task A_client_the_host_configured_is_used_as_it_is()
    {
        // The host's own client: its base address is the Auth URL and its headers carry the key.
        using var http = new HttpClient(_auth) { BaseAddress = new Uri("http://localhost:9999") };
        http.DefaultRequestHeaders.Add("apikey", "a-service-token");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "a-service-token");
        _auth.Answers(200, AuthAnswers.User(Ada, Address));

        using (var admin = new SupabaseAuthAdmin(http))
        {
            await admin.FindUserAsync(Ada, Cancellation);
        }

        _auth.Request.Uri.AbsoluteUri.Should().Be("http://localhost:9999/admin/users/8051a7e8-8599-4ad8-b169-68dc96beb0c5");
        _auth.Request.Headers["apikey"].Should().Be("a-service-token");
        _auth.Request.Headers["Authorization"].Should().Be("Bearer a-service-token");

        // Disposing the admin client left the host's client alone.
        _auth.Answers(200, "{}");
        (await http.GetAsync("ready", Cancellation)).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public void A_client_without_a_base_address_is_refused()
    {
        using var http = new HttpClient(_auth);

        var making = () => new SupabaseAuthAdmin(http);

        making.Should().Throw<ArgumentException>().WithParameterName("http");
    }

    [Fact]
    public async Task A_client_made_here_is_disposed_with_it_and_leaves_the_handler_alone()
    {
        var admin = _auth.Admin();
        admin.Dispose();

        await admin.Invoking(a => a.FindUserAsync(Ada, Cancellation)).Should().ThrowAsync<ObjectDisposedException>();

        // The handler was the caller's, and still works.
        _auth.Answers(200, "{}");
        using var other = new HttpClient(_auth, disposeHandler: false);
        (await other.GetAsync("http://localhost:9999/ready", Cancellation)).IsSuccessStatusCode.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task An_empty_address_is_refused_before_anything_is_sent(string? address)
    {
        var admin = _auth.Admin();

        await admin.Invoking(a => a.InviteByEmailAsync(address!, options: null, Cancellation)).Should().ThrowAsync<ArgumentException>();
        await admin.Invoking(a => a.CreateUserAsync(new SupabaseNewUser(address!), Cancellation)).Should().ThrowAsync<ArgumentException>();

        _auth.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task The_callers_cancellation_reaches_the_request()
    {
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();

        await _auth.Admin().Invoking(a => a.InviteByEmailAsync(Address, options: null, stopped.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        _auth.Requests.Should().BeEmpty("a caller that stopped waiting has nothing sent on its behalf");
    }

    [Fact]
    public async Task Metadata_goes_out_as_the_json_it_is()
    {
        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        var metadata = new Dictionary<string, object?>
        {
            ["text"] = "café",
            ["number"] = 3,
            ["flag"] = true,
            ["nothing"] = null,
            ["list"] = new[] { "a", "b" },
            ["nested"] = new Dictionary<string, object?> { ["inner"] = 1.5 },
        };

        await _auth.Admin().CreateUserAsync(new SupabaseNewUser(Address, AppMetadata: metadata), Cancellation);

        var sent = _auth.Request.Json.GetProperty("app_metadata");
        sent.GetProperty("text").GetString().Should().Be("café");
        sent.GetProperty("number").GetInt32().Should().Be(3);
        sent.GetProperty("flag").GetBoolean().Should().BeTrue();
        sent.GetProperty("nothing").ValueKind.Should().Be(JsonValueKind.Null);
        sent.GetProperty("list").EnumerateArray().Select(item => item.GetString()).Should().Equal("a", "b");
        sent.GetProperty("nested").GetProperty("inner").GetDouble().Should().Be(1.5);
    }
}
