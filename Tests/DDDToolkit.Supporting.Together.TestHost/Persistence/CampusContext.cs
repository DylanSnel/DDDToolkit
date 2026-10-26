using Campus.Converters;
using Campus.Courses;
using Campus.Labs;
using Campus.Tenants;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Campus.Persistence;

/// <summary>
/// The courses' and the labs' context: both kinds of resource with their members, the roles of courses, and
/// Tenancy's read model beside them, so what a caller holds in the organization is asked inside this
/// context's own statements.
/// <list type="bullet">
/// <item><b>The read model.</b> On Postgres the rows Tenancy's questions read come from Tenancy's read
/// functions, since the database shows a seat only its own rights there; on any other database they are views
/// over Tenancy's tables, which are in the same database. A model is built once for the provider its context
/// runs on, so the choice is the same for every instance.</item>
/// <item><b>The tenant.</b> A course, a lab and a role of courses are each a college's, and kept to it by
/// Tenancy's own rule: neither is read or written from another tenant. Membership knows nothing of it.</item>
/// <item><b>Where a resource sits.</b> Said with its members, <c>at:</c>: the unit a key held in the
/// organization is compared with.</item>
/// </list>
/// </summary>
/// <param name="options">The context's options.</param>
public sealed class CampusContext(DbContextOptions<CampusContext> options) : DbContext(options)
{
    /// <summary>The schema the tables are in.</summary>
    public const string Schema = "campus";

    /// <summary>The courses.</summary>
    public DbSet<Course> Courses => Set<Course>();

    /// <summary>The roles of courses, of the college the request is in.</summary>
    public DbSet<CourseRole> CourseRoles => Set<CourseRole>();

    /// <summary>The labs.</summary>
    public DbSet<Lab> Labs => Set<Lab>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder) => Build(modelBuilder, Database, Schema, TenancyContext.Schema);

    /// <summary>
    /// The model of this context in <paramref name="schema"/>, reading Tenancy's rows from
    /// <paramref name="tenancySchema"/>: what <see cref="OnModelCreating"/> builds in <see cref="Schema"/> and
    /// <see cref="TenancyContext.Schema"/>, and what a host that names the schemas otherwise builds, in ones
    /// Postgres keeps only in double quotes, say.
    /// </summary>
    /// <param name="modelBuilder">The builder of the context's model.</param>
    /// <param name="database">The context's database, whose provider decides where the read model's rows come from.</param>
    /// <param name="schema">The schema the courses' and the labs' tables, and on Postgres their functions, are in.</param>
    /// <param name="tenancySchema">The schema of Tenancy's context, which the read model reads.</param>
    public static void Build(ModelBuilder modelBuilder, DatabaseFacade database, string schema, string tenancySchema)
    {
        modelBuilder.HasDefaultSchema(schema);

        if (database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(tenancySchema);
        }
        else
        {
            modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>(tenancySchema);
        }

        modelBuilder.Entity<Course>(course =>
        {
            course.Property(row => row.Id).ValueGeneratedNever();
            course.Property(row => row.Title).HasMaxLength(100);
            course.ScopeToTenant(row => row.TenantId);
            course.HasIndex(row => row.UnitId);

            // The tutors and the roles they hold, and the unit a key held in the organization is compared with.
            course.HasMembers(row => row.Tutors);
        });

        modelBuilder.Entity<CourseRole>(role =>
        {
            // Membership's part of a role: its name, what it is for, its keys, its status, and the starter role it came from.
            role.IsCustomRole();

            // The application's part: whose it is. A name is used once in a college, and a college has each starter role once.
            role.ScopeToTenant(row => row.TenantId);
            role.HasIndex(row => new { row.TenantId, row.Name }).IsUnique();
            role.HasIndex(row => new { row.TenantId, row.MadeFrom }).IsUnique();
        });

        modelBuilder.Entity<Lab>(lab =>
        {
            lab.Property(row => row.Id).ValueGeneratedNever();
            lab.Property(row => row.Name).HasMaxLength(100);
            lab.ScopeToTenant(row => row.TenantId);
            lab.HasIndex(row => row.UnitId);
            lab.HasMembers(row => row.Technicians);
        });
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddCampusConverters();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }
}
