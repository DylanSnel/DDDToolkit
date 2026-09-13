using System.Text.Json;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Inbox;
using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// A published payload is a schema, and a schema outlives the deployment that wrote it. These are the
/// two places that has to hold: the outbox reading a row written before the shape changed, and a
/// consumer reading a message written before the shape changed.
/// </summary>
public sealed class EventVersioningTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private static Shelf NewShelf(string name = "Fiction") => new(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null);

    [Fact]
    public void The_registry_keys_a_payload_on_its_name_and_its_version()
    {
        var contracts = new IntegrationEventContractRegistry()
            .Register<BookShelvedV1>()
            .Register<BookShelvedV2>();

        contracts.TryResolve("library.book-shelved", 1, out var first).Should().BeTrue();
        contracts.TryResolve("library.book-shelved", 2, out var second).Should().BeTrue();
        first.Should().Be<BookShelvedV1>();
        second.Should().Be<BookShelvedV2>();

        contracts.TryResolve("library.book-shelved", 9, out _).Should().BeFalse("a version nobody registered is not a version this side can read");
    }

    [Fact]
    public void A_version_1_payload_is_read_back_as_version_2()
    {
        var contracts = new IntegrationEventContractRegistry()
            .UpcastFrom<BookShelvedV1, BookShelvedV2>(v1 => new BookShelvedV2(v1.BookId, Shelf: "unknown"));

        var written = Message("library.book-shelved", 1, new BookShelvedV1("B-1"));

        var read = contracts.Read<BookShelvedV2>(written);

        read.BookId.Should().Be("B-1", "the field that existed survives verbatim");
        read.Shelf.Should().Be("unknown", "and the one that did not gets whatever the upcaster invents");
    }

    [Fact]
    public void Upcasters_are_followed_as_a_chain_so_a_version_1_payload_arrives_as_version_3()
    {
        var contracts = new IntegrationEventContractRegistry()
            .UpcastFrom<BookShelvedV1, BookShelvedV2>(v1 => new BookShelvedV2(v1.BookId, "unknown"))
            .UpcastFrom<BookShelvedV2, BookShelvedV3>(v2 => new BookShelvedV3(v2.BookId, v2.Shelf, "main"));

        var read = contracts.Read<BookShelvedV3>(Message("library.book-shelved", 1, new BookShelvedV1("B-1")));

        read.Should().Be(new BookShelvedV3("B-1", "unknown", "main"), "one step at a time, so adding v4 is one more line");
    }

    [Fact]
    public void A_payload_at_the_current_version_passes_through_untouched()
    {
        var contracts = new IntegrationEventContractRegistry()
            .UpcastFrom<BookShelvedV1, BookShelvedV2>(_ => throw new InvalidOperationException("the upcaster must not run"));

        var read = contracts.Read<BookShelvedV2>(Message("library.book-shelved", 2, new BookShelvedV2("B-1", "Fiction")));

        read.Should().Be(new BookShelvedV2("B-1", "Fiction"));
    }

    [Fact]
    public void An_unknown_version_says_so_and_names_what_to_register()
    {
        var contracts = new IntegrationEventContractRegistry().Register<BookShelvedV2>();

        var read = () => contracts.Read(Message("library.book-shelved", 1, new BookShelvedV1("B-1")));

        read.Should().Throw<InvalidOperationException>()
            .WithMessage("*library.book-shelved*version 1*")
            .WithMessage("*UpcastFrom*");
    }

    [Fact]
    public void A_chain_that_ends_somewhere_else_is_an_error_rather_than_a_cast_failure()
    {
        var contracts = new IntegrationEventContractRegistry()
            .UpcastFrom<BookShelvedV1, BookShelvedV2>(v1 => new BookShelvedV2(v1.BookId, "unknown"));

        var read = () => contracts.Read<BookShelvedV3>(Message("library.book-shelved", 1, new BookShelvedV1("B-1")));

        read.Should().Throw<InvalidOperationException>().WithMessage("*BookShelvedV2*BookShelvedV3*");
    }

    [Fact]
    public void Scanning_an_assembly_finds_every_contract_and_refuses_to_pick_between_two_of_them()
    {
        // This assembly deliberately declares a second contract under 'library.book-shelved' version 1,
        // so one scan proves both halves: the attribute is what it looks for, and a name and version
        // claimed twice is a configuration error rather than a coin toss about which payload wins.
        var scan = () => new IntegrationEventContractRegistry().RegisterFromAssemblyContaining<BookShelvedV1>();

        scan.Should().Throw<ArgumentException>().WithMessage("*library.book-shelved*version 1*");
    }

    [Fact]
    public void Two_contracts_claiming_one_name_and_version_are_rejected()
    {
        var contracts = new IntegrationEventContractRegistry();

        var clash = () => contracts.Register<BookShelvedV1>().Register<ClashingContract>();

        clash.Should().Throw<ArgumentException>().WithMessage("*library.book-shelved*version 1*");
    }

    [Fact]
    public void The_outbox_row_records_the_shape_it_was_written_in()
    {
        using var host = CreateHost();
        var shelf = NewShelf();
        shelf.Catalogue("QA-1", "dewey");

        host.InScope(context =>
        {
            context.Shelves.Add(shelf);
            context.SaveChanges();
        });

        using var check = _db.CreateLibraryContext();
        var rows = check.Outbox.ToList();
        rows.Single(r => r.EventName == "shelf.catalogued").Version.Should().Be(2, "from [IntegrationEvent(Version = 2)] on the event");
        rows.Single(r => r.EventName == "shelf.created").Version.Should().Be(1, "an event that never changed shape is version 1 and says nothing");
    }

    [Fact]
    public async Task The_processor_upcasts_a_row_written_before_the_shape_changed()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(options =>
        {
            options.MapIntegrationEvents(contracts => contracts
                .UpcastFrom<ShelfCataloguedV1, ShelfCataloguedV2>(v1 => new ShelfCataloguedV2(v1.ShelfId, v1.Code, System: "unknown")));

            options.UseOutbox(outbox => outbox.RegisterEvent<ShelfCataloguedV2>().SendTo(sink));
        });

        // A row exactly as the previous deployment wrote it: same name, the old shape, version 1.
        var shelfId = ShelfId.CreateUnique();
        WriteRow(new ShelfCataloguedV1(shelfId, "QA-1"), version: 1);

        var processed = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

        processed.Should().Be(1);
        var published = sink.Messages.Should().ContainSingle().Subject;
        published.Version.Should().Be(2, "the envelope carries the shape that was actually published, not the one that was stored");
        published.Body.Should().BeOfType<ShelfCataloguedV2>().Which.Should().BeEquivalentTo(new { Code = "QA-1", System = "unknown" });
    }

    [Fact]
    public async Task A_row_from_a_shape_nobody_kept_fails_the_message_and_says_what_is_missing()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(options => options.UseOutbox(outbox => outbox.RegisterEvent<ShelfCataloguedV2>().SendTo(sink)));

        WriteRow(new ShelfCataloguedV1(ShelfId.CreateUnique(), "QA-1"), version: 1);

        var processed = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

        processed.Should().Be(0, "guessing at a shape would be worse than leaving the row for a human");
        sink.Messages.Should().BeEmpty();

        using var check = _db.CreateLibraryContext();
        check.Outbox.Single().LastError.Should().Contain("version 1").And.Contain("UpcastFrom");
    }

    [Fact]
    public async Task The_inbox_reads_a_message_as_the_shape_the_handler_was_written_against()
    {
        using var host = CreateHost(options => options.MapIntegrationEvents(contracts => contracts
            .UpcastFrom<BookShelvedV1, BookShelvedV2>(v1 => new BookShelvedV2(v1.BookId, Shelf: "unknown"))));

        // What a message written by last year's producer looks like arriving today.
        var message = Message("library.book-shelved", 1, new BookShelvedV1("B-1"));
        BookShelvedV2? seen = null;

        var applied = await host.InScopeAsync((_, services) => services
            .GetRequiredService<DomainEventInbox<LibraryContext>>()
            .ExecuteOnceAsync<BookShelvedV2>(message, "library.book-projector", (contract, _, _) =>
            {
                seen = contract;
                return Task.CompletedTask;
            }));

        applied.Should().BeTrue();
        seen.Should().Be(new BookShelvedV2("B-1", "unknown"));
    }

    [Fact]
    public void The_event_registry_keeps_the_newest_shape_under_a_shared_name()
    {
        var registry = new DomainEventTypeRegistry()
            .Register<ShelfCataloguedV1>()
            .Register<ShelfCataloguedV2>();

        registry.Resolve("shelf.catalogued").Should().Be<ShelfCataloguedV2>("new events are written as the newest shape");

        // And the order of registration does not decide it, which matters for an assembly scan.
        new DomainEventTypeRegistry()
            .Register<ShelfCataloguedV2>()
            .Register<ShelfCataloguedV1>()
            .Resolve("shelf.catalogued")
            .Should().Be<ShelfCataloguedV2>();
    }

    [Fact]
    public void An_event_stored_under_one_name_and_published_under_another_is_refused()
    {
        var registry = new DomainEventTypeRegistry();

        var mismatch = () => registry.Register<MisnamedEvent>();

        mismatch.Should().Throw<ArgumentException>().WithMessage("*shelf.misnamed*library.something-else*");
    }

    private TestHost CreateHost(Action<DDDEntityFrameworkOptions>? configure = null)
        => new(
            _db,
            options =>
            {
                options.TimeProvider = _clock;
                configure?.Invoke(options);
                if (!options.OutboxEnabled)
                {
                    options.UseOutbox(outbox => outbox.RegisterEvent<ShelfCreated>().RegisterEvent<ShelfCataloguedV2>());
                }
            },
            dispatchThroughRecorder: false,
            services =>
            {
                services.AddOutboxProcessor<LibraryContext>();
                services.AddDomainEventInbox<LibraryContext>();
            });

    /// <summary>Writes a row by hand, which is the only way to have a payload an older build produced.</summary>
    private void WriteRow(IDomainEvent domainEvent, int version)
    {
        using var context = _db.CreateLibraryContext();
        context.Outbox.Add(new OutboxMessage
        {
            Id = domainEvent.EventId,
            EventName = DomainEventName.Of(domainEvent),
            Version = version,
            Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), new OutboxOptions().JsonOptions),
            OccurredAt = domainEvent.OccurredAt,
            CreatedAt = _clock.GetUtcNow(),
        });
        context.SaveChanges();
    }

    private IntegrationEventMessage Message(string name, int version, object body)
        => new()
        {
            MessageId = Guid.CreateVersion7(),
            Name = name,
            Version = version,
            Payload = JsonSerializer.Serialize(body, body.GetType()),
            OccurredAt = _clock.GetUtcNow(),
        };

    /// <summary>Claims the same published name and version as <see cref="BookShelvedV1"/>.</summary>
    [Abstractions.Attributes.IntegrationEvent("library.book-shelved", Version = 1)]
    private sealed record ClashingContract(string Other);

    /// <summary>Stored under one name and published under another, which no version could reconcile.</summary>
    [Abstractions.Attributes.DomainEventName("shelf.misnamed")]
    [Abstractions.Attributes.IntegrationEvent("library.something-else")]
    private sealed record MisnamedEvent : DomainEvent;
}
