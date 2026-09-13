using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Options;
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

/// <summary>In-process delivery: before the save, same semantics on both SaveChanges overloads.</summary>
public sealed class DomainEventDispatchTests : IDisposable
{
    private readonly SqliteDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static Shelf NewShelf(string name = "Fiction") => new(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null);

    [Fact]
    public async Task Async_save_dispatches_before_the_row_is_written()
    {
        using var host = new TestHost(_db);
        var rowsAtDispatch = -1;
        host.Recorder.OnBatch = _ => rowsAtDispatch = _db.CountRows("Shelves");

        var shelf = NewShelf();
        shelf.AddBook("Dune");

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        rowsAtDispatch.Should().Be(0, "handlers run before the database write");
        _db.CountRows("Shelves").Should().Be(1);
        host.Recorder.Events.Should().HaveCount(2);
        host.Recorder.Events[0].Should().BeOfType<ShelfCreated>().Which.ShelfId.Should().Be(shelf.Id);
        host.Recorder.Events[1].Should().BeOfType<BookAdded>();
        ((IHasDomainEvents)shelf).DomainEvents.Should().BeEmpty("events are dequeued when dispatched");
    }

    [Fact]
    public void Sync_save_has_the_same_semantics_as_the_async_one()
    {
        using var host = new TestHost(_db);
        var rowsAtDispatch = -1;
        host.Recorder.OnBatch = _ => rowsAtDispatch = _db.CountRows("Shelves");

        host.InScope(context =>
        {
            context.Shelves.Add(NewShelf());
            context.SaveChanges();
        });

        rowsAtDispatch.Should().Be(0);
        _db.CountRows("Shelves").Should().Be(1);
        host.Recorder.Events.Should().ContainSingle().Which.Should().BeOfType<ShelfCreated>();
    }

    [Fact]
    public async Task Throwing_handler_aborts_the_save()
    {
        using var host = new TestHost(_db);
        host.Recorder.OnEvent = (_, _, _) => throw new InvalidOperationException("handler failed");

        var act = () => host.InScopeAsync(async context =>
        {
            context.Shelves.Add(NewShelf());
            await context.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failed");
        _db.CountRows("Shelves").Should().Be(0, "the save never happened");
    }

    [Fact]
    public async Task Handler_changes_on_the_scoped_context_ride_the_same_save_and_their_events_are_dispatched_too()
    {
        using var host = new TestHost(_db);
        host.Recorder.OnEvent = (services, domainEvent, _) =>
        {
            if (domainEvent is ShelfCreated created)
            {
                // The handler gets the scope's provider, hence the very DbContext that is saving.
                var context = services.GetRequiredService<LibraryContext>();
                var shelf = context.Shelves.Local.Single(s => s.Id == created.ShelfId);
                shelf.Rename(created.Name + " (renamed by handler)");
                context.People.Add(new Person(MemberId.CreateUnique(), new PersonName("Side", "Effect"), null, new ValidDateOfBirth(new DateOnly(2000, 1, 1))));
            }

            return Task.CompletedTask;
        };

        var shelf = NewShelf("Fiction");
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        host.Recorder.Events.Select(e => e.GetType()).Should().Equal(typeof(ShelfCreated), typeof(ShelfRenamed));

        using var check = _db.CreateLibraryContext();
        check.Shelves.Single(s => s.Id == shelf.Id).Name.Should().Be("Fiction (renamed by handler)");
        check.People.Should().ContainSingle();
    }

    [Fact]
    public async Task Endless_event_chains_are_cut_off_with_a_clear_error()
    {
        using var host = new TestHost(_db, options => options.MaxDispatchRounds = 3);
        host.Recorder.OnEvent = (services, domainEvent, _) =>
        {
            var context = services.GetRequiredService<LibraryContext>();
            context.Shelves.Local.Single().Rename("again"); // every round raises ShelfRenamed again
            return Task.CompletedTask;
        };

        var act = () => host.InScopeAsync(async context =>
        {
            context.Shelves.Add(NewShelf());
            await context.SaveChangesAsync();
        });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*3 dispatch rounds*shelf.renamed*");
        _db.CountRows("Shelves").Should().Be(0);
    }

    [Fact]
    public async Task Save_without_a_configured_delivery_mode_fails_instead_of_dropping_events()
    {
        using var host = new TestHost(_db, dispatchThroughRecorder: false);

        var act = () => host.InScopeAsync(async context =>
        {
            context.Shelves.Add(NewShelf());
            await context.SaveChangesAsync();
        });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*no delivery mode is configured*DispatchInProcess*UseOutbox*");
    }

    [Fact]
    public async Task Save_without_pending_events_does_not_need_a_delivery_mode()
    {
        using var host = new TestHost(_db, dispatchThroughRecorder: false);

        await host.InScopeAsync(async context =>
        {
            context.People.Add(new Person(MemberId.CreateUnique(), new PersonName("No", "Events"), null, new ValidDateOfBirth(new DateOnly(2000, 1, 1))));
            await context.SaveChangesAsync();
        });

        _db.CountRows("People").Should().Be(1);
    }

    [Fact]
    public async Task Events_raised_on_an_unchanged_but_tracked_aggregate_are_dispatched()
    {
        using var host = new TestHost(_db);
        var shelf = NewShelf();
        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        await host.InScopeAsync(async context =>
        {
            var loaded = await context.Shelves.SingleAsync(s => s.Id == shelf.Id);
            loaded.Rename("Non-fiction");
            await context.SaveChangesAsync();
        });

        host.Recorder.OfType<ShelfRenamed>().Should().ContainSingle().Which.Name.Should().Be("Non-fiction");
    }

    [Fact]
    public async Task Obsolete_UseDomainEvents_overload_still_dispatches()
    {
        var received = new List<IDomainEvent>();
        var services = new ServiceCollection();
#pragma warning disable CS0618 // Testing the obsolete overload on purpose.
        services.UseDomainEvents((_, events) =>
        {
            received.AddRange(events);
            return Task.CompletedTask;
        });
#pragma warning restore CS0618
        services.AddDbContext<LibraryContext>((sp, options) => options.UseSqlite(_db.Connection).AddDomainEventInterceptor(sp));
        using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
            context.Database.EnsureCreated();
            context.Shelves.Add(NewShelf());
            await context.SaveChangesAsync();
        }

        received.Should().ContainSingle().Which.Should().BeOfType<ShelfCreated>();
    }

    [Fact]
    public void Interceptors_can_be_used_without_dependency_injection()
    {
        var received = new List<IDomainEvent>();
        var options = new DDDEntityFrameworkOptions().DispatchInProcess((_, events, _) =>
        {
            received.AddRange(events);
            return Task.CompletedTask;
        });
        var interceptor = new PublishDomainEventsInterceptor(new ServiceCollection().BuildServiceProvider(), options);

        using var context = _db.CreateLibraryContext(builder => builder.AddInterceptors(interceptor, new AggregateVersionInterceptor()));
        context.Database.EnsureCreated();
        context.Shelves.Add(NewShelf());
        context.SaveChanges();

        received.Should().ContainSingle();
    }
}
