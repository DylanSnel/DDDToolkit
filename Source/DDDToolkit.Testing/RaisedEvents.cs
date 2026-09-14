using System.Collections;
using System.Text;
using DDDToolkit.Interfaces;
using DDDToolkit.Testing.Internal;

namespace DDDToolkit.Testing;

/// <summary>
/// A batch of domain events, and the assertions you make about it. It is also an
/// <see cref="IReadOnlyList{T}"/>, so anything the assertions do not cover you can still do by hand
/// with LINQ or with your own assertion library.
/// </summary>
/// <remarks>
/// Every failure throws an <see cref="AggregateAssertionException"/> whose message names the
/// expectation and lists the events that were actually raised, with their payloads.
/// </remarks>
public sealed class RaisedEvents : IReadOnlyList<IDomainEvent>
{
    private readonly IReadOnlyList<IDomainEvent> _events;
    private readonly string _subject;
    private readonly string _source;

    internal RaisedEvents(IReadOnlyList<IDomainEvent> events, string subject, string source)
    {
        _events = events;
        _subject = subject;
        _source = source;
    }

    /// <summary>How many events are in the batch.</summary>
    public int Count => _events.Count;

    /// <summary>The event at <paramref name="index"/>, in the order the events were raised.</summary>
    /// <param name="index">Zero based position in the batch.</param>
    public IDomainEvent this[int index] => _events[index];

    /// <inheritdoc />
    public IEnumerator<IDomainEvent> GetEnumerator() => _events.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ------------------------------------------------------------------ presence

    /// <summary>Asserts that at least one event of <typeparamref name="TEvent"/> was raised.</summary>
    /// <typeparam name="TEvent">The event type to look for. Derived types count.</typeparam>
    /// <returns>This batch, so assertions chain.</returns>
    /// <exception cref="AggregateAssertionException">No event of that type was raised.</exception>
    public RaisedEvents Raised<TEvent>() where TEvent : IDomainEvent
    {
        if (!this.OfType<TEvent>().Any())
        {
            throw Failed($"Expected {_subject} to raise {typeof(TEvent).Name}, but it did not.");
        }

        return this;
    }

    /// <summary>
    /// Asserts that at least one event of <typeparamref name="TEvent"/> was raised and that it
    /// satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <typeparam name="TEvent">The event type to look for. Derived types count.</typeparam>
    /// <param name="predicate">The condition the event must satisfy.</param>
    /// <returns>This batch, so assertions chain.</returns>
    /// <exception cref="AggregateAssertionException">No event of that type satisfied the predicate.</exception>
    public RaisedEvents Raised<TEvent>(Func<TEvent, bool> predicate) where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var candidates = this.OfType<TEvent>().ToArray();

        if (candidates.Any(predicate))
        {
            return this;
        }

        var reason = candidates.Length == 0
            ? $"Expected {_subject} to raise a {typeof(TEvent).Name} matching the predicate, but it raised no {typeof(TEvent).Name} at all."
            : $"Expected {_subject} to raise a {typeof(TEvent).Name} matching the predicate, but none of the {candidates.Length} it raised matched.";

        throw Failed(reason);
    }

    /// <summary>
    /// Asserts that an event equal to <paramref name="expected"/> was raised. Only the payload is
    /// compared: <c>EventId</c> and <c>OccurredAt</c> are ignored, because a freshly constructed
    /// expectation can never match those.
    /// </summary>
    /// <typeparam name="TEvent">The event type. Inferred from <paramref name="expected"/>.</typeparam>
    /// <param name="expected">An event carrying the payload you expect. Build it like the real one.</param>
    /// <returns>This batch, so assertions chain.</returns>
    /// <exception cref="AggregateAssertionException">No raised event carried that payload.</exception>
    public RaisedEvents Raised<TEvent>(TEvent expected) where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(expected);

        var candidates = _events.Where(raised => raised.GetType() == expected.GetType()).ToArray();

        if (candidates.Any(candidate => EventPayload.Matches(expected, candidate)))
        {
            return this;
        }

        var message = new StringBuilder()
            .AppendLine($"Expected {_subject} to raise {EventPayload.Describe(expected)}, but no raised event carried that payload.");

        if (candidates.Length == 0)
        {
            message.Append($"It raised no {expected.GetType().Name} at all.");
        }
        else
        {
            foreach (var candidate in candidates)
            {
                message.AppendLine($"  {EventPayload.Describe(candidate)}: {EventPayload.FirstDifference(expected, candidate)}");
            }
        }

        throw Failed(message.ToString(), MetadataNote);
    }

    /// <summary>Asserts that no event of <typeparamref name="TEvent"/> was raised.</summary>
    /// <typeparam name="TEvent">The event type that must be absent. Derived types count.</typeparam>
    /// <returns>This batch, so assertions chain.</returns>
    /// <exception cref="AggregateAssertionException">An event of that type was raised.</exception>
    public RaisedEvents RaisedNo<TEvent>() where TEvent : IDomainEvent
    {
        var found = this.OfType<TEvent>().Count();

        if (found > 0)
        {
            throw Failed($"Expected {_subject} to raise no {typeof(TEvent).Name}, but it raised {found}.");
        }

        return this;
    }

    /// <summary>Asserts that nothing was raised at all.</summary>
    /// <exception cref="AggregateAssertionException">Something was raised.</exception>
    public void RaisedNothing()
    {
        if (_events.Count > 0)
        {
            throw Failed($"Expected {_subject} to raise no events, but it raised {_events.Count}.");
        }
    }

    // ------------------------------------------------------------------ exact sequences

    /// <summary>Asserts that exactly one event was raised, of <typeparamref name="T1"/>.</summary>
    /// <typeparam name="T1">The only event type expected.</typeparam>
    /// <returns>This batch, so assertions chain.</returns>
    public RaisedEvents RaisedExactly<T1>() where T1 : IDomainEvent
        => RaisedExactly(typeof(T1));

    /// <summary>Asserts that exactly two events were raised, in this order.</summary>
    /// <typeparam name="T1">The first expected event type.</typeparam>
    /// <typeparam name="T2">The second expected event type.</typeparam>
    /// <returns>This batch, so assertions chain.</returns>
    public RaisedEvents RaisedExactly<T1, T2>()
        where T1 : IDomainEvent
        where T2 : IDomainEvent
        => RaisedExactly(typeof(T1), typeof(T2));

    /// <summary>Asserts that exactly three events were raised, in this order.</summary>
    /// <typeparam name="T1">The first expected event type.</typeparam>
    /// <typeparam name="T2">The second expected event type.</typeparam>
    /// <typeparam name="T3">The third expected event type.</typeparam>
    /// <returns>This batch, so assertions chain.</returns>
    public RaisedEvents RaisedExactly<T1, T2, T3>()
        where T1 : IDomainEvent
        where T2 : IDomainEvent
        where T3 : IDomainEvent
        => RaisedExactly(typeof(T1), typeof(T2), typeof(T3));

    /// <summary>
    /// Asserts that these event types were raised, in this order, and nothing else. The comparison
    /// is by exact runtime type, so a derived event does not satisfy its base type here.
    /// </summary>
    /// <param name="eventTypes">The expected event types, in order.</param>
    /// <returns>This batch, so assertions chain.</returns>
    /// <exception cref="AggregateAssertionException">A different sequence was raised.</exception>
    public RaisedEvents RaisedExactly(params Type[] eventTypes)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);

        foreach (var eventType in eventTypes)
        {
            if (eventType is null || !typeof(IDomainEvent).IsAssignableFrom(eventType))
            {
                throw new ArgumentException(
                    $"{eventType?.Name ?? "null"} is not a domain event type.", nameof(eventTypes));
            }
        }

        var actual = _events.Select(raised => raised.GetType()).ToArray();

        if (actual.SequenceEqual(eventTypes))
        {
            return this;
        }

        var expectation = new StringBuilder()
            .AppendLine($"Expected {_subject} to raise exactly {Describe(eventTypes)}.");

        var firstMismatch = FirstMismatch(actual, eventTypes);
        if (firstMismatch is int index)
        {
            var actualName = index < actual.Length ? actual[index].Name : "nothing";
            var expectedName = index < eventTypes.Length ? eventTypes[index].Name : "nothing";
            expectation.Append($"The first difference is at index {index}: expected {expectedName}, was {actualName}.");
        }

        throw Failed(expectation.ToString());
    }

    /// <summary>
    /// Asserts that exactly these events were raised, in this order, comparing payloads.
    /// <c>EventId</c> and <c>OccurredAt</c> are ignored.
    /// </summary>
    /// <param name="expected">The events you expect, in order, built the way the aggregate builds them.</param>
    /// <returns>This batch, so assertions chain.</returns>
    /// <exception cref="AggregateAssertionException">A different sequence was raised.</exception>
    public RaisedEvents RaisedExactlyThese(params IDomainEvent[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        var difference = SequenceDifference(expected);

        if (difference is null)
        {
            return this;
        }

        var message = new StringBuilder()
            .AppendLine($"Expected {_subject} to raise exactly these {expected.Length} events:");

        for (var index = 0; index < expected.Length; index++)
        {
            message.AppendLine($"  [{index}] {EventPayload.Describe(expected[index])}");
        }

        message.Append(difference);

        throw Failed(message.ToString(), MetadataNote);
    }

    // ------------------------------------------------------------------ reaching in

    /// <summary>
    /// Returns the one event of <typeparamref name="TEvent"/> that was raised, so you can assert on
    /// its members with whatever assertion library you use.
    /// </summary>
    /// <typeparam name="TEvent">The event type to take. Derived types count.</typeparam>
    /// <returns>The single matching event.</returns>
    /// <exception cref="AggregateAssertionException">Zero, or more than one, were raised.</exception>
    public TEvent SingleEvent<TEvent>() where TEvent : IDomainEvent
    {
        var candidates = this.OfType<TEvent>().ToArray();

        return candidates.Length == 1
            ? candidates[0]
            : throw Failed($"Expected {_subject} to raise exactly one {typeof(TEvent).Name}, but it raised {candidates.Length}.");
    }

    /// <summary>Every raised event of <typeparamref name="TEvent"/>, in order. Never null, possibly empty.</summary>
    /// <typeparam name="TEvent">The event type to take. Derived types count.</typeparam>
    /// <returns>The matching events.</returns>
    public IReadOnlyList<TEvent> EventsOf<TEvent>() where TEvent : IDomainEvent
        => this.OfType<TEvent>().ToArray();

    /// <summary>The batch rendered the way a failure message renders it.</summary>
    /// <returns>One line per event, with its payload.</returns>
    public override string ToString() => ActualBlock();

    // ------------------------------------------------------------------ messages

    private string? SequenceDifference(IReadOnlyList<IDomainEvent> expected)
    {
        if (expected.Count != _events.Count)
        {
            return $"It raised {_events.Count}.";
        }

        for (var index = 0; index < expected.Count; index++)
        {
            var difference = EventPayload.FirstDifference(expected[index], _events[index]);

            if (difference is not null)
            {
                return $"The first difference is at index {index}: {difference}.";
            }
        }

        return null;
    }

    private static int? FirstMismatch(IReadOnlyList<Type> actual, IReadOnlyList<Type> expected)
    {
        var shared = Math.Min(actual.Count, expected.Count);

        for (var index = 0; index < shared; index++)
        {
            if (actual[index] != expected[index])
            {
                return index;
            }
        }

        return actual.Count == expected.Count ? null : shared;
    }

    private static string Describe(IReadOnlyList<Type> eventTypes)
        => eventTypes.Count == 0
            ? "nothing"
            : string.Join(", then ", eventTypes.Select(type => type.Name));

    private const string MetadataNote = "EventId and OccurredAt are never compared.";

    private AggregateAssertionException Failed(string expectation, string? footer = null)
    {
        var message = new StringBuilder(expectation.TrimEnd())
            .AppendLine()
            .AppendLine()
            .Append(ActualBlock());

        if (footer is not null)
        {
            message.AppendLine().AppendLine().Append(footer);
        }

        return new AggregateAssertionException(message.ToString());
    }

    private string ActualBlock()
    {
        if (_events.Count == 0)
        {
            return $"No events were {_source}.";
        }

        var block = new StringBuilder();
        block.AppendLine($"{_events.Count} {(_events.Count == 1 ? "event was" : "events were")} {_source}:");

        for (var index = 0; index < _events.Count; index++)
        {
            block.AppendLine($"  [{index}] {EventPayload.Describe(_events[index])}");
        }

        return block.ToString().TrimEnd();
    }
}
