using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Supporting.Tenancy.Access;

namespace Examples.Tenancy.Projects.Infrastructure.Access;

// What the database itself lets a seat do with the project roles. The Membership package gives the roles' table no
// policy: which rows a caller reads and writes is the application's to say, as for any aggregate. A role's row
// decides what everybody who holds it may do on a crew, so the database holds who may change it as well as the
// commands do. Tenancy's own policies keep every row to its tenant beside these.

/// <summary>A seat reads the project roles of its own tenant: a crew's roles are shown by their names.</summary>
[RowAccess<ProjectRole>(RowOperations.Read, To = [RowAccessRoles.User])]
public static partial class SeatsReadTheProjectRolesOfTheirTenant
{
    /// <summary>Whether <paramref name="role"/> is one of the calling seat's tenant.</summary>
    public static bool Allows(ProjectRole role, Caller caller) => role.TenantId == TenancyRowAccess.CallerTenant<TenantId>();
}
