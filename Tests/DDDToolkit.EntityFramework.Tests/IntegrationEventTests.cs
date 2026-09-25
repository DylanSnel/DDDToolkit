using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The outbox's exit: turning stored domain events into messages that leave the process, and handing
/// them to one or more sinks.
/// </summary>
public sealed class IntegrationEventTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private TestHost CreateHost(Action<OutboxOptions>? outbox = null, bool dispatchThroughRecorder = true, Action<IServiceCollection>? services = null)
        => new(
            _db,
            options =>
            {
                options.TimeProvider = _clock;
                options.UseOutbox(o =>
                {
                    o.RegisterEvent<ShelfCreated>().RegisterEvent<ShelfRenamed>().RegisterEvent<BookAdded>();
                    outbox?.Invoke(o);
                });
            },
            dispatchThroughRecorder,
            collection =>
            {
                collection.AddOutboxProcessor<LibraryContext>();
                services?.Invoke(collection);
            });

    private static Shelf NewShelf(string name = "Fiction") => new(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null);

    private async Task<Shelf> SaveShelfAsync(TestHost host, string name = "Fiction", bool withBook = false)
    {
        var shelf = NewShelf(name);
        if (withBook)
        {
            shelf.AddBook("Dune");
        }

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        return shelf;
    }

    private Task<int> ProcessAsync(TestHost host)
        => host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

    private async Task<OutboxMessage> RowAsync(string eventName)
    {
        using var check = _db.CreateLibraryContext();
        return await check.Outbox.SingleAsync(m => m.EventName == eventName, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_sink_receives_exactly_what_the_outbox_stored()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox => outbox.SendTo(sink));
        var shelf = await SaveShelfAsync(host, "Fiction");
        var stored = await RowAsync("shelf.created");

        var processed = await ProcessAsync(host);

        processed.Should().Be(1);
        var published = sink.Messages.Should().ContainSingle().Subject;
        published.MessageId.Should().Be(stored.Id, "the event id is the idempotency key from end to end");
        published.Name.Should().Be("shelf.created", "with no mapping the stored [DomainEventName] is the published name");
        published.Version.Should().Be(1);
        published.Payload.Should().Be(stored.Payload, "publishing directly reuses the stored JSON rather than serializing again");
        published.ContentType.Should().Be("application/json");
        published.OccurredAt.Should().Be(stored.OccurredAt);
        published.AggregateType.Should().Be(nameof(Shelf));
        published.AggregateId.Should().Be(shelf.Id.ToString());
        published.Body.Should().BeOfType<ShelfCreated>().Which.Name.Should().Be("Fiction");

        host.Recorder.Events.Should().BeEmpty("a configured sink replaces the in-process delegate");
        (await RowAsync("shelf.created")).ProcessedAt.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task Two_sinks_both_receive_the_message()
    {
        var first = new RecordingSink();
        var second = new SecondRecordingSink();
        using var host = CreateHost(outbox => outbox.SendTo(first).SendTo(second));
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(1);
        first.Messages.Should().ContainSingle();
        second.Messages.Should().ContainSingle();
        second.Messages[0].Should().Be(first.Messages[0], "every sink gets the same message");
    }

    [Fact]
    public async Task A_failing_sink_does_not_lose_the_message_for_the_other_and_the_whole_message_is_retried()
    {
        var broken = new SecondRecordingSink { Refuse = true };
        var working = new RecordingSink();
        // Broken first, so the test also proves the loop does not stop at the first failure.
        using var host = CreateHost(outbox => outbox.SendTo(broken).SendTo(working));
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(0, "a message only counts as delivered when every sink accepted it");
        working.Messages.Should().ContainSingle("a broken transport must not starve the sinks behind it");
        broken.Attempts.Should().Be(1);

        var failed = await RowAsync("shelf.created");
        failed.ProcessedAt.Should().BeNull();
        failed.Attempts.Should().Be(1);
        failed.LastError.Should().Contain(nameof(IntegrationEventDeliveryException))
            .And.Contain(nameof(SecondRecordingSink))
            .And.Contain("is down");

        broken.Refuse = false;
        var retried = await ProcessAsync(host);

        retried.Should().Be(1);
        broken.Messages.Should().ContainSingle();
        working.Messages.Should().HaveCount(2, "the row is one message, so a retry redelivers to sinks that already accepted it");
        working.Messages[0].MessageId.Should().Be(working.Messages[1].MessageId, "which is exactly why consumers key idempotency on the message id");

        var row = await RowAsync("shelf.created");
        row.ProcessedAt.Should().Be(_clock.GetUtcNow());
        row.Attempts.Should().Be(2);
        row.LastError.Should().BeNull();
    }

    [Fact]
    public async Task A_mapped_event_is_published_as_its_contract_under_the_contract_name_and_version()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox => outbox
            .PublishAs<ShelfCreated, ShelfOpenedV3>(e => new ShelfOpenedV3(e.ShelfId.ToString(), e.Name.ToUpperInvariant()))
            .SendTo(sink));
        var shelf = await SaveShelfAsync(host, "Fiction");
        var stored = await RowAsync("shelf.created");

        await ProcessAsync(host);

        var published = sink.Messages.Should().ContainSingle().Subject;
        published.Name.Should().Be("library.shelf-opened", "the published name comes from the contract, not the domain event");
        published.Version.Should().Be(3);
        published.MessageId.Should().Be(stored.Id, "the identity stays with the occurrence, not with the shape");
        published.OccurredAt.Should().Be(stored.OccurredAt);
        published.AggregateId.Should().Be(shelf.Id.ToString());
        published.Payload.Should().Contain("\"DisplayName\":\"FICTION\"").And.NotContain("\"Name\"");
        published.Body.Should().BeOfType<ShelfOpenedV3>().Which.ShelfId.Should().Be(shelf.Id.ToString());

        (await RowAsync("shelf.created")).Payload.Should().Be(stored.Payload, "the stored row keeps the domain event; only delivery maps");
    }

    [Fact]
    public async Task A_contract_without_the_attribute_falls_back_to_the_conventional_name_and_version_one()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox => outbox
            .PublishAs<BookAdded, BookShelved>(e => new BookShelved(e.BookId.ToString()))
            .SendTo(sink));
        await SaveShelfAsync(host, withBook: true);

        await ProcessAsync(host);

        var published = sink.Messages.Single(m => m.Body is BookShelved);
        published.Name.Should().Be("book-shelved", "the class name in kebab case, with no module prefix because this assembly declares no module");
        published.Version.Should().Be(1);
    }

    [Fact]
    public async Task DoNotPublish_keeps_an_event_inside_the_process_without_blocking_the_row()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox => outbox.DoNotPublish<BookAdded>().SendTo(sink));
        await SaveShelfAsync(host, withBook: true);

        var processed = await ProcessAsync(host);

        processed.Should().Be(2, "an event that stays inside still counts as delivered");
        sink.Messages.Should().ContainSingle().Which.Name.Should().Be("shelf.created");

        using var check = _db.CreateLibraryContext();
        var rows = await check.Outbox.ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().HaveCount(2).And.OnlyContain(r => r.ProcessedAt != null, "an unpublished event is not a stuck message");
    }

    [Fact]
    public async Task A_conversion_returning_null_drops_that_one_occurrence()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox => outbox
            .PublishAs<ShelfCreated, ShelfOpenedV3>(e => e.Name == "Private" ? null : new ShelfOpenedV3(e.ShelfId.ToString(), e.Name))
            .SendTo(sink));
        await SaveShelfAsync(host, "Private");
        await SaveShelfAsync(host, "Public");

        var processed = await ProcessAsync(host);

        processed.Should().Be(2);
        sink.Messages.Should().ContainSingle().Which.Payload.Should().Contain("Public");
    }

    [Fact]
    public async Task AlsoDispatchInProcess_gives_the_delegate_the_domain_event_and_the_sink_the_contract()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox =>
        {
            outbox.AlsoDispatchInProcess = true;
            outbox.PublishAs<ShelfCreated, ShelfOpenedV3>(e => new ShelfOpenedV3(e.ShelfId.ToString(), e.Name)).SendTo(sink);
        });
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(1);
        host.Recorder.Events.Should().ContainSingle().Which.Should().BeOfType<ShelfCreated>("local handlers keep seeing the internal event");
        sink.Messages.Should().ContainSingle().Which.Body.Should().BeOfType<ShelfOpenedV3>("the outside world only ever sees the contract");
    }

    [Fact]
    public async Task A_failing_in_process_handler_stops_the_message_before_it_reaches_a_sink()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox =>
        {
            outbox.AlsoDispatchInProcess = true;
            outbox.SendTo(sink);
        });
        host.Recorder.OnEvent = (_, _, _) => throw new InvalidOperationException("projection failed");
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(0);
        sink.Messages.Should().BeEmpty("publishing a message whose local side effect failed would be a lie");
        (await RowAsync("shelf.created")).LastError.Should().Contain("projection failed");
    }

    [Fact]
    public async Task With_no_sink_the_processor_still_delivers_to_the_in_process_delegate()
    {
        using var host = CreateHost();
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(1);
        host.Recorder.Events.Should().ContainSingle().Which.Should().BeOfType<ShelfCreated>();
    }

    [Fact]
    public async Task A_sink_registered_by_type_is_built_with_its_dependencies()
    {
        var target = new RecordingSink();
        using var host = CreateHost(
            outbox => outbox.SendTo<InjectedSink>(),
            dispatchThroughRecorder: false,
            services => services.AddSingleton(target));
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(1, "a sink alone is a complete configuration; no dispatch delegate is needed");
        target.Messages.Should().ContainSingle();
    }

    [Fact]
    public void The_processor_refuses_a_configuration_with_nowhere_to_deliver()
    {
        using var context = _db.CreateLibraryContext();
        using var provider = new ServiceCollection().BuildServiceProvider();

        var nowhere = () => new OutboxProcessor<LibraryContext>(context, provider, new DDDEntityFrameworkOptions().UseOutbox());

        nowhere.Should().Throw<InvalidOperationException>()
            .WithMessage("*DispatchInProcess*")
            .WithMessage("*SendTo*");
    }

    [Fact]
    public void The_map_reads_names_and_versions_with_two_fallbacks()
    {
        IntegrationEventContract.NameOf<ShelfOpenedV3>().Should().Be("library.shelf-opened");
        IntegrationEventContract.VersionOf<ShelfOpenedV3>().Should().Be(3);

        IntegrationEventContract.NameOf<ShelfCreated>().Should().Be("shelf.created", "a domain event published directly keeps its [DomainEventName]");
        IntegrationEventContract.VersionOf<ShelfCreated>().Should().Be(1);

        IntegrationEventContract.NameOf(new BookShelved("B")).Should().Be("book-shelved", "with no attribute at all the convention names it");
        IntegrationEventContract.VersionOf(new BookShelved("B")).Should().Be(1);
    }

    [Fact]
    public async Task The_map_publishes_unmapped_events_as_they_stand_and_rejects_a_second_entry()
    {
        var map = new IntegrationEventMap();
        var domainEvent = new ShelfCreated(ShelfId.CreateUnique(), "Fiction");
        using var services = new ServiceCollection().BuildServiceProvider();

        map.IsMapped(typeof(ShelfCreated)).Should().BeFalse();
        (await map.ConvertAsync(domainEvent, services, TestContext.Current.CancellationToken))
            .Should().BeSameAs(domainEvent, "no entry means the domain event itself is published");

        map.PublishAs<ShelfCreated, ShelfOpenedV3>(e => new ShelfOpenedV3(e.ShelfId.ToString(), e.Name));
        map.IsMapped(typeof(ShelfCreated)).Should().BeTrue();
        (await map.ConvertAsync(domainEvent, services, TestContext.Current.CancellationToken)).Should().BeOfType<ShelfOpenedV3>();
        map.MappedEventTypes.Should().BeEquivalentTo([typeof(ShelfCreated)]);

        var twice = () => map.DoNotPublish<ShelfCreated>();
        twice.Should().Throw<ArgumentException>().WithMessage("*already mapped*");
    }

    [Fact]
    public void An_integration_event_version_below_one_is_rejected()
    {
        var attribute = new Abstractions.Attributes.IntegrationEventAttribute("x");

        attribute.Version.Should().Be(1);
        var tooLow = () => attribute.Version = 0;
        tooLow.Should().Throw<ArgumentOutOfRangeException>();
    }
}
