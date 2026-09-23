using DDDToolkit.Interfaces;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// What one domain event becomes when it leaves this module: the outbound counterpart of
/// <see cref="IIntegrationEventHandler{TContract}"/>. Implement it in the module that raises the event,
/// once per event it publishes, and register it on the outbox with
/// <c>outbox.PublishWith&lt;T&gt;()</c> or <c>outbox.PublishFromAssemblyContaining&lt;T&gt;()</c>.
/// <code>
/// public sealed class PublishOrderPlaced : IOutboundIntegrationEvent&lt;OrderPlaced, OrderPlacedV2&gt;
/// {
///     public ValueTask&lt;OrderPlacedV2?&gt; CreateAsync(OrderPlaced placed, CancellationToken cancellationToken)
///         =&gt; new(new OrderPlacedV2(placed.OrderId.Value, placed.Total.Amount, placed.Total.Currency));
/// }
/// </code>
/// <para>
/// It is a class, rather than the lambda <c>outbox.PublishAs</c> takes, for two reasons. The translation
/// is application code and belongs next to the aggregate it publishes for, not in the module's
/// registration. And a class can take services: it is taken from the scope the outbox processor runs
/// in when it is registered there, and otherwise constructed with its constructor services injected, so
/// it can read the module's own <c>DbContext</c> to add what the event does not carry.
/// </para>
/// <para>
/// <b>It runs at delivery, not at save.</b> Whatever it reads is the state when the processor gets to
/// the row, which may be later than the event, and a retry reads it again. Anything the contract needs
/// to be correct, such as a price or an address, belongs in the domain event. Read only what is stable
/// or cosmetic, such as a product's name, and never write: with
/// <see cref="Options.OutboxOptions.DeliverInTransaction"/> a write would commit together with the mark
/// that says the message went out.
/// </para>
/// <para>
/// Returning <see langword="null"/> drops that one occurrence. Throwing fails the delivery like a
/// failing sink does: the row records the error and is retried.
/// </para>
/// </summary>
/// <typeparam name="TDomainEvent">The domain event this module raises.</typeparam>
/// <typeparam name="TContract">The published contract it goes out as.</typeparam>
public interface IOutboundIntegrationEvent<in TDomainEvent, TContract>
    where TDomainEvent : IDomainEvent
    where TContract : class
{
    /// <summary>Creates the contract to publish for <paramref name="domainEvent"/>, or <see langword="null"/> to publish nothing for it.</summary>
    /// <param name="domainEvent">The event, read back from the outbox row.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    ValueTask<TContract?> CreateAsync(TDomainEvent domainEvent, CancellationToken cancellationToken);
}
