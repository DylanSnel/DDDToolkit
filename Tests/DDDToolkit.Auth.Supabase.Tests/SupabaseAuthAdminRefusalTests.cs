using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using FluentAssertions;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// Every way Auth, or something in front of it, refuses a call: each becomes one exception with the
/// status and Auth's code, after one request, and with none of the text that came back.
/// </summary>
public sealed class SupabaseAuthAdminRefusalTests
{
    private const string Address = "ada.lindqvist@example.test";

    private static readonly Guid Ada = Guid.Parse("8051a7e8-8599-4ad8-b169-68dc96beb0c5");

    private readonly StubAuthServer _auth = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// The six calls, by name, so that a theory can run each refusal against each of them. Inviting a user
    /// by its id is two requests, and what is refused here is its first, which reads the user.
    /// </summary>
    public static readonly string[] Calls = ["invite", "invite-user", "create", "find", "update", "delete"];

    private static Task Call(SupabaseAuthAdmin admin, string call) => call switch
    {
        "invite" => admin.InviteByEmailAsync(Address, options: null, Cancellation),
        "invite-user" => admin.InviteUserAsync(Ada, options: null, Cancellation),
        "create" => admin.CreateUserAsync(new SupabaseNewUser(Address, Id: Ada, Password: "a-password-nobody-uses"), Cancellation),
        "find" => admin.FindUserAsync(Ada, Cancellation),
        "update" => admin.UpdateUserAsync(Ada, new SupabaseUserChange(Password: "another-password-nobody-uses"), Cancellation),
        "delete" => admin.DeleteUserAsync(Ada, Cancellation),
        _ => throw new ArgumentOutOfRangeException(nameof(call), call, "Not one of the admin client's calls."),
    };

    /// <summary>
    /// The refusals Auth documents for its admin API, in the shape it gives them, and what a gateway or a
    /// proxy answers instead of Auth. The last column is the code the exception should carry.
    /// </summary>
    public static TheoryData<string, int, string, string, string?> Refusals()
    {
        (int Status, string Body, string MediaType, string? Code)[] refusals =
        [
            (400, AuthAnswers.Refusal(400, "validation_failed", "Unable to validate email address: invalid format"), "application/json", "validation_failed"),
            (400, AuthAnswers.Refusal(400, "bad_json", "Could not parse request body as JSON"), "application/json", "bad_json"),
            (401, AuthAnswers.Refusal(401, "no_authorization", "This endpoint requires a valid Bearer token"), "application/json", "no_authorization"),
            (403, AuthAnswers.Refusal(403, "not_admin", "User not allowed"), "application/json", "not_admin"),
            (403, AuthAnswers.Refusal(403, "bad_jwt", "invalid JWT: unable to parse or verify signature, token is malformed"), "application/json", "bad_jwt"),
            (422, AuthAnswers.Refusal(422, "weak_password", "Password should be at least 6 characters."), "application/json", "weak_password"),
            (422, AuthAnswers.Refusal(422, "email_address_not_authorized", "Email address not authorized"), "application/json", "email_address_not_authorized"),
            (429, AuthAnswers.Refusal(429, "over_email_send_rate_limit", "email rate limit exceeded"), "application/json", "over_email_send_rate_limit"),
            (429, AuthAnswers.Refusal(429, "over_request_rate_limit", "Request rate limit reached"), "application/json", "over_request_rate_limit"),
            (500, AuthAnswers.Refusal(500, "unexpected_failure", "Error sending invite email"), "application/json", "unexpected_failure"),
            (500, AuthAnswers.DatabaseRefusal("23505", "duplicate key value violates unique constraint \"users_pkey\"", "Key (id)=(8051a7e8-8599-4ad8-b169-68dc96beb0c5) already exists."), "application/json", "23505"),
            (403, AuthAnswers.NewerRefusal("not_admin", "User not allowed"), "application/json", "not_admin"),
            (401, """{"message":"Invalid API key","hint":"Double check your Supabase `anon` or `service_role` API key."}""", "application/json", null),
            (502, "<html><body><h1>502 Bad Gateway</h1></body></html>", "text/html", null),
            (503, "", "text/plain", null),
            (504, "upstream request timeout", "text/plain", null),
        ];

        var data = new TheoryData<string, int, string, string, string?>();
        foreach (var call in Calls)
        {
            foreach (var (status, body, mediaType, code) in refusals)
            {
                data.Add(call, status, body, mediaType, code);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_refusal_is_an_exception_with_the_status_and_auths_code(string call, int status, string body, string mediaType, string? code)
    {
        _auth.Answers(status, body, mediaType);

        var refused = await FluentActions.Awaiting(() => Call(_auth.Admin(), call)).Should().ThrowAsync<SupabaseAuthAdminException>();

        refused.Which.Status.Should().Be(status);
        refused.Which.AuthErrorCode.Should().Be(code);
        refused.Which.AddressAlreadyRegistered.Should().BeFalse();
        refused.Which.Message.Should().Contain(status.ToString());
        if (code is not null)
        {
            refused.Which.Message.Should().Contain(code);
        }

        _auth.Requests.Should().ContainSingle("nothing is retried");
    }

    [Theory]
    [InlineData("invite", 500)]
    [InlineData("invite", 503)]
    [InlineData("invite", 429)]
    [InlineData("invite-user", 500)]
    [InlineData("invite-user", 503)]
    [InlineData("create", 500)]
    [InlineData("create", 503)]
    [InlineData("find", 503)]
    [InlineData("update", 503)]
    [InlineData("delete", 500)]
    [InlineData("delete", 503)]
    public async Task Nothing_is_retried(string call, int status)
    {
        // A second answer is ready, and would turn the call into a success if the client asked again.
        _auth.Answers(status, AuthAnswers.Refusal(status, "unexpected_failure", "Try again"));
        _auth.Answers(200, call == "delete" ? "{}" : AuthAnswers.User(Ada, Address));

        await FluentActions.Awaiting(() => Call(_auth.Admin(), call)).Should().ThrowAsync<SupabaseAuthAdminException>();

        _auth.Requests.Should().ContainSingle("an invitation sent twice is two mails, and the caller decides what a failure means");
    }

    [Theory]
    [MemberData(nameof(EveryCall))]
    public async Task A_connection_that_fails_is_the_clients_own_exception_and_is_not_retried(string call)
    {
        _auth.Fails(new HttpRequestException("Connection refused (project.example.test:443)"));
        _auth.Answers(200, call == "delete" ? "{}" : AuthAnswers.User(Ada, Address));

        await FluentActions.Awaiting(() => Call(_auth.Admin(), call)).Should().ThrowAsync<HttpRequestException>();

        _auth.Requests.Should().ContainSingle();
    }

    public static TheoryData<string> EveryCall => [.. Calls];

    [Fact]
    public async Task Creating_a_user_for_a_taken_address_says_so_to_the_server_code()
    {
        _auth.Answers(422, AuthAnswers.Refusal(422, "email_exists", AuthAnswers.AddressExistsText));

        var refused = await _auth.Admin().Invoking(a => a.CreateUserAsync(new SupabaseNewUser(Address, Id: Ada), Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        refused.Which.AddressAlreadyRegistered.Should().BeTrue();
        refused.Which.Message.Should().NotContain(Address);
    }

    [Fact]
    public async Task A_taken_id_is_the_databases_refusal_and_not_a_taken_address()
    {
        _auth.Answers(500, AuthAnswers.DatabaseRefusal("23505", "duplicate key value violates unique constraint \"users_pkey\"", $"Key (id)=({Ada}) already exists."));

        var refused = await _auth.Admin().Invoking(a => a.CreateUserAsync(new SupabaseNewUser(Address, Id: Ada), Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        refused.Which.Status.Should().Be(500);
        refused.Which.AuthErrorCode.Should().Be("23505");
        refused.Which.AddressAlreadyRegistered.Should().BeFalse();
        refused.Which.DuplicateKey.Should().BeTrue();
    }

    [Fact]
    public async Task An_address_that_another_call_registered_at_the_same_moment_is_the_databases_refusal_too()
    {
        // Auth looks for the address before it writes. Of two calls that both found nothing, the database
        // lets one write and refuses the other, in the same words as for an id: only the index differs, and
        // that is in a text which is not read.
        _auth.Answers(500, AuthAnswers.DatabaseRefusal("23505", "duplicate key value violates unique constraint \"users_email_partial_key\"", $"Key (email)=({Address}) already exists."));

        var refused = await _auth.Admin().Invoking(a => a.CreateUserAsync(new SupabaseNewUser(Address), Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        refused.Which.DuplicateKey.Should().BeTrue();
        refused.Which.AddressAlreadyRegistered.Should().BeFalse("that is Auth's own answer, for an address it found");
        refused.Which.Message.Should().NotContain(Address).And.NotContain("users_email_partial_key");
    }

    [Theory]
    [InlineData(500, "unexpected_failure", false)]
    [InlineData(409, "23505", false)]
    [InlineData(500, "23503", false)]
    [InlineData(500, "23505", true)]
    public void Only_the_databases_refusal_of_a_duplicate_is_a_duplicate_key(int status, string code, bool duplicate)
    {
        new SupabaseAuthAdminException(status, code).DuplicateKey.Should().Be(duplicate);
    }

    [Theory]
    [InlineData(401, "no_authorization")]
    [InlineData(403, "not_admin")]
    [InlineData(403, "bad_jwt")]
    public async Task A_refused_key_says_which_key_is_meant(int status, string code)
    {
        _auth.Answers(status, AuthAnswers.Refusal(status, code, "User not allowed"));

        var refused = await _auth.Admin().Invoking(a => a.FindUserAsync(Ada, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        refused.Which.Message.Should().Contain("secret key").And.NotContain(StubAuthServer.SecretKey);
    }

    [Fact]
    public async Task An_address_that_is_not_auths_says_what_to_check()
    {
        // The project's URL given as the Auth URL: its gateway has no route for /invite, which Auth has under /auth/v1.
        _auth.Answers(404, """{"message":"no Route matched with those values"}""");
        _auth.Answers(404, AuthAnswers.Refusal(404, "user_not_found", "User not found"));

        var wrongUrl = await _auth.Admin(new SupabaseAuthOptions { ProjectUrl = StubAuthServer.ProjectUrl, AuthUrl = StubAuthServer.ProjectUrl })
            .Invoking(a => a.InviteByEmailAsync(Address, options: null, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();
        var noSuchUser = await _auth.Admin().Invoking(a => a.UpdateUserAsync(Ada, new SupabaseUserChange(Password: "another-password-nobody-uses"), Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        wrongUrl.Which.Message.Should().Contain("project URL").And.Contain("AuthUrl", "a 404 that names nothing came from something that does not know the admin API");
        wrongUrl.Which.Message.Should().Contain("AuthUrl ends in /auth/v1", "a gateway reached under another name serves Auth there, and AuthUrl is taken as it is written");
        noSuchUser.Which.Message.Should().NotContain("AuthUrl", "Auth said what it did not find, so the address is right");
    }

    [Theory]
    [InlineData("""{"code":422,"error_code":"ada.lindqvist@example.test is taken","msg":"x"}""")]
    [InlineData("""{"code":"ada.lindqvist@example.test","message":"x"}""")]
    [InlineData("""{"code":422,"error_code":"a code that goes on for far longer than any of the codes that Auth gives to anybody anywhere","msg":"x"}""")]
    [InlineData("""{"code":422,"error_code":"","msg":"x"}""")]
    [InlineData("""{"code":422,"error_code":422,"msg":"x"}""")]
    [InlineData("""["email_exists"]""")]
    [InlineData("\"email_exists\"")]
    public async Task What_does_not_look_like_a_code_is_not_taken_for_one(string body)
    {
        _auth.Answers(422, body);

        var refused = await _auth.Admin().Invoking(a => a.InviteByEmailAsync(Address, options: null, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();

        refused.Which.AuthErrorCode.Should().BeNull("a code is a short word, and anything else could be a text with an address in it");
        refused.Which.Message.Should().NotContain("example.test");
    }

    [Fact]
    public void An_exception_made_by_a_host_reads_like_one_made_here()
    {
        // Public, so that a host's own stand-in for the admin client can refuse the way the real one does.
        var refused = new SupabaseAuthAdminException(429, "over_email_send_rate_limit", "inviting an address");

        refused.Status.Should().Be(429);
        refused.AuthErrorCode.Should().Be("over_email_send_rate_limit");
        refused.Message.Should().Be("Supabase Auth's admin API answered 429 (over_email_send_rate_limit) while inviting an address.");
        new SupabaseAuthAdminException(503, authErrorCode: null).Message.Should().Be("Supabase Auth's admin API answered 503.");
    }
}
