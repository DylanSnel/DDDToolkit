using System.Text.Json.Serialization;
using Examples.Tenancy.Tenants.Application.Tenant.Commands;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Examples.Tenancy.Tenants.Api.Tenant.Rest;

/// <summary>
/// The tenant itself over HTTP: changing its shape. The route sends a command of the application project's
/// <c>Tenant</c> feature; the feature's other command, which marks a tenant as a demonstration, is system work
/// and has no route.
/// </summary>
/// <remarks>
/// Like every route of this project, it decides nothing and takes only the sender;
/// <see cref="TenantsModule"/> says what all of them keep to. Internal: the module's entry maps it into the
/// group the host hands it.
/// </remarks>
internal static class TenantEndpoints
{
    /// <summary>Maps <c>POST /tenancy/shape</c>.</summary>
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Flat to hierarchical, never back. The tenant gains the roles of the packs seeded for the new shape it lacks.
        group.MapPost("/tenancy/shape", async (NewShape body, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new ChangeTenantShape(body.Shape), cancellationToken);
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>The new shape, by name (<c>"hierarchical"</c>), as the host spells every enum value.</summary>
    public sealed record NewShape([property: JsonRequired] TenantShape Shape);
}
