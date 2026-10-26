using DDDToolkit.EntityFramework;
using Examples.Webshop.Payments.Infrastructure.Persistence;
using Examples.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Examples.Webshop.Payments.Migrations.SqlServer;

/// <summary>
/// How dotnet ef builds <see cref="PaymentsContext"/> on SQL Server, pointing nowhere because scaffolding a
/// migration opens no connection. With this project as both the target and the startup project, this
/// factory wins over the module's Postgres one:
/// <code>
/// dotnet ef migrations add AddGiftWrap --project Examples/Modules/Payments/Examples.Webshop.Payments.Migrations.SqlServer --output-dir Migrations
/// </code>
/// </summary>
public sealed class PaymentsContextFactory : IDesignTimeDbContextFactory<PaymentsContext>
{
    public PaymentsContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentsContext>();
        // The migration history in the module's schema, where the host's UseDDDToolkit keeps it.
        ModuleDatabase.UseSqlServer(options, "Server=unused").UseDDDToolkitDesignTime();
        return new(options.Options);
    }
}
