using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using FluentAssertions;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// What must never leave the admin client except in the one request to Auth: the address, a password and
/// the secret key. Not in an exception, however much of them Auth repeats in its refusal, not in a URL,
/// which is what request logs and traces keep, and not in what a record prints.
/// </summary>
public sealed class SupabaseAuthAdminSecrecyTests
{
    private const string Address = "ada.lindqvist@example.test";
    private const string Password = "a-password-nobody-uses";
    private const string NewPassword = "another-password-nobody-uses";

    private static readonly Guid Ada = Guid.Parse("8051a7e8-8599-4ad8-b169-68dc96beb0c5");

    /// <summary>Everything a log line or an exception must not have in it.</summary>
    private static readonly string[] Secrets = [Address, "ada.lindqvist", Password, NewPassword, StubAuthServer.SecretKey];

    private readonly StubAuthServer _auth = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static (string Name, Func<SupabaseAuthAdmin, Task> Call)[] Calls =>
    [
        ("invite", admin => admin.InviteByEmailAsync(Address, new SupabaseInvitation("https://crews.example.org/welcome"), Cancellation)),
        ("invite a user", admin => admin.InviteUserAsync(Ada, new SupabaseInvitation("https://crews.example.org/welcome"), Cancellation)),
        ("create", admin => admin.CreateUserAsync(new SupabaseNewUser(Address, Id: Ada, Password: Password), Cancellation)),
        ("find", admin => admin.FindUserAsync(Ada, Cancellation)),
        ("update", admin => admin.UpdateUserAsync(Ada, new SupabaseUserChange(Password: NewPassword), Cancellation)),
        ("delete", admin => admin.DeleteUserAsync(Ada, Cancellation)),
    ];

    /// <summary>
    /// Refusals that repeat everything that was sent, in every member a refusal has, as an Auth server, a
    /// database error or a gateway's debug page might.
    /// </summary>
    private static (int Status, string Body, string MediaType)[] RefusalsThatRepeatWhatWasSent =>
    [
        (400, AuthAnswers.Refusal(400, "validation_failed", $"Unable to validate {Address} with password {Password}"), "application/json"),
        (403, AuthAnswers.Refusal(403, "bad_jwt", $"invalid JWT: {StubAuthServer.SecretKey}"), "application/json"),
        (422, AuthAnswers.NewerRefusal("weak_password", $"{NewPassword} is too weak for {Address}"), "application/json"),
        (500, AuthAnswers.DatabaseRefusal("23505", "duplicate key value violates unique constraint \"users_email_partial_key\"", $"Key (email)=({Address}) already exists."), "application/json"),
        (422, AuthAnswers.Refusal(422, Address, AuthAnswers.AddressExistsText), "application/json"),
        (502, $"<html><body>Bad gateway. apikey: {StubAuthServer.SecretKey}; body: {Address} {Password} {NewPassword}</body></html>", "text/html"),
        (200, $"<html><body>Signed in as {Address}. apikey: {StubAuthServer.SecretKey}</body></html>", "text/html"),
    ];

    [Fact]
    public async Task No_exception_or_log_line_carries_the_address()
    {
        using var telemetry = new HttpTelemetry();

        foreach (var (name, call) in Calls)
        {
            foreach (var (status, body, mediaType) in RefusalsThatRepeatWhatWasSent)
            {
                _auth.Answers(status, body, mediaType);

                var refused = await FluentActions.Awaiting(() => call(_auth.Admin())).Should().ThrowAsync<SupabaseAuthAdminException>($"{name} answered {status} is a refusal");

                // ToString is what a logger writes: the type, the message, the stack and every inner exception.
                refused.Which.ToString().Should().NotContainAny(Secrets, $"{name} answered {status} must not repeat what was sent");
                refused.Which.Data.Count.Should().Be(0);
                refused.Which.InnerException.Should().BeNull("an inner exception from reading the answer could quote the answer");
            }
        }

        // The admin client has no logger. What the runtime itself reports about each request, to whichever
        // listener a host or its tracing attaches, is where it went: so nothing secret may be in a URL.
        telemetry.Lines.Should().Contain(line => line.Contains("RequestStart"), "the listener has to have seen the requests for their silence to mean anything");
        telemetry.Lines.Should().AllSatisfy(line => line.Should().NotContainAny(Secrets));
    }

    [Fact]
    public async Task The_address_and_the_key_never_travel_in_a_url()
    {
        foreach (var (_, call) in Calls)
        {
            _auth.Answers(503, "");
            await FluentActions.Awaiting(() => call(_auth.Admin())).Should().ThrowAsync<SupabaseAuthAdminException>();
        }

        _auth.Requests.Should().HaveCount(Calls.Length);
        _auth.Requests.Should().AllSatisfy(request =>
        {
            request.Uri.AbsoluteUri.Should().NotContainAny(Secrets, "a URL is what proxies, request logs and traces keep");
            Uri.UnescapeDataString(request.Uri.AbsoluteUri).Should().NotContainAny(Secrets);
        });
    }

    [Fact]
    public async Task An_address_read_to_invite_a_user_goes_to_auth_and_nowhere_else()
    {
        // Inviting a user by its id is the one call that takes an address out of an answer: Auth invites by
        // address, so the one it has for the user is said back to it. Whatever Auth then does with it, the
        // address is in the body of that one request and in nothing that comes back to the caller.
        foreach (var (status, body, mediaType) in RefusalsThatRepeatWhatWasSent)
        {
            _auth.Answers(200, AuthAnswers.User(Ada, Address));
            _auth.Answers(status, body, mediaType);

            var refused = await _auth.Admin().Invoking(a => a.InviteUserAsync(Ada, new SupabaseInvitation("https://crews.example.org/welcome"), Cancellation))
                .Should().ThrowAsync<SupabaseAuthAdminException>($"the invitation answered {status} is a refusal");

            refused.Which.ToString().Should().NotContainAny(Secrets);
            refused.Which.Data.Count.Should().Be(0);
            refused.Which.InnerException.Should().BeNull();
        }

        _auth.Answers(200, AuthAnswers.User(Ada, Address));
        _auth.Answers(200, AuthAnswers.User(Ada, Address, invitedAt: "2026-03-02T09:15:54.013489931Z"));

        var sent = await _auth.Admin().InviteUserAsync(Ada, options: null, Cancellation);

        sent.Should().Be(new SupabaseInvitationResult.Sent(Ada));
        sent!.ToString().Should().NotContainAny(Secrets);
        _auth.Requests.Should().AllSatisfy(request => Uri.UnescapeDataString(request.Uri.AbsoluteUri).Should().NotContainAny(Secrets, "a URL is what proxies, request logs and traces keep"));
        _auth.Requests[^1].Json.GetProperty("email").GetString().Should().Be(Address);
    }

    [Fact]
    public async Task A_connection_that_fails_carries_nothing_that_was_sent()
    {
        foreach (var (name, call) in Calls)
        {
            _auth.Fails(new HttpRequestException("No such host is known. (project.example.org:443)"));

            var failed = await FluentActions.Awaiting(() => call(_auth.Admin())).Should().ThrowAsync<HttpRequestException>();

            failed.Which.ToString().Should().NotContainAny(Secrets, $"{name} failed to connect, and that says where to and no more");
        }
    }

    [Fact]
    public void A_new_user_and_a_change_print_without_address_or_password()
    {
        // A record prints its members, and a record in a log message is printed.
        var user = new SupabaseNewUser(Address, Id: Ada, Password: Password);
        var unnamed = new SupabaseNewUser(Address, Password: Password);
        var change = new SupabaseUserChange(Password: NewPassword, AppMetadata: new Dictionary<string, object?> { ["crews_role"] = "operator" });

        user.ToString().Should().Be("SupabaseNewUser { Id = 8051a7e8-8599-4ad8-b169-68dc96beb0c5 }");
        unnamed.ToString().Should().Be("SupabaseNewUser { Id = (chosen by Auth) }");
        $"{change}".Should().Be("SupabaseUserChange { Password = (set), AppMetadata = (set) }");
        new SupabaseUserChange().ToString().Should().Be("SupabaseUserChange { Password = (unchanged), AppMetadata = (unchanged) }");
    }

    [Theory]
    [InlineData("kq7zzv\nbroken-over-two-lines")]
    [InlineData("kq7zzv with spaces")]
    [InlineData("kq7zzv-wïth-an-accent")]
    [InlineData("sb_publishable_kq7zzv")]
    public void A_key_that_cannot_be_the_secret_one_is_refused_without_repeating_it(string key)
    {
        var making = () => new SupabaseAuthAdmin(StubAuthServer.Project, key, _auth);

        var refused = making.Should().Throw<ArgumentException>().WithParameterName("secretKey");
        refused.Which.ToString().Should().NotContain("kq7zzv", "a key that was mistyped is still mostly the key");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void An_empty_key_is_refused(string key)
    {
        var making = () => new SupabaseAuthAdmin(StubAuthServer.Project, key, _auth);

        making.Should().Throw<ArgumentException>().WithParameterName("secretKey");
    }

    [Fact]
    public async Task The_key_pasted_with_a_line_end_is_the_key_without_it()
    {
        _auth.Answers(200, "{}");
        using var admin = new SupabaseAuthAdmin(StubAuthServer.Project, StubAuthServer.SecretKey + "\r\n", _auth);

        await admin.DeleteUserAsync(Ada, Cancellation);

        _auth.Request.Headers["apikey"].Should().Be(StubAuthServer.SecretKey);
        _auth.Request.Headers["Authorization"].Should().Be("Bearer " + StubAuthServer.SecretKey);
    }

    [Fact]
    public void An_address_with_a_password_in_it_is_refused_without_repeating_it()
    {
        var byProject = () => new SupabaseAuthAdmin(
            new SupabaseAuthOptions { ProjectUrl = "https://postgres:the-database-password@project.example.test" }, StubAuthServer.SecretKey, _auth);
        var byAuthUrl = () => new SupabaseAuthAdmin(
            new SupabaseAuthOptions { ProjectUrl = StubAuthServer.ProjectUrl, AuthUrl = "https://postgres:the-database-password@auth.example.test" }, StubAuthServer.SecretKey, _auth);

        byProject.Should().Throw<ArgumentException>().WithParameterName("projectUrl").Which.Message.Should().NotContain("the-database-password");
        byAuthUrl.Should().Throw<ArgumentException>().WithParameterName("authUrl").Which.Message.Should().NotContain("the-database-password");
    }

    /// <summary>
    /// Listens to what .NET's HTTP client reports about every request it sends: the events a host's
    /// diagnostics or an attached trace collector receives without the application logging anything.
    /// </summary>
    private sealed class HttpTelemetry : EventListener
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.Net.Http")
            {
                EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
            => Lines?.Enqueue($"{eventData.EventName}: {string.Join(" | ", eventData.Payload?.Select(value => value?.ToString()) ?? [])}");
    }
}
