using System.Text.Json;
using DDDToolkit.Access;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
/// handed to returned; a crash in between redelivers it. Consumers must therefore be idempotent, keyed by
/// <see cref="IDomainEvent.EventId"/> (= <see cref="OutboxMessage.Id"/> =
/// <see cref="IntegrationEventMessage.MessageId"/>); <c>DomainEventInbox&lt;TContext&gt;</c> does that for
/// you on the receiving side.
/// </para>
/// <para>
/// <b>Two processors on one table.</b> A batch starts with a claim: one <c>UPDATE</c> that writes a new
/// <see cref="OutboxMessage.LockedBy"/> and a <see cref="OutboxMessage.LockedUntil"/>
/// <see cref="OutboxOptions.LeaseDuration"/> ahead on the oldest due rows that no other processor holds. The
/// statement repeats that condition on the row it changes, so of two processors that claim at once, each
/// row goes to one of them. Every mark carries the claim in its <c>WHERE</c> clause, because
/// <c>LockedBy</c> is a concurrency token, and clears it. A mark that changes no row means another processor
/// claimed the message after this lease ended: the save fails, nothing it carried is written, and the
/// message counts neither as delivered nor as failed. The processor starts a message only while at least
/// half the lease is left, and releases the rows it did not reach when it stops early, is cancelled or
/// throws. So two processors deliver one message twice only when one delivery takes longer than half the
/// lease.
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
/// that scoped context and changes an aggregate without saving it, its change commits together with the
/// processed mark, unless the host requires an explicit current user (below).
/// </para>
/// <para>
/// <b>Who it runs as.</b> Where the host requires an explicit current user
/// (<see cref="CurrentUserServiceCollectionExtensions.RequireExplicitCurrentUser"/>), the processor begins
/// <c>CurrentUserSnapshot.System</c> around its own bookkeeping: reading, marking and saving the rows, turning an event
/// into the contract it is published as (<see cref="Integration.IOutboundIntegrationEvent{TDomainEvent, TContract}"/>
/// and <c>PublishAs</c>), and handing it to the sinks, a sink of your own included. Code of yours that runs
/// there runs past row level security, so it reads only what the event's own module publishes, and writes
/// nothing. The in-process dispatch runs with nobody begun, so a handler runs as the user it begins itself;
/// the module sink runs each module's handlers as the module's scopes say. A change an in-process handler
/// leaves on this processor's context, rather than saving it itself, would be saved with the mark as the
/// system: the message fails instead, the change is not saved, and the delivery is tried again. A handler
/// writes as a caller it begins, and saves its own work; on a context of its own when
/// <see cref="OutboxOptions.DeliverInTransaction"/> is on, because the processor's context then holds a
/// transaction begun as the system, and a caller that changes inside it is refused. A transaction the host
/// began on the processor's context is joined, so it has to be begun as the system too.
/// </para>
/// <para>
/// Without it the processor begins nothing itself: its bookkeeping and the in-process handlers run as
/// whoever is current, the system outside any caller, and a module's handlers as its scopes say, or as whoever
/// is current when it has none.
/// </para>
/// Register with <c>services.AddOutboxProcessor&lt;TContext&gt;()</c>, or let
/// <see cref="OutboxBackgroundService{TContext}"/> poll it.
/// </summary>
public sealed class OutboxProcessor<TContext> where TContext : DbContext
{
    /// <summary>How long <see cref="ReleaseAsync"/> waits for the database.</summary>
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The machine and the process, the readable part of every claim this process writes.</summary>
    private static readonly string ClaimPrefix = DescribeProcess();

    private readonly TContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly DDDEntityFrameworkOptions _options;
    private readonly OutboxOptions _outbox;
    private readonly ILogger _logger;
    private readonly bool _requireExplicitCurrentUser;

    /// <summary>Creates a processor for the (scoped) <paramref name="context"/>.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="TContext"/> has no outbox, or it has neither a sink nor a dispatch delegate to deliver through.</exception>
    public OutboxProcessor(TContext context, IServiceProvider serviceProvider, DDDEntityFrameworkOptions options, ILogger<OutboxProcessor<TContext>>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<OutboxProcessor<TContext>>.Instance;
        _requireExplicitCurrentUser = ToolkitWork.Required(serviceProvider);

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
    /// Claims up to <paramref name="batchSize"/> pending messages that are due and that no other processor
    /// holds, oldest first (by write time, then by the event's <c>OccurredAt</c>), and delivers them. A
    /// message that failed is due again at its <see cref="OutboxMessage.NextAttemptAt"/>, so the messages
    /// behind it are claimed in the meantime. Returns the number of messages delivered successfully. Order is
    /// best-effort: retries and concurrent processors can reorder delivery.
    /// <para>
    /// The claim holds the messages for <see cref="OutboxOptions.LeaseDuration"/>. When less than half of it is
    /// left, the processor stops the batch and releases the messages it did not reach; the next call claims
    /// them again. The same release runs when <paramref name="cancellationToken"/> stops the batch or a save
    /// throws.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchSize"/> is below 1.</exception>
    public async Task<int> ProcessPendingAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        // The rows, their marks and the sinks are the toolkit's own bookkeeping; the handlers are not.
        using var bookkeeping = ToolkitWork.BeginBookkeeping(_requireExplicitCurrentUser);

        var outbox = _outbox;
        var claimedAt = _options.TimeProvider.GetUtcNow();
        var lease = outbox.LeaseDuration;
        var claim = NewClaim();

        var messages = await ClaimAsync(claim, claimedAt, claimedAt + lease, batchSize, outbox.MaxAttempts, cancellationToken).ConfigureAwait(false);
        if (messages.Count == 0)
        {
            return 0;
        }

        // A message started at this moment or later could still run when the lease ends.
        var startBefore = claimedAt + (lease / 2);
        var processed = 0;
        var finished = 0;

        try
        {
            foreach (var message in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_options.TimeProvider.GetUtcNow() >= startBefore)
                {
                    _logger.LogInformation(
                        "Less than half of the {LeaseDuration} lease on the outbox of {Context} is left; {Count} claimed messages are released for the next batch.",
                        lease, typeof(TContext).Name, messages.Count - finished);
                    break;
                }

                if (await DeliverOneAsync(message, outbox, cancellationToken).ConfigureAwait(false))
                {
                    processed++;
                }

                finished++;
            }
        }
        finally
        {
            if (finished < messages.Count)
            {
                await ReleaseAsync(claim).ConfigureAwait(false);
            }
        }

        return processed;
    }

    /// <summary>
    /// Writes <paramref name="claim"/> into up to <paramref name="batchSize"/> due rows that no processor holds,
    /// and loads the rows it wrote, oldest first.
    /// <para>
    /// One <c>UPDATE</c> takes the rows. Its subquery picks the oldest candidates, and its own <c>WHERE</c> clause
    /// repeats the condition on the row it changes. A database checks that clause on the row it is about to
    /// change after the row lock: Postgres re-reads a row another transaction changed, SQL Server reads under an
    /// update lock, and SQLite runs one writer at a time. So a row another processor claimed after the
    /// subquery read it is left out, and no row is claimed twice. Without <c>RETURNING</c> in Entity Framework,
    /// a second query loads the claimed rows by their claim.
    /// </para>
    /// </summary>
    private async Task<List<OutboxMessage>> ClaimAsync(string claim, DateTimeOffset now, DateTimeOffset until, int batchSize, int maxAttempts, CancellationToken cancellationToken)
    {
        var rows = _context.Set<OutboxMessage>();

        var candidates = rows
            .Where(message => message.ProcessedAt == null && message.Attempts < maxAttempts)
            .Where(message => message.NextAttemptAt == null || message.NextAttemptAt <= now)
            .Where(message => message.LockedUntil == null || message.LockedUntil <= now)
            // Write time, then occurrence time (events written by one save share CreatedAt), then id.
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .Select(message => message.Id);

        var claimed = await rows
            .Where(message => candidates.Contains(message.Id))
            .Where(message => message.ProcessedAt == null && message.Attempts < maxAttempts)
            .Where(message => message.NextAttemptAt == null || message.NextAttemptAt <= now)
            .Where(message => message.LockedUntil == null || message.LockedUntil <= now)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(message => message.LockedBy, claim)
                    .SetProperty(message => message.LockedUntil, until),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            return [];
        }

        var messages = await rows
            .Where(message => message.LockedBy == claim)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in messages)
        {
            // A row this context tracked before the claim comes back as that instance, with the values it
            // had then. The mark's WHERE clause takes LockedBy's original value, so it has to be this claim.
            if (message.LockedBy != claim)
            {
                await _context.Entry(message).ReloadAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return messages;
    }

    /// <summary>
    /// Clears <paramref name="claim"/> from the rows that still hold it, so another processor can claim them at
    /// once instead of after the lease. It runs on its own timeout, because the batch's token may be the reason
    /// it runs. A failure is logged and not thrown: the lease then ends on its own.
    /// </summary>
    private async Task ReleaseAsync(string claim)
    {
        using var timeout = new CancellationTokenSource(ReleaseTimeout);

        try
        {
            var released = await _context.Set<OutboxMessage>()
                .Where(message => message.LockedBy == claim)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(message => message.LockedBy, (string?)null)
                        .SetProperty(message => message.LockedUntil, (DateTimeOffset?)null),
                    timeout.Token)
                .ConfigureAwait(false);

            _logger.LogDebug("Released {Count} outbox messages of {Context} that the batch did not finish.", released, typeof(TContext).Name);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Releasing the unfinished outbox messages of {Context} failed. They are claimed again when their lease ends.", typeof(TContext).Name);
        }
    }

    /// <summary>A new claim: <see cref="ClaimPrefix"/> and a Guid, so two batches never share one.</summary>
    private static string NewClaim() => ClaimPrefix + Guid.NewGuid().ToString("N");

    private static string DescribeProcess()
    {
        string machine;
        try
        {
            machine = Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            machine = "unknown";
        }

        var process = $":{Environment.ProcessId}:";

        // The Guid takes 32 characters, so the machine name is cut to what remains.
        var room = DomainEventStorage.MaxLockedByLength - 32 - process.Length;
        return (machine.Length <= room ? machine : machine[..room]) + process;
    }

    /// <summary>
    /// Attempts one message and saves the outcome. With
    /// <see cref="OutboxOptions.DeliverInTransaction"/> the attempt and the mark share a transaction, so
    /// a sink that writes to this database commits with the mark or not at all; otherwise the mark is
    /// its own write and delivery is at-least-once, which is what a sink outside the database gives you
    /// anyway.
    /// <para>
    /// Both marks clear the lease. Each save of the row carries the claim in its <c>WHERE</c> clause, so when
    /// another processor took the message over, the save fails and <see cref="LeaseLost"/> takes what it
    /// carried off the context.
    /// </para>
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
            catch (Exception exception) when (TookOver(exception, message))
            {
                // A handler or a sink saved through this context, and the save found the lease gone.
                await RollBackAsync(transaction).ConfigureAwait(false);
                LeaseLost(message);
                return false;
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

                    await SaveMarkAsync(message, cancellationToken).ConfigureAwait(false);
                    return false;
                }
            }

            if (!await SaveMarkAsync(message, cancellationToken).ConfigureAwait(false))
            {
                await RollBackAsync(transaction).ConfigureAwait(false);
                return false;
            }

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
    /// Clears the lease and saves the mark. Returns <see langword="false"/> when the save found the row claimed
    /// by another processor; <see cref="LeaseLost"/> has then taken the save's changes off the context.
    /// </summary>
    private async Task<bool> SaveMarkAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        message.LockedBy = null;
        message.LockedUntil = null;

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (TookOver(exception, message))
        {
            LeaseLost(message);
            return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="exception"/>, or an exception inside it, is a save that found
    /// <paramref name="message"/> claimed by another processor. With the toolkit's interceptors the save
    /// throws a <see cref="Exceptions.ConcurrencyConflictException"/> around Entity Framework's
    /// <see cref="DbUpdateConcurrencyException"/>; without them, the latter alone.
    /// </summary>
    private static bool TookOver(Exception exception, OutboxMessage message)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException concurrency && concurrency.Entries.Any(entry => ReferenceEquals(entry.Entity, message)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Another processor claimed <paramref name="message"/> after this lease ended, and holds it now. The
    /// message is neither delivered nor failed here. The save that found it out wrote nothing, so its changes
    /// come off the context: the row, the outbox rows the save's events added, and what a handler left
    /// unsaved. Otherwise the next message's mark would write them.
    /// </summary>
    private void LeaseLost(OutboxMessage message)
    {
        _logger.LogWarning(
            "Outbox message {MessageId} ({EventName}) was claimed by another processor after this lease ended. Its delivery here is not recorded, and the other processor delivers it.",
            message.Id, message.EventName);

        foreach (var entry in _context.ChangeTracker.Entries().ToList())
        {
            if (ReferenceEquals(entry.Entity, message) || entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private static async Task RollBackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
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
            await DispatchInProcessAsync(message, domainEvent, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Hands the event to the in-process dispatch. An in-process handler is not the processor's bookkeeping:
    /// where the host requires an explicit current user it runs with nobody begun and begins its own user, and whatever it
    /// leaves unsaved on this processor's context is taken off it rather than saved with the mark as the
    /// system, and fails the message.
    /// </summary>
    private async Task DispatchInProcessAsync(OutboxMessage message, IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        if (!_requireExplicitCurrentUser)
        {
            await _options.Dispatcher!(_serviceProvider, [domainEvent], cancellationToken).ConfigureAwait(false);
            return;
        }

        var before = PendingChanges();
        IReadOnlyList<string> left;
        try
        {
            using (ToolkitWork.BeginHandler(required: true))
            {
                await _options.Dispatcher!(_serviceProvider, [domainEvent], cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            left = DropChangesLeftSince(before);
        }

        if (left.Count > 0)
        {
            throw new InvalidOperationException(
                $"An in-process handler of {message.EventName} left changes to {string.Join(", ", left)} on the outbox processor's context without saving them. " +
                "This host requires an explicit current user (RequireExplicitCurrentUser), and the processor saves its context as the system, so they were not saved. " +
                "Save the handler's work itself, under a caller it begins; with DeliverInTransaction, on a context of its own.");
        }
    }

    /// <summary>
    /// The entities other than outbox rows that this processor's context has changes to save for, before a
    /// handler ran; by reference, since an entity's own equality may be its id.
    /// </summary>
    private HashSet<object> PendingChanges()
        => new(
            _context.ChangeTracker.Entries()
                .Where(entry => entry.Entity is not OutboxMessage && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => entry.Entity),
            ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Takes the changes a handler left on this processor's context since <paramref name="before"/> off it, so
    /// the mark's save does not carry them, and names their types.
    /// </summary>
    private List<string> DropChangesLeftSince(HashSet<object> before)
    {
        var left = _context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is not OutboxMessage
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && !before.Contains(entry.Entity))
            .ToList();

        foreach (var entry in left)
        {
            entry.State = EntityState.Detached;
        }

        return [.. left.Select(entry => entry.Metadata.DisplayName()).Distinct(StringComparer.Ordinal)];
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

        // Published as it stands, the event goes out under the name its type is registered under today,
        // which is the row's own name except for a row written before events were named by convention.
        // Published as a contract, the entry says it when it was registered with a name and version, as
        // the generated registration does; a PublishAs lambda leaves it to the contract's attributes.
        var (name, version) = !mapped
            ? outbox.EventTypes.TryDescribe(domainEvent.GetType(), out var eventName, out var eventVersion)
                ? (eventName, eventVersion)
                : (message.EventName, CurrentVersionOf(domainEvent.GetType(), outbox))
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
        if (!outbox.EventTypes.TryResolve(message.EventName, out var eventType, out var byClassName))
        {
            throw new InvalidOperationException(
                $"No domain event type is registered under the name '{message.EventName}'. Register it with outbox.RegisterEventsFromAssembly(...) or outbox.RegisterEvent<T>().");
        }

        if (byClassName)
        {
            return ReadByClassName(message, outbox, contracts, eventType);
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

    /// <summary>
    /// A row stored before events were named by convention, under the bare class name of the type it found.
    /// Exactly that class wrote it, so the payload is that class's shape whatever version the row says: read
    /// it as that class, and let the upcasters take it on to the current shape as they would any older one.
    /// </summary>
    private static IDomainEvent ReadByClassName(OutboxMessage message, OutboxOptions outbox, IntegrationEventContractRegistry contracts, Type storedType)
    {
        var (name, version) = outbox.EventTypes.TryDescribe(storedType, out var registeredName, out var registeredVersion)
            ? (registeredName, registeredVersion)
            : (message.EventName, message.Version);

        var read = contracts.ReadAs(message.Payload, storedType, name, version);

        return read as IDomainEvent
            ?? throw new InvalidOperationException(
                $"Outbox message {message.Id}, stored as '{message.EventName}', upcast from '{storedType}' to '{read.GetType()}', which is not a domain event. The chain has to end at the type registered under '{name}'.");
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
