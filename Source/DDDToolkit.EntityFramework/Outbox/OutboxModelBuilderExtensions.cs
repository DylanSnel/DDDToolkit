using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDDToolkit.EntityFramework.Outbox;

/// <summary>Model configuration for the domain event outbox.</summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>The table name used when none is given.</summary>
    public const string DefaultTableName = "OutboxMessages";

    /// <summary>
    /// Maps <see cref="OutboxMessage"/> to <paramref name="tableName"/> with an index on
    /// <see cref="OutboxMessage.ProcessedAt"/>. Call from <c>OnModelCreating</c> of every context that
    /// uses <c>UseOutbox</c>.
    /// <para>
    /// The timestamps are stored as UTC <see cref="DateTime"/> columns rather than provider-specific
    /// offset types so that every provider, SQLite included, can index and order them.
    /// </para>
    /// </summary>
    public static ModelBuilder AddDomainEventOutbox(this ModelBuilder modelBuilder, string tableName = DefaultTableName, string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable(tableName, schema);
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).ValueGeneratedNever();
            builder.Property(m => m.EventName).HasMaxLength(256).IsRequired();
            builder.Property(m => m.Payload).IsRequired();
            builder.Property(m => m.AggregateType).HasMaxLength(512);
            builder.Property(m => m.AggregateId).HasMaxLength(256);
            builder.Property(m => m.LastError).HasMaxLength(OutboxProcessorDefaults.MaxErrorLength);
            builder.Property(m => m.OccurredAt).HasConversion(new UtcDateTimeOffsetConverter());
            builder.Property(m => m.CreatedAt).HasConversion(new UtcDateTimeOffsetConverter());
            builder.Property(m => m.ProcessedAt).HasConversion(new NullableUtcDateTimeOffsetConverter());
            builder.HasIndex(m => m.ProcessedAt);
        });

        return modelBuilder;
    }

    /// <summary>Stores a <see cref="DateTimeOffset"/> as its UTC instant.</summary>
    private sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTime>(
        static value => value.UtcDateTime,
        static value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero));

    private sealed class NullableUtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset?, DateTime?>(
        static value => value.HasValue ? value.Value.UtcDateTime : null,
        static value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc), TimeSpan.Zero) : null);
}

/// <summary>Limits shared by the outbox writer and processor.</summary>
internal static class OutboxProcessorDefaults
{
    public const int MaxErrorLength = 4000;
}
