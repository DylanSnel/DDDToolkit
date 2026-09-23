using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// The sink that delivers to the other modules in this process. It offers each published message to
/// every consuming module, and each module hands it to its own
/// <see cref="IIntegrationEventHandler{TContract}"/>s for the <b>contract</b>, one inbox row at a time.
/// <para>
/// In a modular monolith this is the sink that matters. Integration events exist so another module can
/// react, and that module is in the same process, so there is no broker in the picture at all. What the
/// toolkit did before sinks existed was dispatch the <em>domain event</em> to local handlers, and that
/// forces the consuming module to reference the producing module's domain assembly. Modules exist to
/// prevent exactly that coupling. This sink delivers the contract instead, which the two modules can
/// share without sharing a domain.
/// </para>
/// <para>
/// <b>Neither side names the other.</b> The producing module says <c>outbox.SendToModules()</c>; each
/// consuming module registers itself with
/// <c>services.AddModuleIntegrationEvents&lt;TContext&gt;(module =&gt; module.Handle&lt;TContract, THandler&gt;())</c>.
/// Adding a consumer is a change to that consumer only.
/// </para>
/// <para>
/// <b>Why through the outbox and not a direct call.</b> A direct call puts the consumer inside the
/// producer's transaction: the consuming module throws and the producing module's aggregate is rolled
/// back, which is the coupling again in a worse form. The outbox breaks it. Module A commits, the row
/// is durable, and whether module B succeeds is module B's problem and a later retry.
/// </para>
/// <para>
/// <b>Why through the inbox.</b> A retry replays the message to every handler, so a handler that already
/// succeeded would run twice. Each handler runs inside its module's
/// <see cref="DomainEventInbox{TContext}"/> under its own consumer name, so its writes and its "applied"
/// row commit together. One handler throwing fails the message, but the handlers that succeeded keep
/// their rows, in whichever module they live, and are skipped on the retry.
/// </para>
/// <code>
/// // the producing module
/// services.AddDDDToolkitEntityFramework(options => options.UseOutbox&lt;OrderingContext&gt;(outbox =>
/// {
///     outbox.RegisterEventsFromAssemblyContaining&lt;Order&gt;();
///     outbox.PublishAs&lt;OrderPlaced, OrderPlacedV2&gt;(e =&gt; new OrderPlacedV2(e.OrderId.Value, e.Total.Amount));
///     outbox.SendToModules();
/// }));
/// services.AddOutboxBackgroundService&lt;OrderingContext&gt;(TimeSpan.FromSeconds(2));
///
/// // a consuming module
/// services.AddDDDToolkitEntityFramework(options => options.MapIntegrationEvents(c => c.RegisterFromAssemblyContaining&lt;OrderPlacedV2&gt;()));
/// services.AddModuleIntegrationEvents&lt;BillingContext&gt;(module => module.Handle&lt;OrderPlacedV2, RaiseInvoice&gt;());
/// </code>
/// <para>
/// Each consuming context's model needs the inbox table: call <c>modelBuilder.AddDomainEventInbox()</c>.
/// </para>
/// </summary>
public sealed class ModuleIntegrationEventSink : IIntegrationEventSink
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DDDEntityFrameworkOptions _options;
    private readonly ILogger _logger;

    /// <summary>Creates the sink. Resolve it from the scope the outbox processor runs in.</summary>
    /// <param name="serviceProvider">The scope the consuming modules and their handlers are resolved from.</param>
    /// <param name="options">Supplies the contract registry the payload is read through.</param>
    /// <param name="logger">Optional.</param>
    public ModuleIntegrationEventSink(IServiceProvider serviceProvider, DDDEntityFrameworkOptions options, ILogger<ModuleIntegrationEventSink>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<ModuleIntegrationEventSink>.Instance;
    }

    /// <summary>
    /// Offers <paramref name="message"/> to every consuming module. Returns when all their handlers for
    /// its contract applied it or had already applied it; throws naming the ones that did not.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    /// <exception cref="IntegrationEventDeliveryException">One or more handlers threw.</exception>
    public async Task SendAsync(IntegrationEventMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var contract = Resolve(message);
        if (contract is null)
        {
            return;
        }

        var modules = _serviceProvider.GetServices<IModuleIntegrationEventConsumer>().ToList();
        if (modules.Count == 0)
        {
            _logger.LogDebug("No module consumes integration events; message {MessageId} ('{Name}') is delivered by default.", message.MessageId, message.Name);
            return;
        }

        List<string>? failedConsumers = null;
        List<Exception>? failures = null;

        // Every module is offered the message even when an earlier one failed: one module's bug must not
        // hold up the others. They all see it again on the retry, and their inboxes skip what they applied.
        foreach (var module in modules)
        {
            foreach (var (consumer, failure) in await module.DeliverAsync(contract, message, cancellationToken).ConfigureAwait(false))
            {
                (failedConsumers ??= []).Add($"{module.Module}/{consumer}");
                (failures ??= []).Add(failure);
            }
        }

        if (failures is not null)
        {
            throw new IntegrationEventDeliveryException(message.MessageId, failedConsumers!, failures);
        }
    }

    /// <summary>
    /// The contract to hand the handlers.
    /// <para>
    /// The payload is preferred over <see cref="IntegrationEventMessage.Body"/> whenever the registry
    /// knows the message's name and version, because that is the boundary doing its job: the consuming
    /// module gets its own object, read through the upcasters, exactly as it would from a queue. The
    /// body is the fallback for a contract nobody registered, which is what an unmapped domain event
    /// published as it stands looks like. A message with a body and no payload came through a broker that
    /// deserializes by type, MassTransit or Wolverine, and the body is all there is.
    /// </para>
    /// </summary>
    private object? Resolve(IntegrationEventMessage message)
    {
        // A broker that routes by type has already read the payload into the contract, and hands over the
        // object with no text to read again.
        if (message.Payload.Length == 0 && message.Body is { } typed)
        {
            return typed;
        }

        if (_options.Contracts.TryRead(message, out var contract))
        {
            return contract;
        }

        if (message.Body is { } body)
        {
            return body;
        }

        _logger.LogWarning(
            "Message {MessageId} arrived as '{Name}' version {Version} with no registered contract and no body, so no module could be offered it.",
            message.MessageId, message.Name, message.Version);

        return null;
    }
}
