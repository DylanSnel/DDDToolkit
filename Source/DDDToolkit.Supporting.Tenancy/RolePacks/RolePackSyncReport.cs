namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// What a sync of the role packs did (<see cref="IRolePackSync"/>), counted over every tenant it visited. Which
/// role gained or lost which key is on the events it raised, one per role that changed.
/// </summary>
/// <param name="Tenants">How many tenants it visited.</param>
/// <param name="RolesChanged">How many roles gained or lost a key, each with its event.</param>
/// <param name="RolesKeptForAnAdministrator">
/// How many roles it left as they were, because following their pack would leave a tenant without an
/// administrator.
/// </param>
/// <param name="RolesWithoutTheirPack">How many roles were made from a pack the catalogue no longer has, and kept their keys.</param>
/// <param name="Failed">Each tenant it could not sync, by its id, with what went wrong; the next run tries it again.</param>
public sealed record RolePackSyncReport(
    int Tenants,
    int RolesChanged,
    int RolesKeptForAnAdministrator,
    int RolesWithoutTheirPack,
    IReadOnlyList<(string Tenant, Exception Error)> Failed)
{
    /// <summary>Whether every tenant was synced.</summary>
    public bool Succeeded => Failed.Count == 0;
}
