using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.Postgres.Tests.Converters;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>A widget's id.</summary>
[EntityId<Guid>]
public readonly partial record struct WidgetId;

/// <summary>
/// What a module of the application keeps in a tenant, at a unit: it stands for a project, an order, anything a
/// module owns and asks Tenancy about. Its parts are an entity table under it, which is kept to the tenant through it.
/// </summary>
[AggregateRoot<WidgetId>]
public sealed partial class Widget
{
    /// <summary>Creates a widget.</summary>
    public Widget(WidgetId id, TenantId tenantId, OrganizationUnitId unitId, string name)
        : base(id)
    {
        TenantId = tenantId;
        UnitId = unitId;
        Name = name;
    }

    /// <summary>The tenant it belongs to.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The unit it hangs at.</summary>
    public OrganizationUnitId UnitId { get; private set; }

    /// <summary>Its name.</summary>
    public string Name { get; private set; }

    /// <summary>What it is made of.</summary>
    public partial IReadOnlyList<WidgetPart> Parts { get; }

    /// <summary>Adds a part.</summary>
    public WidgetPart AddPart(string name)
    {
        var part = new WidgetPart(WidgetPartId.CreateSequential(), name);
        _parts.Add(part);
        return part;
    }

    /// <summary>Renames the widget.</summary>
    public void Rename(string name) => Name = name;
}

/// <summary>A part of a widget.</summary>
[Entity<Guid>]
public sealed partial class WidgetPart
{
    /// <summary>Creates a part.</summary>
    public WidgetPart(WidgetPartId id, string name)
        : base(id)
        => Name = name;

    /// <summary>What the part is called.</summary>
    public string Name { get; private set; }
}

/// <summary>A seat reads the widgets at the units where it holds the key to read them, and below.</summary>
[RowAccess<Widget>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class WidgetsAreReadWhereTheKeyIsHeld
{
    public static bool Allows(Widget widget, Caller caller)
        => TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(HostCatalogue.WidgetRead).Contains(widget.UnitId);
}

/// <summary>A seat adds, changes and removes the widgets at the units where it holds the key to change them.</summary>
[RowAccess<Widget>(RowOperations.Create | RowOperations.Change | RowOperations.Remove, To = [RowAccessRoles.User])]
public static partial class WidgetsAreChangedWhereTheKeyIsHeld
{
    public static bool Allows(Widget widget, Caller caller)
        => TenancyRowAccess.UnitsWhereIHold<OrganizationUnitId>(HostCatalogue.WidgetChange).Contains(widget.UnitId);
}

/// <summary>
/// Whether the caller may read a widget of a tenant given as an argument: what a policy asks where the
/// application's connection is not the one asking and names no tenant, on the path of a widget's stored file say,
/// with the tenant and the widget the path names. The module's own function, built on Tenancy's question that
/// takes the tenant, which is asked once for the statement with the function's own parameter.
/// </summary>
[AccessFunction<Widget>("widgets.readable_in_tenant")]
public static partial class WidgetIsReadableInTenant
{
    public static bool Allows(Widget widget, Caller caller, TenantId tenant)
        => widget.TenantId == tenant
           && TenancyRowAccess.UnitsWhereIHoldInTenant<OrganizationUnitId, TenantId>(tenant, HostCatalogue.WidgetRead).Contains(widget.UnitId);
}

/// <summary>
/// Whether a widget of a tenant given as an argument is one the caller reads as the seat and through the role it
/// names: the module's own function over the other four of Tenancy's questions that take the tenant, so each of
/// them is declared in C#, exported and asked of the database. True when the caller has an active seat in the
/// tenant, that seat is the one named and holds the key to read widgets there, and the role named is one of the
/// tenant's that grants it.
/// </summary>
[AccessFunction<Widget>("widgets.read_as_in_tenant")]
public static partial class WidgetIsReadAsInTenant
{
    public static bool Allows(Widget widget, Caller caller, TenantId tenant, SeatId seat, RoleId role)
        => widget.TenantId == tenant
           && TenancyRowAccess.SeatedInTenant(tenant)
           && TenancyRowAccess.HoldsKeyInTenant(tenant, HostCatalogue.WidgetRead)
           && TenancyRowAccess.SeatInTenant<SeatId, TenantId>(tenant) == seat
           && TenancyRowAccess.RolesWithKeyInTenant<RoleId, TenantId>(tenant, HostCatalogue.WidgetRead).Contains(role);
}

/// <summary>The widgets' rules, as a script is written from them.</summary>
public static class WidgetRules
{
    public static readonly RowAccessRule Read = RowAccessRule.For<Widget>(
        "Read where the key is held", RowOperations.Read, WidgetsAreReadWhereTheKeyIsHeld.RowAccessSql, RowAccessRoles.User);

    public static readonly RowAccessRule Change = RowAccessRule.For<Widget>(
        "Changed where the key is held",
        RowOperations.Create | RowOperations.Change | RowOperations.Remove,
        WidgetsAreChangedWhereTheKeyIsHeld.RowAccessSql,
        RowAccessRoles.User);

    public static IReadOnlyList<RowAccessRule> All { get; } = [Read, Change];

    /// <summary>The widgets' function that takes the tenant, which a test that asks it adds to the script.</summary>
    public static readonly RowAccessFunction ReadableInTenant = RowAccessFunction.For<Widget>(
        WidgetIsReadableInTenant.Name,
        WidgetIsReadableInTenant.RowAccessSql,
        owner: null,
        WidgetIsReadableInTenant.RowAccessParameters);

    /// <summary>The widgets' function over the other questions that take the tenant, which a test that asks it adds to the script.</summary>
    public static readonly RowAccessFunction ReadAsInTenant = RowAccessFunction.For<Widget>(
        WidgetIsReadAsInTenant.Name,
        WidgetIsReadAsInTenant.RowAccessSql,
        owner: null,
        WidgetIsReadAsInTenant.RowAccessParameters);
}

/// <summary>
/// The widgets' module context: a schema of its own, its widgets kept to a tenant, and Tenancy's read model as
/// Tenancy's read functions, which a module on Postgres maps to ask Tenancy inside its own queries: its model
/// names no table of Tenancy's. It names none of its own either, so its tables and columns are what Entity
/// Framework makes of its classes, or what a naming convention on its options does.
/// </summary>
public sealed class WidgetContext(DbContextOptions<WidgetContext> options) : DbContext(options)
{
    /// <summary>The widgets' schema.</summary>
    public const string Schema = "widgets";

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(TestHost.Persistence.TestTenancyContext.Schema);

        modelBuilder.Entity<Widget>(widget =>
        {
            widget.Property(row => row.Name).HasMaxLength(100);
            widget.ScopeToTenant(row => row.TenantId);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddTenancyConverters();
        configurationBuilder.AddTenancyPostgresTestsConverters();
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }
}
