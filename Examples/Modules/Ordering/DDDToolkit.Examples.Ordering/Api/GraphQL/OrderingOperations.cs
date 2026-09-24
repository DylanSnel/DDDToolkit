using DDDToolkit.Exceptions;
using DDDToolkit.Examples.Ordering.Contracts;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using HotChocolate.Types.Relay;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Ordering.Api.GraphQL;

/// <summary>Ordering's queries. A single order is <c>node(id:)</c> or <c>order(id:)</c>, the same lookup.</summary>
[ExtendObjectType(OperationTypeNames.Query)]
public sealed class OrderingQueries
{
    public async Task<IReadOnlyList<Order>> GetOrdersAsync(OrderingContext orders, CancellationToken cancellationToken)
        => await orders.Orders.ToListAsync(cancellationToken);

    public Task<Order?> GetOrderAsync([ID<Order>] OrderId id, OrderByIdDataLoader orders, CancellationToken cancellationToken)
        => orders.LoadAsync(id, cancellationToken);
}

/// <summary>Ordering's mutations: place an order, cancel one.</summary>
/// <remarks>
/// The same domain calls as the REST endpoints, with GraphQL's way of refusing. A bad address throws
/// <c>InvalidValueObjectException</c> from <c>ToValid()</c>; a broken rule throws
/// <c>InvariantViolationException</c> from <c>EnsureInvariants()</c>. <c>AddDDDToolkitErrors()</c> turns
/// each into GraphQL errors carrying the code, so a client branches on <c>ORDER_ALREADY_CONFIRMED</c>
/// here exactly as it does on the 422 over REST.
/// </remarks>
[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class OrderingMutations
{
    public async Task<Order> PlaceOrderAsync(PlaceOrderInput input, OrderingContext orders, CancellationToken cancellationToken)
    {
        var shipTo = new Address(input.Street, input.City, input.PostalCode).ToValid();

        var skus = input.Lines.Select(line => line.Sku).ToList();
        var prices = await orders.CatalogPrices
            .Where(price => skus.Contains(price.Sku))
            .ToDictionaryAsync(price => price.Sku, price => price.Price, cancellationToken);

        var priced = OrderPricer.Price(input.Lines.Select(line => new OrderPricer.RequestedLine(line.Sku, line.Quantity)), prices);
        if (priced.UnknownSkus.Count > 0)
        {
            throw new GraphQLException(priced.UnknownSkus
                .Select(sku => ErrorBuilder.New().SetMessage($"{sku} is not for sale.").SetCode("UNKNOWN_SKU").SetExtension("sku", sku).Build())
                .ToArray());
        }

        var order = new Order(OrderId.CreateSequential(), shipTo, priced.Lines);
        order.EnsureInvariants();

        orders.Add(order);
        await orders.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> CancelOrderAsync([ID<Order>] OrderId id, string? reason, OrderingContext orders, CancellationToken cancellationToken)
    {
        var order = await orders.Orders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new GraphQLException(ErrorBuilder.New().SetMessage("No such order.").SetCode("NOT_FOUND").Build());

        order.Cancel(string.IsNullOrWhiteSpace(reason) ? "Cancelled by the customer." : reason);
        order.EnsureInvariants();

        try
        {
            await orders.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException conflict)
        {
            throw new GraphQLException(ErrorBuilder.New().SetMessage(conflict.Message).SetCode("CONCURRENCY_CONFLICT").Build());
        }

        return order;
    }
}

public sealed record PlaceOrderInput(string Street, string City, string PostalCode, IReadOnlyList<OrderedLineInput> Lines);

public sealed record OrderedLineInput(string Sku, int Quantity);

/// <summary>
/// An order settling, pushed to whoever is watching it. The outbox publishes the contract through
/// <c>GraphQlSubscriptionSink</c> to a topic per order; the field loads the order as it is now.
/// </summary>
/// <remarks>
/// A screen refresher, and nothing more. A client that was not connected when its order was confirmed
/// missed it, and reads the order again; nothing in the shop learns anything through a subscription.
/// </remarks>
[ExtendObjectType(OperationTypeNames.Subscription)]
public sealed class OrderingSubscriptions
{
    [Subscribe(With = nameof(WatchConfirmationAsync))]
    public async Task<Order> OrderConfirmedAsync([ID<Order>] OrderId id, [EventMessage] OrderConfirmedV1 confirmed, OrderByIdDataLoader orders, CancellationToken cancellationToken)
        => await orders.LoadAsync(confirmed.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {confirmed.OrderId} was published but cannot be found.");

    public ValueTask<ISourceStream<OrderConfirmedV1>> WatchConfirmationAsync([ID<Order>] OrderId id, ITopicEventReceiver receiver, CancellationToken cancellationToken)
        => receiver.SubscribeAsync<OrderConfirmedV1>(OrderingGraphQL.Topic(id), cancellationToken);

    [Subscribe(With = nameof(WatchCancellationAsync))]
    public async Task<Order> OrderCancelledAsync([ID<Order>] OrderId id, [EventMessage] OrderCancelledV1 cancelled, OrderByIdDataLoader orders, CancellationToken cancellationToken)
        => await orders.LoadAsync(cancelled.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Order {cancelled.OrderId} was published but cannot be found.");

    public ValueTask<ISourceStream<OrderCancelledV1>> WatchCancellationAsync([ID<Order>] OrderId id, ITopicEventReceiver receiver, CancellationToken cancellationToken)
        => receiver.SubscribeAsync<OrderCancelledV1>(OrderingGraphQL.Topic(id), cancellationToken);
}
