using DDDToolkit.Examples.Catalog.Infrastructure.Persistence;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Inventory.Infrastructure.Persistence;
using DDDToolkit.Examples.Ordering.Infrastructure.Persistence;
using DDDToolkit.Examples.Payments.Infrastructure.Persistence;
using DDDToolkit.Examples.Shipping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDDToolkit.Examples.Migrations.SqlServer;

// How dotnet ef builds each context on SQL Server, pointing nowhere because scaffolding a migration opens
// no connection. With this project as both the target and the startup project, these factories win over
// the Postgres ones in the modules. Scaffold with, for example:
//
//   dotnet ef migrations add AddGiftWrap --project Examples/Modules/Migrations.SqlServer/DDDToolkit.Examples.Migrations.SqlServer
//       --context OrderingContext --output-dir Ordering
//
// Each factory is the module's context and nothing else; the model is the module's to define.

public sealed class CatalogContextFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args) => new(DesignTimeOptions.Options<CatalogContext>(CatalogContext.Schema));
}

public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
{
    public OrderingContext CreateDbContext(string[] args) => new(DesignTimeOptions.Options<OrderingContext>(OrderingContext.Schema));
}

public sealed class InventoryContextFactory : IDesignTimeDbContextFactory<InventoryContext>
{
    public InventoryContext CreateDbContext(string[] args) => new(DesignTimeOptions.Options<InventoryContext>(InventoryContext.Schema));
}

public sealed class PaymentsContextFactory : IDesignTimeDbContextFactory<PaymentsContext>
{
    public PaymentsContext CreateDbContext(string[] args) => new(DesignTimeOptions.Options<PaymentsContext>(PaymentsContext.Schema));
}

public sealed class ShippingContextFactory : IDesignTimeDbContextFactory<ShippingContext>
{
    public ShippingContext CreateDbContext(string[] args) => new(DesignTimeOptions.Options<ShippingContext>(ShippingContext.Schema));
}

file static class DesignTimeOptions
{
    public static DbContextOptions<TContext> Options<TContext>(string schema) where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        ModuleDatabase.UseSqlServer(options, "Server=unused", schema);
        return options.Options;
    }
}
