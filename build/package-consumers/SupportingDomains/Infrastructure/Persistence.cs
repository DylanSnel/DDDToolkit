using Acme.Press.Infrastructure.Converters;
using Acme.Press.Manuscripts;
using Acme.Press.Tenants;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Acme.Press.Persistence;

/// <summary>The context Tenancy lives in, with its tables closed over the application's classes.</summary>
/// <remarks>
/// The marker is what the host's build looks for in the projects it references, for its Supabase export and its
/// AddSupabaseMigrations(). With it, the Supabase package's generator writes the context's design-time factory beside
/// it, on Postgres, for dotnet ef and the export; neither opens a connection.
/// </remarks>
/// <param name="options">The context's options.</param>
[SupabaseMigrations]
public sealed class TenancyContext(DbContextOptions<TenancyContext> options) : DbContext(options)
{
    /// <summary>The schema Tenancy's tables and functions are in.</summary>
    public const string Schema = "tenancy";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Written by the toolkit's generator, over the classes the module's domain project declares with Tenancy's templates.
        modelBuilder.AddTenancy(database: Database);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();

        // Written by the Entity Framework generator over the module's ids, named after the module.
        configurationBuilder.AddPressConverters();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }
}

/// <summary>The manuscripts' context: a manuscript with its members, the roles of manuscripts, and Tenancy's read functions beside them.</summary>
/// <param name="options">The context's options.</param>
/// <remarks>Marked as Tenancy's context is, so the build writes its design-time factory too.</remarks>
[SupabaseMigrations]
public sealed class PressContext(DbContextOptions<PressContext> options) : DbContext(options)
{
    /// <summary>The manuscripts.</summary>
    public DbSet<Manuscript> Manuscripts => Set<Manuscript>();

    /// <summary>The roles of manuscripts.</summary>
    public DbSet<ManuscriptRole> ManuscriptRoles => Set<ManuscriptRole>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(TenancyContext.Schema);

        modelBuilder.Entity<Manuscript>(manuscript =>
        {
            manuscript.ScopeToTenant(row => row.TenantId);
            manuscript.HasMembers(row => row.Editors, row => row.OwnerSeatId, at: row => row.UnitId);
        });

        modelBuilder.Entity<ManuscriptRole>(role =>
        {
            role.IsKeptRole();
            role.ScopeToTenant(row => row.TenantId);
            role.HasIndex(row => new { row.TenantId, row.Name }).IsUnique();
        });
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddPressConverters();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }
}
