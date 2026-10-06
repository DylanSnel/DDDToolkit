using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// The client the admin client makes for itself, over a real connection to a server on this machine: the
/// other tests replace it with a stand-in, and what it does with a redirect or a cookie is its own.
/// </summary>
public sealed class SupabaseAuthAdminConnectionTests
{
    private const string Address = "ada.lindqvist@example.test";

    private static readonly Guid Ada = Guid.Parse("8051a7e8-8599-4ad8-b169-68dc96beb0c5");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A project whose Auth answers at <paramref name="server"/>'s root, with no gateway in front of it.</summary>
    private static SupabaseAuthOptions BareAuthAt(LoopbackServer server) => new() { ProjectUrl = "http://127.0.0.1:54321", AuthUrl = server.Url };

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task A_redirect_is_not_followed_so_the_key_goes_to_auths_address_alone(int status)
    {
        // The server the redirect points to would answer as Auth does, so following it would look like a success.
        await using var elsewhere = new LoopbackServer(LoopbackServer.Answer(200, AuthAnswers.User(Ada, Address)));
        await using var auth = new LoopbackServer(LoopbackServer.Answer(status, "", $"Location: {elsewhere.Url}/invite"));
        using var admin = new SupabaseAuthAdmin(BareAuthAt(auth), StubAuthServer.SecretKey);

        var refused = await admin.Invoking(a => a.InviteByEmailAsync(Address, options: null, Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>("a redirect is a refusal like any other");

        refused.Which.Status.Should().Be(status);
        refused.Which.Message.Should().Contain("redirect").And.Contain("AuthUrl").And.NotContain(elsewhere.Url, "where it points is the answer's own text");
        auth.Requests.Should().ContainSingle().Which.Should().ContainEquivalentOf("apikey: " + StubAuthServer.SecretKey);
        elsewhere.Requests.Should().BeEmpty("the runtime keeps apikey on a request it redirects, and the address too when the method stays");
    }

    [Fact]
    public async Task The_registered_client_reaches_auth_with_the_key_in_its_headers_and_the_address_in_its_body()
    {
        await using var auth = new LoopbackServer(LoopbackServer.Answer(200, AuthAnswers.User(Ada, Address)));
        await using var provider = new ServiceCollection()
            .AddSupabaseAuthAdmin(auth.Url, StubAuthServer.SecretKey)
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<SupabaseAuthAdmin>()
            .InviteByEmailAsync(Address, new SupabaseInvitation("https://crews.example.test/welcome"), Cancellation);

        result.Should().Be(new SupabaseInvitationResult.Sent(Ada));

        var request = auth.Requests.Should().ContainSingle().Which;
        var requestLine = request[..request.IndexOf("\r\n", StringComparison.Ordinal)];
        requestLine.Should().Be(
            "POST /auth/v1/invite?redirect_to=https%3A%2F%2Fcrews.example.test%2Fwelcome HTTP/1.1",
            "the request line is what a server's access log keeps, and it has neither the address nor the key");
        request.Should().ContainEquivalentOf("\r\napikey: " + StubAuthServer.SecretKey + "\r\n");
        request.Should().ContainEquivalentOf("\r\nAuthorization: Bearer " + StubAuthServer.SecretKey + "\r\n");
        request.Should().ContainEquivalentOf("\r\nContent-Type: application/json; charset=utf-8\r\n");
        request.Should().EndWith("\r\n\r\n{\"email\":\"" + Address + "\"}");
    }

    [Fact]
    public async Task A_cookie_from_one_answer_is_not_sent_with_the_next_call()
    {
        // The admin API sets none. Something in front of it might, and a client that lives as long as the
        // host would then carry it into every later call.
        await using var auth = new LoopbackServer(LoopbackServer.Answer(200, AuthAnswers.User(Ada, Address), "Set-Cookie: route=one; Path=/"));
        using var admin = new SupabaseAuthAdmin(BareAuthAt(auth), StubAuthServer.SecretKey);

        await admin.FindUserAsync(Ada, Cancellation);
        await admin.FindUserAsync(Ada, Cancellation);

        auth.Requests.Should().HaveCount(2);
        auth.Requests.Should().AllSatisfy(request => request.Should().NotContainEquivalentOf("cookie"));
    }
}
