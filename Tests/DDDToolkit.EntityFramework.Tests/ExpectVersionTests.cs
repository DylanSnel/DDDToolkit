using DDDToolkit.Abstractions.Access;
using DDDToolkit.EntityFramework.Tests.Converters;
using DDDToolkit.EntityFramework.Tests.Domain;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;
using DDDToolkit.ExampleLibrary.Common.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// <c>ExpectVersion</c>: the version a client last saw decides whether its change is made. A shelf somebody
/// renamed after the client read it is a conflict before anything changes, and one renamed after the server
/// loaded it is the same conflict at the save.
/// </summary>
public sealed class ExpectVersionTests : IDisposable
{
    private readonly SqliteDatabase _db = new();
    private readonly TestHost _host;

    public ExpectVersionTests() => _host = new TestHost(_db);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _host.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task A_loaded_aggregate_of_another_version_is_a_conflict_before_anything_changes()
    {
        // The client read the shelf at version 1, and somebody renamed it since.
        var id = await SeedShelfAsync();
        await RenameAsync(id, "Renamed meanwhile");

        using var scope = _host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
        var shelf = await context.Shelves.SingleAsync(s => s.Id == id, Cancellation);
        shelf.Version.Should().Be(2);

        var conflict = context.Invoking(c => c.ExpectVersion(shelf, 1)).Should().Throw<ConcurrencyConflictException>().Which;
        conflict.AggregateType.Should().Be<Shelf>();
        conflict.AggregateId.Should().Be(id);
        conflict.Message.Should().Contain("Shelf").And.Contain(id.ToString());
        conflict.InnerException.Should().BeNull("no save failed: the versions were compared before one was tried");

        // Nothing changed, in the context or in the database, and the same context still saves nothing.
        context.ChangeTracker.HasChanges().Should().BeFalse();
        await context.SaveChangesAsync(Cancellation);
        (await StoredAsync(id)).Should().Be(("Renamed meanwhile", 2L));

        // A version from the future is as wrong as one from the past.
        context.Invoking(c => c.ExpectVersion(shelf, 3)).Should().Throw<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task A_change_made_after_the_load_is_a_conflict_at_the_save()
    {
        var id = await SeedShelfAsync();

        using var early = _host.CreateScope();
        using var late = _host.CreateScope();
        var earlyContext = early.ServiceProvider.GetRequiredService<LibraryContext>();
        var lateContext = late.ServiceProvider.GetRequiredService<LibraryContext>();

        // The client's version is the stored one when the server loads the shelf, so the expectation holds.
        var shelf = await lateContext.Shelves.SingleAsync(s => s.Id == id, Cancellation);
        lateContext.ExpectVersion(shelf, 1);

        // Somebody else changes it between that load and the save.
        (await earlyContext.Shelves.SingleAsync(s => s.Id == id, Cancellation)).Rename("First");
        await earlyContext.SaveChangesAsync(Cancellation);

        shelf.Rename("Too late");
        var conflict = (await lateContext.Invoking(c => c.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        conflict.AggregateType.Should().Be<Shelf>();
        conflict.AggregateId.Should().Be(id);
        conflict.InnerException.Should().BeOfType<DbUpdateConcurrencyException>("this one is the save's own");

        (await StoredAsync(id)).Should().Be(("First", 2L));
    }

    [Fact]
    public async Task The_expected_version_saves_and_bumps_as_usual()
    {
        var id = await SeedShelfAsync();

        using var scope = _host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
        var shelf = await context.Shelves.SingleAsync(s => s.Id == id, Cancellation);

        context.ExpectVersion(shelf, 1);
        context.ChangeTracker.HasChanges().Should().BeFalse("an expectation that holds changes nothing");

        shelf.Rename("Expected");
        await context.SaveChangesAsync(Cancellation);

        shelf.Version.Should().Be(2);
        (await StoredAsync(id)).Should().Be(("Expected", 2L));

        // The context moved on with the save: what it holds now is version 2, and version 1 is the past.
        context.ExpectVersion(shelf, 2);
        context.Invoking(c => c.ExpectVersion(shelf, 1)).Should().Throw<ConcurrencyConflictException>();

        // After the change and before the save, the expectation is still about what was loaded.
        shelf.Rename("Expected again");
        context.ExpectVersion(shelf, 2);
        await context.SaveChangesAsync(Cancellation);
        (await StoredAsync(id)).Should().Be(("Expected again", 3L));
    }

    [Fact]
    public async Task A_removal_with_the_expected_version_goes_through_and_a_stale_one_does_not()
    {
        var id = await SeedShelfAsync();
        await RenameAsync(id, "Renamed meanwhile");

        using var scope = _host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();
        var shelf = await context.Shelves.SingleAsync(s => s.Id == id, Cancellation);

        context.Shelves.Remove(shelf);
        context.Invoking(c => c.ExpectVersion(shelf, 1)).Should().Throw<ConcurrencyConflictException>("the client would remove a shelf it has not seen as it is");
        _db.CountRows("Shelves").Should().Be(1);

        context.ExpectVersion(shelf, 2);
        await context.SaveChangesAsync(Cancellation);
        _db.CountRows("Shelves").Should().Be(0);
    }

    [Fact]
    public async Task An_aggregate_the_context_did_not_load_is_refused()
    {
        var id = await SeedShelfAsync();
        var elsewhere = await _host.InScopeAsync(async (other, _) => await other.Shelves.AsNoTracking().SingleAsync(s => s.Id == id));

        using var scope = _host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LibraryContext>();

        // Held, but not by this context: there is no loaded version to compare, and nothing a save would check.
        context.Invoking(c => c.ExpectVersion(elsewhere, 1)).Should().Throw<InvalidOperationException>().WithMessage("*not tracked*");

        // New: nobody can have seen a version of it.
        var added = new Shelf(ShelfId.CreateUnique(), "New", UserId.CreateUnique(), CatId.CreateUnique(), null);
        context.Shelves.Add(added);
        context.Invoking(c => c.ExpectVersion(added, 0)).Should().Throw<InvalidOperationException>().WithMessage("*no stored version*");

        context.Invoking(c => c.ExpectVersion<Shelf>(null!, 1)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => AggregateVersionExtensions.ExpectVersion(null!, elsewhere, 1)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_model_that_does_not_compare_the_version_is_refused()
    {
        // Without the toolkit's conventions Version is a column like any other, and a save would not compare it:
        // an expectation that only holds at the load would look like protection and be none.
        using var context = new UnversionedContext(_db.Options<UnversionedContext>());
        var pallet = new Pallet(PalletId.CreateSequential(), new DepotId(1), 1, "Unversioned", owner: null);
        context.Attach(pallet);

        context.Invoking(c => c.ExpectVersion(pallet, 0))
            .Should().Throw<InvalidOperationException>().WithMessage("*concurrency token*AddDDDToolkitConventions*");
    }

    private async Task<ShelfId> SeedShelfAsync()
    {
        var shelf = new Shelf(ShelfId.CreateUnique(), "Fiction", UserId.CreateUnique(), CatId.CreateUnique(), null);
        await _host.InScopeAsync(async context =>
        {
            context.Shelves.Add(shelf);
            await context.SaveChangesAsync();
        });

        return shelf.Id;
    }

    private Task RenameAsync(ShelfId id, string name)
        => _host.InScopeAsync(async context =>
        {
            (await context.Shelves.SingleAsync(s => s.Id == id)).Rename(name);
            await context.SaveChangesAsync();
        });

    private Task<(string Name, long Version)> StoredAsync(ShelfId id)
        => _host.InScopeAsync(async (context, _) =>
        {
            var shelf = await context.Shelves.AsNoTracking().SingleAsync(s => s.Id == id);
            return (shelf.Name, shelf.Version);
        });

    /// <summary>A pallet mapped without the toolkit's conventions: its ids convert, and its version is not a concurrency token.</summary>
    private sealed class UnversionedContext(DbContextOptions<UnversionedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Pallet>(pallet =>
            {
                pallet.Ignore(row => row.Stamps);
                pallet.Ignore(row => row.DomainEvents);
            });

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
            => configurationBuilder.AddEfTestsConverters();
    }
}

/// <summary>
/// The same three answers on Postgres, under row level security: Alice's own pallet, saved the way
/// <c>UseDDDToolkit</c> makes a context save, where a statement that finds no row is read again before it is
/// called a conflict.
/// </summary>
[Collection(PalletDepotDatabase.Collection)]
public sealed class ExpectVersionPostgresTests(PalletDepotDatabase database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Caller Alice => PalletDepotDatabase.Alice;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>The depot as it was seeded, for the next test of any class of the collection.</summary>
    public async ValueTask DisposeAsync() => await database.RestoreAsync();

    [Fact]
    public async Task A_loaded_aggregate_of_another_version_is_a_conflict_before_anything_changes()
    {
        database.Require();
        var id = await SeedPalletAsync(number: 61);
        await RetitleAsync(id, "Retitled meanwhile, 61");

        await using var context = database.CreateSavingContext(Alice);
        var pallet = await context.Pallets.SingleAsync(row => row.Id == id, Cancellation);
        pallet.Version.Should().Be(2);

        var conflict = context.Invoking(c => c.ExpectVersion(pallet, 1)).Should().Throw<ConcurrencyConflictException>().Which;
        conflict.AggregateType.Should().Be<Pallet>();
        conflict.AggregateId.Should().Be(id);
        conflict.InnerException.Should().BeNull();

        context.ChangeTracker.HasChanges().Should().BeFalse();
        (await StoredAsync(id)).Should().Be(("Retitled meanwhile, 61", 2L));
    }

    [Fact]
    public async Task A_change_made_after_the_load_is_a_conflict_at_the_save()
    {
        database.Require();
        var id = await SeedPalletAsync(number: 62);

        await using var late = database.CreateSavingContext(Alice);
        var pallet = await late.Pallets.SingleAsync(row => row.Id == id, Cancellation);
        late.ExpectVersion(pallet, 1);

        await RetitleAsync(id, "First, 62");

        // The statement finds no row. The pallet is read again, as Alice: it is there and its version moved, so
        // somebody else wrote, and this is the lost race and not a denial.
        pallet.Retitle("Too late, 62");
        var conflict = (await late.Invoking(c => c.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        conflict.AggregateId.Should().Be(id);
        conflict.InnerException.Should().BeOfType<DbUpdateConcurrencyException>();

        (await StoredAsync(id)).Should().Be(("First, 62", 2L));
    }

    [Fact]
    public async Task The_expected_version_saves_and_bumps_as_usual()
    {
        database.Require();
        var id = await SeedPalletAsync(number: 63);

        await using var context = database.CreateSavingContext(Alice);
        var pallet = await context.Pallets.SingleAsync(row => row.Id == id, Cancellation);

        context.ExpectVersion(pallet, 1);
        pallet.Retitle("Expected, 63");
        await context.SaveChangesAsync(Cancellation);

        pallet.Version.Should().Be(2);
        (await StoredAsync(id)).Should().Be(("Expected, 63", 2L));
    }

    /// <summary>
    /// A pallet of Alice's own in the south depot, under a number and a label no other class of the collection
    /// uses: both are unique in the database the classes share.
    /// </summary>
    private async Task<PalletId> SeedPalletAsync(int number)
    {
        var id = PalletId.CreateSequential();
        await using var context = database.CreateSavingContext(Alice);
        context.Pallets.Add(new Pallet(id, PalletDepotDatabase.South, number, $"As it was read, {number}", PalletDepotDatabase.AliceId));
        await context.SaveChangesAsync(Cancellation);
        return id;
    }

    private async Task RetitleAsync(PalletId id, string label)
    {
        await using var context = database.CreateSavingContext(Alice);
        (await context.Pallets.SingleAsync(row => row.Id == id, Cancellation)).Retitle(label);
        await context.SaveChangesAsync(Cancellation);
    }

    private async Task<(string Label, long Version)> StoredAsync(PalletId id)
    {
        await using var context = database.CreateContext(Alice);
        var pallet = await context.Pallets.AsNoTracking().SingleAsync(row => row.Id == id, Cancellation);
        return (pallet.Label, pallet.Version);
    }
}
