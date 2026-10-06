using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Switch.Tests.Converters;
using Microsoft.EntityFrameworkCore;

namespace Shop.Persistence;

/// <summary>
/// The application's context: a plain one, with the outbox its domain events go to. <c>AddTenancy</c> is generated into
/// this project, closed over the classes and ids the switch wrote, and so are the converters of the ids.
/// </summary>
/// <param name="options">The context's options.</param>
public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddTenancy(database: Database);
        modelBuilder.AddDomainEventOutbox(Database);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddShopConverters();
    }
}
