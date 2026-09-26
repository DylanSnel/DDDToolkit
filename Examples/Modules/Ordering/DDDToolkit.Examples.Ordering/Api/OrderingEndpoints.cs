using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Invariants;
using DDDToolkit.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Ordering.Api;

/// <summary>
/// Ordering's HTTP surface. It lives in the module, not in a host, so the monolith and the service that
/// runs Ordering on its own serve the same API.
/// </summary>
public static class OrderingEndpoints
{
    public static IEndpointRouteBuilder MapOrderingEndpoints(this IEndpointRouteBuilder app)
    {
        // Validation at the boundary, without exceptions. TryToValid hands back every reason the address
        // is unacceptable, and the caller gets a 400 it can read field by field. ToValid() would have
        // thrown, which is right when an invalid value is a bug and wrong when it is an ordinary answer
        // to an ordinary request.
        app.MapPost("/orders", async (PlaceOrder body, ICallerAccessor callers, OrderingContext orders, CancellationToken cancellationToken) =>
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

            // The domain service prices the lines from Ordering's own copy of the catalog. Loading the
            // prices is this handler's job; deciding what a line costs, and that an unknown SKU cannot
            // be sold, is the service's.
            var skus = body.Lines.Select(line => line.Sku).ToList();
            var prices = await orders.CatalogPrices
                .Where(price => skus.Contains(price.Sku))
                .ToDictionaryAsync(price => price.Sku, price => price.Price, cancellationToken);

            var priced = OrderPricer.Price(body.Lines.Select(line => new OrderPricer.RequestedLine(line.Sku, line.Quantity)), prices);
            errors.AddRange(priced.UnknownSkus.Select(sku => new ValidationError($"{sku} is not for sale.", "lines", "UnknownSku", sku)));

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors.ToErrorDictionary());
            }

            // Placed in the name of whoever is asking, or as a guest's when nobody signed in. The rules in
            // Domain/Aggregates/Orders/Access say who sees it afterwards; this only says whose it is.
            var order = new Order(OrderId.CreateSequential(), shipTo!, priced.Lines, CustomerId.Of(callers.Current));

            // Nothing here validated a blank SKU, and OrderLine has a rule of its own about it. Ask the
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
            // background service picks the row up a second later, and Inventory and Payments start work.
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

        // The customer cancelling, while Inventory and Payments may be answering at the same moment. The
        // version column settles who was first: a policy handler that saved in between turns this save
        // into a ConcurrencyConflictException naming the aggregate, and the honest answer is a 409. The
        // other way round, the policy's save is the one refused, and the outbox retries it.
        app.MapPost("/orders/{id}/cancel", async (OrderId id, CancelOrder body, OrderingContext orders, CancellationToken cancellationToken) =>
        {
            var order = await orders.Orders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
            if (order is null)
            {
                return Results.NotFound();
            }

            order.Cancel(string.IsNullOrWhiteSpace(body.Reason) ? "Cancelled by the customer." : body.Reason);

            // The same one question again, now about an order that came out of the database. Cancelling
            // a confirmed order breaks MustNotCancelAConfirmedOrder, and the order reports it by its
            // code. Nothing was refused by Cancel itself: it acted, and this asks whether the result may
            // exist.
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

        return app;
    }

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
        status = order.Status.ToString(),
        order.StockReserved,
        order.Paid,
        order.CancellationReason,
        total = order.Total.Amount,
        order.Total.Currency,
        shipTo = new { order.ShipTo.Street, order.ShipTo.City, order.ShipTo.PostalCode },
        lines = order.Lines.Select(line => new { id = line.Id.ToString(), line.Sku, line.Quantity, unitPrice = line.UnitPrice.Amount }),
    };

    public sealed record PlaceOrder(string Street, string City, string PostalCode, IReadOnlyList<OrderedLine> Lines);

    public sealed record OrderedLine(string Sku, int Quantity);

    public sealed record CancelOrder(string? Reason);
}
