using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Providers.Tests.Converters;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.ExampleApi.Converters;
using DDDToolkit.ExampleLibrary.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

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
