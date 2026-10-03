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
    /// <see cref="AddSupabaseJwtBearer(AuthenticationBuilder, string, SupabaseAuthOptions, Action{JwtBearerOptions}?)"/>.
    /// </summary>
    public static AuthenticationBuilder AddSupabaseJwtBearer(this AuthenticationBuilder builder, string projectUrl, Action<JwtBearerOptions>? configure = null)
        => builder.AddSupabaseJwtBearer(JwtBearerDefaults.AuthenticationScheme, projectUrl, configure);

    /// <summary>
    /// Adds a JWT bearer scheme for the access tokens of the Supabase project at <paramref name="projectUrl"/>. See
    /// <see cref="AddSupabaseJwtBearer(AuthenticationBuilder, string, SupabaseAuthOptions, Action{JwtBearerOptions}?)"/>.
    /// </summary>
    /// <param name="builder">The application's authentication.</param>
    /// <param name="authenticationScheme">The scheme's name.</param>
    /// <param name="projectUrl">The project's URL, <c>https://&lt;ref&gt;.supabase.co</c>, or the local stack's, <c>http://127.0.0.1:54321</c>.</param>
    /// <param name="configure">Anything else about the scheme.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="projectUrl"/> is not an absolute http or https URL.</exception>
    public static AuthenticationBuilder AddSupabaseJwtBearer(this AuthenticationBuilder builder, string authenticationScheme, string projectUrl, Action<JwtBearerOptions>? configure = null)
        => builder.AddSupabaseJwtBearer(authenticationScheme, new SupabaseAuthOptions { ProjectUrl = projectUrl }, configure);

    /// <summary>
    /// Adds a JWT bearer scheme named <c>Bearer</c> for Supabase Auth's access tokens. See
    /// <see cref="AddSupabaseJwtBearer(AuthenticationBuilder, string, SupabaseAuthOptions, Action{JwtBearerOptions}?)"/>.
    /// </summary>
    public static AuthenticationBuilder AddSupabaseJwtBearer(this AuthenticationBuilder builder, SupabaseAuthOptions supabase, Action<JwtBearerOptions>? configure = null)
        => builder.AddSupabaseJwtBearer(JwtBearerDefaults.AuthenticationScheme, supabase, configure);

    /// <summary>
    /// Adds a JWT bearer scheme for Supabase Auth's access tokens: issued by <c>{project URL}/auth/v1</c> for
    /// the audience <c>authenticated</c>, and signed with one of the keys the project publishes at
    /// <c>{project URL}/auth/v1/.well-known/jwks.json</c>, or with the project's JWT secret where the host
    /// has it. Claims keep the names they have in the token, so <c>sub</c> is <c>sub</c>, the user's id and
    /// the principal's name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two kinds of token.</b> A token's header says how it was signed, and that decides what it is checked
    /// with (<see cref="SupabaseTokenHandler"/>). One signed with a published key (ES256 or RS256) is checked
    /// with the keys Auth publishes, fetched when the first such token arrives, kept, and fetched again as
    /// ASP.NET Core's JWT bearer fetches a provider's: after
    /// <see cref="JwtBearerOptions.AutomaticRefreshInterval"/>, and when a token names a key that is not among
    /// them. One signed with the secret (HS256) is checked with the secret of
    /// <see cref="SupabaseAuthOptions.JwtSecret"/> or <see cref="UseSupabaseJwtSecret"/>, and nothing is
    /// fetched for it; a host without the secret refuses it. Each kind is held to its own algorithms, so a
    /// published key is never taken for a secret, and a token signed any other way is refused. A hosted project
    /// with signing keys needs no secret. A project still on the legacy secret needs it, and so does a host
    /// that signs tokens of its own with it; beside the stack the Supabase CLI starts, whose Auth signs with a
    /// published key, such a host takes both.
    /// </para>
    /// <para>
    /// <b>Where the keys come from.</b> <c>{project URL}/auth/v1</c>, or
    /// <see cref="SupabaseAuthOptions.AuthUrl"/> for an Auth server that answers elsewhere, over https. Plain
    /// http is taken for an Auth server on this machine, <c>localhost</c> or a loopback address; for one on a
    /// private network of the host's own the host sets <see cref="SupabaseAuthOptions.AllowPlainHttp"/>, and
    /// any other address in plain http is refused when the scheme is added: whoever answers for the keys
    /// decides who is signed in. <see cref="JwtBearerOptions.RequireHttpsMetadata"/> follows the address, and
    /// a host that sets it has an http address not asked at all; setting it off allows nothing more. The keys
    /// are fetched through the scheme's <see cref="JwtBearerOptions.Backchannel"/>, whose timeout ends a
    /// request for them and is also how long a token waits behind another token's fetch at most. The scheme
    /// needs no <see cref="JwtBearerOptions.Authority"/>; a host that sets one, or a metadata address or
    /// configuration manager of its own, has the published keys come from there, and how they travel from
    /// there is ASP.NET Core's to say, as for any JWT bearer scheme.
    /// </para>
    /// <para>
    /// <b>The caller.</b> The scheme also makes the request's user the caller for row level security: a
    /// request whose token this scheme validated runs its queries as that user, any other request as
    /// <c>anon</c>, and work outside a request as the system, unless code made another caller current with
    /// <see cref="Callers.Begin"/>, which always wins. With
    /// <see cref="CallerServiceCollectionExtensions.RequireExplicitCallers"/>, work outside a request that
    /// began no caller throws <see cref="NoCallerException"/> instead of running as the system.
    /// <see cref="SupabaseHttpContextExtensions.SupabaseCaller"/> is the request's own caller, whatever was
    /// begun around it. Each token that passes has its claims kept exactly as they were signed, so Postgres
    /// sees what PostgREST would have seen. A handler of <c>OnTokenValidated</c> set in
    /// <paramref name="configure"/> still runs, first; a token it fails is not kept.
    /// </para>
    /// <para>
    /// The scheme's token handlers are replaced by the one that knows the two kinds, so
    /// <see cref="JwtBearerOptions.UseSecurityTokenValidators"/>, which would pass it by, is refused.
    /// </para>
    /// </remarks>
    /// <param name="builder">The application's authentication.</param>
    /// <param name="authenticationScheme">The scheme's name.</param>
    /// <param name="supabase">
    /// The project: its URL, its JWT secret where tokens are signed with it, where its Auth answers when
    /// that is elsewhere, and whether that may be in plain http on a network of the host's own. Read now; a
    /// later change to it changes nothing.
    /// </param>
    /// <param name="configure">Anything else about the scheme.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="supabase"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The project URL, or the Auth URL when one is given, is not an absolute http or https URL; or the
    /// address Auth answers at is plain http to another machine and
    /// <see cref="SupabaseAuthOptions.AllowPlainHttp"/> is off.
    /// </exception>
    public static AuthenticationBuilder AddSupabaseJwtBearer(this AuthenticationBuilder builder, string authenticationScheme, SupabaseAuthOptions supabase, Action<JwtBearerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);
        ArgumentNullException.ThrowIfNull(supabase);

        var issuer = SupabaseTokens.IssuerOf(supabase.ProjectUrl);
        var secret = supabase.JwtSecret is { Length: > 0 } given ? given : null;

        var authUrl = string.IsNullOrWhiteSpace(supabase.AuthUrl) ? issuer : supabase.AuthUrl;
        var allowPlainHttp = supabase.AllowPlainHttp;

        // Asked now, so an Auth URL that is not one, or one that would have the keys travel in the clear
        // without the host having said so, fails at start-up rather than on the first request.
        var keysAddress = SupabaseTokens.KeysAddressOf(authUrl, allowPlainHttp);

        builder.AddJwtBearer(authenticationScheme, options =>
        {
            options.Audience = SupabaseTokens.Audience;
            options.RequireHttpsMetadata = keysAddress.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.OrdinalIgnoreCase);
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ValidIssuer = issuer;
            options.TokenValidationParameters.NameClaimType = "sub";
            options.TokenValidationParameters.RoleClaimType = "role";

            if (secret is not null)
            {
                options.UseSupabaseJwtSecret(secret);
            }

            configure?.Invoke(options);
        });

        builder.Services.PostConfigure<JwtBearerOptions>(authenticationScheme, options => CheckBothKindsOfToken(options, authUrl, allowPlainHttp));
        builder.Services.PostConfigure<JwtBearerOptions>(authenticationScheme, KeepValidatedClaims);

        // Replace: whatever registered a caller before, in ASP.NET Core the request knows best.
        builder.Services.AddHttpContextAccessor();
        builder.Services.Replace(ServiceDescriptor.Singleton<ICallerAccessor, HttpSupabaseCallerAccessor>());

        return builder;
    }

    /// <summary>
    /// Has the scheme take the tokens that are signed with the JWT secret as well: those of a project still on
    /// the legacy secret, and those a developer's machine signs itself. A token signed with a key Auth
    /// publishes is still checked with that key, so a host beside the stack the Supabase CLI starts, whose
    /// Auth signs a user's token that way, takes both. Nothing is fetched for a token signed with the secret.
    /// Supabase advises moving a hosted project to asymmetric signing keys rather than handing its secret to
    /// another service.
    /// </summary>
    /// <remarks>
    /// The secret becomes the scheme's <see cref="TokenValidationParameters.IssuerSigningKey"/>. It checks
    /// the tokens whose header says HS256, and no others.
    /// </remarks>
    /// <param name="options">The scheme <see cref="AddSupabaseJwtBearer(AuthenticationBuilder, string, Action{JwtBearerOptions}?)"/> configures.</param>
    /// <param name="jwtSecret">The project's JWT secret.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="jwtSecret"/> is empty.</exception>
    public static JwtBearerOptions UseSupabaseJwtSecret(this JwtBearerOptions options, string jwtSecret)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(jwtSecret);

        options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

        return options;
    }

    /// <summary>
    /// Puts the handler that knows both kinds of token in place of the scheme's own, with the settings the
    /// host gave the scheme for fetching a provider's keys.
    /// </summary>
    private static void CheckBothKindsOfToken(JwtBearerOptions options, string authUrl, bool allowPlainHttp)
    {
        if (options.UseSecurityTokenValidators)
        {
            throw new InvalidOperationException(
                "The Supabase bearer scheme checks each token with the kind of key its header names, in a token handler of its own, and " +
                "JwtBearerOptions.UseSecurityTokenValidators would pass that handler by. Leave it off.");
        }

        // The scheme's backchannel, made as the JWT bearer makes its own when it has an authority to ask.
        options.Backchannel ??= new HttpClient(options.BackchannelHttpHandler ?? new HttpClientHandler())
        {
            Timeout = options.BackchannelTimeout,
            MaxResponseContentBufferSize = 1024 * 1024 * 10,
        };

        options.TokenHandlers.Clear();
        options.TokenHandlers.Add(new SupabaseTokenHandler(authUrl, options.Backchannel, allowPlainHttp)
        {
            RequireHttps = options.RequireHttpsMetadata,
            MapInboundClaims = options.MapInboundClaims,
            AutomaticRefreshInterval = options.AutomaticRefreshInterval,
            RefreshInterval = options.RefreshInterval,
            RefreshOnKeyNotFound = options.RefreshOnIssuerKeyNotFound,
        });
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
/// The caller of the current request. Asked when a context opens a connection, and again before each command
/// and each transaction on it, so a query runs as whoever the request that is running it came from.
/// </summary>
internal sealed class HttpSupabaseCallerAccessor(IHttpContextAccessor requests, CallerOptions? options = null) : ICallerAccessor
{
    public Caller Current => Callers.Ambient ?? requests.HttpContext switch
    {
        // No request: work the application does on its own behalf. That includes work a request started
        // that is still running after the response went out, because ASP.NET Core forgets the request
        // then; begin a caller around such work to keep it. A host that requires explicit callers gets
        // nobody here, rather than the system's power by default.
        null => options?.RequireExplicitCallers == true ? throw new NoCallerException() : Caller.System,
        { } request => request.SupabaseCaller(),
    };
}
