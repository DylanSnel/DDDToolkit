using System.Text.Json;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.EntityFramework.Outbox;

/// <summary>
/// Delivers pending <see cref="OutboxMessage"/> rows of <typeparamref name="TContext"/>: oldest first,
/// each deserialized through the <see cref="DomainEventTypeRegistry"/> and then handed on.
/// <para>
/// <b>Where a message goes.</b> With <see cref="OutboxOptions.SendTo{TSink}()"/> the row becomes an
/// <see cref="IntegrationEventMessage"/> and is published to every configured
/// <see cref="IIntegrationEventSink"/>, in registration order. With no sink it goes to the dispatch
/// delegate of <see cref="DDDEntityFrameworkOptions.DispatchInProcess"/>, which keeps the event inside
/// this process. Sinks win when both are configured, unless
/// <see cref="OutboxOptions.AlsoDispatchInProcess"/> asks for both, in which case the delegate runs
/// first with the domain event and the sinks follow with the published contract.
/// </para>
/// <para>
/// Guarantees: at-least-once. A message is marked processed (and saved) only after everything it was
/// handed to returned; a crash in between redelivers it, and two processors racing on the same table
/// may both deliver a row. Consumers must therefore be idempotent, keyed by
/// <see cref="IDomainEvent.EventId"/> (= <see cref="OutboxMessage.Id"/> =
/// <see cref="IntegrationEventMessage.MessageId"/>); <c>DomainEventInbox&lt;TContext&gt;</c> does that for
/// you on the receiving side.
/// </para>
/// <para>
/// <b>When one sink fails and another succeeds</b>, every sink is still attempted: a broken transport
/// does not stop the others. The message as a whole then counts as failed, so it is not marked
/// processed and the next run hands it to <em>all</em> sinks again, including the ones that already
/// accepted it. That is the honest cost of one row per message: per-sink bookkeeping would need one
/// row per sink. The failure is recorded on the row as an
/// <see cref="IntegrationEventDeliveryException"/> naming the sinks that threw.
/// </para>
/// <para>
/// A failure of any kind, an event name nobody registered included, is recorded on the row
/// (<see cref="OutboxMessage.LastError"/>, <see cref="OutboxMessage.Attempts"/>) and does not stop the
/// rest of the batch; rows that reached <see cref="OutboxOptions.MaxAttempts"/> are skipped until
/// <c>Attempts</c> is reset.
/// </para>
/// <para>
/// Each message is saved individually through the same context, so when an in-process handler resolves
/// that scoped context and changes an aggregate, its change commits together with the processed mark.
/// </para>
/// Register with <c>services.AddOutboxProcessor&lt;TContext&gt;()</c>, or let
/// <see cref="OutboxBackgroundService{TContext}"/> poll it.
/// </summary>
public sealed class OutboxProcessor<TContext> where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly DDDEntityFrameworkOptions _options;
    private readonly ILogger _logger;

    /// <summary>Creates a processor for the (scoped) <paramref name="context"/>.</summary>
    /// <exception cref="InvalidOperationException">The outbox is not enabled, or it has neither a sink nor a dispatch delegate to deliver through.</exception>
    public OutboxProcessor(TContext context, IServiceProvider serviceProvider, DDDEntityFrameworkOptions options, ILogger<OutboxProcessor<TContext>>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<OutboxProcessor<TContext>>.Instance;

        if (_options.Outbox is not { } outbox)
        {
            throw new InvalidOperationException($"The outbox is not enabled. Call {nameof(DDDEntityFrameworkOptions.UseOutbox)}(...) in AddDDDToolkitEntityFramework.");
        }

        if (DispatchesInProcess(outbox) && _options.Dispatcher is null)
        {
            throw new InvalidOperationException(
                $"The outbox processor has nowhere to deliver to. Call {nameof(DDDEntityFrameworkOptions.DispatchInProcess)}(...) to handle events inside this process, " +
                $"or outbox.{nameof(OutboxOptions.SendTo)}<TSink>() to publish them to a transport.");
        }
    }

    /// <summary>
    /// Loads up to <paramref name="batchSize"/> pending messages, oldest first (by write time, then by
    /// the event's <c>OccurredAt</c>), and delivers them. Returns the number of messages delivered
    /// successfully. Order is best-effort: retries and concurrent processors can reorder delivery.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchSize"/> is below 1.</exception>
    public async Task<int> ProcessPendingAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var outbox = _options.Outbox!;
        var maxAttempts = outbox.MaxAttempts;

        var messages = await _context.Set<OutboxMessage>()
            .Where(message => message.ProcessedAt == null && message.Attempts < maxAttempts)
            // Write time, then occurrence time (events written by one save share CreatedAt), then id.
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var processed = 0;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            message.Attempts++;

            try
            {
                await DeliverAsync(message, outbox, cancellationToken).ConfigureAwait(false);

                message.ProcessedAt = _options.TimeProvider.GetUtcNow();
                message.LastError = null;
                processed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                message.LastError = Describe(exception);
                _logger.LogError(exception, "Delivery of outbox message {MessageId} ({EventName}) failed on attempt {Attempt}.", message.Id, message.EventName, message.Attempts);
            }

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return processed;
    }

    private async Task DeliverAsync(OutboxMessage message, OutboxOptions outbox, CancellationToken cancellationToken)
    {
        var domainEvent = Deserialize(message, outbox);

        if (DispatchesInProcess(outbox))
        {
            await _options.Dispatcher!(_serviceProvider, [domainEvent], cancellationToken).ConfigureAwait(false);
        }

        if (!outbox.HasSinks)
        {
            return;
        }

        if (BuildMessage(message, domainEvent, outbox) is not { } published)
        {
            _logger.LogDebug("Outbox message {MessageId} ({EventName}) is mapped as not published; no sink was called.", message.Id, message.EventName);
            return;
        }

        await PublishAsync(published, outbox, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishAsync(IntegrationEventMessage published, OutboxOptions outbox, CancellationToken cancellationToken)
    {
        List<string>? failedSinks = null;
        List<Exception>? failures = null;

        foreach (var registration in outbox.Sinks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var sink = registration.Resolve(_serviceProvider);
                await sink.SendAsync(published, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Every sink is attempted: one broken transport must not starve the others.
                _logger.LogError(exception, "Sink {Sink} refused message {MessageId} ({Name}).", registration.Name, published.MessageId, published.Name);
                (failedSinks ??= []).Add(registration.Name);
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new IntegrationEventDeliveryException(published.MessageId, failedSinks!, failures);
        }
    }

    /// <summary>
    /// Turns a stored row into the message that leaves the process, or <see langword="null"/> when the
    /// map says this event stays inside. An event with no mapping is published as it stands, reusing
    /// the payload and the name the outbox already stored rather than serializing it a second time.
    /// </summary>
    private static IntegrationEventMessage? BuildMessage(OutboxMessage message, IDomainEvent domainEvent, OutboxOptions outbox)
    {
        var mapped = outbox.IntegrationEvents.TryConvert(domainEvent, out var contract);
        if (contract is null)
        {
            return null;
        }

        return new IntegrationEventMessage
        {
            MessageId = message.Id,
            Name = mapped ? IntegrationEventContract.NameOf(contract) : message.EventName,
            Version = IntegrationEventContract.VersionOf(contract),
            Payload = mapped ? JsonSerializer.Serialize(contract, contract.GetType(), outbox.JsonOptions) : message.Payload,
            OccurredAt = message.OccurredAt,
            AggregateType = message.AggregateType,
            AggregateId = message.AggregateId,
            Body = contract,
        };
    }

    private static bool DispatchesInProcess(OutboxOptions outbox) => !outbox.HasSinks || outbox.AlsoDispatchInProcess;

    private static IDomainEvent Deserialize(OutboxMessage message, OutboxOptions outbox)
    {
        if (!outbox.EventTypes.TryResolve(message.EventName, out var eventType))
        {
            throw new InvalidOperationException(
                $"No domain event type is registered under the name '{message.EventName}'. Register it with outbox.RegisterEventsFromAssembly(...) or outbox.RegisterEvent<T>().");
        }

        return JsonSerializer.Deserialize(message.Payload, eventType, outbox.JsonOptions) as IDomainEvent
            ?? throw new JsonException($"The payload of outbox message {message.Id} deserialized to null.");
    }

    private static string Describe(Exception exception)
    {
        var text = $"{exception.GetType().FullName}: {exception.Message}";
        return text.Length <= DomainEventStorage.MaxErrorLength ? text : text[..DomainEventStorage.MaxErrorLength];
    }
}
