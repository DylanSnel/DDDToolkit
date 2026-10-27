using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.TestHost;

/// <summary>
/// The application's part of the catalogue: its keys and its starter roles. Widgets stand in for whatever the
/// application's own module works on.
/// </summary>
public static class HostCatalogue
{
    /// <summary>See widgets.</summary>
    public const string WidgetRead = "widget.read";

    /// <summary>Change widgets; implies seeing them.</summary>
    public const string WidgetChange = "widget.change";

    /// <summary>Create widgets.</summary>
    public const string WidgetCreate = "widget.create";

    /// <summary>The administrators' starter role, for every shape.</summary>
    public const string AdministratorStarterRole = "host-admin";

    /// <summary>Runs a part of the organization; hierarchical tenants only.</summary>
    public const string SupervisorStarterRole = "supervisor";

    /// <summary>Works with widgets.</summary>
    public const string OperatorStarterRole = "operator";

    /// <summary>Looks at widgets.</summary>
    public const string WatcherStarterRole = "watcher";

    /// <summary>The application's own keys.</summary>
    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        new(WidgetRead, "Widgets", "See widgets", Order: 10),
        new(WidgetChange, "Widgets", "Change widgets", Implies: [WidgetRead], Order: 20),
        new(WidgetCreate, "Widgets", "Create widgets", Order: 30),
    ];

    /// <summary>What the application passes as <c>TenancyOptions.Catalogue</c>.</summary>
    public static ApplicationCatalogue Application { get; } = new(
        StarterRoles:
        [
            new(AdministratorStarterRole, "Administrator", "Runs the tenant", [], Administers: true, Order: 10),
            new(SupervisorStarterRole, "Supervisor", "Runs a part of the organization",
                [WidgetChange, WidgetCreate, TenancyPermissions.UnitsManage, TenancyPermissions.SeatsManage, TenancyPermissions.GrantsManage],
                SeededFor: TenantShape.Hierarchical, Order: 20),
            new(OperatorStarterRole, "Operator", "Works with widgets", [WidgetChange, WidgetCreate], Order: 30),
            new(WatcherStarterRole, "Watcher", "Looks at widgets", [WidgetRead], Order: 40),
        ],
        Permissions: Permissions);
}
