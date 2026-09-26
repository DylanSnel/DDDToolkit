using System.Security.Claims;
using System.Text;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase;

/// <summary>How to reach a project's Supabase Auth.</summary>
public sealed class SupabaseAuthOptions
{
    /// <summary>The project's URL, <c>https://&lt;ref&gt;.supabase.co</c>, or the local stack's, <c>http://127.0.0.1:54321</c>.</summary>
    public string ProjectUrl { get; set; } = "";

    /// <summary>
    /// The project's JWT secret, for the CLI's local stack and projects still on the legacy secret, whose
    /// tokens are signed with it rather than with a key the project publishes. Leave it unset otherwise:
    /// Supabase advises asymmetric signing keys rather than handing the secret to another service.
    /// </summary>
    public string? JwtSecret { get; set; }
}

/// <summary>
/// Validates the access tokens Supabase Auth issues, the ones supabase-js holds for a signed-in user,
/// without a web framework: issued by <c>{projectUrl}/auth/v1</c>, for the audience <c>authenticated</c>,
/// unexpired, and signed with a key the project publishes at <c>{projectUrl}/auth/v1/.well-known/jwks.json</c>,
/// or with <see cref="SupabaseAuthOptions.JwtSecret"/>. The keys are fetched once and again when a token
/// names one it has not seen, so rotating them needs nothing here.
/// </summary>
/// <remarks>
/// What comes out is the <see cref="Caller"/> row level security runs the application's queries
/// as. A token that does not validate is no user at all, <see cref="Caller.Anonymous"/>, the way a
/// request with a bad token is anonymous to an ASP.NET Core endpoint that allows anonymous callers.
/// </remarks>
public sealed class SupabaseTokenValidator
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _parameters;

    /// <summary>A validator for the project <paramref name="options"/> names.</summary>
    /// <param name="options">The project.</param>
    /// <param name="httpClient">Fetches the project's keys; a new one when left out.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The project URL is not an http or https URL.</exception>
    public SupabaseTokenValidator(SupabaseAuthOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        Issuer = SupabaseTokens.IssuerOf(options.ProjectUrl);
        _parameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = SupabaseTokens.Audience,
            NameClaimType = "sub",
            RoleClaimType = "role",
        };

        if (options.JwtSecret is { Length: > 0 } secret)
        {
            _parameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            _parameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
        }
        else
        {
            var documents = new HttpDocumentRetriever(httpClient ?? new HttpClient())
            {
                RequireHttps = Issuer.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.OrdinalIgnoreCase),
            };
            _parameters.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                Issuer + "/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                documents);
        }
    }

    /// <summary>The issuer tokens have to come from: <c>{projectUrl}/auth/v1</c>.</summary>
    public string Issuer { get; }

    /// <summary>Validates <paramref name="token"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="token"/> is empty.</exception>
    public async Task<SupabaseTokenValidation> ValidateAsync(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var result = await _handler.ValidateTokenAsync(token, _parameters).ConfigureAwait(false);
        if (!result.IsValid)
        {
            return new(Caller.Anonymous, Identity: null, result.Exception ?? new SecurityTokenValidationException("The token did not validate."));
        }

        var claims = SupabaseTokens.ClaimsOf(result.SecurityToken)
            ?? throw new InvalidOperationException($"A validated token of type {result.SecurityToken.GetType().Name} has no payload to read the claims from.");

        return new(Callers.FromClaims(claims), result.ClaimsIdentity, Error: null);
    }

    /// <summary>
    /// The caller an <c>Authorization</c> header makes: the user of a valid <c>Bearer</c> token, or
    /// <see cref="Caller.Anonymous"/> without one, or with one that does not validate.
    /// </summary>
    public async Task<Caller> CallerOfAsync(string? authorization)
        => SupabaseTokens.BearerOf(authorization) is { } token
            ? (await ValidateAsync(token).ConfigureAwait(false)).Caller
            : Caller.Anonymous;
}

/// <summary>What <see cref="SupabaseTokenValidator.ValidateAsync"/> found.</summary>
/// <param name="Caller">The token's user, or <see cref="Caller.Anonymous"/> when it did not validate.</param>
/// <param name="Identity">The token's claims as .NET sees them, for a valid token.</param>
/// <param name="Error">Why the token did not validate, or <see langword="null"/> when it did.</param>
public sealed record SupabaseTokenValidation(Caller Caller, ClaimsIdentity? Identity, Exception? Error)
{
    /// <summary>Whether the token validated.</summary>
    public bool IsValid => Error is null;
}
