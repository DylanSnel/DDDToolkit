using DDDToolkit.Abstractions.Access;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Access;

/// <summary>Registration of <see cref="ICurrentUser"/>, and of what the host requires of it.</summary>
public static class CurrentUserServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CurrentUser"/> as the application's <see cref="ICurrentUser"/>, a singleton, unless one is
    /// registered already. Every registration of the toolkit that needs it calls this, so a host rarely does.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddCurrentUser(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICurrentUser, CurrentUser>();
        return services;
    }

    /// <summary>
    /// Makes every flow of work say who it runs as: registers <see cref="CurrentUserOptions"/> with
    /// <see cref="CurrentUserOptions.RequireExplicitCurrentUser"/> on. <see cref="CurrentUser"/> then throws
    /// <see cref="NoCurrentUserException"/> where nothing was begun and the host knows no user, instead of answering
    /// system work. The toolkit's background work begins system work around its own bookkeeping only, and the handlers
    /// it calls run with nobody begun until something begins a user for them.
    /// <code>
    /// builder.Services.RequireExplicitCurrentUser();
    /// builder.Services.AddModuleIntegrationEvents&lt;ShippingContext&gt;(module => module
    ///     .Around((services, message, contract) => services.GetRequiredService&lt;ICurrentUser&gt;().BeginSystem("shipping"))
    ///     .Handle&lt;OrderPlacedV1, BookShipment&gt;());
    /// </code>
    /// Calling it more than once is harmless.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection RequireExplicitCurrentUser(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(CurrentUserOptions) && !services[i].IsKeyedService && services[i].ImplementationInstance is CurrentUserOptions registered)
            {
                registered.RequireExplicitCurrentUser = true;
                return services;
            }
        }

        services.AddSingleton(new CurrentUserOptions { RequireExplicitCurrentUser = true });
        return services;
    }
}
