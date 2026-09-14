using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Shipping;
using DDDToolkit.Testing;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>The consuming module's aggregate, which holds another module's id and nothing else of it.</summary>
public class ShipmentTests
{
    [Fact]
    public void A_shipment_points_at_an_order_by_id()
    {
        var order = OrderId.CreateSequential();

        var shipment = new Shipment(ShipmentId.CreateSequential(), order, "3511 AA, Utrecht", DateTimeOffset.UtcNow);

        shipment.Order.Should().Be(order);
        shipment.Order.ToString().Should().StartWith("ORD_", "the prefix belongs to the module that published the id");
    }

    [Fact]
    public void A_shipment_raises_nothing()
    {
        var shipment = new Shipment(ShipmentId.CreateSequential(), OrderId.CreateSequential(), "3511 AA, Utrecht", DateTimeOffset.UtcNow);

        // Deliberate. Nothing in this example reads a shipment event, and an event with no reader is a
        // schema you have to keep stable for nobody. The day something reads one, this test changes.
        shipment.PendingEvents().RaisedNothing();
    }
}
