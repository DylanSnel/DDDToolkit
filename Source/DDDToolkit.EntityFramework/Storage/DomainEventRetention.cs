using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Storage;

/// <summary>What one run of <see cref="DomainEventRetention{TContext}"/> deleted.</summary>
/// <param name="OutboxMessages">Delivered outbox rows.</param>
/// <param name="InboxMessages">Inbox rows.</param>
public readonly record struct DomainEventRetentionResult(int OutboxMessages, int InboxMessages);

/// <summary>
/// Deletes the outbox and inbox rows of <typeparamref name="TContext"/> that are older than
/// <see cref="DomainEventRetentionOptions{TContext}"/> says to keep them.
/// <para>
/// Neither table ever shrinks by itself. The outbox marks a row delivered and leaves it, and the inbox
/// writes a row for every message every consumer applies. That history is worth having for a while and
/// after that it is only weight, in the <c>ProcessedAt</c> index and in every backup. This is the other
/// end of both tables.
/// </para>
/// <para>
/// The rows are deleted by <c>ExecuteDelete</c>, in the database and past the change tracker,
/// <see cref="DomainEventRetentionOptions{TContext}.BatchSize"/> at a time. Nothing is loaded.
/// </para>
/// <code>
/// services.AddDomainEventRetention&lt;OrderingContext&gt;(retention =&gt;
/// {
///     retention.KeepOutboxFor = TimeSpan.FromDays(7);
///     retention.KeepInboxFor = TimeSpan.FromDays(30);
/// });
/// </code>
/// That registers this class and <see cref="DomainEventRetentionService{TContext}"/>, which runs it on a
/// timer. To run it from a scheduler of your own instead, construct it over a context and call it.
/// </summary>
public sealed class DomainEventRetention<TContext> where TContext : DbContext
{
    private readonly TContext _context;
    private readonly DomainEventRetentionOptions<TContext> _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the retention over the (scoped) <paramref name="context"/>.</summary>
    /// <param name="context">The context whose outbox and inbox tables are cleaned up.</param>
    /// <param name="options">The windows and the batch size; defaults keep everything.</param>
    /// <param name="toolkit">Supplies the clock; defaults to <see cref="TimeProvider.System"/> when absent.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The batch size is below 1.</exception>
    public DomainEventRetention(TContext context, DomainEventRetentionOptions<TContext>? options = null, DDDEntityFrameworkOptions? toolkit = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? new DomainEventRetentionOptions<TContext>();
        _timeProvider = toolkit?.TimeProvider ?? TimeProvider.System;

        ArgumentOutOfRangeException.ThrowIfLessThan(_options.BatchSize, 1, nameof(options));
    }

    /// <summary>
    /// Deletes every row older than its table's window, measured from now. A table without a window is
    /// not touched.
    /// </summary>
    /// <exception cref="InvalidOperationException">A window is set for a table the context's model does not contain.</exception>
    public async Task<DomainEventRetentionResult> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        var outbox = _options.KeepOutboxFor is { } keepOutbox
            ? await DeleteDeliveredOutboxMessagesAsync(now - keepOutbox, cancellationToken).ConfigureAwait(false)
            : 0;

        var inbox = _options.KeepInboxFor is { } keepInbox
            ? await DeleteInboxMessagesAsync(now - keepInbox, cancellationToken).ConfigureAwait(false)
            : 0;

        return new DomainEventRetentionResult(outbox, inbox);
    }

    /// <summary>
    /// Deletes the outbox rows delivered before <paramref name="deliveredBefore"/>. A row that was never
    /// delivered is never deleted, however old it is.
    /// </summary>
    /// <returns>The number of rows deleted.</returns>
    /// <exception cref="InvalidOperationException">The context's model does not contain the outbox table.</exception>
    public Task<int> DeleteDeliveredOutboxMessagesAsync(DateTimeOffset deliveredBefore, CancellationToken cancellationToken = default)
    {
        EnsureMapped<OutboxMessage>("outbox", nameof(OutboxModelBuilderExtensions.AddDomainEventOutbox), nameof(DomainEventRetentionOptions<TContext>.KeepOutboxFor));

        return DeleteInBatchesAsync(
            _context.Set<OutboxMessage>().Where(message => message.ProcessedAt < deliveredBefore),
            cancellationToken);
    }

    /// <summary>
    /// Deletes the inbox rows written before <paramref name="processedBefore"/>. A message whose row is
    /// deleted is applied again if it is ever delivered again.
    /// </summary>
    /// <returns>The number of rows deleted.</returns>
    /// <exception cref="InvalidOperationException">The context's model does not contain the inbox table.</exception>
    public Task<int> DeleteInboxMessagesAsync(DateTimeOffset processedBefore, CancellationToken cancellationToken = default)
    {
        EnsureMapped<InboxMessage>("inbox", nameof(InboxModelBuilderExtensions.AddDomainEventInbox), nameof(DomainEventRetentionOptions<TContext>.KeepInboxFor));

        return DeleteInBatchesAsync(
            _context.Set<InboxMessage>().Where(message => message.ProcessedAt < processedBefore),
            cancellationToken);
    }

    /// <summary>Deletes a batch at a time until a batch comes back short, which means nothing is left.</summary>
    private async Task<int> DeleteInBatchesAsync<TRow>(IQueryable<TRow> expired, CancellationToken cancellationToken)
    {
        var batchSize = _options.BatchSize;
        var total = 0;
        int deleted;

        do
        {
            deleted = await expired.Take(batchSize).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            total += deleted;
        }
        while (deleted == batchSize);

        return total;
    }

    private void EnsureMapped<TRow>(string table, string mapping, string window)
    {
        if (_context.Model.FindEntityType(typeof(TRow)) is null)
        {
            throw new InvalidOperationException(
                $"The model of '{_context.GetType().Name}' does not contain the {table} table. " +
                $"Call modelBuilder.{mapping}() in OnModelCreating, or leave {window} unset.");
        }
    }
}
