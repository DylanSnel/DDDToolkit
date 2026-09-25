using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.EntityFramework.Inbox;

/// <summary>
/// Runs a handler once for a given message and consumer, whatever the transport does.
/// <para>
/// Every durable delivery mechanism, this toolkit's outbox included, is at-least-once: it would rather
/// send a message twice than lose it. That is only safe if the receiving side can recognise a message
/// it has already applied. This is that side. The handler's database work and the row that says
/// "applied" are written by one <c>SaveChanges</c> inside one transaction, so there is no instant at
/// which one exists without the other. A crash anywhere in between rolls both back and the next
/// delivery applies the message cleanly, exactly once.
/// </para>
/// <code>
/// public async Task Consume(IntegrationEventMessage message, CancellationToken cancellationToken)
/// {
///     var applied = await inbox.ExecuteOnceAsync(message, "billing.order-projector", async (msg, ct) =>
///     {
///         var order = JsonSerializer.Deserialize&lt;OrderPlacedV1&gt;(msg.Payload)!;
///         context.Invoices.Add(new Invoice(order.OrderId, order.Total));
///     },
///     cancellationToken);
///
///     // applied is false when this consumer had already seen the message. Acknowledge it either way.
/// }
/// </code>
/// <para>
/// Two copies of one message delivered at the same moment both find no row and both run. The first to
/// commit wins, and the other's save fails on the row, or on whatever the winner changed, and is rolled
/// back. When the inbox owns the transaction it then looks again, finds the winner's row and returns
/// <see langword="false"/>, the same as for any other repeat. Inside a caller's transaction it throws
/// instead, because only the caller can roll that back.
/// </para>
/// <para>
/// A failed attempt is rolled back in the change tracker as well as in the database: whatever the
/// attempt started tracking is detached, so the next save on the same context does not write it after
/// all. Entities the context tracked before the attempt are left as they are.
/// </para>
/// <para>
/// What it cannot do: undo effects outside the database. If your handler sends a mail and the
/// transaction then rolls back, the mail is gone but sent. Do outside work by writing a row the
/// transaction owns, and let something else act on that row.
/// </para>
/// Register with <c>services.AddDomainEventInbox&lt;TContext&gt;()</c> and map the table with
/// <c>modelBuilder.AddDomainEventInbox()</c>.
/// </summary>
public sealed class DomainEventInbox<TContext> where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly IntegrationEventContractRegistry _contracts;
    private readonly ILogger _logger;

    /// <summary>Creates an inbox over the (scoped) <paramref name="context"/>.</summary>
    /// <param name="context">The context the handler writes through. Its model must contain the inbox table.</param>
    /// <param name="options">Supplies the clock; defaults to <see cref="TimeProvider.System"/> when absent.</param>
    /// <param name="logger">Optional.</param>
    public DomainEventInbox(TContext context, DDDEntityFrameworkOptions? options = null, ILogger<DomainEventInbox<TContext>>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = options?.TimeProvider ?? TimeProvider.System;
        _contracts = options?.Contracts ?? new IntegrationEventContractRegistry();
        _logger = logger ?? NullLogger<DomainEventInbox<TContext>>.Instance;
    }

    /// <summary>
    /// The payload shapes this side can read, and the upcasters between them. The typed
    /// <see cref="ExecuteOnceAsync{TContract}"/> reads through it.
    /// </summary>
    public IntegrationEventContractRegistry Contracts => _contracts;

    /// <summary>Whether <paramref name="consumer"/> has already applied <paramref name="messageId"/>.</summary>
    public Task<bool> HasProcessedAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        EnsureMapped();

        return _context.Set<InboxMessage>()
            .AsNoTracking()
            .AnyAsync(row => row.MessageId == messageId && row.Consumer == consumer, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="handler"/> for <paramref name="message"/> unless <paramref name="consumer"/>
    /// already applied it, and records that it did. The published name is kept on the row for
    /// diagnostics.
    /// </summary>
    /// <returns><see langword="true"/> when the handler ran, <see langword="false"/> when the message was a repeat.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="handler"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="consumer"/> is empty or white space.</exception>
    /// <exception cref="InvalidOperationException">The context's model does not contain the inbox table.</exception>
    public Task<bool> ExecuteOnceAsync(IntegrationEventMessage message, string consumer, Func<IntegrationEventMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(handler);

        return ExecuteOnceAsync(message.MessageId, consumer, token => handler(message, token), message.Name, cancellationToken);
    }

    /// <summary>
    /// Reads <paramref name="message"/> as <typeparamref name="TContract"/> and runs
    /// <paramref name="handler"/> for it unless <paramref name="consumer"/> already applied it.
    /// <para>
    /// The payload is resolved from the message's name <em>and version</em> and then upcast, so a
    /// message written before a deployment arrives as the shape this code was written against. That is
    /// the point of applying upcasters on read: the sender cannot rewrite what it already sent, so the
    /// reader has to be able to catch up. Register the shapes with
    /// <c>options.MapIntegrationEvents(...)</c>.
    /// </para>
    /// <code>
    /// await inbox.ExecuteOnceAsync&lt;OrderPlacedV2&gt;(message, "billing.invoicer", (order, received, token) =>
    /// {
    ///     context.Invoices.Add(new Invoice(order.OrderId, order.Total));
    ///     return Task.CompletedTask;
    /// });
    /// </code>
    /// </summary>
    /// <returns><see langword="true"/> when the handler ran, <see langword="false"/> when the message was a repeat.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="handler"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="consumer"/> is empty or white space.</exception>
    /// <exception cref="InvalidOperationException">Nothing is registered under the message's name and version, or the upcasters do not end at <typeparamref name="TContract"/>.</exception>
    public Task<bool> ExecuteOnceAsync<TContract>(IntegrationEventMessage message, string consumer, Func<TContract, IntegrationEventMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(handler);

        // Read before the "already applied" check so a payload this side cannot read is an error rather
        // than a message quietly marked as done.
        var contract = _contracts.Read<TContract>(message);

        return ExecuteOnceAsync(message.MessageId, consumer, token => handler(contract, message, token), message.Name, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="handler"/> for <paramref name="messageId"/> unless
    /// <paramref name="consumer"/> already applied it, and records that it did.
    /// </summary>
    /// <returns><see langword="true"/> when the handler ran, <see langword="false"/> when the message was a repeat.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="consumer"/> is empty or white space.</exception>
    /// <exception cref="InvalidOperationException">The context's model does not contain the inbox table.</exception>
    public Task<bool> ExecuteOnceAsync(Guid messageId, string consumer, Func<CancellationToken, Task> handler, CancellationToken cancellationToken = default)
        => ExecuteOnceAsync(messageId, consumer, handler, messageName: null, cancellationToken);

    private async Task<bool> ExecuteOnceAsync(Guid messageId, string consumer, Func<CancellationToken, Task> handler, string? messageName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        EnsureMapped();

        if (await HasProcessedAsync(messageId, consumer, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug("Consumer {Consumer} already applied message {MessageId}; skipping.", consumer, messageId);
            return false;
        }

        // Join the caller's transaction when there is one, so the inbox row commits with whatever else
        // they are doing; otherwise own one, because the marker and the effect must not be separable.
        var transaction = _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var row = new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumer,
            MessageName = messageName,
            ProcessedAt = _timeProvider.GetUtcNow(),
        };

        // Whatever the context tracks beyond this once the handler ran is the attempt's own.
        var trackedBefore = TrackedEntities();

        // Added before the handler runs, so a handler that saves for itself carries the marker along.
        _context.Add(row);

        try
        {
            await handler(cancellationToken).ConfigureAwait(false);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            // The database is back where it was; the change tracker is not. Left there, the attempt's
            // writes would go out with the next save on this context, the next consumer's, without the
            // marker, and the retry would apply them a second time.
            ForgetAttempt(trackedBefore);

            // Whether another copy got there first is only known once the transaction is rolled back, and
            // a caller's transaction is theirs to roll back.
            if (transaction is null || cancellationToken.IsCancellationRequested
                || !await AppliedMeanwhileAsync(messageId, consumer, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            _logger.LogInformation(
                "Another delivery of message {MessageId} was applied by consumer {Consumer} while this one ran; this one ({Failure}) was rolled back as a repeat.",
                messageId,
                consumer,
                exception.GetType().Name);
            return false;
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
    /// Whether the inbox holds this message for this consumer after all: another delivery of the same
    /// message committed while this attempt ran, which is why this one failed. No when it cannot tell,
    /// so the attempt's own failure is what gets reported.
    /// </summary>
    private async Task<bool> AppliedMeanwhileAsync(Guid messageId, string consumer, CancellationToken cancellationToken)
    {
        try
        {
            return await HasProcessedAsync(messageId, consumer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogDebug(exception, "Could not tell whether message {MessageId} was applied by another delivery.", messageId);
            return false;
        }
    }

    private HashSet<object> TrackedEntities()
    {
        var tracked = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var entry in Entries())
        {
            tracked.Add(entry.Entity);
        }

        return tracked;
    }

    /// <summary>
    /// Stops tracking everything this attempt started tracking, the marker included. What was tracked
    /// before it is the caller's and stays, so a change the handler made to one of those entities is not
    /// undone: a context the attempt shares with earlier work is only as clean as that work left it.
    /// </summary>
    private void ForgetAttempt(HashSet<object> trackedBefore)
    {
        foreach (var entry in Entries())
        {
            if (!trackedBefore.Contains(entry.Entity))
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    /// <summary>The tracked entries as they stand, without the change detection a plain read would run first.</summary>
    private List<EntityEntry> Entries()
    {
        var tracker = _context.ChangeTracker;
        var detect = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;

        try
        {
            return [.. tracker.Entries()];
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detect;
        }
    }

    private void EnsureMapped()
    {
        if (_context.Model.FindEntityType(typeof(InboxMessage)) is null)
        {
            throw new InvalidOperationException(
                $"The model of '{_context.GetType().Name}' does not contain the inbox table. " +
                $"Call modelBuilder.{nameof(InboxModelBuilderExtensions.AddDomainEventInbox)}() in OnModelCreating.");
        }
    }
}
