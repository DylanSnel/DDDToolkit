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

    private TestHost CreateHost(Action<ModuleIntegrationEvents<LibraryContext>>? handlers = null, Action<OutboxOptions>? outbox = null, Action<IServiceCollection>? services = null)
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
                    o.SendToModules();
                    outbox?.Invoke(o);
                });
            },
            dispatchThroughRecorder: false,
            collection =>
            {
                collection.AddOutboxProcessor<LibraryContext>();
                collection.AddModuleIntegrationEvents<LibraryContext>(module => handlers?.Invoke(module));
                services?.Invoke(collection);
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

    private List<Person> People()
    {
        using var check = _db.CreateLibraryContext();
        return [.. check.People];
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
        using var host = CreateHost(module => module.Handle(board));
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
        using var host = CreateHost(module => module.Handle(board));
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
        using var host = CreateHost(module => module
            .Handle(board)
            .Handle<ShelfOpenedV3>(index));
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
    public async Task A_consumer_that_fails_after_writing_leaves_nothing_behind_for_the_consumers_after_it()
    {
        var projector = new ProjectorSwitch { Refuse = true };
        var board = new ShelfBoard();
        using var host = CreateHost(
            module => module
                .Handle<ShelfOpenedV3, RefusingProjector>()
                .Handle(board),
            services: collection => collection.AddSingleton(projector));
        await SaveShelfAsync(host);

        await ProcessAsync(host);

        board.Seen.Should().ContainSingle();
        People().Should().BeEmpty("the failed consumer's write was rolled back, and the next consumer's save must not carry it in");
        Rows().Select(r => r.Consumer).Should().Equal("library.shelf-board");

        projector.Refuse = false;
        await RetryAsync(host);

        People().Should().ContainSingle("applied once, on the retry, together with its inbox row");
        Rows().Select(r => r.Consumer).Should().BeEquivalentTo("library.shelf-board", "library.refusing-projector");
    }

    [Fact]
    public async Task A_consumer_without_a_name_falls_back_to_its_type_name()
    {
        var unnamed = new UnnamedShelfConsumer();
        using var host = CreateHost(module => module.Handle<ShelfOpenedV3>(unnamed));
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
        using var host = CreateHost(module => module.Handle<ShelfOpenedV3, ProjectingHandler>());
        await SaveShelfAsync(host);

        await ProcessAsync(host);

        using var check = _db.CreateLibraryContext();
        check.People.Should().ContainSingle();
        Rows().Should().ContainSingle().Which.Consumer.Should().Be("library.person-projector");
    }

    [Fact]
    public async Task Two_consuming_modules_each_run_their_own_handlers_under_their_own_inbox()
    {
        var board = new ShelfBoard();
        var index = new ShelfIndex { Refuse = true };
        using var host = CreateHost(
            module => module.Handle(board),
            services: collection =>
            {
                // A second module: its own context, its own inbox table, its own handler. The producing
                // side above still only says SendToModules() and knows about neither.
                collection.AddScoped(_ => new RenamedStorageContext(_db.Options<RenamedStorageContext>()));
                collection.AddModuleIntegrationEvents<RenamedStorageContext>(module => module.Handle<ShelfOpenedV3>(index));
            });
        CreateSecondModuleTables();
        await SaveShelfAsync(host);

        var processed = await ProcessAsync(host);

        processed.Should().Be(0);
        Row().LastError.Should().Contain("RenamedStorageContext/search.shelf-index", "the failure names the module as well as the consumer");
        Rows().Select(r => r.Consumer).Should().Equal(["library.shelf-board"], "each handler writes to its own module's inbox and nowhere else");
        SecondModuleRows().Should().BeEmpty();

        index.Refuse = false;
        await RetryAsync(host);

        board.Runs.Should().Be(1, "the first module applied it already, whatever the second module did");
        index.Seen.Should().ContainSingle();
        Rows().Select(r => r.Consumer).Should().Equal("library.shelf-board");
        SecondModuleRows().Select(r => r.Consumer).Should().Equal("search.shelf-index");
    }

    [Fact]
    public void Two_handlers_under_one_consumer_name_in_one_module_are_refused_because_the_inbox_could_not_tell_them_apart()
    {
        var act = () => new ServiceCollection().AddModuleIntegrationEvents<LibraryContext>(module => module
            .Handle(new ShelfBoard())
            .Handle(new ShelfBoard()));

        act.Should().Throw<ArgumentException>().WithMessage("*LibraryContext*'library.shelf-board'*");
    }

    [Fact]
    public void Registering_a_module_twice_adds_to_it_rather_than_offering_every_message_to_it_twice()
    {
        var services = new ServiceCollection()
            .AddModuleIntegrationEvents<LibraryContext>(module => module.Handle(new ShelfBoard()))
            .AddModuleIntegrationEvents<LibraryContext>(module => module.Handle<ShelfOpenedV3>(new ShelfIndex()));

        services.Count(d => d.ServiceType.Name == "IModuleIntegrationEventConsumer").Should().Be(1);
    }

    /// <summary>The second module's tables, added next to the library's in the same SQLite database.</summary>
    private void CreateSecondModuleTables()
    {
        using var context = new RenamedStorageContext(_db.Options<RenamedStorageContext>());
        Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>(context.Database)
            .CreateTables();
    }

    private List<InboxMessage> SecondModuleRows()
    {
        using var check = new RenamedStorageContext(_db.Options<RenamedStorageContext>());
        return [.. check.Inbox];
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
                row.NextAttemptAt = null;
            }

            await reset.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return await ProcessAsync(host);
    }

    /// <summary>Whether <see cref="RefusingProjector"/> fails, shared with the test through the container.</summary>
    private sealed class ProjectorSwitch
    {
        public bool Refuse { get; set; }
    }

    /// <summary>A consumer that writes through the context and then fails, before its write was saved.</summary>
    [IntegrationEventConsumer("library.refusing-projector")]
    private sealed class RefusingProjector(LibraryContext context, ProjectorSwitch projector) : IIntegrationEventHandler<ShelfOpenedV3>
    {
        public Task HandleAsync(ShelfOpenedV3 contract, BaseTypes.IntegrationEventMessage message, CancellationToken cancellationToken = default)
        {
            context.People.Add(new Person(MemberId.CreateUnique(), new PersonName("Ada", "Lovelace"), null, new ValidDateOfBirth(new DateOnly(1815, 12, 10))));
            return projector.Refuse ? Task.FromException(new InvalidOperationException("the projector is down")) : Task.CompletedTask;
        }
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
