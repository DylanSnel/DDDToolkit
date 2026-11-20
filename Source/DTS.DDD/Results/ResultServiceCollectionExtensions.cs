using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DTS.DDD.Results;

/// <summary>Registers how the mediator logs failed results.</summary>
public static class ResultServiceCollectionExtensions
{
    /// <summary>
    /// Sets the log level per error type for the failures that requests answer with a result. Without this call the
    /// mediator logs with the defaults of <see cref="ResultLoggingOptions"/>.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configure">Sets levels, such as <c>options.Level&lt;NotFoundError&gt;(LogLevel.Debug)</c>.</param>
    public static IServiceCollection AddDDDToolkitResultLogging(this IServiceCollection services, Action<ResultLoggingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ResultLoggingOptions();
        configure(options);
        services.RemoveAll<ResultLoggingOptions>();
        services.AddSingleton(options);
        return services;
    }
}