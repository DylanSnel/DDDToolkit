using System.Text.Json;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Domain.Events;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.ExampleApi.Domain.UserAggregate.Events;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using DDDToolkit.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;


namespace DDDToolkit.EntityFramework.Tests;

/// <summary>Transactional delivery through the outbox table and its processor.</summary>
public sealed class OutboxTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private TestHost CreateHost(Action<Options.OutboxOptions>? outbox = null, bool dispatchThroughRecorder = true)
        => new(
            _db,
            options =>
            {
                options.TimeProvider = _clock;
                options.UseOutbox(o =>
                {
                    // Explicit registration: this assembly also declares a deliberately clashing fixture type.
                    o.RegisterEvent<ShelfCreated>().RegisterEvent<ShelfRenamed>().RegisterEvent<BookAdded>();
                    outbox?.Invoke(o);
                });
            },
            dispatchThroughRecorder,
            services => services.AddOutboxProcessor<LibraryContext>());

    private static Shelf NewShelf(string name = "Fiction") => new(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null);

    [Fact]
    public async Task Save_writes_one_row_per_event_and_dispatches_nothing()
    {
        using var host = CreateHost();
        var shelf = NewShelf("Fiction");
        var book = shelf.AddBook("Dune");
        var events = ((IHasDomainEvents)shelf).DomainEvents.ToList();

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        host.Recorder.Events.Should().BeEmpty("outbox mode does not dispatch at save time");
        ((IHasDomainEvents)shelf).DomainEvents.Should().BeEmpty();

        using var check = _db.CreateLibraryContext();
        var rows = await check.Outbox.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().HaveCount(2);
        rows.Select(r => r.Id).Should().BeEquivalentTo(events.Select(e => e.EventId));

        var created = rows.Single(r => r.EventName == "shelf.created");
        created.AggregateType.Should().Be(nameof(Shelf));
        created.AggregateId.Should().Be(shelf.Id.ToString()).And.StartWith("SHELF_");
        created.OccurredAt.Should().Be(events[0].OccurredAt);
        created.CreatedAt.Should().Be(_clock.GetUtcNow());
        created.ProcessedAt.Should().BeNull();
        created.Attempts.Should().Be(0);
        created.NextAttemptAt.Should().BeNull("a new row is due at once");
        created.LastError.Should().BeNull();
        created.Payload.Should().Contain("\"Name\":\"Fiction\"");

        rows.Single(r => r.EventName == nameof(BookAdded)).Payload.Should().Contain(book.Id.Value.ToString());
    }

    [Fact]
    public async Task Rows_are_written_in_the_same_transaction_as_the_aggregate()
    {
        using var host = CreateHost();

        await host.InScopeAsync(async context =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Shelves.Add(NewShelf());
            await context.SaveChangesAsync();

            _db.CountRows("OutboxMessages").Should().Be(1, "inside the transaction the row is visible on the same connection");
            await transaction.RollbackAsync();
        });

        _db.CountRows("Shelves").Should().Be(0);
        _db.CountRows("OutboxMessages").Should().Be(0, "rolling back the aggregate rolls back its events");
    }

    [Fact]
    public async Task Processor_dispatches_pending_messages_oldest_first_and_marks_them_processed()
    {
        using var host = CreateHost();
        var first = NewShelf("First");
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(first);
            await context.SaveChangesAsync();
        });

        _clock.Advance(TimeSpan.FromMinutes(1));
        var second = NewShelf("Second");
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(second);
            await context.SaveChangesAsync();
        });

        _clock.Advance(TimeSpan.FromMinutes(1));
        var processed = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

        processed.Should().Be(2);
        host.Recorder.OfType<ShelfCreated>().Select(e => e.Name).Should().Equal("First", "Second");

        using var check = _db.CreateLibraryContext();
        var rows = await check.Outbox.ToListAsync(TestContext.Current.CancellationToken);
        rows.Should().OnlyContain(r => r.ProcessedAt == _clock.GetUtcNow() && r.Attempts == 1 && r.LastError == null);

        var again = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());
        again.Should().Be(0, "processed messages are not delivered twice");
        host.Recorder.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task Positional_record_events_round_trip_with_their_EventId_and_OccurredAt()
    {
        using var host = CreateHost();
        var shelf = NewShelf("Fiction");
        shelf.AddBook("Dune");
        var original = ((IHasDomainEvents)shelf).DomainEvents.ToList();

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });
        await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

        // Records compare all members, EventId and OccurredAt included.
        host.Recorder.Events.Should().Equal(original);
        host.Recorder.Events.Should().AllSatisfy(e => e.Should().NotBeSameAs(original.Single(o => o.EventId == e.EventId)));
    }

    [Fact]
    public void Events_with_class_ids_serialize_as_raw_values_and_round_trip()
    {
        using var host = CreateHost(outbox => outbox.RegisterEvent<UserCreated>());
        var options = host.Options.Outbox!.JsonOptions;
        var original = new UserCreated(UserId.CreateUnique());

        var json = JsonSerializer.Serialize(original, options);
        json.Should().Contain($"\"UserId\":\"{original.UserId.Value}\"", "single value objects are stored as their value");

        var copy = JsonSerializer.Deserialize<UserCreated>(json, options);
        copy.Should().Be(original);
    }

    [Fact]
    public async Task Unknown_event_name_is_recorded_and_does_not_block_other_messages()
    {
        // Only ShelfCreated is registered: BookAdded rows cannot be deserialized.
        var registry = new DomainEventTypeRegistry().Register<ShelfCreated>();
        using var host = new TestHost(
            _db,
            options => options.UseOutbox(o =>
            {
                o.EventTypes.Register<ShelfCreated>();
            }),
            services: services => services.AddOutboxProcessor<LibraryContext>());
        host.Options.Outbox!.EventTypes.Names.Should().BeEquivalentTo(registry.Names);

        var shelf = NewShelf();
        shelf.AddBook("Dune");
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        var processed = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

        processed.Should().Be(1);
        host.Recorder.Events.Should().ContainSingle().Which.Should().BeOfType<ShelfCreated>();

        using var check = _db.CreateLibraryContext();
        var failed = await check.Outbox.SingleAsync(m => m.EventName == nameof(BookAdded), TestContext.Current.CancellationToken);
        failed.ProcessedAt.Should().BeNull();
        failed.Attempts.Should().Be(1);
        failed.LastError.Should().Contain("BookAdded").And.Contain("RegisterEventsFromAssembly");
        (await check.Outbox.SingleAsync(m => m.EventName == "shelf.created", TestContext.Current.CancellationToken)).ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Failing_handler_is_recorded_other_messages_are_processed_and_a_retry_succeeds()
    {
        using var host = CreateHost();
        var shouldFail = true;
        host.Recorder.OnEvent = (_, domainEvent, _) =>
            domainEvent is BookAdded && shouldFail ? throw new InvalidOperationException("mail server down") : Task.CompletedTask;

        var shelf = NewShelf();
        shelf.AddBook("Dune");
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        var processed = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());
        processed.Should().Be(1);

        using (var check = _db.CreateLibraryContext())
        {
            var failed = await check.Outbox.SingleAsync(m => m.EventName == nameof(BookAdded), TestContext.Current.CancellationToken);
            failed.ProcessedAt.Should().BeNull();
            failed.Attempts.Should().Be(1);
            failed.LastError.Should().Be("System.InvalidOperationException: mail server down");
            failed.NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromSeconds(5));
        }

        shouldFail = false;
        var early = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());
        early.Should().Be(0, "the failed message waits for its NextAttemptAt");

        _clock.Advance(TimeSpan.FromSeconds(5));
        var retried = await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());
        retried.Should().Be(1);

        using (var check = _db.CreateLibraryContext())
        {
            var row = await check.Outbox.SingleAsync(m => m.EventName == nameof(BookAdded), TestContext.Current.CancellationToken);
            row.ProcessedAt.Should().NotBeNull();
            row.Attempts.Should().Be(2);
            row.LastError.Should().BeNull();
            row.NextAttemptAt.Should().BeNull();
        }

        host.Recorder.Events.Select(e => e.GetType()).Should().Equal(typeof(ShelfCreated), typeof(BookAdded));
    }

    [Fact]
    public async Task Messages_past_MaxAttempts_are_left_alone()
    {
        using var host = CreateHost(outbox => outbox.MaxAttempts = 2);
        host.Recorder.OnEvent = (_, _, _) => throw new InvalidOperationException("always");

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(NewShelf());
            await context.SaveChangesAsync();
        });

        // An hour apart, well past every wait, so only MaxAttempts can stop a retry.
        for (var i = 0; i < 4; i++)
        {
            await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());
            _clock.Advance(TimeSpan.FromHours(1));
        }

        using var check = _db.CreateLibraryContext();
        var row = await check.Outbox.SingleAsync(TestContext.Current.CancellationToken);
        row.Attempts.Should().Be(2, "after MaxAttempts the processor skips the message");
        row.ProcessedAt.Should().BeNull();
        row.LastError.Should().Contain("always");
    }

    [Fact]
    public async Task Handler_side_effects_on_the_scoped_context_commit_with_the_processed_mark()
    {
        using var host = CreateHost();
        host.Recorder.OnEvent = async (services, domainEvent, ct) =>
        {
            if (domainEvent is ShelfCreated created)
            {
                var context = services.GetRequiredService<LibraryContext>();
                (await context.Shelves.SingleAsync(s => s.Id == created.ShelfId, ct)).Rename("Handled");
            }
        };

        var shelf = NewShelf();
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });
        await host.InScopeAsync((_, services) => services.GetRequiredService<OutboxProcessor<LibraryContext>>().ProcessPendingAsync());

        using var check = _db.CreateLibraryContext();
        check.Shelves.Single(s => s.Id == shelf.Id).Name.Should().Be("Handled");
        // The rename raised ShelfRenamed inside the processor's save: it became a new outbox row.
        check.Outbox.Select(m => m.EventName).Should().BeEquivalentTo("shelf.created", "shelf.renamed");
    }

    [Fact]
    public async Task Background_service_drains_the_outbox()
    {
        using var host = CreateHost();
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(NewShelf("A"));
            context.Shelves.Add(NewShelf("B"));
            await context.SaveChangesAsync();
        });

        var service = new OutboxBackgroundService<LibraryContext>(
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            new OutboxBackgroundServiceOptions<LibraryContext> { BatchSize = 1, PollingInterval = TimeSpan.FromMilliseconds(50) });

        await service.DrainAsync(CancellationToken.None);
        host.Recorder.Events.Should().HaveCount(2, "batches of one are drained until nothing is left");

        // The hosted loop starts, polls and stops cleanly.
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(NewShelf("C"));
            await context.SaveChangesAsync();
        });
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(CancellationToken.None);
        host.Recorder.Events.Should().HaveCount(3);
    }

    [Fact]
    public void AddOutboxBackgroundService_registers_processor_options_and_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework(o => o.UseOutbox().DispatchInProcess((_, _, _) => Task.CompletedTask));
        services.AddOutboxBackgroundService<LibraryContext>(TimeSpan.FromSeconds(1), batchSize: 25);

        services.Should().Contain(d => d.ServiceType == typeof(OutboxProcessor<LibraryContext>));
        services.Should().Contain(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && d.ImplementationType == typeof(OutboxBackgroundService<LibraryContext>));
        var options = services.Single(d => d.ServiceType == typeof(OutboxBackgroundServiceOptions<LibraryContext>)).ImplementationInstance as OutboxBackgroundServiceOptions<LibraryContext>;
        options!.BatchSize.Should().Be(25);
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Processor_requires_outbox_and_dispatcher()
    {
        using var context = _db.CreateLibraryContext();
        var provider = new ServiceCollection().BuildServiceProvider();

        var noOutbox = () => new OutboxProcessor<LibraryContext>(context, provider, new Options.DDDEntityFrameworkOptions().DispatchInProcess((_, _, _) => Task.CompletedTask));
        noOutbox.Should().Throw<InvalidOperationException>().WithMessage("*UseOutbox*");

        var noDispatcher = () => new OutboxProcessor<LibraryContext>(context, provider, new Options.DDDEntityFrameworkOptions().UseOutbox());
        noDispatcher.Should().Throw<InvalidOperationException>().WithMessage("*DispatchInProcess*");
    }

    [Fact]
    public async Task Outbox_without_the_table_in_the_model_fails_with_guidance()
    {
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework(o => o.UseOutbox());
        services.AddDbContext<NoOutboxContext>((sp, options) => options.UseSqlite(_db.Connection).UseDDDToolkit(sp));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NoOutboxContext>();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        context.Shelves.Add(NewShelf());

        var act = () => context.SaveChangesAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*NoOutboxContext*AddDomainEventOutbox*");
    }

    [Fact]
    public void Registry_scans_an_assembly_for_concrete_events_by_stable_name()
    {
        var registry = new DomainEventTypeRegistry().RegisterFromAssembly(typeof(UserCreated).Assembly);

        registry.Names.Should().BeEquivalentTo("user.created", "user.order-placed");
        registry.Resolve("user.created").Should().Be<UserCreated>();
        registry.Resolve("user.order-placed").Should().Be<OrderPlaced>();

        var options = new Options.OutboxOptions().RegisterEventsFromAssemblyContaining<UserCreated>();
        options.EventTypes.Names.Should().BeEquivalentTo(registry.Names);
    }

    [Fact]
    public void Registry_rejects_duplicate_names_and_non_events()
    {
        var registry = new DomainEventTypeRegistry();
        registry.Register<ShelfCreated>().Register<ShelfCreated>();
        registry.Resolve("shelf.created").Should().Be<ShelfCreated>();
        registry.Resolve("nope").Should().BeNull();

        var notAnEvent = () => registry.Register(typeof(string));
        notAnEvent.Should().Throw<ArgumentException>();

        var clash = () => registry.Register<AlsoShelfCreated>();
        clash.Should().Throw<ArgumentException>().WithMessage("*shelf.created*");
    }

    [Abstractions.Attributes.DomainEventName("shelf.created")]
    private sealed record AlsoShelfCreated : DomainEvent;
}
