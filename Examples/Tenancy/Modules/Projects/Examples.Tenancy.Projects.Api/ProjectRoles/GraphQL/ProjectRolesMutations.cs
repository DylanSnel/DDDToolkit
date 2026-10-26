using Examples.Tenancy.Projects.Application.ProjectRoles;
using Examples.Tenancy.Projects.Application.ProjectRoles.Commands;
using Examples.Tenancy.Projects.Application.ProjectRoles.Queries;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Projects.Api.ProjectRoles.GraphQL;

/// <summary>
/// What changes the tenant's project roles. A mutation sends the command its route sends, then reads the role and
/// answers that; a refusal is not caught here, and arrives in the payload's <c>errors</c>.
/// </summary>
internal static class ProjectRolesMutations
{
    /// <summary>Makes a project role of the tenant's own.</summary>
    [Mutation]
    public static async Task<ProjectRoleListing?> ProjectRoleCreateAsync(string name, string? description, IReadOnlyList<string>? keys, [Service] ISender sender, CancellationToken cancellationToken)
    {
        var id = await sender.Send(new MakeProjectRole(name, description, keys ?? []), cancellationToken);
        return await RoleNowAsync(sender, id, cancellationToken);
    }

    /// <summary>Gives a project role another name and description, both as they are to be.</summary>
    [Mutation]
    public static async Task<ProjectRoleListing?> ProjectRoleRenameAsync(ProjectRoleId id, string name, string? description, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new RenameProjectRole(id, name, description), cancellationToken);
        return await RoleNowAsync(sender, id, cancellationToken);
    }

    /// <summary>Has a project role give exactly these keys from now on.</summary>
    [Mutation]
    public static async Task<ProjectRoleListing?> ProjectRoleKeysSetAsync(ProjectRoleId id, IReadOnlyList<string> keys, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new SetProjectRoleKeys(id, keys), cancellationToken);
        return await RoleNowAsync(sender, id, cancellationToken);
    }

    /// <summary>Archives a project role: it gives nothing from then on, and goes to nobody.</summary>
    [Mutation]
    public static async Task<ProjectRoleListing?> ProjectRoleArchiveAsync(ProjectRoleId id, [Service] ISender sender, CancellationToken cancellationToken)
    {
        await sender.Send(new ArchiveProjectRole(id), cancellationToken);
        return await RoleNowAsync(sender, id, cancellationToken);
    }

    /// <summary>The role as it is after the change, as the same caller reads it.</summary>
    private static async Task<ProjectRoleListing?> RoleNowAsync(ISender sender, ProjectRoleId id, CancellationToken cancellationToken)
        => (await sender.Send(new ProjectRolesById([id]), cancellationToken)).SingleOrDefault();
}
