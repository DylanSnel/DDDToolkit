using Examples.Tenancy.Inspections.Application.Recording;
using GreenDonut.Data;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types.Pagination;

namespace Examples.Tenancy.Inspections.Api.Recording.GraphQL;

/// <summary>
/// One page of a project's inspections, and whether the caller may record another: HotChocolate's connection,
/// with one field more.
/// </summary>
/// <remarks>
/// The edges, the nodes and the page's cursors are HotChocolate's, made from the page the query answered.
/// <c>canRecord</c> is asked only when a client selects it, through a loader of its own, so a list that shows no
/// button costs no second question.
/// </remarks>
/// <param name="page">The page, as the query answered it.</param>
/// <param name="project">The project the inspections are of.</param>
internal sealed class InspectionsConnection(Page<InspectionOverview> page, ProjectId project) : PageConnection<InspectionOverview>(page)
{
    /// <summary>
    /// What a field that loads through a data loader weighs for each project of a list. HotChocolate weighs a
    /// field with a resolver that reads as if every project read on its own; these read once for the projects of
    /// a batch, as a rule all of an answer, so a project weighs little.
    /// </summary>
    internal const double LoadedForThePage = 1;

    /// <summary>Whether recording an inspection on the project would be allowed now: the caller holds the key, and the project is open.</summary>
    [Cost(LoadedForThePage)]
    public async Task<bool> GetCanRecordAsync(IOpenToRecordingByProjectIdDataLoader open, CancellationToken cancellationToken)
        => await open.LoadAsync(project, cancellationToken);
}
