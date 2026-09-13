using DDDToolkit.EntityFramework.Conventions;
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
