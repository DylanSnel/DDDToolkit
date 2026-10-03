using Examples.Webshop.Ordering.Infrastructure.Persistence;
using Examples.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Examples.Webshop.Ordering.Migrations.SqlServer;

/// <summary>
/// How dotnet ef builds <see cref="OrderingContext"/> on SQL Server, pointing nowhere because scaffolding a
/// migration opens no connection. With this project as both the target and the startup project, this
/// factory wins over the module's Postgres one:
/// <code>
/// dotnet ef migrations add AddGiftWrap --project Examples/Modules/Ordering/Examples.Webshop.Ordering.Migrations.SqlServer --output-dir Migrations
/// </code>
/// </summary>
public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
{
    public OrderingContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderingContext>();
        ModuleDatabase.UseSqlServer(options, "Server=unused", OrderingContext.Schema);
        return new(options.Options);
    }
}
