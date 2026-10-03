using Examples.Tenancy.Tenants.Api.Rest;
using Examples.Tenancy.Tenants.Application.History.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.History.Rest;

/// <summary>
/// The tenant's access history over HTTP: who changed whose access, and when, a page at a time. The route sends the
/// one query of the application project's <c>History</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, it decides nothing and takes only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps it into the group
/// the host hands it.
/// </remarks>
internal static class HistoryEndpoints
{
    /// <summary>Maps <c>GET /tenancy/history</c>, for a seat in the tenant the request selected.</summary>
    public static IEndpointRouteBuilder MapHistoryEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Newest first and a page at a time: "next" is the marker to send as "after" for the following page, and
        // is null on the last.
        group.MapGet("/tenancy/history", async (string? after, int? size, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(HistoryPages.Describe(await sender.Send(new AccessHistory(HistoryPages.Asked(after, size)), cancellationToken))));

        return group;
    }
}
