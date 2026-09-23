using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Ordering.Contracts.Converters;
using DDDToolkit.Examples.Payments.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.Examples.Payments.Infrastructure.Persistence;

/// <summary>Payments' own database: the payments, an outbox and an inbox.</summary>
public sealed class PaymentsContext(DbContextOptions<PaymentsContext> options) : DbContext(options)
{
    public const string Schema = "payments";

    public DbSet<Payment> Payments => Set<Payment>();

    public static void UsePostgres(DbContextOptionsBuilder options, string connectionString)
        => options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // One payment per order, whatever the transport delivers twice.
        modelBuilder.Entity<Payment>().HasIndex(payment => payment.Order).IsUnique();

        modelBuilder.AddDomainEventOutbox(Database, schema: Schema);
        modelBuilder.AddDomainEventInbox(Database, schema: Schema);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddOrderingContractsConverters();
        configurationBuilder.AddPaymentsConverters();
    }
}

[SupabaseMigrations]
public sealed class PaymentsContextFactory : IDesignTimeDbContextFactory<PaymentsContext>
{
    public PaymentsContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentsContext>();
        PaymentsContext.UsePostgres(options, "Host=unused");
        return new PaymentsContext(options.Options);
    }
}
