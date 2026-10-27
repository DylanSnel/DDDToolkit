namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// What a sync of the starter roles did (<see cref="IStarterRoleSync"/>), counted over every tenant it visited. Which
/// role gained or lost which permission is on the events it raised, one per role that changed.
/// </summary>
/// <param name="Tenants">How many tenants it visited.</param>
/// <param name="RolesChanged">How many roles gained or lost a permission, each with its event.</param>
/// <param name="RolesKeptForAnAdministrator">
/// How many roles it left as they were, because following their starter role would leave a tenant without an
/// administrator.
/// </param>
/// <param name="RolesWithoutTheirStarterRole">How many roles were made from a starter role the catalogue no longer has, and kept their permissions.</param>
/// <param name="Failed">Each tenant it could not sync, by its id, with what went wrong; the next run tries it again.</param>
public sealed record StarterRoleSyncReport(
    int Tenants,
    int RolesChanged,
    int RolesKeptForAnAdministrator,
    int RolesWithoutTheirStarterRole,
    IReadOnlyList<(string Tenant, Exception Error)> Failed)
{
    /// <summary>Whether every tenant was synced.</summary>
    public bool Succeeded => Failed.Count == 0;
}
