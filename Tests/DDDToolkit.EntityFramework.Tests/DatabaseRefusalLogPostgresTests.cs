using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What a save the policies refused is logged as, where Postgres decides. A keeper of a depot may retitle and add
/// its pallets, in C# and in two policies of the depot that read the same table of keepers. A keeper whose key is
/// taken away between the request's check and the save is refused by the policies, and by the check asked again
/// too: his rights changed, and an information line says what happened. A keeper C# knows of and the policies do
/// not is a disagreement: the check asked again still lets him through, and that stays a warning. Both ways a
/// policy refuses a save, a statement that finds no row and a new row it rejects, log alike.
/// </summary>
[Collection(PalletDepotDatabase.Collection)]
public sealed class DatabaseRefusalLogPostgresTests(PalletDepotDatabase database) : IAsyncLifetime
{
    private const string Keepers = PalletContext.Schema + ".\"Keepers\"";

    private const string Pallets = PalletContext.Schema + ".\"Pallets\"";

    private const string IsKeeper = "EXISTS (SELECT 1 FROM " + Keepers + " k WHERE k.\"Depot\" = " + Pallets + ".\"Depot\" AND k.\"User\" = (SELECT ddd.caller_id()))";

    private const string RightsChangedLine =
        "The database refused a save to depot.Pallets after the access check of {0} (KeepsDepot) let the caller through. Asked again, the check refuses as well: "
        + "the caller's rights changed between the check and the save, and the caller is refused.";

    private const string DisagreementLine =
        "The database refused a save the application allowed: depot.Pallets. C# and the policies disagree: asked again, the access check of {0} (KeepsDepot) still lets the caller through.";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Caller Bob => PalletDepotDatabase.Bob;

    private static Guid BobId => PalletDepotDatabase.BobId;

    private static DepotId North => PalletDepotDatabase.North;

    /// <summary>The keepers and their two policies, beside the depot's own rules, which they widen for keepers only.</summary>
    public async ValueTask InitializeAsync()
    {
        if (!database.Available)
        {
            return;
        }

        await database.RunAsOwnerAsync(
            $"""
            CREATE TABLE {Keepers} ("Depot" integer NOT NULL, "User" uuid NOT NULL, PRIMARY KEY ("Depot", "User"));
            ALTER TABLE {Keepers} OWNER TO {PalletDepotDatabase.LoginRole};
            GRANT SELECT ON {Keepers} TO authenticated;
            CREATE POLICY "Keepers change the pallets of their depot" ON {Pallets} FOR UPDATE TO authenticated USING ({IsKeeper}) WITH CHECK ({IsKeeper});
            CREATE POLICY "Keepers add pallets to their depot" ON {Pallets} FOR INSERT TO authenticated WITH CHECK ({IsKeeper});
            """,
            Cancellation);
    }

    /// <summary>The depot as the other classes of the collection expect it: its own rules alone, and its pallets as they were seeded.</summary>
    public async ValueTask DisposeAsync()
    {
        if (!database.Available)
        {
            return;
        }

        await database.RunAsOwnerAsync(
            $"""
            DROP POLICY IF EXISTS "Keepers change the pallets of their depot" ON {Pallets};
            DROP POLICY IF EXISTS "Keepers add pallets to their depot" ON {Pallets};
            DROP TABLE IF EXISTS {Keepers};
            """,
            CancellationToken.None);
        await database.RestoreAsync();
    }

    [Fact]
    public async Task A_keeper_whose_key_is_taken_away_before_the_save_is_an_information_line_that_his_rights_changed()
    {
        database.Require();
        using var logs = new KeptLogLines();
        using var factory = logs.Factory();
        await KeepsAsync(BobId, North);
        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, new KeepersInTheDatabase(database))]);

        // A keeper retitles a pallet that is not his: the policies let him, as the check does.
        await KeeperCheck.SendAsync(checks, new RetitlePallet(PalletDepotDatabase.AlicesPallet, North, "Kept by Bob"), () => RetitleAsync(factory, "Kept by Bob", refused: false), Cancellation);
        (await LabelAsync()).Should().Be("Kept by Bob");

        // Then he is no keeper any more by the time his next save reaches the database.
        var command = new RetitlePallet(PalletDepotDatabase.AlicesPallet, North, "Bob was here");
        await KeeperCheck.SendAsync(checks, command, () => RetitleAsync(factory, command.Label, refused: true, meanwhile: () => StopsKeepingAsync(BobId, North)), Cancellation);

        var line = logs.Of<DatabaseRefusalInterceptor>().Should().ContainSingle("the change of rights is said once, and no warning with it").Which;
        line.Level.Should().Be(LogLevel.Information);
        line.Message.Should().Be(string.Format(RightsChangedLine, nameof(RetitlePallet)));
        line.Exception.Should().BeNull("an answer has no stack trace");
        (await LabelAsync()).Should().Be("Kept by Bob", "the refused save wrote nothing");
    }

    [Fact]
    public async Task A_keeper_only_csharp_knows_of_is_a_disagreement_and_that_stays_a_warning()
    {
        database.Require();
        using var logs = new KeptLogLines();
        using var factory = logs.Factory();

        // C# lists Bob as a keeper of the north depot, and the database never did: nothing changed in between.
        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, new KeepersInMemory().Keeps(BobId, North))]);
        var command = new RetitlePallet(PalletDepotDatabase.AlicesPallet, North, "Bob was here");
        await KeeperCheck.SendAsync(checks, command, () => RetitleAsync(factory, command.Label, refused: true), Cancellation);

        var line = logs.Of<DatabaseRefusalInterceptor>().Should().ContainSingle().Which;
        line.Level.Should().Be(LogLevel.Warning);
        line.Message.Should().Be(string.Format(DisagreementLine, nameof(RetitlePallet)));
        line.Exception.Should().BeOfType<ConcurrencyConflictException>("the statement found no row, and a warning carries it");
    }

    [Fact]
    public async Task A_new_row_refused_after_the_key_was_taken_away_is_logged_as_a_change_of_rights()
    {
        database.Require();
        using var logs = new KeptLogLines();
        using var factory = logs.Factory();
        var before = await database.CountAsync(Cancellation);
        await KeepsAsync(BobId, North);
        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, new KeepersInTheDatabase(database))]);

        var command = new AddPallet(North, 30, "Delivered for Alice", PalletDepotDatabase.AliceId);
        await KeeperCheck.SendAsync(checks, command, () => AddAsync(factory, command, meanwhile: () => StopsKeepingAsync(BobId, North)), Cancellation);

        var line = logs.Of<DatabaseRefusalInterceptor>().Should().ContainSingle().Which;
        line.Level.Should().Be(LogLevel.Information);
        line.Message.Should().Be(string.Format(RightsChangedLine, nameof(AddPallet)));
        line.Exception.Should().BeNull("an answer has no stack trace");
        (await database.CountAsync(Cancellation)).Should().Be(before);
    }

    [Fact]
    public async Task A_new_row_refused_that_the_check_still_allows_is_a_warning()
    {
        database.Require();
        using var logs = new KeptLogLines();
        using var factory = logs.Factory();

        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, new KeepersInMemory().Keeps(BobId, North))]);
        var command = new AddPallet(North, 30, "Delivered for Alice", PalletDepotDatabase.AliceId);
        await KeeperCheck.SendAsync(checks, command, () => AddAsync(factory, command), Cancellation);

        var line = logs.Of<DatabaseRefusalInterceptor>().Should().ContainSingle().Which;
        line.Level.Should().Be(LogLevel.Warning);
        line.Message.Should().Be(string.Format(DisagreementLine, nameof(AddPallet)));
        line.Exception.Should().BeOfType<DbUpdateException>("Postgres refused the new row")
            .Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_check_that_reads_over_the_handlers_own_transaction_is_asked_again_after_the_refused_statement()
    {
        database.Require();
        using var logs = new KeptLogLines();
        using var factory = logs.Factory();
        await KeepsAsync(BobId, North);

        // A unit of work: one transaction, and the check reads the keepers over the very context that saves. The
        // refused insert fails its statement; Entity Framework goes back to its savepoint, and the check is asked
        // again on that connection, inside that transaction, which sees what the other connection committed.
        await using var context = database.CreateSavingContext(Bob, factory);
        await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, new KeepersInTheDatabase(database, over: context))]);
        var command = new AddPallet(North, 30, "Delivered for Alice", PalletDepotDatabase.AliceId);

        await KeeperCheck.SendAsync(checks, command, async () =>
        {
            context.Pallets.Add(new Pallet(PalletId.CreateSequential(), command.Depot, command.Number, command.Label, command.Owner));
            await StopsKeepingAsync(BobId, North);
            (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>())
                .Which.Code.Should().Be(ToolkitRefusals.Refused);
        }, Cancellation);

        var line = logs.Of<DatabaseRefusalInterceptor>().Should().ContainSingle().Which;
        line.Level.Should().Be(LogLevel.Information);
        line.Message.Should().Be(string.Format(RightsChangedLine, nameof(AddPallet)));
        await transaction.RollbackAsync(Cancellation);
    }

    /// <summary>The handler of <see cref="RetitlePallet"/>, as Bob, with whatever happens between its load and its save.</summary>
    private async Task RetitleAsync(ILoggerFactory factory, string label, bool refused, Func<Task>? meanwhile = null)
    {
        await using var context = database.CreateSavingContext(Bob, factory);
        var pallet = await context.Pallets.SingleAsync(row => row.Id == PalletDepotDatabase.AlicesPallet, Cancellation);
        pallet.Retitle(label);
        if (meanwhile is not null)
        {
            await meanwhile();
        }

        var save = () => context.SaveChangesAsync(Cancellation);
        if (!refused)
        {
            await save();
            return;
        }

        (await save.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(ToolkitRefusals.Refused);
    }

    /// <summary>The handler of <see cref="AddPallet"/>, as Bob, whose save is refused.</summary>
    private async Task AddAsync(ILoggerFactory factory, AddPallet command, Func<Task>? meanwhile = null)
    {
        await using var context = database.CreateSavingContext(Bob, factory);
        context.Pallets.Add(new Pallet(PalletId.CreateSequential(), command.Depot, command.Number, command.Label, command.Owner));
        if (meanwhile is not null)
        {
            await meanwhile();
        }

        (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>())
            .Which.Code.Should().Be(ToolkitRefusals.Refused);
    }

    private Task KeepsAsync(Guid user, DepotId depot)
        => database.RunAsOwnerAsync($"INSERT INTO {Keepers} VALUES ({depot.Value}, '{user}')", Cancellation);

    private Task StopsKeepingAsync(Guid user, DepotId depot)
        => database.RunAsOwnerAsync($"DELETE FROM {Keepers} WHERE \"Depot\" = {depot.Value} AND \"User\" = '{user}'", Cancellation);

    private Task<string?> LabelAsync()
        => database.ScalarAsOwnerAsync($"SELECT \"Label\" FROM {Pallets} WHERE \"Id\" = '{PalletDepotDatabase.AlicesPallet.Value}'", Cancellation);

    /// <summary>
    /// The keepers as the database holds them: read as its owner, or, given a context, over that context's own
    /// connection as its caller, as a check reads that runs on the scope's own context.
    /// </summary>
    private sealed class KeepersInTheDatabase(PalletDepotDatabase database, PalletContext? over = null) : IDepotKeepers
    {
        public async Task<bool> KeepsAsync(Guid user, DepotId depot, CancellationToken cancellationToken)
        {
            if (over is null)
            {
                return await database.ScalarAsOwnerAsync($"SELECT count(*) FROM {Keepers} WHERE \"Depot\" = {depot.Value} AND \"User\" = '{user}'", cancellationToken) != "0";
            }

            var counted = await over.Database
                .SqlQueryRaw<int>($"SELECT count(*)::integer AS \"Value\" FROM {Keepers} WHERE \"Depot\" = {{0}} AND \"User\" = {{1}}", depot.Value, user)
                .SingleAsync(cancellationToken);
            return counted > 0;
        }
    }
}
