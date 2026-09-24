using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Catalog.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDDToolkit.Examples.Catalog.Infrastructure.Persistence;

/// <summary>Catalog's own database: the products, and the outbox that tells everyone about them.</summary>
public sealed class CatalogContext(DbContextOptions<CatalogContext> options) : DbContext(options)
{
    /// <summary>The schema Catalog's tables and migration history live in.</summary>
    public const string Schema = "catalog";

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // The one line of configuration in this module, and it is not about mapping: the generated
        // conventions map everything already. A SKU names one product, and only the database can
        // promise that across two requests racing to list the same one.
        modelBuilder.Entity<Product>().HasIndex(product => product.Sku).IsUnique();

        modelBuilder.AddDomainEventOutbox(Database, schema: Schema);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();

        // Amounts of money: two decimals, up to a trillion. Without a precision SQL Server guesses
        // (18,2) and warns, and Postgres stores numbers of any length.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.AddCatalogConverters();
    }
}

/// <summary>How <c>dotnet ef</c> and the Supabase export build a <see cref="CatalogContext"/>.</summary>
[SupabaseMigrations]
public sealed class CatalogContextFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>();
        ModuleDatabase.UsePostgres(options, "Host=unused", CatalogContext.Schema);
        return new CatalogContext(options.Options);
    }
}
