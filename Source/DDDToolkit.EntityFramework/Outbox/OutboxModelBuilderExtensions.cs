using DDDToolkit.EntityFramework.Storage;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Outbox;

/// <summary>Model configuration for the domain event outbox.</summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>The table name used when none is given.</summary>
    public const string DefaultTableName = DomainEventStorage.DefaultOutboxTableName;

    /// <summary>
    /// Maps <see cref="OutboxMessage"/> to <paramref name="tableName"/> in <paramref name="schema"/>,
    /// with an index on <see cref="OutboxMessage.ProcessedAt"/>. Call it from <c>OnModelCreating</c> of
    /// every context that uses <c>UseOutbox</c>.
    /// <para>
    /// The table lives in the <c>ddd</c> schema by default, away from your domain tables. Pass a name
    /// of your own, or <see langword="null"/> for the provider's default schema. SQLite has no
    /// schemas and ignores the argument, so the table is plain <c>OutboxMessages</c> there.
    /// </para>
    /// <para>
    /// The timestamps are stored as UTC <see cref="DateTime"/> columns rather than provider-specific
    /// offset types so that every provider, SQLite included, can index and order them.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    public static ModelBuilder AddDomainEventOutbox(this ModelBuilder modelBuilder, string tableName = DefaultTableName, string? schema = DomainEventStorage.DefaultSchema)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable(tableName, schema);
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).ValueGeneratedNever();
            builder.Property(m => m.EventName).HasMaxLength(DomainEventStorage.MaxNameLength).IsRequired();
            builder.Property(m => m.Payload).IsRequired();
            builder.Property(m => m.AggregateType).HasMaxLength(DomainEventStorage.MaxAggregateTypeLength);
            builder.Property(m => m.AggregateId).HasMaxLength(DomainEventStorage.MaxAggregateIdLength);
            builder.Property(m => m.LastError).HasMaxLength(DomainEventStorage.MaxErrorLength);
            builder.Property(m => m.OccurredAt).HasConversion(new UtcDateTimeOffsetConverter());
            builder.Property(m => m.CreatedAt).HasConversion(new UtcDateTimeOffsetConverter());
            builder.Property(m => m.ProcessedAt).HasConversion(new NullableUtcDateTimeOffsetConverter());
            builder.HasIndex(m => m.ProcessedAt);
        });

        return modelBuilder;
    }
}
