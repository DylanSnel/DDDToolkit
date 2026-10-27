using Examples.Tenancy.Tenants.Application.Operators.Queries;
using HotChocolate;
using Mediator;

namespace Examples.Tenancy.Tenants.Api.Operators.GraphQL;

/// <summary>
/// What the application's own staff ask of Tenancy. A field sends the query its route sends, and nothing else.
/// </summary>
/// <remarks>
/// An operator holds no seat, so the host lets these fields through for an operator instead of a seat, as it
/// maps the routes into a group of their own. The query refuses whoever is no operator itself, wherever it is
/// sent from. One tenant's access history pages as a connection, and is in <see cref="OperatorsPagedQueries"/>.
/// </remarks>
internal static class OperatorsQueries
{
    /// <summary>
    /// Every tenant of the application, by slug, each beside its name and how many of its seats are active: the tenant
    /// manager's page, as the route answers it. <c>items</c> holds the tenants, and <c>next</c> the marker to send as
    /// <paramref name="after"/> for the page after it. The tenant manager pages by a marker of its own, so this is no
    /// connection, and the field is a method marked <c>[Query]</c> like any other.
    /// </summary>
    [Query]
    public static async Task<TenantManager.TenantPage> GetTenantsAsync(string? after, int? size, [Service] ISender sender, CancellationToken cancellationToken)
        => await sender.Send(new AllTenants(after, size ?? AllTenants.DefaultPage), cancellationToken);
}
