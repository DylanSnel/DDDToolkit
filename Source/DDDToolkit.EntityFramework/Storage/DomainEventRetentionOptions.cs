using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Storage;

/// <summary>
/// How long the outbox, the inbox and the event log of <typeparamref name="TContext"/> keep their rows, for
/// <see cref="DomainEventRetention{TContext}"/>. Set with
/// <c>services.AddDomainEventRetention&lt;TContext&gt;(retention =&gt; ...)</c>. Each table has a window of
/// its own, and a table without one is not touched.
/// </summary>
public sealed class DomainEventRetentionOptions<TContext> where TContext : DbContext
{
    /// <summary>
    /// How long a delivered outbox row is kept, counted from its <c>ProcessedAt</c>. Null, the
    /// default, keeps them all. Only delivered rows are ever deleted: a row still waiting, or one that
    /// ran out of attempts, stays until somebody deals with it.
    /// </summary>
    public TimeSpan? KeepOutboxFor { get; set; }

    /// <summary>
    /// How long an inbox row is kept, counted from its <c>ProcessedAt</c>. Null, the default, keeps them
    /// all.
    /// <para>
    /// The row is what makes a repeat a repeat. Once it is deleted, the same message delivered again is
    /// applied again. Keep it longer than any message can take to come back: the broker's own
    /// retention, the outbox's retries, and an operator resetting <c>Attempts</c> on a row that failed
    /// a week ago.
    /// </para>
    /// </summary>
    public TimeSpan? KeepInboxFor { get; set; }

    /// <summary>
    /// How long an event log row is kept, counted from its <c>RecordedAt</c>. Null, the default, keeps them
    /// all: cleaning up the outbox never touches the log.
    /// <para>
    /// The log's own table has the last word. A log mapped without <c>keepFor</c> keeps every row for good, and
    /// one mapped with it refuses the delete of a younger row on Postgres, so this has to be at least the
    /// <c>keepFor</c> that <c>AddEventLog</c> was given; a run that finds otherwise fails before it deletes
    /// anything, and names both. The guard reads the database's clock and retention the application's, so a row
    /// is never deleted until it is a minute older than <c>keepFor</c>, whatever this says: keep the two clocks
    /// within that minute of each other.
    /// </para>
    /// <para>
    /// The rows are deleted as the toolkit's own bookkeeping, like the outbox's: as the system caller, which on
    /// Postgres under a login role that holds nothing is the bookkeeping role.
    /// </para>
    /// </summary>
    public TimeSpan? KeepEventLogFor { get; set; }

    /// <summary>How often <see cref="DomainEventRetentionService{TContext}"/> deletes. Defaults to an hour.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How many rows one delete statement takes. Defaults to 1000. A backlog goes in as many statements
    /// as it takes, each its own short transaction, so the first run over a year of rows does not lock
    /// all of them at once.
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Throws when these settings could not delete anything, or could not be run.</summary>
    internal void Validate(string paramName)
    {
        if (KeepOutboxFor is null && KeepInboxFor is null && KeepEventLogFor is null)
        {
            throw new ArgumentException(
                $"Set {nameof(KeepOutboxFor)}, {nameof(KeepInboxFor)} or {nameof(KeepEventLogFor)}. With none, retention has nothing to delete.",
                paramName);
        }

        if (KeepOutboxFor is { } outbox)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(outbox, TimeSpan.Zero, nameof(KeepOutboxFor));
        }

        if (KeepInboxFor is { } inbox)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(inbox, TimeSpan.Zero, nameof(KeepInboxFor));
        }

        if (KeepEventLogFor is { } eventLog)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(eventLog, TimeSpan.Zero, nameof(KeepEventLogFor));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Interval, TimeSpan.Zero, nameof(Interval));
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchSize, 1, nameof(BatchSize));
    }
}
