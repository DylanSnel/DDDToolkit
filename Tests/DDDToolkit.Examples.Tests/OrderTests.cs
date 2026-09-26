using DDDToolkit.BaseTypes;
using DDDToolkit.Exceptions;
using DDDToolkit.Examples.Ordering;
using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.SharedKernel;
using DDDToolkit.Testing;
using FluentAssertions;

namespace DDDToolkit.Examples.Tests;

/// <summary>
/// The aggregate on its own: no database, no host, no clock but the one the test installs.
/// </summary>
public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static ValidAddress ShipTo()
        => new Address("Oudegracht 1", "Utrecht", "3511 AA").ToValid();

    private static Order Place(params OrderLine[] lines)
        => new(OrderId.CreateSequential(), ShipTo(), lines.Length > 0 ? lines : [Line("COFFEE-1KG", 2, 12.50m)], placedBy: null);

    private static OrderLine Line(string sku, int quantity, decimal unitPrice = 1m)
        => new(OrderLineId.CreateSequential(), sku, quantity, new Money(unitPrice, Money.Euro));

    /// <summary>An order that was placed and whose placement event has been looked at already.</summary>
    private static AggregateScenario<Order> Placed()
    {
        var scenario = AggregateScenario.Given(Place());
        scenario.IgnorePendingEvents();
        return scenario;
    }

    [Fact]
    public void Placing_an_order_raises_one_event_carrying_what_was_ordered()
    {
        var order = Place(Line("COFFEE-1KG", 2, 12.50m), Line("MUG", 1, 8m));

        var placed = order.PendingEvents()
            .RaisedExactly<OrderPlaced>()
            .SingleEvent<OrderPlaced>();

        placed.Lines.Should().Equal(new OrderPlaced.Line("COFFEE-1KG", 2), new OrderPlaced.Line("MUG", 1));
        placed.Total.Should().Be(new Money(33m, Money.Euro));
    }

    [Fact]
    public void The_total_is_the_lines_added_up_when_the_order_is_placed()
        => Place(Line("COFFEE-1KG", 2, 12.50m), Line("MUG", 3, 8m)).Total.Should().Be(new Money(49m, Money.Euro));

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
    public void A_line_with_no_items_is_refused_before_anything_happens()
    {
        var zero = () => Line("MUG", 0);

        zero.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_order_with_no_lines_cannot_be_stored()
    {
        // The constructor allows it, because an aggregate is allowed to be inconsistent inside its own
        // methods. What it promises is never to be saved broken, and this is the check the interceptor
        // runs before every save.
        var empty = new Order(OrderId.CreateSequential(), ShipTo(), [], placedBy: null);

        var violation = Assert.Throws<InvariantViolationException>(empty.EnsureInvariants);
        violation.Message.Should().Contain("at least one line");
    }

    [Fact]
    public void An_order_with_lines_satisfies_its_invariants()
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

    // ------------------------------------------------------------------ checkout

    [Fact]
    public void One_answer_is_not_enough_to_confirm()
    {
        var scenario = Placed();

        scenario.When(order => order.RecordStockReserved(Now)).RaisedNothing();

        scenario.Subject.Status.Should().Be(OrderStatus.Placed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_answers_confirm_the_order_whichever_arrives_first(bool stockFirst)
    {
        var scenario = Placed();

        scenario.When(order =>
        {
            if (stockFirst)
            {
                order.RecordStockReserved(Now);
                order.RecordPayment(Now);
            }
            else
            {
                order.RecordPayment(Now);
                order.RecordStockReserved(Now);
            }
        }).RaisedExactly<OrderConfirmed>();

        scenario.Subject.Status.Should().Be(OrderStatus.Confirmed);
        scenario.Subject.ConfirmedAt.Should().Be(Now);
    }

    [Fact]
    public void The_same_answer_twice_confirms_once()
    {
        var scenario = Placed();

        scenario.When(order =>
        {
            order.RecordStockReserved(Now);
            order.RecordPayment(Now);
            order.RecordPayment(Now);
        }).RaisedExactly<OrderConfirmed>();
    }

    [Fact]
    public void Cancelling_says_why_and_cancelling_again_says_nothing()
    {
        var scenario = Placed();

        scenario.When(order => order.Cancel("No stock.")).RaisedExactly<OrderCancelled>();
        scenario.When(order => order.Cancel("Changed my mind.")).RaisedNothing();

        scenario.Subject.CancellationReason.Should().Be("No stock.");
    }

    [Fact]
    public void A_cancelled_order_ignores_answers_that_arrive_late()
    {
        var scenario = Placed();
        scenario.When(order => order.Cancel("Payment refused.")).RaisedExactly<OrderCancelled>();

        scenario.When(order => order.RecordStockReserved(Now)).RaisedNothing();

        scenario.Subject.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void A_confirmed_order_that_is_cancelled_breaks_a_named_rule()
    {
        var order = Place();
        order.RecordStockReserved(Now);
        order.RecordPayment(Now);

        order.Cancel("Changed my mind.");

        // Cancel did what it was told; the rule says the result may not exist. The endpoint asks this
        // question and answers 422 with the code; the save would refuse it either way.
        order.GetInvariantViolations().Select(v => v.Code)
            .Should().ContainSingle().Which.Should().Be(Order.MustNotCancelAConfirmedOrder.ViolationCode);
    }
}
