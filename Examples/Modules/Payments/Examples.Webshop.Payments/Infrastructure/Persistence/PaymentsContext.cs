using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Supabase;
using Examples.Webshop.Ordering.Contracts.Converters;
using Examples.Webshop.Payments.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Examples.Webshop.Payments.Infrastructure.Persistence;

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
        configurationBuilder.AddOrderingConverters();
        configurationBuilder.AddPaymentsConverters();
    }
}

[SupabaseMigrations]
public sealed class PaymentsContextFactory : IDesignTimeDbContextFactory<PaymentsContext>
{
    public PaymentsContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<PaymentsContext>().UseNpgsql("Host=unused").UseDDDToolkitDesignTime().Options);
}
