using System.Security.Cryptography;
using System.Text;
using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// The two kinds of token Supabase Auth signs, and what each is checked with: one signed with the JWT secret
/// with that secret and nothing else, one signed with a key Auth publishes with the published keys, and one
/// signed any other way with nothing at all.
/// </summary>
/// <remarks>
/// No Docker and no network: the test signs the tokens itself, and <see cref="PublishedKeysStub"/> stands in
/// for the place Auth publishes its keys and counts how often it is asked.
/// <c>SupabaseBearerOnAuthTests</c> asks Supabase's own Auth server.
/// </remarks>
public sealed class SupabaseTokenHandlerTests
{
    /// <summary>A limit for what has to happen. It is never reached while things work, so nothing waits on it.</summary>
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a test listens for a fetch that must not come. The library fetches again behind the token that
    /// asked, on a thread of its own, so nothing but time tells a fetch nobody asked for from one that is on
    /// its way: this is long for a request that never leaves the process, and one that does arrive ends the
    /// listening at once and fails the test.
    /// </summary>
    private static readonly TimeSpan ListeningTime = TimeSpan.FromSeconds(2);

    private readonly PublishedKeysStub _auth = new();
    private readonly ECDsaSecurityKey _published = AccessTokens.NewSigningKey("the-key-auth-publishes");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public SupabaseTokenHandlerTests() => _auth.Publishes(_published);

    // ---- The two kinds ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_token_signed_with_the_secret_is_checked_with_the_secret_and_nobody_is_asked()
    {
        var handler = Handler();

        for (var times = 0; times < 3; times++)
        {
            var result = await handler.ValidateTokenAsync(AccessTokens.SignedWithTheSecret(), Parameters());

            result.IsValid.Should().BeTrue(result.Exception?.Message);
            result.ClaimsIdentity.Name.Should().Be(AccessTokens.Ada.ToString(), "sub keeps its name, and is the user's");
        }

        (await handler.ValidateTokenAsync(AccessTokens.SignedWithTheSecret("somebody-else's-secret-that-is-also-32-characters-long"), Parameters())).IsValid.Should().BeFalse();

        _auth.Fetches.Should().Be(0, "a token signed with the secret is checked with the secret alone, right or wrong");
    }

    [Fact]
    public async Task A_token_signed_with_a_published_key_is_checked_with_the_keys_auth_publishes_fetched_once()
    {
        var rsa = AccessTokens.NewRsaSigningKey("an-rsa-key-auth-publishes");
        _auth.Publishes(_published, rsa);
        var handler = Handler();

        for (var times = 0; times < 3; times++)
        {
            var result = await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters());

            result.IsValid.Should().BeTrue(result.Exception?.Message);
            result.ClaimsIdentity.Name.Should().Be(AccessTokens.Ada.ToString());
            SupabaseTokens.ClaimsOf(result.SecurityToken).Should().Contain("\"app_metadata\":{\"provider\":\"email\"}", "the claims are there as they were signed");
        }

        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(rsa), Parameters())).IsValid.Should().BeTrue("a project's signing key is P-256 or RSA");

        handler.KeysAddress.Should().Be(AccessTokens.KeysAddress);
        _auth.Asked.Should().Equal([new Uri(AccessTokens.KeysAddress)], "the keys are fetched when the first token needs them, and kept");
    }

    [Fact]
    public async Task Whether_the_host_has_the_secret_changes_nothing_for_a_token_signed_with_a_published_key()
    {
        var handler = Handler();

        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters(withTheSecret: true))).IsValid.Should().BeTrue();
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters(withTheSecret: false))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Without_the_secret_a_token_signed_with_one_is_refused_and_nobody_is_asked()
    {
        var handler = Handler();

        var result = await handler.ValidateTokenAsync(AccessTokens.SignedWithTheSecret(), Parameters(withTheSecret: false));

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeAssignableTo<SecurityTokenSignatureKeyNotFoundException>("there is no secret to check it with");
        _auth.Fetches.Should().Be(0, "what Auth publishes is no secret, so there is nothing to ask it for");
    }

    // ---- What is refused --------------------------------------------------------------------------------------

    [Fact]
    public async Task A_token_signed_with_a_key_nobody_published_is_refused()
    {
        var handler = Handler();
        var another = AccessTokens.NewSigningKey(_published.KeyId);
        var unknown = AccessTokens.NewSigningKey("a-key-nobody-published");

        var underThePublishedName = await handler.ValidateTokenAsync(AccessTokens.SignedWith(another), Parameters());
        var underItsOwnName = await handler.ValidateTokenAsync(AccessTokens.SignedWith(unknown), Parameters());

        underThePublishedName.IsValid.Should().BeFalse();
        underThePublishedName.Exception.Should().BeOfType<SecurityTokenInvalidSignatureException>();
        underItsOwnName.IsValid.Should().BeFalse();
        underItsOwnName.Exception.Should().BeOfType<SecurityTokenSignatureKeyNotFoundException>();

        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters())).IsValid.Should().BeTrue("the real one still passes");
    }

    [Fact]
    public async Task A_published_key_used_as_a_secret_signs_nothing()
    {
        // A published key is public: anybody can read it and sign with what they read.
        var point = _published.ECDsa.ExportParameters(includePrivateParameters: false).Q;
        var forged = new Dictionary<string, SymmetricSecurityKey>
        {
            ["the key's point"] = new([.. point.X!, .. point.Y!]),
            ["the key as it is published"] = new(Encoding.UTF8.GetBytes(PublishedKeysStub.PublicHalfOf(_published))),
        };
        var handler = Handler();

        foreach (var (what, key) in forged)
        {
            var token = AccessTokens.For(new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            (await handler.ValidateTokenAsync(token, Parameters(withTheSecret: true))).IsValid.Should().BeFalse(what);
            (await handler.ValidateTokenAsync(token, Parameters(withTheSecret: false))).IsValid.Should().BeFalse(what);
        }

        _auth.Fetches.Should().Be(0, "a token that says it was signed with the secret is not held against a published key at all");
    }

    [Fact]
    public async Task A_secret_auth_would_publish_is_not_among_the_keys()
    {
        // Auth never publishes its secret. If a key set carried one anyway, the whole world could read it.
        var leaked = RandomNumberGenerator.GetBytes(48);
        _auth.PublishesDocument("{\"keys\":[" + PublishedKeysStub.PublicHalfOf(_published) + "," + PublishedKeysStub.SecretAsPublished("a-secret-in-the-key-set", leaked) + "]}");
        var handler = Handler();
        var signedWithIt = new SymmetricSecurityKey(leaked) { KeyId = "a-secret-in-the-key-set" };

        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters())).IsValid.Should().BeTrue("the key set was fetched, the secret in it included");

        (await handler.ValidateTokenAsync(AccessTokens.For(new SigningCredentials(signedWithIt, SecurityAlgorithms.HmacSha256)), Parameters())).IsValid.Should().BeFalse();
        (await handler.ValidateTokenAsync(AccessTokens.For(new SigningCredentials(signedWithIt, SecurityAlgorithms.HmacSha256)), Parameters(withTheSecret: false))).IsValid.Should().BeFalse();
        (await handler.ValidateTokenAsync(SaysItWasSignedWith(SecurityAlgorithms.EcdsaSha256, "a-secret-in-the-key-set", leaked), Parameters())).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_signed_any_other_way_is_refused_and_nobody_is_asked()
    {
        var rsa = AccessTokens.NewRsaSigningKey("an-rsa-key-auth-publishes");
        var larger = new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP384)) { KeyId = "a-larger-key-auth-publishes" };
        _auth.Publishes(_published, rsa, larger);
        var handler = Handler();
        var tokens = new Dictionary<string, string>
        {
            ["signed by nobody"] = AccessTokens.For(signedWith: null),
            ["signed with the secret, another way than Auth signs"] = AccessTokens.For(new SigningCredentials(AccessTokens.SecretKey(), SecurityAlgorithms.HmacSha384)),
            ["signed with a published key, on a curve Auth does not sign on"] = AccessTokens.SignedWith(larger, SecurityAlgorithms.EcdsaSha384),
            ["signed with a published key, another way than Auth signs"] = AccessTokens.SignedWith(rsa, SecurityAlgorithms.RsaSsaPssSha256),
        };

        new JsonWebToken(tokens["signed by nobody"]).Alg.Should().Be(SecurityAlgorithms.None);
        foreach (var (why, token) in tokens)
        {
            var result = await handler.ValidateTokenAsync(token, Parameters());

            result.IsValid.Should().BeFalse(why);
            result.Exception.Should().BeOfType<SecurityTokenInvalidAlgorithmException>(why);
        }

        _auth.Fetches.Should().Be(0, "a token of neither kind is refused for what its header says");
    }

    [Fact]
    public async Task The_header_picks_the_kind_of_key_and_the_signature_is_still_checked_with_it()
    {
        var handler = Handler();
        var secret = Encoding.UTF8.GetBytes(AccessTokens.Secret);

        // Signed with the secret, under a header that says a published key signed it, and the other way round.
        var saysPublished = SaysItWasSignedWith(SecurityAlgorithms.EcdsaSha256, _published.KeyId, secret);
        var saysSecret = Relabeled(AccessTokens.SignedWith(_published), SecurityAlgorithms.HmacSha256);

        (await handler.ValidateTokenAsync(saysPublished, Parameters())).IsValid.Should().BeFalse();
        (await handler.ValidateTokenAsync(saysSecret, Parameters())).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("three.parts.and-none-is-a-token")]
    public async Task A_token_that_cannot_be_read_is_nobodys_and_nobody_is_asked(string token)
    {
        var result = await Handler().ValidateTokenAsync(token, Parameters());

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<SecurityTokenMalformedException>();
        _auth.Fetches.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Either_kind_is_held_to_the_issuer_the_audience_and_its_lifetime(bool withTheSecret)
    {
        var handler = Handler();
        var signedWith = withTheSecret
            ? new SigningCredentials(AccessTokens.SecretKey(), SecurityAlgorithms.HmacSha256)
            : new SigningCredentials(_published, SecurityAlgorithms.EcdsaSha256);

        (await handler.ValidateTokenAsync(AccessTokens.For(signedWith, issuer: "https://another.example.test/auth/v1"), Parameters())).Exception.Should().BeOfType<SecurityTokenInvalidIssuerException>();
        (await handler.ValidateTokenAsync(AccessTokens.For(signedWith, audience: "another-audience"), Parameters())).Exception.Should().BeOfType<SecurityTokenInvalidAudienceException>();
        (await handler.ValidateTokenAsync(AccessTokens.For(signedWith, endedAnHourAgo: true), Parameters())).Exception.Should().BeOfType<SecurityTokenExpiredException>();

        (await handler.ValidateTokenAsync(AccessTokens.For(signedWith), Parameters())).IsValid.Should().BeTrue();
    }

    // ---- When Auth cannot be asked ----------------------------------------------------------------------------

    [Fact]
    public async Task Without_an_auth_server_the_secrets_token_is_accepted_and_a_key_signed_one_is_refused_with_the_reason()
    {
        _auth.IsDown();
        var handler = Handler();

        (await handler.ValidateTokenAsync(AccessTokens.SignedWithTheSecret(), Parameters())).IsValid.Should().BeTrue("the secret needs no Auth server");
        _auth.Fetches.Should().Be(0);

        var result = await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters());

        result.IsValid.Should().BeFalse();
        result.Exception.Should().BeOfType<SecurityTokenSignatureKeyNotFoundException>()
            .Which.Message.Should().Contain(AccessTokens.KeysAddress, "whoever reads the log learns where the keys were asked for");
        result.Exception.InnerException.Should().NotBeNull("and why they could not be fetched");
    }

    [Fact]
    public async Task A_public_key_the_host_holds_itself_checks_a_token_while_auth_cannot_be_asked()
    {
        _auth.IsDown();
        var handler = Handler();
        var parameters = Parameters();
        parameters.IssuerSigningKeys = [new ECDsaSecurityKey(ECDsa.Create(_published.ECDsa.ExportParameters(includePrivateParameters: false))) { KeyId = _published.KeyId }];

        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), parameters)).IsValid.Should().BeTrue();
        (await handler.ValidateTokenAsync(AccessTokens.SignedWithTheSecret(), parameters)).IsValid.Should().BeTrue("the secret beside it still checks its own kind");
    }

    [Fact]
    public async Task A_token_waits_behind_another_tokens_fetch_no_longer_than_the_clients_timeout()
    {
        _auth.SaysNothing();
        using var client = _auth.Client();
        client.Timeout = TimeSpan.FromMilliseconds(200);
        var handler = new SupabaseTokenHandler(AccessTokens.Issuer, client);

        // The first token's fetch is under way and stays so: it is stuck where its timeout does not reach.
        var first = handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters());
        (await _auth.IsAskedAsync(times: 1, within: Soon, Cancellation)).Should().BeTrue();

        // The second waits behind it, for as long as the client's timeout and no longer.
        var second = await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters()).WaitAsync(Soon, Cancellation);

        second.IsValid.Should().BeFalse();
        second.Exception.Should().BeOfType<SecurityTokenSignatureKeyNotFoundException>()
            .Which.Message.Should().Contain(AccessTokens.KeysAddress, "it says where the keys were asked for, as when nobody answers");
        second.Exception.InnerException.Should().BeAssignableTo<OperationCanceledException>("it stopped waiting; nobody refused it anything");
        first.IsCompleted.Should().BeFalse("the fetch it waited behind is still under way");
        _auth.Fetches.Should().Be(1, "it did not get as far as asking Auth itself");

        // Answered at last, the first ends too. Whether its answer still counts is its client's to say.
        _auth.AnswersAgain();
        await first.WaitAsync(Soon, Cancellation);
    }

    // ---- Where the keys travel --------------------------------------------------------------------------------

    [Fact]
    public async Task Keys_are_fetched_over_https_and_an_address_in_the_clear_is_not_asked_where_https_is_required()
    {
        new SupabaseTokenHandler("https://project.example.test/auth/v1").RequireHttps.Should().BeTrue();
        new SupabaseTokenHandler("https://project.example.test/auth/v1", allowPlainHttp: true).RequireHttps.Should().BeTrue("the address is what says how the keys travel");
        new SupabaseTokenHandler("http://127.0.0.1:54321/auth/v1").RequireHttps.Should().BeFalse("a local stack's address is in the clear, and nothing travels");

        // A host that requires https all the same does not ask an address in the clear.
        var handler = new SupabaseTokenHandler("http://localhost:9999/", _auth.Client()) { RequireHttps = true };
        handler.KeysAddress.Should().Be("http://localhost:9999/.well-known/jwks.json");

        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters())).IsValid.Should().BeFalse();
        _auth.Fetches.Should().Be(0);

        handler.RequireHttps = false;
        (await Eventually(() => handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters()))).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://auth.example.test:9999")]
    [InlineData("http://project.example.test/auth/v1")]
    [InlineData("HTTP://192.0.2.10:54321/auth/v1")]
    [InlineData("http://[2001:db8::10]:9999/")]
    public void Plain_http_to_another_machine_is_refused_when_the_handler_is_made(string authUrl)
    {
        var make = () => new SupabaseTokenHandler(authUrl, _auth.Client());
        var address = () => SupabaseTokens.KeysAddressOf(authUrl);

        make.Should().Throw<ArgumentException>().WithParameterName("authUrl")
            .Which.Message.Should().Contain("unencrypted").And.Contain("allowPlainHttp").And.Contain("AllowPlainHttp")
            .And.NotContain(new Uri(authUrl).Host, "the message does not repeat the URL");
        address.Should().Throw<ArgumentException>().WithParameterName("authUrl");
        _auth.Fetches.Should().Be(0, "nothing was asked for on the way to refusing");
    }

    [Theory]
    [InlineData("http://localhost:54321/auth/v1")]
    [InlineData("http://127.0.0.1:49999")]
    [InlineData("http://[::1]:9999/")]
    public async Task Plain_http_on_this_machine_is_taken_without_the_host_saying_anything(string authUrl)
    {
        var handler = new SupabaseTokenHandler(authUrl, _auth.Client());

        handler.RequireHttps.Should().BeFalse("nothing travels to an address on this machine");
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters())).IsValid.Should().BeTrue();
        _auth.Asked.Should().Equal(new Uri(SupabaseTokens.KeysAddressOf(authUrl)));
    }

    [Theory]
    [InlineData("http://auth.example.test:9999")]
    [InlineData("http://192.0.2.10:54321/auth/v1")]
    public async Task Plain_http_to_another_machine_is_taken_when_the_host_says_the_network_is_its_own(string authUrl)
    {
        var handler = new SupabaseTokenHandler(authUrl, _auth.Client(), allowPlainHttp: true);

        handler.RequireHttps.Should().BeFalse("the host said that the address in the clear is meant");
        handler.KeysAddress.Should().Be(SupabaseTokens.KeysAddressOf(authUrl, allowPlainHttp: true)).And.StartWith("http://");
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters())).IsValid.Should().BeTrue();
        _auth.Asked.Should().Equal(new Uri(handler.KeysAddress));
    }

    [Theory]
    [InlineData("")]
    [InlineData("project.example.test/auth/v1")]
    [InlineData("ftp://project.example.test/auth/v1")]
    public void An_address_auth_cannot_answer_at_is_refused_when_the_handler_is_made(string authUrl)
    {
        var make = () => new SupabaseTokenHandler(authUrl);

        make.Should().Throw<ArgumentException>().WithParameterName("authUrl");
    }

    // ---- When Auth's keys change ------------------------------------------------------------------------------

    [Fact]
    public async Task A_new_signing_key_is_found_when_a_token_names_it_and_a_stream_of_unknown_keys_asks_once()
    {
        var handler = Handler();
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters())).IsValid.Should().BeTrue();
        _auth.Fetches.Should().Be(1);

        // Auth gets a new key and signs with it; the old one stays published while its tokens are still good.
        var next = AccessTokens.NewSigningKey("the-next-key-auth-publishes");
        _auth.Publishes(_published, next);

        (await Eventually(() => handler.ValidateTokenAsync(AccessTokens.SignedWith(next), Parameters()))).IsValid.Should().BeTrue("a token that names a key that is not among the kept ones has the keys fetched again");
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters())).IsValid.Should().BeTrue();
        _auth.Fetches.Should().Be(2);

        // Anybody can send tokens that name keys nobody has. They are refused, and Auth is left alone.
        for (var times = 0; times < 5; times++)
        {
            (await handler.ValidateTokenAsync(AccessTokens.SignedWith(AccessTokens.NewSigningKey("a-key-nobody-published-" + times)), Parameters())).IsValid.Should().BeFalse();
        }

        (await _auth.IsAskedAsync(times: 3, within: ListeningTime, Cancellation)).Should().BeFalse("the keys are fetched again no more often than the refresh interval allows");
    }

    [Fact]
    public async Task A_key_auth_no_longer_publishes_is_refused_once_the_keys_were_fetched_again()
    {
        var handler = Handler();
        var old = AccessTokens.SignedWith(_published);
        (await handler.ValidateTokenAsync(old, Parameters())).IsValid.Should().BeTrue();

        // Auth takes the key away, as it does with one that leaked, and signs with another.
        var next = AccessTokens.NewSigningKey("the-next-key-auth-publishes");
        _auth.Publishes(next);
        (await Eventually(() => handler.ValidateTokenAsync(AccessTokens.SignedWith(next), Parameters()))).IsValid.Should().BeTrue();

        (await handler.ValidateTokenAsync(old, Parameters())).IsValid.Should().BeFalse("the kept keys are the ones Auth published last, and no earlier set is tried");
    }

    [Fact]
    public async Task A_handler_told_not_to_fetch_again_for_an_unknown_key_keeps_the_keys_it_has()
    {
        var handler = Handler();
        handler.RefreshOnKeyNotFound = false;
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), Parameters())).IsValid.Should().BeTrue();

        var next = AccessTokens.NewSigningKey("the-next-key-auth-publishes");
        _auth.Publishes(_published, next);

        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(next), Parameters())).IsValid.Should().BeFalse();
        (await _auth.IsAskedAsync(times: 2, within: ListeningTime, Cancellation)).Should().BeFalse("the handler was told not to ask again for a key it does not have");
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(next), Parameters())).IsValid.Should().BeFalse("the keys it has are the keys it keeps");

        // Told to after all, it asks for the very token it left alone before: being told is what held it back.
        handler.RefreshOnKeyNotFound = true;
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(next), Parameters())).IsValid.Should().BeFalse("the token that asks is not the one that gains by it");
        (await Eventually(() => handler.ValidateTokenAsync(AccessTokens.SignedWith(next), Parameters()))).IsValid.Should().BeTrue();
        _auth.Fetches.Should().Be(2);
    }

    // ---- What a host set on the parameters --------------------------------------------------------------------

    [Fact]
    public async Task Algorithms_the_host_left_out_stay_out_for_either_kind()
    {
        var rsa = AccessTokens.NewRsaSigningKey("an-rsa-key-auth-publishes");
        _auth.Publishes(_published, rsa);
        var handler = Handler();

        var ellipticOnly = Parameters();
        ellipticOnly.ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256];
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), ellipticOnly)).IsValid.Should().BeTrue();
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(rsa), ellipticOnly)).IsValid.Should().BeFalse();
        (await handler.ValidateTokenAsync(AccessTokens.SignedWithTheSecret(), ellipticOnly)).Exception.Should().BeOfType<SecurityTokenInvalidAlgorithmException>(
            "a kind whose algorithms are all left out is refused, not checked with any algorithm at all");

        var fetched = _auth.Fetches;
        var secretOnly = Parameters();
        secretOnly.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
        (await handler.ValidateTokenAsync(AccessTokens.SignedWithTheSecret(), secretOnly)).IsValid.Should().BeTrue();
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), secretOnly)).Exception.Should().BeOfType<SecurityTokenInvalidAlgorithmException>();
        _auth.Fetches.Should().Be(fetched);
    }

    [Fact]
    public async Task Parameters_with_a_configuration_manager_of_their_own_have_the_published_keys_come_from_there()
    {
        var theirs = AccessTokens.NewSigningKey("a-key-the-hosts-own-manager-has");
        var configuration = new OpenIdConnectConfiguration();
        configuration.SigningKeys.Add(theirs);
        var parameters = Parameters();
        parameters.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        var handler = Handler();

        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(theirs), parameters)).IsValid.Should().BeTrue();
        (await handler.ValidateTokenAsync(AccessTokens.SignedWithTheSecret(), parameters)).IsValid.Should().BeTrue();
        (await handler.ValidateTokenAsync(AccessTokens.SignedWith(_published), parameters)).IsValid.Should().BeFalse("the handler's own address is not asked beside the host's");
        _auth.Fetches.Should().Be(0);
    }

    [Fact]
    public async Task Claims_keep_their_names_unless_the_host_asks_for_dot_nets()
    {
        var handler = Handler();
        var kept = await handler.ValidateTokenAsync(AccessTokens.SignedWithTheSecret(), Parameters());
        kept.ClaimsIdentity.FindFirst("sub")!.Value.Should().Be(AccessTokens.Ada.ToString());

        handler.MapInboundClaims = true;
        var mapped = await handler.ValidateTokenAsync(AccessTokens.SignedWithTheSecret(), Parameters());
        mapped.ClaimsIdentity.FindFirst("sub").Should().BeNull();
        mapped.ClaimsIdentity.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value.Should().Be(AccessTokens.Ada.ToString());
    }

    [Fact]
    public async Task A_token_that_was_read_already_is_checked_as_it_was_sent()
    {
        var handler = Handler();
        var read = handler.ReadToken(AccessTokens.SignedWith(_published));

        read.Should().BeOfType<JsonWebToken>();
        (await handler.ValidateTokenAsync(read, Parameters())).IsValid.Should().BeTrue();
        (await handler.ValidateTokenAsync(handler.ReadToken(AccessTokens.SignedWithTheSecret("somebody-else's-secret-that-is-also-32-characters-long")), Parameters())).IsValid.Should().BeFalse();
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------

    private SupabaseTokenHandler Handler() => new(AccessTokens.Issuer, _auth.Client());

    /// <summary>What a scheme for the project checks besides the signature, with the project's secret unless a test leaves it out.</summary>
    private static TokenValidationParameters Parameters(bool withTheSecret = true) => new()
    {
        ValidIssuer = AccessTokens.Issuer,
        ValidAudience = SupabaseTokens.Audience,
        NameClaimType = "sub",
        RoleClaimType = "role",
        IssuerSigningKey = withTheSecret ? AccessTokens.SecretKey() : null,
    };

    /// <summary>
    /// The first answer that is valid, asked again for a few seconds: keys that are fetched again arrive for a
    /// token after the one that asked for them.
    /// </summary>
    private static async Task<TokenValidationResult> Eventually(Func<Task<TokenValidationResult>> validate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var result = await validate();
            if (result.IsValid || DateTime.UtcNow >= deadline)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Cancellation);
        }
    }

    /// <summary>
    /// A token for Ada whose header says <paramref name="algorithm"/> and names <paramref name="keyId"/>, and
    /// that was in truth signed with <paramref name="secret"/> as a secret signs.
    /// </summary>
    private static string SaysItWasSignedWith(string algorithm, string keyId, byte[] secret)
    {
        var payload = AccessTokens.SignedWithTheSecret().Split('.')[1];
        var header = Base64UrlEncoder.Encode($$"""{"alg":"{{algorithm}}","typ":"JWT","kid":"{{keyId}}"}""");
        var signature = Base64UrlEncoder.Encode(HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(header + "." + payload)));
        return header + "." + payload + "." + signature;
    }

    /// <summary><paramref name="token"/> with its signature as it was, under a header that says <paramref name="algorithm"/>.</summary>
    private static string Relabeled(string token, string algorithm)
    {
        var parts = token.Split('.');
        return Base64UrlEncoder.Encode($$"""{"alg":"{{algorithm}}","typ":"JWT"}""") + "." + parts[1] + "." + parts[2];
    }
}
