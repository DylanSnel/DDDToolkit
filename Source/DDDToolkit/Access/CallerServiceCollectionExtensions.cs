using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Access;

/// <summary>Registration of what the host requires of its callers.</summary>
public static class CallerServiceCollectionExtensions
{
    /// <summary>
    /// Makes every flow of work say who it runs as: registers <see cref="CallerOptions"/> with
    /// <see cref="CallerOptions.RequireExplicitCallers"/> on. An accessor that knows nobody then throws
    /// <see cref="NoCallerException"/> instead of answering the system, the toolkit's background work begins
    /// the system caller around its own bookkeeping only, and the handlers it calls run with no caller
    /// until something begins one for them.
    /// <code>
    /// builder.Services.RequireExplicitCallers();
    /// builder.Services.AddModuleIntegrationEvents&lt;ShippingContext&gt;(module => module
    ///     .Around((services, message, contract) => Callers.Begin(Caller.SystemIn("shipping")))
    ///     .Handle&lt;OrderPlacedV1, BookShipment&gt;());
    /// </code>
    /// Calling it more than once is harmless.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection RequireExplicitCallers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(CallerOptions) && !services[i].IsKeyedService && services[i].ImplementationInstance is CallerOptions registered)
            {
                registered.RequireExplicitCallers = true;
                return services;
            }
        }

        services.AddSingleton(new CallerOptions { RequireExplicitCallers = true });
        return services;
    }
}
