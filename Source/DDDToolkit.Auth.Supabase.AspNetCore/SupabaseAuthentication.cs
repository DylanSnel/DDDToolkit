using System.Text;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace DDDToolkit.Auth.Supabase.AspNetCore;

/// <summary>
/// Supabase Auth in ASP.NET Core: a JWT bearer scheme for the access tokens Supabase Auth issues, the
/// ones supabase-js sends, and each request's user as the caller row level security runs its queries as.
/// <code>
/// builder.Services.AddAuthentication().AddSupabaseJwtBearer("https://&lt;ref&gt;.supabase.co");
/// builder.Services.AddSupabaseRowLevelSecurity();   // or AddPostgresRowLevelSecurity() off Supabase
/// builder.Services.AddDbContext&lt;OrderingContext&gt;((provider, options) => options
///     .UseNpgsql(connectionString)
///     .UseSupabaseRowLevelSecurity(provider));
///
/// app.UseAuthentication();
/// </code>
/// </summary>
public static class SupabaseAuthentication
{
    /// <summary>
    /// Adds a JWT bearer scheme named <c>Bearer</c> for Supabase Auth's access tokens. See
    /// <see cref="AddSupabaseJwtBearer(AuthenticationBuilder, string, string, Action{JwtBearerOptions}?)"/>.
    /// </summary>
    public static AuthenticationBuilder AddSupabaseJwtBearer(this AuthenticationBuilder builder, string projectUrl, Action<JwtBearerOptions>? configure = null)
        => builder.AddSupabaseJwtBearer(JwtBearerDefaults.AuthenticationScheme, projectUrl, configure);

    /// <summary>
    /// Adds a JWT bearer scheme for Supabase Auth's access tokens: issued by <c>{projectUrl}/auth/v1</c>
    /// for the audience <c>authenticated</c>, and signed with one of the keys the project publishes at
    /// <c>{projectUrl}/auth/v1/.well-known/jwks.json</c>. Claims keep the names they have in the token, so
    /// <c>sub</c> is <c>sub</c>, the user's id and the principal's name.
    /// </summary>
    /// <remarks>
    /// It also makes the request's user the caller for row level security: a request whose token this
    /// scheme validated runs its queries as that user, any other request as <c>anon</c>, and work outside
    /// a request as the system, unless code made another caller current with
    /// <see cref="Callers.Begin"/>, which always wins. Each token that passes has its claims kept
    /// exactly as they were signed, so Postgres sees what PostgREST would have seen.
    /// <para>
    /// A project still on the legacy JWT secret publishes no keys, and nor does the Supabase CLI's local
    /// stack unless it is given signing keys; call <see cref="UseSupabaseJwtSecret"/> in
    /// <paramref name="configure"/> for those. A handler of <c>OnTokenValidated</c> set there still runs,
    /// first; a token it fails is not kept.
    /// </para>
    /// </remarks>
    /// <param name="builder">The application's authentication.</param>
    /// <param name="authenticationScheme">The scheme's name.</param>
    /// <param name="projectUrl">The project's URL, <c>https://&lt;ref&gt;.supabase.co</c>, or the local stack's, <c>http://127.0.0.1:54321</c>.</param>
    /// <param name="configure">Anything else about the scheme.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="projectUrl"/> is not an absolute http or https URL.</exception>
    public static AuthenticationBuilder AddSupabaseJwtBearer(this AuthenticationBuilder builder, string authenticationScheme, string projectUrl, Action<JwtBearerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);

        var issuer = SupabaseTokens.IssuerOf(projectUrl);

        builder.AddJwtBearer(authenticationScheme, options =>
        {
            options.Authority = issuer;
            options.Audience = SupabaseTokens.Audience;
            options.RequireHttpsMetadata = issuer.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.OrdinalIgnoreCase);
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ValidIssuer = issuer;
            options.TokenValidationParameters.NameClaimType = "sub";
            options.TokenValidationParameters.RoleClaimType = "role";

            configure?.Invoke(options);
        });

        builder.Services.PostConfigure<JwtBearerOptions>(authenticationScheme, KeepValidatedClaims);

        // Replace: whatever registered a caller before, in ASP.NET Core the request knows best.
        builder.Services.AddHttpContextAccessor();
        builder.Services.Replace(ServiceDescriptor.Singleton<ICallerAccessor, HttpSupabaseCallerAccessor>());

        return builder;
    }

    /// <summary>
    /// Validates tokens with the project's shared JWT secret instead of its published keys: for the
    /// Supabase CLI's local stack, whose secret is in <c>supabase status</c>, and for projects still on
    /// the legacy secret. Supabase advises moving a hosted project to asymmetric signing keys rather than
    /// handing its secret to another service.
    /// </summary>
    /// <param name="options">The scheme <see cref="AddSupabaseJwtBearer(AuthenticationBuilder, string, Action{JwtBearerOptions}?)"/> configures.</param>
    /// <param name="jwtSecret">The project's JWT secret.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="jwtSecret"/> is empty.</exception>
    public static JwtBearerOptions UseSupabaseJwtSecret(this JwtBearerOptions options, string jwtSecret)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(jwtSecret);

        // No authority, so nothing is fetched: the key is right here.
        options.Authority = null;
        options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];

        return options;
    }

    private static void KeepValidatedClaims(JwtBearerOptions options)
    {
        if (options.EventsType is not null)
        {
            throw new InvalidOperationException(
                $"The Supabase bearer scheme keeps each validated token's claims from its OnTokenValidated event, and JwtBearerOptions.EventsType " +
                $"({options.EventsType.Name}) replaces the events it hooks. Set JwtBearerOptions.Events instead.");
        }

        options.Events ??= new JwtBearerEvents();
        var validated = options.Events.OnTokenValidated;

        options.Events.OnTokenValidated = async context =>
        {
            await validated(context).ConfigureAwait(false);

            // A handler that failed the token, or answered for it, decided; only a token that passed is kept.
            if (context.Result is null or { Succeeded: true } && SupabaseTokens.ClaimsOf(context.SecurityToken) is { } claims)
            {
                context.HttpContext.Features.Set(new SupabaseAccessTokenFeature(Callers.FromClaims(claims)));
            }
        };
    }
}

/// <summary>The caller a validated Supabase access token made of this request.</summary>
internal sealed record SupabaseAccessTokenFeature(Caller Caller);

/// <summary>
/// The caller of the current request. Asked every time a context opens a connection, so a query runs as
/// whoever the request that is running it came from.
/// </summary>
internal sealed class HttpSupabaseCallerAccessor(IHttpContextAccessor requests) : ICallerAccessor
{
    public Caller Current => Callers.Ambient ?? requests.HttpContext switch
    {
        // No request: work the application does on its own behalf. That includes work a request started
        // that is still running after the response went out, because ASP.NET Core forgets the request
        // then; begin a caller around such work to keep it.
        null => Caller.System,
        { User.Identity.IsAuthenticated: true } request when request.Features.Get<SupabaseAccessTokenFeature>() is { } token => token.Caller,
        _ => Caller.Anonymous,
    };
}
