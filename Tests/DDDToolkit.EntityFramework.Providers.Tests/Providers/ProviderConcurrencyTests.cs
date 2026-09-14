using DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.Exceptions;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Providers.Tests.Providers;

/// <summary>
/// The concurrency token against a real server. On SQLite every "concurrent" writer is really the same
/// connection to the same in-memory file, so a passing test there says less than it looks like it
/// does: two contexts here are two connections to a server with its own locking.
/// </summary>
public abstract class ProviderConcurrencyTests(ProviderFixture fixture) : ProviderTestBase(fixture)
{
    // Shelf raises events, and the interceptor refuses to save an aggregate whose events have nowhere
    // to go. These tests are about the version, not the events, so they are dispatched to nothing.
    private ProviderHost CreateHost() => new(Database, options => options.DispatchInProcess((_, _, _) => Task.CompletedTask));

    private static Shelf NewShelf(string name = "Fiction")
        => new(ShelfId.CreateUnique(), name, UserId.CreateUnique(), CatId.CreateUnique(), null);

    private async Task<ShelfId> SeedShelfAsync(ProviderHost host, int books = 1)
    {
        var shelf = NewShelf();
        for (var i = 0; i < books; i++)
        {
            shelf.AddBook($"Book {i}", new TagId(i));
        }

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
        });

        return shelf.Id;
    }

    [Fact]
    public async Task Version_is_1_after_the_first_save_and_increments_by_exactly_one_per_save()
    {
        SkipIfUnavailable();

        using var host = CreateHost();
        var shelf = NewShelf();
        shelf.Version.Should().Be(0, "a new aggregate has never been saved");

        await host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync(Cancellation);
            shelf.Version.Should().Be(1);
        });

        await host.InScopeAsync(async context =>
        {
            var loaded = await context.Shelves.SingleAsync(s => s.Id == shelf.Id, Cancellation);
            loaded.Version.Should().Be(1, "reload shows the stored version");

            loaded.Rename("Non-fiction");
            await context.SaveChangesAsync(Cancellation);
            loaded.Version.Should().Be(2);

            await context.SaveChangesAsync(Cancellation);
            loaded.Version.Should().Be(2, "a save without changes does not bump");
        });
    }

    [Fact]
    public async Task Second_writer_of_the_same_aggregate_gets_a_ConcurrencyConflictException()
    {
        SkipIfUnavailable();

        using var host = CreateHost();
        var id = await SeedShelfAsync(host);

        using var scopeA = host.CreateScope();
        using var scopeB = host.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<ProviderContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<ProviderContext>();

        var shelfA = await contextA.Shelves.SingleAsync(s => s.Id == id, Cancellation);
        var shelfB = await contextB.Shelves.SingleAsync(s => s.Id == id, Cancellation);

        shelfA.Rename("A wins");
        await contextA.SaveChangesAsync(Cancellation);

        shelfB.Rename("B loses");
        var act = () => contextB.SaveChangesAsync();

        var exception = (await act.Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        exception.AggregateType.Should().Be<Shelf>();
        exception.AggregateId.Should().Be(id);
        exception.InnerException.Should().BeOfType<DbUpdateConcurrencyException>();

        await host.InScopeAsync(async context =>
        {
            var reloaded = await context.Shelves.SingleAsync(s => s.Id == id, Cancellation);
            reloaded.Name.Should().Be("A wins");
            reloaded.Version.Should().Be(2);
        });
    }

    [Fact]
    public async Task Changing_only_an_owned_child_bumps_the_root_and_conflicts_with_a_concurrent_root_change()
    {
        SkipIfUnavailable();

        using var host = CreateHost();
        var id = await SeedShelfAsync(host, books: 2);

        using var scopeA = host.CreateScope();
        using var scopeB = host.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<ProviderContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<ProviderContext>();
        var shelfA = await contextA.Shelves.SingleAsync(s => s.Id == id, Cancellation);
        var shelfB = await contextB.Shelves.SingleAsync(s => s.Id == id, Cancellation);

        shelfA.Books[0].Retitle("Dune (child-only change)");
        await contextA.SaveChangesAsync(Cancellation);
        shelfA.Version.Should().Be(2, "a child change is a change of the aggregate");

        shelfB.Rename("stale root");
        var act = () => contextB.SaveChangesAsync();

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Deleting_an_aggregate_with_a_stale_version_conflicts()
    {
        SkipIfUnavailable();

        using var host = CreateHost();
        var id = await SeedShelfAsync(host);

        using var scopeA = host.CreateScope();
        using var scopeB = host.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<ProviderContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<ProviderContext>();
        var shelfA = await contextA.Shelves.SingleAsync(s => s.Id == id, Cancellation);
        var shelfB = await contextB.Shelves.SingleAsync(s => s.Id == id, Cancellation);

        shelfA.Rename("changed first");
        await contextA.SaveChangesAsync(Cancellation);

        contextB.Shelves.Remove(shelfB);
        var act = () => contextB.SaveChangesAsync();

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        (await Database.CountRowsAsync("Shelves", schema: null, Cancellation)).Should().Be(1);
    }
}
