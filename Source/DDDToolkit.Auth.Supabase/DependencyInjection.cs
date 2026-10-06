using DDDToolkit.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Auth.Supabase;

/// <summary>
/// Registers the <see cref="SupabaseTokenValidator"/> and the <see cref="SupabaseAuthAdmin"/> for a project.
/// Each takes the project as the bearer scheme of <c>DDDToolkit.Auth.Supabase.AspNetCore</c> does: by its
/// URL, or, for a host with more to say about it, by one <see cref="SupabaseAuthOptions"/> that the host
/// hands to each of them.
/// </summary>
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

        return Register(services, options);
    }

    /// <summary>
    /// Registers a <see cref="SupabaseTokenValidator"/> for the project <paramref name="supabase"/> describes:
    /// the object a host also hands the bearer scheme, or
    /// <see cref="AddSupabaseAuthAdmin(IServiceCollection, SupabaseAuthOptions, string, HttpMessageHandler?)"/>,
    /// so that the project is described once. See
    /// <see cref="AddSupabaseAuth(IServiceCollection, string, Action{SupabaseAuthOptions}?)"/>.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="supabase">The project. Read now; a later change to it changes nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="supabase"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The project URL, or the Auth URL when one is given, is not an http or https URL; or the address Auth
    /// answers at is plain http to another machine and <see cref="SupabaseAuthOptions.AllowPlainHttp"/> is off.
    /// </exception>
    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, SupabaseAuthOptions supabase)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(supabase);

        return Register(services, supabase.Copy());
    }

    /// <summary>
    /// Registers a <see cref="SupabaseAuthAdmin"/> for the project at <paramref name="projectUrl"/>, whose Auth
    /// it reaches at <c>{projectUrl}/auth/v1</c> as the bearer scheme does, and Supabase Auth as the
    /// application's <see cref="IIdentityAccounts"/>. For server code only: both work with the project's
    /// secret key. See
    /// <see cref="AddSupabaseAuthAdmin(IServiceCollection, SupabaseAuthOptions, string, HttpMessageHandler?)"/>.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="projectUrl">
    /// The project's URL, <c>https://&lt;ref&gt;.supabase.co</c>, or the local stack's,
    /// <c>http://127.0.0.1:54321</c>: the one the bearer scheme is given.
    /// </param>
    /// <param name="secretKey">The project's secret key (<c>sb_secret_...</c>, or the legacy <c>service_role</c> key).</param>
    /// <param name="handler">
    /// What sends the requests, for a host that reaches Auth through a proxy of its own, or a test; it
    /// stays the caller's to dispose, and it follows no redirect.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="projectUrl"/> is not an http or https URL, or is plain http to another machine, which
    /// takes the form with a <see cref="SupabaseAuthOptions"/> and its
    /// <see cref="SupabaseAuthOptions.AllowPlainHttp"/>; <paramref name="secretKey"/> is empty, is a
    /// publishable key, or has a character a header cannot carry; or <paramref name="handler"/> follows
    /// redirects. The message repeats neither the key nor the URL.
    /// </exception>
    public static IServiceCollection AddSupabaseAuthAdmin(this IServiceCollection services, string projectUrl, string secretKey, HttpMessageHandler? handler = null)
        => services.AddSupabaseAuthAdmin(new SupabaseAuthOptions { ProjectUrl = projectUrl }, secretKey, handler);

    /// <summary>
    /// Registers a <see cref="SupabaseAuthAdmin"/> for the project <paramref name="supabase"/> describes, and
    /// Supabase Auth as the application's <see cref="IIdentityAccounts"/>. For server code only: both work
    /// with the project's secret key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client calls Auth where the project's tokens say it is, <c>{ProjectUrl}/auth/v1</c>, or at
    /// <see cref="SupabaseAuthOptions.AuthUrl"/> for an Auth server reached without the project's gateway,
    /// and over plain http to another machine only with <see cref="SupabaseAuthOptions.AllowPlainHttp"/>.
    /// Those are the settings the bearer scheme fetches the published keys by, so a host hands both the same
    /// object. Its JWT secret is the bearer's: the admin client checks no token.
    /// </para>
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
    /// <param name="supabase">
    /// The project: its URL, where its Auth answers when that is not <c>{ProjectUrl}/auth/v1</c>, and whether
    /// that may be in plain http on a network of the host's own. Read now; a later change to it changes nothing.
    /// </param>
    /// <param name="secretKey">
    /// The project's secret key (<c>sb_secret_...</c>, or the legacy <c>service_role</c> key); for a bare
    /// Auth server, a token with the service role.
    /// </param>
    /// <param name="handler">
    /// What sends the requests, for a host that reaches Auth through a proxy of its own, or a test; it
    /// stays the caller's to dispose, and it follows no redirect: one of the runtime's own handlers that does
    /// is refused. Left out, the client pools connections itself and follows no redirect. Either way the key
    /// goes to Auth's address and nowhere else.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="supabase"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The project URL, or <see cref="SupabaseAuthOptions.AuthUrl"/> when one is given, is not an http or
    /// https URL, or Auth's address is plain http to another machine without
    /// <see cref="SupabaseAuthOptions.AllowPlainHttp"/>; <paramref name="secretKey"/> is empty, is a
    /// publishable key, or has a character a header cannot carry; or <paramref name="handler"/> follows
    /// redirects. The message repeats neither the key nor the address.
    /// </exception>
    public static IServiceCollection AddSupabaseAuthAdmin(this IServiceCollection services, SupabaseAuthOptions supabase, string secretKey, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Checked now, so an address that is not one, a key that cannot be the secret one or a handler that
        // would carry the key elsewhere fails at start-up rather than on the first invitation. The address is
        // kept as it was read. The client itself is made by the container, which then disposes it, with its
        // connections, when the host stops.
        var auth = SupabaseAuthAdmin.AuthAddressOf(supabase);
        SupabaseAuthAdmin.SecretKeyOf(secretKey, nameof(secretKey));
        if (handler is not null)
        {
            SupabaseAuthAdmin.FollowingNoRedirect(handler, nameof(handler));
        }

        services.Replace(ServiceDescriptor.Singleton(_ => new SupabaseAuthAdmin(auth, secretKey, handler)));
        services.Replace(ServiceDescriptor.Singleton<IIdentityAccounts>(provider => new SupabaseIdentityAccounts(provider.GetRequiredService<SupabaseAuthAdmin>())));
        return services;
    }

    private static IServiceCollection Register(IServiceCollection services, SupabaseAuthOptions options)
    {
        // Built now, so a URL that is not one, or one that would have the keys travel in the clear without
        // the host having said so, fails at start-up rather than on the first request.
        var validator = new SupabaseTokenValidator(options);

        services.Replace(ServiceDescriptor.Singleton(options));
        services.Replace(ServiceDescriptor.Singleton(validator));
        return services;
    }
}
