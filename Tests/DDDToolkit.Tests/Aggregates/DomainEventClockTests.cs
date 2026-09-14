using DDDToolkit.BaseTypes;
using DDDToolkit.Tests.Domain;
using FluentAssertions;

namespace DDDToolkit.Tests.Aggregates;

/// <summary>
/// The seam that makes an event's timestamp and id deterministic.
/// <para>
/// <c>OccurredAt</c> and <c>EventId</c> are <c>init</c>, so whoever writes <c>new BasketOpened(id)</c>
/// can set them. That is not the test's code: the aggregate raises its own events, so a test that
/// calls <c>basket.AddLine(line)</c> never reaches the constructor. <see cref="DomainEventClock"/>
/// closes that gap, and these tests pin both the behaviour and the isolation that makes it safe to
/// use while other tests run alongside.
/// </para>
/// </summary>
public class DomainEventClockTests
{
    private static readonly DateTimeOffset Noon = new(2024, 1, 21, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- the default

    [Fact]
    public void WithoutAScopeTheClockIsTheSystemClock()
    {
        DomainEventClock.Current.Should().BeSameAs(TimeProvider.System);

        var before = DateTimeOffset.UtcNow;
        var raised = new BasketOpened(BasketId.CreateUnique());
        var after = DateTimeOffset.UtcNow;

        raised.OccurredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ---------------------------------------------------------------- what init alone cannot do

    [Fact]
    public void EventsRaisedInsideAnAggregateTakeTheScopedTime()
    {
        // The whole point: nothing in this test constructs BasketOpened or LineAdded, so no
        // initialiser could have supplied these values.
        var clock = new ManualClock(Noon);

        using var scope = DomainEventClock.Use(clock);

        var basket = new Basket(BasketId.CreateUnique(), "ada");
        basket.AddLine(new BasketLine(BasketLineId.Create(1), Sku.Create("A"), 1));

        basket.DomainEvents.Should().HaveCount(2);
        basket.DomainEvents.Should().OnlyContain(e => e.OccurredAt == Noon);
    }

    [Fact]
    public void AdvancingTheClockMovesLaterEvents()
    {
        var clock = new ManualClock(Noon);

        using var scope = DomainEventClock.Use(clock);

        var basket = new Basket(BasketId.CreateUnique(), "ada");
        clock.Advance(TimeSpan.FromMinutes(5));
        basket.AddLine(new BasketLine(BasketLineId.Create(1), Sku.Create("A"), 1));
        clock.Advance(TimeSpan.FromMinutes(5));
        basket.RemoveLine(BasketLineId.Create(1));

        basket.DomainEvents.Select(e => e.OccurredAt).Should().Equal(
            Noon,
            Noon.AddMinutes(5),
            Noon.AddMinutes(10));
    }

    [Fact]
    public void AnInitialiserStillWinsOverTheScopedClock()
    {
        var replayed = new DateTimeOffset(2019, 6, 1, 8, 30, 0, TimeSpan.Zero);
        var replayedId = Guid.Parse("0194f0a0-1111-7000-8000-000000000001");

        using var scope = DomainEventClock.Use(new ManualClock(Noon));

        var raised = new BasketOpened(BasketId.CreateUnique())
        {
            EventId = replayedId,
            OccurredAt = replayed,
        };

        raised.OccurredAt.Should().Be(replayed);
        raised.EventId.Should().Be(replayedId);
    }

    // ---------------------------------------------------------------- the event id

    [Fact]
    public void TheEventIdIsStillUniqueUnderAFrozenClock()
    {
        using var scope = DomainEventClock.Use(new ManualClock(Noon));

        var ids = Enumerable.Range(0, 100)
            .Select(_ => new BasketOpened(BasketId.CreateUnique()).EventId)
            .ToList();

        ids.Should().OnlyHaveUniqueItems("the clock fixes the ordering half of a version 7 id, not the random half");
        ids.Should().OnlyContain(id => id.Version == 7);
    }

    [Fact]
    public void TheEventIdCarriesTheScopedTime()
    {
        using var scope = DomainEventClock.Use(new ManualClock(Noon));

        var eventId = new BasketOpened(BasketId.CreateUnique()).EventId;

        TimestampOf(eventId).Should().Be(Noon, "a frozen clock must make the id sort at the frozen instant");
    }

    [Fact]
    public void IdsSortInTheOrderTheFakeClockRanEvenWhenTheRealClockDoesNot()
    {
        var clock = new ManualClock(Noon);

        using var scope = DomainEventClock.Use(clock);

        var first = new BasketOpened(BasketId.CreateUnique()).EventId;
        clock.Advance(TimeSpan.FromDays(1));
        var second = new BasketOpened(BasketId.CreateUnique()).EventId;

        TimestampOf(second).Should().Be(TimestampOf(first).AddDays(1));
    }

    [Fact]
    public void AScopeMaySupplyTheEventIdsOutright()
    {
        var counter = 0;
        var clock = new ManualClock(Noon);

        using var scope = DomainEventClock.Use(clock, _ => new Guid(++counter, 0, 0, new byte[8]));

        var basket = new Basket(BasketId.CreateUnique(), "ada");
        basket.AddLine(new BasketLine(BasketLineId.Create(1), Sku.Create("A"), 1));

        basket.DomainEvents.Select(e => e.EventId).Should().Equal(
            new Guid(1, 0, 0, new byte[8]),
            new Guid(2, 0, 0, new byte[8]));
    }

    [Fact]
    public void TheIdFactoryIsHandedTheScopedTime()
    {
        var seen = new List<DateTimeOffset>();
        var clock = new ManualClock(Noon);

        using (DomainEventClock.Use(clock, when => { seen.Add(when); return Guid.NewGuid(); }))
        {
            _ = new BasketOpened(BasketId.CreateUnique());
            clock.Advance(TimeSpan.FromHours(2));
            _ = new BasketOpened(BasketId.CreateUnique());
        }

        seen.Should().Equal(Noon, Noon.AddHours(2));
    }

    [Fact]
    public void AClockBeforeTheUnixEpochIsRefusedRatherThanSilentlyWrong()
    {
        // Guid version 7 encodes Unix milliseconds, so it cannot represent 1969. Say so loudly:
        // the alternative is an id whose ordering is nonsense.
        using var scope = DomainEventClock.Use(new ManualClock(new DateTimeOffset(1969, 7, 20, 20, 17, 0, TimeSpan.Zero)));

        var act = () => new BasketOpened(BasketId.CreateUnique());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------- the scope

    [Fact]
    public void DisposingRestoresTheSystemClock()
    {
        using (DomainEventClock.Use(new ManualClock(Noon)))
        {
            DomainEventClock.Current.Should().NotBeSameAs(TimeProvider.System);
        }

        DomainEventClock.Current.Should().BeSameAs(TimeProvider.System);
        new BasketOpened(BasketId.CreateUnique()).OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ScopesNest()
    {
        var outer = new ManualClock(Noon);
        var inner = new ManualClock(Noon.AddYears(1));

        using (DomainEventClock.Use(outer))
        {
            new BasketOpened(BasketId.CreateUnique()).OccurredAt.Should().Be(Noon);

            using (DomainEventClock.Use(inner))
            {
                new BasketOpened(BasketId.CreateUnique()).OccurredAt.Should().Be(Noon.AddYears(1));
            }

            new BasketOpened(BasketId.CreateUnique()).OccurredAt.Should().Be(Noon, "the outer scope is back");
        }
    }

    [Fact]
    public void DisposingTwiceDoesNotUndoAnUnrelatedScope()
    {
        var scope = DomainEventClock.Use(new ManualClock(Noon));
        scope.Dispose();

        using var later = DomainEventClock.Use(new ManualClock(Noon.AddYears(1)));
        scope.Dispose();

        DomainEventClock.Current.GetUtcNow().Should().Be(Noon.AddYears(1));
    }

    [Fact]
    public void UseRejectsNull()
    {
        var act = () => DomainEventClock.Use(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------- isolation

    [Fact]
    public async Task TheScopeFlowsAcrossAwait()
    {
        using var scope = DomainEventClock.Use(new ManualClock(Noon));

        await Task.Yield();
        await Task.Delay(1, TestContext.Current.CancellationToken);

        await Task.Run(() => new BasketOpened(BasketId.CreateUnique()).OccurredAt.Should().Be(Noon), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConcurrentFlowsDoNotSeeEachOthersClock()
    {
        // This is why the clock is not a mutable static. Ten flows, ten different times, all running
        // at once: a static setter would hand every one of them whichever value was written last.
        var times = Enumerable.Range(0, 10).Select(i => Noon.AddDays(i)).ToList();

        var raised = await Task.WhenAll(times.Select(time => Task.Run(async () =>
        {
            using var scope = DomainEventClock.Use(new ManualClock(time));

            await Task.Delay(5, TestContext.Current.CancellationToken);
            return new Basket(BasketId.CreateUnique(), "ada").DomainEvents.Single().OccurredAt;
        }, TestContext.Current.CancellationToken)));

        raised.Should().Equal(times);
    }

    [Fact]
    public async Task AScopeDoesNotLeakOutOfTheFlowThatOpenedIt()
    {
        await Task.Run(() =>
        {
            using var scope = DomainEventClock.Use(new ManualClock(Noon));
            DomainEventClock.Current.GetUtcNow().Should().Be(Noon);
        }, TestContext.Current.CancellationToken);

        DomainEventClock.Current.Should().BeSameAs(TimeProvider.System, "the caller never entered a scope");
    }

    /// <summary>Reads the Unix millisecond timestamp out of the first 48 bits of a version 7 Guid.</summary>
    private static DateTimeOffset TimestampOf(Guid value)
    {
        var bytes = value.ToByteArray(bigEndian: true);
        var milliseconds = ((long)bytes[0] << 40)
            | ((long)bytes[1] << 32)
            | ((long)bytes[2] << 24)
            | ((long)bytes[3] << 16)
            | ((long)bytes[4] << 8)
            | bytes[5];

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }
}
