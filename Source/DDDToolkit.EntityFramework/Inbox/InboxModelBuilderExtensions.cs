using DDDToolkit.EntityFramework.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DDDToolkit.EntityFramework.Inbox;

/// <summary>Model configuration for the consumer side inbox.</summary>
public static class InboxModelBuilderExtensions
{
    /// <summary>The table name used when none is given.</summary>
    public const string DefaultTableName = DomainEventStorage.DefaultInboxTableName;

    /// <summary>
    /// Maps <see cref="InboxMessage"/> to <paramref name="tableName"/> in <paramref name="schema"/>,
    /// keyed on (<see cref="InboxMessage.MessageId"/>, <see cref="InboxMessage.Consumer"/>). Call it
    /// from <c>OnModelCreating</c> of the context <c>DomainEventInbox&lt;TContext&gt;</c> runs on,
    /// passing the context's own <c>Database</c>:
    /// <code>
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     modelBuilder.AddDomainEventInbox(Database);
    /// }
    /// </code>
    /// <para>
    /// The composite primary key is the mechanism, not decoration: it is what makes a second attempt to
    /// mark the same message for the same consumer fail rather than write a duplicate row.
    /// </para>
    /// <para>
    /// Like the outbox, the table lives in the <c>ddd</c> schema by default. Pass <see langword="null"/>
    /// for the provider's default schema; SQLite ignores schemas either way.
    /// </para>
    /// <para>
    /// <paramref name="database"/> is read for its provider name, which is what decides the
    /// <c>ProcessedAt</c> column. See <see cref="DomainEventTimestamps"/>; pass the same value you
    /// passed to <c>AddDomainEventOutbox</c>, so the two tables agree.
    /// </para>
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="database">The context's <c>Database</c>, read for its provider name only.</param>
    /// <param name="tableName">The table to map to.</param>
    /// <param name="schema">The schema to put it in, or <see langword="null"/> for the provider's default.</param>
    /// <param name="timestamps">What the <c>ProcessedAt</c> column becomes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> or <paramref name="database"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    public static ModelBuilder AddDomainEventInbox(
        this ModelBuilder modelBuilder,
        DatabaseFacade database,
        string tableName = DefaultTableName,
        string? schema = DomainEventStorage.DefaultSchema,
        DomainEventTimestamps timestamps = DomainEventTimestamps.ProviderDefault)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var utcDateTime = DomainEventTimestampMapping.StoresUtcDateTime(database.ProviderName, timestamps);

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable(tableName, schema);
            builder.HasKey(m => new { m.MessageId, m.Consumer });
            builder.Property(m => m.Consumer).HasMaxLength(DomainEventStorage.MaxConsumerLength).IsRequired();
            builder.Property(m => m.MessageName).HasMaxLength(DomainEventStorage.MaxNameLength);
            builder.Property(m => m.ProcessedAt).AsTimestamp(utcDateTime);
            builder.HasIndex(m => m.ProcessedAt);
        });

        return modelBuilder;
    }
}
