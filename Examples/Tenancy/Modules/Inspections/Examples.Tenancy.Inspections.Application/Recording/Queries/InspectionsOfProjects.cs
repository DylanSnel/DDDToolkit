using GreenDonut.Data;
using Mediator;

namespace Examples.Tenancy.Inspections.Application.Recording.Queries;

/// <summary>
/// The same page of the inspections of each of several projects, newest first: for a client that shows projects
/// with their inspections, asked once for all of them.
/// </summary>
/// <remarks>
/// It refuses no project. Those of <paramref name="Projects"/> the caller may see with <see cref="RequiredKey"/>
/// are answered, each with its page, an empty one when nothing was recorded on it; a project the caller may not
/// see is left out exactly as one that does not exist, so asking about an id tells nothing about it. Projects'
/// gate is asked once, about all of them, before the handler runs.
/// </remarks>
/// <param name="Projects">The projects, at most <see cref="MostProjects"/> different ones.</param>
/// <param name="Paging">How many inspections of each, 1 to <see cref="ProjectInspections.LargestPage"/>, and after or before which cursor.</param>
public sealed record InspectionsOfProjects(IReadOnlyList<ProjectId> Projects, PagingArguments Paging)
    : IQuery<IReadOnlyDictionary<ProjectId, Page<InspectionOverview>>>, IInspectionsRequest
{
    /// <summary>The key that decides whether a project is there for the caller.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <summary>The most projects one question is about: what Projects' gate answers at a time.</summary>
    public const int MostProjects = IProjectGate.MostProjects;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new InspectionsRequirement.OnProjectsInReach(RequiredKey, Projects);
}

/// <summary>
/// Answers <see cref="InspectionsOfProjects"/>: the pages of every project the gate answered for, in one
/// statement on a context of this query's own.
/// </summary>
/// <param name="reads">Where inspections are read: a context per query.</param>
/// <param name="gated">What Projects' gate answered about the projects of this query.</param>
public sealed class InspectionsOfProjectsHandler(IInspectionReads reads, Checked<GatedProjects> gated)
    : IQueryHandler<InspectionsOfProjects, IReadOnlyDictionary<ProjectId, Page<InspectionOverview>>>
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The query did not pass the access check: the gate answered nothing for it.</exception>
    /// <exception cref="Exceptions.RefusalException"><c>inspections.page-size-invalid</c>, with <c>Max</c>; <c>inspections.page-from-both-ends</c>.</exception>
    public async ValueTask<IReadOnlyDictionary<ProjectId, Page<InspectionOverview>>> Handle(InspectionsOfProjects query, CancellationToken cancellationToken)
    {
        // Only the projects the gate answered for are read: the others are not there for this caller.
        var seen = gated.TakeFor(query).Answers.Keys.ToList();
        var paging = InspectionPages.Checked(query.Paging);

        if (seen.Count == 0)
        {
            return new Dictionary<ProjectId, Page<InspectionOverview>>();
        }

        var pages = await reads.PagesForProjectsAsync(seen, paging, cancellationToken);

        // A project the caller sees and nothing was recorded on has a page too, an empty one: that is how whoever
        // asked tells it from a project that is not there.
        return seen.ToDictionary(project => project, project => pages.GetValueOrDefault(project) ?? Page<InspectionOverview>.Empty);
    }
}
