using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Tests.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

// Modules in schemas Postgres keeps only in double quotes, which Entity Framework creates quoted: the help desk in
// one with a capital and an underscore, and again in a word Postgres keeps for itself; a module whose schema is that
// long that Postgres would cut it; and a module that exports its migrations to Supabase from a schema with capitals.

/// <summary>The help desk of <see cref="DeskContext"/> in <c>"Help_Desk"</c>, a schema with capitals and an underscore.</summary>
public sealed class HelpDeskContext(DbContextOptions<HelpDeskContext> options) : DbContext(options)
{
    /// <summary>The schema, as the model names it and Entity Framework creates it: <c>"Help_Desk"</c>.</summary>
    public const string Schema = "Help_Desk";

    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Ticket>().OwnsMany(ticket => ticket.Comments, comment => comment.OwnsMany(each => each.Reactions));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }

    /// <summary>A context on <paramref name="connectionString"/>, or on nothing for a script, which never connects.</summary>
    public static HelpDeskContext Create(string connectionString = "Host=nowhere.invalid;Database=unused", IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<HelpDeskContext>().UseNpgsql(connectionString);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new HelpDeskContext(options.Options);
    }
}

/// <summary>The help desk in <c>"order"</c>, a word Postgres keeps for itself, which it takes for a schema only in double quotes.</summary>
public sealed class OrderDeskContext(DbContextOptions<OrderDeskContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("order");
        modelBuilder.Entity<Ticket>().OwnsMany(ticket => ticket.Comments, comment => comment.OwnsMany(each => each.Reactions));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }

    /// <summary>A context on nothing, for a script, which never connects.</summary>
    public static OrderDeskContext Create() => new(new DbContextOptionsBuilder<OrderDeskContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}

/// <summary>A module whose schema is 64 letters long, one more than Postgres keeps of a name.</summary>
public sealed class OverlongMemosContext(DbContextOptions<OverlongMemosContext> options) : DbContext(options)
{
    /// <summary>The schema: <c>Memos</c> and 59 more letters.</summary>
    public static readonly string Schema = "Memos" + new string('x', 59);

    public DbSet<Memo> Memos => Set<Memo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema(Schema);

    /// <summary>A context on nothing, for a script, which never connects.</summary>
    public static OverlongMemosContext Create() => new(new DbContextOptionsBuilder<OverlongMemosContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}

/// <summary>
/// A module with real migrations, which the Supabase export works from, in <c>"Shelf_Life"</c>: a schema with capitals
/// and an underscore. Nothing here connects.
/// </summary>
public sealed class CapitalShelfContext(DbContextOptions<CapitalShelfContext> options) : DbContext(options)
{
    /// <summary>The schema, as the model and the migration name it.</summary>
    public const string Schema = "Shelf_Life";

    public DbSet<SupabaseShelf> Shelves => Set<SupabaseShelf>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<SupabaseShelf>().ToTable("Shelves");
    }

    /// <summary>A context on Npgsql pointing at nothing.</summary>
    public static CapitalShelfContext Create() => new(new DbContextOptionsBuilder<CapitalShelfContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}

/// <summary>The module's one migration: its schema, and the shelves in it.</summary>
[DbContext(typeof(CapitalShelfContext))]
[Migration(Id)]
public class CreateCapitalShelves : Migration
{
    public const string Id = "20261023120000_CreateCapitalShelves";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(CapitalShelfContext.Schema);
        migrationBuilder.CreateTable(
            name: "Shelves",
            schema: CapitalShelfContext.Schema,
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 200, nullable: false),
                Capacity = table.Column<int>(nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Shelves", x => x.Id));
    }
}
