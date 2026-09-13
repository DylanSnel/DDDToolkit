using DDDToolkit.Testing.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Testing.Tests;

/// <summary>
/// A testing package is only worth having if its failures explain themselves. Every test here makes
/// an assertion fail on purpose and reads the message.
/// </summary>
public class FailureMessageTests
{
    private static Order Draft() => new(OrderId.CreateUnique(), CustomerId.CreateUnique());

    private static string MessageOf(Action assertion)
    {
        var failure = Record.Exception(assertion);

        failure.Should().BeOfType<AggregateAssertionException>("the kit reports its own failures");
        return failure!.Message;
    }

    [Fact]
    public void AMissingEventNamesWhatWasExpectedAndListsWhatArrived()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 2))
            .Raised<OrderConfirmed>());

        message.Should().Contain("Expected Order to raise OrderConfirmed, but it did not.");
        message.Should().Contain("1 event was raised while running the action:");
        message.Should().Contain("[0] LineAdded { OrderId = ").And.Contain("Sku = SKU_SKU-1, Quantity = 2 }");
    }

    [Fact]
    public void AMissingEventInAnEmptyBatchSaysSoRatherThanPrintingAnEmptyList()
    {
        var order = Draft();
        order.AddLine(Sku.Of("SKU-1"), 1);
        order.Confirm();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o => o.Confirm())
            .Raised<OrderConfirmed>());

        message.Should().Contain("No events were raised while running the action.");
    }

    [Fact]
    public void AFailedPredicateSaysHowManyEventsOfThatTypeWereConsidered()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o =>
            {
                o.AddLine(Sku.Of("SKU-1"), 1);
                o.AddLine(Sku.Of("SKU-2"), 2);
            })
            .Raised<LineAdded>(added => added.Quantity == 9));

        message.Should().Contain("matching the predicate, but none of the 2 it raised matched.");
        message.Should().Contain("[1] LineAdded");
    }

    [Fact]
    public void AFailedPredicateWithNoEventOfThatTypeSaysThatInstead()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 1))
            .Raised<OrderShipped>(shipped => shipped.TrackingCode == "TRACK-1"));

        message.Should().Contain("it raised no OrderShipped at all.");
    }

    [Fact]
    public void AWrongPayloadNamesTheMemberThatDiffers()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 2))
            .Raised(new LineAdded(order.Id, Sku.Of("SKU-1"), 5)));

        message.Should().Contain("Quantity = 5 }, but no raised event carried that payload.");
        message.Should().Contain("Quantity was 2, expected 5");
        message.Should().Contain("EventId and OccurredAt are never compared.");
    }

    [Fact]
    public void AnExpectedPayloadWithNoEventOfThatTypeSaysSo()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 2))
            .Raised(new OrderCancelled(order.Id, "out of stock")));

        message.Should().Contain("It raised no OrderCancelled at all.");
    }

    [Fact]
    public void UnexpectedEventsAreListedWhenNothingWasSupposedToHappen()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 2))
            .RaisedNothing());

        message.Should().Contain("Expected Order to raise no events, but it raised 1.");
        message.Should().Contain("[0] LineAdded");
    }

    [Fact]
    public void ARuledOutEventSaysHowManyOfItArrived()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o =>
            {
                o.AddLine(Sku.Of("SKU-1"), 1);
                o.AddLine(Sku.Of("SKU-2"), 1);
            })
            .RaisedNo<LineAdded>());

        message.Should().Contain("Expected Order to raise no LineAdded, but it raised 2.");
    }

    [Fact]
    public void AWrongSequenceNamesTheIndexOfTheFirstDifference()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o =>
            {
                o.AddLine(Sku.Of("SKU-1"), 1);
                o.Confirm();
            })
            .RaisedExactly<OrderConfirmed, LineAdded>());

        message.Should().Contain("Expected Order to raise exactly OrderConfirmed, then LineAdded.");
        message.Should().Contain("The first difference is at index 0: expected OrderConfirmed, was LineAdded.");
        message.Should().Contain("2 events were raised while running the action:");
    }

    [Fact]
    public void ATooShortSequenceReportsTheIndexWhereItRanOut()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 1))
            .RaisedExactly<LineAdded, OrderConfirmed>());

        message.Should().Contain("The first difference is at index 1: expected OrderConfirmed, was nothing.");
    }

    [Fact]
    public void AWrongPayloadSequenceShowsBothSides()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o =>
            {
                o.AddLine(Sku.Of("SKU-1"), 1);
                o.Confirm();
            })
            .RaisedExactlyThese(
                new LineAdded(order.Id, Sku.Of("SKU-1"), 1),
                new OrderConfirmed(order.Id, 7)));

        message.Should().Contain("Expected Order to raise exactly these 2 events:");
        message.Should().Contain("[1] OrderConfirmed { OrderId = ").And.Contain("LineCount = 7 }");
        message.Should().Contain("The first difference is at index 1: LineCount was 1, expected 7.");
    }

    [Fact]
    public void AWrongPayloadSequenceLengthSaysHowManyArrived()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o => o.AddLine(Sku.Of("SKU-1"), 1))
            .RaisedExactlyThese(new LineAdded(order.Id, Sku.Of("SKU-1"), 1), new OrderConfirmed(order.Id, 1)));

        message.Should().Contain("It raised 1.");
    }

    [Fact]
    public void AskingForTheOneEventWhenThereAreTwoSaysHowMany()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .When(o =>
            {
                o.AddLine(Sku.Of("SKU-1"), 1);
                o.AddLine(Sku.Of("SKU-2"), 1);
            })
            .SingleEvent<LineAdded>());

        message.Should().Contain("Expected Order to raise exactly one LineAdded, but it raised 2.");
    }

    // ---------------------------------------------------------------- exceptions

    [Fact]
    public void AMethodThatDoesNotThrowSaysSoAndShowsWhatItDidInstead()
    {
        var order = Draft();

        var message = MessageOf(() => AggregateScenario.Given(order)
            .WhenThrows<InvalidOperationException>(o => o.AddLine(Sku.Of("SKU-1"), 1)));

        message.Should().Contain("Expected Order to throw InvalidOperationException, but the call returned normally.");
        message.Should().Contain("[0] LineAdded");
    }

    [Fact]
    public void TheWrongExceptionNamesBothTypesAndKeepsTheOriginal()
    {
        var order = Draft();

        var failure = Record.Exception(() => AggregateScenario.Given(order)
            .WhenThrows<InvalidOperationException>(o => o.AddLine(Sku.Of("SKU-1"), 0)));

        failure.Should().BeOfType<AggregateAssertionException>();
        failure!.Message.Should().Contain(
            "Expected Order to throw InvalidOperationException, but it threw ArgumentOutOfRangeException:");
        failure.InnerException.Should().BeOfType<ArgumentOutOfRangeException>(
            "the original stack trace is the useful part of the failure");
    }

    [Fact]
    public void AnAggregateThatRaisesBeforeItThrowsIsReportedInFull()
    {
        var sloppy = new SloppyOrder(SloppyOrderId.CreateUnique());

        var failure = Record.Exception(() => AggregateScenario.Given(sloppy)
            .WhenThrows<InvalidOperationException>(o => o.Cancel("changed my mind")));

        failure.Should().BeOfType<AggregateAssertionException>();
        failure!.Message.Should().Contain("SloppyOrder threw InvalidOperationException as expected, but it raised 1 event first.");
        failure.Message.Should().Contain("describing something that never happened");
        failure.Message.Should().Contain("[0] OrderCancelled { OrderId = ").And.Contain("Reason = \"changed my mind\" }");
        failure.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    // ---------------------------------------------------------------- rendering

    [Fact]
    public void EventMetadataIsKeptOutOfTheRenderedEvents()
    {
        var order = Draft();
        var raised = order.PendingEvents();

        var rendered = raised.ToString();

        rendered.Should().StartWith("1 event was pending on the aggregate:");
        rendered.Should().Contain("OrderPlaced { OrderId = ");
        rendered.Should().NotContain("EventId", "the identity of the occurrence is noise in a failure message");
        rendered.Should().NotContain("OccurredAt");
    }
}
