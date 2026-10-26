namespace Examples.Tenancy.Projects.Application.ProjectRoles;

/// <summary>
/// What every query that lists project roles answers: the roles as they were read, each with its permissions for a caller
/// who holds <see cref="ProjectRoleListing.ManageRolesPermission"/> for the whole tenant and without them for anybody else.
/// </summary>
internal static class ProjectRoleListings
{
    /// <summary><paramref name="roles"/>, each with its permissions only when <paramref name="withPermissions"/>.</summary>
    /// <param name="roles">The roles, as the query read them.</param>
    /// <param name="withPermissions">Whether the caller holds <see cref="ProjectRoleListing.ManageRolesPermission"/> for the whole tenant.</param>
    public static IReadOnlyList<ProjectRoleListing> Answered(IReadOnlyList<ProjectRoleListing> roles, bool withPermissions)
        => withPermissions ? roles : [.. roles.Select(role => role with { Permissions = null })];
}
