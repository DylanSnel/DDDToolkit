using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace Examples.Tenancy.Projects.Infrastructure.Access;

/// <summary>
/// A seat makes and changes the project roles of its tenant where it holds the key that manages the tenant's roles
/// for the whole tenant: what the commands that make, rename, re-key and archive a project role ask. Nobody removes
/// a project role: it is archived, and stays on the crews that hold it. That a row is one of the calling seat's
/// tenant is Tenancy's own policy on the table, which keeps every row of a table scoped to the tenant to the
/// caller's; this rule asks the one thing more.
/// </summary>
[RowAccess<ProjectRole>(RowOperations.Create | RowOperations.Change, To = [RowAccessRoles.User])]
public static partial class RoleManagersChangeTheProjectRoles
{
    /// <summary>Whether the seat holds the key that manages the tenant's roles at its root.</summary>
    public static bool Allows(ProjectRole role, Caller caller) => TenancyRowAccess.HoldsTenantWide(TenancyKeys.RolesManage);
}
