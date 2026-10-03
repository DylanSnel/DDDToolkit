using Examples.Tenancy.Projects.Application.Overview;
using Examples.Tenancy.Projects.Application.Overview.Queries;
using GreenDonut;
using Mediator;

namespace Examples.Tenancy.Projects.Api.Overview.GraphQL;

/// <summary>
/// What a project's fields load through: for each method HotChocolate writes a data loader, which gathers the ids
/// the fields of one request ask about and sends for all of them at once.
/// </summary>
/// <remarks>
/// A loader sends a query as a field does, from a scope of its own, and reads nothing itself: the query declares
/// its access and answers only what the caller may see, in one statement. A project out of the caller's reach is
/// missing from the answer, and its field is then nothing. A query is asked about a limited number of projects, so
/// more ids than that are sent in parts.
/// </remarks>
internal static class OverviewDataLoaders
{
    /// <summary>The projects the caller may see, by id: what the lookup and the node field answer from.</summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<ProjectId, ProjectOverview>> GetProjectByIdAsync(
        IReadOnlyList<ProjectId> ids,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var projects = new Dictionary<ProjectId, ProjectOverview>();
        foreach (var part in ids.Chunk(ProjectsById.MostProjects))
        {
            foreach (var project in await sender.Send(new ProjectsById(part), cancellationToken))
            {
                projects[project.Id] = project;
            }
        }

        return projects;
    }

    /// <summary>The crew of each project, with the caller's own roles on it.</summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<ProjectId, ProjectCrew>> GetCrewByProjectIdAsync(
        IReadOnlyList<ProjectId> projects,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var crews = new Dictionary<ProjectId, ProjectCrew>();
        foreach (var part in projects.Chunk(ProjectsById.MostProjects))
        {
            foreach (var (project, crew) in await sender.Send(new CrewsOfProjects(part), cancellationToken))
            {
                crews[project] = crew;
            }
        }

        return crews;
    }

    /// <summary>What the caller may do to each project.</summary>
    [DataLoader]
    public static async Task<IReadOnlyDictionary<ProjectId, ProjectAbilities>> GetAbilitiesByProjectIdAsync(
        IReadOnlyList<ProjectId> projects,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var abilities = new Dictionary<ProjectId, ProjectAbilities>();
        foreach (var part in projects.Chunk(ProjectsById.MostProjects))
        {
            foreach (var (project, can) in await sender.Send(new AbilitiesOnProjects(part), cancellationToken))
            {
                abilities[project] = can;
            }
        }

        return abilities;
    }
}
