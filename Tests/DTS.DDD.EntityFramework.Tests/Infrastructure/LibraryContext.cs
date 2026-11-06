using DTS.DDD.EntityFramework.Conventions;
using DTS.DDD.EntityFramework.Inbox;
using DTS.DDD.EntityFramework.Outbox;
using DTS.DDD.EntityFramework.Tests.Converters;
using DTS.DDD.EntityFramework.Tests.Domain;
using DTS.DDD.ExampleApi.Converters;
using DTS.DDD.ExampleLibrary.Converters;
using Microsoft.EntityFrameworkCore;

namespace DTS.DDD.EntityFramework.Tests.Infrastructure;

/// <summary>
/// The test domain's context. Nothing is configured by hand: [Entity] types are owned through the
/// generated [Owned] attribute, generated read-only collections are mapped by the DDDToolkit
/// conventions and every converter comes from a generated Add{Module}Converters call.
/// </summary>
public class LibraryContext(DbContextOptions<LibraryContext> options) : DbContext(options)
{
    public DbSet<Shelf> Shelves => Set<Shelf>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddDomainEventOutbox(Database);
        modelBuilder.AddDomainEventInbox(Database);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddCommonConverters();
        configurationBuilder.AddApiConverters();
        configurationBuilder.AddEfTestsConverters();
    }
}
