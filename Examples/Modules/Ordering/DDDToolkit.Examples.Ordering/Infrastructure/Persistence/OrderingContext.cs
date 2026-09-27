using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Examples.Ordering.Contracts.Converters;
using DDDToolkit.Examples.Ordering.Converters;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Examples.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDDToolkit.Examples.Ordering.Infrastructure.Persistence;

/// <summary>
/// Ordering's own database. A module owns its tables; nothing in Shipping can query one of these, and
/// no query can join across the two by accident.
/// </summary>
/// <remarks>
/// There is no configuration for the domain model at all. <c>OrderLine</c> is an owned type because
/// <c>[Entity]</c> generated <c>[Owned]</c>, <c>Order.Lines</c> is discovered as an owned collection
/// through the generated backing field, <c>Address</c> is stored inline because <c>[ValueObject]</c>
/// generated <c>[ComplexType]</c>, <c>OrderId</c> and <c>OrderLineId</c> map through generated value
/// converters, and <c>Order.Version</c> is a concurrency token.
/// </remarks>
public sealed class OrderingContext(DbContextOptions<OrderingContext> options) : DbContext(options)
{
    /// <summary>
    /// The schema Ordering's tables and migration history live in. On Postgres the two modules can share
    /// one database, as they do on one Supabase project, and still own their tables: each has its own
    /// schema and its own history table, so neither module's migrations can see the other's.
    /// </summary>
    public const string Schema = "ordering";

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<CatalogPrice> CatalogPrices => Set<CatalogPrice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // The read model is a plain class, so it is the one type here the conventions cannot key.
        modelBuilder.Entity<CatalogPrice>().HasKey(price => price.Sku);

        // Where row level security is on, every query of a customer's is filtered on who placed the order,
        // by the rules in Domain/Aggregates/Orders/Access.
        modelBuilder.Entity<Order>().HasIndex(order => order.PlacedBy);

        // This module produces integration events, so it needs the outbox table: SaveChanges writes one
        // row per domain event in the same transaction as the order.
        // Database tells the toolkit which provider this is, so the timestamp columns get the
        // provider's own instant type rather than SQLite's lowest common denominator.
        //
        // schema: Schema puts the table in "ordering", not in the toolkit's default "ddd". On Supabase all
        // five modules share one database, and a shared ddd.OutboxMessages would have every module's
        // poller reading every other module's rows, and every module's migration creating the same table.
        // In a database of its own a module could keep the default; sharing one, each module owns its
        // outbox and inbox the way it owns its other tables.
        modelBuilder.AddDomainEventOutbox(Database, schema: Schema);

        // And it consumes them too: prices from Catalog, answers from Inventory and Payments. The inbox is
        // what makes each of those handlers idempotent. A module can have both tables; there is nothing
        // special about a module that only produces or only consumes.
        modelBuilder.AddDomainEventInbox(Database, schema: Schema);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The same three conventions in every context: read-only collections, [Internal] members, Version.
        configurationBuilder.AddDDDToolkitConventions();

        // Amounts of money: two decimals, up to a trillion. Without a precision SQL Server guesses
        // (18,2) and warns, and Postgres stores numbers of any length.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // One generated call per assembly that declares identifiers or single value objects.
        configurationBuilder.AddOrderingContractsConverters();
        configurationBuilder.AddOrderingConverters();
    }
}

/// <summary>
/// How <c>dotnet ef migrations add</c> and the Supabase export build an <see cref="OrderingContext"/>:
/// on Postgres, and pointing nowhere, because neither of them opens a connection. Without it the tools
/// would build the host, and the host picks SQLite unless it is given a Supabase connection string.
/// <para>
/// <c>[SupabaseMigrations]</c> is what puts Ordering's migrations in <c>supabase/migrations</c>: the host
/// turns the export on, and its build finds this factory without anybody listing it.
/// </para>
/// </summary>
[SupabaseMigrations]
public sealed class OrderingContextFactory : IDesignTimeDbContextFactory<OrderingContext>
{
    public OrderingContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderingContext>();
        ModuleDatabase.UsePostgres(options, "Host=unused", OrderingContext.Schema);
        return new OrderingContext(options.Options);
    }
}
