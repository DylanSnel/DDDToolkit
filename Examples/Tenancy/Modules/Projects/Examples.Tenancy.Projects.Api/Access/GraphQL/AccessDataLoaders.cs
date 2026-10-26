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
    /// The keys a field of this schema asks for (<see cref="CrewFieldKeys.Keys"/>) that the caller holds on each
    /// project, so the projects of one batch, as a rule a page, cost the rules one question. A project the caller
    /// does not see, or holds none of the keys on, is missing from the answer, and whoever asks about it is
    /// refused.
    /// </summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<ProjectId, ProjectKeySet>> GetHeldKeysByProjectIdAsync(
        IReadOnlyList<ProjectId> projects,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var held = new Dictionary<ProjectId, ProjectKeySet>();
        foreach (var part in projects.Chunk(KeysOnProjects.MostProjects))
        {
            foreach (var set in (await sender.Send(new KeysOnProjects(part, CrewFieldKeys.Keys), cancellationToken)).OnProjects)
            {
                held[set.Project] = set;
            }
        }

        return held;
    }
}
