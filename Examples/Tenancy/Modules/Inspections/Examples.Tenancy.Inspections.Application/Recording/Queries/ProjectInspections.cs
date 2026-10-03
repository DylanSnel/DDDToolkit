using Examples.Tenancy.Inspections.Application.Recording.Commands;
using GreenDonut.Data;
using Mediator;

namespace Examples.Tenancy.Inspections.Application.Recording.Queries;

/// <summary>A project's inspections, newest first and a page at a time, with whether the caller may record another.</summary>
/// <remarks>
/// For a caller who may see the project: the caller holds <see cref="RequiredKey"/> on it, which Projects' gate
/// answers before the handler runs. A project the caller may not see is not found, as everywhere. It is the
/// list of one project, which a route asks for by the project's id; the inspections of many projects at a time
/// are <see cref="InspectionsOfProjects"/>'s.
/// </remarks>
/// <param name="Project">The project.</param>
/// <param name="Paging">
/// How many inspections, 1 to <see cref="LargestPage"/>, and after or before which cursor; left out, the first
/// <see cref="DefaultPage"/>.
/// </param>
public sealed record ProjectInspections(ProjectId Project, PagingArguments? Paging = null) : IQuery<InspectionList>, IInspectionsRequest
{
    /// <summary>How many inspections a page holds when the caller does not say.</summary>
    public const int DefaultPage = 50;

    /// <summary>The most inspections a page holds.</summary>
    public const int LargestPage = 200;

    /// <summary>The key the caller holds on the project.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => new InspectionsRequirement.OnProject(RequiredKey, Project);
}

/// <summary>One page of a project's inspections, and whether the caller may record another.</summary>
/// <param name="Items">The page, newest first: its inspections, whether more follow, and the cursor of each.</param>
/// <param name="CanRecord">
/// Whether recording one would be allowed now: the caller holds the key of <see cref="RecordInspection"/> on the
/// project, and the project is open. A screen fills its "record an inspection" button from this, because
/// Projects cannot know Inspections' key and its own answer about the project says nothing about it.
/// </param>
public sealed record InspectionList(Page<InspectionOverview> Items, bool CanRecord);

/// <summary>
/// Answers <see cref="ProjectInspections"/>: the page in one statement on a context of this query's own, and
/// whether the caller may record, asked of Projects' gate with the very key the command declares.
/// </summary>
/// <remarks>
/// <see cref="InspectionList.CanRecord"/> is an answer, not a check: nothing is refused here for a key the caller
/// does not hold. It reads <see cref="RecordInspection.RequiredKey"/>, so a command whose key changes takes the
/// flag with it, and it says what the command's own check would: a closed project takes no inspection, whatever
/// the caller holds.
/// </remarks>
/// <param name="reads">Where inspections are read: a context per query.</param>
/// <param name="gated">The project this query passed the gate for.</param>
/// <param name="projects">Projects' answer to "may the caller do this to that project".</param>
public sealed class ProjectInspectionsHandler(IInspectionReads reads, Checked<GatedProject> gated, IProjectGate projects)
    : IQueryHandler<ProjectInspections, InspectionList>
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The query did not pass the access check: no project passed the gate for it.</exception>
    /// <exception cref="Exceptions.RefusalException"><c>inspections.page-size-invalid</c>, with <c>Max</c>; <c>inspections.page-from-both-ends</c>.</exception>
    public async ValueTask<InspectionList> Handle(ProjectInspections query, CancellationToken cancellationToken)
    {
        var (project, _) = gated.TakeFor(query);
        var paging = InspectionPages.Checked(query.Paging ?? default, ProjectInspections.DefaultPage);

        var record = await projects.AskAsync(project, RecordInspection.RequiredKey, cancellationToken);

        return new InspectionList(
            await reads.PageForProjectAsync(project, paging, cancellationToken),
            CanRecord: record.Allowed && !record.Closed);
    }
}
