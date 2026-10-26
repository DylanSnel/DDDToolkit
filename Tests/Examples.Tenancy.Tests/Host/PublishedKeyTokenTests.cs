using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDDToolkit.Auth.Supabase;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// The two kinds of token a developer's host meets under one issuer: the dev login's, signed with the local
/// secret, and the local Auth server's, signed with a key it publishes, as the stack the Supabase CLI starts
/// signs them. Each is checked with its own kind of key, and the secret's never asks Auth for anything.
/// </summary>
/// <remarks>
/// The rules are the toolkit's bearer scheme's, and its own tests hold them; what is asked here is that the
/// sample's host, registered as <c>SampleAuthentication</c> registers it, has them. What stands in for Auth is
/// a listener of the test's own that publishes one key, and the test signs the tokens itself: so a test says
/// which key signed what, and counts how often the host asked for the keys.
/// <c>SupabaseStackTests</c> and <c>SampleOnSupabaseTests</c> prove the same against Supabase's own Auth
/// image, and <c>SampleOnTheCliStackTests</c> against the Auth of the stack the Supabase CLI starts.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class PublishedKeyTokenTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    /// <summary>The local stack's secret, as appsettings.Development.json has it.</summary>
    private const string LocalSecret = "super-secret-jwt-token-with-at-least-32-characters-long";

    private const string KeyId = "the-key-auth-publishes";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_token_signed_with_a_published_key_and_one_signed_with_the_secret_are_both_taken()
    {
        using var published = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var auth = await PublishingAsync(published);
        await using var host = await sample.StartAsync(settings: new Dictionary<string, string> { [SampleAuthentication.AuthUrlSetting] = auth.Url });

        // The dev login first, and often: its tokens are signed with the secret, and Auth is asked nothing.
        for (var times = 0; times < 3; times++)
        {
            using var dev = await host.ClientAsync(DemoPeople.Rhea.Key, tenant: null);
            (await TenantsSeatedInAsync(dev)).Should().Equal("harbor");
        }

        auth.Asked.Should().Be(0, "a token signed with the secret is checked with the secret alone");

        // The same person, with a token as Auth signs it: found by the key Auth publishes, fetched once and kept.
        var token = Token(new ECDsaSecurityKey(published) { KeyId = KeyId }, SecurityAlgorithms.EcdsaSha256);
        for (var times = 0; times < 3; times++)
        {
            using var real = host.Client(token, tenant: null);
            (await TenantsSeatedInAsync(real)).Should().Equal("harbor");
        }

        auth.Asked.Should().Be(1);
    }

    [Fact]
    public async Task A_token_signed_with_any_other_key_is_refused()
    {
        using var published = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var another = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var auth = await PublishingAsync(published);
        await using var host = await sample.StartAsync(settings: new Dictionary<string, string> { [SampleAuthentication.AuthUrlSetting] = auth.Url });

        // A key nobody published, under the published key's name and under a name of its own.
        (await StatusAsync(host, Token(new ECDsaSecurityKey(another) { KeyId = KeyId }, SecurityAlgorithms.EcdsaSha256))).Should().Be(HttpStatusCode.Unauthorized);
        (await StatusAsync(host, Token(new ECDsaSecurityKey(another) { KeyId = "unknown" }, SecurityAlgorithms.EcdsaSha256))).Should().Be(HttpStatusCode.Unauthorized);

        // A published key is public: used as a secret it signs nothing the host takes.
        var parameters = published.ExportParameters(includePrivateParameters: false);
        var publicKey = new SymmetricSecurityKey([.. parameters.Q.X!, .. parameters.Q.Y!]);
        (await StatusAsync(host, Token(publicKey, SecurityAlgorithms.HmacSha256))).Should().Be(HttpStatusCode.Unauthorized);

        // The header picks the kind of key, so it is held to the two ways of signing there are: a token that
        // says nobody signed it, and one signed with the secret in a way neither login signs, are nobody's.
        var secret = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(LocalSecret));
        new JsonWebToken(Token(signedWith: null)).Alg.Should().Be(SecurityAlgorithms.None);
        (await StatusAsync(host, Token(signedWith: null))).Should().Be(HttpStatusCode.Unauthorized);
        (await StatusAsync(host, Token(secret, SecurityAlgorithms.HmacSha384))).Should().Be(HttpStatusCode.Unauthorized);

        // And the real ones still pass.
        (await StatusAsync(host, Token(new ECDsaSecurityKey(published) { KeyId = KeyId }, SecurityAlgorithms.EcdsaSha256))).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(host, Token(secret, SecurityAlgorithms.HmacSha256))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_token_signed_with_a_published_key_is_held_to_the_issuer_the_audience_and_its_lifetime()
    {
        using var published = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var auth = await PublishingAsync(published);
        await using var host = await sample.StartAsync(settings: new Dictionary<string, string> { [SampleAuthentication.AuthUrlSetting] = auth.Url });
        var signedWith = new SigningCredentials(new ECDsaSecurityKey(published) { KeyId = KeyId }, SecurityAlgorithms.EcdsaSha256);

        // Signed as Auth signs, and still another project's, for another audience, or past its end: the key
        // changes what the signature is checked with, and nothing else the scheme asks of a token.
        (await StatusAsync(host, Token(signedWith, issuer: SupabaseTokens.IssuerOf("http://127.0.0.1:54399")))).Should().Be(HttpStatusCode.Unauthorized);
        (await StatusAsync(host, Token(signedWith, audience: "another-audience"))).Should().Be(HttpStatusCode.Unauthorized);
        (await StatusAsync(host, Token(signedWith, endedAnHourAgo: true))).Should().Be(HttpStatusCode.Unauthorized);

        (await StatusAsync(host, Token(signedWith))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_an_auth_server_a_token_signed_with_a_key_is_refused_and_the_dev_login_works()
    {
        // A host that is told an address where no Auth server answers: nothing publishes a key.
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var host = await sample.StartAsync(settings: new Dictionary<string, string> { [SampleAuthentication.AuthUrlSetting] = "http://127.0.0.1:9/auth/v1" });

        (await StatusAsync(host, Token(new ECDsaSecurityKey(signing) { KeyId = KeyId }, SecurityAlgorithms.EcdsaSha256))).Should().Be(HttpStatusCode.Unauthorized);

        using var dev = await host.ClientAsync(DemoPeople.Rhea.Key, tenant: null);
        (await TenantsSeatedInAsync(dev)).Should().Equal("harbor");
    }

    /// <summary>A token for Rhea as the local stack's Auth issues one, signed with <paramref name="key"/>.</summary>
    private static string Token(SecurityKey key, string algorithm)
        => Token(new SigningCredentials(key, algorithm));

    /// <summary>
    /// A token for Rhea as the local stack's Auth issues one, unless an argument says otherwise: signed with
    /// <paramref name="signedWith"/>, or by nobody when there is none.
    /// </summary>
    private static string Token(SigningCredentials? signedWith, string? issuer = null, string? audience = null, bool endedAnHourAgo = false)
    {
        var issued = endedAnHourAgo ? DateTime.UtcNow.AddHours(-2) : DateTime.UtcNow;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? SupabaseTokens.IssuerOf("http://127.0.0.1:54321"),
            Audience = audience ?? SupabaseTokens.Audience,
            IssuedAt = issued,
            Expires = issued.AddHours(1),
            Claims = new Dictionary<string, object> { ["sub"] = DemoPeople.Rhea.Id.ToString(), ["role"] = "authenticated" },
            SigningCredentials = signedWith,
        });
    }

    private static async Task<HttpStatusCode> StatusAsync(SampleFactory host, string token)
    {
        using var client = host.Client(token, tenant: null);
        using var response = await client.GetAsync("/me/seats", Cancellation);
        return response.StatusCode;
    }

    private static async Task<IEnumerable<string?>> TenantsSeatedInAsync(HttpClient client)
    {
        var seats = await client.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation);
        return [.. seats.EnumerateArray().Select(seat => seat.GetProperty("tenant").GetProperty("slug").GetString())];
    }

    /// <summary>A listener on this machine that publishes <paramref name="key"/> where Auth publishes its keys, and counts how often it was asked.</summary>
    private static async Task<PublishingAuth> PublishingAsync(ECDsa key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var keys = new
        {
            keys = new[]
            {
                new { kty = "EC", crv = "P-256", alg = "ES256", use = "sig", kid = KeyId, x = Base64UrlEncoder.Encode(parameters.Q.X), y = Base64UrlEncoder.Encode(parameters.Q.Y) },
            },
        };

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        var auth = new PublishingAuth(app);
        app.MapGet("/auth/v1/.well-known/jwks.json", () =>
        {
            auth.WasAsked();
            return Results.Json(keys);
        });

        await app.StartAsync(Cancellation);
        auth.Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/auth/v1";
        return auth;
    }

    private sealed class PublishingAuth(WebApplication app) : IAsyncDisposable
    {
        private int _asked;

        /// <summary>Where it answers: what the host is given as <c>Supabase:AuthUrl</c>.</summary>
        public string Url { get; set; } = "";

        /// <summary>How often the keys were fetched.</summary>
        public int Asked => Volatile.Read(ref _asked);

        public void WasAsked() => Interlocked.Increment(ref _asked);

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
