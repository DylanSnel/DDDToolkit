using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.Aggregates;

/// <summary>
/// <c>DomainEventName.Of</c> is the piece that lets an event survive a class rename once it has been
/// written to a queue or a table. These pin the convention, the override and the deliberate lack of
/// inheritance.
/// </summary>
public class DomainEventNameTests
{
    [Fact]
    public void AttributedEventsUseTheirStableName()
    {
        DomainEventName.Of(typeof(BasketOpened)).Should().Be("test.basket-opened");
        DomainEventName.Of<BasketOpened>().Should().Be("test.basket-opened");
        DomainEventName.Of(new BasketOpened(BasketId.CreateUnique())).Should().Be("test.basket-opened");
    }

    [Fact]
    public void UnattributedEventsAreNamedByConvention()
    {
        // This assembly declares no [assembly: Module], so the name is the class name in kebab case alone.
        DomainEventName.Of(typeof(LineAdded)).Should().Be("line-added");
        DomainEventName.Of<LineAdded>().Should().Be("line-added");
        DomainEventName.Of(new LineAdded(BasketId.CreateUnique(), BasketLineId.Create(1)))
            .Should().Be("line-added");
    }

    [Fact]
    public void ThePinnedNameWinsOverTheConvention()
    {
        DomainEventName.Of<BasketOpened>().Should().Be("test.basket-opened");
        DomainEventName.ConventionalNameOf(typeof(BasketOpened)).Should().Be("basket-opened", "the convention is still there to ask for");
    }

    [Theory]
    [InlineData(typeof(Spelling.OrderPlaced), "order-placed")]
    [InlineData(typeof(Spelling.HTTPRequestSent), "http-request-sent")]
    [InlineData(typeof(Spelling.Ipv6AddressChanged), "ipv6-address-changed")]
    [InlineData(typeof(Spelling.Level2Reached), "level2-reached")]
    [InlineData(typeof(Spelling.Stock_Counted), "stock-counted")]
    [InlineData(typeof(Spelling.OrderPlacedV2), "order-placed")]
    [InlineData(typeof(Spelling.OrderPlacedV12), "order-placed")]
    [InlineData(typeof(Spelling.SnapshotV0), "snapshot-v0")]
    [InlineData(typeof(Spelling.SnapshotV01), "snapshot-v01")]
    [InlineData(typeof(Spelling.V2), "v2")]
    public void TheConventionSpellsTheClassNameInKebabCase(Type type, string expected)
        => DomainEventName.ConventionalNameOf(type).Should().Be(expected);

    [Theory]
    [InlineData(typeof(Spelling.OrderPlacedV2), 2)]
    [InlineData(typeof(Spelling.OrderPlacedV12), 12)]
    [InlineData(typeof(Spelling.OrderPlaced), null)]
    [InlineData(typeof(Spelling.Level2Reached), null)]
    [InlineData(typeof(Spelling.SnapshotV0), null)]
    [InlineData(typeof(Spelling.SnapshotV01), null)]
    [InlineData(typeof(Spelling.V2), null)]
    public void AClassNameEndingInVAndANumberCarriesTheVersion(Type type, int? expected)
    {
        // V0 and V01 cannot be versions, so they stay part of the name; the analyzer refuses them on an
        // event (DDD00035), and this is what code compiled without it gets.
        DomainEventName.VersionSuffixOf(type).Should().Be(expected);
    }

    [Fact]
    public void EveryVersionOfAnEventSharesOneName()
    {
        IntegrationEventContract.NameOf<Spelling.OrderPlacedV2>().Should().Be(IntegrationEventContract.NameOf<Spelling.OrderPlaced>());
        IntegrationEventContract.VersionOf<Spelling.OrderPlaced>().Should().Be(1);
        IntegrationEventContract.VersionOf<Spelling.OrderPlacedV2>().Should().Be(2);
    }

    [Fact]
    public void TheVersionComesFromTheNameBeforeTheAttribute()
    {
        IntegrationEventContract.VersionOf<Spelling.ReservedV3>().Should().Be(3, "the suffix and Version agree");
        IntegrationEventContract.VersionOf<Spelling.Released>().Should().Be(4, "a name without a suffix leaves it to [IntegrationEvent(Version = 4)]");
        IntegrationEventContract.NameOf<Spelling.Released>().Should().Be("released", "[IntegrationEvent] without a name leaves the name to the convention");
    }

    [Fact]
    public void TheBrokerEntityNameIsTheNameAndTheVersion()
    {
        IntegrationEventContract.EntityNameOf<Spelling.OrderPlacedV2>().Should().Be("order-placed.v2");
        IntegrationEventContract.EntityNameOf<BasketOpened>().Should().Be("test.basket-opened.v1");
    }

    [Fact]
    public void OnlyTheToolkitsOwnEventsAreRenamedOnABroker()
    {
        IntegrationEventContract.IsNamedByToolkit(typeof(LineAdded)).Should().BeTrue("a domain event");
        IntegrationEventContract.IsNamedByToolkit(typeof(Spelling.Released)).Should().BeTrue("[IntegrationEvent]");
        IntegrationEventContract.IsNamedByToolkit(typeof(Spelling.OrderPlaced)).Should().BeFalse("a class nothing marks is some other message");
    }

    [Fact]
    public void TheNameIsResolvedFromTheRuntimeTypeNotTheStaticOne()
    {
        IDomainEvent raised = new BasketOpened(BasketId.CreateUnique());

        DomainEventName.Of(raised).Should().Be("test.basket-opened");
    }

    [Fact]
    public void TheNameIsNotInherited()
    {
        // DECISION (pinned): Of() reads the attribute with inherit:false. A derived event is a
        // different contract on the wire, so it must be named deliberately rather than silently
        // inheriting its base's name.
        DomainEventName.Of<NamedBaseEvent>().Should().Be("test.named-base");
        DomainEventName.Of<DerivedFromNamedEvent>().Should().Be("derived-from-named-event");
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        var fromType = () => DomainEventName.Of((Type)null!);
        var fromInstance = () => DomainEventName.Of((IDomainEvent)null!);

        fromType.Should().Throw<ArgumentNullException>();
        fromInstance.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AnEmptyNameIsRejectedAtTheAttribute()
    {
        var empty = () => new DomainEventNameAttribute("");
        var whitespace = () => new DomainEventNameAttribute("   ");

        empty.Should().Throw<ArgumentException>();
        whitespace.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Class names to spell. Plain classes rather than events, except where a test needs the attributes, so
    /// the analyzer has nothing to say about the ones that are deliberately wrong.
    /// </summary>
    private static class Spelling
    {
        public sealed class OrderPlaced;

        public sealed class OrderPlacedV2;

        public sealed class OrderPlacedV12;

        public sealed class HTTPRequestSent;

        public sealed class Ipv6AddressChanged;

        public sealed class Level2Reached;

        public sealed class Stock_Counted;

        public sealed class SnapshotV0;

        public sealed class SnapshotV01;

        public sealed class V2;

        [IntegrationEvent(Version = 3)]
        public sealed class ReservedV3;

        [IntegrationEvent(Version = 4)]
        public sealed class Released;
    }
}
