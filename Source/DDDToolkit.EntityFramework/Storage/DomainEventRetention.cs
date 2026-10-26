using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Storage;

/// <summary>What one run of <see cref="DomainEventRetention{TContext}"/> deleted.</summary>
/// <param name="OutboxMessages">Delivered outbox rows.</param>
/// <param name="InboxMessages">Inbox rows.</param>
public readonly record struct DomainEventRetentionResult(int OutboxMessages, int InboxMessages)
{
    /// <summary>Event log rows. A property rather than a third position, so code written against the first two still compiles.</summary>
    public int EventLogEntries { get; init; }
}

/// <summary>
/// Deletes the outbox, inbox and event log rows of <typeparamref name="TContext"/> that are older than
/// <see cref="DomainEventRetentionOptions{TContext}"/> says to keep them.
/// <para>
/// None of the tables ever shrinks by itself. The outbox marks a row delivered and leaves it, the inbox
/// writes a row for every message every consumer applies, and the event log is never changed at all. That
/// history is worth having for a while and after that it is only weight, in the indexes and in every
/// backup. This is the other end of the tables.
/// </para>
/// <para>
/// Each table goes by a window of its own, so cleaning up the outbox after a week says nothing about a log
/// that is kept for years.
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
///     retention.KeepEventLogFor = TimeSpan.FromDays(365);
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

    /// <summary>
    /// How much older than its table keeps it an event log row has to be before retention deletes it: a minute.
    /// The log's guard on Postgres counts a row's age by the database's clock, and retention by the
    /// application's. Asked about the same instant, a clock a moment ahead here would send the guard a row it
    /// still keeps, and the guard refuses the whole statement.
    /// </summary>
    private static readonly TimeSpan EventLogClockMargin = TimeSpan.FromMinutes(1);

    /// <summary>Creates the retention over the (scoped) <paramref name="context"/>.</summary>
    /// <param name="context">The context whose outbox, inbox and event log tables are cleaned up.</param>
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
    /// <exception cref="InvalidOperationException">
    /// A window is set for a table the context's model does not contain, or the event log's window is one its
    /// table would refuse: the log keeps every row, or keeps a row for longer than the window.
    /// </exception>
    public async Task<DomainEventRetentionResult> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        // Before anything is deleted: a window the log would refuse is a mistake in the settings, and it should
        // not show up as half a run.
        var eventLogBefore = _options.KeepEventLogFor is { } keepEventLog ? EventLogCutoff(now, keepEventLog) : (DateTimeOffset?)null;

        var outbox = _options.KeepOutboxFor is { } keepOutbox
            ? await DeleteDeliveredOutboxMessagesAsync(now - keepOutbox, cancellationToken).ConfigureAwait(false)
            : 0;

        var inbox = _options.KeepInboxFor is { } keepInbox
            ? await DeleteInboxMessagesAsync(now - keepInbox, cancellationToken).ConfigureAwait(false)
            : 0;

        var eventLog = eventLogBefore is { } recordedBefore
            ? await DeleteInBatchesAsync(_context.Set<EventLogEntry>().Where(entry => entry.RecordedAt < recordedBefore), cancellationToken).ConfigureAwait(false)
            : 0;

        return new DomainEventRetentionResult(outbox, inbox) { EventLogEntries = eventLog };
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

    /// <summary>
    /// Deletes the event log rows recorded before <paramref name="recordedBefore"/>. It is refused where the
    /// log's own table would refuse it: a log mapped without <c>keepFor</c> keeps every row, and one mapped with
    /// it keeps a row until it is that old, and a minute more, since the guard counts by the database's clock.
    /// </summary>
    /// <returns>The number of rows deleted.</returns>
    /// <exception cref="InvalidOperationException">The context's model does not contain the event log table, or the log keeps every row.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordedBefore"/> reaches rows the log still keeps.</exception>
    public Task<int> DeleteEventLogEntriesAsync(DateTimeOffset recordedBefore, CancellationToken cancellationToken = default)
    {
        var keepFor = EventLogKeepFor();
        var oldest = Before(Before(_timeProvider.GetUtcNow(), keepFor), EventLogClockMargin);
        if (recordedBefore > oldest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recordedBefore),
                recordedBefore,
                $"The event log of '{_context.GetType().Name}' keeps a row for {keepFor}, so only rows recorded before {oldest:O} may be deleted: its table refuses the rest.");
        }

        return DeleteInBatchesAsync(_context.Set<EventLogEntry>().Where(entry => entry.RecordedAt < recordedBefore), cancellationToken);
    }

    /// <summary>
    /// The instant before which event log rows go, for a window of <paramref name="keepEventLogFor"/>: that
    /// long ago, and never closer to the line the log's own table draws than <see cref="EventLogClockMargin"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The window is shorter than the log keeps a row, or the log keeps every row.</exception>
    private DateTimeOffset EventLogCutoff(DateTimeOffset now, TimeSpan keepEventLogFor)
    {
        var keepFor = EventLogKeepFor();
        if (keepEventLogFor < keepFor)
        {
            throw new InvalidOperationException(
                $"{nameof(DomainEventRetentionOptions<TContext>.KeepEventLogFor)} is {keepEventLogFor}, but the event log of '{_context.GetType().Name}' keeps a row for {keepFor}, " +
                $"and its table refuses to delete a younger one. Set {nameof(DomainEventRetentionOptions<TContext>.KeepEventLogFor)} to {keepFor} or longer, or give {nameof(EventLogModelBuilderExtensions.AddEventLog)} a shorter keepFor.");
        }

        var asked = Before(now, keepEventLogFor);
        var allowed = Before(Before(now, keepFor), EventLogClockMargin);
        return asked < allowed ? asked : allowed;
    }

    /// <summary>
    /// <paramref name="age"/> before <paramref name="now"/>, or the earliest instant there is when the calendar
    /// does not go back that far: a window of ten thousand years lets nothing go, and says so without an
    /// overflow.
    /// </summary>
    private static DateTimeOffset Before(DateTimeOffset now, TimeSpan age)
        => now - DateTimeOffset.MinValue > age ? now - age : DateTimeOffset.MinValue;

    /// <summary>How long the model's event log keeps a row before it may be deleted.</summary>
    /// <exception cref="InvalidOperationException">The model has no event log, or one that keeps every row.</exception>
    private TimeSpan EventLogKeepFor()
    {
        EnsureMapped<EventLogEntry>("event log", nameof(EventLogModelBuilderExtensions.AddEventLog), nameof(DomainEventRetentionOptions<TContext>.KeepEventLogFor));

        return KeptRows.KeepForSecondsOf(_context.Model.FindEntityType(typeof(EventLogEntry))!) is { } seconds
            ? TimeSpan.FromSeconds(seconds)
            : throw new InvalidOperationException(
                $"The event log of '{_context.GetType().Name}' keeps every row: {nameof(EventLogModelBuilderExtensions.AddEventLog)} was given no keepFor, so nothing may delete one. " +
                $"Give it a keepFor to let old rows go, or leave {nameof(DomainEventRetentionOptions<TContext>.KeepEventLogFor)} unset.");
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
