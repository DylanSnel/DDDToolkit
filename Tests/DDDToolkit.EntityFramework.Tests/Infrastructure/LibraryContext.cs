using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Tests.Converters;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.ExampleApi.Converters;
using DDDToolkit.ExampleLibrary.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddDomainEventOutbox();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddCommonConverters();
        configurationBuilder.AddApiConverters();
        configurationBuilder.AddEfTestsConverters();
    }
}
