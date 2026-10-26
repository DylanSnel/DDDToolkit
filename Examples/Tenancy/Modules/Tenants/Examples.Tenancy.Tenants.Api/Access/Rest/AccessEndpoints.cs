using Examples.Tenancy.Tenants.Application.Access.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Access.Rest;

/// <summary>
/// A probe over HTTP: where the caller holds a permission. The route sends the one query of the application project's
/// <c>Access</c> feature.
/// </summary>
/// <remarks>
/// Like every route of this project, it decides nothing and takes only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps it into the
/// group the host hands it.
/// </remarks>
internal static class AccessEndpoints
{
    /// <summary>Maps <c>GET /access/units?permission=</c>.</summary>
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Where the caller holds a permission, for the "who may do what" screens. A request with no seat behind it and a
        // permission the catalogue does not know are both refused. The units are ids: what they are called is asked of
        // the directory.
        group.MapGet("/access/units", async (string permission, ISender sender, CancellationToken cancellationToken) =>
        {
            var held = await sender.Send(new UnitsWhereIHold(permission), cancellationToken);
            return Results.Ok(new { unitsWhereIHold = held.Units, atTenant = held.AtTenant });
        });

        return group;
    }
}
