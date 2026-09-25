using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Storage;

/// <summary>
/// How long the outbox and the inbox of <typeparamref name="TContext"/> keep their rows, for
/// <see cref="DomainEventRetention{TContext}"/>. Set with
/// <c>services.AddDomainEventRetention&lt;TContext&gt;(retention =&gt; ...)</c>.
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
        if (KeepOutboxFor is null && KeepInboxFor is null)
        {
            throw new ArgumentException(
                $"Set {nameof(KeepOutboxFor)}, {nameof(KeepInboxFor)} or both. With neither, retention has nothing to delete.",
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

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Interval, TimeSpan.Zero, nameof(Interval));
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchSize, 1, nameof(BatchSize));
    }
}
