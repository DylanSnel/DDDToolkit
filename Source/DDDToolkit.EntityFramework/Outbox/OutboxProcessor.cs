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
/// processed and the next attempt hands it to <em>all</em> sinks again, including the ones that already
/// accepted it. That is the honest cost of one row per message: per-sink bookkeeping would need one
/// row per sink. The failure is recorded on the row as an
/// <see cref="IntegrationEventDeliveryException"/> naming the sinks that threw.
/// </para>
/// <para>
/// A failure of any kind, an event name nobody registered included, is recorded on the row
/// (<see cref="OutboxMessage.LastError"/>, <see cref="OutboxMessage.Attempts"/>) and does not stop the
/// rest of the batch. The row is then left alone until its <see cref="OutboxMessage.NextAttemptAt"/>,
/// which <see cref="OutboxOptions.RetryDelay"/> pushes further out after every failure, so it neither
/// holds back the rows behind it nor spends its attempts in one busy drain. Rows that reached
/// <see cref="OutboxOptions.MaxAttempts"/> are skipped until <c>Attempts</c> is reset.
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
    private readonly OutboxOptions _outbox;
    private readonly ILogger _logger;

    /// <summary>Creates a processor for the (scoped) <paramref name="context"/>.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="TContext"/> has no outbox, or it has neither a sink nor a dispatch delegate to deliver through.</exception>
    public OutboxProcessor(TContext context, IServiceProvider serviceProvider, DDDEntityFrameworkOptions options, ILogger<OutboxProcessor<TContext>>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<OutboxProcessor<TContext>>.Instance;

        // The context's own outbox when it has one, the shared one otherwise: the same answer the
        // interceptor gave when it wrote the rows.
        if (_options.OutboxFor(typeof(TContext)) is not { } outbox)
        {
            throw new InvalidOperationException(
                $"{typeof(TContext).Name} has no outbox. Call {nameof(DDDEntityFrameworkOptions.UseOutbox)}<{typeof(TContext).Name}>(...) " +
                $"or the shared {nameof(DDDEntityFrameworkOptions.UseOutbox)}(...) in AddDDDToolkitEntityFramework.");
        }

        _outbox = outbox;

        if (DispatchesInProcess(outbox) && _options.Dispatcher is null)
        {
            throw new InvalidOperationException(
                $"The outbox processor has nowhere to deliver to. Call {nameof(DDDEntityFrameworkOptions.DispatchInProcess)}(...) to handle events inside this process, " +
                $"or outbox.{nameof(OutboxOptions.SendTo)}<TSink>() to publish them to a transport.");
        }
    }

    /// <summary>
    /// Loads up to <paramref name="batchSize"/> pending messages that are due, oldest first (by write
    /// time, then by the event's <c>OccurredAt</c>), and delivers them. A message that failed is due again
    /// at its <see cref="OutboxMessage.NextAttemptAt"/>, so the messages behind it are loaded in the
    /// meantime. Returns the number of messages delivered successfully. Order is best-effort: retries
    /// and concurrent processors can reorder delivery.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchSize"/> is below 1.</exception>
    public async Task<int> ProcessPendingAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var outbox = _outbox;
        var maxAttempts = outbox.MaxAttempts;
        var now = _options.TimeProvider.GetUtcNow();

        var messages = await _context.Set<OutboxMessage>()
            .Where(message => message.ProcessedAt == null && message.Attempts < maxAttempts)
            .Where(message => message.NextAttemptAt == null || message.NextAttemptAt <= now)
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

            if (await DeliverOneAsync(message, outbox, cancellationToken).ConfigureAwait(false))
            {
                processed++;
            }
        }

        return processed;
    }

    /// <summary>
    /// Attempts one message and saves the outcome. With
    /// <see cref="OutboxOptions.DeliverInTransaction"/> the attempt and the mark share a transaction, so
    /// a sink that writes to this database commits with the mark or not at all; otherwise the mark is
    /// its own write and delivery is at-least-once, which is what a sink outside the database gives you
    /// anyway.
    /// </summary>
    private async Task<bool> DeliverOneAsync(OutboxMessage message, OutboxOptions outbox, CancellationToken cancellationToken)
    {
        // Owned only when the caller has none: joining theirs is what lets a caller batch the whole run.
        var transaction = outbox.DeliverInTransaction && _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        try
        {
            message.Attempts++;
            var delivered = false;

            try
            {
                await DeliverAsync(message, outbox, cancellationToken).ConfigureAwait(false);

                message.ProcessedAt = _options.TimeProvider.GetUtcNow();
                message.NextAttemptAt = null;
                message.LastError = null;
                delivered = true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                message.LastError = Describe(exception);
                message.NextAttemptAt = NextAttemptAfterFailure(message.Attempts, outbox);

                if (message.NextAttemptAt is { } next)
                {
                    _logger.LogError(exception, "Delivery of outbox message {MessageId} ({EventName}) failed on attempt {Attempt}; it is tried again at {NextAttemptAt}.", message.Id, message.EventName, message.Attempts, next);
                }
                else
                {
                    _logger.LogError(exception, "Delivery of outbox message {MessageId} ({EventName}) failed on attempt {Attempt}, its last; it stays in the outbox until Attempts is reset.", message.Id, message.EventName, message.Attempts);
                }

                if (transaction is not null)
                {
                    // Roll the failed attempt back with whatever the sinks wrote here, then record the
                    // failure on its own so the attempt count and the error survive.
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

                    // A sink that saved through this context saved the incremented Attempts with it,
                    // and the rollback took that write back while the change tracker still counts it
                    // as done. Mark the bookkeeping as changed so it is written whatever a sink saved.
                    var entry = _context.Entry(message);
                    entry.Property(m => m.Attempts).IsModified = true;
                    entry.Property(m => m.NextAttemptAt).IsModified = true;
                    entry.Property(m => m.LastError).IsModified = true;

                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return false;
                }
            }

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return delivered;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// When a message that just failed its <paramref name="attempts"/>th attempt is due again, or
    /// <see langword="null"/> when that was its last. A row out of attempts is not loaded whatever this
    /// says, so the null only means that resetting <c>Attempts</c> makes it due at once, rather than
    /// after a wait nobody asked for.
    /// </summary>
    private DateTimeOffset? NextAttemptAfterFailure(int attempts, OutboxOptions outbox)
    {
        if (attempts >= outbox.MaxAttempts)
        {
            return null;
        }

        // Read now rather than at the start of the batch, so an attempt that waited out a timeout is
        // not due again the moment it gave up.
        var now = _options.TimeProvider.GetUtcNow();
        var delay = outbox.RetryDelay(attempts);

        if (delay <= TimeSpan.Zero)
        {
            return now;
        }

        // A delay past the end of the calendar means never, and must not throw on the failure path,
        // where it would lose the record of the attempt.
        return delay < DateTimeOffset.MaxValue - now ? now + delay : DateTimeOffset.MaxValue;
    }

    private async Task DeliverAsync(OutboxMessage message, OutboxOptions outbox, CancellationToken cancellationToken)
    {
        var domainEvent = Deserialize(message, outbox, _options.Contracts);

        if (DispatchesInProcess(outbox))
        {
            await _options.Dispatcher!(_serviceProvider, [domainEvent], cancellationToken).ConfigureAwait(false);
        }

        if (!outbox.HasSinks)
        {
            return;
        }

        if (await BuildMessageAsync(message, domainEvent, outbox, cancellationToken).ConfigureAwait(false) is not { } published)
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
    private async ValueTask<IntegrationEventMessage?> BuildMessageAsync(OutboxMessage message, IDomainEvent domainEvent, OutboxOptions outbox, CancellationToken cancellationToken)
    {
        var mapped = outbox.IntegrationEvents.IsMapped(domainEvent.GetType());
        var contract = await outbox.IntegrationEvents.ConvertAsync(domainEvent, _serviceProvider, cancellationToken).ConfigureAwait(false);
        if (contract is null)
        {
            return null;
        }

        // Published as it stands, the row already says what it is. Published as a contract, the entry
        // says it when it was registered with a name and version, as the generated registration does; a
        // PublishAs lambda leaves it to the contract's attributes.
        var (name, version) = !mapped
            ? (message.EventName, CurrentVersionOf(domainEvent.GetType(), outbox))
            : outbox.IntegrationEvents.TryDescribeContract(domainEvent.GetType(), out var contractName, out var contractVersion)
                ? (contractName, contractVersion)
                : (IntegrationEventContract.NameOf(contract), IntegrationEventContract.VersionOf(contract));

        return new IntegrationEventMessage
        {
            MessageId = message.Id,
            Name = name,
            Version = version,
            Payload = mapped ? JsonSerializer.Serialize(contract, contract.GetType(), outbox.JsonOptions) : message.Payload,
            OccurredAt = message.OccurredAt,
            AggregateType = message.AggregateType,
            AggregateId = message.AggregateId,
            Body = contract,
        };
    }

    private static bool DispatchesInProcess(OutboxOptions outbox) => !outbox.HasSinks || outbox.AlsoDispatchInProcess;

    /// <summary>The version the event type was registered at, or what its attributes say when nobody registered it with one.</summary>
    private static int CurrentVersionOf(Type eventType, OutboxOptions outbox)
        => outbox.EventTypes.TryDescribe(eventType, out _, out var registered)
            ? registered
            : IntegrationEventContract.VersionOf(eventType);

    /// <summary>
    /// Reads the stored row back as the domain event it was written from.
    /// <para>
    /// The row says which shape it was written in. When that is the shape the registered type has today,
    /// which is every row until somebody bumps a version, the payload is deserialized straight into it.
    /// When it is older, the payload is read as the type registered under that name and version and then
    /// upcast, so a row written before a deployment is still deliverable after it.
    /// </para>
    /// </summary>
    private static IDomainEvent Deserialize(OutboxMessage message, OutboxOptions outbox, IntegrationEventContractRegistry contracts)
    {
        if (!outbox.EventTypes.TryResolve(message.EventName, out var eventType))
        {
            throw new InvalidOperationException(
                $"No domain event type is registered under the name '{message.EventName}'. Register it with outbox.RegisterEventsFromAssembly(...) or outbox.RegisterEvent<T>().");
        }

        // A column added to an existing table without a default reads as 0; that row predates versioning.
        var storedVersion = message.Version < 1 ? 1 : message.Version;
        var currentVersion = CurrentVersionOf(eventType, outbox);

        if (storedVersion != currentVersion)
        {
            return Upcast(message, contracts, eventType, storedVersion, currentVersion);
        }

        return JsonSerializer.Deserialize(message.Payload, eventType, outbox.JsonOptions) as IDomainEvent
            ?? throw new JsonException($"The payload of outbox message {message.Id} deserialized to null.");
    }

    private static IDomainEvent Upcast(OutboxMessage message, IntegrationEventContractRegistry contracts, Type currentType, int storedVersion, int currentVersion)
    {
        if (!contracts.TryResolve(message.EventName, storedVersion, out var storedType))
        {
            throw new InvalidOperationException(
                $"Outbox message {message.Id} was written as '{message.EventName}' version {storedVersion}, but '{currentType}' is version {currentVersion} today and nothing is registered for the older shape. " +
                $"Keep the old record and register it with options.MapIntegrationEvents(c => c.UpcastFrom<{message.EventName}V{storedVersion}, {currentType.Name}>(...)).");
        }

        var upcast = contracts.ReadAs(message.Payload, storedType, message.EventName, storedVersion);

        return upcast as IDomainEvent
            ?? throw new InvalidOperationException(
                $"Outbox message {message.Id} upcast from '{storedType}' to '{upcast.GetType()}', which is not a domain event. The chain has to end at the type registered under '{message.EventName}'.");
    }

    private static string Describe(Exception exception)
    {
        var text = $"{exception.GetType().FullName}: {exception.Message}";
        return text.Length <= DomainEventStorage.MaxErrorLength ? text : text[..DomainEventStorage.MaxErrorLength];
    }
}
