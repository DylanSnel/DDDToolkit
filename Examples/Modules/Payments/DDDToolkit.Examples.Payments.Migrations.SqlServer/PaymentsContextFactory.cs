using DDDToolkit.Examples.Payments.Infrastructure.Persistence;
using DDDToolkit.Examples.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDDToolkit.Examples.Payments.Migrations.SqlServer;

/// <summary>
/// How dotnet ef builds <see cref="PaymentsContext"/> on SQL Server, pointing nowhere because scaffolding a
/// migration opens no connection. With this project as both the target and the startup project, this
/// factory wins over the module's Postgres one:
/// <code>
/// dotnet ef migrations add AddGiftWrap --project Examples/Modules/Payments/DDDToolkit.Examples.Payments.Migrations.SqlServer --output-dir Migrations
/// </code>
/// </summary>
public sealed class PaymentsContextFactory : IDesignTimeDbContextFactory<PaymentsContext>
{
    public PaymentsContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentsContext>();
        ModuleDatabase.UseSqlServer(options, "Server=unused", PaymentsContext.Schema);
        return new(options.Options);
    }
}
