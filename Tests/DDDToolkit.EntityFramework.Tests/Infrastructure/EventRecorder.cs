using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// The test's "handler": records every dispatched event and lets a test plug in behaviour that runs
/// per event with the scoped service provider (to reach the saving <see cref="LibraryContext"/>).
/// </summary>
public sealed class EventRecorder
{
    private readonly List<IDomainEvent> _events = [];

    public IReadOnlyList<IDomainEvent> Events => _events;

    /// <summary>Invoked for every dispatched event; throw to simulate a failing handler.</summary>
    public Func<IServiceProvider, IDomainEvent, CancellationToken, Task>? OnEvent { get; set; }

    /// <summary>Invoked once per dispatch batch before the events are recorded.</summary>
    public Action<IReadOnlyList<IDomainEvent>>? OnBatch { get; set; }

    public async Task DispatchAsync(IServiceProvider serviceProvider, IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
    {
        OnBatch?.Invoke(events);

        foreach (var domainEvent in events)
        {
            if (OnEvent is not null)
            {
                await OnEvent(serviceProvider, domainEvent, cancellationToken);
            }

            _events.Add(domainEvent);
        }
    }

    public IEnumerable<TEvent> OfType<TEvent>() where TEvent : IDomainEvent => _events.OfType<TEvent>();
}
