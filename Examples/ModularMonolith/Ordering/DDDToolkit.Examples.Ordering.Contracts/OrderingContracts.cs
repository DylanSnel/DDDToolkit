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
/// the one identifier in the module that other people read. Shipping stores it, the HTTP API parses it
/// and the integration event carries it, so it deserves a file you can navigate to and comment on. The
/// identifiers nobody outside the aggregate mentions, such as <c>OrderLineId</c>, are left to the
/// generator; see <c>Order.cs</c>.
/// </remarks>
[ModuleContract]
[EntityId<Guid>("ORD")]
public readonly partial record struct OrderId;

/// <summary>
/// What the Ordering module promises to tell everyone else when an order is placed.
/// </summary>
/// <remarks>
/// It needs no <c>[ModuleContract]</c>: a type whose whole job is to be read by somebody else is
/// already a contract. The name and the version are pinned here, so the class can be renamed or moved
/// without breaking a consumer that deployed against it. Bump <c>Version</c> and keep this record when
/// the payload changes shape; see <c>docs/integration-events.md</c>.
/// <para>
/// Note what it carries. <see cref="OrderId"/> travels because the id is published and pointing at an
/// order is the whole point. The address does not: <c>Address</c> is Ordering's own value object, and a
/// consumer that deserialized it would be coupled to a type Ordering expects to change freely.
/// </para>
/// </remarks>
[IntegrationEvent("ordering.order-placed", Version = 1)]
public sealed record OrderPlacedV1(OrderId OrderId, string City, string PostalCode, int LineCount);
