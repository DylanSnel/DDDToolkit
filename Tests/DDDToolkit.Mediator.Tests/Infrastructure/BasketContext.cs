using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Mediator.Tests.Converters;
using DDDToolkit.Mediator.Tests.Domain;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Mediator.Tests.Infrastructure;

/// <summary>
/// The test context. Nothing is configured by hand: the <see cref="BasketId"/> converter comes from
/// the generated <c>AddMediatorTestsConverters</c> call and <c>Basket.Items</c> is mapped by the
/// DDDToolkit conventions. The outbox table is always in the model so one test can switch delivery
/// modes without a second context.
/// </summary>
public sealed class BasketContext(DbContextOptions<BasketContext> options) : DbContext(options)
{
    public DbSet<Basket> Baskets => Set<Basket>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddDomainEventOutbox();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddMediatorTestsConverters();
    }
}
