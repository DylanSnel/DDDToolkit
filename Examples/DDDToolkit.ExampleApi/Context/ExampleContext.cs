using DDDToolkit.ExampleApi.Converters;
using DDDToolkit.ExampleApi.Domain.ProductAggregate;
using DDDToolkit.ExampleApi.Domain.UserAggregate;
using DDDToolkit.ExampleLibrary.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.ExampleApi.Context;

public class ExampleContext : DbContext
{
    public ExampleContext(DbContextOptions<ExampleContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExampleContext).Assembly);

        // Order is [Entity] and therefore an owned type; its Products collection of struct ids is a primitive collection.
        modelBuilder.Entity<User>().OwnsMany(x => x.Orders);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Generated: one call per assembly that declares ids or single value objects.
        configurationBuilder.AddCommonConverters();
        configurationBuilder.AddApiConverters();
    }
}
