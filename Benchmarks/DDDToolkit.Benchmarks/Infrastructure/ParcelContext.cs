using DDDToolkit.Benchmarks.Converters;
using DDDToolkit.Benchmarks.Domain;
using DDDToolkit.EntityFramework.Conventions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Benchmarks.Infrastructure;

/// <summary>A context with the parcels, for the benchmark of the <c>[Own]</c> check.</summary>
public sealed class ParcelContext(DbContextOptions<ParcelContext> options) : DbContext(options)
{
    /// <summary>The parcels.</summary>
    public DbSet<Parcel> Parcels => Set<Parcel>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddBenchmarksConverters();
    }
}
