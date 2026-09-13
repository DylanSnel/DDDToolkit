using DDDToolkit.BaseTypes;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// A consumer of a published contract, in this process. Implement it in the module that reacts, once
/// per contract it cares about, and register it with
/// <c>services.AddIntegrationEventHandler&lt;TContract, THandler&gt;()</c>.
/// <para>
/// The type argument is the <b>contract</b>, never the producing module's domain event. That is the
/// whole point of the seam: a handler typed on <c>OrderPlaced</c> forces this module to reference the
/// ordering module's domain assembly, and then the two are one module again with extra steps. A handler
/// typed on <c>OrderPlacedV2</c> needs the contract and nothing else.
/// </para>
/// <code>
/// [IntegrationEventConsumer("billing.invoicer")]
/// public sealed class RaiseInvoice(BillingContext context) : IIntegrationEventHandler&lt;OrderPlacedV2&gt;
/// {
///     public Task HandleAsync(OrderPlacedV2 contract, IntegrationEventMessage message, CancellationToken cancellationToken)
///     {
///         context.Invoices.Add(new Invoice(contract.OrderId, contract.Total));
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// <para>
/// Do not call <c>SaveChanges</c> for a marker of your own. <see cref="ModuleIntegrationEventSink{TContext}"/>
/// runs the handler inside the inbox, so your writes and the row that says "this consumer applied this
/// message" are written by one save inside one transaction.
/// </para>
/// <para>
/// Throwing says this consumer did not apply the message. Its inbox row is rolled back with its writes,
/// the outbox message as a whole counts as failed, and the next run replays it to <em>this</em>
/// handler only: handlers that already succeeded keep their rows and are skipped.
/// </para>
/// </summary>
/// <typeparam name="TContract">The published contract this handler reacts to.</typeparam>
public interface IIntegrationEventHandler<in TContract> where TContract : class
{
    /// <summary>
    /// Reacts to <paramref name="contract"/>. Write through the same <c>DbContext</c> the sink's inbox
    /// uses, and leave the saving to it.
    /// </summary>
    /// <param name="contract">The contract, already upcast to the shape this handler asked for.</param>
    /// <param name="message">The envelope it arrived in, for the message id, the occurrence time and the aggregate it came from.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    Task HandleAsync(TContract contract, IntegrationEventMessage message, CancellationToken cancellationToken = default);
}
