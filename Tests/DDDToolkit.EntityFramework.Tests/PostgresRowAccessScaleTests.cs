using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// A set-shaped question in a policy, on 5,000 tickets: Postgres asks the function once per statement, in an
/// InitPlan, before it reads the table, and then finds the rows through the key's index. And a set-shaped
/// function whose question is about the aggregate's entities alone reads their table, not the aggregate's.
/// </summary>
/// <remarks>
/// A fixture of its own, <c>desk_scale</c>, because a planner shows what it would do with many rows only when
/// there are many rows, and the small desk's tests count its five.
/// </remarks>
public sealed class PostgresRowAccessScaleTests(PostgresRowAccessScaleDatabase database) : IClassFixture<PostgresRowAccessScaleDatabase>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string EveryTicket = """SELECT "Id" FROM desk_scale."Tickets" """;

    [Fact]
    public async Task A_set_question_in_a_policy_is_an_init_plan_with_an_index_scan()
    {
        database.Require();
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await PostgresRowAccessScaleDatabase.BecomeAsync(connection, PostgresRowAccessScaleDatabase.Watcher, Cancellation);

        var plan = await QueryPlans.ExplainAsync(connection, EveryTicket, "VERBOSE", Cancellation);

        QueryPlans.InitPlansCalling(plan, "desk_scale.tickets_i_watch(").Should().NotBeEmpty(
            "the policy asks the set once, before the scan, rather than for every ticket");
        QueryPlans.Scans(plan, "Tickets").Should().ContainSingle().Which.Should().Match<QueryPlans.Node>(
            scan => QueryPlans.UsesAnIndex(scan) && scan.IndexCondition!.Contains("ANY", StringComparison.Ordinal),
            "the tickets are found through the key's index, by the set's ids");
        QueryPlans.Nodes(plan).Where(node => !node.InInitPlan && node.Filter is not null)
            .Should().NotContain(node => node.Filter!.Contains("tickets_i_watch", StringComparison.Ordinal), "no ticket asks the function itself");

        await using var count = new NpgsqlCommand("""SELECT count(*) FROM desk_scale."Tickets" """, connection);
        (await count.ExecuteScalarAsync(Cancellation)).Should().Be((long)PostgresRowAccessScaleDatabase.WatchedByWatcher, "the watcher reads the tickets they watch");
    }

    [Fact]
    public async Task A_set_function_in_a_policy_is_called_once_per_statement()
    {
        database.Require();
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await RunAsync(connection, "SET track_functions = 'all'");
        var before = await CallsAsync(connection);

        await using (var transaction = await connection.BeginTransactionAsync(Cancellation))
        {
            await PostgresRowAccessScaleDatabase.BecomeAsync(connection, PostgresRowAccessScaleDatabase.Watcher, Cancellation);
            await using var read = new NpgsqlCommand("""SELECT count(*) FROM desk_scale."Tickets" """, connection);
            (await read.ExecuteScalarAsync(Cancellation)).Should().Be((long)PostgresRowAccessScaleDatabase.WatchedByWatcher);
            await transaction.CommitAsync(Cancellation);
        }

        // The counts reach the shared statistics when the backend is next idle; a new transaction reads them.
        await RunAsync(connection, "SELECT pg_catalog.pg_stat_force_next_flush()");
        var after = await CallsAsync(connection);

        (after - before).Should().Be(1, $"one statement over {PostgresRowAccessScaleDatabase.TicketCount} tickets asks the set once");
    }

    // The probe: where a set-shaped function starts, when its question is about the aggregate's entities.

    [Fact]
    public async Task A_set_function_from_the_entity_table_reads_fewer_buffers_than_from_the_root()
    {
        database.Require();
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);

        // The same question, written from the tickets' table: the form the export writes when the question
        // asks something of the ticket itself as well.
        var fromTheRoot = RowAccessFunction.For<Ticket>(
            "tickets_i_watch_from_the_root", "(" + TicketsIWatch.RowAccessSql + " AND ({col:Id} IS NOT NULL))", "desk", shape: AccessFunctionShape.Set);
        await using (var model = DeskScaleContext.Create())
        {
            await RunAsync(connection, PostgresRowAccess.CreateStatements(model, [], [DeskRules.WatchedSet, fromTheRoot], new RowAccessExport()));
        }

        // No policy of this script asks either function, so it grants them to nobody, and takes back what an earlier
        // script granted; the probe asks them itself.
        await RunAsync(connection, "GRANT EXECUTE ON FUNCTION desk_scale.tickets_i_watch(), desk_scale.tickets_i_watch_from_the_root() TO authenticated");

        (await BodyAsync(connection, "tickets_i_watch")).Should().Contain("""FROM desk_scale."TicketWatcher" e1""").And.NotContain("\"Tickets\"");
        (await BodyAsync(connection, "tickets_i_watch_from_the_root")).Should().Contain("""FROM desk_scale."Tickets" root""");

        await PostgresRowAccessScaleDatabase.BecomeAsync(connection, PostgresRowAccessScaleDatabase.Watcher, Cancellation);
        var fromTheEntities = await BuffersAsync(connection, "desk_scale.tickets_i_watch()");
        var fromTheTickets = await BuffersAsync(connection, "desk_scale.tickets_i_watch_from_the_root()");

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Shared buffers, {PostgresRowAccessScaleDatabase.TicketCount} tickets: from the entity table {fromTheEntities.Buffers}, from the tickets' table {fromTheTickets.Buffers}.");
        fromTheEntities.Rows.Should().Be(PostgresRowAccessScaleDatabase.WatchedByWatcher).And.Be(fromTheTickets.Rows, "both answer the same question");
        fromTheEntities.Buffers.Should().BeLessThan(fromTheTickets.Buffers, "the entity table's form never reads the tickets");
    }

    /// <summary>
    /// How many rows <paramref name="function"/> answers with, and how many shared buffers asking it reads,
    /// the function's own statement included. Asked once first, so the second is not paying for the first
    /// plan's catalog lookups.
    /// </summary>
    private static async Task<(long Rows, long Buffers)> BuffersAsync(NpgsqlConnection connection, string function)
    {
        var query = $"SELECT count(*) FROM {function}";
        await using (var warm = new NpgsqlCommand(query, connection))
        {
            await warm.ExecuteScalarAsync(Cancellation);
        }

        var plan = await QueryPlans.ExplainAsync(connection, query, "ANALYZE, BUFFERS", Cancellation);
        await using var count = new NpgsqlCommand(query, connection);
        return ((long)(await count.ExecuteScalarAsync(Cancellation))!, plan.GetProperty("Shared Hit Blocks").GetInt64() + plan.GetProperty("Shared Read Blocks").GetInt64());
    }

    private static async Task<string> BodyAsync(NpgsqlConnection connection, string function)
    {
        await using var command = new NpgsqlCommand("SELECT prosrc FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'desk_scale' AND p.proname = $1", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = function });
        return (string)(await command.ExecuteScalarAsync(Cancellation))!;
    }

    private static async Task<long> CallsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT coalesce(sum(calls), 0)::bigint FROM pg_catalog.pg_stat_user_functions WHERE schemaname = 'desk_scale' AND funcname = 'tickets_i_watch'",
            connection);
        return (long)(await command.ExecuteScalarAsync(Cancellation))!;
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }
}
