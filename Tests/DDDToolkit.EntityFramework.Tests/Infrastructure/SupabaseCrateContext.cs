using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A context whose migration has the model snapshot <c>dotnet ef migrations add</c> writes beside it, which the
/// export compares the model with. Its model is what the migration makes. <see cref="Labelled.SupabaseCrateContext"/>
/// is the same context in a later build, after <see cref="SupabaseCrate.Label"/> was mapped and before a migration
/// adds its column.
/// </summary>
public class SupabaseCrateContext(DbContextOptions<SupabaseCrateContext> options) : DbContext(options)
{
    public DbSet<SupabaseCrate> Crates => Set<SupabaseCrate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<SupabaseCrate>().ToTable("Crates").Ignore(crate => crate.Label);

    public static SupabaseCrateContext Create()
        => new(new DbContextOptionsBuilder<SupabaseCrateContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}

public class SupabaseCrate
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public string? Label { get; set; }
}

/// <summary>The one migration of both crate contexts: the table, without <c>Label</c>.</summary>
[DbContext(typeof(SupabaseCrateContext))]
[Migration(Id)]
public class CreateCrates : Migration
{
    public const string Id = "20260920080000_CreateCrates";

    protected override void Up(MigrationBuilder migrationBuilder) => UpOf(migrationBuilder);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("Crates");

    internal static void UpOf(MigrationBuilder migrationBuilder)
        => migrationBuilder.CreateTable(
            name: "Crates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Crates", x => x.Id));
}

/// <summary>The snapshot <c>dotnet ef migrations add</c> wrote with <see cref="CreateCrates"/>: no <c>Label</c>.</summary>
[DbContext(typeof(SupabaseCrateContext))]
public class SupabaseCrateContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => BuildModelOf(modelBuilder);

    internal static void BuildModelOf(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseCrate", b =>
        {
            b.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            b.Property<string>("Name")
                .IsRequired()
                .HasColumnType("text");

            b.HasKey("Id");

            b.ToTable("Crates");
        });
    }
}
