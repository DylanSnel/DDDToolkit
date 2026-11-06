using DTS.DDD.EntityFramework.Conventions;
using DTS.DDD.EntityFramework.Inbox;
using DTS.DDD.EntityFramework.Outbox;
using DTS.DDD.EntityFramework.Providers.Tests.Converters;
using DTS.DDD.EntityFramework.Tests.Domain;
using DTS.DDD.ExampleApi.Converters;
using DTS.DDD.ExampleLibrary.Converters;
using Microsoft.EntityFrameworkCore;

namespace DTS.DDD.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>
/// The same context the SQLite suite uses, configured the same way and against the same aggregates.
/// Nothing here is provider aware on purpose: if a convention only works because SQLite is forgiving,
/// this is where it shows.
/// </summary>
public class ProviderContext(DbContextOptions<ProviderContext> options) : DbContext(options)
{
    /// <summary>Aggregate with a struct key, owned collections and a primitive collection.</summary>
    public DbSet<Shelf> Shelves => Set<Shelf>();

    /// <summary>Aggregate with a complex type and a nullable single value object.</summary>
    public DbSet<Person> People => Set<Person>();

    /// <summary>The outbox table, in the default <c>ddd</c> schema.</summary>
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    /// <summary>The inbox table, in the default <c>ddd</c> schema.</summary>
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddDomainEventOutbox(Database);
        modelBuilder.AddDomainEventInbox(Database);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddCommonConverters();
        configurationBuilder.AddApiConverters();
        configurationBuilder.AddProviderTestsConverters();
    }
}
