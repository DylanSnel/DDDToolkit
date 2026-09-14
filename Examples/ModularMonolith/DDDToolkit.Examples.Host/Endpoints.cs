using DDDToolkit.Exceptions;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Invariants;
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

            // "An order must have at least one line" is also an invariant, checked on every save.
            // Asking here as well is not a duplicate: this one answers the user, in the field they
            // filled in, before an Order exists at all.
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

            // Nothing here validated the SKUs, and OrderLine has a rule of its own about them. Ask the
            // order and it answers for its lines as well, before the order has been handed to a
            // context at all: this is a question about an object in memory, and persistence has
            // nothing to do with it. Ask, and a broken line is an answer; save without asking and it
            // is a 500.
            if (Broken(order) is { } refusal)
            {
                return refusal;
            }

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

            // The same one question again, now about an order that came out of the database. A blank
            // SKU breaks the line's own rule and the order reports it, naming the line; a SKU the
            // order already names breaks the order's own rule, because only the order can see both
            // lines. One call covers both, because the order is the boundary.
            if (Broken(order) is { } refusal)
            {
                return refusal;
            }

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

    /// <summary>
    /// The check stage of the two, asked of the aggregate. The order has already been changed; this is
    /// the handler finding out whether what it just did is allowed to exist, while "no" is still an
    /// answer it can return. Null means nothing is broken. Nothing is written either way.
    /// </summary>
    /// <remarks>
    /// <see cref="Order"/> is the consistency boundary, so asking it asks its lines too, and
    /// <c>OrderLine.MustNameASku</c> is reported here without this method, the endpoint that called it
    /// or <see cref="Order"/> itself knowing that rule exists. No <c>DbContext</c>, no query and no
    /// save: an aggregate a handler is holding is in memory and whole, and its consistency is a
    /// question about those objects and nothing else.
    /// <para>
    /// Asking is not what makes the rules a guarantee; the interceptor running the save-time stage
    /// before every <c>SaveChanges</c> is, and it still runs a few lines below each call to this. What
    /// asking buys is the answer arriving as a value in the one place that wanted it, so the client
    /// reads a 422 naming the rule rather than a 500 naming an exception.
    /// </para>
    /// <para>
    /// The codes are what make that body worth reading, and they are the argument for giving a rule
    /// a type of its own. A named rule reports the code it was written with; a rule that stayed in
    /// the <c>CheckInvariants</c> seam reports <see cref="InvariantViolation.SeamCode"/>, which says
    /// where it came from and nothing at all about which rule it was.
    /// </para>
    /// <para>
    /// For a unit of work spanning several aggregates there is
    /// <c>InvariantInterceptor.GetInvariantViolations(context)</c>, which asks the change tracker
    /// instead. That is the save's question rather than the domain's: it covers every aggregate this
    /// save would write, and of the children only the ones it is about to write, where this covers one
    /// aggregate and every child it holds.
    /// </para>
    /// </remarks>
    private static IResult? Broken(Order order)
    {
        var violations = order.GetInvariantViolations();
        if (violations.Count == 0)
        {
            return null;
        }

        // Branching on a code, never on a message. The violation carries the line that reported it, so
        // the answer can name the offending line without this method searching the order for it.
        if (violations.FirstOrDefault(v => v.Code == OrderLine.MustNameASku.ViolationCode) is { } blank)
        {
            return Results.UnprocessableEntity(new { blank.Code, blank.Message, line = blank.EntityId?.ToString() });
        }

        return Results.UnprocessableEntity(violations.Select(v => new
        {
            v.Code,
            v.Message,
            entity = v.EntityType?.Name,
            id = v.EntityId?.ToString(),
        }));
    }

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
