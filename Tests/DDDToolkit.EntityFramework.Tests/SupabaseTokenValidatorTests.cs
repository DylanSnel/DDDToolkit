using System.Text.Json;
using DDDToolkit.Auth.Supabase;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseTestTokens;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Supabase Auth's tokens checked without a web framework, the way a function or a worker checks them.
/// A small server stands in for a project's Auth: it publishes the discovery document and the signing
/// key at the paths a hosted project does, and the validator finds the key there on its own.
/// </summary>
public sealed class SupabaseTokenValidatorTests : IAsyncLifetime
{
    private static readonly Guid Alice = SupabaseRowLevelSecurityDatabase.Alice;

    private readonly ECDsaSecurityKey _projectKey = NewSigningKey("project-key");
    private WebApplication? _project;
    private string _projectUrl = "";

    private SupabaseTokenValidator Validator => new(new SupabaseAuthOptions { ProjectUrl = _projectUrl });

    private string Issuer => _projectUrl + "/auth/v1";

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _project = builder.Build();

        _project.MapGet("/auth/v1/.well-known/openid-configuration", (HttpRequest request) =>
        {
            var issuer = $"{request.Scheme}://{request.Host}/auth/v1";
            return Results.Text(JsonSerializer.Serialize(new { issuer, jwks_uri = issuer + "/.well-known/jwks.json" }), "application/json");
        });
        _project.MapGet("/auth/v1/.well-known/jwks.json", () => Results.Text(JwksOf(_projectKey), "application/json"));

        await _project.StartAsync(TestContext.Current.CancellationToken);
        _projectUrl = _project.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
    }

    public async ValueTask DisposeAsync()
    {
        if (_project is not null)
        {
            await _project.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_token_signed_with_a_key_the_project_publishes_is_its_user_with_the_claims_as_signed()
    {
        var validation = await Validator.ValidateAsync(Signed(Alice, _projectKey, Issuer));

        validation.IsValid.Should().BeTrue();
        validation.Caller.Kind.Should().Be(CallerKind.User);
        validation.Identity!.Name.Should().Be(Alice.ToString(), "sub is the name, as it is in ASP.NET Core");

        using var claims = JsonDocument.Parse(validation.Caller.Claims!);
        claims.RootElement.GetProperty("app_metadata").GetProperty("teams")[0].GetString().Should().Be("north");
    }

    [Fact]
    public async Task A_token_the_project_did_not_sign_or_did_not_sign_for_this_is_nobody()
    {
        var validator = Validator;
        var tokens = new Dictionary<string, string>
        {
            ["signed with a key the project does not publish"] = Signed(Alice, NewSigningKey("project-key"), Issuer),
            ["issued by another project"] = Signed(Alice, _projectKey, "https://someone-else.supabase.co/auth/v1"),
            ["for another audience"] = Signed(Alice, _projectKey, Issuer, audience: "anon"),
            ["expired"] = Signed(Alice, _projectKey, Issuer, expires: DateTime.UtcNow.AddMinutes(-10)),
        };

        foreach (var (why, token) in tokens)
        {
            var validation = await validator.ValidateAsync(token);

            validation.IsValid.Should().BeFalse(why);
            validation.Caller.Should().BeSameAs(Caller.Anonymous, why);
            validation.Error.Should().NotBeNull(why);
        }
    }

    [Fact]
    public async Task The_local_stack_signs_with_its_secret()
    {
        var validator = new SupabaseTokenValidator(new SupabaseAuthOptions { ProjectUrl = LocalProjectUrl, JwtSecret = LocalJwtSecret });

        (await validator.ValidateAsync(Local(Alice))).Caller.Kind.Should().Be(CallerKind.User);
        (await validator.ValidateAsync(Local(Alice, secret: "somebody-else's-secret-that-is-also-32-characters-long"))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task An_authorization_header_makes_a_user_only_with_a_valid_bearer_token()
    {
        var validator = Validator;

        (await validator.CallerOfAsync("Bearer " + Signed(Alice, _projectKey, Issuer))).Kind.Should().Be(CallerKind.User);
        (await validator.CallerOfAsync("bearer " + Signed(Alice, _projectKey, Issuer))).Kind.Should().Be(CallerKind.User, "the scheme is not case sensitive");
        (await validator.CallerOfAsync(null)).Should().BeSameAs(Caller.Anonymous);
        (await validator.CallerOfAsync("Basic YWxpY2U6c2VjcmV0")).Should().BeSameAs(Caller.Anonymous);
        (await validator.CallerOfAsync("Bearer not-a-token")).Should().BeSameAs(Caller.Anonymous);
    }

    [Fact]
    public void A_project_url_that_is_not_one_is_refused_when_it_is_registered()
    {
        var register = () => new ServiceCollection().AddSupabaseAuth("<ref>.supabase.co");

        register.Should().Throw<ArgumentException>().WithMessage("*https://<ref>.supabase.co*");
        SupabaseTokens.IssuerOf("https://abc.supabase.co/").Should().Be("https://abc.supabase.co/auth/v1");
        SupabaseTokens.IssuerOf("https://abc.supabase.co/auth/v1").Should().Be("https://abc.supabase.co/auth/v1");
    }
}
