using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Options;
using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger _logger;

    /// <summary>Creates an inbox over the (scoped) <paramref name="context"/>.</summary>
    /// <param name="context">The context the handler writes through. Its model must contain the inbox table.</param>
    /// <param name="options">Supplies the clock; defaults to <see cref="TimeProvider.System"/> when absent.</param>
    /// <param name="logger">Optional.</param>
    public DomainEventInbox(TContext context, DDDEntityFrameworkOptions? options = null, ILogger<DomainEventInbox<TContext>>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _timeProvider = options?.TimeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<DomainEventInbox<TContext>>.Instance;
    }

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

        // Added before the handler runs, so a handler that saves for itself carries the marker along.
        var entry = _context.Add(row);

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
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            // The database is back where it was; the change tracker is not, so drop the marker at least.
            entry.State = EntityState.Detached;
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
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
