using Examples.Tenancy.Tenants.Api.Rest;
using Examples.Tenancy.Tenants.Application.Operators.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Operators.Rest;

/// <summary>
/// What the application's own staff read of Tenancy, over HTTP: every tenant, and one tenant's access history.
/// Each route sends one query of the application project's <c>Operators</c> feature.
/// </summary>
/// <remarks>
/// An operator holds no seat, so these routes name no tenant in a header: one that reads inside a tenant has the
/// tenant's id in its path. Every one of them reads. There is no route here that changes anything, because an
/// operator changes nothing itself.
/// <para>
/// Like every route of this project, these decide nothing and take only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps them into the
/// group the host hands it.
/// </para>
/// </remarks>
internal static class OperatorsEndpoints
{
    /// <summary>
    /// Maps <c>GET /operations/tenants</c> and <c>GET /operations/tenants/{tenant}/history</c> into
    /// <paramref name="group"/>, the host's group for operators.
    /// </summary>
    public static IEndpointRouteBuilder MapOperatorsEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Every tenant by slug, each with its status and its active seats. "next" is the marker to send as "after".
        group.MapGet("/operations/tenants", async (string? after, int? size, ISender sender, CancellationToken cancellationToken) =>
        {
            var page = await sender.Send(new AllTenants(after, size ?? AllTenants.DefaultPage), cancellationToken);
            return Results.Ok(new
            {
                items = page.Items.Select(tenant => new { tenant.Id, tenant.Slug, tenant.Name, tenant.Status, tenant.ActiveSeats }),
                page.Next,
            });
        });

        // One tenant's access history, as a holder of the key that reads it sees it in that tenant.
        group.MapGet("/operations/tenants/{tenant}/history", async (TenantId tenant, string? after, int? size, ISender sender, CancellationToken cancellationToken)
            => Results.Ok(HistoryPages.Describe(await sender.Send(new TenantAccessHistory(tenant, HistoryPages.Asked(after, size)), cancellationToken))));

        return group;
    }
}
