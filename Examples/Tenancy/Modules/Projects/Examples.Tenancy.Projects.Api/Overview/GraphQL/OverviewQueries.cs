using Examples.Tenancy.Projects.Application.Overview;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types.Composite;
using HotChocolate.Types.Relay;

namespace Examples.Tenancy.Projects.Api.Overview.GraphQL;

/// <summary>
/// What is asked about one project. Like a route, a field decides nothing: it hands its id to the loader that
/// sends for every id of the request at once. The list of projects pages, and is in
/// <see cref="OverviewPagedQueries"/>.
/// </summary>
internal static class OverviewQueries
{
    /// <summary>
    /// One project by its id, or nothing when it is not there or not the caller's to see. A lookup: a client asks
    /// it, and the gateway resolves another module's reference to a project through it, the ids of one batch in
    /// one question.
    /// </summary>
    [Query]
    [Lookup]
    [Cost(ProjectType.LoadedForThePage)]
    public static async Task<ProjectOverview?> GetProjectAsync([ID("Project")] ProjectId id, IProjectByIdDataLoader projects, CancellationToken cancellationToken)
        => await projects.LoadAsync(id, cancellationToken);
}
