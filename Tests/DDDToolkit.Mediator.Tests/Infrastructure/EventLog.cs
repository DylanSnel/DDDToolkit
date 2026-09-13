using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;

namespace DDDToolkit.Mediator.Tests.Infrastructure;

/// <summary>
/// What every handler in this test project writes to. One instance per <see cref="TestHost"/>,
/// registered as a singleton so a test can read it after the scope that dispatched is gone.
/// </summary>
public sealed class EventLog
{
    private readonly List<Entry> _entries = [];

    /// <summary>Set by a test to make a handler throw; returning null means the handler succeeds.</summary>
    public Func<string, IDomainEvent, Exception?>? Fail { get; set; }

    /// <summary>The <c>BasketContext</c> the last handler that asked for one was given.</summary>
    public object? ContextSeenByHandler { get; set; }

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>The stable names of the events handled, in the order the handlers saw them.</summary>
    public IReadOnlyList<string> Names => [.. Entries.Select(entry => DomainEventName.Of(entry.Event))];

    /// <summary>The handler names that ran, in order, as "handler:event".</summary>
    public IReadOnlyList<string> Handled => [.. Entries.Select(entry => $"{entry.Handler}:{DomainEventName.Of(entry.Event)}")];

    /// <summary>Records that <paramref name="handler"/> handled <paramref name="domainEvent"/>, then fails if the test asked it to.</summary>
    public void Record(string handler, IDomainEvent domainEvent)
    {
        lock (_entries)
        {
            _entries.Add(new Entry(handler, domainEvent));
        }

        if (Fail?.Invoke(handler, domainEvent) is { } exception)
        {
            throw exception;
        }
    }

    public int CountOf<TEvent>() where TEvent : IDomainEvent => Entries.Count(entry => entry.Event is TEvent);

    public sealed record Entry(string Handler, IDomainEvent Event);
}
