using DDDToolkit.EntityFramework.Migrations;
using DDDToolkit.EntityFramework.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A module's context in a schema of its own, with its outbox beside its tables and one migration: what the tests of
/// where a migration history is kept migrate, script and export. Its migration is written for every provider, so the
/// same context runs on SQLite, Postgres and SQL Server. Nothing here connects unless a test gives it a server.
/// </summary>
public sealed class StockroomContext(DbContextOptions<StockroomContext> options) : DbContext(options)
{
    /// <summary>The schema the stockroom's tables, and its migration history, are in.</summary>
    public const string Schema = "stockroom";

    /// <summary>The outbox, named after the module, in the module's schema.</summary>
    public const string OutboxTable = "StockroomOutbox";

    public DbSet<StockroomBin> Bins => Set<StockroomBin>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<StockroomBin>(bin =>
        {
            bin.ToTable("Bins");
            bin.Property(each => each.Label).HasMaxLength(100);
        });
        modelBuilder.AddDomainEventOutbox(Database, tableName: OutboxTable, schema: Schema);
    }
}

public sealed class StockroomBin
{
    public Guid Id { get; set; }

    public string Label { get; set; } = "";
}

/// <summary>The stockroom's one migration: its schema, its bins and its outbox.</summary>
[DbContext(typeof(StockroomContext))]
[Migration(Id)]
public sealed class CreateStockroom : Migration
{
    public const string Id = "20261006120000_CreateStockroom";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(StockroomContext.Schema);
        migrationBuilder.CreateTable(
            name: "Bins",
            schema: StockroomContext.Schema,
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Label = table.Column<string>(maxLength: 100, nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Bins", bin => bin.Id));
        migrationBuilder.CreateDomainEventOutbox(StockroomContext.OutboxTable, StockroomContext.Schema);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropDomainEventOutbox(StockroomContext.OutboxTable, StockroomContext.Schema);
        migrationBuilder.DropTable(name: "Bins", schema: StockroomContext.Schema);
    }
}

/// <summary>
/// A second module's context, in a schema of its own, in the same database as <see cref="StockroomContext"/>: its
/// migration history is a second one, and neither module reads the other's migrations as its own.
/// </summary>
public sealed class CounterContext(DbContextOptions<CounterContext> options) : DbContext(options)
{
    /// <summary>The schema the counter's tables, and its migration history, are in.</summary>
    public const string Schema = "counter";

    public DbSet<CounterTill> Tills => Set<CounterTill>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<CounterTill>().ToTable("Tills");
    }
}

public sealed class CounterTill
{
    public Guid Id { get; set; }

    public int Number { get; set; }
}

/// <summary>The counter's one migration: its schema and its tills.</summary>
[DbContext(typeof(CounterContext))]
[Migration(Id)]
public sealed class CreateCounter : Migration
{
    public const string Id = "20261006121500_CreateCounter";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(CounterContext.Schema);
        migrationBuilder.CreateTable(
            name: "Tills",
            schema: CounterContext.Schema,
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Number = table.Column<int>(nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Tills", till => till.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "Tills", schema: CounterContext.Schema);
}

/// <summary>A context whose model names no schema: its tables, and its migration history, are in the provider's default.</summary>
public sealed class NoticeboardContext(DbContextOptions<NoticeboardContext> options) : DbContext(options)
{
    public DbSet<Notice> Notices => Set<Notice>();
}

public sealed class Notice
{
    public Guid Id { get; set; }

    public string Text { get; set; } = "";
}
