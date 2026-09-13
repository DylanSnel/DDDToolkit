using DDDToolkit.Interfaces;

namespace DDDToolkit.Testing;

/// <summary>
/// The short form, for a test that does not need a whole scenario. These are the two casts to
/// <c>IHasDomainEvents</c> that every aggregate test otherwise writes by hand.
/// </summary>
public static class AggregateTestingExtensions
{
    /// <summary>
    /// The events the aggregate is holding, without taking them off it. Assert on these when one
    /// call is all the test does.
    /// </summary>
    /// <param name="aggregate">The aggregate under test.</param>
    /// <returns>The pending events, in the order they were raised.</returns>
    /// <example>
    /// <code>
    /// order.Cancel("out of stock");
    ///
    /// order.PendingEvents().RaisedExactly&lt;OrderPlaced, OrderCancelled&gt;();
    /// </code>
    /// </example>
    public static RaisedEvents PendingEvents(this IHasDomainEvents aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        return new RaisedEvents(
            aggregate.DomainEvents.ToArray(),
            aggregate.GetType().Name,
            "pending on the aggregate");
    }

    /// <summary>
    /// Takes the pending events off the aggregate, the way the persistence layer does during a save,
    /// and returns them.
    /// </summary>
    /// <param name="aggregate">The aggregate under test.</param>
    /// <returns>The drained events, in the order they were raised.</returns>
    public static RaisedEvents DrainEvents(this IHasDomainEvents aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        return new RaisedEvents(
            aggregate.DequeueDomainEvents(),
            aggregate.GetType().Name,
            "drained from the aggregate");
    }

    /// <summary>Starts a scenario over this aggregate. The same thing as <c>AggregateScenario.Given</c>.</summary>
    /// <typeparam name="TAggregate">The aggregate root type. Inferred from the argument.</typeparam>
    /// <param name="aggregate">The aggregate under test.</param>
    /// <returns>A scenario over that aggregate.</returns>
    public static AggregateScenario<TAggregate> AsScenario<TAggregate>(this TAggregate aggregate)
        where TAggregate : class, IHasDomainEvents
        => AggregateScenario.Given(aggregate);
}
