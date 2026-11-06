using DTS.DDD.EntityFramework.Conventions;
using DTS.DDD.EntityFramework.Inbox;
using DTS.DDD.EntityFramework.Outbox;
using DTS.DDD.EntityFramework.Tests.Converters;
using DTS.DDD.EntityFramework.Tests.Domain;
using DTS.DDD.ExampleApi.Converters;
using DTS.DDD.ExampleLibrary.Converters;
using Microsoft.EntityFrameworkCore;

namespace DTS.DDD.EntityFramework.Tests.Infrastructure;

/// <summary>Contains only the unmappable <see cref="TagCloud"/>; building its model must fail loudly.</summary>
public class UnmappableSetContext(DbContextOptions<UnmappableSetContext> options) : DbContext(options)
{
    public DbSet<TagCloud> TagClouds => Set<TagCloud>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }
}

/// <summary>
/// The messaging tables moved off their defaults: the outbox into another schema under another name,
/// the inbox into the provider's default schema.
/// </summary>
public class RenamedStorageContext(DbContextOptions<RenamedStorageContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddDomainEventOutbox(Database, "EventsOut", schema: "messaging");
        modelBuilder.AddDomainEventInbox(Database, "EventsIn", schema: null);
    }
}

/// <summary>Has aggregates but no outbox table, to prove UseOutbox fails with guidance instead of silently.</summary>
public class NoOutboxContext(DbContextOptions<NoOutboxContext> options) : DbContext(options)
{
    public DbSet<Shelf> Shelves => Set<Shelf>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddCommonConverters();
        configurationBuilder.AddApiConverters();
        configurationBuilder.AddEfTestsConverters();
    }
}
