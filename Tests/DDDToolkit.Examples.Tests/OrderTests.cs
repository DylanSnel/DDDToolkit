using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Testing;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>
/// The aggregate on its own: no database, no host, no clock but the one the test installs.
/// </summary>
public class OrderTests
{
    private static ValidAddress ShipTo()
        => new Address("Oudegracht 1", "Utrecht", "3511 AA").ToValid();

    private static Order Place(params OrderLine[] lines)
        => new(OrderId.CreateSequential(), ShipTo(), lines.Length > 0 ? lines : [Line("COFFEE-1KG", 2)]);

    private static OrderLine Line(string sku, int quantity)
        => new(OrderLineId.CreateSequential(), sku, quantity);

    [Fact]
    public void Placing_an_order_raises_one_event_carrying_what_was_ordered()
    {
        var order = Place(Line("COFFEE-1KG", 2), Line("MUG", 1));

        order.PendingEvents()
            .RaisedExactly<OrderPlaced>()
            .SingleEvent<OrderPlaced>()
            .LineCount.Should().Be(2);
    }

    [Fact]
    public void The_event_carries_the_whole_payload_and_not_the_metadata()
    {
        var id = OrderId.CreateSequential();

        var order = new Order(id, ShipTo(), [Line("COFFEE-1KG", 2)]);

        // EventId and OccurredAt are never compared, which is what makes this readable at all: two
        // events with identical payloads are never equal as records.
        order.PendingEvents().Raised(new OrderPlaced(id, new Address("Oudegracht 1", "Utrecht", "3511 AA"), 1));
    }

    [Fact]
    public void OccurredAt_is_whatever_the_domain_event_clock_says()
    {
        var moment = new DateTimeOffset(2026, 9, 14, 9, 30, 0, TimeSpan.Zero);
        using var scope = DomainEventClock.Use(new FixedClock(moment));

        // The aggregate calls new OrderPlaced(...), not the test, so an initialiser is out of reach.
        // The clock is the seam, and it is scoped to this flow so a parallel test cannot see it.
        var order = Place();

        order.PendingEvents().Should().OnlyContain(raised => raised.OccurredAt == moment);
    }

    [Fact]
    public void Amending_an_order_raises_nothing()
    {
        var scenario = AggregateScenario.Given(Place());
        scenario.IgnorePendingEvents();

        // Nothing outside Ordering reacts to a line being added, so no event describes it. Raising one
        // anyway would be a promise nobody asked for and everybody would then be free to depend on.
        scenario.When(order => order.AddLine("MUG", 1)).RaisedNothing();

        scenario.Subject.Lines.Should().HaveCount(2);
    }

    [Fact]
    public void A_line_with_no_items_is_refused_before_anything_happens()
    {
        // WhenThrows asserts both halves: the exception came out, and nothing was raised on the way.
        AggregateScenario.Given(Place())
            .WhenThrows<ArgumentOutOfRangeException>(order => order.AddLine("MUG", 0));
    }

    [Fact]
    public void An_order_with_no_lines_cannot_be_stored()
    {
        // The constructor allows it, because an aggregate is allowed to be inconsistent inside its own
        // methods. What it promises is never to be saved broken, and this is the check the interceptor
        // runs before every save.
        var empty = new Order(OrderId.CreateSequential(), ShipTo(), []);

        var violation = Assert.Throws<InvariantViolationException>(empty.EnsureInvariants);
        violation.Message.Should().Contain("at least one line");
    }

    [Fact]
    public void An_order_with_lines_satisfies_its_invariant()
    {
        Place().EnsureInvariants();
    }

    [Fact]
    public void The_read_only_view_is_the_only_way_in()
    {
        var order = Place();

        // The generated property hands out a view, not the list. There is no cast back to List<T>.
        order.Lines.Should().BeAssignableTo<IReadOnlyList<OrderLine>>();
        order.Lines.Should().NotBeAssignableTo<List<OrderLine>>();
    }
}
