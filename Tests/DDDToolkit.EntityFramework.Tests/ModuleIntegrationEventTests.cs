using DDDToolkit.EntityFramework.Inbox;
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
/// The sink that matters in a modular monolith: the outbox hands a message to the other modules in this
/// process, typed on the contract, one inbox row per consumer.
/// </summary>
public sealed class ModuleIntegrationEventTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private static Shelf NewShelf(string name = "Fiction") => new(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null);

    private TestHost CreateHost(Action<IServiceCollection>? handlers = null, Action<OutboxOptions>? outbox = null)
        => new(
            _db,
            options =>
            {
                options.TimeProvider = _clock;
                options.MapIntegrationEvents(contracts => contracts.Register<ShelfOpenedV3>());
                options.UseOutbox(o =>
                {
                    o.RegisterEvent<ShelfCreated>().RegisterEvent<BookAdded>();
                    o.PublishAs<ShelfCreated, ShelfOpenedV3>(e => new ShelfOpenedV3(e.ShelfId.ToString(), e.Name));
                    o.DoNotPublish<BookAdded>();
                    o.SendToModules<LibraryContext>();
                    outbox?.Invoke(o);
                });
            },
            dispatchThroughRecorder: false,
            services =>
            {
                services.AddOutboxProcessor<LibraryContext>();
                services.AddModuleIntegrationEvents<LibraryContext>();
                handlers?.Invoke(services);
            });

    private Task<int> ProcessAsync(TestHost host)
        => host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

    private async Task<Shelf> SaveShelfAsync(TestHost host, string name = "Fiction")
    {
        var shelf = NewShelf(name);
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        return shelf;
    }

    private List<InboxMessage> Rows()
    {
        using var check = _db.CreateLibraryContext();
        return [.. check.Inbox];
    }

    private OutboxMessage Row()
    {
        using var check = _db.CreateLibraryContext();
        return check.Outbox.Single(m => m.EventName == "shelf.created");
    }

    [Fact]
    public async Task A_module_handler_is_typed_on_the_contract_and_never_sees_the_domain_event()
    {
        var board = new ShelfBoard();
        using var host = CreateHost(services => services.AddIntegrationEventHandler(board));
        var shelf = await SaveShelfAsync(host, "Fiction");

        var processed = await ProcessAsync(host);

        processed.Should().Be(1);
        var seen = board.Seen.Should().ContainSingle().Subject;
        seen.ShelfId.Should().Be(shelf.Id.ToString(), "the contract carries the id as text, not as the producing module's ShelfId type");
        seen.DisplayName.Should().Be("Fiction");

        var envelope = board.Envelopes.Should().ContainSingle().Subject;
        envelope.Name.Should().Be("library.shelf-opened");
        envelope.MessageId.Should().Be(Row().Id, "the identity runs from the aggregate to the consumer unchanged");
    }

    [Fact]
    public async Task The_handler_and_its_inbox_row_are_written_together_and_a_repeat_does_not_run_it_again()
    {
        var board = new ShelfBoard();
        using var host = CreateHost(services => services.AddIntegrationEventHandler(board));
        await SaveShelfAsync(host);

        await ProcessAsync(host);

        Rows().Should().ContainSingle().Which.Consumer.Should().Be("library.shelf-board", "the attribute name, not the class name");

        // Reset the row the way a retry after an unrelated failure would, and deliver again.
        await RetryAsync(host);

        board.Runs.Should().Be(1, "the inbox row is what stops the second run");
        board.Seen.Should().ContainSingle();
        Rows().Should().ContainSingle();
    }

    [Fact]
    public async Task A_failing_consumer_leaves_the_message_retryable_without_re_running_the_ones_that_succeeded()
    {
        var board = new ShelfBoard();
        var index = new ShelfIndex { Refuse = true };
        using var host = CreateHost(services => services
            .AddIntegrationEventHandler(board)
            .AddIntegrationEventHandler<ShelfOpenedV3>(index));
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(0, "the message counts as delivered only when every consumer applied it");
        board.Seen.Should().ContainSingle("a broken consumer must not starve the one behind it");
        Rows().Should().ContainSingle().Which.Consumer.Should().Be("library.shelf-board");

        var failed = Row();
        failed.ProcessedAt.Should().BeNull();
        failed.LastError.Should().Contain("search.shelf-index");

        index.Refuse = false;
        var retried = await RetryAsync(host);

        retried.Should().Be(1);
        board.Runs.Should().Be(1, "the consumer that had already applied the message is skipped, which is the whole reason for the inbox");
        index.Seen.Should().ContainSingle();
        Rows().Select(r => r.Consumer).Should().BeEquivalentTo("library.shelf-board", "search.shelf-index");
        Row().ProcessedAt.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task A_consumer_without_a_name_falls_back_to_its_type_name()
    {
        var unnamed = new UnnamedShelfConsumer();
        using var host = CreateHost(services => services.AddIntegrationEventHandler<ShelfOpenedV3>(unnamed));
        await SaveShelfAsync(host);

        await ProcessAsync(host);

        Rows().Should().ContainSingle().Which.Consumer.Should().Be(typeof(UnnamedShelfConsumer).FullName);
    }

    [Fact]
    public async Task A_contract_no_module_handles_is_delivered_rather_than_stuck()
    {
        using var host = CreateHost();
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(1);
        Rows().Should().BeEmpty("nothing applied it, so nothing has to remember applying it");

        using var check = _db.CreateLibraryContext();
        check.Outbox.Should().OnlyContain(m => m.ProcessedAt != null);
    }

    [Fact]
    public async Task A_handler_writing_through_the_context_commits_with_its_inbox_row()
    {
        using var host = CreateHost(services => services.AddIntegrationEventHandler<ShelfOpenedV3, ProjectingHandler>());
        await SaveShelfAsync(host);

        await ProcessAsync(host);

        using var check = _db.CreateLibraryContext();
        check.People.Should().ContainSingle();
        Rows().Should().ContainSingle().Which.Consumer.Should().Be("library.person-projector");
    }

    /// <summary>
    /// Resets the row the way an operator would after fixing whatever broke, and runs the processor
    /// again. This is what a real retry looks like from the consumers' point of view.
    /// </summary>
    private async Task<int> RetryAsync(TestHost host)
    {
        using (var reset = _db.CreateLibraryContext())
        {
            foreach (var row in reset.Outbox)
            {
                row.ProcessedAt = null;
                row.Attempts = 0;
            }

            await reset.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return await ProcessAsync(host);
    }

    /// <summary>A consumer that writes through the same context, which is how a projection is built.</summary>
    [IntegrationEventConsumer("library.person-projector")]
    private sealed class ProjectingHandler(LibraryContext context) : IIntegrationEventHandler<ShelfOpenedV3>
    {
        public Task HandleAsync(ShelfOpenedV3 contract, BaseTypes.IntegrationEventMessage message, CancellationToken cancellationToken = default)
        {
            context.People.Add(new Person(MemberId.CreateUnique(), new PersonName("Ada", "Lovelace"), null, new ValidDateOfBirth(new DateOnly(1815, 12, 10))));
            return Task.CompletedTask;
        }
    }
}
