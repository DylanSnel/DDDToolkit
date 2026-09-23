using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Catalog.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.Examples.Catalog.Infrastructure.Persistence;

/// <summary>Catalog's own database: the products, and the outbox that tells everyone about them.</summary>
public sealed class CatalogContext(DbContextOptions<CatalogContext> options) : DbContext(options)
{
    /// <summary>The schema Catalog's tables and migration history live in.</summary>
    public const string Schema = "catalog";

    public DbSet<Product> Products => Set<Product>();

    /// <summary>Postgres, with the migration history in <see cref="Schema"/>.</summary>
    public static void UsePostgres(DbContextOptionsBuilder options, string connectionString)
        => options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schema));

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
        CatalogContext.UsePostgres(options, "Host=unused");
        return new CatalogContext(options.Options);
    }
}
