using DDDToolkit.EntityFramework.Integration;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.EntityFramework.Tests.IntegrationEvents;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// <see cref="IOutboundIntegrationEvent{TDomainEvent, TContract}"/>: the translation as a class, built
/// at delivery from the processor's scope.
/// </summary>
public sealed class OutboundIntegrationEventTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private TestHost CreateHost(Action<OutboxOptions> outbox, OutboundSwitch? outboundSwitch = null)
        => new(
            _db,
            options => options.UseOutbox(o =>
            {
                o.RegisterEvent<ShelfCreated>().RegisterEvent<ShelfRenamed>().RegisterEvent<BookAdded>();
                outbox(o);
            }),
            dispatchThroughRecorder: false,
            collection =>
            {
                collection.AddOutboxProcessor<LibraryContext>();
                collection.AddSingleton(outboundSwitch ?? new OutboundSwitch());
            });

    private static async Task<Shelf> SaveShelfAsync(TestHost host, string name = "Fiction", Action<Shelf>? change = null)
    {
        var shelf = new Shelf(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null);
        change?.Invoke(shelf);

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        return shelf;
    }

    private static Task<int> ProcessAsync(TestHost host)
        => host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

    private static OutboxOptions PublishOpened(OutboxOptions outbox)
        => outbox.PublishWith<ShelfCreated, ShelfOpenedV3>(
            "library.shelf-opened",
            3,
            static services => new PublishShelfOpened(services.GetRequiredService<LibraryContext>(), services.GetRequiredService<OutboundSwitch>()));

    private static OutboxOptions PublishChanges(OutboxOptions outbox)
        => outbox
            .PublishWith<ShelfRenamed, ShelfRenamedV1>("ShelfRenamedV1", 1, static _ => new PublishShelfChanges())
            .PublishWith<BookAdded, BookShelved>("BookShelved", 1, static _ => new PublishShelfChanges());

    [Fact]
    public async Task An_outbound_class_is_built_from_the_processors_scope_and_can_read_the_module()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox => PublishOpened(outbox).SendTo(sink));
        await SaveShelfAsync(host, "Fiction");
        var shelf = await SaveShelfAsync(host, "Poetry");

        await ProcessAsync(host);

        var published = sink.Messages.Where(m => m.Body is ShelfOpenedV3 opened && opened.ShelfId == shelf.Id.ToString()).Should().ContainSingle().Subject;
        published.Name.Should().Be("library.shelf-opened", "the contract still supplies the published name");
        published.Version.Should().Be(3);
        ((ShelfOpenedV3)published.Body!).DisplayName.Should().Be("Poetry (2 shelves)", "the class read the module's own context, which a lambda could not");
    }

    [Fact]
    public async Task Returning_null_drops_that_one_occurrence()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox => PublishOpened(outbox).SendTo(sink));
        await SaveShelfAsync(host, "Private");
        await SaveShelfAsync(host, "Public");

        var processed = await ProcessAsync(host);

        processed.Should().Be(2, "a dropped occurrence still counts as delivered");
        sink.Messages.Should().ContainSingle().Which.Payload.Should().Contain("Public");
    }

    [Fact]
    public async Task A_throwing_outbound_class_fails_the_delivery_and_the_retry_runs_it_again()
    {
        var sink = new RecordingSink();
        var outboundSwitch = new OutboundSwitch { Fail = true };
        using var host = CreateHost(outbox => PublishOpened(outbox).SendTo(sink), outboundSwitch);
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(0);
        sink.Messages.Should().BeEmpty("nothing reaches a sink when the contract could not be made");
        using (var check = _db.CreateLibraryContext())
        {
            var row = await check.Outbox.SingleAsync(TestContext.Current.CancellationToken);
            row.ProcessedAt.Should().BeNull();
            row.Attempts.Should().Be(1);
            row.LastError.Should().Contain("lookup is down");
        }

        outboundSwitch.Fail = false;
        (await ProcessAsync(host)).Should().Be(1);
        sink.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task One_class_can_publish_several_events()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox => PublishChanges(outbox).SendTo(sink));
        await SaveShelfAsync(host, change: shelf =>
        {
            shelf.Rename("Poetry");
            shelf.AddBook("Dune");
        });

        await ProcessAsync(host);

        sink.Messages.Select(m => m.Body).Should().Contain(body => body is ShelfRenamedV1).And.Contain(body => body is BookShelved);
        sink.Messages.Should().Contain(m => m.Name == "shelf.created", "an event the class does not publish is published as it stands");
    }

    [Fact]
    public async Task The_generated_registration_publishes_through_every_outbound_class_in_the_assembly()
    {
        var sink = new RecordingSink();
        using var host = CreateHost(outbox => outbox.AddEfTestsIntegrationEvents().SendTo(sink));
        await SaveShelfAsync(host, "Fiction", shelf => shelf.AddBook("Dune"));

        await ProcessAsync(host);

        sink.Messages.Select(m => m.Body).Should().Contain(body => body is ShelfOpenedV3).And.Contain(body => body is BookShelved);
        sink.Messages.Should().Contain(m => m.Name == "library.shelf-opened" && m.Version == 3, "the name and version were written into the registration when this assembly compiled");
        host.Options.OutboxFor(typeof(LibraryContext))!.IntegrationEvents.MappedEventTypes
            .Should().Contain([typeof(ShelfCreated), typeof(ShelfRenamed), typeof(BookAdded)]);
    }

    [Fact]
    public void A_class_and_a_lambda_cannot_both_publish_the_same_event()
    {
        var map = new IntegrationEventMap().PublishAs<ShelfCreated, ShelfOpenedV3>(e => new ShelfOpenedV3(e.ShelfId.ToString(), e.Name));

        var twice = () => map.PublishWith<ShelfCreated, ShelfOpenedV3>("library.shelf-opened", 3, static services => new PublishShelfOpened(services.GetRequiredService<LibraryContext>(), services.GetRequiredService<OutboundSwitch>()));

        twice.Should().Throw<ArgumentException>().WithMessage("*ShelfCreated*already mapped*");
    }

    [Fact]
    public async Task The_map_converts_unmapped_mapped_and_class_based_entries()
    {
        using var provider = new ServiceCollection().AddSingleton(new OutboundSwitch()).BuildServiceProvider();
        var created = new ShelfCreated(ShelfId.CreateUnique(), "Fiction");
        var renamed = new ShelfRenamed(created.ShelfId, "Poetry");
        var map = new IntegrationEventMap().PublishWith<ShelfRenamed, ShelfRenamedV1>("ShelfRenamedV1", 1, static _ => new PublishShelfChanges()).PublishWith<BookAdded, BookShelved>("BookShelved", 1, static _ => new PublishShelfChanges());

        map.IsMapped(typeof(ShelfCreated)).Should().BeFalse();
        (await map.ConvertAsync(created, provider, TestContext.Current.CancellationToken)).Should().BeSameAs(created, "no entry means the domain event itself is published");

        map.IsMapped(typeof(ShelfRenamed)).Should().BeTrue();
        (await map.ConvertAsync(renamed, provider, TestContext.Current.CancellationToken)).Should().Be(new ShelfRenamedV1(created.ShelfId.ToString(), "Poetry"));
    }

#pragma warning disable CS0618 // TryConvert is obsolete; these pin down what it still does.
    [Fact]
    public void The_obsolete_TryConvert_still_runs_a_lambda_and_refuses_a_class()
    {
        var map = new IntegrationEventMap()
            .PublishAs<ShelfCreated, ShelfOpenedV3>(e => new ShelfOpenedV3(e.ShelfId.ToString(), e.Name))
            .PublishWith<ShelfRenamed, ShelfRenamedV1>("ShelfRenamedV1", 1, static _ => new PublishShelfChanges()).PublishWith<BookAdded, BookShelved>("BookShelved", 1, static _ => new PublishShelfChanges());
        var shelf = ShelfId.CreateUnique();

        map.TryConvert(new ShelfCreated(shelf, "Fiction"), out var contract).Should().BeTrue();
        contract.Should().BeOfType<ShelfOpenedV3>();

        var withServices = () => map.TryConvert(new ShelfRenamed(shelf, "Poetry"), out _);
        withServices.Should().Throw<InvalidOperationException>().WithMessage("*ConvertAsync*");
    }
#pragma warning restore CS0618
}

public sealed record ShelfRenamedV1(string ShelfId, string Name);

/// <summary>Lets a test make <see cref="PublishShelfOpened"/> fail.</summary>
public sealed class OutboundSwitch
{
    public bool Fail { get; set; }
}

/// <summary>Enriches from the module's own context, drops private shelves, and fails on request.</summary>
public sealed class PublishShelfOpened(LibraryContext context, OutboundSwitch outboundSwitch) : IOutboundIntegrationEvent<ShelfCreated, ShelfOpenedV3>
{
    public async ValueTask<ShelfOpenedV3?> CreateAsync(ShelfCreated created, CancellationToken cancellationToken)
    {
        if (outboundSwitch.Fail)
        {
            throw new InvalidOperationException("The lookup is down.");
        }

        if (created.Name == "Private")
        {
            return null;
        }

        var shelves = await context.Shelves.CountAsync(cancellationToken);
        return new ShelfOpenedV3(created.ShelfId.ToString(), $"{created.Name} ({shelves} shelves)");
    }
}

/// <summary>Two events from one class, no services.</summary>
public sealed class PublishShelfChanges : IOutboundIntegrationEvent<ShelfRenamed, ShelfRenamedV1>, IOutboundIntegrationEvent<BookAdded, BookShelved>
{
    public ValueTask<ShelfRenamedV1?> CreateAsync(ShelfRenamed renamed, CancellationToken cancellationToken)
        => new(new ShelfRenamedV1(renamed.ShelfId.ToString(), renamed.Name));

    public ValueTask<BookShelved?> CreateAsync(BookAdded added, CancellationToken cancellationToken)
        => new(new BookShelved(added.BookId.ToString()));
}
