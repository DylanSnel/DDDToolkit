using DDDToolkit.Abstractions.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The probes: what Postgres and SQLite say, through Entity Framework and nothing of the toolkit's, when a save
/// breaks a policy or a unique index. What the toolkit makes of a failed save is read from exactly these facts,
/// so they are pinned here, against the providers themselves.
/// </summary>
[Collection(PalletDepotDatabase.Collection)]
public sealed class DatabaseRefusalProbeTests(PalletDepotDatabase database) : IAsyncLifetime
{
    /// <summary>The pallets' table, as SQL names it.</summary>
    private const string Pallets = PalletContext.Schema + ".\"Pallets\"";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Caller Alice => PalletDepotDatabase.Alice;

    private static Caller Bob => PalletDepotDatabase.Bob;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>The depot as it was seeded, for the next test of any class of the collection.</summary>
    public async ValueTask DisposeAsync() => await database.RestoreAsync();

    [Fact]
    public async Task A_policy_denied_insert_is_42501_naming_row_level_security_on_npgsql()
    {
        database.Require();

        // A pallet in somebody else's name: the policy's WITH CHECK refuses the new row.
        await using var inserting = database.CreateContext(Alice);
        inserting.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 1, "Bob's, says Alice", PalletDepotDatabase.BobId));

        var insert = (await FluentActions.Awaiting(() => inserting.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
        var refused = insert.InnerException.Should().BeOfType<PostgresException>().Subject;
        refused.SqlState.Should().Be("42501");
        refused.MessageText.Should().Be("new row violates row-level security policy for table \"Pallets\"");
        refused.TableName.Should().BeNull("Postgres names the table in the message alone");
        refused.Routine.Should().Be("ExecWithCheckOptions", "the check of a new row against the policies, named in any language");
        refused.ConstraintName.Should().BeNull();
        insert.Entries.Should().ContainSingle().Which.Entity.Should().BeOfType<Pallet>();

        // Her own pallet, handed to Bob: the row she may change becomes one she may not have written.
        await using var updating = database.CreateContext(Alice);
        var hers = await updating.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
        hers.HandTo(PalletDepotDatabase.BobId);

        var update = (await FluentActions.Awaiting(() => updating.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
        update.Should().NotBeOfType<DbUpdateConcurrencyException>("the statement failed; it did not miss its row");
        var check = update.InnerException.Should().BeOfType<PostgresException>().Subject;
        check.SqlState.Should().Be("42501");
        check.MessageText.Should().Be("new row violates row-level security policy for table \"Pallets\"");

        // A missing privilege is 42501 as well, and says something else.
        await database.RunAsOwnerAsync("REVOKE INSERT ON " + Pallets + " FROM authenticated", Cancellation);
        try
        {
            await using var ungranted = database.CreateContext(Alice);
            ungranted.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 2, "Alice's second", PalletDepotDatabase.AliceId));

            var failure = (await FluentActions.Awaiting(() => ungranted.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
            var privilege = failure.InnerException.Should().BeOfType<PostgresException>().Subject;
            privilege.SqlState.Should().Be("42501");
            privilege.MessageText.Should().Be("permission denied for table Pallets").And.NotContain("row-level security");
            privilege.Routine.Should().Be("aclcheck_error");
        }
        finally
        {
            await database.RunAsOwnerAsync("GRANT INSERT ON " + Pallets + " TO authenticated", Cancellation);
        }
    }

    [Fact]
    public async Task A_policy_denied_update_affects_no_row_while_the_row_stays_visible()
    {
        database.Require();

        // Bob reads Alice's pallet and may not change it: the policy's USING leaves his statement no row, and
        // Postgres reports no error.
        (await database.RunAsAsync(Bob, "UPDATE " + Pallets + " SET \"Label\" = 'Bob was here' WHERE \"Id\" = {0}", Cancellation, PalletDepotDatabase.AlicesPallet.Value))
            .Should().Be(0);
        (await database.RunAsAsync(Bob, "DELETE FROM " + Pallets + " WHERE \"Id\" = {0}", Cancellation, PalletDepotDatabase.AlicesPallet.Value))
            .Should().Be(0);

        // Through a save, that is the failure of a lost race: no row, and nothing that says why.
        await using var context = database.CreateContext(Bob);
        var alices = await context.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
        var loaded = alices.Version;
        alices.Retitle("Bob was here");

        var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateConcurrencyException>()).Which;
        failure.InnerException.Should().BeNull();
        failure.Entries.Should().ContainSingle().Which.Entity.Should().BeSameAs(alices);

        // What tells it from one: the same caller still reads the row, as it was.
        await using var again = database.CreateContext(Bob);
        var seen = await again.Pallets.AsNoTracking().SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
        seen.Label.Should().Be("Alice's");
        seen.Version.Should().Be(loaded);
    }

    [Fact]
    public async Task A_unique_violation_names_its_index_on_npgsql_and_its_columns_on_sqlite()
    {
        database.Require();

        // Npgsql: the index by name, with its table and schema, whichever pallet of the save broke it.
        await using (var context = database.CreateContext(Alice))
        {
            var free = new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 7, "A free number", PalletDepotDatabase.AliceId);
            var taken = new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.North, 2, "Bob's number", PalletDepotDatabase.AliceId);
            context.Pallets.AddRange(free, taken);

            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
            var unique = failure.InnerException.Should().BeOfType<PostgresException>().Subject;
            unique.SqlState.Should().Be("23505");
            unique.ConstraintName.Should().Be("IX_Pallets_Depot_Number");
            unique.TableName.Should().Be("Pallets");
            unique.SchemaName.Should().Be(PalletContext.Schema);
            failure.Entries.Select(entry => entry.Entity).Should().BeEquivalentTo([free, taken], "Npgsql sends the save as one batch, and Entity Framework names every row of it");
        }

        // SQLite: the table and the columns, and no name.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(Cancellation);
        await using (var context = new PalletContext(new DbContextOptionsBuilder<PalletContext>().UseSqlite(connection).Options))
        {
            await context.Database.EnsureCreatedAsync(Cancellation);
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.North, 2, "The first", owner: null));
            await context.SaveChangesAsync(Cancellation);

            var free = new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.South, 7, "A free number", owner: null);
            var taken = new Pallet(PalletId.CreateSequential(), PalletDepotDatabase.North, 2, "The second", owner: null);
            context.Pallets.AddRange(free, taken);

            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
            var unique = failure.InnerException.Should().BeOfType<SqliteException>().Subject;
            unique.SqliteErrorCode.Should().Be(19);
            unique.SqliteExtendedErrorCode.Should().Be(2067);
            unique.Message.Should().Be("SQLite Error 19: 'UNIQUE constraint failed: Pallets.Depot, Pallets.Number'.");
            failure.Entries.Select(entry => entry.Entity).Should().BeEquivalentTo([taken], "SQLite runs a save row by row, and Entity Framework names the one that failed");
        }
    }

    [Fact]
    public async Task A_trigger_raises_42501_from_the_routine_of_every_raise_and_says_what_it_was_told_on_npgsql()
    {
        database.Require();

        // A guard of the depot's own, written as the toolkit writes its guards: a signed-in user's pallet keeps its number.
        await database.RunAsOwnerAsync(NumberStays(RowAccessModel.Refusal("pallets_number_stays", "A pallet keeps its number.")), Cancellation);
        try
        {
            await using var context = database.CreateContext(Alice);
            var hers = await context.Pallets.SingleAsync(pallet => pallet.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
            hers.Renumber(9);

            var failure = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which;
            failure.Should().NotBeOfType<DbUpdateConcurrencyException>("the statement failed; it did not miss its row");
            var refused = failure.InnerException.Should().BeOfType<PostgresException>().Subject;
            refused.SqlState.Should().Be("42501");
            refused.MessageText.Should().Be("A pallet keeps its number.");
            refused.Hint.Should().Be("ddd:access.refused", "a RAISE hands over the hint it was given, untranslated");
            refused.ConstraintName.Should().Be("pallets_number_stays");
            refused.TableName.Should().BeNull("a RAISE names a table only where it is told to");
            refused.Routine.Should().Be("exec_stmt_raise", "every RAISE of PL/pgSQL comes from this one routine, so where it came from tells no guard apart");
            failure.Entries.Should().ContainSingle().Which.Entity.Should().BeSameAs(hers);

            // Without the hint, the same statement says nothing that is not said by any other RAISE.
            await database.RunAsOwnerAsync(NumberStays("RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', MESSAGE = 'A pallet keeps its number.';"), Cancellation);
            var unmarked = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>()).Which
                .InnerException.Should().BeOfType<PostgresException>().Subject;
            (unmarked.SqlState, unmarked.Hint, unmarked.ConstraintName, unmarked.Routine).Should().Be(("42501", null, null, "exec_stmt_raise"));
        }
        finally
        {
            await database.RunAsOwnerAsync(DropNumberStays, Cancellation);
        }
    }

    [Fact]
    public async Task A_read_with_row_security_off_is_42501_naming_row_level_security_from_another_routine_on_npgsql()
    {
        database.Require();

        // A role the policies hold that turns row security off, as a dump tool does: Postgres refuses the read, and
        // the message names row level security, though no policy refused a row.
        await using var context = database.CreateContext(Alice);
        await context.Database.OpenConnectionAsync(Cancellation);
        await context.Database.ExecuteSqlRawAsync("SET row_security = off", Cancellation);

        var refused = (await FluentActions.Awaiting(() => context.Pallets.CountAsync(Cancellation)).Should().ThrowAsync<PostgresException>()).Which;
        refused.SqlState.Should().Be("42501");
        refused.MessageText.Should().Be("query would be affected by row-level security policy for table \"Pallets\"");
        refused.Routine.Should().Be("check_enable_rls", "not the check of a new row against the policies");
        refused.Hint.Should().BeNull();
    }

    /// <summary>
    /// The depot's trigger that keeps a signed-in user's pallet at its number, refusing with <paramref name="raise"/>. The
    /// application's own work, which restores the depot, is not held.
    /// </summary>
    internal static string NumberStays(string raise) => $$"""
        CREATE OR REPLACE FUNCTION {{PalletContext.Schema}}.pallets_number_stays() RETURNS trigger LANGUAGE plpgsql SET search_path = '' AS $body$
        BEGIN
            IF CURRENT_USER = 'authenticated' THEN
                {{raise}}
            END IF;
            RETURN NEW;
        END
        $body$;
        DROP TRIGGER IF EXISTS pallets_number_stays ON {{Pallets}};
        CREATE TRIGGER pallets_number_stays BEFORE UPDATE OF "Number" ON {{Pallets}}
            FOR EACH ROW WHEN (OLD."Number" IS DISTINCT FROM NEW."Number") EXECUTE FUNCTION {{PalletContext.Schema}}.pallets_number_stays();
        """;

    /// <summary>Takes the trigger of <see cref="NumberStays"/> away again.</summary>
    internal static string DropNumberStays
        => $"DROP TRIGGER IF EXISTS pallets_number_stays ON {Pallets}; DROP FUNCTION IF EXISTS {PalletContext.Schema}.pallets_number_stays()";
}
