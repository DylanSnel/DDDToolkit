namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>
/// Tenancy's own permission keys. They are always in the catalogue, and no other module may declare a key
/// under <c>tenancy.</c>.
/// <para>
/// A key held at the root holds for the whole tenant; a key held at a unit holds for that unit and every unit
/// below it. The keys that manage the tenant itself (settings, adding seats, roles) are asked for at the root.
/// </para>
/// <para>
/// Every one of them but <see cref="HistoryView"/> manages access: a role holding one is given only by a seat that
/// holds it there. Reading what happened changes nobody's rights, so that key is given like any other.
/// </para>
/// </summary>
public static class TenancyKeys
{
    /// <summary>The tenant's own settings: its name, and its shape. Asked for tenant-wide.</summary>
    public const string SettingsManage = "tenancy.settings.manage";

    /// <summary>
    /// Change the tree below a unit: new units, names, parents and archiving. Asked for at a unit, and for a move
    /// at both parents. A move gives the mover no key it lacks at the unit, and none for longer than it holds it
    /// there, and gives or takes away from anyone a key that manages access only when the mover holds it at the
    /// unit for at least as long as that grant runs.
    /// </summary>
    public const string UnitsManage = "tenancy.units.manage";

    /// <summary>
    /// Add seats and suspend, reactivate or deactivate them, asked for tenant-wide; place a seat in a unit,
    /// withdraw it or make a placement primary, asked for at the unit. A seat that holds a role that manages
    /// access is suspended, reactivated or deactivated only by a seat that also holds that role's keys that do,
    /// where and for as long as the seat holds them.
    /// </summary>
    public const string SeatsManage = "tenancy.seats.manage";

    /// <summary>
    /// Give seats organization roles at a unit and take them away. Asked for at the unit. Holding it is enough to
    /// give roles: its holder gives any role that manages no access to a seat placed there or below without
    /// holding the role's keys, and to itself for no longer than it holds this key. A role that manages access it
    /// gives only while holding there each of its keys that do, and never to itself.
    /// </summary>
    public const string GrantsManage = "tenancy.grants.manage";

    /// <summary>The tenant's roles: new ones, their names, their keys and archiving. Asked for tenant-wide.</summary>
    public const string RolesManage = "tenancy.roles.manage";

    /// <summary>
    /// Read the tenant's access history: which seat, role, grant or unit changed, when, and who changed it. Asked
    /// for tenant-wide. It manages no access, so a role that holds it is given like any other; an administrators'
    /// pack that lists its keys holds it, as it holds every key of Tenancy's.
    /// </summary>
    public const string HistoryView = "tenancy.history.view";

    /// <summary>
    /// The key that makes a seat an administrator: whoever manages roles can give themselves any key, so that
    /// is the key the last-administrator rule counts.
    /// </summary>
    public const string AdministratorKey = RolesManage;

    /// <summary>The module Tenancy's keys are listed under.</summary>
    public const string Module = "Tenancy";

    /// <summary>Tenancy's keys, as the catalogue lists them.</summary>
    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        new(SettingsManage, Module, "Change the organization's name and turn a flat tenant into a tree", Order: 10, ManagesAccess: true),
        new(UnitsManage, Module, "Create units below a unit, and rename, re-parent or archive them", Order: 20, ManagesAccess: true),
        new(SeatsManage, Module, "Add people to the tenant, and place, withdraw, suspend, reactivate or deactivate their seats", Order: 30, ManagesAccess: true),
        new(GrantsManage, Module, "Give seats organization roles at a unit, and take them away", Order: 40, ManagesAccess: true),
        new(RolesManage, Module, "Make roles, and change their names, keys and status", Order: 50, ManagesAccess: true),
        new(HistoryView, Module, "Read who changed the tenant's seats, roles, grants and units, and when", Order: 60),
    ];
}
