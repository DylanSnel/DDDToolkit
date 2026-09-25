using DDDToolkit.Abstractions.Attributes;

// Same module name as DDDToolkit.Examples.Ordering, so the two assemblies are one module. That is how
// you split a module into a domain half and a published half without inventing a boundary between them.
[assembly: Module("Ordering")]

namespace DDDToolkit.Examples.Ordering.Contracts;

/// <summary>
/// The identifier of an order.
/// </summary>
/// <remarks>
/// Declared by hand rather than generated from <c>[AggregateRoot&lt;Guid&gt;("ORD")]</c>, because this is
/// the one identifier in the module that other people read. Inventory, Payments and Shipping store it,
/// the HTTP API parses it and every integration event about an order carries it, so it deserves a file
/// you can navigate to and comment on. The identifiers nobody outside the aggregate mentions, such as
/// <c>OrderLineId</c>, are left to the generator; see <c>Order.cs</c>.
/// </remarks>
[ModuleContract]
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;

/// <summary>
/// What the Ordering module promises to tell everyone else when an order is placed.
/// </summary>
/// <remarks>
/// It needs no <c>[ModuleContract]</c>: a type whose whole job is to be read by somebody else is
/// already a contract. Its name is <c>ordering.order-placed</c>, from the module and the class name, and its
/// version is the 1 the class name ends in. When the payload changes shape, keep this record and add an
/// <c>OrderPlacedV2</c> beside it; when the class is renamed, put the old name in the attribute,
/// <c>[IntegrationEvent("ordering.order-placed")]</c>, so consumers keep routing on it. See
/// <c>docs/integration-events.md</c>.
/// <para>
/// Note what it carries. <see cref="OrderId"/> travels because the id is published and pointing at an
/// order is the whole point. The address does not: <c>Address</c> is Ordering's own value object, and a
/// consumer that deserialized it would be coupled to a type Ordering expects to change freely. The
/// same goes for the total, which travels as an amount and a currency rather than as <c>Money</c>.
/// </para>
/// <para>
/// Two modules read it for different reasons. Inventory needs the lines, to set stock aside; Payments
/// needs the total, to know what to charge. Neither needs the other's half, and neither is named here.
/// </para>
/// </remarks>
[IntegrationEvent]
public sealed record OrderPlacedV1(
    OrderId OrderId,
    string City,
    string PostalCode,
    IReadOnlyList<OrderedLineV1> Lines,
    decimal Total,
    string Currency);

/// <summary>One line of <see cref="OrderPlacedV1"/>: how many of which SKU.</summary>
public sealed record OrderedLineV1(string Sku, int Quantity);

/// <summary>
/// The stock is set aside and the money is taken: the order will be delivered. Shipping books a van on
/// this, and not on <see cref="OrderPlacedV1"/>, because an order that is placed may still be cancelled.
/// </summary>
[IntegrationEvent]
public sealed record OrderConfirmedV1(OrderId OrderId, string City, string PostalCode);

/// <summary>
/// The order will not be delivered. Whatever a module did for it, it undoes: Inventory releases the
/// stock it set aside, Payments voids a payment it has not taken yet.
/// </summary>
[IntegrationEvent]
public sealed record OrderCancelledV1(OrderId OrderId, string Reason);
