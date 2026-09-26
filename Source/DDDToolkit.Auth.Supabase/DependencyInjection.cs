using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Auth.Supabase;

/// <summary>Registers the <see cref="SupabaseTokenValidator"/> for a project.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers a <see cref="SupabaseTokenValidator"/> for the project at <paramref name="projectUrl"/>,
    /// which <c>DDDToolkit.Auth.Supabase.AzureFunctions</c>'s middleware and your own code take from the
    /// container.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="projectUrl">The project's URL, <c>https://&lt;ref&gt;.supabase.co</c>, or the local stack's.</param>
    /// <param name="configure">Anything else about the project, such as a JWT secret for the local stack.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="projectUrl"/> is not an http or https URL.</exception>
    public static IServiceCollection AddSupabaseAuth(this IServiceCollection services, string projectUrl, Action<SupabaseAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SupabaseAuthOptions { ProjectUrl = projectUrl };
        configure?.Invoke(options);

        // Built now, so a URL that is not one fails at start-up rather than on the first request.
        var validator = new SupabaseTokenValidator(options);

        services.Replace(ServiceDescriptor.Singleton(options));
        services.Replace(ServiceDescriptor.Singleton(validator));
        return services;
    }
}
