using DDDToolkit.Benchmarks.Converters;
using DDDToolkit.Benchmarks.Domain;
using DDDToolkit.EntityFramework.Conventions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Benchmarks.Infrastructure;

/// <summary>
/// Holds both aggregates, mapped the way the documentation tells you to map them: the DDDToolkit
/// conventions plus the generated converters, and not a line of hand-written configuration for
/// either identifier.
/// </summary>
public sealed class OrderContext(DbContextOptions<OrderContext> options) : DbContext(options)
{
    /// <summary>Orders keyed by the struct identifier.</summary>
    public DbSet<StructOrder> StructOrders => Set<StructOrder>();

    /// <summary>Orders keyed by the record identifier.</summary>
    public DbSet<RecordOrder> RecordOrders => Set<RecordOrder>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddBenchmarksConverters();
    }
}
