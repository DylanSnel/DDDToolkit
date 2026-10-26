namespace Examples.Tenancy.Projects.Application.ProjectRoles;

/// <summary>
/// What every query that lists project roles answers: the roles as they were read, each with its keys for a caller
/// who holds <see cref="ProjectRoleListing.KeysKey"/> for the whole tenant and without them for anybody else.
/// </summary>
internal static class ProjectRoleListings
{
    /// <summary><paramref name="roles"/>, each with its keys only when <paramref name="withKeys"/>.</summary>
    /// <param name="roles">The roles, as the query read them.</param>
    /// <param name="withKeys">Whether the caller holds <see cref="ProjectRoleListing.KeysKey"/> for the whole tenant.</param>
    public static IReadOnlyList<ProjectRoleListing> Answered(IReadOnlyList<ProjectRoleListing> roles, bool withKeys)
        => withKeys ? roles : [.. roles.Select(role => role with { Keys = null })];
}
