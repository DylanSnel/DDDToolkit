using DDDToolkit.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Inventory.Api;

/// <summary>Inventory's HTTP surface: what is in stock, and goods arriving.</summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/stock", async (InventoryContext inventory, CancellationToken cancellationToken) =>
            (await inventory.StockItems.OrderBy(item => item.Sku).ToListAsync(cancellationToken))
                .Select(item => new { item.Sku, item.OnHand, item.Reserved, item.Available }));

        app.MapGet("/reservations", async (InventoryContext inventory, CancellationToken cancellationToken) =>
            (await inventory.StockReservations.ToListAsync(cancellationToken))
                .Select(r => new { id = r.Id.ToString(), order = r.Order.ToString(), status = r.Status.ToString(), r.Refusal }));

        app.MapPost("/stock/{sku}/receive", async (string sku, Receive body, InventoryContext inventory, CancellationToken cancellationToken) =>
        {
            if (body.Quantity < 1)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["quantity"] = ["Receive at least one."] });
            }

            var item = await inventory.StockItems.SingleOrDefaultAsync(i => i.Sku == sku, cancellationToken);
            if (item is null)
            {
                inventory.StockItems.Add(item = new StockItem(StockItemId.CreateSequential(), sku, body.Quantity));
            }
            else
            {
                item.Receive(body.Quantity);
            }

            try
            {
                await inventory.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrencyConflictException conflict)
            {
                return Results.Conflict(conflict.Message);
            }

            return Results.Ok(new { item.Sku, item.OnHand, item.Reserved, item.Available });
        });

        return app;
    }

    public sealed record Receive(int Quantity);
}
