using DDDToolkit.Supporting.Membership;
using Examples.Tenancy.Projects.Application.ProjectRoles;
using Examples.Tenancy.Projects.Application.ProjectRoles.Queries;
using GreenDonut;
using HotChocolate;
using HotChocolate.Resolvers;
using Mediator;

namespace Examples.Tenancy.Projects.Api.ProjectRoles.GraphQL;

/// <summary>
/// The project roles that the fields of one request name by id, read together: a crew member's roles and the
/// caller's own on a project, for every member and every project of an answer at once.
/// </summary>
internal static class ProjectRolesDataLoaders
{
    /// <summary>The fields of a project role that are read from its row: all of them but its id.</summary>
    private static readonly HashSet<string> ReadFromTheRow = new(StringComparer.Ordinal) { "name", "description", "madeFrom", "status", "keys" };

    /// <summary>
    /// The project roles a field names by id, such as a crew's, as that field answers them: read through
    /// <paramref name="roles"/>, once for the request, when the request asks anything of a role but its id, and a
    /// role that is not the tenant's left out; otherwise answered by the ids alone, and nothing is read, since the
    /// one thing asked is the id the field holds already.
    /// </summary>
    /// <param name="context">The field being resolved, which says what the request asks of each role.</param>
    /// <param name="ids">The roles the field names.</param>
    /// <param name="roles">The loader that reads them.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    internal static async Task<IReadOnlyList<ProjectRoleListing>> NamedAsync(
        IResolverContext context,
        IReadOnlyList<ProjectRoleId> ids,
        IProjectRoleByIdDataLoader roles,
        CancellationToken cancellationToken)
        => context.IsSelected(ReadFromTheRow)
            ? [.. (await roles.LoadAsync(ids, cancellationToken)).OfType<ProjectRoleListing>()]
            : [.. ids.Select(id => new ProjectRoleListing(id, string.Empty, string.Empty, MadeFrom: null, KeptRoleStatus.Active, Keys: null))];

    /// <summary>
    /// The project roles with these ids, of the caller's tenant, as <see cref="ProjectRolesById"/> answers them: in
    /// parts of as many as one question is about. A role that is not the tenant's is not in the answer.
    /// </summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<ProjectRoleId, ProjectRoleListing>> GetProjectRoleByIdAsync(
        IReadOnlyList<ProjectRoleId> ids,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var roles = new Dictionary<ProjectRoleId, ProjectRoleListing>();
        foreach (var part in ids.Chunk(ProjectRolesById.MostRoles))
        {
            foreach (var role in await sender.Send(new ProjectRolesById(part), cancellationToken))
            {
                roles[role.Id] = role;
            }
        }

        return roles;
    }
}
