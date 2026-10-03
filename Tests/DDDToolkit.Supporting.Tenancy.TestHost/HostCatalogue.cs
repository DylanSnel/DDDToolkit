using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.TestHost;

/// <summary>
/// The application's part of the catalogue: its keys, its packs and its unit kinds. Widgets stand in for
/// whatever the application's own module works on.
/// </summary>
public static class HostCatalogue
{
    /// <summary>See widgets.</summary>
    public const string WidgetRead = "widget.read";

    /// <summary>Change widgets; implies seeing them.</summary>
    public const string WidgetChange = "widget.change";

    /// <summary>Create widgets.</summary>
    public const string WidgetCreate = "widget.create";

    /// <summary>The administrators' pack, for every shape.</summary>
    public const string AdministratorPack = "host-admin";

    /// <summary>Runs a part of the organization; hierarchical tenants only.</summary>
    public const string SupervisorPack = "supervisor";

    /// <summary>Works with widgets.</summary>
    public const string OperatorPack = "operator";

    /// <summary>Looks at widgets.</summary>
    public const string WatcherPack = "watcher";

    /// <summary>The application's own keys.</summary>
    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        new(WidgetRead, "Widgets", "See widgets", Order: 10),
        new(WidgetChange, "Widgets", "Change widgets", Implies: [WidgetRead], Order: 20),
        new(WidgetCreate, "Widgets", "Create widgets", Order: 30),
    ];

    /// <summary>What the application passes as <c>TenancyOptions.Catalogue</c>.</summary>
    public static ApplicationCatalogue Application { get; } = new(
        Packs:
        [
            new(AdministratorPack, "Administrator", "Runs the tenant", [], Administers: true, Order: 10),
            new(SupervisorPack, "Supervisor", "Runs a part of the organization",
                [WidgetChange, WidgetCreate, TenancyKeys.UnitsManage, TenancyKeys.SeatsManage, TenancyKeys.GrantsManage],
                Shape: TenantShape.Hierarchical, Order: 20),
            new(OperatorPack, "Operator", "Works with widgets", [WidgetChange, WidgetCreate], Order: 30),
            new(WatcherPack, "Watcher", "Looks at widgets", [WidgetRead], Order: 40),
        ],
        UnitKinds: [new("company", "Company", 10), new("region", "Region", 20), new("site", "Site", 30)],
        Permissions: Permissions);
}
