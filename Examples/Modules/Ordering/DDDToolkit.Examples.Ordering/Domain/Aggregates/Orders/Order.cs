using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Ordering.Domain.Orders;

/// <summary>
/// An order. The aggregate root of this module: one transaction, one version, one set of invariants.
/// </summary>
/// <remarks>
/// The generator supplies the <c>AggregateRoot&lt;OrderId&gt;</c> base class, a constructor for Entity
/// Framework, the <c>_lines</c> list behind <see cref="Lines"/>, and both stages of the invariant
/// check: <c>GetInvariantViolations()</c>, which answers with a list, and <c>EnsureInvariants()</c>,
/// which throws. Both run the rules in <c>Invariants/</c>, then the <c>CheckInvariants</c> seam at the
/// bottom of this file, and then ask every line.
/// <para>
/// That last part is what makes this class a boundary rather than a class with rules. A handler
/// holding an order asks it one question and is answered for the whole aggregate, each violation
/// naming the entity that reported it, so <c>OrderLine</c>'s own rule reaches the caller without this
/// file mentioning it. <c>Api/OrderingEndpoints.cs</c> is that handler. The self-only pair,
/// <c>GetOwnInvariantViolations()</c> and <c>EnsureOwnInvariants()</c>, is for a caller already
/// walking the graph, which here is the save.
/// </para>
/// <para>
/// <b>The order is also where the checkout process lives.</b> Two other modules have to agree before an
/// order is confirmed: Inventory sets the stock aside and Payments takes the money. Their answers arrive
/// as integration events, in whatever order the transport delivers them, and each one is a method here:
/// <see cref="RecordStockReserved"/>, <see cref="RecordPayment"/>, <see cref="Cancel"/>. The order
/// confirms itself when it has both, whichever came first. There is no saga class and no workflow
/// engine: the state is the aggregate's, and so are the rules about it.
/// </para>
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
    /// <param name="lines">Priced lines. <c>Domain/Services/OrderPricer.cs</c> is what prices them.</param>
    /// <param name="placedBy">
    /// The customer who placed it, or <see langword="null"/> for a guest. The handler takes it from the
    /// caller, <c>CustomerId.Of(caller)</c>; the rules in <c>Access/</c> say who may see the order after.
    /// </param>
    public Order(OrderId id, ValidAddress shipTo, IEnumerable<OrderLine> lines, CustomerId? placedBy) : base(id)
    {
        ArgumentNullException.ThrowIfNull(shipTo);
        ArgumentNullException.ThrowIfNull(lines);

        ShipTo = shipTo;
        PlacedBy = placedBy;
        _lines.AddRange(lines);
        Status = OrderStatus.Placed;
        Total = _lines.Aggregate(Money.Zero(), (total, line) => total.Plus(line.Subtotal));

        RaiseDomainEvent(new OrderPlaced(
            id,
            shipTo,
            [.. _lines.Select(line => new OrderPlaced.Line(line.Sku, line.Quantity))],
            Total));
    }

    public Address ShipTo { get; private set; }

    /// <summary>
    /// The customer who placed the order, or <see langword="null"/> for a guest's. It never changes: an
    /// order does not move to another customer.
    /// </summary>
    public CustomerId? PlacedBy { get; private set; }

    /// <summary>The lines of this order. Read-only outside the aggregate; the generated <c>_lines</c> field is what Entity Framework maps.</summary>
    public partial IReadOnlyList<OrderLine> Lines { get; }

    /// <summary>What the customer pays: the lines' subtotals added up when the order was placed.</summary>
    public Money Total { get; private set; }

    public OrderStatus Status { get; private set; }

    /// <summary>Inventory has set every line aside.</summary>
    public bool StockReserved { get; private set; }

    /// <summary>Payments has taken <see cref="Total"/>.</summary>
    public bool Paid { get; private set; }

    /// <summary>When the order was confirmed, or <see langword="null"/> while it is not.</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>Why the order was cancelled, or <see langword="null"/> while it is not.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Inventory set the stock aside. A cancelled order ignores it: Inventory hears about the
    /// cancellation too and releases what it reserved, so there is nothing to undo here.
    /// </summary>
    public void RecordStockReserved(DateTimeOffset at)
    {
        if (Status is not OrderStatus.Placed || StockReserved)
        {
            return;
        }

        StockReserved = true;
        ConfirmWhenReady(at);
    }

    /// <summary>Payments took the money.</summary>
    public void RecordPayment(DateTimeOffset at)
    {
        if (Status is not OrderStatus.Placed || Paid)
        {
            return;
        }

        Paid = true;
        ConfirmWhenReady(at);
    }

    /// <summary>
    /// Cancels the order. Cancelling it twice changes nothing the second time.
    /// </summary>
    /// <remarks>
    /// Cancelling a confirmed order is not refused here: the method does what it is told, and
    /// <c>Invariants/MustNotCancelAConfirmedOrder.cs</c> says whether the result may exist. That is the
    /// same order of events as everywhere else in this aggregate: act, then ask. The endpoint asks and
    /// answers 422; a caller that does not ask is stopped by the save.
    /// </remarks>
    public void Cancel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status is OrderStatus.Cancelled)
        {
            return;
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        RaiseDomainEvent(new OrderCancelled(Id, reason));
    }

    private void ConfirmWhenReady(DateTimeOffset at)
    {
        if (!StockReserved || !Paid)
        {
            return;
        }

        Status = OrderStatus.Confirmed;
        ConfirmedAt = at;
        RaiseDomainEvent(new OrderConfirmed(Id, ShipTo));
    }

    /// <summary>
    /// The one rule of this aggregate that is still a seam rather than a type of its own, because it
    /// is one line and nobody outside the order needs to name it. Compare
    /// <c>Invariants/MustHaveLines.cs</c>, which is the same length and earned a file anyway: the
    /// endpoint answers differently for that one, and answering differently means branching on a
    /// code.
    /// </summary>
    /// <remarks>
    /// It reaches across the lines, which is why it is the root's rule and not
    /// <see cref="OrderLine"/>'s. No line can see its siblings, and no line knows which of a pair is
    /// the one at fault. A line's own rules are a line's own business, and nothing here loops over the
    /// lines to run them: the generated walk asks each line, and the save asks each changed one.
    /// <para>
    /// Everything the seam reports is a violation whose <c>Code</c> is
    /// <c>InvariantViolation.SeamCode</c>, so a caller can find the rules that would read better with
    /// a name of their own by looking for that code.
    /// </para>
    /// </remarks>
    partial void CheckInvariants()
    {
        if (_lines.Select(line => line.Sku).Distinct().Count() != _lines.Count)
        {
            throw InvariantViolation("An order may not name the same SKU on two lines.");
        }
    }
}

/// <summary>Where an order is in checkout.</summary>
public enum OrderStatus
{
    /// <summary>Waiting for Inventory and Payments.</summary>
    Placed,

    /// <summary>Stock set aside and money taken. Shipping books a van.</summary>
    Confirmed,

    /// <summary>Will not be delivered.</summary>
    Cancelled,
}
