using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.EventLog;

/// <summary>
/// Which domain events a context keeps in its event log; see <c>OutboxOptions.KeepEventLog</c>. With nothing
/// chosen, every event the context saves is kept. Once <see cref="Keep{TEvent}"/> or <see cref="Only"/> is
/// called, only what they name is.
/// </summary>
public sealed class EventLogOptions
{
    private readonly List<Func<IDomainEvent, bool>> _chosen = [];

    /// <summary>
    /// Keeps events of <typeparamref name="TEvent"/>, and of every type derived from it or implementing it. Call
    /// it once per type to keep; the choices add up.
    /// </summary>
    public EventLogOptions Keep<TEvent>() where TEvent : IDomainEvent
    {
        _chosen.Add(static domainEvent => domainEvent is TEvent);
        return this;
    }

    /// <summary>Keeps the events <paramref name="which"/> chooses, on top of those already chosen.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="which"/> is null.</exception>
    public EventLogOptions Only(Func<IDomainEvent, bool> which)
    {
        ArgumentNullException.ThrowIfNull(which);
        _chosen.Add(which);
        return this;
    }

    /// <summary>Whether <paramref name="domainEvent"/> goes into the log.</summary>
    internal bool Keeps(IDomainEvent domainEvent)
    {
        if (_chosen.Count == 0)
        {
            return true;
        }

        foreach (var chosen in _chosen)
        {
            if (chosen(domainEvent))
            {
                return true;
            }
        }

        return false;
    }
}
