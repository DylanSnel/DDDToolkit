using DDDToolkit.Interfaces;
using DDDToolkit.Testing.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Testing.Tests;

/// <summary>
/// The kit used the way a consumer uses it: given an aggregate, when a method runs, then these
/// events were raised.
/// </summary>
public class ScenarioTests
{
    private static Order Draft() => new(OrderId.CreateUnique(), CustomerId.CreateUnique());

    private static Order Confirmed()
    {
        var order = Draft();
        order.AddLine(Sku.Of("SKU-1"), 2);
        order.Confirm();
        return order;
    }

    // ---------------------------------------------------------------- acting

    [Fact]
    public void WhenReportsOnlyTheEventsThatCallRaised()
    {
        var order = Draft();   // the constructor already raised OrderPlaced

        AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 3))
            .RaisedExactly<LineAdded>();
    }

    [Fact]
    public void AnEventCanBeAssertedByPayload()
    {
        var order = Draft();

        AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 3))
            .Raised(new LineAdded(order.Id, Sku.Of("SKU-1"), 3));
    }

    [Fact]
    public void AnEventCanBeAssertedWithAPredicate()
    {
        var order = Draft();

        AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 3))
            .Raised<LineAdded>(added => added.Quantity == 3);
    }

    [Fact]
    public void SingleEventHandsBackTheEventItself()
    {
        var order = Draft();

        var added = AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 3))
            .SingleEvent<LineAdded>();

        added.Sku.Should().Be(Sku.Of("SKU-1"));
        added.OccurredAt.Should().NotBe(default);
    }

    [Fact]
    public void EventsOfReturnsEveryEventOfThatType()
    {
        var order = Draft();

        var added = AggregateScenario.Given(order)
            .When(o =>
            {
                o.AddLine(Sku.Of("SKU-1"), 1);
                o.AddLine(Sku.Of("SKU-2"), 2);
            })
            .EventsOf<LineAdded>();

        added.Select(line => line.Sku).Should().Equal(Sku.Of("SKU-1"), Sku.Of("SKU-2"));
    }

    [Fact]
    public void ExactlyChecksOrderAsWellAsContent()
    {
        var order = Draft();

        AggregateScenario.Given(order)
            .When(o =>
            {
                o.AddLine(Sku.Of("SKU-1"), 1);
                o.Confirm();
                o.Ship("TRACK-1");
            })
            .RaisedExactly<LineAdded, OrderConfirmed, OrderShipped>();
    }

    [Fact]
    public void ExactlyTheseComparesPayloadsInOrder()
    {
        var order = Draft();

        AggregateScenario.Given(order)
            .When(o =>
            {
                o.AddLine(Sku.Of("SKU-1"), 1);
                o.Confirm();
            })
            .RaisedExactlyThese(
                new LineAdded(order.Id, Sku.Of("SKU-1"), 1),
                new OrderConfirmed(order.Id, 1));
    }

    // ---------------------------------------------------------------- raising nothing

    [Fact]
    public void AMethodThatDecidesToDoNothingRaisesNothing()
    {
        var order = Confirmed();

        AggregateScenario.Given(order)
            .When(o => o.Confirm())
            .RaisedNothing();
    }

    [Fact]
    public void RaisedNoRulesOutOneTypeWithoutRulingOutTheRest()
    {
        var order = Draft();

        AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 1))
            .Raised<LineAdded>()
            .RaisedNo<OrderConfirmed>();
    }

    // ---------------------------------------------------------------- throwing

    [Fact]
    public void AnInvariantViolationThrowsAndLeavesNothingBehind()
    {
        var order = Confirmed();
        order.Ship("TRACK-1");

        var thrown = AggregateScenario.Given(order)
            .WhenThrows<InvalidOperationException>(o => o.Cancel("changed my mind"));

        thrown.Message.Should().Be("A shipped order cannot be cancelled.");
        order.Status.Should().Be(OrderStatus.Shipped);
    }

    [Fact]
    public void GuardClausesThatRunBeforeAnyChangeAreCovered()
    {
        var order = Draft();

        AggregateScenario.Given(order)
            .WhenThrows<ArgumentOutOfRangeException>(o => o.AddLine(Sku.Of("SKU-1"), 0));

        order.Lines.Should().BeEmpty();
    }

    [Fact]
    public void AnAggregateThatRaisesAndThenThrowsIsReportedAsAFailure()
    {
        var sloppy = new SloppyOrder(SloppyOrderId.CreateUnique());

        var act = () => AggregateScenario.Given(sloppy)
            .WhenThrows<InvalidOperationException>(o => o.Cancel("changed my mind"));

        act.Should().Throw<AggregateAssertionException>(
            "the exception was the expected one, but an event describing something that never happened was left behind");
    }

    // ---------------------------------------------------------------- multi step

    [Fact]
    public void EachStepOfALongerScenarioIsAssertedOnItsOwn()
    {
        var scenario = AggregateScenario.Given(Draft());

        scenario.PendingEvents.RaisedExactly<OrderPlaced>();

        scenario.When(o => o.AddLine(Sku.Of("SKU-1"), 2)).RaisedExactly<LineAdded>();
        scenario.When(o => o.Confirm()).RaisedExactly<OrderConfirmed>();
        scenario.When(o => o.Ship("TRACK-1")).RaisedExactly<OrderShipped>();

        scenario.PendingEvents.RaisedExactly(
            typeof(OrderPlaced), typeof(LineAdded), typeof(OrderConfirmed), typeof(OrderShipped));

        scenario.Subject.TrackingCode.Should().Be("TRACK-1");
    }

    [Fact]
    public void DrainingBetweenStepsModelsASave()
    {
        var scenario = AggregateScenario.Given(Draft());

        scenario.Drain().RaisedExactly<OrderPlaced>();

        scenario.When(o => o.AddLine(Sku.Of("SKU-1"), 2)).RaisedExactly<LineAdded>();
        // Only the LineAdded is left: the OrderPlaced went out with the drain.
        scenario.PendingEvents.RaisedExactly<LineAdded>();
    }

    [Fact]
    public void IgnorePendingEventsDropsWhatTheArrangeStepRaised()
    {
        var scenario = AggregateScenario.Given(Confirmed()).IgnorePendingEvents();

        scenario.PendingEvents.RaisedNothing();

        scenario.When(o => o.Cancel("out of stock")).RaisedExactly<OrderCancelled>();
        scenario.PendingEvents.RaisedExactly<OrderCancelled>();
    }

    [Fact]
    public void AnActionThatDrainsTheAggregateIsReportedRatherThanGuessedAt()
    {
        var sloppy = new SloppyOrder(SloppyOrderId.CreateUnique());
        sloppy.Raise(new OrderCancelled(OrderId.Empty, "first"));

        var act = () => AggregateScenario.Given(sloppy).When(o => o.DrainItself());

        act.Should().Throw<AggregateAssertionException>()
            .WithMessage("*removed events*PendingEvents*");
    }

    // ---------------------------------------------------------------- the short form

    [Fact]
    public void TheExtensionsCoverATestThatDoesNotNeedAScenario()
    {
        var order = Draft();

        order.Cancel("out of stock");

        order.PendingEvents().RaisedExactly<OrderPlaced, OrderCancelled>();
        order.DrainEvents().RaisedExactly<OrderPlaced, OrderCancelled>();
        order.PendingEvents().RaisedNothing();
    }

    [Fact]
    public void AsScenarioIsTheSameThingAsGiven()
    {
        var order = Draft();

        order.AsScenario()
            .When(o => o.Cancel("out of stock"))
            .RaisedExactly<OrderCancelled>();
    }

    [Fact]
    public void PendingEventsDoesNotTakeTheEventsOffTheAggregate()
    {
        var order = Draft();

        order.PendingEvents().RaisedExactly<OrderPlaced>();

        ((IHasDomainEvents)order).DomainEvents.Should().ContainSingle();
    }

    // ---------------------------------------------------------------- asynchronous methods

    [Fact]
    public async Task AnAsynchronousMethodIsActedOnTheSameWay()
    {
        var order = Draft();

        var raised = await AggregateScenario.Given(order)
            .WhenAsync(async o =>
            {
                await Task.Yield();
                o.AddLine(Sku.Of("SKU-1"), 1);
            });

        raised.RaisedExactly<LineAdded>();
    }

    [Fact]
    public async Task AnAsynchronousMethodCanBeAssertedToThrow()
    {
        var order = Draft();

        var thrown = await AggregateScenario.Given(order)
            .WhenThrowsAsync<InvalidOperationException>(async o =>
            {
                await Task.Yield();
                o.Confirm();
            });

        thrown.Message.Should().Be("An order with no lines cannot be confirmed.");
    }
}
