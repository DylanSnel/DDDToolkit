using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.EntityFramework.Providers.Tests.Providers;

/// <summary>
/// A unique index that says what it refuses with, against a real server. What the toolkit reads from a failed
/// save is each provider's own exception, by the names of its type, and for SQL Server the words of its
/// message: nothing short of the server itself can show that those are the names and the words.
/// </summary>
public abstract class ProviderRefusalTests(ProviderFixture fixture) : ProviderTestBase(fixture)
{
    /// <summary>The schema the server puts a table in when the model names none, as its failure names it.</summary>
    protected abstract string DefaultSchema { get; }

    private DocketContext CreateContext(bool refusing = true)
    {
        var builder = new DbContextOptionsBuilder<DocketContext>();
        Database.Configure(builder);
        if (refusing)
        {
            builder.AddInterceptors(new DatabaseRefusalInterceptor());
        }

        return new DocketContext(builder.Options);
    }

    private async Task SeedAsync()
    {
        await using var context = CreateContext();
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(Cancellation);
        context.Dockets.Add(new Docket { Id = Guid.NewGuid(), Shelf = "A", Number = 7, Code = "A-7" });
        await context.SaveChangesAsync(Cancellation);
    }

    [Fact]
    public async Task A_marked_unique_index_answers_its_refusal()
    {
        SkipIfUnavailable();
        await SeedAsync();

        await using var context = CreateContext();
        context.Dockets.Add(new Docket { Id = Guid.NewGuid(), Shelf = "A", Number = 7, Code = "A-7 again" });

        var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(DocketContext.NumberTaken);
        refusal.Kind.Should().Be(RefusalKind.Conflict);
        refusal.Message.Should().Be("Shelf A already has a docket numbered 7.");
        refusal.Arguments.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Shelf"] = "A", ["Number"] = 7 });
        refusal.InnerException.Should().BeOfType<DbUpdateException>("the refusal keeps the failure it stands for");

        FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<RefusalException>().Which.Code.Should().Be(DocketContext.NumberTaken);
        (await Database.CountRowsAsync("Dockets", schema: null, Cancellation)).Should().Be(1, "nothing was written");
    }

    [Fact]
    public async Task What_the_server_refused_is_read_from_the_failure()
    {
        SkipIfUnavailable();
        await SeedAsync();

        // The index, by the name the model gives it, with its table and the schema the server put it in.
        await using (var context = CreateContext(refusing: false))
        {
            context.Dockets.Add(new Docket { Id = Guid.NewGuid(), Shelf = "A", Number = 7, Code = "A-7 again" });
            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;

            var duplicate = DatabaseRefusal.From(failure)!;
            duplicate.Kind.Should().Be(DatabaseRefusalKind.DuplicateKey);
            duplicate.Constraint.Should().Be("IX_Dockets_Shelf_Number");
            duplicate.Table.Should().Be("Dockets");
            duplicate.Schema.Should().Be(DefaultSchema);
            duplicate.Columns.Should().BeEmpty("the server names the index, not its columns");
        }

        // A unique constraint is read the same way, and is nobody's refusal: only an index says what it refuses with.
        await using (var context = CreateContext())
        {
            context.Dockets.Add(new Docket { Id = Guid.NewGuid(), Shelf = "B", Number = 1, Code = "A-7" });
            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;

            var duplicate = DatabaseRefusal.From(failure)!;
            duplicate.Kind.Should().Be(DatabaseRefusalKind.DuplicateKey);
            duplicate.Constraint.Should().Be("AK_Dockets_Code");
            duplicate.Table.Should().Be("Dockets");
        }

        // Anything else the server refuses is not read as a refusal: a shelf too long for its column.
        await using (var context = CreateContext())
        {
            context.Dockets.Add(new Docket { Id = Guid.NewGuid(), Shelf = new string('x', 80), Number = 2, Code = "X-2" });
            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;

            DatabaseRefusal.From(failure).Should().BeNull();
        }
    }
}
