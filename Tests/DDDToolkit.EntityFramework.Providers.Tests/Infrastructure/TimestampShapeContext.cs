using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Storage;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>
/// The outbox and the inbox mapped a second time, with
/// <see cref="DomainEventTimestamps.UtcDateTime"/> and under names of their own, so a test can create
/// both shapes in one database and read what each column actually became out of
/// <c>information_schema</c>.
/// <para>
/// The point is the promise the documentation makes to somebody who already has a 3.0 database: pass
/// <see cref="DomainEventTimestamps.UtcDateTime"/> and the columns stay exactly as they were. A
/// promise about DDL is worth testing as DDL, not as a model property.
/// </para>
/// <para>
/// No schema, because <c>ddd</c> already exists by the time this runs and the create script would try
/// to make it again. The table names carry the distinction instead.
/// </para>
/// </summary>
public class TimestampShapeContext(DbContextOptions<TimestampShapeContext> options) : DbContext(options)
{
    /// <summary>The outbox table this context maps, in the provider's default schema.</summary>
    public const string OutboxTable = "OutboxMessagesUtc";

    /// <summary>The inbox table this context maps, in the provider's default schema.</summary>
    public const string InboxTable = "InboxMessagesUtc";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddDomainEventOutbox(Database, OutboxTable, schema: null, DomainEventTimestamps.UtcDateTime);
        modelBuilder.AddDomainEventInbox(Database, InboxTable, schema: null, DomainEventTimestamps.UtcDateTime);
    }
}
