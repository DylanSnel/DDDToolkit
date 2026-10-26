using Examples.Tenancy.Projects.Api.Crew.GraphQL;
using Examples.Tenancy.Projects.Application.Access.Queries;
using GreenDonut;
using Mediator;

namespace Examples.Tenancy.Projects.Api.Access.GraphQL;

/// <summary>
/// What a rule on a field asks through: HotChocolate writes a data loader for the method, which gathers the
/// projects the ruled fields of one request belong to and sends for all of them at once.
/// </summary>
internal static class AccessDataLoaders
{
    /// <summary>
    /// The permissions a field of this schema asks for (<see cref="CrewFieldPermissions.Keys"/>) that the caller holds on each
    /// project, so the projects of one batch, as a rule a page, cost the rules one question. A project the caller
    /// does not see, or holds none of the permissions on, is missing from the answer, and whoever asks about it is
    /// refused.
    /// </summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<ProjectId, ProjectPermissionSet>> GetHeldPermissionsByProjectIdAsync(
        IReadOnlyList<ProjectId> projects,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var held = new Dictionary<ProjectId, ProjectPermissionSet>();
        foreach (var part in projects.Chunk(PermissionsOnProjects.MostProjects))
        {
            foreach (var set in (await sender.Send(new PermissionsOnProjects(part, CrewFieldPermissions.Permissions), cancellationToken)).OnProjects)
            {
                held[set.Project] = set;
            }
        }

        return held;
    }
}
