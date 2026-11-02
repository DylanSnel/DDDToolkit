namespace DDDToolkit.Supporting.Tenancy;

/// <summary>What one <see cref="ITenantStarterRoles"/> did in one tenant.</summary>
/// <param name="RolesMade">How many roles it made from a starter role the tenant lacked.</param>
/// <param name="RolesChanged">How many roles gained or lost a permission.</param>
/// <param name="RolesWithoutTheirStarterRole">How many roles were made from a starter role that is no longer declared, and kept their permissions.</param>
public sealed record TenantStarterRolesFollowed(int RolesMade, int RolesChanged, int RolesWithoutTheirStarterRole);
