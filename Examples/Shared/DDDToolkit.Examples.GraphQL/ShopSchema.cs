using DDDToolkit.Examples.Catalog.Api.GraphQL;
using DDDToolkit.Examples.Catalog.Domain.Products;
using DDDToolkit.Examples.Inventory.Api.GraphQL;
using DDDToolkit.Examples.Ordering.Api.GraphQL;
using DDDToolkit.Examples.Ordering.Domain.Orders;
using DDDToolkit.Examples.Payments.Api.GraphQL;
using DDDToolkit.Examples.Payments.Domain.Payments;
using DDDToolkit.Examples.Shipping.Api.GraphQL;
using DDDToolkit.Examples.Shipping.Domain.Shipments;
using DDDToolkit.HotChocolate;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.GraphQL;

/// <summary>
/// The shop as one GraphQL schema: every module's types, queries and mutations, and the fields that make
/// it read as one shop rather than five.
/// </summary>
/// <remarks>
/// A client asks
/// <code>
/// { order(id: "…") { status lines { quantity product { name price { amount } } } payment { status } shipment { destination } } }
/// </code>
/// and has no idea that five modules answered. The modules themselves still know nothing of each other:
/// Ordering knows a line's SKU, not the product; Payments and Shipping know an order's id, not the order.
/// The joins are made here, each on a lookup the owning module publishes, so every table is still read
/// by the one module that owns it.
/// <para>
/// This is the monolith's gateway. Run the modules as services and a Fusion gateway makes the same joins,
/// over the same lookups, and the client's query does not change.
/// </para>
/// </remarks>
public static class ShopSchema
{
    public static IRequestExecutorBuilder AddShopGraphQL(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddGraphQLServer()
            // Relay: node(id:), and every node's id is a global id the toolkit's identifiers fill.
            .AddGlobalObjectIdentification()
            // Ids as UUIDs, [Internal] members out of the schema, the DomainEvent interface...
            .AddDDDToolkitTypes()
            // ...and a broken rule or an invalid value as a GraphQL error carrying its code.
            .AddDDDToolkitErrors()
            .AddQueryType()
            .AddMutationType()
            .AddSubscriptionType()
            .AddInMemorySubscriptions()
            .AddCatalogGraphQL()
            .AddOrderingGraphQL()
            .AddInventoryGraphQL()
            .AddPaymentsGraphQL()
            .AddShippingGraphQL()
            .AddTypeExtension<OrderLineProduct>()
            .AddTypeExtension<OrderFulfilment>();
    }
}

/// <summary>The product a line is for: Ordering's SKU, joined to Catalog's lookup.</summary>
[ExtendObjectType<OrderLine>]
public sealed class OrderLineProduct
{
    public Task<Product?> GetProductAsync([Parent] OrderLine line, ProductBySkuDataLoader products, CancellationToken cancellationToken)
        => products.LoadAsync(line.Sku, cancellationToken);
}

/// <summary>What became of the order elsewhere: the payment Payments keeps, the van Shipping booked.</summary>
[ExtendObjectType<Order>]
public sealed class OrderFulfilment
{
    public Task<Payment?> GetPaymentAsync([Parent] Order order, PaymentByOrderDataLoader payments, CancellationToken cancellationToken)
        => payments.LoadAsync(order.Id, cancellationToken);

    public Task<Shipment?> GetShipmentAsync([Parent] Order order, ShipmentByOrderDataLoader shipments, CancellationToken cancellationToken)
        => shipments.LoadAsync(order.Id, cancellationToken);
}
