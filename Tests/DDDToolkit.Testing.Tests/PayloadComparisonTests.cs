using DDDToolkit.Testing.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Testing.Tests;

/// <summary>
/// What "the same event" means. Record equality cannot answer that question for a domain event: two
/// events with identical payloads always have different <c>EventId</c>s, so <c>Equals</c> is always
/// false. These tests pin the comparison the kit uses instead.
/// </summary>
public class PayloadComparisonTests
{
    private static SloppyOrder Probe() => new(SloppyOrderId.CreateUnique());

    [Fact]
    public void RecordEqualityIsUselessHereWhichIsWhyThePayloadComparisonExists()
    {
        var id = OrderId.CreateUnique();

        var first = new OrderCancelled(id, "out of stock");
        var second = new OrderCancelled(id, "out of stock");

        first.Should().NotBe(second, "EventId and OccurredAt differ on every instance");

        var probe = Probe();
        probe.AsScenario().When(o => o.Raise(first)).Raised(second);
    }

    [Fact]
    public void AnExplicitlySuppliedTimestampIsStillIgnored()
    {
        var id = OrderId.CreateUnique();
        var raised = new OrderCancelled(id, "out of stock")
        {
            OccurredAt = DateTimeOffset.UnixEpoch,
            EventId = Guid.Empty,
        };

        Probe().AsScenario()
            .When(o => o.Raise(raised))
            .Raised(new OrderCancelled(id, "out of stock"));
    }

    [Fact]
    public void MetadataIsIgnoredOnAnEventThatImplementsTheInterfaceItself()
    {
        var raised = new HandRolledEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, "written by hand");

        Probe().AsScenario()
            .When(o => o.Raise(raised))
            .Raised(new HandRolledEvent(Guid.Empty, DateTimeOffset.MinValue, "written by hand"));
    }

    [Fact]
    public void APayloadDifferenceOnAHandRolledEventStillFails()
    {
        var raised = new HandRolledEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, "written by hand");

        var act = () => Probe().AsScenario()
            .When(o => o.Raise(raised))
            .Raised(new HandRolledEvent(Guid.Empty, DateTimeOffset.MinValue, "typed by hand"));

        act.Should().Throw<AggregateAssertionException>()
            .WithMessage("*Note was \"written by hand\", expected \"typed by hand\"*");
    }

    [Fact]
    public void CollectionsAreComparedElementByElement()
    {
        var id = OrderId.CreateUnique();
        var raised = new BatchShipped(id, [Sku.Of("SKU-1"), Sku.Of("SKU-2")]);

        Probe().AsScenario()
            .When(o => o.Raise(raised))
            .Raised(new BatchShipped(id, new List<Sku> { Sku.Of("SKU-1"), Sku.Of("SKU-2") }));
    }

    [Fact]
    public void ADifferentCollectionIsADifferentPayload()
    {
        var id = OrderId.CreateUnique();
        var raised = new BatchShipped(id, [Sku.Of("SKU-1"), Sku.Of("SKU-2")]);

        var act = () => Probe().AsScenario()
            .When(o => o.Raise(raised))
            .Raised(new BatchShipped(id, [Sku.Of("SKU-1")]));

        act.Should().Throw<AggregateAssertionException>()
            .WithMessage("*Skus was [SKU_SKU-1, SKU_SKU-2], expected [SKU_SKU-1]*");
    }

    [Fact]
    public void ExactlyCompareByRuntimeTypeSoABaseTypeDoesNotStandInForADerivedOne()
    {
        var probe = Probe();

        var act = () => probe.AsScenario()
            .When(o => o.Raise(new SpecialOrderCancelled(OrderId.Empty, "out of stock", "gold")))
            .RaisedExactly<OrderCancelled>();

        act.Should().Throw<AggregateAssertionException>(
            "RaisedExactly is about the sequence of event types that were actually raised");
    }

    [Fact]
    public void PresenceAssertionsDoAcceptADerivedEvent()
    {
        var probe = Probe();

        probe.AsScenario()
            .When(o => o.Raise(new SpecialOrderCancelled(OrderId.Empty, "out of stock", "gold")))
            .Raised<OrderCancelled>();
    }
}
