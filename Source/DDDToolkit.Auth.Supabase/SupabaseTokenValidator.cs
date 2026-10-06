using System.Security.Claims;
using System.Text;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase;

/// <summary>
/// How to reach a project's Supabase Auth: one description of the project that the bearer scheme
/// (<c>AddSupabaseJwtBearer</c>), the token validator (<c>AddSupabaseAuth</c>) and the admin client
/// (<c>AddSupabaseAuthAdmin</c>) all take, so a host with more to say than the project's URL says it once
/// and hands the same object to each.
/// </summary>
/// <remarks>
/// Each of them reads it when it is called, and a later change to it changes nothing. What one of them has
/// no use for it passes by: the admin client checks no token, so it reads where Auth answers and not
/// <see cref="JwtSecret"/>.
/// </remarks>
public sealed class SupabaseAuthOptions
{
    /// <summary>
    /// The project's URL, <c>https://&lt;ref&gt;.supabase.co</c>, or the local stack's, <c>http://127.0.0.1:54321</c>.
    /// Its tokens name <c>{ProjectUrl}/auth/v1</c> as their issuer, and that is where its gateway serves Auth.
    /// </summary>
    public string ProjectUrl { get; set; } = "";

    /// <summary>
    /// The project's JWT secret, for the tokens that are signed with it: those of a project still on the
    /// legacy secret, and those a developer's machine signs itself. A token signed with a key Auth publishes
    /// is checked with that key whether or not a secret is set, so a host that has both kinds, as one beside
    /// the stack the Supabase CLI starts has, sets it and takes both. Leave it unset for a hosted project
    /// with signing keys: Supabase advises those rather than handing the secret to another service.
    /// </summary>
    public string? JwtSecret { get; set; }

    /// <summary>
    /// Where Supabase Auth answers, when that is not <c>{ProjectUrl}/auth/v1</c>: an Auth server with no
    /// gateway in front of it, which answers at its own root, or a project's gateway reached inside a network
    /// under another name than the one its tokens carry, which serves Auth under that name's <c>/auth/v1</c>,
    /// such as <c>http://kong:8000/auth/v1</c>. It is taken as it is written, with nothing added, so a
    /// gateway's address without its <c>/auth/v1</c> reaches no Auth. The keys Auth publishes are fetched from
    /// there, and <see cref="SupabaseAuthAdmin"/> sends its calls there. The issuer a token has to name stays the one of
    /// <see cref="ProjectUrl"/>. Leave it unset for a project, hosted or the stack the Supabase CLI starts:
    /// its gateway serves Auth at <c>{ProjectUrl}/auth/v1</c>.
    /// </summary>
    public string? AuthUrl { get; set; }

    /// <summary>
    /// Whether Auth is reached over plain http on a server that is not on this machine: for an Auth server on
    /// a private network of the host's own. Off: Auth is reached over https, or over plain http on
    /// <c>localhost</c> or a loopback address, where nothing travels, and any other address in plain http is
    /// refused when the host starts. Two things would travel unencrypted: the keys Auth publishes, which a
    /// token signed with a published key is checked with, so whoever is in between could sign in as anybody;
    /// and the secret key, which every call of <see cref="SupabaseAuthAdmin"/> sends. Turning this on says
    /// that the network in between is the host's own.
    /// </summary>
    public bool AllowPlainHttp { get; set; }

    /// <summary>A copy, for a registration that keeps what it was given: the host's later changes reach nothing.</summary>
    internal SupabaseAuthOptions Copy() => (SupabaseAuthOptions)MemberwiseClone();
}

/// <summary>
/// Validates the access tokens Supabase Auth issues, the ones supabase-js holds for a signed-in user,
/// without a web framework: issued by <c>{projectUrl}/auth/v1</c>, for the audience <c>authenticated</c>,
/// unexpired, and signed with a key the project publishes at <c>{projectUrl}/auth/v1/.well-known/jwks.json</c>,
/// or with <see cref="SupabaseAuthOptions.JwtSecret"/> when there is one. The keys are fetched once and again
/// when a token names one it has not seen, so rotating them needs nothing here.
/// </summary>
/// <remarks>
/// <para>
/// What comes out is the <see cref="Caller"/> row level security runs the application's queries
/// as. A token that does not validate is no user at all, <see cref="Caller.Anonymous"/>, the way a
/// request with a bad token is anonymous to an ASP.NET Core endpoint that allows anonymous callers.
/// </para>
/// <para>
/// Which of the two a token is checked with is decided by how its header says it was signed, each kind with
/// its own algorithms only: <see cref="SupabaseTokenHandler"/> has the rules. A token signed with the secret
/// is checked without asking Auth anything.
/// </para>
/// </remarks>
public sealed class SupabaseTokenValidator
{
    private readonly SupabaseTokenHandler _handler;
    private readonly TokenValidationParameters _parameters;

    /// <summary>A validator for the project <paramref name="options"/> names.</summary>
    /// <param name="options">The project.</param>
    /// <param name="httpClient">Fetches the project's keys; a new one when left out.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The project URL, or the Auth URL when one is given, is not an http or https URL; or the address Auth
    /// answers at is plain http to another machine and <see cref="SupabaseAuthOptions.AllowPlainHttp"/> is off.
    /// </exception>
    public SupabaseTokenValidator(SupabaseAuthOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        Issuer = SupabaseTokens.IssuerOf(options.ProjectUrl);
        _handler = new SupabaseTokenHandler(string.IsNullOrWhiteSpace(options.AuthUrl) ? Issuer : options.AuthUrl, httpClient, options.AllowPlainHttp);
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
