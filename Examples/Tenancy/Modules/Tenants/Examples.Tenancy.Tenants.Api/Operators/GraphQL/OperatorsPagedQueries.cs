using Examples.Tenancy.Tenants.Application.History;
using Examples.Tenancy.Tenants.Application.Operators.Queries;
using GreenDonut.Data;
using HotChocolate;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Operators.GraphQL;

/// <summary>
/// What the application's own staff page through of Tenancy: one tenant's access history. The field sends the
/// query its route sends, and nothing else. What they ask that does not page is in <see cref="OperatorsQueries"/>.
/// </summary>
/// <remarks>
/// An operator holds no seat, so the host lets the field through for an operator instead of a seat, as it
/// maps the routes into a group of their own. The query refuses whoever is no operator itself, wherever it is
/// sent from.
/// <para>
/// A class marked <c>[QueryType]</c>, since its field pages: HotChocolate writes the connection type of a paged
/// field for a class it generates the type of. Such a class holds paged fields and nothing else.
/// </para>
/// </remarks>
[QueryType]
internal static partial class OperatorsPagedQueries
{
    /// <summary>How many rows a page of this field holds when a client names no size: HotChocolate's own default, said out loud.</summary>
    private const int DefaultPage = 10;

    /// <summary>The most rows a page of this field holds: HotChocolate's own limit, said out loud. The schema refuses a larger one before the query is sent.</summary>
    private const int LargestPage = 50;

    /// <summary>
    /// The access history of one tenant, newest first and a page at a time: what
    /// <c>GET /operations/tenants/{tenant}/history</c> answers, as the connection a tenant's own
    /// <c>accessHistory</c> is. A tenant that does not exist has none.
    /// </summary>
    /// <remarks>
    /// Who acted is <c>byKind</c> and, for a seat, <c>bySeatId</c>. The seat itself, <c>bySeat</c>, is the
    /// directory's to answer, and the directory answers a seat of the tenant: an operator has none.
    /// <para>
    /// The sizes of a page are the ones the tenant's own <c>accessHistory</c> has, said on the field.
    /// </para>
    /// </remarks>
    [UseConnection(IncludeTotalCount = true, DefaultPageSize = DefaultPage, MaxPageSize = LargestPage)]
    public static async Task<PageConnection<AccessHistoryEntry>> GetTenantAccessHistoryAsync(
        TenantId tenant,
        PagingArguments paging,
        [Service] ISender sender,
        CancellationToken cancellationToken)
        => new(await sender.Send(new TenantAccessHistory(tenant, paging), cancellationToken));
}
