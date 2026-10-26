using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Auth.Supabase.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase.Tests;

/// <summary>
/// The two kinds of token through <see cref="SupabaseTokenValidator"/>, the way a function or a worker checks
/// one without a web framework: what a host with the secret takes, where the keys are fetched when Auth
/// answers elsewhere, and what comes out when Auth cannot be asked.
/// </summary>
/// <remarks>No Docker and no network: <see cref="PublishedKeysStub"/> is the validator's way to Auth.</remarks>
public sealed class SupabaseTokenValidatorKindsTests
{
    private readonly PublishedKeysStub _auth = new();
    private readonly ECDsaSecurityKey _published = AccessTokens.NewSigningKey("the-key-auth-publishes");

    public SupabaseTokenValidatorKindsTests() => _auth.Publishes(_published);

    [Fact]
    public async Task A_validator_given_the_secret_takes_both_kinds()
    {
        using var client = _auth.Client();
        var validator = new SupabaseTokenValidator(new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, JwtSecret = AccessTokens.Secret }, client);

        var withTheSecret = await validator.ValidateAsync(AccessTokens.SignedWithTheSecret());

        withTheSecret.IsValid.Should().BeTrue(withTheSecret.Error?.Message);
        withTheSecret.Caller.Should().BeEquivalentTo(new { Kind = CallerKind.User, UserId = (Guid?)AccessTokens.Ada });
        _auth.Fetches.Should().Be(0, "a token signed with the secret is checked with the secret alone");

        var withAPublishedKey = await validator.ValidateAsync(AccessTokens.SignedWith(_published));

        withAPublishedKey.IsValid.Should().BeTrue(withAPublishedKey.Error?.Message);
        withAPublishedKey.Caller.Should().BeEquivalentTo(new { Kind = CallerKind.User, UserId = (Guid?)AccessTokens.Ada });
        _auth.Asked.Should().Equal(new Uri(AccessTokens.KeysAddress));
    }

    [Fact]
    public async Task A_validator_without_the_secret_refuses_a_token_signed_with_one()
    {
        using var client = _auth.Client();
        var validator = new SupabaseTokenValidator(new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl }, client);

        (await validator.ValidateAsync(AccessTokens.SignedWithTheSecret())).Caller.Should().BeSameAs(Caller.Anonymous);
        (await validator.ValidateAsync(AccessTokens.SignedWith(_published))).Caller.Kind.Should().Be(CallerKind.User);
    }

    [Fact]
    public async Task A_validator_told_where_auth_answers_fetches_the_keys_there_and_keeps_the_projects_issuer()
    {
        using var client = _auth.Client();
        var validator = new SupabaseTokenValidator(
            new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, AuthUrl = "http://auth.example.test:9999", AllowPlainHttp = true }, client);

        (await validator.ValidateAsync(AccessTokens.SignedWith(_published))).IsValid.Should().BeTrue();

        validator.Issuer.Should().Be(AccessTokens.Issuer);
        _auth.Asked.Should().Equal(new Uri("http://auth.example.test:9999/.well-known/jwks.json"));
    }

    [Fact]
    public async Task A_token_that_cannot_be_checked_because_auth_does_not_answer_is_nobodys_and_says_why()
    {
        _auth.IsDown();
        using var client = _auth.Client();
        var validator = new SupabaseTokenValidator(new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, JwtSecret = AccessTokens.Secret }, client);

        var validation = await validator.ValidateAsync(AccessTokens.SignedWith(_published));

        validation.IsValid.Should().BeFalse();
        validation.Caller.Should().BeSameAs(Caller.Anonymous, "a function answers such a request as it answers one with no token, and does not fail");
        validation.Error.Should().BeOfType<SecurityTokenSignatureKeyNotFoundException>().Which.Message.Should().Contain(AccessTokens.KeysAddress);

        (await validator.CallerOfAsync("Bearer " + AccessTokens.SignedWithTheSecret())).Kind.Should().Be(CallerKind.User, "the secret needs no Auth server");
    }

    [Fact]
    public void An_auth_address_that_is_not_one_is_refused_when_the_validator_is_registered()
    {
        var register = () => new ServiceCollection().AddSupabaseAuth(AccessTokens.ProjectUrl, options => options.AuthUrl = "auth.example.test");

        register.Should().Throw<ArgumentException>().WithParameterName("authUrl");
        SupabaseTokens.KeysAddressOf("https://abc.supabase.co/auth/v1/").Should().Be("https://abc.supabase.co/auth/v1/.well-known/jwks.json");
        SupabaseTokens.KeysAddressOf(SupabaseTokens.IssuerOf("http://127.0.0.1:54321")).Should().Be("http://127.0.0.1:54321/auth/v1/.well-known/jwks.json");
    }

    [Fact]
    public void Plain_http_to_another_machine_is_refused_when_the_validator_is_registered_unless_the_host_says_the_network_is_its_own()
    {
        var register = (Action<SupabaseAuthOptions> configure) => () => new ServiceCollection().AddSupabaseAuth(AccessTokens.ProjectUrl, configure);

        register(options => options.AuthUrl = "http://auth.example.test:9999")
            .Should().Throw<ArgumentException>().WithParameterName("authUrl").WithMessage("*unencrypted*AllowPlainHttp*");
        FluentActions.Invoking(() => new ServiceCollection().AddSupabaseAuth("http://project.example.test"))
            .Should().Throw<ArgumentException>().WithMessage("*unencrypted*", "the project's own address is where Auth answers when no other is given");

        register(options => options.AuthUrl = "http://localhost:9999").Should().NotThrow("nothing travels to an address on this machine");
        register(options =>
        {
            options.AuthUrl = "http://auth.example.test:9999";
            options.AllowPlainHttp = true;
        }).Should().NotThrow();

        SupabaseTokens.KeysAddressOf("http://auth.example.test:9999", allowPlainHttp: true).Should().Be("http://auth.example.test:9999/.well-known/jwks.json");
    }

    [Fact]
    public void The_validator_takes_the_project_as_the_bearer_and_the_admin_client_take_it()
    {
        var register = (SupabaseAuthOptions project) => () => new ServiceCollection().AddSupabaseAuth(project);

        register(new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, AuthUrl = "auth.example.test" })
            .Should().Throw<ArgumentException>().WithParameterName("authUrl");
        register(new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, AuthUrl = "http://auth.example.test:9999" })
            .Should().Throw<ArgumentException>().WithMessage("*unencrypted*AllowPlainHttp*");
        register(new SupabaseAuthOptions { ProjectUrl = AccessTokens.ProjectUrl, AuthUrl = "http://auth.example.test:9999", AllowPlainHttp = true })
            .Should().NotThrow();
        register(null!).Should().Throw<ArgumentNullException>().WithParameterName("supabase");
    }
}
