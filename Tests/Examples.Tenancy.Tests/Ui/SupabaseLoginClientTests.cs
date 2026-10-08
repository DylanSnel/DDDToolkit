using System.Net;
using System.Text.Json;
using Examples.Tenancy.Ui;
using Examples.Tenancy.Ui.Auth;
using Examples.Tenancy.Ui.Languages;
using Examples.Tenancy.Ui.Session;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Examples.Tenancy.Tests.Ui;

/// <summary>
/// The UI's sign-in with a password, as plain C# over a stub of Supabase Auth: what it sends, what it reads, what
/// it tells a person who is refused, in the session's language, and that it is offered only where the UI knows an Auth server. Against
/// Supabase's own Auth server it is used in <c>Supabase/SampleOnSupabaseTests</c>.
/// </summary>
public sealed class SupabaseLoginClientTests
{
    private const string Rhea = "d0000000-0000-4000-8000-000000000002";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sign_in_posts_the_password_grant_and_reads_the_token()
    {
        using var stub = StubApi.Answering(HttpStatusCode.OK, """
            {"access_token":"header.payload.signature","token_type":"bearer","expires_in":3600,"expires_at":1790000000,
             "refresh_token":"not-kept","user":{"id":"d0000000-0000-4000-8000-000000000002","aud":"authenticated","role":"authenticated","email":"rhea@example.test"}}
            """);
        var login = new SupabaseLoginClient(new HttpClient(stub) { BaseAddress = new Uri("http://auth.test/auth/v1/") }, new UiSession());

        var signedIn = await login.SignInAsync("  rhea@example.test ", " a password, as typed ", Cancellation);

        signedIn.Refusal.Should().BeNull();
        signedIn.Answer!.AccessToken.Should().Be("header.payload.signature");
        signedIn.Answer.Expires.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1790000000));
        signedIn.Answer.User!.Id.Should().Be(Guid.Parse(Rhea));

        var sent = stub.Requests.Should().ContainSingle().Subject;
        (sent.Method, sent.PathAndQuery, sent.Authorization).Should().Be(("POST", "/auth/v1/token?grant_type=password", null));
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        body.GetProperty("email").GetString().Should().Be("rhea@example.test");
        body.GetProperty("password").GetString().Should().Be(" a password, as typed ");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"code":400,"error_code":"invalid_credentials","msg":"Invalid login credentials"}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"code":400,"error_code":"email_not_confirmed","msg":"Email not confirmed"}""")]
    public async Task A_refused_sign_in_is_told_one_way_and_sent_once(HttpStatusCode status, string answer)
    {
        using var stub = StubApi.Answering(status, answer);
        var session = new UiSession();
        var login = new SupabaseLoginClient(new HttpClient(stub) { BaseAddress = new Uri("http://auth.test/") }, session);

        var signedIn = await login.SignInAsync("rhea@example.test", "wrong", Cancellation);

        // Whatever Auth's reason, the person learns nothing about whose address it is, and reads none of Auth's words.
        signedIn.Should().Be(PasswordSignIn.RefusedBecause("That e-mail address and password do not sign anyone in."));
        stub.Requests.Should().ContainSingle();

        // In the language the session reads, as every text of the page it is shown on.
        session.SelectLanguage("nl");
        (await login.SignInAsync("rhea@example.test", "wrong", Cancellation))
            .Should().Be(PasswordSignIn.RefusedBecause(UiTexts.For("login.password.refused", "nl")))
            .And.NotBe(signedIn, "the Dutch text is not the English one");
    }

    [Fact]
    public async Task The_sign_in_of_a_link_is_auths_to_vouch_for_and_a_chosen_password_goes_to_auth_with_it()
    {
        using var stub = new StubApi(request => StubApi.Response(HttpStatusCode.OK, request.Method == "GET"
            ? """{"id":"d0000000-0000-4000-8000-000000000002","aud":"authenticated","role":"authenticated","email":"wren@example.test"}"""
            : "{}"));
        var login = new SupabaseLoginClient(new HttpClient(stub) { BaseAddress = new Uri("http://auth.test/auth/v1/") }, new UiSession());
        const string Page = "http://ui.test/invitations/accept";

        // The address said who is signed in and until when; Auth is asked, and its answer is who the session is for.
        var signedIn = await login.SignInWithLinkAsync(AuthLink.Read(Page + "#a-token#access_token=header.payload.signature&expires_at=1790000000&type=invite"), Cancellation);
        signedIn.Refusal.Should().BeNull();
        (signedIn.Answer!.AccessToken, signedIn.Answer.Expires, signedIn.Answer.User!.Email)
            .Should().Be(("header.payload.signature", DateTimeOffset.FromUnixTimeSeconds(1790000000), "wren@example.test"));

        (await login.ChoosePasswordAsync("header.payload.signature", " a password, as typed ", Cancellation)).Should().BeNull();

        stub.Requests.Select(sent => (sent.Method, sent.PathAndQuery, sent.Authorization)).Should().Equal(
            ("GET", "/auth/v1/user", "Bearer header.payload.signature"),
            ("PUT", "/auth/v1/user", "Bearer header.payload.signature"));
        JsonDocument.Parse(stub.Requests[1].Body!).RootElement.GetProperty("password").GetString().Should().Be(" a password, as typed ");

        // A link without a sign-in, one whose sign-in no header can carry, and a password with no sign-in to
        // send it with: Auth is asked nothing.
        var refused = PasswordSignIn.RefusedBecause(UiTexts.For("login.link.refused", "en"));
        (await login.SignInWithLinkAsync(AuthLink.Read(Page + "#a-token"), Cancellation)).Should().Be(refused);
        (await login.SignInWithLinkAsync(AuthLink.Read(Page + "#access_token=header%0Apayload&expires_at=1790000000"), Cancellation)).Should().Be(refused);
        (await login.ChoosePasswordAsync(null, "a password", Cancellation)).Should().Be(UiTexts.For("login.password.session-over", "en"));
        stub.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task It_is_offered_only_where_the_ui_knows_an_auth_server_and_a_failed_sign_in_is_never_retried()
    {
        // Registered as the UI registers it, after the service defaults, which give every client a standard
        // resilience handler that retries a 503.
        using var stub = StubApi.Answering(HttpStatusCode.ServiceUnavailable);
        IHost Ui(params (string Key, string Value)[] settings)
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            foreach (var (key, value) in settings)
            {
                builder.Configuration[key] = value;
            }

            builder.AddServiceDefaults();
            builder.Services.AddSampleUi();
            builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => stub));
            return builder.Build();
        }

        // A UI that nothing tells where Auth is: the page shows the dev login alone, and nothing is sent.
        using (var withoutAuth = Ui())
        {
            var login = withoutAuth.Services.GetRequiredService<SupabaseLoginClient>();
            login.IsOffered.Should().BeFalse();
            (await login.SignInAsync("rhea@example.test", "a password", Cancellation)).Answer.Should().BeNull();
            stub.Requests.Should().BeEmpty();
        }

        using (var withAuth = Ui((SupabaseLoginClient.AuthUrlSetting, "http://auth.test")))
        {
            var login = withAuth.Services.GetRequiredService<SupabaseLoginClient>();
            login.IsOffered.Should().BeTrue();
            (await login.SignInAsync("rhea@example.test", "a password", Cancellation)).Should().Be(PasswordSignIn.RefusedBecause("Supabase Auth answered 503."));
            stub.Requests.Should().ContainSingle("a sign-in is sent once").Which.PathAndQuery.Should().Be("/token?grant_type=password");
        }

        // A project's Auth answers under its URL.
        SupabaseLoginClient.AuthAddressOf(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [SupabaseLoginClient.UrlSetting] = "https://tenancy.example.test" }).Build())
            .Should().Be(new Uri("https://tenancy.example.test/auth/v1/"));
    }
}
