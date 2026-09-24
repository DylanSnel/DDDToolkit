using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Hosting;
using DDDToolkit.Examples.Ordering.Contracts.Converters;
using DDDToolkit.Examples.Payments.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDDToolkit.Examples.Payments.Infrastructure.Persistence;

/// <summary>Payments' own database: the payments, an outbox and an inbox.</summary>
public sealed class PaymentsContext(DbContextOptions<PaymentsContext> options) : DbContext(options)
{
    public const string Schema = "payments";

    public DbSet<Payment> Payments => Set<Payment>();

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

        // Amounts of money: two decimals, up to a trillion. Without a precision SQL Server guesses
        // (18,2) and warns, and Postgres stores numbers of any length.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
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
        ModuleDatabase.UsePostgres(options, "Host=unused", PaymentsContext.Schema);
        return new PaymentsContext(options.Options);
    }
}
