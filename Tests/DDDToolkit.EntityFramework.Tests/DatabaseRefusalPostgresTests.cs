using DDDToolkit.Abstractions.Access;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What Postgres refuses in a save, as the caller gets it: the refusal a unique index declares, and
/// <c>access.refused</c> for a row a policy denies, whether Postgres says so or leaves the statement no row. A lost
/// race stays a conflict, and a missing privilege stays the failure it is. Alice and Bob each own a pallet, and
/// both read both.
/// </summary>
[Collection(PalletDepotDatabase.Collection)]
public sealed class DatabaseRefusalPostgresTests(PalletDepotDatabase database) : IAsyncLifetime
{
    private const string Pallets = PalletContext.Schema + ".\"Pallets\"";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Caller Alice => PalletDepotDatabase.Alice;

    private static Caller Bob => PalletDepotDatabase.Bob;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>The depot as it was seeded, for the next test of any class of the collection.</summary>
    public async ValueTask DisposeAsync() => await database.RestoreAsync();

    [Fact]
    public async Task A_marked_unique_index_answers_its_refusal_on_postgres()
    {
        database.Require();
        var before = await database.CountAsync(Cancellation);

        // Bob's pallet is number 2 of the north depot.
        await using var context = database.CreateSavingContext(Alice);
        context.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.North, 2, "Alice's second", PalletDepotDatabase.AliceId));

        var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(PalletContext.NumberTaken);
        refusal.Kind.Should().Be(RefusalKind.Conflict);
        refusal.Message.Should().Be("Depot 1 already has a pallet numbered 2, so 'Alice's second' cannot take it.");
        refusal.Arguments.Should().BeEquivalentTo(new Dictionary<string, object?> { ["Depot"] = 1, ["Number"] = 2, ["Label"] = "Alice's second" });
        refusal.InnerException.Should().BeOfType<DbUpdateException>("the refusal keeps the failure it stands for")
            .Which.InnerException.Should().BeOfType<PostgresException>().Which.ConstraintName.Should().Be("IX_Pallets_Depot_Number");

        // The save without await answers the same.
        FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<RefusalException>().Which.Code.Should().Be(PalletContext.NumberTaken);

        (await database.CountAsync(Cancellation)).Should().Be(before, "nothing was written");
    }

    [Fact]
    public async Task Rows_of_one_save_that_differ_leave_the_message_as_written_on_postgres()
    {
        database.Require();

        // Npgsql sends a save as one batch and Postgres does not say which row of it broke the index: with two
        // pallets that differ in what the message names, nothing is filled in. The code is the same.
        await using var context = database.CreateSavingContext(Alice);
        context.Pallets.AddRange(
            new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 7, "A free number", PalletDepotDatabase.AliceId),
            new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.North, 2, "Bob's number", PalletDepotDatabase.AliceId));

        var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(PalletContext.NumberTaken);
        refusal.Message.Should().Be(PalletContext.NumberTakenText);
        refusal.Arguments.Should().BeEmpty();
    }

    [Fact]
    public async Task An_insert_the_policies_deny_is_access_refused()
    {
        database.Require();
        var before = await database.CountAsync(Cancellation);

        // A pallet in somebody else's name: the policy's check refuses the new row.
        await using var context = database.CreateSavingContext(Alice);
        context.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 1, "Bob's, says Alice", PalletDepotDatabase.BobId));

        var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(ToolkitRefusals.Refused);
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        refusal.Message.Should().Be("The database refused this change.");
        refusal.Arguments.Should().BeEmpty("the caller is told no more than that");
        refusal.InnerException.Should().BeOfType<DbUpdateException>()
            .Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<RefusalException>().Which.Code.Should().Be(ToolkitRefusals.Refused);

        (await database.CountAsync(Cancellation)).Should().Be(before, "nothing was written");
    }

    [Fact]
    public async Task An_update_the_policies_deny_is_access_refused_and_a_real_conflict_stays_a_conflict()
    {
        database.Require();

        // Bob reads Alice's pallet and may not change it: his statement finds no row, as a lost race would. The
        // pallet is still there as he loaded it, so nobody else wrote, and the save is refused.
        await using (var context = database.CreateSavingContext(Bob))
        {
            var alices = await context.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            alices.Retitle("Bob was here");

            var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
            refusal.Code.Should().Be(ToolkitRefusals.Refused);
            refusal.Kind.Should().Be(RefusalKind.NotPermitted);
            refusal.InnerException.Should().BeOfType<ConcurrencyConflictException>("the refusal keeps what the save looked like")
                .Which.AggregateId.Should().Be(PalletDepotDatabase.AlicesPallet);

            FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<RefusalException>().Which.Code.Should().Be(ToolkitRefusals.Refused);
        }

        // Her own pallet, handed to Bob: the row she may change becomes one she may not have written, and
        // Postgres says so.
        await using (var context = database.CreateSavingContext(Alice))
        {
            var hers = await context.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            hers.HandTo(PalletDepotDatabase.BobId);

            var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
            refusal.Code.Should().Be(ToolkitRefusals.Refused);
            refusal.InnerException.Should().BeOfType<DbUpdateException>().Which.InnerException.Should().BeOfType<PostgresException>();
        }

        // A lost race: Alice changes her pallet in one context while another still holds it as it was.
        await using (var first = database.CreateSavingContext(Alice))
        await using (var second = database.CreateSavingContext(Alice))
        {
            var early = await first.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            var late = await second.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);

            early.Retitle("Alice's, checked");
            await first.SaveChangesAsync(Cancellation);

            late.Retitle("Alice's, too late");
            var conflict = (await FluentActions.Awaiting(() => second.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>()).Which;
            conflict.AggregateType.Should().Be<Pallet>();
            conflict.InnerException.Should().BeOfType<DbUpdateConcurrencyException>();
            FluentActions.Invoking(() => second.SaveChanges()).Should().Throw<ConcurrencyConflictException>();

            early.Retitle("Alice's");
            await first.SaveChangesAsync(Cancellation);
        }

        // And a pallet somebody removed meanwhile is a conflict too: it is not there to be read again.
        var passing = PalletId.CreateSequential();
        await using (var first = database.CreateSavingContext(Alice))
        await using (var second = database.CreateSavingContext(Alice))
        {
            first.Pallets.Add(new Pallet(passing, PalletDepotDatabase.South, 40, "Passing through", PalletDepotDatabase.AliceId));
            await first.SaveChangesAsync(Cancellation);

            var late = await second.Pallets.SingleAsync(pallet => pallet.Id == passing, Cancellation);
            first.Pallets.Remove(await first.Pallets.SingleAsync(pallet => pallet.Id == passing, Cancellation));
            await first.SaveChangesAsync(Cancellation);

            late.Retitle("Still here?");
            await FluentActions.Awaiting(() => second.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>();
        }

        await using var check = database.CreateContext(Alice);
        (await check.Pallets.AsNoTracking().SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation))
            .Should().Match<Pallet>(pallet => pallet.Label == "Alice's" && pallet.Owner == PalletDepotDatabase.AliceId, "no refused save wrote anything");
    }

    [Fact]
    public async Task A_row_the_caller_cannot_read_stays_a_conflict()
    {
        database.Require();

        // Nobody who is not signed in reads a pallet. Such a caller that holds one all the same, as it is in the
        // database, and saves a change to it finds no row, and does not find it when it is read again either: the
        // read runs as that caller, not as the application, which owns the table and would see it. To this
        // caller the pallet is not there, and a save of what is not there is the conflict it always was.
        Pallet held;
        await using (var alice = database.CreateContext(Alice))
        {
            held = await alice.Pallets.AsNoTracking().SingleAsync(pallet => pallet.Id == PalletDepotDatabase.BobsPallet, Cancellation);
        }

        await using var context = database.CreateSavingContext(Caller.Anonymous);
        (await context.Pallets.AsNoTracking().CountAsync(Cancellation)).Should().Be(0, "the caller reads no pallet");
        context.Attach(held);
        held.Retitle("Nobody was here");

        var conflict = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        conflict.AggregateId.Should().Be(PalletDepotDatabase.BobsPallet);
        FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<ConcurrencyConflictException>();

        await using var check = database.CreateContext(Bob);
        (await check.Pallets.AsNoTracking().SingleAsync(pallet => pallet.Id == PalletDepotDatabase.BobsPallet, Cancellation)).Label.Should().Be("Bob's");
    }

    [Fact]
    public async Task A_delete_the_policies_deny_is_access_refused()
    {
        database.Require();
        var before = await database.CountAsync(Cancellation);

        await using var context = database.CreateSavingContext(Bob);
        context.Pallets.Remove(await context.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation));

        var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(ToolkitRefusals.Refused);
        refusal.InnerException.Should().BeOfType<ConcurrencyConflictException>();

        (await database.CountAsync(Cancellation)).Should().Be(before, "the pallet is still there");
    }

    [Fact]
    public async Task A_change_of_an_entity_the_policies_deny_is_judged_by_its_aggregate_root()
    {
        database.Require();

        // Alice owns the pallet, so its row is hers to change, and the save bumps its version. The stamp is the
        // pallet's entity, in a table whose policies let nobody change one: that statement finds no row, and it
        // has no version of its own to compare. The pallet is unchanged, so nobody else wrote, and it is a denial.
        await using (var context = database.CreateSavingContext(Alice))
        {
            var hers = await context.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            var loaded = hers.Version;
            hers.Stamps.Should().ContainSingle().Which.Reword("Sturdy");

            var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
            refusal.Code.Should().Be(ToolkitRefusals.Refused);
            refusal.InnerException.Should().BeOfType<ConcurrencyConflictException>();

            await using var check = database.CreateContext(Alice);
            var stored = await check.Pallets.AsNoTracking().SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            stored.Version.Should().Be(loaded, "the save was rolled back, the pallet's own row with it");
            stored.Stamps.Should().ContainSingle().Which.Text.Should().Be("Fragile");
        }

        // The same change after somebody else changed the pallet is a lost race: its version moved.
        await using (var first = database.CreateSavingContext(Alice))
        await using (var second = database.CreateSavingContext(Alice))
        {
            var early = await first.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            var late = await second.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);

            early.Retitle("Alice's, checked");
            await first.SaveChangesAsync(Cancellation);

            late.Stamps.Single().Reword("Sturdy");
            await FluentActions.Awaiting(() => second.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>();

            early.Retitle("Alice's");
            await first.SaveChangesAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_save_inside_a_transaction_of_the_callers_is_answered_the_same_way()
    {
        database.Require();
        var before = await database.CountAsync(Cancellation);

        // A unit of work that opens its own transaction and saves more than once. A denied update finds no row:
        // the pallet is read again inside that transaction, and the transaction goes on.
        await using (var context = database.CreateSavingContext(Bob))
        await using (var transaction = await context.Database.BeginTransactionAsync(Cancellation))
        {
            var alices = await context.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            alices.Retitle("Bob was here");

            var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
            refusal.Code.Should().Be(ToolkitRefusals.Refused);
            refusal.InnerException.Should().BeOfType<ConcurrencyConflictException>();

            (await context.Pallets.AsNoTracking().CountAsync(Cancellation)).Should().Be((int)before, "the transaction is not broken by the refused save");
            await transaction.RollbackAsync(Cancellation);
        }

        // A number that is taken and a row the policy's check refuses both fail their statement. Entity Framework
        // goes back to the savepoint it made, so each is answered as it is outside a transaction, and the
        // transaction still commits what it saves next.
        var kept = PalletId.CreateSequential();
        await using (var context = database.CreateSavingContext(Alice))
        await using (var transaction = await context.Database.BeginTransactionAsync(Cancellation))
        {
            var taken = new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.North, 2, "Alice's second", PalletDepotDatabase.AliceId);
            context.Pallets.Add(taken);
            (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>())
                .Which.Code.Should().Be(PalletContext.NumberTaken);
            context.Entry(taken).State = EntityState.Detached;

            var bobs = new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 1, "Bob's, says Alice", PalletDepotDatabase.BobId);
            context.Pallets.Add(bobs);
            (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>())
                .Which.Code.Should().Be(ToolkitRefusals.Refused);
            context.Entry(bobs).State = EntityState.Detached;

            context.Pallets.Add(new Pallet(kept, PalletDepotDatabase.South, 41, "Kept", PalletDepotDatabase.AliceId));
            await context.SaveChangesAsync(Cancellation);
            await transaction.CommitAsync(Cancellation);
        }

        (await database.CountAsync(Cancellation)).Should().Be(before + 1, "only the pallet that was allowed was written");

        // A lost race inside a transaction stays a conflict: the read sees what the other save committed.
        await using (var first = database.CreateSavingContext(Alice))
        await using (var second = database.CreateSavingContext(Alice))
        await using (var transaction = await second.Database.BeginTransactionAsync(Cancellation))
        {
            var early = await first.Pallets.SingleAsync(pallet => pallet.Id == kept, Cancellation);
            var late = await second.Pallets.SingleAsync(pallet => pallet.Id == kept, Cancellation);

            early.Retitle("Kept, checked");
            await first.SaveChangesAsync(Cancellation);

            late.Retitle("Kept, too late");
            await FluentActions.Awaiting(() => second.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>();
            await transaction.RollbackAsync(Cancellation);

            first.Pallets.Remove(early);
            await first.SaveChangesAsync(Cancellation);
        }

        (await database.CountAsync(Cancellation)).Should().Be(before, "the depot is as the other tests expect it");
    }

    [Fact]
    public async Task A_missing_privilege_is_not_a_refusal()
    {
        database.Require();

        // No policy refuses anything here: the role was never granted the command. That is the application's
        // own set-up, and it fails as it always did.
        await database.RunAsOwnerAsync("REVOKE INSERT, UPDATE ON " + Pallets + " FROM authenticated", Cancellation);
        try
        {
            await using var inserting = database.CreateSavingContext(Alice);
            inserting.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 2, "Alice's second", PalletDepotDatabase.AliceId));
            var insert = (await FluentActions.Awaiting(() => inserting.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
            insert.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            DatabaseRefusal.From(insert).Should().BeNull();

            await using var updating = database.CreateSavingContext(Alice);
            var hers = await updating.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            hers.Retitle("Alice's, renamed");
            var update = (await FluentActions.Awaiting(() => updating.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
            update.Should().NotBeOfType<DbUpdateConcurrencyException>();
            update.InnerException.Should().BeOfType<PostgresException>().Which.MessageText.Should().StartWith("permission denied");
        }
        finally
        {
            await database.RunAsOwnerAsync("GRANT INSERT, UPDATE ON " + Pallets + " TO authenticated", Cancellation);
        }
    }

    [Fact]
    public async Task A_policy_refusal_is_logged_as_a_warning()
    {
        database.Require();
        using var logs = new KeptWarnings();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Warning));

        // Postgres refuses the new row.
        await using (var context = database.CreateSavingContext(Alice, factory))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 1, "Bob's, says Alice", PalletDepotDatabase.BobId));
            await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>();
        }

        var denied = logs.Of<DatabaseRefusalInterceptor>().Should().ContainSingle().Which;
        denied.Level.Should().Be(LogLevel.Warning);
        denied.Message.Should().Be("The database refused a save the application allowed: depot.Pallets. C# and the policies disagree.");
        denied.Exception.Should().BeOfType<DbUpdateException>();

        // The policy leaves the statement no row.
        logs.Clear();
        await using (var context = database.CreateSavingContext(Bob, factory))
        {
            var alices = await context.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            alices.Retitle("Bob was here");
            await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>();
        }

        var hidden = logs.Of<DatabaseRefusalInterceptor>().Should().ContainSingle().Which;
        hidden.Level.Should().Be(LogLevel.Warning);
        hidden.Message.Should().Be("The database refused a save the application allowed: depot.Pallets. C# and the policies disagree.");
        hidden.Exception.Should().BeOfType<ConcurrencyConflictException>();

        // A number that is taken is a rule both sides agree on, and a lost race is nobody's rule: neither is logged.
        logs.Clear();
        await using (var context = database.CreateSavingContext(Alice, factory))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.North, 2, "Alice's second", PalletDepotDatabase.AliceId));
            await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>();
        }

        await using (var first = database.CreateSavingContext(Alice, factory))
        await using (var second = database.CreateSavingContext(Alice, factory))
        {
            var early = await first.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            var late = await second.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            early.Retitle("Alice's, checked");
            await first.SaveChangesAsync(Cancellation);
            late.Retitle("Alice's, too late");
            await FluentActions.Awaiting(() => second.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>();
            early.Retitle("Alice's");
            await first.SaveChangesAsync(Cancellation);
        }

        logs.Of<DatabaseRefusalInterceptor>().Should().BeEmpty();
    }

    [Fact]
    public async Task What_postgres_refused_is_read_from_the_failure()
    {
        database.Require();

        await using (var context = database.CreateContext(Alice))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.North, 2, "Alice's second", PalletDepotDatabase.AliceId));
            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;

            var duplicate = DatabaseRefusal.From(failure)!;
            duplicate.Kind.Should().Be(DatabaseRefusalKind.DuplicateKey);
            duplicate.Constraint.Should().Be("IX_Pallets_Depot_Number");
            duplicate.Table.Should().Be("Pallets");
            duplicate.Schema.Should().Be(PalletContext.Schema);
            duplicate.Columns.Should().BeEmpty("Postgres names the index, not its columns");
        }

        await using (var context = database.CreateContext(Alice))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 1, "Bob's, says Alice", PalletDepotDatabase.BobId));
            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;

            var denied = DatabaseRefusal.From(failure)!;
            denied.Kind.Should().Be(DatabaseRefusalKind.PolicyDenied);
            denied.Table.Should().Be("Pallets");
            denied.Constraint.Should().BeNull();
            denied.Schema.Should().BeNull();
        }

        // Anything else Postgres refuses is not read as a refusal: a label too long for its column.
        await using (var context = database.CreateContext(Alice))
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 3, new string('x', 80), PalletDepotDatabase.AliceId));
            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;

            DatabaseRefusal.From(failure).Should().BeNull();
        }
    }

    [Fact]
    public async Task A_save_a_guard_refuses_is_access_refused_and_one_refused_without_the_hint_fails_as_it_did()
    {
        database.Require();
        using var logs = new KeptWarnings();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Warning));

        // The depot's own guard, written with the toolkit's statement: a signed-in user's pallet keeps its number.
        // The policy lets Alice change her pallet; the guard refuses the column.
        await database.RunAsOwnerAsync(DatabaseRefusalProbeTests.NumberStays(RowAccessModel.Refusal("pallets_number_stays", "A pallet keeps its number.")), Cancellation);
        try
        {
            await using (var context = database.CreateSavingContext(Alice, factory))
            {
                var hers = await context.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
                hers.Renumber(9);

                var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
                refusal.Code.Should().Be(ToolkitRefusals.Refused);
                refusal.Kind.Should().Be(RefusalKind.NotPermitted);
                refusal.Message.Should().Be("The database refused this change.");
                refusal.Arguments.Should().BeEmpty("the caller is told no more than that, and nothing of the guard");
                var failure = refusal.InnerException.Should().BeOfType<DbUpdateException>().Subject;
                failure.InnerException.Should().BeOfType<PostgresException>().Which.MessageText.Should().Be("A pallet keeps its number.");

                var read = DatabaseRefusal.From(failure)!;
                (read.Kind, read.Constraint).Should().Be((DatabaseRefusalKind.GuardRefused, "pallets_number_stays"));

                FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<RefusalException>().Which.Code.Should().Be(ToolkitRefusals.Refused);
            }

            var warned = logs.Of<DatabaseRefusalInterceptor>().Should().HaveCount(2).And.Subject.First();
            warned.Level.Should().Be(LogLevel.Warning);
            warned.Message.Should().Be("The database refused a save the application allowed: the guard pallets_number_stays on depot.Pallets. C# and the guards disagree.");
            warned.Exception.Should().BeOfType<DbUpdateException>();

            // The same trigger, raising without the hint: a 42501 the toolkit cannot tell from the application's own
            // set-up, so it fails as it always did, and nothing is logged as a refusal.
            logs.Clear();
            await database.RunAsOwnerAsync(DatabaseRefusalProbeTests.NumberStays("RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', MESSAGE = 'A pallet keeps its number.';"), Cancellation);
            await using (var context = database.CreateSavingContext(Alice, factory))
            {
                var hers = await context.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
                hers.Renumber(9);

                var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
                failure.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
                DatabaseRefusal.From(failure).Should().BeNull();
            }

            logs.Of<DatabaseRefusalInterceptor>().Should().BeEmpty();
        }
        finally
        {
            await database.RunAsOwnerAsync(DatabaseRefusalProbeTests.DropNumberStays, Cancellation);
        }

        (await database.ScalarAsOwnerAsync($"SELECT \"Number\" FROM {Pallets} WHERE \"Id\" = '{PalletDepotDatabase.AlicesPallet.Value}'", Cancellation)).Should().Be("1", "nothing was renumbered");
    }

    [Fact]
    public async Task A_42501_that_names_row_level_security_and_refused_no_row_is_not_read_as_a_refusal()
    {
        database.Require();

        // A read with row security turned off, by a role the policies hold: the words of a policy's refusal, from
        // a routine that is not the check of a row, about the application's own set-up.
        await using var context = database.CreateSavingContext(Alice);
        await context.Database.OpenConnectionAsync(Cancellation);
        await context.Database.ExecuteSqlRawAsync("SET row_security = off", Cancellation);

        var failure = (await FluentActions.Awaiting(() => context.Pallets.CountAsync(Cancellation)).Should().ThrowAsync<PostgresException>()).Which;
        failure.MessageText.Should().Contain("row-level security", "what the toolkit once read a refusal from");
        DatabaseRefusal.From(failure).Should().BeNull();
        DatabaseRefusal.From(new DbUpdateException("A save that failed so.", failure)).Should().BeNull();
    }

    /// <summary>Every logger of a context, keeping what it was told at warning level and above.</summary>
    private sealed class KeptWarnings : ILoggerProvider
    {
        private readonly List<(string Category, LogLevel Level, string Message, Exception? Exception)> _entries = [];

        public IReadOnlyList<(string Category, LogLevel Level, string Message, Exception? Exception)> Of<T>()
        {
            lock (_entries)
            {
                return [.. _entries.Where(entry => entry.Category == typeof(T).FullName)];
            }
        }

        public void Clear()
        {
            lock (_entries)
            {
                _entries.Clear();
            }
        }

        public ILogger CreateLogger(string categoryName) => new Keeper(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Keeper(KeptWarnings logs, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                lock (logs._entries)
                {
                    logs._entries.Add((category, logLevel, formatter(state, exception), exception));
                }
            }
        }
    }
}
