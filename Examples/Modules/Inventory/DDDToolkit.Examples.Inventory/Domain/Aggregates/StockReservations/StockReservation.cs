using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Examples.Ordering.Contracts;

namespace DDDToolkit.Examples.Inventory.Domain.StockReservations;

/// <summary>What Inventory decided about one order: everything set aside, or nothing and why.</summary>
/// <remarks>
/// A refusal is a reservation too. Recording it as a row, rather than just publishing a failure, gives
/// the refusal an aggregate to raise its event from, and gives Inventory a record of every order it was
/// asked about. There is a public constructor for neither outcome: <c>StockAllocator</c> decides,
/// and it creates the reservation in the state it decided on.
/// <para>
/// <see cref="Order"/> is an <see cref="OrderId"/>, Ordering's published identifier, never an
/// <c>Order</c>. Inventory knows orders exist and nothing else about them.
/// </para>
/// </remarks>
[AggregateRoot<Guid>("RSV")]
public partial class StockReservation
{
    private StockReservation(StockReservationId id, OrderId order, ReservationStatus status, string? refusal) : base(id)
    {
        Order = order;
        Status = status;
        Refusal = refusal;
    }

    public OrderId Order { get; private set; }

    public ReservationStatus Status { get; private set; }

    /// <summary>Why the order could not be filled, when it could not.</summary>
    public string? Refusal { get; private set; }

    public partial IReadOnlyList<ReservedLine> Lines { get; }

    internal static StockReservation Reserved(OrderId order, IEnumerable<ReservedLine> lines)
    {
        var reservation = new StockReservation(StockReservationId.CreateSequential(), order, ReservationStatus.Reserved, refusal: null);
        reservation._lines.AddRange(lines);
        reservation.RaiseDomainEvent(new StockReserved(order));
        return reservation;
    }

    internal static StockReservation Refused(OrderId order, string reason)
    {
        var reservation = new StockReservation(StockReservationId.CreateSequential(), order, ReservationStatus.Refused, reason);
        reservation.RaiseDomainEvent(new StockRefused(order, reason));
        return reservation;
    }

    /// <summary>
    /// The order was cancelled: hands the stock back. Nothing happens for a reservation that was refused
    /// or already released, so the second cancellation of an order changes nothing.
    /// </summary>
    public void Release(IReadOnlyDictionary<string, StockItem> stock)
    {
        ArgumentNullException.ThrowIfNull(stock);

        if (Status is not ReservationStatus.Reserved)
        {
            return;
        }

        foreach (var line in _lines)
        {
            stock[line.Sku].Release(line.Quantity);
        }

        Status = ReservationStatus.Released;
    }
}

public enum ReservationStatus
{
    Reserved,
    Refused,
    Released,
}
