using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Tests.Converters;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.ExampleApi.Converters;
using DDDToolkit.ExampleLibrary.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

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
        modelBuilder.AddDomainEventOutbox("EventsOut", schema: "messaging");
        modelBuilder.AddDomainEventInbox("EventsIn", schema: null);
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
