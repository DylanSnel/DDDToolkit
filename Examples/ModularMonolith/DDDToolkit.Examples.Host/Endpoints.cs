using DDDToolkit.Exceptions;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Validation;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Host;

/// <summary>The HTTP surface. Four endpoints, each showing one thing the toolkit does at a boundary.</summary>
public static class Endpoints
{
    public static void MapOrderingEndpoints(this WebApplication app)
    {
        // Validation at the boundary, without exceptions. TryToValid hands back every reason the address
        // is unacceptable, and the caller gets a 400 it can read field by field. ToValid() would have
        // thrown, which is right when an invalid value is a bug and wrong when it is an ordinary answer
        // to an ordinary request.
        app.MapPost("/orders", async (PlaceOrder body, OrderingContext orders, CancellationToken cancellationToken) =>
        {
            var errors = new List<ValidationError>();

            // Prefixed puts the failures back under the field they came from: "City" becomes "shipTo.City".
            if (!new Address(body.Street, body.City, body.PostalCode).TryToValid(out var shipTo, out var addressErrors))
            {
                errors.AddRange(addressErrors.Prefixed("shipTo"));
            }

            // "An order must have at least one line" is also an invariant, checked on every save. Asking
            // here as well is not a duplicate: this one answers the user, in the field they filled in.
            if (body.Lines.Count == 0)
            {
                errors.Add(new ValidationError("An order needs at least one line.", "lines", "Required"));
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors.ToErrorDictionary());
            }

            var order = new Order(
                OrderId.CreateSequential(),
                shipTo!,
                body.Lines.Select(line => new OrderLine(OrderLineId.CreateSequential(), line.Sku, line.Quantity)));

            orders.Add(order);

            // This save writes the order, its lines and one outbox row, in one transaction. The
            // background service picks the row up a second later and Shipping books against it.
            await orders.SaveChangesAsync(cancellationToken);

            return Results.Created($"/orders/{order.Id}", Describe(order));
        });

        // OrderId binds straight out of the route, because a struct identifier implements IParsable<T>
        // and minimal APIs bind through it. Text that is not an order id is a 400 before this runs.
        app.MapGet("/orders/{id}", async (OrderId id, OrderingContext orders, CancellationToken cancellationToken) =>
        {
            var order = await orders.Orders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
            return order is null ? Results.NotFound() : Results.Ok(Describe(order));
        });

        // Amending an order two people are looking at is what the version column is for. The interceptor
        // turns the stale write into a ConcurrencyConflictException naming the aggregate, and the honest
        // answer to the second caller is 409 rather than a silent overwrite.
        app.MapPost("/orders/{id}/lines", async (OrderId id, AddLine body, OrderingContext orders, CancellationToken cancellationToken) =>
        {
            var order = await orders.Orders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
            if (order is null)
            {
                return Results.NotFound();
            }

            order.AddLine(body.Sku, body.Quantity);

            try
            {
                await orders.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrencyConflictException conflict)
            {
                return Results.Conflict(conflict.Message);
            }

            return Results.Ok(Describe(order));
        });
    }

    public static void MapShippingEndpoints(this WebApplication app)
        => app.MapGet("/shipments", async (ShippingContext shipping, CancellationToken cancellationToken) =>
            await shipping.Shipments
                .Select(s => new { id = s.Id.ToString(), order = s.Order.ToString(), s.Destination, s.OrderedAt })
                .ToListAsync(cancellationToken));

    private static object Describe(Order order) => new
    {
        id = order.Id.ToString(),
        order.Version,
        shipTo = new { order.ShipTo.Street, order.ShipTo.City, order.ShipTo.PostalCode },
        lines = order.Lines.Select(line => new { id = line.Id.ToString(), line.Sku, line.Quantity }),
    };

    public sealed record PlaceOrder(string Street, string City, string PostalCode, IReadOnlyList<AddLine> Lines);

    public sealed record AddLine(string Sku, int Quantity);
}
