using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Two modules of one database whose first migrations both make the same schema, <see cref="SharedSchema.Name"/>, as
/// the webshop's Ordering and Shipping both make <c>ddd</c>: each its own schema and history, and a table each in the
/// shared one. Each migration notes in its table when it began and when it ended, by the database's clock, and takes a
/// moment in between, as a migration that moves data does: two that run side by side then overlap for certain, and two
/// that run one after the other show it in their notes.
/// </summary>
public static class SharedSchema
{
    /// <summary>The schema both first migrations make.</summary>
    public const string Name = "ddd";

    /// <summary>How long each migration holds its transaction open after it made the schema.</summary>
    public const double PauseSeconds = 0.2;

    /// <summary>
    /// What both migrations write: their own schema, the shared one, their table in it, the note of their start, the
    /// pause, and the note of their end.
    /// </summary>
    public static void Up(MigrationBuilder migrationBuilder, string schema, string table)
    {
        migrationBuilder.EnsureSchema(schema);
        migrationBuilder.EnsureSchema(Name);
        migrationBuilder.CreateTable(
            name: table,
            schema: Name,
            columns: columns => new
            {
                Step = columns.Column<string>(nullable: false),
                At = columns.Column<DateTimeOffset>(nullable: false),
            },
            constraints: note => note.PrimaryKey("PK_" + table, each => each.Step));
        migrationBuilder.Sql(Note(table, "began"));
        migrationBuilder.Sql(FormattableString.Invariant($"SELECT pg_catalog.pg_sleep({PauseSeconds});"));
        migrationBuilder.Sql(Note(table, "ended"));
    }

    private static string Note(string table, string step)
        => $"""INSERT INTO "{Name}"."{table}" ("Step", "At") VALUES ('{step}', pg_catalog.clock_timestamp());""";
}

/// <summary>The dock: a module in the schema <c>dock</c>, with a table in the shared schema.</summary>
public sealed class DockContext(DbContextOptions<DockContext> options) : DbContext(options)
{
    public const string Schema = "dock";

    public const string Table = "DockNotes";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<SharedSchemaNote>().ToTable(Table, SharedSchema.Name).HasKey(note => note.Step);
    }
}

/// <summary>The quay: a module in the schema <c>quay</c>, with a table in the shared schema.</summary>
public sealed class QuayContext(DbContextOptions<QuayContext> options) : DbContext(options)
{
    public const string Schema = "quay";

    public const string Table = "QuayNotes";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<SharedSchemaNote>().ToTable(Table, SharedSchema.Name).HasKey(note => note.Step);
    }
}

/// <summary>A migration's note: the step, <c>began</c> or <c>ended</c>, and when.</summary>
public sealed class SharedSchemaNote
{
    public string Step { get; set; } = "";

    public DateTimeOffset At { get; set; }
}

/// <summary>The dock's one migration.</summary>
[DbContext(typeof(DockContext))]
[Migration(Id)]
public sealed class CreateDock : Migration
{
    public const string Id = "20261026120000_CreateDock";

    protected override void Up(MigrationBuilder migrationBuilder) => SharedSchema.Up(migrationBuilder, DockContext.Schema, DockContext.Table);
}

/// <summary>The quay's one migration.</summary>
[DbContext(typeof(QuayContext))]
[Migration(Id)]
public sealed class CreateQuay : Migration
{
    public const string Id = "20261026120500_CreateQuay";

    protected override void Up(MigrationBuilder migrationBuilder) => SharedSchema.Up(migrationBuilder, QuayContext.Schema, QuayContext.Table);
}
