using DDDToolkit.Interfaces;

namespace DDDToolkit.Testing;

/// <summary>
/// Starts an aggregate test: <c>given</c> an aggregate, <c>when</c> you call a method,
/// <c>then</c> these events were raised.
/// </summary>
/// <example>
/// <code>
/// AggregateScenario.Given(order)
///     .When(o =&gt; o.Cancel("out of stock"))
///     .RaisedExactly&lt;OrderCancelled&gt;();
/// </code>
/// </example>
public static class AggregateScenario
{
    /// <summary>
    /// Takes the aggregate the test already built. This kit does not replay an event stream, because
    /// a DDDToolkit aggregate is not event sourced: the arrange step is the constructor and whatever
    /// methods you call to get the aggregate into the state the rule is about.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate root type. Inferred from the argument.</typeparam>
    /// <param name="aggregate">The aggregate under test.</param>
    /// <returns>A scenario over that aggregate.</returns>
    public static AggregateScenario<TAggregate> Given<TAggregate>(TAggregate aggregate)
        where TAggregate : class, IHasDomainEvents
        => new(aggregate);
}

/// <summary>
/// One aggregate under test. Every <c>When</c> reports only the events that method raised, so a
/// multi-step test asserts per step without draining anything in between.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
public sealed class AggregateScenario<TAggregate> where TAggregate : class, IHasDomainEvents
{
    private const string ActionSource = "raised while running the action";

    private readonly TAggregate _aggregate;

    internal AggregateScenario(TAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _aggregate = aggregate;
    }

    /// <summary>The aggregate itself, for asserting on its state after the action.</summary>
    public TAggregate Subject => _aggregate;

    /// <summary>
    /// The events sitting on the aggregate right now, without taking them off it. Assert on these
    /// when you care about everything that has happened rather than about one step.
    /// </summary>
    public RaisedEvents PendingEvents => new(Snapshot(), Name, "pending on the aggregate");

    /// <summary>
    /// Discards the events raised so far, typically the ones the constructor raised, so the next
    /// <see cref="PendingEvents"/> reads cleanly. <c>When</c> does not need this: it reports the
    /// events of that action alone.
    /// </summary>
    /// <returns>This scenario, so the call chains.</returns>
    public AggregateScenario<TAggregate> IgnorePendingEvents()
    {
        _aggregate.ClearDomainEvents();
        return this;
    }

    /// <summary>
    /// Takes the pending events off the aggregate, the way the persistence layer does during a save,
    /// and returns them. Use it to model a save in the middle of a longer scenario.
    /// </summary>
    /// <returns>The drained events.</returns>
    public RaisedEvents Drain()
        => new(_aggregate.DequeueDomainEvents(), Name, "drained from the aggregate");

    // ------------------------------------------------------------------ acting

    /// <summary>Calls a method on the aggregate and returns the events that call raised.</summary>
    /// <param name="act">The method under test.</param>
    /// <returns>The events raised during <paramref name="act"/>, in order.</returns>
    public RaisedEvents When(Action<TAggregate> act)
    {
        ArgumentNullException.ThrowIfNull(act);

        var before = Snapshot();
        act(_aggregate);
        return new RaisedEvents(Delta(before), Name, ActionSource);
    }

    /// <summary>
    /// Calls an asynchronous method on the aggregate and returns the events that call raised.
    /// </summary>
    /// <param name="act">The method under test.</param>
    /// <returns>The events raised during <paramref name="act"/>, in order.</returns>
    public async Task<RaisedEvents> WhenAsync(Func<TAggregate, Task> act)
    {
        ArgumentNullException.ThrowIfNull(act);

        var before = Snapshot();
        await act(_aggregate).ConfigureAwait(false);
        return new RaisedEvents(Delta(before), Name, ActionSource);
    }

    /// <summary>
    /// Asserts that the method threw <typeparamref name="TException"/> <em>and</em> that it raised
    /// nothing on the way out. An aggregate that raises an event and then refuses the command has
    /// published something that did not happen, which is the failure this overload exists to catch.
    /// </summary>
    /// <typeparam name="TException">The expected exception type. Derived types count.</typeparam>
    /// <param name="act">The method under test.</param>
    /// <returns>The exception, so you can assert on its message.</returns>
    /// <exception cref="AggregateAssertionException">
    /// Nothing was thrown, something else was thrown, or events were raised before the throw.
    /// </exception>
    public TException WhenThrows<TException>(Action<TAggregate> act) where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(act);

        var before = Snapshot();
        Exception? thrown = null;

        try
        {
            act(_aggregate);
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        return Verify<TException>(thrown, before);
    }

    /// <summary>
    /// The asynchronous <see cref="WhenThrows{TException}(Action{TAggregate})"/>: asserts the
    /// expected exception and that nothing was raised before it.
    /// </summary>
    /// <typeparam name="TException">The expected exception type. Derived types count.</typeparam>
    /// <param name="act">The method under test.</param>
    /// <returns>The exception, so you can assert on its message.</returns>
    public async Task<TException> WhenThrowsAsync<TException>(Func<TAggregate, Task> act)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(act);

        var before = Snapshot();
        Exception? thrown = null;

        try
        {
            await act(_aggregate).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        return Verify<TException>(thrown, before);
    }

    // ------------------------------------------------------------------ internals

    private string Name => typeof(TAggregate).Name;

    private TException Verify<TException>(Exception? thrown, IDomainEvent[] before) where TException : Exception
    {
        var raised = new RaisedEvents(Delta(before), Name, ActionSource);

        if (thrown is null)
        {
            throw new AggregateAssertionException(
                $"Expected {Name} to throw {typeof(TException).Name}, but the call returned normally."
                + Environment.NewLine + Environment.NewLine + raised);
        }

        if (thrown is not TException expected)
        {
            throw new AggregateAssertionException(
                $"Expected {Name} to throw {typeof(TException).Name}, but it threw {thrown.GetType().Name}: {thrown.Message}"
                + Environment.NewLine + Environment.NewLine + raised,
                thrown);
        }

        if (raised.Count > 0)
        {
            throw new AggregateAssertionException(
                $"{Name} threw {typeof(TException).Name} as expected, but it raised {raised.Count} {(raised.Count == 1 ? "event" : "events")} first."
                + Environment.NewLine
                + "An aggregate that raises an event and then refuses the command leaves an event describing something that never happened."
                + Environment.NewLine + Environment.NewLine + raised,
                expected);
        }

        return expected;
    }

    private IDomainEvent[] Snapshot() => _aggregate.DomainEvents.ToArray();

    private IDomainEvent[] Delta(IDomainEvent[] before)
    {
        var after = Snapshot();

        if (!StartsWith(after, before))
        {
            throw new AggregateAssertionException(
                $"The action removed events from {Name} instead of only adding to them, so this kit cannot "
                + "tell which events the action raised. Assert on PendingEvents instead.");
        }

        return after.Skip(before.Length).ToArray();
    }

    private static bool StartsWith(IDomainEvent[] after, IDomainEvent[] before)
    {
        if (after.Length < before.Length)
        {
            return false;
        }

        for (var index = 0; index < before.Length; index++)
        {
            if (!ReferenceEquals(before[index], after[index]))
            {
                return false;
            }
        }

        return true;
    }
}
