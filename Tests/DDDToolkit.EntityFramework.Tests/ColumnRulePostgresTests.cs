using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Column rules on a real Postgres: the help desk lets a teammate work on the team's tickets, and only a ticket's
/// owner, or the team's lead, change its status. The policy for UPDATE lets the teammate change the row; the column
/// rule's trigger is what refuses the status. What each caller may do is asked with SQL of its own, past any C#.
/// </summary>
/// <remarks>
/// A fixture of its own, because each test writes the rules it is about. Each one puts back what it changed.
/// </remarks>
public sealed class ColumnRulePostgresTests(PostgresRowAccessDatabase database) : IClassFixture<PostgresRowAccessDatabase>
{
    private const string AlicesTicket = "Alice's, open for the north";

    private const string StatusHeldByTheOwners = "The column rule 'Owners close their tickets' does not let this caller change \"Status\" of desk.\"Tickets\".";

    private static readonly Caller Alice = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Alice, team: "north"));

    private static readonly Caller Bob = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Bob, team: "north"));

    /// <summary>Bob, as the north team's lead.</summary>
    private static readonly Caller BobLeading = Callers.FromClaims(
        $$$"""{"sub":"{{{PostgresRowAccessDatabase.Bob}}}","role":"authenticated","app_metadata":{"team":"north","lead":"yes"}}""");

    /// <summary>What the desk's own work runs as when it stays inside the policies.</summary>
    private static readonly Caller DeskWork = Caller.SystemIn("desk");

    /// <summary>The desk's own work changes any ticket: a rule for the scoped system role.</summary>
    private static readonly RowAccessRule DeskWorkChangesTickets = RowAccessRule.For<Ticket>("The desks own work changes tickets", RowOperations.Read | RowOperations.Change, "TRUE", RowAccessRoles.SystemIn);

    /// <summary>A role of the host's own, named as the database spells it, that reads and changes any ticket.</summary>
    private static readonly RowAccessRule ClerksChangeTickets = RowAccessRule.For<Ticket>("Clerks change tickets", RowOperations.Read | RowOperations.Change, "TRUE", "desk_clerk");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_teammate_changes_a_tickets_title_and_not_its_status_which_its_owner_changes()
    {
        database.Require();
        await WriteRulesAsync(DeskRules.TeamWork, DeskRules.OwnersClose);
        try
        {
            // The policy for UPDATE lets Bob change the north team's ticket, and he renames it.
            (await database.RunAsAsync(Bob, """UPDATE desk."Tickets" SET "Title" = {0} WHERE "Title" = {1}""", Cancellation, AlicesTicket + ", retitled", AlicesTicket))
                .Should().Be(1, "the rules for the row let a teammate change it");

            // Its status is the owner's to change, whatever statement he sends.
            var closing = () => database.RunAsAsync(Bob, """UPDATE desk."Tickets" SET "Status" = 1 WHERE "Title" = {0}""", Cancellation, AlicesTicket + ", retitled");
            var refused = (await closing.Should().ThrowAsync<PostgresException>()).Which;
            (refused.SqlState, refused.MessageText, refused.ConstraintName).Should().Be((PostgresErrorCodes.InsufficientPrivilege, StatusHeldByTheOwners, "tickets_status_column_rule"));

            // Nor along with a column he may change: the statement is refused whole.
            var both = () => database.RunAsAsync(Bob, """UPDATE desk."Tickets" SET "Title" = 'Both', "Status" = 1 WHERE "Title" = {0}""", Cancellation, AlicesTicket + ", retitled");
            (await both.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(StatusHeldByTheOwners);

            // Alice owns it, and closes it.
            (await database.RunAsAsync(Alice, """UPDATE desk."Tickets" SET "Status" = 1 WHERE "Title" = {0}""", Cancellation, AlicesTicket + ", retitled"))
                .Should().Be(1, "the column rule allows the owner");
            (await StatusAndTitleAsync(AlicesTicket + ", retitled")).Should().Be((1, AlicesTicket + ", retitled"));
        }
        finally
        {
            await PutBackAsync();
        }
    }

    [Fact]
    public async Task Several_column_rules_on_the_status_add_up_and_the_teams_lead_closes_the_teams_ticket()
    {
        database.Require();
        await WriteRulesAsync(DeskRules.TeamWork, DeskRules.OwnersClose, DeskRules.LeadsClose);
        try
        {
            var asTeammate = () => database.RunAsAsync(Bob, """UPDATE desk."Tickets" SET "Status" = 1 WHERE "Title" = {0}""", Cancellation, AlicesTicket);
            (await asTeammate.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(
                "None of the column rules 'Leads close the teams tickets' and 'Owners close their tickets' lets this caller change \"Status\" of desk.\"Tickets\".");

            (await database.RunAsAsync(BobLeading, """UPDATE desk."Tickets" SET "Status" = 1 WHERE "Title" = {0}""", Cancellation, AlicesTicket))
                .Should().Be(1, "one of the column rules allows the team's lead");
        }
        finally
        {
            await PutBackAsync();
        }
    }

    [Fact]
    public async Task The_applications_own_work_is_not_held_unless_a_column_rule_names_its_role()
    {
        database.Require();
        await database.RunAsOwnerAsync("GRANT USAGE ON SCHEMA desk TO ddd_system_in; GRANT SELECT, UPDATE ON ALL TABLES IN SCHEMA desk TO ddd_system_in;", Cancellation);
        await WriteRulesAsync(DeskWorkChangesTickets, DeskRules.TeamWork, DeskRules.OwnersClose);
        try
        {
            // The scoped system role passes the column rule's trigger, and is held to its own policies alone.
            (await database.RunAsAsync(DeskWork, """UPDATE desk."Tickets" SET "Status" = 1 WHERE "Title" = {0}""", Cancellation, AlicesTicket))
                .Should().Be(1, "the application's own work has checked already");

            // The application as the tables' owner, which no policy holds either.
            (await database.RunAsAsync(Caller.System, """UPDATE desk."Tickets" SET "Status" = 0 WHERE "Title" = {0}""", Cancellation, AlicesTicket))
                .Should().Be(1);

            // A column rule that names the scoped system role holds it: the desk's own work has no user, so it is no owner.
            await WriteRulesAsync(
                DeskWorkChangesTickets,
                DeskRules.TeamWork,
                RowAccessRule.ForColumns<Ticket>("Owners close their tickets", [nameof(Ticket.Status)], OwnersCloseTheirTickets.RowAccessSql, RowAccessRoles.User, RowAccessRoles.Anonymous, RowAccessRoles.SystemIn));
            var held = () => database.RunAsAsync(DeskWork, """UPDATE desk."Tickets" SET "Status" = 1 WHERE "Title" = {0}""", Cancellation, AlicesTicket);
            (await held.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(StatusHeldByTheOwners);
        }
        finally
        {
            await PutBackAsync();
        }
    }

    [Fact]
    public async Task A_role_of_the_hosts_own_that_a_rule_lets_change_tickets_is_held_where_no_column_rule_is_for_it()
    {
        database.Require();
        await database.RunAsOwnerAsync(
            """
            DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'desk_clerk') THEN CREATE ROLE desk_clerk NOLOGIN; END IF; END $$;
            GRANT USAGE ON SCHEMA desk TO desk_clerk;
            GRANT SELECT, UPDATE ON ALL TABLES IN SCHEMA desk TO desk_clerk;
            """,
            Cancellation);
        await WriteRulesAsync(DeskRules.TeamWork, DeskRules.OwnersClose, ClerksChangeTickets);
        try
        {
            // The clerks' own rule lets the role change any ticket, and it retitles one.
            (await RunAsRoleAsync("desk_clerk", $"""UPDATE desk."Tickets" SET "Title" = 'Retitled by a clerk' WHERE "Title" = '{AlicesTicket.Replace("'", "''", StringComparison.Ordinal)}'"""))
                .Should().Be(1, "the rules for the row let the clerk change it");

            // Its status is held, and no column rule is for the role.
            var closing = () => RunAsRoleAsync("desk_clerk", """UPDATE desk."Tickets" SET "Status" = 1 WHERE "Title" = 'Retitled by a clerk'""");
            var refused = (await closing.Should().ThrowAsync<PostgresException>()).Which;
            (refused.SqlState, refused.MessageText).Should().Be((PostgresErrorCodes.InsufficientPrivilege, "No column rule is for this caller's role, so it may not change \"Status\" of desk.\"Tickets\"."));
        }
        finally
        {
            await PutBackAsync();
        }
    }

    [Fact]
    public async Task A_column_rule_taken_out_takes_its_trigger_and_its_function_along_with_the_next_script()
    {
        database.Require();
        await WriteRulesAsync(DeskRules.TeamWork, DeskRules.OwnersClose);
        (await ScalarAsync("SELECT count(*) FROM pg_catalog.pg_trigger WHERE tgname = 'tickets_status_column_rule'")).Should().Be(1);

        await WriteRulesAsync(DeskRules.TeamWork);
        try
        {
            (await ScalarAsync("SELECT count(*) FROM pg_catalog.pg_trigger WHERE tgname = 'tickets_status_column_rule'")).Should().Be(0, "the drop at the start of the script found it by its comment");
            (await ScalarAsync("SELECT count(*) FROM pg_catalog.pg_proc WHERE proname = 'tickets_status_column_rule'")).Should().Be(0, "a function the context no longer writes goes once nothing asks it");
            (await database.RunAsAsync(Bob, """UPDATE desk."Tickets" SET "Status" = 1 WHERE "Title" = {0}""", Cancellation, AlicesTicket))
                .Should().Be(1, "without the column rule, the rules for the row are all there is");
        }
        finally
        {
            await PutBackAsync();
        }
    }

    [Fact]
    public async Task The_drop_at_the_start_of_a_migration_is_what_lets_it_drop_a_column_a_column_rule_holds()
    {
        database.Require();
        await WriteRulesAsync(DeskRules.TeamWork, DeskRules.OwnersClose);
        await using var model = DeskContext.Create();

        // Inside a transaction that is rolled back, so the column is there for the other tests.
        var alone = () => database.RunAsOwnerAsync("""BEGIN; ALTER TABLE desk."Tickets" DROP COLUMN "Status"; ROLLBACK;""", Cancellation);
        (await alone.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.DependentObjectsStillExist, "the trigger of the column rule holds the column");

        var withTheDrop = () => database.RunAsOwnerAsync("BEGIN;\n" + PostgresRowAccess.DropStatement(model) + """ALTER TABLE desk."Tickets" DROP COLUMN "Status"; ROLLBACK;""", Cancellation);
        await withTheDrop.Should().NotThrowAsync();
    }

    /// <summary>The desk's own rules with <paramref name="more"/>, written as the database's policies and triggers.</summary>
    private async Task WriteRulesAsync(params RowAccessRule[] more)
    {
        await using var model = DeskContext.Create();
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, [.. PostgresRowAccessDatabase.Rules, .. more], [DeskRules.IsWatcher]), Cancellation);
    }

    /// <summary>The desk's own rules again, and Alice's ticket as the fixture made it.</summary>
    private async Task PutBackAsync()
    {
        await WriteRulesAsync();
        await database.RunAsOwnerAsync(
            $"""UPDATE desk."Tickets" SET "Title" = '{AlicesTicket.Replace("'", "''", StringComparison.Ordinal)}', "Status" = 0 WHERE "Owner" = '{PostgresRowAccessDatabase.Alice}';""",
            Cancellation);
    }

    private async Task<(int Status, string Title)> StatusAndTitleAsync(string title)
    {
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var command = new NpgsqlCommand("""SELECT "Status", "Title" FROM desk."Tickets" WHERE "Title" = $1""", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = title });
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        (await reader.ReadAsync(Cancellation)).Should().BeTrue();
        return (reader.GetInt32(0), reader.GetString(1));
    }

    /// <summary>Runs <paramref name="sql"/> as <paramref name="role"/>, switched to for one transaction, and returns the rows it changed.</summary>
    private async Task<int> RunAsRoleAsync(string role, string sql)
    {
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await using (var switching = new NpgsqlCommand($"SET LOCAL ROLE {role}", connection, transaction))
        {
            await switching.ExecuteNonQueryAsync(Cancellation);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        var changed = await command.ExecuteNonQueryAsync(Cancellation);
        await transaction.CommitAsync(Cancellation);
        return changed;
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Cancellation))!;
    }
}
