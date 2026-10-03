using Examples.Tenancy.Projects.Application.Overview;
using Examples.Tenancy.Projects.Application.Overview.Queries;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects.ValueObjects;
using GreenDonut.Data;
using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;
using Mediator;

namespace Examples.Tenancy.Projects.Api.Overview.GraphQL;

/// <summary>
/// The list of projects, a page at a time. Like a route, the field decides nothing: it sends the query the route
/// of the same feature sends.
/// </summary>
/// <remarks>
/// A class marked <c>[QueryType]</c>, where every other field of this project is a method marked
/// <c>[Query]</c>: HotChocolate writes the connection type of a paged field for a class it generates the type of,
/// and a method marked <c>[Query]</c> is bound without it, as a plain list. That is the one exception, and it
/// reaches no further than it must: such a class holds paged fields and nothing else, and is named for it.
/// </remarks>
[QueryType]
internal static partial class OverviewPagedQueries
{
    /// <summary>How many rows a page of this field holds when a client names no size: HotChocolate's own default, said out loud.</summary>
    private const int DefaultPage = 10;

    /// <summary>The most rows a page of this field holds: HotChocolate's own limit, said out loud. The schema refuses a larger one before the query is sent.</summary>
    private const int LargestPage = 50;

    /// <summary>
    /// The projects the caller may see, by number, a page at a time. The filters narrow the list. A page is the
    /// project's own row and how the caller reaches it; its crew and what the caller may do to it are read only
    /// when a client asks for them, once for a batch of projects, as a rule the whole page.
    /// </summary>
    /// <remarks>
    /// The sizes of a page are HotChocolate's own here, ten when a client names none and fifty at most, and
    /// smaller than the route's; the field says them itself, so they are in the schema and do not move with a
    /// default somebody changes elsewhere. What a request may cost is estimated from the largest page its list
    /// may hold, and HotChocolate's limits are made for its own sizes: a page of projects with their crews and
    /// each member's roles is many objects. The query itself refuses a page larger than its own cap, and one
    /// asked for from both ends, whoever sends it.
    /// </remarks>
    [UseConnection(IncludeTotalCount = true, DefaultPageSize = DefaultPage, MaxPageSize = LargestPage)]
    public static async Task<PageConnection<ProjectOverview>> GetProjectsAsync(
        PagingArguments paging,
        ProjectState? state,
        OrganizationUnitId? unit,
        string? text,
        [Service] ISender sender,
        CancellationToken cancellationToken)
        => new(await sender.Send(new VisibleProjects(paging, state, unit, text), cancellationToken));
}
