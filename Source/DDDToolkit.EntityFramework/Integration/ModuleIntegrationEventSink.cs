using System.Collections.Concurrent;
using System.Reflection;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.EntityFramework.Integration;

/// <summary>
/// The sink that delivers to the other modules in this process. It hands each published message to
/// every <see cref="IIntegrationEventHandler{TContract}"/> registered against the <b>contract</b>, one
/// inbox row at a time.
/// <para>
/// In a modular monolith this is the sink that matters. Integration events exist so another module can
/// react, and that module is in the same process, so there is no broker in the picture at all. What the
/// toolkit did before sinks existed was dispatch the <em>domain event</em> to local handlers, and that
/// forces the consuming module to reference the producing module's domain assembly. Modules exist to
/// prevent exactly that coupling. This sink delivers the contract instead, which the two modules can
/// share without sharing a domain.
/// </para>
/// <para>
/// <b>Why through the outbox and not a direct call.</b> A direct call puts the consumer inside the
/// producer's transaction: the consuming module throws and the producing module's aggregate is rolled
/// back, which is the coupling again in a worse form. The outbox breaks it. Module A commits, the row
/// is durable, and whether module B succeeds is module B's problem and a later retry.
/// </para>
/// <para>
/// <b>Why through the inbox.</b> A retry replays the message to every handler, so a handler that already
/// succeeded would run twice. Each handler runs inside
/// <see cref="DomainEventInbox{TContext}.ExecuteOnceAsync(IntegrationEventMessage, string, Func{IntegrationEventMessage, CancellationToken, Task}, CancellationToken)"/>
/// under its own consumer name, so its writes and its "applied" row commit together. One handler
/// throwing fails the message, but the handlers that succeeded keep their rows and are skipped on the
/// retry.
/// </para>
/// <code>
/// builder.Services.AddDDDToolkitEntityFramework(options =>
/// {
///     options.MapIntegrationEvents(contracts => contracts.RegisterFromAssemblyContaining&lt;OrderPlacedV2&gt;());
///     options.UseOutbox(outbox =>
///     {
///         outbox.RegisterEventsFromAssemblyContaining&lt;Program&gt;();
///         outbox.PublishAs&lt;OrderPlaced, OrderPlacedV2&gt;(e =&gt; new OrderPlacedV2(e.OrderId.Value, e.Total.Amount));
///         outbox.SendToModules&lt;AppContext&gt;();
///     });
/// });
///
/// builder.Services.AddIntegrationEventHandler&lt;OrderPlacedV2, RaiseInvoice&gt;();
/// builder.Services.AddDomainEventInbox&lt;AppContext&gt;();
/// builder.Services.AddOutboxBackgroundService&lt;AppContext&gt;(TimeSpan.FromSeconds(2));
/// </code>
/// <para>
/// The context's model needs the inbox table: call <c>modelBuilder.AddDomainEventInbox()</c>.
/// </para>
/// </summary>
/// <typeparam name="TContext">The context whose inbox records which handler applied which message.</typeparam>
public sealed class ModuleIntegrationEventSink<TContext> : IIntegrationEventSink where TContext : DbContext
{
    private static readonly ConcurrentDictionary<Type, (Type HandlerType, MethodInfo Handle)> Dispatch = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly DomainEventInbox<TContext> _inbox;
    private readonly DDDEntityFrameworkOptions _options;
    private readonly ILogger _logger;

    /// <summary>Creates the sink. Resolve it from the scope the outbox processor runs in.</summary>
    /// <param name="serviceProvider">The scope the handlers are resolved from.</param>
    /// <param name="inbox">Records which handler applied which message.</param>
    /// <param name="options">Supplies the contract registry the payload is read through.</param>
    /// <param name="logger">Optional.</param>
    public ModuleIntegrationEventSink(
        IServiceProvider serviceProvider,
        DomainEventInbox<TContext> inbox,
        DDDEntityFrameworkOptions options,
        ILogger<ModuleIntegrationEventSink<TContext>>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<ModuleIntegrationEventSink<TContext>>.Instance;
    }

    /// <summary>
    /// Delivers <paramref name="message"/> to every handler registered against its contract. Returns
    /// when they all applied it or had already applied it; throws naming the ones that did not.
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

        var (handlerType, handle) = Dispatch.GetOrAdd(contract.GetType(), static type =>
        {
            var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(type);
            return (handlerType, handlerType.GetMethod(nameof(IIntegrationEventHandler<object>.HandleAsync))!);
        });

        var handlers = _serviceProvider.GetServices(handlerType).OfType<object>().ToList();
        if (handlers.Count == 0)
        {
            _logger.LogDebug("No module handles '{Name}' version {Version}; message {MessageId} is delivered by default.", message.Name, message.Version, message.MessageId);
            return;
        }

        List<string>? failedConsumers = null;
        List<Exception>? failures = null;

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var consumer = IntegrationEventConsumer.NameOf(handler.GetType());

            try
            {
                // Each handler owns its inbox row, so a failure here does not undo the handlers that ran
                // before it and does not make them run again on the retry.
                var applied = await _inbox.ExecuteOnceAsync(
                    message,
                    consumer,
                    (received, token) => (Task)handle.Invoke(handler, [contract, received, token])!,
                    cancellationToken).ConfigureAwait(false);

                if (!applied)
                {
                    _logger.LogDebug("Consumer {Consumer} had already applied message {MessageId}.", consumer, message.MessageId);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(exception, "Consumer {Consumer} failed on message {MessageId} ({Name}).", consumer, message.MessageId, message.Name);
                (failedConsumers ??= []).Add(consumer);
                (failures ??= []).Add(exception);
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
    /// published as it stands looks like.
    /// </para>
    /// </summary>
    private object? Resolve(IntegrationEventMessage message)
    {
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
