using DTS.DDD.Benchmarks.Converters;
using DTS.DDD.Benchmarks.Domain;
using DTS.DDD.EntityFramework.Conventions;
using Microsoft.EntityFrameworkCore;

namespace DTS.DDD.Benchmarks.Infrastructure;

/// <summary>
/// The struct-keyed orders with one named query filter, as Tenancy's tenant filter is one: the context a read
/// that skips a filter by name is measured on.
/// </summary>
public sealed class FilteredOrderContext(DbContextOptions<FilteredOrderContext> options) : DbContext(options)
{
    /// <summary>The name of the filter, which hides archived orders.</summary>
    public const string Archived = "benchmarks.archived";

    /// <summary>Orders keyed by the struct identifier.</summary>
    public DbSet<StructOrder> StructOrders => Set<StructOrder>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddBenchmarksConverters();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<StructOrder>().HasQueryFilter(Archived, order => order.Reference != "archived");
}
