using DDDToolkit.EntityFramework;
using Examples.Hosting;
using Examples.Webshop.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Examples.Webshop.Catalog.Migrations.SqlServer;

/// <summary>
/// How dotnet ef builds <see cref="CatalogContext"/> on SQL Server, pointing nowhere because scaffolding a
/// migration opens no connection. With this project as both the target and the startup project, this
/// factory wins over the module's Postgres one:
/// <code>
/// dotnet ef migrations add AddGiftWrap --project Examples/Modules/Catalog/Examples.Webshop.Catalog.Migrations.SqlServer --output-dir Migrations
/// </code>
/// </summary>
public sealed class CatalogContextFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>();
        // The migration history in the module's schema, where the host's UseDDDToolkit keeps it.
        ModuleDatabase.UseSqlServer(options, "Server=unused").UseDDDToolkitDesignTime();
        return new(options.Options);
    }
}
