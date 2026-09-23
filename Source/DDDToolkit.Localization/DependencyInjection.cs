using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Localization;

/// <summary>Registers <see cref="IFailureLocalizer"/>.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers an <see cref="IFailureLocalizer"/> that asks your translations first and the toolkit's
    /// own messages last:
    /// <code>
    /// builder.Services.AddLocalization();
    /// builder.Services.AddDDDToolkitLocalization(options =&gt; options.AddResource&lt;SharedFailures&gt;());
    /// </code>
    /// <para>
    /// Calls add up. Each module can register its own translations from its own composition method, and
    /// every source is asked, in the order the calls were made:
    /// </para>
    /// <code>
    /// services.AddDDDToolkitLocalization(options =&gt; options.AddResource&lt;OrderingFailures&gt;());   // in AddOrdering()
    /// services.AddDDDToolkitLocalization(options =&gt; options.AddResource&lt;ShippingFailures&gt;());   // in AddShipping()
    /// </code>
    /// <para>
    /// A singleton: the language is read per call from the current UI culture, not fixed at registration.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Adds the sources of your translations. Without it only the toolkit's own messages are known.</param>
    public static IServiceCollection AddDDDToolkitLocalization(this IServiceCollection services, Action<FailureLocalizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new FailureLocalizationOptions();
        configure?.Invoke(options);

        foreach (var source in options.Sources)
        {
            services.AddSingleton(new FailureLocalizationSource(source.Name, source.Create));
        }

        services.TryAddSingleton<IFailureLocalizer>(provider =>
            new FailureLocalizer(provider.GetServices<FailureLocalizationSource>().Select(source => source.Create(provider))));

        return services;
    }
}
