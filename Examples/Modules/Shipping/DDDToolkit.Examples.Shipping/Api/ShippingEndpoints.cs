using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Shipping.Api;

/// <summary>Shipping's HTTP surface: the vans booked so far.</summary>
public static class ShippingEndpoints
{
    public static IEndpointRouteBuilder MapShippingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/shipments", async (ShippingContext shipping, CancellationToken cancellationToken) =>
            await shipping.Shipments
                .Select(s => new { id = s.Id.ToString(), order = s.Order.ToString(), s.Destination, s.ConfirmedAt })
                .ToListAsync(cancellationToken));

        return app;
    }
}
