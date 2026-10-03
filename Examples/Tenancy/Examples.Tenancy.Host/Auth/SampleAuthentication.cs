using DDDToolkit.Auth.Supabase;
using DDDToolkit.Auth.Supabase.AspNetCore;

namespace Examples.Tenancy.Host.Auth;

/// <summary>
/// Who is calling: the access token Supabase Auth issues, checked by the Supabase bearer, whose subject is
/// the verified identity every seat is found by.
/// </summary>
public static class SampleAuthentication
{
    /// <summary>The Supabase project's URL, whose Auth issues the tokens; the local stack's in Development.</summary>
    public const string UrlSetting = "Supabase:Url";

    /// <summary>
    /// The project's JWT secret, for the tokens that are signed with it: the dev login's, and those of a
    /// project still on the legacy secret. A token signed with a key Auth publishes needs none.
    /// </summary>
    public const string JwtSecretSetting = "Supabase:JwtSecret";

    /// <summary>
    /// Where Supabase Auth answers, when that is not <c>{Supabase:Url}/auth/v1</c>: an Auth server with no
    /// gateway in front of it, as the AppHost and the tests run one. It is where the host reaches Auth's admin
    /// API and the keys Auth publishes; the issuer its tokens are checked against stays the one of
    /// <see cref="UrlSetting"/>.
    /// </summary>
    public const string AuthUrlSetting = "Supabase:AuthUrl";

    /// <summary>Where Auth answers: <see cref="AuthUrlSetting"/>, or the project's own Auth address when it is not set.</summary>
    /// <param name="configuration">The host's configuration.</param>
    /// <exception cref="InvalidOperationException">Neither setting is there.</exception>
    public static string AuthUrlOf(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration[AuthUrlSetting] is { Length: > 0 } auth)
        {
            return auth;
        }

        return configuration[UrlSetting] is { Length: > 0 } project
            ? SupabaseTokens.IssuerOf(project)
            : throw new InvalidOperationException("Neither " + AuthUrlSetting + " nor " + UrlSetting + " is set, so there is no Auth server to reach.");
    }

    /// <summary>
    /// Adds the Supabase bearer for <c>Supabase:Url</c>, and the dev login's token issuer when the dev login
    /// is on and allowed (<see cref="DevLoginGuard"/>).
    /// </summary>
    /// <remarks>
    /// The bearer is the toolkit's, and it takes the two kinds of token a developer's machine meets under one
    /// issuer. With <c>Supabase:JwtSecret</c> set, a token signed with that secret is checked with it: the dev
    /// login's, and those of a project on the legacy secret. A token signed with a key Auth publishes is
    /// checked with that key, fetched from <see cref="AuthUrlSetting"/> or the project's own Auth address, which
    /// is how the stack the Supabase CLI starts signs a person's token: both logins work side by side. Without
    /// the secret only the published keys count, which is what a hosted project with signing keys needs.
    /// Nothing is fetched until the first token that needs a published key arrives. The bearer also makes
    /// each request's token its caller, which tenant selection reads.
    /// </remarks>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="environment">The host's environment, which the dev login guard asks.</param>
    /// <exception cref="InvalidOperationException"><c>Supabase:Url</c> is missing, or the dev login is on where it must not be.</exception>
    public static IServiceCollection AddSampleAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var devLogin = DevLoginGuard.Check(configuration, environment);

        var url = configuration[UrlSetting] is { Length: > 0 } configured
            ? configured
            : throw new InvalidOperationException(
                UrlSetting + " is not set. The API accepts the access tokens of one Supabase project, and needs its URL: "
                + "https://<ref>.supabase.co, or the local stack's http://127.0.0.1:54321, which appsettings.Development.json sets.");
        var secret = configuration[JwtSecretSetting] is { Length: > 0 } configuredSecret ? configuredSecret : null;

        services.AddAuthentication().AddSupabaseJwtBearer(new SupabaseAuthOptions
        {
            ProjectUrl = url,
            JwtSecret = secret,
            AuthUrl = configuration[AuthUrlSetting],
        });

        if (devLogin)
        {
            // The guard passed, so there is a secret, and the project is this machine's.
            services.AddSingleton(provider => new LocalTokenIssuer(url, secret!, provider.GetService<TimeProvider>() ?? TimeProvider.System));
        }

        return services;
    }
}
