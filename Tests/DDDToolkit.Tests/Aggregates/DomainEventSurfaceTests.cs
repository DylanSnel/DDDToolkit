using System.Reflection;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.Aggregates;

/// <summary>
/// The event surface of an aggregate root: who may raise, who may drain, and what a child entity gets
/// (nothing). This is the plumbing HANDOFF 2.6 and 2.8 called out as both wrong and untested.
/// </summary>
public class DomainEventSurfaceTests
{
    // ---------------------------------------------------------------- raising

    [Fact]
    public void ConstructorRaisesItsEvent()
    {
        var id = BasketId.CreateUnique();

        var basket = new Basket(id, "ada");

        basket.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BasketOpened>()
            .Which.BasketId.Should().Be(id);
    }

    [Fact]
    public void EventsArriveInTheOrderTheyWereRaised()
    {
        var basket = new Basket(BasketId.CreateUnique(), "ada");
        var first = new BasketLine(BasketLineId.Create(1), Sku.Create("A"), 1);
        var second = new BasketLine(BasketLineId.Create(2), Sku.Create("B"), 2);

        basket.AddLine(first);
        basket.AddLine(second);
        basket.RemoveLine(first.Id);

        basket.DomainEvents.Select(e => e.GetType()).Should().Equal(
            typeof(BasketOpened),
            typeof(LineAdded),
            typeof(LineAdded),
            typeof(LineRemoved));

        basket.DomainEvents.OfType<LineAdded>().Select(e => e.LineId)
            .Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public void RemovingSomethingAbsentRaisesNothing()
    {
        var basket = new Basket(BasketId.CreateUnique(), "ada");

        basket.RemoveLine(BasketLineId.Create(404)).Should().BeFalse();

        basket.DomainEvents.Should().ContainSingle("only the BasketOpened from the constructor");
    }

    [Fact]
    public void RaiseDomainEventIsProtected()
    {
        var raise = typeof(AggregateRoot<BasketId>).GetMethod(
            "RaiseDomainEvent",
            BindingFlags.Instance | BindingFlags.NonPublic);

        raise.Should().NotBeNull("aggregates raise their own events");
        raise!.IsFamily.Should().BeTrue("protected, so nothing outside the aggregate can inject an event");

        typeof(AggregateRoot<BasketId>).GetMethod("RaiseDomainEvent", BindingFlags.Instance | BindingFlags.Public)
            .Should().BeNull();
        typeof(Basket).GetMethod("AddDomainEvent", BindingFlags.Instance | BindingFlags.Public)
            .Should().BeNull("the old public AddDomainEvent is gone");
    }

    [Fact]
    public void RaiseDomainEventRejectsNull()
    {
        var probe = new EventProbe(BasketId.CreateUnique());

        var act = () => probe.Raise(null!);

        act.Should().Throw<ArgumentNullException>();
        probe.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AnyIDomainEventCanBeRaised()
    {
        var probe = new EventProbe(BasketId.CreateUnique());
        var handRolled = new HandRolledEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        probe.Raise(handRolled);

        probe.DomainEvents.Should().ContainSingle().Which.Should().BeSameAs(handRolled);
    }

    // ---------------------------------------------------------------- the read-only view

    [Fact]
    public void DomainEventsIsAReadOnlyViewThatTracksLaterRaises()
    {
        var probe = new EventProbe(BasketId.CreateUnique());
        var view = probe.DomainEvents;

        view.Should().BeEmpty();

        probe.Raise(new NamedBaseEvent());
        view.Should().ContainSingle("the view is a window onto the live list, not a snapshot");

        probe.Raise(new NamedBaseEvent());
        view.Should().HaveCount(2);
    }

    [Fact]
    public void DomainEventsCannotBeMutatedThroughTheView()
    {
        var probe = new EventProbe(BasketId.CreateUnique());

        (probe.DomainEvents as List<IDomainEvent>).Should().BeNull("it must not be the backing list");

        var act = () => ((IList<IDomainEvent>)probe.DomainEvents).Add(new NamedBaseEvent());

        act.Should().Throw<NotSupportedException>();
    }

    // ---------------------------------------------------------------- draining

    [Fact]
    public void DequeueReturnsEventsInOrderAndEmptiesTheAggregate()
    {
        var basket = new Basket(BasketId.CreateUnique(), "ada");
        basket.AddLine(new BasketLine(BasketLineId.Create(1), Sku.Create("A"), 1));

        var drained = ((IHasDomainEvents)basket).DequeueDomainEvents();

        drained.Select(e => e.GetType()).Should().Equal(typeof(BasketOpened), typeof(LineAdded));
        basket.DomainEvents.Should().BeEmpty("dequeue takes the events away");
        ((IHasDomainEvents)basket).DequeueDomainEvents().Should().BeEmpty("a second drain finds nothing");
    }

    [Fact]
    public void DequeuedEventsAreDetachedFromTheAggregate()
    {
        var probe = new EventProbe(BasketId.CreateUnique());
        probe.Raise(new NamedBaseEvent());

        var drained = ((IHasDomainEvents)probe).DequeueDomainEvents();
        probe.Raise(new NamedBaseEvent());

        drained.Should().ContainSingle("the returned batch is a copy, not the live list");
        probe.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void ClearEmptiesWithoutReturningAnything()
    {
        var basket = new Basket(BasketId.CreateUnique(), "ada");
        basket.AddLine(new BasketLine(BasketLineId.Create(1), Sku.Create("A"), 1));

        ((IHasDomainEvents)basket).ClearDomainEvents();

        basket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void DrainingIsNotPartOfThePublicSurface()
    {
        // Explicit interface implementation: reachable by the persistence layer, which knows about
        // IHasDomainEvents, and by nobody who only has a Basket.
        typeof(Basket).GetMethod("DequeueDomainEvents", BindingFlags.Instance | BindingFlags.Public)
            .Should().BeNull();
        typeof(Basket).GetMethod("ClearDomainEvents", BindingFlags.Instance | BindingFlags.Public)
            .Should().BeNull();

        typeof(AggregateRoot<BasketId>).GetInterfaceMap(typeof(IHasDomainEvents))
            .TargetMethods.Where(m => m.Name.Contains("DequeueDomainEvents") || m.Name.Contains("ClearDomainEvents"))
            .Should().OnlyContain(m => m.IsPrivate, "explicit implementations compile to private methods");
    }

    // ---------------------------------------------------------------- child entities

    [Fact]
    public void ChildEntitiesHaveNoEventSurfaceAtAll()
    {
        typeof(BasketLine).Should().NotBeAssignableTo<IHasDomainEvents>();
        typeof(BasketLine).GetProperty("DomainEvents").Should().BeNull();
        typeof(BasketLine).GetMethod("RaiseDomainEvent", BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().BeNull("only the root raises events");
        typeof(BasketLine).GetProperty("Version").Should().BeNull("only the root is versioned");
    }

    [Fact]
    public void RootsCarryTheEventAndVersionSurface()
    {
        typeof(Basket).Should().BeAssignableTo<IHasDomainEvents>();
        typeof(Basket).GetProperty("DomainEvents").Should().NotBeNull();
        typeof(Basket).GetProperty("Version").Should().NotBeNull();
    }

    // ---------------------------------------------------------------- version

    [Fact]
    public void VersionStartsAtZeroAndIsNotWritableFromOutside()
    {
        var basket = new Basket(BasketId.CreateUnique(), "ada");

        basket.Version.Should().Be(0, "a brand new aggregate has never been saved");

        basket.AddLine(new BasketLine(BasketLineId.Create(1), Sku.Create("A"), 1));
        basket.Version.Should().Be(0, "the domain layer never touches Version; the persistence layer owns it");

        var setter = typeof(AggregateRoot<BasketId>).GetProperty("Version")!.SetMethod;
        setter.Should().NotBeNull();
        setter!.IsPrivate.Should().BeTrue("EF Core writes it by reflection; application code must not");
    }
}
