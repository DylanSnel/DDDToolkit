using DDDToolkit.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// One configured <see cref="IIntegrationEventSink"/>: either a type resolved per batch from the
/// scope the outbox processor runs in, or a single instance you already own.
/// </summary>
public sealed class IntegrationEventSinkRegistration
{
    private readonly Type? _sinkType;
    private readonly IIntegrationEventSink? _instance;
    private readonly Func<IServiceProvider, IIntegrationEventSink>? _create;

    internal IntegrationEventSinkRegistration(Type sinkType)
    {
        _sinkType = sinkType;
        Name = sinkType.Name;
    }

    internal IntegrationEventSinkRegistration(IIntegrationEventSink instance)
    {
        _instance = instance;
        Name = instance.GetType().Name;
    }

    internal IntegrationEventSinkRegistration(string name, Func<IServiceProvider, IIntegrationEventSink> create)
    {
        _create = create;
        Name = name;
    }

    /// <summary>The sink's type name, used in log and error messages.</summary>
    public string Name { get; }

    /// <summary>
    /// The sink to deliver through. A registered type is taken from <paramref name="serviceProvider"/>
    /// when it is registered there, and otherwise constructed with its services injected, so a sink
    /// with a constructor the container can satisfy needs no registration of its own.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> is null.</exception>
    public IIntegrationEventSink Resolve(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (_instance is not null)
        {
            return _instance;
        }

        if (_create is not null)
        {
            return _create(serviceProvider);
        }

        return (IIntegrationEventSink)ActivatorUtilities.GetServiceOrCreateInstance(serviceProvider, _sinkType!);
    }
}
