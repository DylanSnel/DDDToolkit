using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A module's entity that belongs to a tenant and hangs at a unit, with a soft delete of its own. It stands
/// for whatever a consumer of Tenancy keeps, a project say.
/// </summary>
public sealed class Widget
{
    public Guid Id { get; set; }

    public TenantId TenantId { get; set; }

    public OrganizationUnitId UnitId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The role the widget is handed to, if any: who holds it may work on it, wherever they are placed.</summary>
    public RoleId? HandedTo { get; set; }

    public bool IsDeleted { get; set; }
}

/// <summary>
/// A consumer of Tenancy: a module's context in a schema of its own, which maps Tenancy's read model over
/// Tenancy's tables, keeps its widgets to a tenant, has a named filter of its own on them, and keeps on each
/// widget who wrote it first and who changed it last.
/// </summary>
public sealed class TestWidgetContext(DbContextOptions<TestWidgetContext> options) : DbContext(options)
{
    /// <summary>The widgets' schema.</summary>
    public const string Schema = "widgets";

    /// <summary>The widgets' own filter, next to the tenant filter.</summary>
    public const string SoftDelete = "soft-delete";

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);

        modelBuilder.Entity<Widget>(widget =>
        {
            widget.ToTable("Widgets");
            widget.HasKey(row => row.Id);
            widget.Property(row => row.Id).ValueGeneratedNever();
            widget.Property(row => row.Name).HasMaxLength(100);
            widget.ScopeToTenant(row => row.TenantId);
            widget.HasQueryFilter(SoftDelete, row => !row.IsDeleted);

            // Who wrote a widget first and who changed it last, on the row. A module of its own names the seat's id.
            widget.RecordsWhoChanged<Widget, SeatId>();
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddTenancyConverters();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }
}
