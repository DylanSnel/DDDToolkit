using DDDToolkit.Examples.Ordering.Contracts;
using HotChocolate.Types;

namespace DDDToolkit.Examples.Ordering.Api.GraphQL;

/// <summary>
/// How an <see cref="Order"/> looks in the schema: a Relay node, with its fields listed one by one.
/// </summary>
/// <remarks>
/// <c>BindFieldsExplicitly</c> because an aggregate has public methods, and HotChocolate would publish
/// every one that returns something as a field: <c>GetInvariantViolations()</c> among them. A schema is a
/// promise to clients, so it is a list somebody wrote, not whatever the class happens to have.
/// <para>
/// The id is the <see cref="OrderId"/> itself. The toolkit's generated bindings for Ordering's contracts
/// register the serializer that writes it into a node id, which is also what lets Inventory, Payments
/// and Shipping publish an order reference as an <c>Order</c> node id without knowing this type.
/// </para>
/// </remarks>
public sealed class OrderType : ObjectType<Order>
{
    protected override void Configure(IObjectTypeDescriptor<Order> descriptor)
    {
        descriptor.BindFieldsExplicitly();

        descriptor
            .ImplementsNode()
            .IdField(order => order.Id)
            .ResolveNode(async (context, id) =>
                await context.DataLoader<OrderByIdDataLoader>().LoadAsync(id, context.RequestAborted));

        descriptor.Field(order => order.Status);
        descriptor.Field(order => order.StockReserved);
        descriptor.Field(order => order.Paid);
        descriptor.Field(order => order.Total);
        descriptor.Field(order => order.ShipTo);
        descriptor.Field(order => order.Lines);
        descriptor.Field(order => order.ConfirmedAt);
        descriptor.Field(order => order.CancellationReason);
        descriptor.Field(order => order.Version);
    }
}

/// <summary>A line of an order. Not a node: nobody fetches a line on its own, only through its order.</summary>
public sealed class OrderLineType : ObjectType<OrderLine>
{
    protected override void Configure(IObjectTypeDescriptor<OrderLine> descriptor)
    {
        descriptor.BindFieldsExplicitly();

        descriptor.Field(line => line.Id);
        descriptor.Field(line => line.Sku);
        descriptor.Field(line => line.Quantity);
        descriptor.Field(line => line.UnitPrice);
        descriptor.Field(line => line.Subtotal);
    }
}
