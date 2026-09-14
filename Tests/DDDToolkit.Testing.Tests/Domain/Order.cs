using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Testing.Tests.Domain;

/// <summary>
/// The aggregate the tests drive. It is declared exactly the way a consumer declares one, so the
/// testing kit is exercised against generated code: the base class, the identifier and the read-only
/// <see cref="Lines"/> collection all come from the generator.
/// </summary>
[AggregateRoot<Guid>("ORD")]
public partial class Order
{
    /// <summary>Places an order for a customer. Raises <see cref="OrderPlaced"/>.</summary>
    public Order(OrderId id, CustomerId customer) : base(id)
    {
        Customer = customer;
        RaiseDomainEvent(new OrderPlaced(id, customer));
    }

    /// <summary>Who the order is for.</summary>
    public CustomerId Customer { get; private set; }

    /// <summary>Where the order is in its life cycle.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;

    /// <summary>The tracking code, once the order shipped.</summary>
    public string? TrackingCode { get; private set; }

    /// <summary>What was ordered.</summary>
    public partial IReadOnlyList<OrderLine> Lines { get; }

    /// <summary>
    /// Adds a line. The quantity check comes before anything is changed or raised, which is the
    /// ordering the <c>WhenThrows</c> assertions pin.
    /// </summary>
    public void AddLine(Sku sku, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "A line needs at least one item.");
        }

        if (Status != OrderStatus.Draft)
        {
            throw new InvalidOperationException($"A {Status} order cannot take new lines.");
        }

        _lines.Add(new OrderLine(sku, quantity));
        RaiseDomainEvent(new LineAdded(Id, sku, quantity));
    }

    /// <summary>Confirms the order. Confirming twice is a no-op, so the second call raises nothing.</summary>
    public void Confirm()
    {
        if (Status == OrderStatus.Confirmed)
        {
            return;
        }

        if (Status != OrderStatus.Draft)
        {
            throw new InvalidOperationException($"A {Status} order cannot be confirmed.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("An order with no lines cannot be confirmed.");
        }

        Status = OrderStatus.Confirmed;
        RaiseDomainEvent(new OrderConfirmed(Id, _lines.Count));
    }

    /// <summary>Ships a confirmed order.</summary>
    public void Ship(string trackingCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackingCode);

        if (Status != OrderStatus.Confirmed)
        {
            throw new InvalidOperationException($"A {Status} order cannot ship.");
        }

        Status = OrderStatus.Shipped;
        TrackingCode = trackingCode;
        RaiseDomainEvent(new OrderShipped(Id, trackingCode));
    }

    /// <summary>Cancels the order. A shipped order cannot be cancelled; cancelling twice raises nothing.</summary>
    public void Cancel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status == OrderStatus.Shipped)
        {
            throw new InvalidOperationException("A shipped order cannot be cancelled.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        Status = OrderStatus.Cancelled;
        RaiseDomainEvent(new OrderCancelled(Id, reason));
    }
}

/// <summary>One line of an <see cref="Order"/>.</summary>
/// <param name="Sku">What was ordered.</param>
/// <param name="Quantity">How many.</param>
public sealed record OrderLine(Sku Sku, int Quantity);

/// <summary>Where an <see cref="Order"/> is in its life cycle.</summary>
public enum OrderStatus
{
    /// <summary>Still being assembled.</summary>
    Draft,

    /// <summary>Confirmed by the customer.</summary>
    Confirmed,

    /// <summary>On its way.</summary>
    Shipped,

    /// <summary>Called off.</summary>
    Cancelled,
}

/// <summary>The customer an order belongs to.</summary>
[EntityId<Guid>("CUS")]
public readonly partial record struct CustomerId;

/// <summary>A stock keeping unit.</summary>
[EntityId<string>("SKU")]
public readonly partial record struct Sku
{
    /// <summary>Wraps an existing value, which reads better at a call site than the constructor.</summary>
    public static Sku Of(string value) => new(value);
}
