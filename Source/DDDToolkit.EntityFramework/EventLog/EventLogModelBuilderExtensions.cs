using DDDToolkit.EntityFramework.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDDToolkit.EntityFramework.EventLog;

/// <summary>Model configuration for the event log.</summary>
public static class EventLogModelBuilderExtensions
{
    /// <summary>The table name used when none is given.</summary>
    public const string DefaultTableName = DomainEventStorage.DefaultEventLogTableName;

    /// <summary>
    /// Maps <see cref="EventLogEntry"/> to <paramref name="tableName"/> in <paramref name="schema"/>, with an
    /// index on <see cref="EventLogEntry.RecordedAt"/>, and marks the table as one that only grows. Call it from
    /// <c>OnModelCreating</c> of a context whose outbox keeps events with <c>outbox.KeepEventLog()</c>, passing
    /// the context's own <c>Database</c>:
    /// <code>
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     modelBuilder.AddDomainEventOutbox(Database);
    ///     modelBuilder.AddEventLog(Database, keepFor: TimeSpan.FromDays(365));
    /// }
    /// </code>
    /// <para>
    /// The mark is what <c>DDDToolkit.EntityFramework.Postgres</c> writes the table's guard from, in every
    /// script of policies and every Supabase access file of the context: triggers that refuse every update of a
    /// row, a truncate, and every delete of a row younger than <paramref name="keepFor"/>, for every role, the
    /// table's owner included. The model only carries the mark, so it needs no migration of its own. On a
    /// provider without that package, SQLite for one, nothing guards the table: the toolkit itself never
    /// updates a row, and deletes one only through retention's own window.
    /// </para>
    /// <para>
    /// Like the outbox, the table lives in the <c>ddd</c> schema by default; pass <see langword="null"/> for the
    /// provider's default schema. SQLite ignores schemas either way. The timestamps are the provider's own
    /// instant type, and a UTC <see cref="DateTime"/> on SQLite, which cannot order its offset type.
    /// </para>
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="database">The context's <c>Database</c>, read for its provider name only.</param>
    /// <param name="tableName">The table to map to.</param>
    /// <param name="schema">The schema to put it in, or <see langword="null"/> for the provider's default.</param>
    /// <param name="keepFor">
    /// How long a row is kept before it may be deleted, counted from <see cref="EventLogEntry.RecordedAt"/> and
    /// rounded up to whole seconds; at most a thousand years. <see langword="null"/>, the default, keeps every
    /// row for as long as the table exists: nothing may delete one, retention included.
    /// </param>
    /// <param name="configure">
    /// Further configuration of the table, such as columns of the module's own, a tenant for one, which an
    /// <see cref="IEventLogFields"/> fills. Add them as shadow properties:
    /// <c>log =&gt; log.Property&lt;Guid?&gt;("TenantId")</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> or <paramref name="database"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="keepFor"/> is zero, negative or longer than a thousand years.</exception>
    public static ModelBuilder AddEventLog(
        this ModelBuilder modelBuilder,
        DatabaseFacade database,
        string tableName = DefaultTableName,
        string? schema = DomainEventStorage.DefaultSchema,
        TimeSpan? keepFor = null,
        Action<EntityTypeBuilder<EventLogEntry>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        var keepForSeconds = KeptRows.SecondsOf(keepFor, nameof(keepFor));

        var utcDateTime = DomainEventTimestampMapping.StoresUtcDateTime(database.ProviderName, DomainEventTimestamps.ProviderDefault);

        modelBuilder.Entity<EventLogEntry>(builder =>
        {
            builder.ToTable(tableName, schema);
            builder.HasKey(entry => entry.Id);
            builder.Property(entry => entry.Id).ValueGeneratedNever();
            builder.Property(entry => entry.EventName).HasMaxLength(DomainEventStorage.MaxNameLength).IsRequired();
            builder.Property(entry => entry.Version);
            builder.Property(entry => entry.Payload).IsRequired();
            builder.Property(entry => entry.OccurredAt).AsTimestamp(utcDateTime);
            builder.Property(entry => entry.RecordedAt).AsTimestamp(utcDateTime);
            builder.Property(entry => entry.AggregateType).HasMaxLength(DomainEventStorage.MaxAggregateTypeLength);
            builder.Property(entry => entry.AggregateId).HasMaxLength(DomainEventStorage.MaxAggregateIdLength);
            builder.Property(entry => entry.ActedByKind).HasMaxLength(DomainEventStorage.MaxActedByKindLength).IsRequired();
            builder.Property(entry => entry.ActedById).HasMaxLength(DomainEventStorage.MaxActedByIdLength);

            // What a reader pages by and retention deletes by: the newest first, the oldest gone.
            builder.HasIndex(entry => entry.RecordedAt);

            // The mark the guard and the privileges are written from. Plain values, so a migration snapshot can
            // write them down. A log kept for good carries no period at all.
            builder.HasAnnotation(KeptRows.AppendOnlyAnnotation, true);
            builder.HasAnnotation(KeptRows.RecordedAtAnnotation, nameof(EventLogEntry.RecordedAt));
            if (keepForSeconds is { } seconds)
            {
                builder.HasAnnotation(KeptRows.KeepForSecondsAnnotation, seconds);
            }
            else
            {
                builder.Metadata.RemoveAnnotation(KeptRows.KeepForSecondsAnnotation);
            }

            configure?.Invoke(builder);
        });

        return modelBuilder;
    }
}
