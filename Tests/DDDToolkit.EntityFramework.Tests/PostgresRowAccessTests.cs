using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The help desk's rules as policies on a Postgres that is not Supabase's: no <c>auth</c> schema, no
/// PostgREST roles until <see cref="PostgresRowAccess.SetupScript"/> makes them, and the policies asking
/// <c>ddd.caller_id()</c>. What the database lets each caller see is what the rules say in C#.
/// </summary>
/// <remarks>
/// The tests that put policies of their own on the desk, the probes of how Postgres plans a set-shaped
/// question among them, do so in a transaction they roll back, so the others read the desk as it is.
/// </remarks>
public sealed class PostgresRowAccessTests(PostgresRowAccessDatabase database) : IClassFixture<PostgresRowAccessDatabase>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly Caller Alice = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Alice, team: "north"));

    private static readonly Caller Bob = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Bob, team: "north"));

    private static readonly Caller Carol = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Carol, team: "south"));

    public static TheoryData<string> CallerNames => ["Alice", "Bob", "Carol", "anonymous"];

    private static Caller Named(string name) => name switch
    {
        "Alice" => Alice,
        "Bob" => Bob,
        "Carol" => Carol,
        _ => Caller.Anonymous,
    };

    [Theory]
    [MemberData(nameof(CallerNames))]
    public async Task The_database_lets_each_caller_read_what_the_rules_say_in_csharp(string name)
    {
        database.Require();
        var caller = Named(name);

        var all = await database.TicketsAsync(Caller.System, Cancellation);
        var seen = await database.TicketsAsync(caller, Cancellation);

        all.Should().HaveCount(PostgresRowAccessDatabase.TicketCount, "the application itself is not subject to the rules");
        seen.Select(ticket => ticket.Title).Should().BeEquivalentTo(
            all.Where(ticket => DeskRules.Reads(ticket, caller)).Select(ticket => ticket.Title),
            "Postgres answers what OwnersHaveTheirTickets, TeammatesReadOpenTickets and PublicTicketsAreEveryones answer");
    }

    [Fact]
    public async Task A_teammate_reads_an_open_ticket_of_the_team_and_nothing_else_of_it()
    {
        database.Require();

        var seen = await database.TicketsAsync(Bob, Cancellation);

        seen.Select(ticket => ticket.Title).Should().BeEquivalentTo(["Bob's own, closed", "Alice's, open for the north", "Everyone's"]);
    }

    [Fact]
    public async Task Somebody_who_has_not_signed_in_reads_the_public_tickets_only()
    {
        database.Require();

        var seen = await database.TicketsAsync(Caller.Anonymous, Cancellation);

        seen.Should().ContainSingle().Which.Title.Should().Be("Everyone's");
    }

    [Fact]
    public async Task A_user_made_in_code_without_a_token_is_who_the_database_sees()
    {
        database.Require();

        var seen = await database.TicketsAsync(Caller.User(PostgresRowAccessDatabase.Carol), Cancellation);

        seen.Select(ticket => ticket.Title).Should().BeEquivalentTo(["Carol's own", "Everyone's", "Nobody's"], "the interceptor gives the database her id and role as the claims");
    }

    [Fact]
    public async Task A_watcher_reads_the_ticket_through_the_access_function_that_reads_the_tickets_entities()
    {
        database.Require();

        var carols = await database.TicketsAsync(Carol, Cancellation);
        var bobs = await database.TicketsAsync(Bob, Cancellation);

        carols.Select(ticket => ticket.Title).Should().Contain("Nobody's", "Carol watches it");
        bobs.Select(ticket => ticket.Title).Should().NotContain("Nobody's", "nobody else does");
        (await database.FunctionsAsync("is_watcher", Cancellation)).Should().ContainSingle()
            .Which.Should().Be(("desk.is_watcher(uuid)", true), "it is made once, and runs as its owner, so the watchers' table answers without asking the tickets' policies back");
    }

    [Fact]
    public async Task A_question_about_the_entities_of_an_entity_is_answered_from_their_tables()
    {
        database.Require();

        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await using (var model = DeskContext.Create())
        {
            await RunAsync(connection, PostgresRowAccess.CreateStatements(model, [], [DeskRules.ReactedSet, DeskRules.ReactedTo], new RowAccessExport()));
        }

        // No rule asks them here, so nobody was granted them: a caller asks them directly. Bob reacted to the
        // comment on Alice's ticket and to the one on Carol's; Carol to the one on Bob's.
        await RunAsync(connection,
            $$"""
            GRANT EXECUTE ON FUNCTION desk.tickets_i_reacted_in(), desk.reacted_to_comment(uuid, text) TO authenticated;
            INSERT INTO desk."CommentReaction" ("TicketCommentTicketId", "TicketCommentId", "Id", "User")
            SELECT c."TicketId", c."Id", gen_random_uuid(), '{{PostgresRowAccessDatabase.Bob}}' FROM desk."TicketComment" c WHERE c."Text" IN ('On Alice''s', 'On Carol''s own');
            INSERT INTO desk."CommentReaction" ("TicketCommentTicketId", "TicketCommentId", "Id", "User")
            SELECT c."TicketId", c."Id", gen_random_uuid(), '{{PostgresRowAccessDatabase.Carol}}' FROM desk."TicketComment" c WHERE c."Text" = 'On Bob''s';
            """);
        var titles = new Dictionary<Guid, string>();
        await using (var all = new NpgsqlCommand("""SELECT "Id", "Title" FROM desk."Tickets" """, connection))
        await using (var reader = await all.ExecuteReaderAsync(Cancellation))
        {
            while (await reader.ReadAsync(Cancellation))
            {
                titles[reader.GetGuid(0)] = reader.GetString(1);
            }
        }

        await RunAsync(connection, $"SET LOCAL ROLE authenticated; SELECT set_config('request.jwt.claims', '{PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Bob, "north")}', true);");

        // The set: the comments' table gives each ticket once, and each comment is asked for its own reactions.
        var reactedIn = new List<string>();
        await using (var set = new NpgsqlCommand("SELECT desk.tickets_i_reacted_in()", connection))
        await using (var reader = await set.ExecuteReaderAsync(Cancellation))
        {
            while (await reader.ReadAsync(Cancellation))
            {
                reactedIn.Add(titles[reader.GetGuid(0)]);
            }
        }

        reactedIn.Should().BeEquivalentTo(["Alice's, open for the north", "Carol's own"], "Bob reacted to a comment on each, and Carol's reaction on his own ticket is not his");

        // One row at a time, with the function's own parameter read next to the comment.
        async Task<bool> ReactedToAsync(string title, string text)
        {
            await using var asked = new NpgsqlCommand("SELECT desk.reacted_to_comment($1, $2)", connection);
            asked.Parameters.Add(new NpgsqlParameter { Value = titles.Single(ticket => ticket.Value == title).Key });
            asked.Parameters.Add(new NpgsqlParameter { Value = text });
            return (bool)(await asked.ExecuteScalarAsync(Cancellation))!;
        }

        (await ReactedToAsync("Carol's own", "On Carol's own")).Should().BeTrue("he reacted to that comment");
        (await ReactedToAsync("Carol's own", "On Alice's")).Should().BeFalse("the ticket has no comment with that text");
        (await ReactedToAsync("Bob's own, closed", "On Bob's")).Should().BeFalse("the reaction to that comment is Carol's");
        (await ReactedToAsync("Everyone's", "anything")).Should().BeTrue("a public ticket needs no reaction");
    }

    [Fact]
    public async Task The_comments_follow_their_ticket()
    {
        database.Require();

        var comments = await database.CommentsAsync(Carol, Cancellation);

        comments.Should().BeEquivalentTo(["On Carol's own", "On everyone's", "On nobody's"], "Carol sees her own ticket, the public one and the one she watches, and the comments of nothing else");
    }

    [Fact]
    public async Task A_ticket_written_in_somebody_elses_name_is_refused()
    {
        database.Require();

        await using var context = database.CreateContext(Alice);
        context.Tickets.Add(new Ticket(TicketId.CreateSequential(), "Bob's, says Alice", PostgresRowAccessDatabase.Bob, team: null, TicketStatus.Open, isPublic: false));

        var save = () => context.SaveChangesAsync(Cancellation);

        (await save.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "new row violates row-level security policy");
    }

    [Fact]
    public async Task The_setup_can_run_again()
    {
        database.Require();

        var again = () => database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(loginRole: PostgresRowAccessDatabase.LoginRole), Cancellation);

        await again.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Comparing_a_nullable_column_with_caller_seat_never_matches_a_null()
    {
        database.Require();

        // Anonymous has no seat, so desk.caller_seat() is null for it; two of the tickets have no owner.
        (await SeenUnderAsync(DeskRules.OfTheCallersSeat, Caller.Anonymous)).Should().BeEmpty(
            "a ticket without an owner is not the seat of a caller without one: a null matches no null");
        (await SeenUnderAsync(DeskRules.OfAnotherSeat, Caller.Anonymous)).Should().BeEmpty(
            "an owner is not 'another seat' than a seat nobody knows");
        (await SeenUnderAsync(DeskRules.NotOfTheCallersSeat, Caller.Anonymous)).Should().BeEmpty(
            "whether a ticket is not of a seat nobody knows is not known either, and NOT keeps it unknown");

        (await SeenUnderAsync(DeskRules.OfTheCallersSeat, Alice)).Should().BeEquivalentTo(["Alice's, open for the north"]);
        (await SeenUnderAsync(DeskRules.OfAnotherSeat, Alice)).Should().BeEquivalentTo(
            ["Bob's own, closed", "Carol's own"], "the tickets of the other seats, and none of those without an owner");
        (await SeenUnderAsync(DeskRules.NotOfTheCallersSeat, Alice)).Should().BeEquivalentTo(
            ["Bob's own, closed", "Carol's own"], "not hers is another seat's, and a ticket without an owner is neither");
    }

    [Fact]
    public async Task A_rule_that_is_one_scalar_question_is_written_as_a_condition_postgres_reads_and_holds_every_command()
    {
        database.Require();

        // The rule's whole body is the question, (SELECT desk.caller_on_duty()): those parentheses are the
        // subquery's own, and a policy wants a pair of its own around its condition as well.
        string script;
        await using (var model = DeskContext.Create())
        {
            script = PostgresRowAccess.Script(model, [DeskRules.OnDuty]);
        }

        script.Should().Contain("\n    USING ((SELECT desk.caller_on_duty()))").And.Contain("\n    WITH CHECK ((SELECT desk.caller_on_duty()))")
            .And.NotContain("USING (SELECT").And.NotContain("CHECK (SELECT");

        // On Postgres, with the rule as the tickets' only policy: Alice is on duty, and reads, files, changes and
        // removes tickets; Bob is not, and does none of it; nor does a caller the desk does not know.
        (await OnDutyAsync(Alice, script)).Should().Be(((long)PostgresRowAccessDatabase.TicketCount, true, PostgresRowAccessDatabase.TicketCount, PostgresRowAccessDatabase.TicketCount));
        (await OnDutyAsync(Bob, script)).Should().Be((0L, false, 0, 0));
        (await OnDutyAsync(Caller.Anonymous, script)).Should().Be((0L, false, 0, 0));

        // The same rule written by hand with a comment at its end, which runs to the end of its line: the pair the
        // policy wants is closed on the next, and the access file is one Postgres reads.
        string commented;
        await using (var model = DeskContext.Create())
        {
            commented = PostgresRowAccess.Script(model, [RowAccessRule.For<Ticket>(DeskRules.OnDuty.Name, DDDToolkit.Abstractions.Attributes.RowOperations.All, OnDutyHandleEveryTicket.RowAccessSql + " -- whoever is on duty")]);
        }

        commented.Should().Contain("\n    USING ((SELECT desk.caller_on_duty()) -- whoever is on duty\n)\n    WITH CHECK ((SELECT desk.caller_on_duty()) -- whoever is on duty\n);");
        (await OnDutyAsync(Alice, commented)).Should().Be(((long)PostgresRowAccessDatabase.TicketCount, true, PostgresRowAccessDatabase.TicketCount, PostgresRowAccessDatabase.TicketCount));
        (await OnDutyAsync(Bob, commented)).Should().Be((0L, false, 0, 0));
    }

    // The probes: how Postgres plans a set-shaped question in a policy, which is why the generator writes
    // "column = ANY (ARRAY(SELECT f()))". The desk is small, so the planner is told to avoid a sequential scan
    // where it can: the first form then looks the set's ids up in the key's index, and the second still checks
    // every ticket against a hashed SubPlan, reading the whole index where it cannot read the table.

    [Fact]
    public async Task Any_of_an_array_of_a_set_function_is_an_init_plan_with_an_index_scan()
    {
        database.Require();

        var plan = await PlanOfAPolicyAsync("\"Id\" = ANY (ARRAY(SELECT desk.probe_owned_ticket_ids()))");

        QueryPlans.InitPlansCalling(plan, "desk.probe_owned_ticket_ids(").Should().NotBeEmpty("the set is asked once, before the tickets are read");
        QueryPlans.Scans(plan, "Tickets").Should().ContainSingle().Which.Should().Match<QueryPlans.Node>(
            scan => QueryPlans.UsesAnIndex(scan) && scan.IndexCondition!.Contains("ANY", StringComparison.Ordinal),
            "the set's ids are looked up in the key's index");
        QueryPlans.Nodes(plan).Where(node => !node.InInitPlan && node.Filter is not null)
            .Should().NotContain(node => node.Filter!.Contains("probe_owned_ticket_ids", StringComparison.Ordinal), "no ticket asks the function");
    }

    [Fact]
    public async Task In_select_of_a_set_function_is_a_hashed_subplan()
    {
        database.Require();

        var plan = await PlanOfAPolicyAsync("\"Id\" IN (SELECT desk.probe_owned_ticket_ids())");

        QueryPlans.InitPlansCalling(plan, "desk.probe_owned_ticket_ids(").Should().BeEmpty();
        QueryPlans.Scans(plan, "Tickets").Should().ContainSingle().Which.Filter.Should().Contain(
            "hashed SubPlan", "the policy stays a filter every ticket is checked against");
        QueryPlans.Nodes(plan).Where(node => !node.InInitPlan).Should().NotContain(
            node => node.IndexCondition != null, "no index can answer it: kept off a sequential scan, Postgres reads the whole index instead");
        QueryPlans.Nodes(plan).Should().Contain(node => node.ParentRelationship == "SubPlan");
    }

    /// <summary>
    /// The plan of reading every ticket as Alice, with the rules' policies taken off and one policy asking a
    /// set of her tickets' ids by <paramref name="condition"/> put on, in a transaction that is rolled back.
    /// </summary>
    private async Task<System.Text.Json.JsonElement> PlanOfAPolicyAsync(string condition)
    {
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await using (var model = DeskContext.Create())
        {
            await RunAsync(connection, PostgresRowAccess.DropStatement(model));
        }

        await RunAsync(connection,
            $$"""
            CREATE FUNCTION desk.probe_owned_ticket_ids() RETURNS SETOF uuid
                LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
                SELECT "Id" FROM desk."Tickets" WHERE "Owner" = (SELECT ddd.caller_id())
            $function$;
            CREATE POLICY "Probe" ON desk."Tickets" FOR SELECT TO authenticated USING ({{condition}});
            SET LOCAL enable_seqscan = off;
            SET LOCAL ROLE authenticated;
            SELECT set_config('request.jwt.claims', '{{PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Alice, "north")}}', true);
            """);

        return await QueryPlans.ExplainAsync(connection, """SELECT "Id" FROM desk."Tickets" """, "VERBOSE", Cancellation);
    }

    /// <summary>
    /// The titles <paramref name="caller"/> reads with <paramref name="rule"/> as the tickets' only policy, and
    /// <c>desk.caller_seat()</c> answering the caller's own id, in a transaction that is rolled back.
    /// </summary>
    private async Task<List<string>> SeenUnderAsync(RowAccessRule rule, Caller caller)
    {
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await RunAsync(connection, "CREATE FUNCTION desk.caller_seat() RETURNS uuid LANGUAGE sql STABLE AS $function$ SELECT ddd.caller_id() $function$;");

        await using (var model = DeskContext.Create())
        {
            await RunAsync(connection, PostgresRowAccess.Script(model, [rule]));
        }

        var claims = caller.IsSignedIn ? PostgresRowAccessDatabase.ClaimsOf(caller.UserId!.Value, "north") : """{"role":"anon"}""";
        await RunAsync(connection, $"SET LOCAL ROLE {(caller.IsSignedIn ? "authenticated" : "anon")}; SELECT set_config('request.jwt.claims', '{claims}', true);");

        var titles = new List<string>();
        await using var read = new NpgsqlCommand("""SELECT "Title" FROM desk."Tickets" """, connection);
        await using var reader = await read.ExecuteReaderAsync(Cancellation);
        while (await reader.ReadAsync(Cancellation))
        {
            titles.Add(reader.GetString(0));
        }

        return titles;
    }

    /// <summary>
    /// What <paramref name="caller"/> does under <paramref name="script"/>, with <c>desk.caller_on_duty()</c>
    /// answering whether the caller is Alice, in a transaction that is rolled back: how many tickets it reads,
    /// whether a ticket it files is let in, and how many it changes and removes.
    /// </summary>
    private async Task<(long Read, bool Filed, int Changed, int Removed)> OnDutyAsync(Caller caller, string script)
    {
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await RunAsync(connection, $"CREATE FUNCTION desk.caller_on_duty() RETURNS boolean LANGUAGE sql STABLE AS $function$ SELECT ddd.caller_id() = '{PostgresRowAccessDatabase.Alice}'::uuid $function$;");
        await RunAsync(connection, script);

        var claims = caller.IsSignedIn ? PostgresRowAccessDatabase.ClaimsOf(caller.UserId!.Value, "north") : """{"role":"anon"}""";
        await RunAsync(connection, $"SET LOCAL ROLE {(caller.IsSignedIn ? "authenticated" : "anon")}; SELECT set_config('request.jwt.claims', '{claims}', true);");

        await using var read = new NpgsqlCommand("""SELECT count(*) FROM desk."Tickets" """, connection);
        var seen = (long)(await read.ExecuteScalarAsync(Cancellation))!;

        // A ticket filed past every check but the policies.
        bool filed;
        await RunAsync(connection, "SAVEPOINT filing");
        try
        {
            await RunAsync(connection, $$"""
                INSERT INTO desk."Tickets"
                SELECT (pg_catalog.jsonb_populate_record(NULL::desk."Tickets", '{"Id":"{{Guid.NewGuid()}}","Title":"Filed","Status":0,"IsPublic":false,"Version":1}'::jsonb)).*
                """);
            filed = true;
        }
        catch (PostgresException refused) when (refused.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            await RunAsync(connection, "ROLLBACK TO SAVEPOINT filing");
            filed = false;
        }

        await using var change = new NpgsqlCommand("""UPDATE desk."Tickets" SET "Title" = "Title" WHERE "Title" <> 'Filed'""", connection);
        var changed = await change.ExecuteNonQueryAsync(Cancellation);
        await using var remove = new NpgsqlCommand("""DELETE FROM desk."Tickets" WHERE "Title" <> 'Filed'""", connection);
        var removed = await remove.ExecuteNonQueryAsync(Cancellation);

        return (seen, filed, changed, removed);
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }
}
