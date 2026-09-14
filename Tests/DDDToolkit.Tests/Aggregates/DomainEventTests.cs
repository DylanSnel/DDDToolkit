using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.Aggregates;

/// <summary>
/// What an event knows about itself. HANDOFF 2.5: the old <c>IDomainEvent</c> was empty, so every
/// consumer had to reconstruct identity and timing from context. These pin the replacement.
/// </summary>
public class DomainEventTests
{
    [Fact]
    public void EveryInstanceGetsItsOwnEventId()
    {
        var id = BasketId.CreateUnique();

        var ids = Enumerable.Range(0, 100).Select(_ => new BasketOpened(id).EventId).ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().NotContain(Guid.Empty);
    }

    [Fact]
    public void EventIdHasTheVersion7Layout()
    {
        var eventId = new BasketOpened(BasketId.CreateUnique()).EventId;

        eventId.Version.Should().Be(7, "time-ordered ids keep event tables and outboxes index-friendly");

        // Read the layout out of the bytes rather than trusting the Guid helpers, so this keeps
        // meaning if the runtime's accessors change shape.
        var bytes = eventId.ToByteArray(bigEndian: true);
        (bytes[6] >> 4).Should().Be(7, "RFC 9562 version nibble");
        (bytes[8] >> 6).Should().Be(0b10, "RFC 9562 variant bits");
    }

    [Fact]
    public void EventIdCarriesTheCreationTimestamp()
    {
        // The first 48 bits of a version 7 Guid are the Unix millisecond timestamp. That is what makes
        // the id time-ordered, and it is worth knowing it really is the time the event was raised.
        var before = DateTimeOffset.UtcNow.AddSeconds(-10);
        var eventId = new BasketOpened(BasketId.CreateUnique()).EventId;
        var after = DateTimeOffset.UtcNow.AddSeconds(10);

        var bytes = eventId.ToByteArray(bigEndian: true);
        var milliseconds = ((long)bytes[0] << 40)
            | ((long)bytes[1] << 32)
            | ((long)bytes[2] << 24)
            | ((long)bytes[3] << 16)
            | ((long)bytes[4] << 8)
            | bytes[5];

        DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            .Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void OccurredAtIsStampedWithUtcNow()
    {
        var before = DateTimeOffset.UtcNow;

        var raised = new BasketOpened(BasketId.CreateUnique());

        var after = DateTimeOffset.UtcNow;
        raised.OccurredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        raised.OccurredAt.Offset.Should().Be(TimeSpan.Zero, "UtcNow carries a zero offset");
    }

    [Fact]
    public void InitialisersOverrideTheDefaults()
    {
        // Replays and deterministic tests need to supply both values.
        var eventId = Guid.Parse("0194f0a0-1111-7000-8000-000000000001");
        var occurredAt = new DateTimeOffset(2024, 1, 21, 17, 34, 7, TimeSpan.Zero);

        var raised = new BasketOpened(BasketId.CreateUnique())
        {
            EventId = eventId,
            OccurredAt = occurredAt,
        };

        raised.EventId.Should().Be(eventId);
        raised.OccurredAt.Should().Be(occurredAt);
    }

    [Fact]
    public void RecordEqualityIncludesTheEventId()
    {
        var id = BasketId.CreateUnique();
        var left = new BasketOpened(id);
        var right = new BasketOpened(id);

        left.Should().NotBe(right, "same payload, different occurrence");

        var sameOccurrence = right with { EventId = left.EventId, OccurredAt = left.OccurredAt };
        sameOccurrence.Should().Be(left);
        sameOccurrence.GetHashCode().Should().Be(left.GetHashCode());

        (left with { }).Should().Be(left, "a plain copy is the same occurrence");
    }

    [Fact]
    public void RecordEqualityIncludesThePayload()
    {
        var left = new LineAdded(BasketId.CreateUnique(), BasketLineId.Create(1));
        var right = left with { LineId = BasketLineId.Create(2) };

        right.Should().NotBe(left);
        right.EventId.Should().Be(left.EventId, "'with' copies the occurrence identity");
    }

    [Fact]
    public void DomainEventSatisfiesIDomainEvent()
    {
        IDomainEvent raised = new BasketOpened(BasketId.CreateUnique());

        raised.EventId.Should().NotBe(Guid.Empty);
        raised.OccurredAt.Should().NotBe(default(DateTimeOffset));
        typeof(DomainEvent).Should().BeAssignableTo<IDomainEvent>();
    }
}
