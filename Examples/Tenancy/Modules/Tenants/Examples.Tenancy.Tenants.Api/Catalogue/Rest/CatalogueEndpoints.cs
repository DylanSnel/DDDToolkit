using Examples.Tenancy.Tenants.Application.Catalogue.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Catalogue.Rest;

/// <summary>
/// The permission catalogue over HTTP: the keys and the role packs the application knows. The route sends the one
/// query of the application project's <c>Catalogue</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, it decides nothing and takes only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps it into the
/// group the host hands it.
/// </remarks>
internal static class CatalogueEndpoints
{
    /// <summary>Maps <c>GET /tenancy/catalogue</c>.</summary>
    public static IEndpointRouteBuilder MapCatalogueEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/tenancy/catalogue", async (ISender sender, CancellationToken cancellationToken)
            => Results.Ok(Describe(await sender.Send(new CatalogueContents(), cancellationToken))));

        return group;
    }

    private static object Describe(CatalogueOverview catalogue) => new
    {
        permissions = catalogue.Permissions.Select(permission => new
        {
            permission.Key,
            permission.Module,
            permission.Description,
            implies = permission.Implies,
            permission.Retired,
            permission.ManagesAccess,
        }),
        packs = catalogue.Packs.Select(pack => new
        {
            pack.Key,
            pack.Name,
            pack.Description,
            pack.Keys,
            pack.Shape,
            pack.Administers,
        }),
    };
}
