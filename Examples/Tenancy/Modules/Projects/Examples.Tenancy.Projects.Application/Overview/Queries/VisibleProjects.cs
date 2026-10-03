using Examples.Tenancy.Shared.Application.Paging;
using GreenDonut.Data;
using Mediator;

namespace Examples.Tenancy.Projects.Application.Overview.Queries;

/// <summary>
/// The projects the caller may see, by number, a page at a time: each with how the caller sees it.
/// </summary>
/// <remarks>
/// Nobody is refused for seeing nothing: the list holds what the caller reaches with <see cref="RequiredKey"/>,
/// through the organization or by being on a crew, and is empty for a caller who reaches none. So the key is not a
/// check made before the handler but the filter of the handler's own statement, and the handler reads the same
/// constant the query declares. Every filter narrows that list and none widens it: a unit the caller holds nothing
/// at lists the projects below it that the caller reaches some other way, and no others.
/// <para>
/// The page is asked the way every paged list is, forward with <c>First</c> and <c>After</c> or backward with
/// <c>Last</c> and <c>Before</c>, and answers a <see cref="Page{T}"/>, which makes the cursor of each of its
/// rows. A cursor is a place in the list, not a right. Neither a size nor a direction asks for the first
/// <see cref="DefaultPage"/>. The whole list is counted only when <c>IncludeTotalCount</c> asks: a count reads
/// every project the caller reaches.
/// </para>
/// </remarks>
/// <param name="Paging">How many, after or before which place, and whether to count the whole list.</param>
/// <param name="State">Only the projects in this state.</param>
/// <param name="Unit">Only the projects at this unit or below it.</param>
/// <param name="Text">
/// Only the projects whose number starts with this or whose name contains it, whatever the case. Which letters
/// have a case is the database's to say, and Postgres folds every letter: a capital outside A to Z in a name,
/// as in "Écluse", is found in either case.
/// </param>
public sealed record VisibleProjects(
    PagingArguments Paging = default,
    ProjectState? State = null,
    OrganizationUnitId? Unit = null,
    string? Text = null) : IQuery<Page<ProjectOverview>>, IProjectsRequest
{
    /// <summary>The key that decides which projects the caller is shown.</summary>
    public const string RequiredKey = ProjectKeys.View;

    /// <summary>How many projects a page holds when the caller does not say.</summary>
    public const int DefaultPage = 50;

    /// <summary>The most projects a page holds.</summary>
    public const int LargestPage = 200;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => MemberAccess.SeenWith<ProjectId>(RequiredKey);
}

/// <summary>
/// Answers <see cref="VisibleProjects"/> in one statement, on a reading of this query's own, however many projects
/// there are: the page, with Tenancy's answers subqueries of it and the count, when asked, a part of it.
/// </summary>
/// <param name="reads">Where projects are read: a context per query.</param>
/// <param name="access">Who reaches which project.</param>
public sealed class VisibleProjectsHandler(IProjectReads reads, ProjectAccess access) : IQueryHandler<VisibleProjects, Page<ProjectOverview>>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// <c>projects.page-size-invalid</c>, with <c>Max</c>; <c>projects.page-from-both-ends</c> for a page asked for
    /// by its first rows and by its last at once.
    /// </exception>
    public async ValueTask<Page<ProjectOverview>> Handle(VisibleProjects query, CancellationToken cancellationToken)
    {
        // The page itself has no largest size and takes no page from both ends, so both are held here, for every
        // caller of the query alike, by the check every paged query of the sample makes.
        var paging = PageSizes.Checked(
            query.Paging,
            VisibleProjects.DefaultPage,
            VisibleProjects.LargestPage,
            sizeOutOfRange: () => ProjectRefusals.Of(ProjectRefusals.PageSizeInvalid, ("Max", VisibleProjects.LargestPage)),
            fromBothEnds: () => ProjectRefusals.Of(ProjectRefusals.PageFromBothEnds));

        await using var reading = reads.Open();

        // The reach is a set not yet read, and so is the part of the tree under a unit; the reading puts them into
        // the statement that reads the projects.
        var reach = access.ReachFor(VisibleProjects.RequiredKey);
        var filter = new ProjectListFilter(
            query.State,
            query.Unit is { } unit ? access.Questions(reading).UnitsUnder(unit) : null,
            string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim());

        return await reading.PageAsync(reach, filter, paging, cancellationToken);
    }
}
