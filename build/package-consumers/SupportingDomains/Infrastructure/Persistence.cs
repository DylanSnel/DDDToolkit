using Acme.Press.Infrastructure.Converters;
using Acme.Press.Manuscripts;
using Acme.Press.Tenants;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.Supporting.Membership.Access;
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

        // A manuscript's editors and the roles of manuscripts are mapped by Membership's convention, which
        // AddDDDToolkitConventions() brings: no call of the package's here.
        modelBuilder.Entity<Manuscript>(manuscript => manuscript.ScopeToTenant(row => row.TenantId));

        modelBuilder.Entity<ManuscriptRole>(role =>
        {
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

/// <summary>
/// What the press reads of its manuscripts for the caller, as Membership answers it: the manuscripts it may read, asked
/// of their own set, and how it holds a permission on one, asked of the access manager by the id a request carries.
/// </summary>
/// <param name="db">The manuscripts' context.</param>
/// <param name="access">Membership's access manager of manuscripts.</param>
public sealed class ManuscriptReads(PressContext db, MembershipAccessManager<Manuscript> access)
{
    /// <summary>The titles of the manuscripts the caller may read, and the permissions it holds on each of them that it may edit or retract.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    public async Task<(IReadOnlyList<string> Titles, IReadOnlyList<(ManuscriptId Manuscript, string Permission)> Held)> ReadableAsync(CancellationToken cancellationToken)
    {
        var titles = await db.Manuscripts.WhereIHold(ManuscriptPermissions.Read).OrderBy(manuscript => manuscript.Title).Select(manuscript => manuscript.Title).ToListAsync(cancellationToken);
        var held = await db.Manuscripts.WhereIHold([ManuscriptPermissions.Edit, ManuscriptPermissions.Retract])
            .Select(pair => new { pair.Resource.Id, pair.Permission })
            .ToListAsync(cancellationToken);

        return (titles, [.. held.Select(pair => (pair.Id, pair.Permission))]);
    }

    /// <summary>How the caller holds the permission to edit <paramref name="manuscript"/>, or <see langword="null"/> where it does not.</summary>
    /// <param name="manuscript">The manuscript, by the id a request carries.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public Task<MemberVia?> EditsAsync(ManuscriptId manuscript, CancellationToken cancellationToken)
        => access.ViaAsync(manuscript, ManuscriptPermissions.Edit, cancellationToken);
}
