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
/// gate is asked once, about all of them, by the handler.
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
/// Answers <see cref="InspectionsOfProjects"/>: which of its projects the caller sees, asked of Projects' gate once
/// for all of them, and the pages of those, in one statement on a context of this query's own.
/// </summary>
/// <param name="reads">Where inspections are read: a context per query.</param>
/// <param name="projects">Projects' answer to "may the caller do this to those projects".</param>
public sealed class InspectionsOfProjectsHandler(IInspectionReads reads, IProjectGate projects)
    : IQueryHandler<InspectionsOfProjects, IReadOnlyDictionary<ProjectId, Page<InspectionOverview>>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException"><c>inspections.page-size-invalid</c>, with <c>Max</c>; <c>inspections.page-from-both-ends</c>.</exception>
    public async ValueTask<IReadOnlyDictionary<ProjectId, Page<InspectionOverview>>> Handle(InspectionsOfProjects query, CancellationToken cancellationToken)
    {
        var paging = InspectionPages.Checked(query.Paging);

        // Only the projects the gate answers for are read: the others are not there for this caller.
        var seen = (await projects.AskAsync(query.Projects, InspectionsOfProjects.RequiredKey, cancellationToken)).Keys.ToList();

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
