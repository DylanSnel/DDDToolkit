using Examples.Webshop.Ordering.Contracts;
using HotChocolate.Types;

namespace Examples.Webshop.Shipping.Api.GraphQL;

/// <summary>
/// An order as Shipping knows it: its id, and nothing else. What Shipping adds to it is the shipment.
/// </summary>
public sealed record OrderStub(OrderId Id);

/// <summary>
/// Shipping's part of the <c>Order</c> type, for a schema a Fusion gateway composes out of services.
/// </summary>
/// <remarks>
/// Ordering owns <c>Order</c>; Shipping contributes <c>shipment</c>, and needs only the order's id for
/// it. A node, so Shipping can read back the node id the gateway hands it; registered with the rest of
/// Shipping's types, so Shipping shares no schema with Ordering's <c>Order</c>. Payments' <c>OrderStub</c>
/// says more.
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
