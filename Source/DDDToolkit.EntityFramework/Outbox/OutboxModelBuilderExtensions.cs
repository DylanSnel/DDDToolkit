using DDDToolkit.EntityFramework.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DDDToolkit.EntityFramework.Outbox;

/// <summary>Model configuration for the domain event outbox.</summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>The table name used when none is given.</summary>
    public const string DefaultTableName = DomainEventStorage.DefaultOutboxTableName;

    /// <summary>
    /// Maps <see cref="OutboxMessage"/> to <paramref name="tableName"/> in <paramref name="schema"/>,
    /// with an index on <see cref="OutboxMessage.ProcessedAt"/>. Call it from <c>OnModelCreating</c> of
    /// every context that uses <c>UseOutbox</c>, passing the context's own <c>Database</c>:
    /// <code>
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     modelBuilder.AddDomainEventOutbox(Database);
    /// }
    /// </code>
    /// <para>
    /// The table lives in the <c>ddd</c> schema by default, away from your domain tables. Pass a name
    /// of your own, or <see langword="null"/> for the provider's default schema. SQLite has no
    /// schemas and ignores the argument, so the table is plain <c>OutboxMessages</c> there.
    /// </para>
    /// <para>
    /// <paramref name="database"/> is there for one reason: the timestamp columns. Entity Framework
    /// builds one model per provider, so the provider is known here, and with it the toolkit can give
    /// each provider the instant type it actually has instead of the one SQLite forces. See
    /// <see cref="DomainEventTimestamps"/> for what that means per provider, and pass
    /// <see cref="DomainEventTimestamps.UtcDateTime"/> to keep the columns of a database written by an
    /// earlier build.
    /// </para>
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="database">The context's <c>Database</c>, read for its provider name only.</param>
    /// <param name="tableName">The table to map to.</param>
    /// <param name="schema">The schema to put it in, or <see langword="null"/> for the provider's default.</param>
    /// <param name="timestamps">What the timestamp columns become.</param>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> or <paramref name="database"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    public static ModelBuilder AddDomainEventOutbox(
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

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable(tableName, schema);
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).ValueGeneratedNever();
            builder.Property(m => m.EventName).HasMaxLength(DomainEventStorage.MaxNameLength).IsRequired();
            builder.Property(m => m.Payload).IsRequired();
            builder.Property(m => m.Version);
            builder.Property(m => m.AggregateType).HasMaxLength(DomainEventStorage.MaxAggregateTypeLength);
            builder.Property(m => m.AggregateId).HasMaxLength(DomainEventStorage.MaxAggregateIdLength);
            builder.Property(m => m.LastError).HasMaxLength(DomainEventStorage.MaxErrorLength);
            builder.Property(m => m.OccurredAt).AsTimestamp(utcDateTime);
            builder.Property(m => m.CreatedAt).AsTimestamp(utcDateTime);
            builder.Property(m => m.ProcessedAt).AsTimestamp(utcDateTime);
            builder.HasIndex(m => m.ProcessedAt);
        });

        return modelBuilder;
    }
}
