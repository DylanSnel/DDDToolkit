using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Examples.Ordering.Contracts;

namespace DDDToolkit.Examples.Ordering;

/// <summary>
/// An order. The aggregate root of this module: one transaction, one version, one set of invariants.
/// </summary>
/// <remarks>
/// The generator supplies the <c>AggregateRoot&lt;OrderId&gt;</c> base class, a constructor for Entity
/// Framework, the <c>_lines</c> list behind <see cref="Lines"/>, and the <c>CheckInvariants</c> seam
/// implemented at the bottom of this file.
/// </remarks>
[AggregateRoot<OrderId>]
public partial class Order
{
    /// <summary>
    /// Places an order. There is no draft state and no <c>Place()</c> method: an order that was never
    /// placed is not an order, so placing it is what constructing it means.
    /// </summary>
    /// <param name="shipTo">
    /// The always-valid twin, not <c>Address</c>. The signature is the check: an address that nobody
    /// validated cannot be passed here, so the aggregate never has to re-validate one. The property
    /// below is the base type, because that is what comes back when Entity Framework reads the row.
    /// </param>
    public Order(OrderId id, ValidAddress shipTo, IEnumerable<OrderLine> lines) : base(id)
    {
        ArgumentNullException.ThrowIfNull(shipTo);
        ArgumentNullException.ThrowIfNull(lines);

        ShipTo = shipTo;
        _lines.AddRange(lines);

        RaiseDomainEvent(new OrderPlaced(id, shipTo, _lines.Count));
    }

    public Address ShipTo { get; private set; }

    /// <summary>The lines of this order. Read-only outside the aggregate; the generated <c>_lines</c> field is what Entity Framework maps.</summary>
    public partial IReadOnlyList<OrderLine> Lines { get; }

    /// <summary>Amends a placed order. Two callers doing this at once is what the version column is for.</summary>
    public void AddLine(string sku, int quantity)
        => _lines.Add(new OrderLine(OrderLineId.CreateSequential(), sku, quantity));

    /// <summary>
    /// What must be true of a whole order every time anyone can look at one. The interceptor runs this
    /// before every save that touches the order, so it is a guarantee rather than a check somebody
    /// remembered to call.
    /// </summary>
    /// <remarks>
    /// Nothing in this example can break it, because the endpoint refuses a request with no lines
    /// before an order is ever constructed. That is the point: the rule outlives the endpoint. The
    /// method somebody adds next year to remove a line meets it without having to know it exists.
    /// </remarks>
    partial void CheckInvariants()
    {
        if (Lines.Count == 0)
        {
            throw InvariantViolation("An order must have at least one line.");
        }
    }
}
