using DDDToolkit.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Auth.Supabase;

/// <summary>Registers the <see cref="SupabaseTokenValidator"/> and the <see cref="SupabaseAuthAdmin"/> for a project.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers a <see cref="SupabaseTokenValidator"/> for the project at <paramref name="projectUrl"/>,
    /// which <c>DDDToolkit.Auth.Supabase.AzureFunctions</c>'s middleware and your own code take from the
    /// container.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="projectUrl">The project's URL, <c>https://&lt;ref&gt;.supabase.co</c>, or the local stack's.</param>
    /// <param name="configure">
    /// Anything else about the project: its JWT secret, for the tokens that are signed with it, where its
    /// Auth answers when that is not <c>{projectUrl}/auth/v1</c>, and whether that may be in plain http on a
    /// network of the host's own.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="projectUrl"/>, or the Auth URL when one is given, is not an http or https URL; or the
    /// address Auth answers at is plain http to another machine and
    /// <see cref="SupabaseAuthOptions.AllowPlainHttp"/> is off.
    /// </exception>
    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, string projectUrl, Action<SupabaseAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SupabaseAuthOptions { ProjectUrl = projectUrl };
        configure?.Invoke(options);

        // Built now, so a URL that is not one, or one that would have the keys travel in the clear without
        // the host having said so, fails at start-up rather than on the first request.
        var validator = new SupabaseTokenValidator(options);

        services.Replace(ServiceDescriptor.Singleton(options));
        services.Replace(ServiceDescriptor.Singleton(validator));
        return services;
    }

    /// <summary>
    /// Registers a <see cref="SupabaseAuthAdmin"/> for the Auth server at <paramref name="authUrl"/>, and
    /// Supabase Auth as the application's <see cref="IIdentityAccounts"/>. For server code only: both work
    /// with the project's secret key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The admin client gets a client of its own instead of one from <c>IHttpClientFactory</c>. What a host
    /// sets up for every client it makes, retries and the logging of requests among it, does not belong
    /// around these calls: an invitation that is retried is a second mail.
    /// </para>
    /// <para>
    /// Read the key from the host's configuration or secret store and pass it here; it is kept by the
    /// client alone and written to no log and no exception. A host on another identity provider does not
    /// call this and registers its own <see cref="IIdentityAccounts"/>; one that registers its own after
    /// calling this keeps its own.
    /// </para>
    /// </remarks>
    /// <param name="services">The application's services.</param>
    /// <param name="authUrl">
    /// Where Auth answers: <c>https://&lt;ref&gt;.supabase.co/auth/v1</c> for a project, which is
    /// <see cref="SupabaseTokens.IssuerOf"/> of its URL, or the address of an Auth server with no gateway in
    /// front of it.
    /// </param>
    /// <param name="secretKey">
    /// The project's secret key (<c>sb_secret_...</c>, or the legacy <c>service_role</c> key); for a bare
    /// Auth server, a token with the service role.
    /// </param>
    /// <param name="handler">
    /// What sends the requests, for a host that reaches Auth through a proxy of its own, or a test; it
    /// stays the caller's to dispose, and it follows no redirect: one of the runtime's own handlers that does
    /// is refused. Left out, the client pools connections itself and follows no redirect. Either way the key
    /// goes to <paramref name="authUrl"/> and nowhere else.
    /// </param>
    /// <param name="allowPlainHttp">
    /// Whether an <paramref name="authUrl"/> in plain http is taken for a server that is not on this machine:
    /// for an Auth server on a private network of the host's own. Every call sends the secret key,
    /// unencrypted there. Plain http to <c>localhost</c> or a loopback address needs no saying.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="authUrl"/> is not an http or https URL, or is plain http to another machine without
    /// <paramref name="allowPlainHttp"/>; <paramref name="secretKey"/> is empty, is a publishable key, or has
    /// a character a header cannot carry; or <paramref name="handler"/> follows redirects. The message never
    /// repeats the key.
    /// </exception>
    public static IServiceCollection AddSupabaseAuthAdmin(this IServiceCollection services, string authUrl, string secretKey, HttpMessageHandler? handler = null, bool allowPlainHttp = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Checked now, so a URL that is not one, a key that cannot be the secret one or a handler that would
        // carry the key elsewhere fails at start-up rather than on the first invitation. The client itself is
        // made by the container, which then disposes it, with its connections, when the host stops.
        SupabaseAuthAdmin.AuthAddressOf(authUrl, nameof(authUrl), allowPlainHttp);
        SupabaseAuthAdmin.SecretKeyOf(secretKey, nameof(secretKey));
        if (handler is not null)
        {
            SupabaseAuthAdmin.FollowingNoRedirect(handler, nameof(handler));
        }

        services.Replace(ServiceDescriptor.Singleton(_ => new SupabaseAuthAdmin(authUrl, secretKey, handler, allowPlainHttp)));
        services.Replace(ServiceDescriptor.Singleton<IIdentityAccounts>(provider => new SupabaseIdentityAccounts(provider.GetRequiredService<SupabaseAuthAdmin>())));
        return services;
    }
}
