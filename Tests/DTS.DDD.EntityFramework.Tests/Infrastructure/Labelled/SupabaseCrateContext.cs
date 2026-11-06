using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DTS.DDD.EntityFramework.Tests.Infrastructure.Labelled;

/// <summary>
/// <see cref="Infrastructure.SupabaseCrateContext"/> in a later build: the model maps <see cref="SupabaseCrate.Label"/>,
/// added in the code or by a package upgrade, and no migration adds its column yet. A class of the same name, so its
/// files are the same context's, as one context class is in two builds.
/// </summary>
public class SupabaseCrateContext(DbContextOptions<SupabaseCrateContext> options) : DbContext(options)
{
    public DbSet<SupabaseCrate> Crates => Set<SupabaseCrate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<SupabaseCrate>().ToTable("Crates");

    public static SupabaseCrateContext Create()
        => new(new DbContextOptionsBuilder<SupabaseCrateContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
}

/// <summary>The same migration as <see cref="Infrastructure.CreateCrates"/>.</summary>
[DbContext(typeof(SupabaseCrateContext))]
[Migration(Infrastructure.CreateCrates.Id)]
public class CreateCrates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => Infrastructure.CreateCrates.UpOf(migrationBuilder);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("Crates");
}

/// <summary>The same snapshot as <see cref="Infrastructure.SupabaseCrateContextModelSnapshot"/>: no <c>Label</c>.</summary>
[DbContext(typeof(SupabaseCrateContext))]
public class SupabaseCrateContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => Infrastructure.SupabaseCrateContextModelSnapshot.BuildModelOf(modelBuilder);
}
