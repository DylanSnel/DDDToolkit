using System.Net;
using System.Text;
using DDDToolkit.Auth.Supabase.AspNetCore;
using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// The bearer scheme <c>AddSupabaseJwtBearer</c> registers, asked the way a client asks a host: a request
/// with a token, to an application on Kestrel. A token signed with the JWT secret and a token signed with a
/// key Auth publishes are both taken, each checked with its own kind of key, and either is the caller row
/// level security runs the request's queries as.
/// </summary>
/// <remarks>
/// No Docker: the test signs the tokens, and <see cref="PublishedKeysStub"/> is the scheme's way to Auth. The
/// rules themselves are <see cref="SupabaseTokenHandlerTests"/>'; what is asked here is that a host gets them
/// from each way of registering the scheme, and that the scheme's own settings reach the keys.
/// </remarks>
public sealed class SupabaseJwtBearerTests
{
    private readonly PublishedKeysStub _auth = new();
    private readonly ECDsaSecurityKey _published = AccessTokens.NewSigningKey("the-key-auth-publishes");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Ada => AccessTokens.Ada.ToString();

    public SupabaseJwtBearerTests() => _auth.Publishes(_published);

    // ---- Both kinds, by each way of registering ---------------------------------------------------------------

    [Fact]
    public async Task A_host_given_the_secret_takes_both_kinds_and_each_token_is_the_requests_caller()
    {
        // The project's URL and UseSupabaseJwtSecret: the registration a host on the legacy secret has had all along.
        await using var host = await BearerApplication.StartAsync(
            authentication => authentication.AddSupabaseJwtBearer(AccessTokens.ProjectUrl, jwt =>
            {
                jwt.UseSupabaseJwtSecret(AccessTokens.Secret);
                jwt.BackchannelHttpHandler = _auth;
            }),
            Cancellation);

        for (var times = 0; times < 3; times++)
        {
            (await host.AskAsync(AccessTokens.SignedWithTheSecret())).Should().BeEquivalentTo(new { Status = HttpStatusCode.OK, Body = $"{Ada}|authenticated {Ada}" });
        }

        _auth.Fetches.Should().Be(0, "a token signed with the secret is checked with the secret alone");

        for (var times = 0; times < 3; times++)
        {
            (await host.AskAsync(AccessTokens.SignedWith(_published))).Should().BeEquivalentTo(new { Status = HttpStatusCode.OK, Body = $"{Ada}|authenticated {Ada}" });
        }

        _auth.Asked.Should().Equal([new Uri(AccessTokens.KeysAddress)], "the keys are fetched from the project's Auth when the first token needs them, and kept");
        host.Refusals.Should().BeEmpty();
    }

    [Fact]
    public async Task A_host_without_the_secret_takes_a_published_keys_token_and_refuses_the_secrets()
    {
        // The project's URL and nothing else: a hosted project with signing keys.
        await using var host = await BearerApplication.StartAsync(
            authentication => authentication.AddSupabaseJwtBearer(AccessTokens.ProjectUrl, jwt => jwt.BackchannelHttpHandler = _auth),
            Cancellation);

        var refused = await host.AskAsync(AccessTokens.SignedWithTheSecret());

        refused.Status.Should().Be(HttpStatusCode.Unauthorized);
        refused.Challenge.Should().Contain("invalid_token");
        _auth.Fetches.Should().Be(0, "there is no secret among the keys Auth publishes, so it is not asked for one");

        (await host.AskAsync(AccessTokens.SignedWith(_published))).Should().BeEquivalentTo(new { Status = HttpStatusCode.OK, Body = $"{Ada}|authenticated {Ada}" });
    }

    [Fact]
    public async Task A_host_told_where_auth_answers_fetches_the_keys_there_and_keeps_the_projects_issuer()
    {
        // The project as its tokens name it, and an Auth server with no gateway in front of it, reached under
        // its name in a network of the host's own: a stack in containers.
        const string LocalProject = "http://127.0.0.1:54321";
        var options = new SupabaseAuthOptions { ProjectUrl = LocalProject, JwtSecret = AccessTokens.Secret, AuthUrl = "http://auth.example.test:9999/", AllowPlainHttp = true };
        await using var host = await BearerApplication.StartAsync(
            authentication => authentication.AddSupabaseJwtBearer(options, jwt => jwt.BackchannelHttpHandler = _auth),
            Cancellation);
        var issuer = SupabaseTokens.IssuerOf(LocalProject);

        // Read when the scheme was added: what the host does to the options afterwards is not the scheme's.
        options.AuthUrl = "http://somewhere-else.example.test";
        options.JwtSecret = null;
        options.AllowPlainHttp = false;

        (await host.AskAsync(AccessTokens.For(new SigningCredentials(_published, SecurityAlgorithms.EcdsaSha256), issuer: issuer))).Status.Should().Be(HttpStatusCode.OK);
        (await host.AskAsync(AccessTokens.For(new SigningCredentials(AccessTokens.SecretKey(), SecurityAlgorithms.HmacSha256), issuer: issuer))).Status.Should().Be(HttpStatusCode.OK);
        _auth.Asked.Should().Equal(new Uri("http://auth.example.test:9999/.well-known/jwks.json"));

        // Signed with the right key and named after where Auth answers instead of after the project: not this project's.
        (await host.AskAsync(AccessTokens.For(new SigningCredentials(_published, SecurityAlgorithms.EcdsaSha256), issuer: "http://auth.example.test:9999"))).Status.Should().Be(HttpStatusCode.Unauthorized);
        host.Refusals.Should().ContainSingle().Which.Should().BeOfType<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public async Task A_scheme_under_a_name_of_its_own_checks_both_kinds_too()
    {
        await using var host = await BearerApplication.StartAsync(
            authentication =>
            {
                authentication.Services.Configure<AuthenticationOptions>(options => options.DefaultScheme = "Supabase");
                authentication.AddSupabaseJwtBearer("Supabase", new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, JwtSecret = AccessTokens.Secret }, jwt => jwt.BackchannelHttpHandler = _auth);
            },
            Cancellation);

        (await host.AskAsync(AccessTokens.SignedWithTheSecret())).Status.Should().Be(HttpStatusCode.OK);
        (await host.AskAsync(AccessTokens.SignedWith(_published))).Status.Should().Be(HttpStatusCode.OK);
        host.Options("Supabase").TokenHandlers.Should().ContainSingle().Which.Should().BeOfType<SupabaseTokenHandler>();
    }

    // ---- What a host refuses ----------------------------------------------------------------------------------

    [Fact]
    public async Task Tokens_signed_with_the_wrong_key_or_the_wrong_way_are_refused_and_their_requests_are_nobodys()
    {
        await using var host = await BearerApplication.StartAsync(
            authentication => authentication.AddSupabaseJwtBearer(AccessTokens.ProjectUrl, jwt =>
            {
                jwt.UseSupabaseJwtSecret(AccessTokens.Secret);
                jwt.BackchannelHttpHandler = _auth;
            }),
            Cancellation);
        var point = _published.ECDsa.ExportParameters(includePrivateParameters: false).Q;
        var tokens = new Dictionary<string, string>
        {
            ["signed with somebody else's secret"] = AccessTokens.SignedWithTheSecret("somebody-else's-secret-that-is-also-32-characters-long"),
            ["signed with another key under the published key's name"] = AccessTokens.SignedWith(AccessTokens.NewSigningKey(_published.KeyId)),
            ["signed with a key nobody published"] = AccessTokens.SignedWith(AccessTokens.NewSigningKey("a-key-nobody-published")),
            ["signed with the published key as if it were a secret"] = AccessTokens.For(new SigningCredentials(new SymmetricSecurityKey([.. point.X!, .. point.Y!]), SecurityAlgorithms.HmacSha256)),
            ["signed by nobody"] = AccessTokens.For(signedWith: null),
            ["signed with the secret, another way than Auth signs"] = AccessTokens.For(new SigningCredentials(AccessTokens.SecretKey(), SecurityAlgorithms.HmacSha384)),
            ["signed with the published key for another project"] = AccessTokens.For(new SigningCredentials(_published, SecurityAlgorithms.EcdsaSha256), issuer: "https://another.example.test/auth/v1"),
            ["signed with the secret and past its end"] = AccessTokens.For(new SigningCredentials(AccessTokens.SecretKey(), SecurityAlgorithms.HmacSha256), endedAnHourAgo: true),
        };

        foreach (var (why, token) in tokens)
        {
            (await host.AskAsync(token)).Status.Should().Be(HttpStatusCode.Unauthorized, why);
            (await host.CallerOfAsync(token)).Should().Be("anon", why);
        }

        (await host.CallerOfAsync(AccessTokens.SignedWith(_published))).Should().Be($"authenticated {Ada}", "the real ones still pass");
        (await host.CallerOfAsync(AccessTokens.SignedWithTheSecret())).Should().Be($"authenticated {Ada}");
    }

    [Fact]
    public async Task Without_an_auth_server_a_host_takes_the_secrets_token_and_refuses_a_key_signed_one()
    {
        // No stand-in: the scheme's own client, and an address on this machine where nobody listens.
        await using var host = await BearerApplication.StartAsync(
            authentication => authentication.AddSupabaseJwtBearer(new SupabaseAuthOptions
            {
                ProjectUrl = "http://127.0.0.1:54321",
                JwtSecret = AccessTokens.Secret,
                AuthUrl = "http://127.0.0.1:9/auth/v1",
            }),
            Cancellation);
        var issuer = SupabaseTokens.IssuerOf("http://127.0.0.1:54321");

        (await host.AskAsync(AccessTokens.For(new SigningCredentials(AccessTokens.SecretKey(), SecurityAlgorithms.HmacSha256), issuer: issuer))).Status.Should().Be(HttpStatusCode.OK);
        host.Refusals.Should().BeEmpty();

        var refused = await host.AskAsync(AccessTokens.For(new SigningCredentials(_published, SecurityAlgorithms.EcdsaSha256), issuer: issuer));

        refused.Status.Should().Be(HttpStatusCode.Unauthorized);
        refused.Challenge.Should().Contain("The signature key was not found");
        host.Refusals.Should().ContainSingle().Which.Message.Should().Contain("http://127.0.0.1:9/auth/v1/.well-known/jwks.json");
    }

    // ---- The scheme's own settings ----------------------------------------------------------------------------

    [Fact]
    public async Task What_the_host_set_on_the_scheme_for_fetching_keys_is_how_the_keys_are_fetched()
    {
        await using var host = await BearerApplication.StartAsync(
            authentication => authentication.AddSupabaseJwtBearer(AccessTokens.ProjectUrl, jwt =>
            {
                jwt.BackchannelHttpHandler = _auth;
                jwt.BackchannelTimeout = TimeSpan.FromSeconds(7);
                jwt.AutomaticRefreshInterval = TimeSpan.FromHours(3);
                jwt.RefreshInterval = TimeSpan.FromMinutes(2);
                jwt.RefreshOnIssuerKeyNotFound = false;
            }),
            Cancellation);

        var options = host.Options();

        options.Authority.Should().BeNull("the scheme asks for the keys itself, and for nothing until a token needs them");
        options.ConfigurationManager.Should().BeNull();
        options.RequireHttpsMetadata.Should().BeTrue("the project's address is an https one");
        options.Backchannel.Timeout.Should().Be(TimeSpan.FromSeconds(7));
        options.TokenHandlers.Should().ContainSingle().Which.Should().BeOfType<SupabaseTokenHandler>().Which.Should().BeEquivalentTo(new
        {
            KeysAddress = AccessTokens.KeysAddress,
            RequireHttps = true,
            MapInboundClaims = false,
            AutomaticRefreshInterval = TimeSpan.FromHours(3),
            RefreshInterval = TimeSpan.FromMinutes(2),
            RefreshOnKeyNotFound = false,
        });
    }

    [Fact]
    public void Plain_http_to_another_machine_fails_when_the_scheme_is_added_unless_the_host_says_the_network_is_its_own()
    {
        var authentication = new ServiceCollection().AddAuthentication();
        var inTheClear = new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, AuthUrl = "http://auth.example.test:9999" };

        FluentActions.Invoking(() => authentication.AddSupabaseJwtBearer(inTheClear))
            .Should().Throw<ArgumentException>().WithParameterName("authUrl").WithMessage("*unencrypted*AllowPlainHttp*");

        // Where no Auth URL is given the project's own address is where Auth answers, and it is held to the same.
        FluentActions.Invoking(() => authentication.AddSupabaseJwtBearer("http://project.example.test"))
            .Should().Throw<ArgumentException>().WithMessage("*unencrypted*AllowPlainHttp*");

        // Not requiring https for metadata is ASP.NET Core's switch and says nothing about whose network it is.
        FluentActions.Invoking(() => authentication.AddSupabaseJwtBearer(inTheClear, jwt => jwt.RequireHttpsMetadata = false))
            .Should().Throw<ArgumentException>().WithParameterName("authUrl");

        // On this machine nothing travels, and a host that says so is taken at its word.
        FluentActions.Invoking(() => authentication.AddSupabaseJwtBearer("Local", new SupabaseAuthOptions { ProjectUrl = "http://127.0.0.1:54321" })).Should().NotThrow();
        FluentActions.Invoking(() => authentication.AddSupabaseJwtBearer("Private", new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, AuthUrl = "http://auth.example.test:9999", AllowPlainHttp = true })).Should().NotThrow();
    }

    [Fact]
    public async Task Keys_are_not_asked_for_in_the_clear_where_the_host_requires_https()
    {
        var local = new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, AuthUrl = "http://auth.example.test:9999", AllowPlainHttp = true };

        await using (var lenient = await BearerApplication.StartAsync(
            authentication => authentication.AddSupabaseJwtBearer(local, jwt => jwt.BackchannelHttpHandler = _auth), Cancellation))
        {
            lenient.Options().RequireHttpsMetadata.Should().BeFalse("the address the host gave for Auth is an http one, and the host said that it is meant");
            (await lenient.AskAsync(AccessTokens.SignedWith(_published))).Status.Should().Be(HttpStatusCode.OK);
            _auth.Asked.Should().Equal(new Uri("http://auth.example.test:9999/.well-known/jwks.json"));
        }

        var asked = _auth.Fetches;
        await using var strict = await BearerApplication.StartAsync(
            authentication => authentication.AddSupabaseJwtBearer(local, jwt =>
            {
                jwt.BackchannelHttpHandler = _auth;
                jwt.RequireHttpsMetadata = true;
            }),
            Cancellation);

        (await strict.AskAsync(AccessTokens.SignedWith(_published))).Status.Should().Be(HttpStatusCode.Unauthorized);
        _auth.Fetches.Should().Be(asked);
    }

    [Fact]
    public async Task Algorithms_the_host_left_out_of_the_scheme_stay_out()
    {
        await using var host = await BearerApplication.StartAsync(
            authentication => authentication.AddSupabaseJwtBearer(AccessTokens.ProjectUrl, jwt =>
            {
                jwt.UseSupabaseJwtSecret(AccessTokens.Secret);
                jwt.BackchannelHttpHandler = _auth;
                jwt.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256];
            }),
            Cancellation);

        (await host.AskAsync(AccessTokens.SignedWith(_published))).Status.Should().Be(HttpStatusCode.OK);
        (await host.AskAsync(AccessTokens.SignedWithTheSecret())).Status.Should().Be(HttpStatusCode.Unauthorized);
        host.Refusals.Should().ContainSingle().Which.Should().BeOfType<SecurityTokenInvalidAlgorithmException>();
    }

    [Fact]
    public async Task A_host_with_an_authority_of_its_own_has_the_published_keys_come_from_there()
    {
        await using var host = await BearerApplication.StartAsync(
            authentication => authentication.AddSupabaseJwtBearer(AccessTokens.ProjectUrl, jwt =>
            {
                jwt.UseSupabaseJwtSecret(AccessTokens.Secret);
                jwt.BackchannelHttpHandler = _auth;
                jwt.MetadataAddress = "https://discovery.example.test" + PublishedKeysStub.DiscoveryPath;
            }),
            Cancellation);

        // The secret's token first: the host's own authority is not asked for it either.
        (await host.AskAsync(AccessTokens.SignedWithTheSecret())).Status.Should().Be(HttpStatusCode.OK);
        _auth.Fetches.Should().Be(0, "a token signed with the secret is checked with the secret alone, whoever publishes keys");

        (await host.AskAsync(AccessTokens.SignedWith(_published))).Status.Should().Be(HttpStatusCode.OK);
        (await host.AskAsync(AccessTokens.SignedWithTheSecret())).Status.Should().Be(HttpStatusCode.OK);

        _auth.Asked.Select(address => address.Host).Distinct().Should().Equal(["discovery.example.test"], "the project's own address is not asked beside the host's");
    }

    [Fact]
    public void A_scheme_set_to_the_older_validators_is_refused()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddSupabaseJwtBearer(AccessTokens.ProjectUrl, jwt => jwt.UseSecurityTokenValidators = true);
        using var provider = services.BuildServiceProvider();

        var options = () => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        options.Should().Throw<InvalidOperationException>().WithMessage("*UseSecurityTokenValidators*");
    }

    [Fact]
    public void A_project_or_an_auth_address_that_cannot_work_fails_when_the_scheme_is_added()
    {
        var authentication = new ServiceCollection().AddAuthentication();

        FluentActions.Invoking(() => authentication.AddSupabaseJwtBearer("project.example.test")).Should().Throw<ArgumentException>().WithParameterName("projectUrl");
        FluentActions.Invoking(() => authentication.AddSupabaseJwtBearer(new SupabaseAuthOptions())).Should().Throw<ArgumentException>().WithParameterName("projectUrl");
        FluentActions.Invoking(() => authentication.AddSupabaseJwtBearer(new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, AuthUrl = "auth.example.test" }))
            .Should().Throw<ArgumentException>().WithParameterName("authUrl");
        FluentActions.Invoking(() => authentication.AddSupabaseJwtBearer((SupabaseAuthOptions)null!)).Should().Throw<ArgumentNullException>().WithParameterName("supabase");
        FluentActions.Invoking(() => new JwtBearerOptions().UseSupabaseJwtSecret(" ")).Should().Throw<ArgumentException>().WithParameterName("jwtSecret");
    }

    [Fact]
    public void The_secret_is_one_of_the_schemes_signing_keys_and_changes_nothing_else_about_it()
    {
        var options = new JwtBearerOptions { Authority = "https://kept.example.test" };

        options.UseSupabaseJwtSecret(AccessTokens.Secret);

        options.TokenValidationParameters.IssuerSigningKey.Should().BeOfType<SymmetricSecurityKey>()
            .Which.Key.Should().Equal(Encoding.UTF8.GetBytes(AccessTokens.Secret));
        options.TokenValidationParameters.ValidAlgorithms.Should().BeNull("which algorithms a kind is held to is the handler's to say, for both kinds");
        options.Authority.Should().Be("https://kept.example.test");
    }
}
