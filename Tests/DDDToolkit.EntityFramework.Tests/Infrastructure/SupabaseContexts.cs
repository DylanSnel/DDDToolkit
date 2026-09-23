using DDDToolkit.EntityFramework.Migrations;
using DDDToolkit.EntityFramework.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A context with real migrations, which is what the Supabase export works from: it reads the
/// migrations assembly, not the model. Nothing here connects unless a test gives it a live server.
/// </summary>
public class SupabaseShelfContext(DbContextOptions<SupabaseShelfContext> options) : DbContext(options)
{
    public DbSet<SupabaseShelf> Shelves => Set<SupabaseShelf>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SupabaseShelf>().ToTable("Shelves");
        modelBuilder.AddDomainEventOutbox(Database);
    }

    /// <summary>A context on Npgsql pointing at <paramref name="connectionString"/>, or at nothing.</summary>
    public static SupabaseShelfContext Create(string connectionString = "Host=nowhere.invalid;Database=unused")
        => new(new DbContextOptionsBuilder<SupabaseShelfContext>().UseNpgsql(connectionString).Options);
}

public class SupabaseShelf
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public int Capacity { get; set; }
}

/// <summary>The first migration: a table in <c>public</c>, and the outbox in <c>ddd</c>.</summary>
[DbContext(typeof(SupabaseShelfContext))]
[Migration(Id)]
public class CreateShelves : Migration
{
    public const string Id = "20260901120000_CreateShelves";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Shelves",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 200, nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Shelves", x => x.Id));

        migrationBuilder.CreateDomainEventOutbox();
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropDomainEventOutbox();
        migrationBuilder.DropTable("Shelves");
    }
}

/// <summary>The second migration: a change to an existing table, so no new table and no row level security.</summary>
[DbContext(typeof(SupabaseShelfContext))]
[Migration(Id)]
public class AddShelfCapacity : Migration
{
    public const string Id = "20260915093000_AddShelfCapacity";

    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<int>(name: "Capacity", table: "Shelves", nullable: false, defaultValue: 0);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "Capacity", table: "Shelves");
}

/// <summary>
/// A second module's context, in its own schema, exporting into the same directory as
/// <see cref="SupabaseShelfContext"/>.
/// </summary>
public class SupabaseLedgerContext(DbContextOptions<SupabaseLedgerContext> options) : DbContext(options)
{
    public static SupabaseLedgerContext Create()
        => new(new DbContextOptionsBuilder<SupabaseLedgerContext>().UseNpgsql("Host=nowhere.invalid").Options);
}

[DbContext(typeof(SupabaseLedgerContext))]
[Migration(Id)]
public class CreateLedger : Migration
{
    public const string Id = "20260910080000_CreateLedger";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("ledger");
        migrationBuilder.CreateTable(
            name: "Entries",
            schema: "ledger",
            columns: table => new { Id = table.Column<Guid>(nullable: false) },
            constraints: table => table.PrimaryKey("PK_Entries", x => x.Id));
    }
}

/// <summary>A context whose only migration has an id Supabase could not order.</summary>
public class UntimedMigrationContext(DbContextOptions<UntimedMigrationContext> options) : DbContext(options)
{
    public static UntimedMigrationContext Create()
        => new(new DbContextOptionsBuilder<UntimedMigrationContext>().UseNpgsql("Host=nowhere.invalid").Options);
}

[DbContext(typeof(UntimedMigrationContext))]
[Migration("Initial")]
public class UntimedMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.EnsureSchema("untimed");
}
