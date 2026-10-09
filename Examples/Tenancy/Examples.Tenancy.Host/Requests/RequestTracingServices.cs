using Mediator;

namespace Examples.Tenancy.Host.Requests;

/// <summary>Registers the tracing of every command and query the host handles.</summary>
public static class RequestTracingServices
{
    /// <summary>
    /// Adds the application's source of request activities and the step that starts one around every request.
    /// Call it before the modules are added: the steps of a pipeline run in the order they were registered, and
    /// this one goes around a module's access check.
    /// </summary>
    /// <param name="services">The host's services.</param>
    public static IServiceCollection AddRequestTracing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<RequestTracing>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RequestTracingBehavior<,>));
        return services;
    }
}
