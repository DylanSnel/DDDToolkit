using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Examples.Ordering.Contracts;

namespace DDDToolkit.Examples.Ordering;

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
/// file mentioning it. <c>Host/Endpoints.cs</c> is that handler. The self-only pair,
/// <c>GetOwnInvariantViolations()</c> and <c>EnsureOwnInvariants()</c>, is for a caller already
/// walking the graph, which here is the save.
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
