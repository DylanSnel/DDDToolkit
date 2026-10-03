using Examples.Webshop.Ordering.Contracts;
using HotChocolate.Types;

namespace Examples.Webshop.Payments.Api.GraphQL;

/// <summary>
/// An order as Payments knows it: its id, and nothing else. What Payments adds to it is the payment.
/// </summary>
public sealed record OrderStub(OrderId Id);

/// <summary>
/// Payments' part of the <c>Order</c> type, for a schema a Fusion gateway composes out of services.
/// </summary>
/// <remarks>
/// Ordering owns <c>Order</c>; Payments contributes one field to it, <c>payment</c>, and needs only the
/// order's id for that. The gateway sees two source schemas each declaring an <c>Order</c> node and merges
/// them on the id: a client asks <c>order { status payment { status } }</c> and the gateway fetches
/// <c>status</c> from Ordering, then <c>payment</c> from Payments through its <c>node</c> lookup.
/// <para>
/// A node, rather than a type with an id: the gateway hands Payments the order's node id, and Payments
/// can only read it back into an <see cref="OrderId"/> if <c>Order</c> is a node here too. The toolkit's
/// serializer for <see cref="OrderId"/> writes the same id in both services.
/// </para>
/// <para>
/// It is registered with the rest of Payments' types, so Payments shares no schema with Ordering's
/// <c>Order</c>, which would be two types of one name: a gateway composes the two.
/// </para>
/// </remarks>
public sealed class OrderStubType : ObjectType<OrderStub>
{
    protected override void Configure(IObjectTypeDescriptor<OrderStub> descriptor)
    {
        descriptor.Name("Order");
        descriptor.BindFieldsExplicitly();

        // Any order id is an order to Payments: whether it exists is Ordering's to say.
        descriptor
            .ImplementsNode()
            .IdField(order => order.Id)
            .ResolveNode((_, id) => Task.FromResult<OrderStub?>(new OrderStub(id)));

        descriptor
            .Field("payment")
            .Type<PaymentType>()
            .Resolve(async context => await context.DataLoader<PaymentByOrderDataLoader>()
                .LoadAsync(context.Parent<OrderStub>().Id, context.RequestAborted));
    }
}
