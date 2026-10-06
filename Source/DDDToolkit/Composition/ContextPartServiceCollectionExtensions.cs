using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Composition;

/// <summary>
/// Registration of the parts packages bring to a builder that a host finishes with one call. A package registers its
/// part where it registers what the part is about; the host's one call applies every registered part, in order.
/// <code>
/// builder.Services.AddSupabaseRowLevelSecurity();         // brings row level security, for every context on Postgres
/// builder.Services.AddTenancy&lt;TenancyContext&gt;(...);       // brings Tenancy's save check
/// builder.Services.AddDbContext&lt;TenancyContext&gt;((services, options) => options
///     .UseNpgsql(connectionString)
///     .UseDDDToolkit(services));                          // the toolkit's interceptors, then both parts
/// </code>
/// </summary>
public static class ContextPartServiceCollectionExtensions
{
    /// <summary>
    /// Registers <paramref name="part"/>, which the host's one call applies to every builder it belongs on. A package
    /// calls it from the registration that brings what the part is about, so a host that uses the package gets the
    /// part without naming it. Registering a part under a name already taken registers nothing, so a registration a
    /// host calls more than once brings its part once.
    /// </summary>
    /// <typeparam name="TBuilder">What the part is applied to.</typeparam>
    /// <param name="services">The application's services.</param>
    /// <param name="part">The part.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="part"/> is null.</exception>
    public static IServiceCollection AddContextPart<TBuilder>(this IServiceCollection services, ContextPart<TBuilder> part)
        where TBuilder : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(part);

        PartsOf<TBuilder>(services).Add(part);
        return services;
    }

    /// <summary>
    /// The parts registered so far for <typeparamref name="TBuilder"/>: the one instance in
    /// <paramref name="services"/>, added the first time anything asks. Read before the host is built, so a test can
    /// see what a host would apply, and in which order.
    /// </summary>
    /// <typeparam name="TBuilder">What the parts are applied to.</typeparam>
    /// <param name="services">The application's services.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static ContextParts<TBuilder> GetContextParts<TBuilder>(this IServiceCollection services)
        where TBuilder : class
    {
        ArgumentNullException.ThrowIfNull(services);
        return PartsOf<TBuilder>(services);
    }

    /// <summary>The one instance the services hold, found among the registrations, or registered now.</summary>
    private static ContextParts<TBuilder> PartsOf<TBuilder>(IServiceCollection services)
        where TBuilder : class
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(ContextParts<TBuilder>) && !services[i].IsKeyedService && services[i].ImplementationInstance is ContextParts<TBuilder> registered)
            {
                return registered;
            }
        }

        var parts = new ContextParts<TBuilder>();
        services.AddSingleton(parts);
        return parts;
    }
}
