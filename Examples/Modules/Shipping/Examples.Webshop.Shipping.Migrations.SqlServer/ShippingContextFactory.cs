using Examples.Webshop.Shipping.Infrastructure.Persistence;
using Examples.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Examples.Webshop.Shipping.Migrations.SqlServer;

/// <summary>
/// How dotnet ef builds <see cref="ShippingContext"/> on SQL Server, pointing nowhere because scaffolding a
/// migration opens no connection. With this project as both the target and the startup project, this
/// factory wins over the module's Postgres one:
/// <code>
/// dotnet ef migrations add AddGiftWrap --project Examples/Modules/Shipping/Examples.Webshop.Shipping.Migrations.SqlServer --output-dir Migrations
/// </code>
/// </summary>
public sealed class ShippingContextFactory : IDesignTimeDbContextFactory<ShippingContext>
{
    public ShippingContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShippingContext>();
        ModuleDatabase.UseSqlServer(options, "Server=unused", ShippingContext.Schema);
        return new(options.Options);
    }
}
