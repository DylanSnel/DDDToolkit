using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Supporting.Membership.EntityFramework;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using Examples.Tenancy.Projects.Infrastructure.Converters;
using Microsoft.EntityFrameworkCore;

namespace Examples.Tenancy.Projects.Infrastructure.Persistence;

/// <summary>
/// Projects' own database: the projects, their crews, the roles each crew member holds, and the tenants' project
/// roles. Next to them it maps Tenancy's read model, so the access questions are asked inside this context's
/// queries.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>The read model.</b> The rows the access questions read (the rights, the closure of the
/// organization's tree, units, seats, roles, placements) come from Tenancy's read functions, which
/// <c>AddTenancyReadFunctions</c> maps them to: the database shows a seat only its own rights, and a module
/// reads no table of Tenancy's, only what Tenancy offers it. The questions are asked over them as over tables,
/// and stay one statement. Tenancy's access file makes the functions and Entity Framework creates none, so this
/// context's migrations never touch them.</item>
/// <item><b>The tenant.</b> <c>ScopeToTenant</c> puts the tenant filter on <see cref="Project"/> and on
/// <see cref="ProjectRole"/>, next to any filter of the module's own, and marks them for the save check of
/// <c>UseTenancy</c>: a project or a project role of another tenant is neither read nor written, whoever loaded or
/// made it. The crew is kept to its project's tenant through the project that owns it.</item>
/// <item><b>Instants.</b> Every <see cref="DateTimeOffset"/> is stored as its UTC instant, whatever offset it
/// was written with, so a crew member's period and a crew role's are compared in SQL, and Tenancy's grant
/// periods in the read model the same way.</item>
/// <item><b>The crew.</b> The domain project references no Entity Framework, so nothing on its classes says how
/// they are stored. The crew is the Membership package's, and <c>HasMembers</c> maps it, under the tables and the
/// column it has always had: the members owned by their project, a member's roles owned by the member, each in a
/// table of its own, and the unit a project hangs at as where a key held above reaches it. <c>IsKeptRole</c>
/// maps the package's part of a project role; whose a role is, and that a name is used once in a tenant, are
/// said here.</item>
/// </list>
/// Table names carry the module's name where Tenancy's could be meant: a crew member's table says whose it is
/// without its schema.
/// </remarks>
public sealed class ProjectsContext(DbContextOptions<ProjectsContext> options) : DbContext(options)
{
    /// <summary>The schema Projects' tables live in, with the module's migration history.</summary>
    public const string Schema = "projects";

    /// <summary>
    /// The schema Tenancy's tables and functions live in, which the read model points at. The Tenants module's
    /// own context uses the same name; this module does not reference that module, so it says it again.
    /// </summary>
    public const string TenancyTablesSchema = "tenancy";

    /// <summary>The projects' table.</summary>
    public const string ProjectsTable = "Projects";

    /// <summary>The crews' table: a row for each seat on a project's crew, with the period it is on it for.</summary>
    public const string CrewTable = "ProjectCrewMembers";

    /// <summary>The table of the roles crew members hold: a row for each role of each member, with the period it is held for.</summary>
    public const string CrewRolesTable = "ProjectCrewRoleGrants";

    /// <summary>The column of <see cref="CrewTable"/> that holds the seat on the crew.</summary>
    public const string CrewSeatColumn = "SeatId";

    /// <summary>The tenants' project roles' table.</summary>
    public const string ProjectRolesTable = "ProjectRoles";

    /// <summary>The column that holds the first day of a project's planned range, empty for a project that has none.</summary>
    public const string PlannedFromColumn = "PlannedFrom";

    /// <summary>The column that holds the last day of a project's planned range.</summary>
    public const string PlannedUntilColumn = "PlannedUntil";

    /// <summary>The outbox table, named after the module, so a table says whose it is without its schema.</summary>
    public const string OutboxTable = "ProjectsOutboxMessages";

    public DbSet<Project> Projects => Set<Project>();

    /// <summary>The project roles of the caller's tenant.</summary>
    public DbSet<ProjectRole> ProjectRoles => Set<ProjectRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Tenancy's rows, read in this context's queries: asked over them, UnitsWhereIHold(key) becomes a
        // subquery of the project statement rather than a list fetched first (see EfProjectReads). They come from
        // Tenancy's read functions and from none of its tables: what a module may read of Tenancy is what Tenancy
        // offers it.
        modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(TenancyTablesSchema);

        modelBuilder.Entity<Project>(project =>
        {
            project.ToTable(ProjectsTable);
            project.ScopeToTenant(row => row.TenantId);

            // Who opened a project and who changed it last: the seat, an operator or the system, as ids. Tenancy's
            // save check fills the columns, and a trigger of the database holds a caller to its own seat in them.
            project.RecordsWhoChanged<Project, SeatId>();

            // A number is unique in its tenant. The command checks first and refuses by code; the index refuses two
            // opened at the same moment, and answers with that same code and text.
            project.HasIndex(row => new { row.TenantId, row.Number })
                .IsUnique()
                .RefusesAs(ProjectRefusals.NumberTaken, ProjectRefusals.TemplateOf(ProjectRefusals.NumberTaken));

            // "Every project at these units" is the question every list asks.
            project.HasIndex(row => row.UnitId);

            // A project keeps the number it was opened under: no command changes it, a save that would is refused,
            // and the privileges the export writes leave the column out of what may be updated.
            project.Property(row => row.Number).HasMaxLength(Project.LongestNumber).IsFixedAfterInsert();
            project.Property(row => row.Name).HasMaxLength(Project.LongestName);
            project.Property(row => row.State).HasConversion<string>().HasMaxLength(16);

            // The planned range is a value object the modules share, declared without Entity Framework, so how it
            // is stored is said here: inline, as two columns of the project's row, both empty when there is none.
            project.ComplexProperty(row => row.Planned, planned =>
            {
                planned.Property(range => range.From).HasColumnName(PlannedFromColumn);
                planned.Property(range => range.Until).HasColumnName(PlannedUntilColumn);
            });

            // The crew, as the Membership package keeps it: who is on it for which period, the roles each holds there,
            // and the owner among them. The unit is where a key held in the organization reaches the project from.
            // The tables and the seat's column keep the names they have always had.
            project.HasMembers(row => row.Crew, row => row.OwnerSeatId, at: row => row.UnitId, new MemberTableNames(CrewTable, CrewRolesTable, MemberColumn: CrewSeatColumn));
        });

        modelBuilder.Entity<ProjectRole>(role =>
        {
            role.ToTable(ProjectRolesTable);

            // The package's part of a role: its name, what it is for, its keys, its status, and the starter role it came from.
            role.IsKeptRole();

            // This module's: whose a role is. A name is used once in a tenant, and a tenant has each starter role
            // once, which also keeps two set-ups of one tenant at the same moment apart.
            role.ScopeToTenant(row => row.TenantId);
            role.HasIndex(row => new { row.TenantId, row.Name })
                .IsUnique()
                .RefusesAs(ProjectRefusals.RoleNameTaken, ProjectRefusals.TemplateOf(ProjectRefusals.RoleNameTaken));
            role.HasIndex(row => new { row.TenantId, row.MadeFrom }).IsUnique();
        });

        // Projects raises domain events, stored with the change that raised them.
        modelBuilder.AddDomainEventOutbox(Database, tableName: OutboxTable, schema: Schema);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The same conventions in every context: read-only collections, [Internal] members, Version.
        configurationBuilder.AddDDDToolkitConventions();

        // Every instant as UTC, whatever offset it was written with: crew and grant periods are compared in SQL,
        // and the driver takes no other offset for a timestamp with time zone.
        configurationBuilder.StoreDateTimeOffsetsAsUtc();

        // The ids this context stores: the project's, the crew member's and the project role's, which this module's
        // contracts and domain projects declare without Entity Framework, and Tenancy's published ones. The
        // generated registration of this project stores them all, through SingleValueConverter.
        configurationBuilder.AddProjectsConverters();
    }
}
