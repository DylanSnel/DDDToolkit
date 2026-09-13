using DDDToolkit.EntityFramework.Storage;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Inbox;

/// <summary>Model configuration for the consumer side inbox.</summary>
public static class InboxModelBuilderExtensions
{
    /// <summary>The table name used when none is given.</summary>
    public const string DefaultTableName = DomainEventStorage.DefaultInboxTableName;

    /// <summary>
    /// Maps <see cref="InboxMessage"/> to <paramref name="tableName"/> in <paramref name="schema"/>,
    /// keyed on (<see cref="InboxMessage.MessageId"/>, <see cref="InboxMessage.Consumer"/>). Call it
    /// from <c>OnModelCreating</c> of the context <c>DomainEventInbox&lt;TContext&gt;</c> runs on.
    /// <para>
    /// The composite primary key is the mechanism, not decoration: it is what makes a second attempt to
    /// mark the same message for the same consumer fail rather than write a duplicate row.
    /// </para>
    /// <para>
    /// Like the outbox, the table lives in the <c>ddd</c> schema by default. Pass <see langword="null"/>
    /// for the provider's default schema; SQLite ignores schemas either way.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    public static ModelBuilder AddDomainEventInbox(this ModelBuilder modelBuilder, string tableName = DefaultTableName, string? schema = DomainEventStorage.DefaultSchema)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable(tableName, schema);
            builder.HasKey(m => new { m.MessageId, m.Consumer });
            builder.Property(m => m.Consumer).HasMaxLength(DomainEventStorage.MaxConsumerLength).IsRequired();
            builder.Property(m => m.MessageName).HasMaxLength(DomainEventStorage.MaxNameLength);
            builder.Property(m => m.ProcessedAt).HasConversion(new UtcDateTimeOffsetConverter());
            builder.HasIndex(m => m.ProcessedAt);
        });

        return modelBuilder;
    }
}
