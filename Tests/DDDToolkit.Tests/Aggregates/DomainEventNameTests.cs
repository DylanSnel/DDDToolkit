using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.Aggregates;

/// <summary>
/// <c>DomainEventName.Of</c> is the piece that lets an event survive a class rename once it has been
/// written to a queue or a table. These pin the fallback, the override and the deliberate lack of
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
    public void UnattributedEventsFallBackToTheClassName()
    {
        DomainEventName.Of(typeof(LineAdded)).Should().Be(nameof(LineAdded));
        DomainEventName.Of<LineAdded>().Should().Be("LineAdded");
        DomainEventName.Of(new LineAdded(BasketId.CreateUnique(), BasketLineId.Create(1)))
            .Should().Be("LineAdded");
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
        DomainEventName.Of<DerivedFromNamedEvent>().Should().Be(nameof(DerivedFromNamedEvent));
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
}
