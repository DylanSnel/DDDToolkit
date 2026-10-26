using Examples.Tenancy.Tenants.Application.History;
using Examples.Tenancy.Tenants.Application.History.Queries;
using GreenDonut.Data;
using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.History.GraphQL;

/// <summary>What is asked about the tenant's access history. The field sends the query its route sends, and nothing else.</summary>
/// <remarks>
/// A class marked <c>[QueryType]</c>, where every other field of this project is a method marked
/// <c>[Query]</c>: HotChocolate writes the connection type of a paged field for a class it generates the type of,
/// and a method marked <c>[Query]</c> is bound without it, as a plain list. That is the one exception, and it
/// reaches no further than it must: such a class holds paged fields and nothing else, and is named for it.
/// </remarks>
[QueryType]
internal static partial class HistoryPagedQueries
{
    /// <summary>How many rows a page of this field holds when a client names no size: HotChocolate's own default, said out loud.</summary>
    private const int DefaultPage = 10;

    /// <summary>The most rows a page of this field holds: HotChocolate's own limit, said out loud. The schema refuses a larger one before the query is sent.</summary>
    private const int LargestPage = 50;

    /// <summary>
    /// Every change to who may do what in the tenant, newest first and a page at a time, each with who made it.
    /// For whoever holds <c>tenancy.history.view</c> for the whole tenant: the query asks for the key, as it does
    /// for the route. The rows are counted only for a client that asks for <c>totalCount</c>.
    /// </summary>
    /// <remarks>
    /// The sizes of a page are HotChocolate's own here, ten when a client names none and fifty at most, and
    /// smaller than the route's; the field says them itself, so they are in the schema and do not move with a
    /// default somebody changes elsewhere. What a request may cost is estimated from the largest page its list
    /// may hold, and HotChocolate's limits are made for its own sizes: a page of two hundred rows that each name
    /// a seat is over them. The query itself refuses a page larger than its own cap, and one asked for from both
    /// ends, whoever sends it.
    /// </remarks>
    [UseConnection(IncludeTotalCount = true, DefaultPageSize = DefaultPage, MaxPageSize = LargestPage)]
    public static async Task<PageConnection<AccessHistoryEntry>> GetAccessHistoryAsync(PagingArguments paging, [Service] ISender sender, CancellationToken cancellationToken)
        => new(await sender.Send(new AccessHistory(paging), cancellationToken));
}
