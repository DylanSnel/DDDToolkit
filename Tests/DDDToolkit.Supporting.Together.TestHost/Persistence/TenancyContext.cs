using Campus.Converters;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Campus.Persistence;

/// <summary>
/// The context Tenancy lives in: a plain context in a schema of its own, with Tenancy's tables closed over the
/// application's classes. <c>AddTenancy()</c> is the call the generator writes into this project. The courses
/// and the labs are in another context, which reads these rows through Tenancy's read model and never maps
/// its tables.
/// </summary>
/// <param name="options">The context's options.</param>
public sealed class TenancyContext(DbContextOptions<TenancyContext> options) : DbContext(options)
{
    /// <summary>The schema Tenancy's tables, and on Postgres its functions, are in.</summary>
    public const string Schema = "tenancy";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // With the provider known, a tenant's root and a seat's primary placement are unique in the database as well.
        modelBuilder.AddTenancy(database: Database);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddCampusConverters();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }
}
