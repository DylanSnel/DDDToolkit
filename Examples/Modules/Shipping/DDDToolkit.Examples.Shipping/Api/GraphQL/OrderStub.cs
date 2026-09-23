using DDDToolkit.Examples.Ordering.Contracts;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Shipping.Api.GraphQL;

/// <summary>
/// An order as Shipping knows it: its id, and nothing else. What Shipping adds to it is the shipment.
/// </summary>
public sealed record OrderStub(OrderId Id);

/// <summary>
/// Shipping's part of the <c>Order</c> type, for a schema a Fusion gateway composes out of services.
/// </summary>
/// <remarks>
/// Ordering owns <c>Order</c>; Shipping contributes <c>shipment</c>, and needs only the order's id for
/// it. A node, so Shipping can read back the node id the gateway hands it; not for a schema that also has
/// Ordering's <c>Order</c>. Payments' <c>OrderStub</c> says more.
/// </remarks>
public sealed class OrderStubType : ObjectType<OrderStub>
{
    protected override void Configure(IObjectTypeDescriptor<OrderStub> descriptor)
    {
        descriptor.Name("Order");
        descriptor.BindFieldsExplicitly();

        descriptor
            .ImplementsNode()
            .IdField(order => order.Id)
            .ResolveNode((_, id) => Task.FromResult<OrderStub?>(new OrderStub(id)));

        descriptor
            .Field("shipment")
            .Type<ShipmentType>()
            .Resolve(async context => await context.DataLoader<ShipmentByOrderDataLoader>()
                .LoadAsync(context.Parent<OrderStub>().Id, context.RequestAborted));
    }
}

public static class OrderStubGraphQL
{
    /// <summary>
    /// Adds <c>Order.shipment</c> to a source schema that a Fusion gateway composes with Ordering's.
    /// </summary>
    public static IRequestExecutorBuilder AddShippingOrderStub(this IRequestExecutorBuilder graphql)
    {
        ArgumentNullException.ThrowIfNull(graphql);
        return graphql.AddType<OrderStubType>();
    }
}
