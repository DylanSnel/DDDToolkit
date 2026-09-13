using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>Optimistic concurrency through the Version token and the AggregateVersionInterceptor.</summary>
public sealed class ConcurrencyTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly TestHost _host;

    public ConcurrencyTests() => _host = new TestHost(_db);

    public void Dispose()
    {
        _host.Dispose();
        _db.Dispose();
    }

    private async Task<ShelfId> SeedShelfAsync(int books = 1, int notes = 0)
    {
        var shelf = new Shelf(ShelfId.CreateUnique(), "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null);
        for (var i = 0; i < books; i++)
        {
            shelf.AddBook($"Book {i}", new TagId(i));
        }

        for (var i = 0; i < notes; i++)
        {
            shelf.AddNote($"Note {i}");
        }

        await _host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        return shelf.Id;
    }

    private async Task<long> StoredVersionAsync(ShelfId id)
        => await _host.InScopeAsync(async (context, _) => (await context.Shelves.SingleAsync(s => s.Id == id)).Version);

    [Fact]
    public async Task Version_is_1_after_the_first_save_and_increments_by_exactly_one_per_save()
    {
        var shelf = new Shelf(ShelfId.CreateUnique(), "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null);
        shelf.Version.Should().Be(0, "a new aggregate has never been saved");

        await _host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
            shelf.Version.Should().Be(1);
        });
        (await StoredVersionAsync(shelf.Id)).Should().Be(1, "reload shows the stored version");

        await _host.InScopeAsync(async context =>
        {
            var loaded = await context.Shelves.SingleAsync(s => s.Id == shelf.Id);
            loaded.Rename("Non-fiction");
            await context.SaveChangesAsync();
            loaded.Version.Should().Be(2);

            loaded.Rename("Reference");
            context.SaveChanges();
            loaded.Version.Should().Be(3, "the sync path bumps the same way");

            await context.SaveChangesAsync();
            loaded.Version.Should().Be(3, "a save without changes does not bump");
        });

        (await StoredVersionAsync(shelf.Id)).Should().Be(3);
    }

    [Fact]
    public async Task Second_writer_of_the_same_aggregate_gets_a_ConcurrencyConflictException()
    {
        var id = await SeedShelfAsync();

        using var scopeA = _host.CreateScope();
        using var scopeB = _host.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<LibraryContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<LibraryContext>();

        var shelfA = await contextA.Shelves.SingleAsync(s => s.Id == id);
        var shelfB = await contextB.Shelves.SingleAsync(s => s.Id == id);

        shelfA.Rename("A wins");
        await contextA.SaveChangesAsync();

        shelfB.Rename("B loses");
        var act = () => contextB.SaveChangesAsync();

        var exception = (await act.Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        exception.AggregateType.Should().Be<Shelf>();
        exception.AggregateId.Should().Be(id);
        exception.Message.Should().Contain("Shelf").And.Contain(id.ToString());
        exception.InnerException.Should().BeOfType<DbUpdateConcurrencyException>();

        (await StoredVersionAsync(id)).Should().Be(2);
        (await _host.InScopeAsync(async (context, _) => (await context.Shelves.SingleAsync(s => s.Id == id)).Name)).Should().Be("A wins");
    }

    [Fact]
    public void Sync_SaveChanges_also_translates_the_conflict()
    {
        var id = SeedShelfAsync().GetAwaiter().GetResult();

        using var scopeA = _host.CreateScope();
        using var scopeB = _host.CreateScope();
        var shelfA = scopeA.ServiceProvider.GetRequiredService<LibraryContext>().Shelves.Single(s => s.Id == id);
        var contextB = scopeB.ServiceProvider.GetRequiredService<LibraryContext>();
        var shelfB = contextB.Shelves.Single(s => s.Id == id);

        shelfA.Rename("A");
        scopeA.ServiceProvider.GetRequiredService<LibraryContext>().SaveChanges();
        shelfB.Rename("B");

        var act = () => contextB.SaveChanges();

        act.Should().Throw<ConcurrencyConflictException>().Which.InnerException.Should().BeOfType<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Changing_only_an_owned_child_bumps_the_root_and_conflicts_with_a_concurrent_root_change()
    {
        var id = await SeedShelfAsync(books: 2);

        using var scopeA = _host.CreateScope();
        using var scopeB = _host.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<LibraryContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<LibraryContext>();
        var shelfA = await contextA.Shelves.SingleAsync(s => s.Id == id);
        var shelfB = await contextB.Shelves.SingleAsync(s => s.Id == id);

        shelfA.Books[0].Retitle("Dune (child-only change)");
        await contextA.SaveChangesAsync();
        shelfA.Version.Should().Be(2, "a child change is a change of the aggregate");

        shelfB.Rename("stale root");
        var act = () => contextB.SaveChangesAsync();
        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Two_concurrent_child_only_changes_conflict_too()
    {
        var id = await SeedShelfAsync(books: 2);

        using var scopeA = _host.CreateScope();
        using var scopeB = _host.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<LibraryContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<LibraryContext>();
        var shelfA = await contextA.Shelves.SingleAsync(s => s.Id == id);
        var shelfB = await contextB.Shelves.SingleAsync(s => s.Id == id);

        shelfA.Books[0].Tag(new TagId(42)); // primitive collection on an owned child
        await contextA.SaveChangesAsync();

        shelfB.Books[1].Retitle("other child");
        var act = () => contextB.SaveChangesAsync();

        (await act.Should().ThrowAsync<ConcurrencyConflictException>()).Which.AggregateType.Should().Be<Shelf>();
    }

    [Fact]
    public async Task Adding_and_removing_children_bumps_the_root_once_per_save()
    {
        var id = await SeedShelfAsync(books: 1, notes: 1);

        await _host.InScopeAsync(async context =>
        {
            var shelf = await context.Shelves.SingleAsync(s => s.Id == id);
            shelf.AddBook("New");
            shelf.AddBook("Newer");
            shelf.Books[0].Retitle("Changed");
            shelf.Rename("Everything at once");
            await context.SaveChangesAsync();
            shelf.Version.Should().Be(2, "one save, one increment, however many entries changed");
        });

        await _host.InScopeAsync(async context =>
        {
            var shelf = await context.Shelves.SingleAsync(s => s.Id == id);
            shelf.RemoveBook(shelf.Books[0].Id).Should().BeTrue();
            await context.SaveChangesAsync();
            shelf.Version.Should().Be(3, "removing an owned child changes the aggregate");
        });

        await _host.InScopeAsync(async context =>
        {
            var shelf = await context.Shelves.SingleAsync(s => s.Id == id);
            shelf.AddNote("set-backed collection");
            await context.SaveChangesAsync();
            shelf.Version.Should().Be(4);
        });

        (await StoredVersionAsync(id)).Should().Be(4);
    }

    [Fact]
    public async Task Unrelated_aggregates_in_the_same_save_are_versioned_independently()
    {
        var shelfId = await SeedShelfAsync();
        var personId = MemberId.CreateUnique();

        await _host.InScopeAsync(async context =>
        {
            var shelf = await context.Shelves.SingleAsync(s => s.Id == shelfId);
            shelf.Rename("Touched");
            var person = new Person(personId, new PersonName("Grace", "Hopper"), null, new ValidDateOfBirth(new DateOnly(1906, 12, 9)));
            context.People.Add(person);
            await context.SaveChangesAsync();

            shelf.Version.Should().Be(2);
            person.Version.Should().Be(1);
        });

        await _host.InScopeAsync(async context =>
        {
            (await context.People.SingleAsync(p => p.Id == personId)).Version.Should().Be(1);
        });
    }

    [Fact]
    public async Task Deleting_an_aggregate_with_a_stale_version_conflicts()
    {
        var id = await SeedShelfAsync();

        using var scopeA = _host.CreateScope();
        using var scopeB = _host.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<LibraryContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<LibraryContext>();
        var shelfA = await contextA.Shelves.SingleAsync(s => s.Id == id);
        var shelfB = await contextB.Shelves.SingleAsync(s => s.Id == id);

        shelfA.Rename("changed first");
        await contextA.SaveChangesAsync();

        contextB.Shelves.Remove(shelfB);
        var act = () => contextB.SaveChangesAsync();

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        _db.CountRows("Shelves").Should().Be(1);
    }
}
