using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.ExampleApi.Converters;
using DDDToolkit.ExampleApi.Domain.ProductAggregate;
using DDDToolkit.ExampleApi.Domain.UserAggregate;
using DDDToolkit.ExampleLibrary.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.ExampleApi.Context;

/// <summary>
/// Everything DDD-specific about this model comes from attributes, generators and conventions:
/// <list type="bullet">
///   <item>[Entity] Order is an owned type (generated [Owned]); User.Orders is discovered as an owned collection.</item>
///   <item>Order.Products (generated IReadOnlyList&lt;ProductId&gt;) is mapped as a primitive collection of converted ids by the DDDToolkit conventions.</item>
///   <item>Ids and single value objects use the generated converters; PersonName is a complex type.</item>
///   <item>User.Version and Product.Version are concurrency tokens.</item>
/// </list>
/// </summary>
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

        // The outbox table is always part of the model, so switching Program.cs to the outbox variant
        // needs no schema change. Harmless when events are dispatched in process.
        modelBuilder.AddDomainEventOutbox(Database);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The DDDToolkit conventions are the same for every context...
        configurationBuilder.AddDDDToolkitConventions();

        // ...the converters are generated per assembly that declares ids or single value objects.
        configurationBuilder.AddCommonConverters();
        configurationBuilder.AddApiConverters();
    }
}
