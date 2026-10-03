using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The declarative half of the policies, against a real Postgres: a script says what the rules are now,
/// so running a later one takes out what an earlier one made and nothing else, and the drop at the start
/// of a module's migration is what lets that migration change a column a rule reads. What a script writes
/// for the tables of an aggregate's entities, which needs no database to read. And the roles a script
/// makes and refuses: the scoped system role, which the role running the migrations may switch to, and
/// never one that could bypass the policies.
/// </summary>
/// <remarks>
/// A fixture of its own, because these tests change the policies and the schema the reading tests rely on.
/// </remarks>
public sealed class PostgresRowAccessScriptTests(PostgresRowAccessDatabase database) : IClassFixture<PostgresRowAccessDatabase>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_rule_taken_out_loses_its_policy_and_a_policy_written_by_hand_stays()
    {
        database.Require();
        await database.RunAsOwnerAsync("""CREATE POLICY "Written by hand" ON desk."Tickets" FOR SELECT TO authenticated USING (false);""", Cancellation);

        await using var model = DeskContext.Create();
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, [DeskRules.Owners]), Cancellation);

        (await database.PolicyNamesAsync("Tickets", Cancellation)).Should().Equal(
            [
                "Owners have their tickets (delete) for anon",
                "Owners have their tickets (delete) for authenticated",
                "Owners have their tickets (insert) for anon",
                "Owners have their tickets (insert) for authenticated",
                "Owners have their tickets (select) for anon",
                "Owners have their tickets (select) for authenticated",
                "Owners have their tickets (update) for anon",
                "Owners have their tickets (update) for authenticated",
                "Written by hand",
            ],
            "the other rules' policies are gone, and the one without the comment stays");
        (await database.PolicyNamesAsync("TicketComment", Cancellation)).Should().Equal(
            [
                "TicketComment (delete) for anon",
                "TicketComment (delete) for authenticated",
                "TicketComment (insert) for anon",
                "TicketComment (insert) for authenticated",
                "TicketComment (select) for anon",
                "TicketComment (select) for authenticated",
                "TicketComment (update) for anon",
                "TicketComment (update) for authenticated",
            ],
            "the aggregate's entities still follow it, now per command and role");

        // The fixture's database is shared by the tests of this class, which read the policies of this table: the one
        // written by hand goes again, so what they find does not depend on the order the tests run in.
        await database.RunAsOwnerAsync("""DROP POLICY "Written by hand" ON desk."Tickets";""", Cancellation);
    }

    [Fact]
    public async Task The_drop_at_the_start_of_a_migration_is_what_lets_it_drop_a_column_a_rule_reads()
    {
        database.Require();
        await using var model = DeskContext.Create();
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, [DeskRules.Owners, DeskRules.Teammates, DeskRules.Public]), Cancellation);

        var alone = () => database.RunAsOwnerAsync("""ALTER TABLE desk."Tickets" DROP COLUMN "Team";""", Cancellation);
        (await alone.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.DependentObjectsStillExist, "\"Teammates read open tickets\" reads the column");

        var withTheDrop = () => database.RunAsOwnerAsync(PostgresRowAccess.DropStatement(model) + """ALTER TABLE desk."Tickets" DROP COLUMN "Team";""", Cancellation);
        await withTheDrop.Should().NotThrowAsync();
    }

    [Fact]
    public void Each_entity_table_gets_one_policy_per_command_and_role()
    {
        var script = ScriptFor(DeskRules.Owners);

        foreach (var table in (string[])["TicketComment", "TicketWatcher", "CommentReaction"])
        {
            PoliciesOn(script, table).Should().Equal(
                [
                    $"{table} (select) for anon",
                    $"{table} (select) for authenticated",
                    $"{table} (insert) for anon",
                    $"{table} (insert) for authenticated",
                    $"{table} (update) for anon",
                    $"{table} (update) for authenticated",
                    $"{table} (delete) for anon",
                    $"{table} (delete) for authenticated",
                ],
                "a rule that allows everything grants every command, and a rule without To is for anon and authenticated");
        }

        script.Should().Contain(
            "CREATE POLICY \"TicketComment (select) for anon\" ON desk.\"TicketComment\" FOR SELECT TO anon\n" +
            "    USING (EXISTS (SELECT 1 FROM desk.\"Tickets\" parent WHERE parent.\"Id\" = desk.\"TicketComment\".\"TicketId\"));\n" +
            "COMMENT ON POLICY \"TicketComment (select) for anon\" ON desk.\"TicketComment\" IS 'DDDToolkit row access rule';",
            "reading follows the parent row, whose own policies decide");
        script.Should().Contain(
            "CREATE POLICY \"TicketComment (update) for authenticated\" ON desk.\"TicketComment\" FOR UPDATE TO authenticated\n" +
            "    USING (EXISTS (SELECT 1 FROM desk.\"Tickets\" r WHERE r.\"Id\" = desk.\"TicketComment\".\"TicketId\" AND (((SELECT ddd.caller_id()) IS NOT NULL) AND (r.\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id())))))\n" +
            "    WITH CHECK (EXISTS (SELECT 1 FROM desk.\"Tickets\" r WHERE r.\"Id\" = desk.\"TicketComment\".\"TicketId\" AND (((SELECT ddd.caller_id()) IS NOT NULL) AND (r.\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id())))));",
            "changing an entity asks the root's Change rules about the root row");
        script.Should().Contain(
            "CREATE POLICY \"TicketComment (delete) for authenticated\" ON desk.\"TicketComment\" FOR DELETE TO authenticated\n" +
            "    USING (EXISTS (SELECT 1 FROM desk.\"Tickets\" r WHERE r.\"Id\" = desk.\"TicketComment\".\"TicketId\" AND (((SELECT ddd.caller_id()) IS NOT NULL) AND (r.\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id())))));",
            "a rule that allows Change is asked once, however many of the operations it allows");
    }

    /// <summary>
    /// A policy's condition stands in parentheses of its own. One that a pair already encloses from its first
    /// character to its last keeps that pair, unless the pair is a subquery's; a parenthesis in a literal, a
    /// quoted name or a comment is not counted, a literal between dollar quotes or with escapes included. A
    /// condition with a comment that runs to the end of its line gets its closing parenthesis on the next.
    /// </summary>
    [Theory]
    [InlineData("(SELECT desk.caller_on_duty())", "((SELECT desk.caller_on_duty()))")]
    [InlineData("((SELECT desk.caller_on_duty()))", "((SELECT desk.caller_on_duty()))")]
    [InlineData("((SELECT desk.caller_on_duty()) = TRUE)", "((SELECT desk.caller_on_duty()) = TRUE)")]
    [InlineData("((SELECT TRUE) UNION (SELECT FALSE))", "(((SELECT TRUE) UNION (SELECT FALSE)))")]
    [InlineData("(WITH duty AS (SELECT TRUE AS here) SELECT here FROM duty)", "((WITH duty AS (SELECT TRUE AS here) SELECT here FROM duty))")]
    [InlineData("({col:IsPublic})", "(\"IsPublic\")")]
    [InlineData("({col:IsPublic}) OR ({col:Title} = 'x')", "((\"IsPublic\") OR (\"Title\" = 'x'))")]
    [InlineData("('\"' = {col:Title}) OR ({col:Title} = '\"')", "(('\"' = \"Title\") OR (\"Title\" = '\"'))")]
    [InlineData("({col:Title} = ')') OR ({col:IsPublic})", "((\"Title\" = ')') OR (\"IsPublic\"))")]
    [InlineData("({col:IsPublic} /* ) OR ( */)", "(\"IsPublic\" /* ) OR ( */)")]
    [InlineData("{col:IsPublic}", "(\"IsPublic\")")]
    [InlineData("{col:IsPublic} -- public rows", "(\"IsPublic\" -- public rows\n)")]
    [InlineData("({col:IsPublic}) -- public rows", "((\"IsPublic\") -- public rows\n)")]
    [InlineData("({col:IsPublic} -- public rows\n)", "(\"IsPublic\" -- public rows\n)")]
    [InlineData("({col:Title} = '--') OR ({col:IsPublic})", "((\"Title\" = '--') OR (\"IsPublic\"))")]
    [InlineData("({col:Title} = $$($$) OR ({col:Title} = $$)$$)", "((\"Title\" = $$($$) OR (\"Title\" = $$)$$))")]
    [InlineData("({col:Title} = $t$($t$) OR ({col:Title} = $t$)$t$)", "((\"Title\" = $t$($t$) OR (\"Title\" = $t$)$t$))")]
    [InlineData("({col:Title} = E'\\'(') OR ({col:Title} = ')')", "((\"Title\" = E'\\'(') OR (\"Title\" = ')'))")]
    public void A_condition_keeps_a_pair_of_parentheses_only_where_it_encloses_the_whole_and_is_no_subquerys(string sql, string written)
    {
        var script = ScriptFor(RowAccessRule.For<Ticket>("Read as the test says", RowOperations.Read, sql));

        script.Should().Contain("CREATE POLICY \"Read as the test says (select) for authenticated\" ON desk.\"Tickets\" FOR SELECT TO authenticated\n    USING " + written + ";\n");
    }

    [Fact]
    public void A_command_no_rule_grants_gets_no_entity_policy()
    {
        PoliciesOn(ScriptFor(DeskRules.Public, DeskRules.Teammates), "TicketComment").Should().Equal(
            ["TicketComment (select) for anon", "TicketComment (select) for authenticated"],
            "rules that only read let nobody write the entities, and a pair without a policy is refused");

        PoliciesOn(ScriptFor(DeskRules.TeamFiles), "TicketComment").Should().Equal(
            ["TicketComment (insert) for anon", "TicketComment (insert) for authenticated"],
            "a rule that only creates lets its callers add the entities of a new root, and neither read, change nor remove them");

        var removing = RowAccessRule.For<Ticket>("Owners throw their tickets away", RowOperations.Remove, OwnersHaveTheirTickets.RowAccessSql);
        PoliciesOn(ScriptFor(removing), "TicketComment").Should().Equal(
            ["TicketComment (delete) for anon", "TicketComment (delete) for authenticated"],
            "removing the root is removing its entities");
    }

    [Fact]
    public void Inserting_an_entity_needs_change_or_create_of_a_new_root()
    {
        var script = ScriptFor(DeskRules.Owners, DeskRules.TeamFiles);

        script.Should().Contain(
            "CREATE POLICY \"TicketComment (insert) for authenticated\" ON desk.\"TicketComment\" FOR INSERT TO authenticated\n" +
            "    WITH CHECK (EXISTS (SELECT 1 FROM desk.\"Tickets\" r WHERE r.\"Id\" = desk.\"TicketComment\".\"TicketId\" AND (" +
            "(((SELECT ddd.caller_id()) IS NOT NULL) AND (r.\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id())))" +
            " OR " +
            "((((r.\"Owner\" IS NULL) AND (r.\"Team\" IS NOT NULL)) AND (r.\"Team\" IS NOT DISTINCT FROM (SELECT ddd.caller_claims() #>> '{app_metadata,team}'))) AND ddd.written_in_this_transaction(r.xmin)))));",
            "the owner changes the ticket, so adds to it; a teammate only creates one, so adds only to a ticket this transaction wrote");
    }

    [Fact]
    public void A_grandchild_joins_up_to_the_root()
    {
        var script = ScriptFor(DeskRules.Owners);

        script.Should().Contain(
            "CREATE POLICY \"CommentReaction (select) for anon\" ON desk.\"CommentReaction\" FOR SELECT TO anon\n" +
            "    USING (EXISTS (SELECT 1 FROM desk.\"TicketComment\" parent WHERE parent.\"TicketId\" = desk.\"CommentReaction\".\"TicketCommentTicketId\" AND parent.\"Id\" = desk.\"CommentReaction\".\"TicketCommentId\"));",
            "a reaction is read with its comment, which is read with its ticket");
        script.Should().Contain(
            "CREATE POLICY \"CommentReaction (update) for anon\" ON desk.\"CommentReaction\" FOR UPDATE TO anon\n" +
            "    USING (EXISTS (SELECT 1 FROM desk.\"TicketComment\" p1" +
            " WHERE p1.\"TicketId\" = desk.\"CommentReaction\".\"TicketCommentTicketId\" AND p1.\"Id\" = desk.\"CommentReaction\".\"TicketCommentId\"" +
            " AND EXISTS (SELECT 1 FROM desk.\"Tickets\" r WHERE r.\"Id\" = p1.\"TicketId\"" +
            " AND (((SELECT ddd.caller_id()) IS NOT NULL) AND (r.\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id()))))))\n",
            "the write rules are about the ticket, so the policy goes up from the reaction through its comment to the ticket, which is alone where the rule is asked");
    }

    [Fact]
    public async Task A_rule_that_names_a_root_column_itself_holds_for_a_grandchild_whose_parent_has_that_column_too()
    {
        database.Require();
        await using var model = DeskContext.Create();
        var named = RowAccessRule.For<Ticket>("Tickets with an id change", RowOperations.Change, "\"Id\" IS NOT NULL");

        var run = () => database.RunAsOwnerAsync(PostgresRowAccess.Script(model, [DeskRules.Owners, named]), Cancellation);

        await run.Should().NotThrowAsync("a comment has an \"Id\" as well, and the rule is asked where only the ticket is in reach");
        (await database.PolicyNamesAsync("CommentReaction", Cancellation)).Should().Contain("CommentReaction (update) for anon");
    }

    [Fact]
    public void An_entity_rule_for_two_roles_is_two_policies()
    {
        var moderators = RowAccessRule.For<Ticket>("Moderators change any ticket", RowOperations.Change, "TRUE", "moderator", "authenticated");

        var script = ScriptFor(moderators, DeskRules.Public);

        PoliciesOn(script, "TicketWatcher").Should().Equal(
            [
                "TicketWatcher (select) for anon",
                "TicketWatcher (select) for authenticated",
                "TicketWatcher (insert) for authenticated",
                "TicketWatcher (insert) for moderator",
                "TicketWatcher (update) for authenticated",
                "TicketWatcher (update) for moderator",
                "TicketWatcher (delete) for authenticated",
                "TicketWatcher (delete) for moderator",
            ],
            "each policy names one role, and a role gets only the commands its own rules grant");
        script.Should().Contain("CREATE POLICY \"TicketWatcher (update) for moderator\" ON desk.\"TicketWatcher\" FOR UPDATE TO moderator\n");
    }

    [Fact]
    public async Task The_prelude_is_idempotent_and_comes_after_the_drop()
    {
        await using var model = DeskContext.Create();
        var script = PostgresRowAccess.Script(model, [DeskRules.Owners, DeskRules.Public]);
        var drop = PostgresRowAccess.DropStatement(model);

        script.Should().StartWith(drop, "the policies come off first, whatever the prelude would change under them");
        script[drop.Length..].TrimStart('\n').Should().StartWith(
            "-- What the policies below ask: the ddd schema, and the function",
            "the prelude comes straight after the drop, before any function or policy that needs it");
        script.Should().Contain("    IF pg_catalog.to_regnamespace('ddd') IS NULL THEN\n        CREATE SCHEMA ddd;\n    END IF;\n");
        script.Should().NotContain("CREATE SCHEMA IF NOT EXISTS", "Postgres asks for the right to create a schema before it looks whether it exists");
        script.Should().Contain("        GRANT USAGE ON SCHEMA ddd TO anon;\n");
        script.Should().Contain("        GRANT EXECUTE ON FUNCTION ddd.written_in_this_transaction(xid) TO authenticated;\n");

        database.Require();
        await database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(loginRole: PostgresRowAccessDatabase.LoginRole), Cancellation);
        var before = await FunctionVersionAsync();
        var twice = async () =>
        {
            await database.RunAsOwnerAsync(script, Cancellation);
            await database.RunAsOwnerAsync(script, Cancellation);
        };

        await twice.Should().NotThrowAsync("a script can run on a database that already has everything its prelude makes");
        (await FunctionVersionAsync()).Should().Be(before, "the function the setup made is the one the policies need, so the prelude leaves it alone");
    }

    [Fact]
    public async Task The_setup_script_makes_the_procedure_that_sets_a_caller_and_gives_it_to_the_login_role_alone()
    {
        const string signature = "ddd.use_caller(text, text, text[], text[])";
        var setup = PostgresRowAccess.SetupScript(loginRole: PostgresRowAccessDatabase.LoginRole);

        setup.Should().Contain("CREATE OR REPLACE PROCEDURE ddd.use_caller(role_name text, claims text, setting_names text[], setting_values text[]) LANGUAGE plpgsql AS $procedure$\nBEGIN\n");
        setup.Should().Contain($"REVOKE ALL ON PROCEDURE {signature} FROM PUBLIC;\n");
        setup.Should().Contain($"GRANT USAGE ON SCHEMA ddd TO \"{PostgresRowAccessDatabase.LoginRole}\";\n", "the role that logs in calls it, and needs the schema it is in for that");
        setup.Should().Contain($"GRANT EXECUTE ON PROCEDURE {signature} TO \"{PostgresRowAccessDatabase.LoginRole}\";\n");
        PostgresRowAccess.SetupScript().Should().Contain($"GRANT EXECUTE ON PROCEDURE {signature} TO CURRENT_USER;\n", "without a login role named, it is the role that runs the script");

        database.Require();
        await database.RunAsOwnerAsync(setup, Cancellation);

        (await ScalarAsync<string>(
            $"""
            SELECT pg_catalog.has_function_privilege('{PostgresRowAccessDatabase.LoginRole}', '{signature}', 'EXECUTE')::text || '|' ||
                   pg_catalog.has_function_privilege('public', '{signature}', 'EXECUTE')::text || '|' ||
                   pg_catalog.has_function_privilege('authenticated', '{signature}', 'EXECUTE')::text || '|' ||
                   pg_catalog.has_function_privilege('anon', '{signature}', 'EXECUTE')::text || '|' ||
                   pg_catalog.has_function_privilege('ddd_system_in', '{signature}', 'EXECUTE')::text
            """))
            .Should().Be("true|false|false|false|false", "only the role the application logs in as may call it: whoever could call it could say who they are");
        (await ScalarAsync<string>($"SELECT prokind::text || '|' || prosecdef::text || '|' || coalesce(proconfig::text, 'none') FROM pg_catalog.pg_proc WHERE oid = '{signature}'::regprocedure"))
            .Should().Be("p|false|none", "a procedure that runs as its caller, the role that may switch roles, with no setting of its own");
    }

    [Fact]
    public async Task The_prelude_makes_the_procedure_where_it_is_missing_or_differs_and_takes_it_from_public()
    {
        database.Require();
        const string signature = "ddd.use_caller(text, text, text[], text[])";
        const string state = $"SELECT xmin::text || '|' || pg_catalog.has_function_privilege('public', oid, 'EXECUTE')::text || '|' || (prosrc LIKE '%request.jwt.claim.email%')::text FROM pg_catalog.pg_proc WHERE oid = '{signature}'::regprocedure";
        await using var model = DeskContext.Create();
        var script = PostgresRowAccess.Script(model, [DeskRules.Owners, DeskRules.Public]);

        script.Should().Contain("    use_caller constant text := $procedure$\nBEGIN\n    PERFORM pg_catalog.set_config('role', role_name, true);\n");
        script.Should().Contain($"    IF pg_catalog.has_function_privilege('public', '{signature}', 'EXECUTE') THEN\n        REVOKE ALL ON PROCEDURE {signature} FROM PUBLIC;\n    END IF;\n");
        script.Should().NotContain("CREATE OR REPLACE PROCEDURE ddd.use_caller(role_name text, claims text, setting_names text[], setting_values text[]) LANGUAGE plpgsql AS $procedure$", "only its owner may replace it, so the prelude replaces it only when it differs");

        try
        {
            // As the setup left it: the prelude finds it as it needs it, and leaves it alone.
            await database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(loginRole: PostgresRowAccessDatabase.LoginRole), Cancellation);
            var before = await ScalarAsync<string>(state);
            before.Should().EndWith("|false|true");
            await database.RunAsOwnerAsync(script, Cancellation);
            (await ScalarAsync<string>(state)).Should().Be(before, "the same procedure, not made again");

            // Missing, on a database that never saw the setup script: made, and never left to PUBLIC, which
            // Postgres gives every new routine to.
            await database.RunAsOwnerAsync($"DROP PROCEDURE {signature};", Cancellation);
            await database.RunAsOwnerAsync(script, Cancellation);
            (await ScalarAsync<string>(state)).Should().EndWith("|false|true");

            // Another body under the same name, executable by everybody: replaced, and taken back.
            await database.RunAsOwnerAsync(
                $"CREATE OR REPLACE PROCEDURE ddd.use_caller(role_name text, claims text, setting_names text[], setting_values text[]) LANGUAGE plpgsql AS $$BEGIN NULL; END$$; GRANT EXECUTE ON PROCEDURE {signature} TO PUBLIC;",
                Cancellation);
            (await ScalarAsync<string>(state)).Should().EndWith("|true|false");
            await database.RunAsOwnerAsync(script, Cancellation);
            (await ScalarAsync<string>(state)).Should().EndWith("|false|true");
        }
        finally
        {
            // The login role's right to call it went with the procedure that was dropped.
            await database.RunAsOwnerAsync(PostgresRowAccess.SetupScript(loginRole: PostgresRowAccessDatabase.LoginRole), Cancellation);
        }
    }

    [Fact]
    public async Task The_application_runs_a_script_after_somebody_else_ran_the_setup()
    {
        database.Require();

        // The fixture ran the setup as the superuser, who owns ddd and its function, and gave the
        // application's role the desk's schema and tables. What a script needed already, it gets here: its
        // access functions, and USAGE on ddd for the caller functions the policies call.
        await database.RunAsOwnerAsync(
            $"""
            GRANT USAGE ON SCHEMA ddd TO {PostgresRowAccessDatabase.LoginRole};
            DO $$
            DECLARE
                f record;
            BEGIN
                FOR f IN SELECT p.oid::regprocedure AS signature FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'desk' LOOP
                    EXECUTE format('ALTER FUNCTION %s OWNER TO {PostgresRowAccessDatabase.LoginRole}', f.signature);
                END LOOP;
            END
            $$;
            """,
            Cancellation);

        (await ScalarAsync<bool>($"SELECT pg_catalog.has_database_privilege('{PostgresRowAccessDatabase.LoginRole}', pg_catalog.current_database(), 'CREATE')"))
            .Should().BeFalse("the application may not create a schema, not even one that exists");
        (await ScalarAsync<string>("SELECT pg_catalog.pg_get_userbyid(proowner)::text FROM pg_catalog.pg_proc WHERE oid = 'ddd.written_in_this_transaction(xid)'::regprocedure"))
            .Should().NotBe(PostgresRowAccessDatabase.LoginRole, "the application does not own the function, so it could not replace it");

        await using var model = DeskContext.Create();
        var run = () => database.RunAsApplicationAsync(
            PostgresRowAccess.Script(model, [DeskRules.Owners, DeskRules.Public, DeskRules.Watchers, ScopedWork], [DeskRules.IsWatcher]),
            Cancellation);

        await run.Should().NotThrowAsync("the prelude finds the schema, the function, the scoped role and the grants as the policies need them, and touches none of them");
        (await database.PolicyNamesAsync("TicketComment", Cancellation)).Should().Contain(["TicketComment (insert) for authenticated", "TicketComment (select) for ddd_system_in"]);
    }

    [Fact]
    public void Scripts_puts_a_context_before_those_that_ask_its_functions_and_leaves_the_rest_in_order()
    {
        using var shelves = SupabaseShelfContext.Create();
        using var yard = YardContext.Create();
        using var scale = DeskScaleContext.Create();
        var shelvesByName = RowAccessRule.For<SupabaseShelf>("Shelves by name", RowOperations.Read, "({col:Name} IS NOT DISTINCT FROM {caller:claim:shelf})");
        var cratesOfWatchedTickets = RowAccessRule.For<Crate>(
            "Crates of watched tickets", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:desk/tickets_i_watch}())))");
        RowAccessRule[] rules = [shelvesByName, cratesOfWatchedTickets, DeskRules.WatchersBySet];

        var scripts = PostgresRowAccess.Scripts([shelves, yard, scale], rules, [DeskRules.WatchedSet], new RowAccessExport());

        scripts.Select(each => each.Context).Should().Equal(
            new DbContext[] { shelves, scale, yard },
            "the yard asks desk/tickets_i_watch, which the scaled desk makes, so the desk runs before it; the shelves ask nobody's, and keep their place");
        scripts[1].Script.Should().Contain("CREATE OR REPLACE FUNCTION desk_scale.tickets_i_watch() RETURNS SETOF uuid");
        scripts[0].Script.Should().Contain("CREATE POLICY \"Shelves by name (select) for anon\"").And.NotContain("tickets_i_watch", "shelves know no tickets");
    }

    [Fact]
    public void A_set_function_is_written_returning_setof_its_key_with_typed_parameters()
    {
        using var desk = DeskContext.Create();

        var sql = PostgresRowAccess.CreateStatements(desk, [], [DeskRules.ForTeam], new RowAccessExport());

        DeskRules.ForTeam.Parameters.Should().Be("text, boolean", "the generator gave the SQL types of Allows' parameters after the caller");
        sql.Should().Contain(
            "CREATE OR REPLACE FUNCTION desk.tickets_for_team(text, boolean) RETURNS SETOF uuid\n" +
            "    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$\n" +
            "    SELECT root.\"Id\" FROM desk.\"Tickets\" root\n" +
            "    WHERE ((root.\"Team\" IS NOT DISTINCT FROM $1) AND ((NOT $2) OR (root.\"Status\" = 0)))\n" +
            "$function$;\n" +
            "COMMENT ON FUNCTION desk.tickets_for_team(text, boolean) IS 'DDDToolkit access function of DeskContext';",
            "a set-shaped function takes its own parameters, and answers with the key of every row it allows");
    }

    [Fact]
    public void A_function_about_one_row_takes_its_own_parameters_after_the_key()
    {
        using var desk = DeskContext.Create();
        var inTeam = RowAccessFunction.For<Ticket>("desk.in_team", "({col:Team} IS NOT DISTINCT FROM {arg:1})", owner: null, parameters: "text");
        var asked = RowAccessRule.For<Ticket>("In the north", RowOperations.Read, "({fn:desk.in_team}({key}, 'north') OR {fn:desk.in_team}({col:Id}, 'south'))");

        var sql = PostgresRowAccess.CreateStatements(desk, [asked], [inTeam], new RowAccessExport());

        sql.Should().Contain("CREATE OR REPLACE FUNCTION desk.in_team(uuid, text) RETURNS boolean")
            .And.Contain("WHERE root.\"Id\" = $1 AND (root.\"Team\" IS NOT DISTINCT FROM $2))")
            .And.Contain("USING (desk.in_team(\"Id\", 'north') OR desk.in_team(\"Id\", 'south'));");
    }

    [Fact]
    public void A_set_function_of_one_any_starts_from_the_entity_table()
    {
        using var desk = DeskContext.Create();

        var sql = PostgresRowAccess.CreateStatements(desk, [DeskRules.WatchersBySet], [DeskRules.WatchedSet], new RowAccessExport());

        sql.Should().Contain(
            "CREATE OR REPLACE FUNCTION desk.tickets_i_watch() RETURNS SETOF uuid\n" +
            "    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$\n" +
            "    SELECT DISTINCT e1.\"TicketId\" FROM desk.\"TicketWatcher\" e1\n" +
            "    WHERE (e1.\"User\" IS NOT DISTINCT FROM (SELECT ddd.caller_id()))\n" +
            "$function$;",
            "its whole question is whether one of the ticket's watchers is the caller, which the watchers' table answers alone");
        sql.Should().Contain(
            "CREATE POLICY \"Watchers read the tickets they watch (select) for authenticated\" ON desk.\"Tickets\" FOR SELECT TO authenticated\n" +
            "    USING (\"Id\" = ANY (ARRAY(SELECT desk.tickets_i_watch())));",
            "the rule asks the set once per statement, by the name the function has in the database");
    }

    [Fact]
    public void A_set_function_over_the_entities_of_an_entity_starts_from_the_entity_table_and_asks_theirs_inside()
    {
        using var desk = DeskContext.Create();

        var sql = PostgresRowAccess.CreateStatements(desk, [], [DeskRules.ReactedSet], new RowAccessExport());

        TicketsIReactedIn.RowAccessSql.Should().Be(
            "{exists:Comments:e1}{exists:e1:Reactions:e2}({col:e2:User} IS NOT DISTINCT FROM {caller:uid}){/exists}{/exists}",
            "the generator names the entity whose collection the inner question is asked of");
        sql.Should().Contain(
            "CREATE OR REPLACE FUNCTION desk.tickets_i_reacted_in() RETURNS SETOF uuid\n" +
            "    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$\n" +
            "    SELECT DISTINCT e1.\"TicketId\" FROM desk.\"TicketComment\" e1\n" +
            "    WHERE (EXISTS (SELECT 1 FROM desk.\"CommentReaction\" e2 WHERE e2.\"TicketCommentTicketId\" = e1.\"TicketId\" AND e2.\"TicketCommentId\" = e1.\"Id\" AND ((e2.\"User\" IS NOT DISTINCT FROM (SELECT ddd.caller_id())))))\n" +
            "$function$;",
            "the comments' table answers with each ticket's key, and a comment's reactions are asked for that comment: the tickets' table is never read");
    }

    [Fact]
    public void A_function_about_one_row_asks_the_entities_of_an_entity_inside_its_exists()
    {
        using var desk = DeskContext.Create();

        var sql = PostgresRowAccess.CreateStatements(desk, [], [DeskRules.ReactedTo], new RowAccessExport());

        sql.Should().Contain("CREATE OR REPLACE FUNCTION desk.reacted_to_comment(uuid, text) RETURNS boolean")
            .And.Contain(
                "WHERE root.\"Id\" = $1 AND (root.\"IsPublic\" OR EXISTS (SELECT 1 FROM desk.\"TicketComment\" e1 WHERE e1.\"TicketId\" = root.\"Id\" AND ("
                + "((e1.\"Text\" IS NOT DISTINCT FROM $2) AND EXISTS (SELECT 1 FROM desk.\"CommentReaction\" e2 WHERE e2.\"TicketCommentTicketId\" = e1.\"TicketId\" AND e2.\"TicketCommentId\" = e1.\"Id\" AND ("
                + "(e2.\"User\" IS NOT DISTINCT FROM (SELECT ddd.caller_id())))))))))\n",
                "a comment belongs to the row, a reaction to its comment, and the function's own parameter is read between the two");
    }

    [Fact]
    public void A_set_function_that_also_asks_the_root_row_keeps_starting_from_the_root()
    {
        using var desk = DeskContext.Create();
        var publicOrReacted = RowAccessFunction.For<Ticket>(
            "desk.tickets_public_or_reacted",
            "{exists:Comments:e1}({col:IsPublic} OR {exists:e1:Reactions:e2}TRUE{/exists}){/exists}",
            owner: null,
            shape: AccessFunctionShape.Set);
        var twoBlocks = RowAccessFunction.For<Ticket>(
            "desk.tickets_reacted_and_watched",
            "{exists:Comments:e1}{exists:e1:Reactions:e2}TRUE{/exists}{/exists} AND {exists:Watchers:e1}TRUE{/exists}",
            owner: null,
            shape: AccessFunctionShape.Set);

        var sql = PostgresRowAccess.CreateStatements(desk, [], [publicOrReacted, twoBlocks], new RowAccessExport());

        sql.Should().Contain(
            "CREATE OR REPLACE FUNCTION desk.tickets_public_or_reacted() RETURNS SETOF uuid\n" +
            "    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$\n" +
            "    SELECT root.\"Id\" FROM desk.\"Tickets\" root\n" +
            "    WHERE (EXISTS (SELECT 1 FROM desk.\"TicketComment\" e1 WHERE e1.\"TicketId\" = root.\"Id\" AND ((root.\"IsPublic\" OR EXISTS (SELECT 1 FROM desk.\"CommentReaction\" e2 ",
            "what the comments' block asks of the ticket itself only the tickets' table answers");
        sql.Should().Contain(
            "CREATE OR REPLACE FUNCTION desk.tickets_reacted_and_watched() RETURNS SETOF uuid\n" +
            "    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$\n" +
            "    SELECT root.\"Id\" FROM desk.\"Tickets\" root\n",
            "two blocks next to each other are two tables, which only the root ties together");
    }

    [Fact]
    public void An_exists_over_the_entities_of_an_entity_nothing_is_looking_at_is_refused()
    {
        using var desk = DeskContext.Create();
        var astray = RowAccessFunction.For<Ticket>("desk.astray", "{exists:e1:Reactions:e2}TRUE{/exists}");
        var unmapped = RowAccessFunction.For<Ticket>("desk.unmapped", "{exists:Comments:e1}{exists:e1:Replies:e2}TRUE{/exists}{/exists}");

        var outside = () => PostgresRowAccess.CreateStatements(desk, [], [astray], new RowAccessExport());
        var noSuch = () => PostgresRowAccess.CreateStatements(desk, [], [unmapped], new RowAccessExport());

        outside.Should().Throw<InvalidOperationException>().WithMessage("*'{exists:e1:Reactions:e2}' outside the {exists} that names 'e1'*");
        noSuch.Should().Throw<InvalidOperationException>().WithMessage(
            "*reads TicketComment.Replies, which the Entity Framework model does not map as a collection of TicketComment's entities in a table of their own*");
    }

    [Fact]
    public void Relative_names_resolve_to_the_defining_contexts_schema()
    {
        using var yard = YardContext.Create();
        using var scale = DeskScaleContext.Create();
        var cratesOfWatchedTickets = RowAccessRule.For<Crate>(
            "Crates of watched tickets", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:desk/tickets_i_watch}())))");
        RowAccessRule[] rules = [cratesOfWatchedTickets, DeskRules.WatchersBySet];

        var scripts = PostgresRowAccess.Scripts([yard, scale], rules, [DeskRules.WatchedSet], new RowAccessExport());

        PostgresRowAccess.FunctionNamesOf([yard, scale], [DeskRules.WatchedSet]).Should().Equal(
            new Dictionary<string, string> { ["desk/tickets_i_watch"] = "desk_scale.tickets_i_watch" },
            "the function is created in the schema of the context that maps the tickets");
        scripts.Single(each => each.Context == scale).Script.Should().Contain("CREATE OR REPLACE FUNCTION desk_scale.tickets_i_watch() RETURNS SETOF uuid")
            .And.Contain("USING (\"Id\" = ANY (ARRAY(SELECT desk_scale.tickets_i_watch())));");
        scripts.Single(each => each.Context == yard).Script.Should().Contain("USING (\"Id\" = ANY (ARRAY(SELECT desk_scale.tickets_i_watch())));",
            "a rule of another context asks it by its logical name, and gets the name it has in the database")
            .And.NotContain("FUNCTION desk_scale.tickets_i_watch", "only the context that maps the tickets writes the function");

        using var desk = DeskContext.Create();
        PostgresRowAccess.Script(desk, [DeskRules.WatchersBySet], [DeskRules.WatchedSet]).Should().Contain("SELECT desk.tickets_i_watch()",
            "the small desk's tickets are in desk, and so is its function");
        PostgresRowAccess.Script(yard, [cratesOfWatchedTickets], [], new RowAccessExport { FunctionNames = new Dictionary<string, string> { ["desk/tickets_i_watch"] = "desk.tickets_i_watch" } })
            .Should().Contain("SELECT desk.tickets_i_watch()", "a script of one context learns where another context's functions live from the export");
    }

    [Fact]
    public void A_name_nobody_defines_is_refused_naming_the_rule()
    {
        using var yard = YardContext.Create();
        var asking = RowAccessRule.For<Crate>("Crates of watched tickets", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:desk/tickets_i_watch}())))");

        var script = () => PostgresRowAccess.Script(yard, [asking]);

        script.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Crates of watched tickets' asks the function desk/tickets_i_watch, and none of the functions this is written with is called that.*");
    }

    [Fact]
    public async Task A_function_that_calls_one_later_in_the_alphabet_is_created_after_it()
    {
        database.Require();
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await using var model = DeskContext.Create();
        var script = PostgresRowAccess.Script(model, [], [], new RowAccessExport { Contributions = [new DutyRowAccess()] });

        // What a script that wrote its functions by name would run first: the set, before the question it asks.
        var asker = Regex.Match(script, @"CREATE OR REPLACE FUNCTION desk\.duty_tickets\(\).*?\$function\$;", RegexOptions.Singleline).Value;
        await RunAsync(connection, "SAVEPOINT by_name");
        var byName = () => RunAsync(connection, asker);
        (await byName.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UndefinedFunction, "Postgres checks a function's body when it creates it");
        await RunAsync(connection, "ROLLBACK TO SAVEPOINT by_name");

        var inOrder = () => RunAsync(connection, script);
        await inOrder.Should().NotThrowAsync("the script creates the question before the set that asks it");
        (await ScalarAsync<long>(connection, "SELECT count(*) FROM pg_catalog.pg_proc WHERE oid IN (to_regprocedure('desk.on_duty()'), to_regprocedure('desk.duty_tickets()'))")).Should().Be(2);
    }

    [Fact]
    public async Task A_contributed_function_is_kept_by_the_next_access_file()
    {
        database.Require();
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await using var yard = YardContext.Create();
        await using var desk = DeskContext.Create();

        // Another test of this class may have left a yard of its own; this one is gone again with the transaction.
        await RunAsync(connection, "DROP SCHEMA IF EXISTS yard CASCADE;");
        await RunAsync(connection, yard.Database.GenerateCreateScript());

        var cratesOnDuty = RowAccessRule.For<Crate>("Crates on duty", RowOperations.Read, "{fn:duty/on_duty}()", RowAccessRoles.User);
        var export = new RowAccessExport { Contributions = [new DutyRowAccess()] };
        var scripts = PostgresRowAccess.Scripts([yard, desk], [cratesOnDuty], [], export);
        scripts.Select(each => each.Context).Should().Equal([desk, yard], "the yard's policy asks a function the desk's contribution writes");

        foreach (var (_, script) in scripts)
        {
            await RunAsync(connection, script);
        }

        var made = await ScalarAsync<long>(connection, "SELECT 'desk.on_duty()'::regprocedure::oid::bigint");

        var again = async () =>
        {
            foreach (var (_, script) in scripts)
            {
                await RunAsync(connection, script);
            }
        };

        await again.Should().NotThrowAsync("the desk's next file keeps the function the yard's policy uses, rather than dropping it");
        (await ScalarAsync<long>(connection, "SELECT 'desk.on_duty()'::regprocedure::oid::bigint")).Should().Be(made, "it was replaced in place");
        (await ScalarAsync<long>(connection, "SELECT count(*) FROM pg_catalog.pg_policies WHERE schemaname = 'yard' AND policyname = 'Crates on duty (select) for authenticated'")).Should().Be(1);
        (await ScalarAsync<bool>(connection, "SELECT has_function_privilege('authenticated', 'desk.on_duty()', 'EXECUTE')")).Should().BeTrue("the function is granted to the users");
        (await ScalarAsync<bool>(connection, "SELECT has_function_privilege('anon', 'desk.on_duty()', 'EXECUTE')")).Should().BeFalse("and to nobody else, not even through PUBLIC");
    }

    [Fact]
    public async Task Grants_follow_the_roles_that_ask_and_outlast_no_default_privilege()
    {
        database.Require();
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await using var desk = DeskContext.Create();

        // What Supabase's public schema does: every new function is executable by the users and the anonymous callers.
        await RunAsync(connection, "ALTER DEFAULT PRIVILEGES IN SCHEMA desk GRANT EXECUTE ON FUNCTIONS TO anon, authenticated;");

        IReadOnlyList<string> grantTo = [RowAccessRoles.User, RowAccessRoles.SystemIn];
        var shifting = new SpotContribution("shifting", _ => new([new ContributedFunction("on_shift", "", "boolean", "SELECT true", GrantTo: grantTo)], [], []));
        var watchersForEveryone = RowAccessRule.For<Ticket>("Watchers read their tickets", RowOperations.Read, "{call:desk.is_watcher}");
        await RunAsync(connection, PostgresRowAccess.Script(desk, [watchersForEveryone], [DeskRules.IsWatcher], new RowAccessExport { Contributions = [shifting] }));

        (await CanExecuteAsync("anon", "desk.on_shift()")).Should().BeFalse("the default privileges gave it to the anonymous callers, and the script took that back");
        (await CanExecuteAsync("authenticated", "desk.on_shift()")).Should().BeTrue();
        (await CanExecuteAsync("ddd_system_in", "desk.on_shift()")).Should().BeTrue();
        (await CanExecuteAsync("anon", "desk.is_watcher(uuid)")).Should().BeTrue("a policy for the anonymous callers asks it");

        // The next script: the contributed function for scoped work alone, and the rule for signed-in users alone.
        grantTo = [RowAccessRoles.SystemIn];
        var watchersSignedIn = RowAccessRule.For<Ticket>("Watchers read their tickets", RowOperations.Read, "{call:desk.is_watcher}", RowAccessRoles.User);
        await RunAsync(connection, PostgresRowAccess.Script(desk, [watchersSignedIn], [DeskRules.IsWatcher], new RowAccessExport { Contributions = [shifting] }));

        (await CanExecuteAsync("authenticated", "desk.on_shift()")).Should().BeFalse("CREATE OR REPLACE keeps a function's grants, so the script takes back what it no longer grants");
        (await CanExecuteAsync("ddd_system_in", "desk.on_shift()")).Should().BeTrue();
        (await CanExecuteAsync("anon", "desk.is_watcher(uuid)")).Should().BeFalse("no policy for the anonymous callers asks it any more");
        (await CanExecuteAsync("authenticated", "desk.is_watcher(uuid)")).Should().BeTrue();

        async Task<bool> CanExecuteAsync(string role, string function)
            => await ScalarAsync<bool>(connection, $"SELECT has_function_privilege('{role}', '{function}', 'EXECUTE')");
    }

    [Fact]
    public async Task A_function_another_modules_policy_still_asks_is_dropped_by_a_later_file_once_nothing_does()
    {
        database.Require();
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await using var yard = YardContext.Create();
        await using var desk = DeskContext.Create();

        // Another test of this class may have left a yard of its own; this one is gone again with the transaction.
        await RunAsync(connection, "DROP SCHEMA IF EXISTS yard CASCADE;");
        await RunAsync(connection, yard.Database.GenerateCreateScript());

        var name = "counted";
        var counting = new SpotContribution("counting", context => context is DeskContext ? new([new ContributedFunction(name, "", "boolean", "SELECT true", GrantTo: [RowAccessRoles.User])], [], []) : null);
        IReadOnlyList<(DbContext Context, string Script)> ScriptsNow()
            => PostgresRowAccess.Scripts(
                [yard, desk],
                [RowAccessRule.For<Crate>("Counted crates", RowOperations.Read, "{fn:counting/" + name + "}()", RowAccessRoles.User)],
                [],
                new RowAccessExport { Contributions = [counting] });

        foreach (var (_, script) in ScriptsNow())
        {
            await RunAsync(connection, script);
        }

        // Renamed, as Postgres needs when a function's parameters or what it returns change: the desk's file comes
        // first, while the yard's policy from the file before still asks the old name.
        name = "recounted";
        var renamed = ScriptsNow();
        renamed.Select(each => each.Context).Should().Equal([desk, yard]);
        var run = async () =>
        {
            foreach (var (_, script) in renamed)
            {
                await RunAsync(connection, script);
            }
        };

        await run.Should().NotThrowAsync("the desk's file leaves the old function to the yard's policy that still asks it, which the yard's file replaces");
        (await ExistsAsync("desk.counted()")).Should().BeTrue("it stays until nothing asks it");
        (await ScalarAsync<string>(connection, "SELECT qual FROM pg_catalog.pg_policies WHERE schemaname = 'yard' AND policyname = 'Counted crates (select) for authenticated'"))
            .Should().Contain("recounted");

        await RunAsync(connection, renamed.Single(each => each.Context == desk).Script);
        (await ExistsAsync("desk.counted()")).Should().BeFalse("the desk's next file drops it, now that no policy asks it");
        (await ExistsAsync("desk.recounted()")).Should().BeTrue();

        async Task<bool> ExistsAsync(string function)
            => await ScalarAsync<bool>(connection, $"SELECT to_regprocedure('{function}') IS NOT NULL");
    }

    [Fact]
    public async Task A_restrictive_policy_is_never_merged()
    {
        database.Require();
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await using (var model = DeskContext.Create())
        {
            await RunAsync(connection, PostgresRowAccess.Script(model, [DeskRules.Owners, DeskRules.Public], [], new RowAccessExport { Contributions = [new DutyRowAccess { OpenOnly = true }] }));
        }

        var policies = new List<(string, string)>();
        await using (var read = new NpgsqlCommand("SELECT policyname::text, permissive FROM pg_catalog.pg_policies WHERE schemaname = 'desk' AND tablename = 'Tickets' AND 'authenticated' = ANY (roles) AND cmd = 'SELECT' ORDER BY policyname", connection))
        await using (var reader = await read.ExecuteReaderAsync(Cancellation))
        {
            while (await reader.ReadAsync(Cancellation))
            {
                policies.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        policies.Should().Equal(
            [("Open tickets only (select) for authenticated", "RESTRICTIVE"), ("Tickets (select) for authenticated", "PERMISSIVE")],
            "the rules and the contribution's permissive policy are one policy, and the restrictive one stays apart");
        (await TitlesAsAsync(connection, $$"""{"sub":"{{PostgresRowAccessDatabase.Bob}}","role":"authenticated"}""")).Should().Equal(
            ["Everyone's"],
            "Bob's rules let him read his own ticket and the public one, and the restrictive policy keeps him to the open one");
        (await TitlesAsAsync(connection, $$"""{"sub":"{{PostgresRowAccessDatabase.Bob}}","role":"authenticated","on_duty":true}""")).Should().BeEquivalentTo(
            ["Alice's, open for the north", "Carol's own", "Everyone's"],
            "on duty, he reads every open ticket through the contribution's set, and still no closed one");
    }

    /// <summary>The titles of the tickets an authenticated caller with <paramref name="claims"/> reads, inside the running transaction.</summary>
    private static async Task<List<string>> TitlesAsAsync(NpgsqlConnection connection, string claims)
    {
        await RunAsync(connection, "RESET ROLE; SET LOCAL ROLE authenticated;");
        await using (var set = new NpgsqlCommand("SELECT set_config('request.jwt.claims', $1, true)", connection))
        {
            set.Parameters.Add(new NpgsqlParameter { Value = claims });
            await set.ExecuteNonQueryAsync(Cancellation);
        }

        var titles = new List<string>();
        await using (var read = new NpgsqlCommand("""SELECT "Title" FROM desk."Tickets" ORDER BY "Title" """, connection))
        await using (var reader = await read.ExecuteReaderAsync(Cancellation))
        {
            while (await reader.ReadAsync(Cancellation))
            {
                titles.Add(reader.GetString(0));
            }
        }

        await RunAsync(connection, "RESET ROLE;");
        return titles;
    }

    [Theory]
    [InlineData("is_member", null)]
    [InlineData("projects.is-member", null)]
    [InlineData("Projects/is_member", null)]
    [InlineData("desk.is_member", "text; DROP TABLE x")]
    public void A_function_that_is_no_function_or_takes_no_sql_types_is_refused(string name, string? parameters)
    {
        var build = () => RowAccessFunction.For<Ticket>(name, "TRUE", owner: null, parameters);

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task A_restrictive_policy_of_your_own_is_left_apart_and_still_restricts()
    {
        database.Require();
        var bob = Callers.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Bob, team: "north"));
        await database.RunAsOwnerAsync("""CREATE POLICY "Only public tickets, by hand" ON desk."Tickets" AS RESTRICTIVE FOR SELECT TO authenticated USING ("IsPublic");""", Cancellation);

        try
        {
            await using var model = DeskContext.Create();
            await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, [DeskRules.Owners, DeskRules.Public]), Cancellation);

            (await PoliciesAsync("Tickets")).Should().Contain(
                [("Tickets (select) for authenticated", "PERMISSIVE"), ("Only public tickets, by hand", "RESTRICTIVE")],
                "the rules that let a caller read are one permissive policy, and the restrictive one is not folded into it");
            (await database.TitlesAsync(bob, Cancellation)).Should().Equal(
                ["Everyone's"],
                "Bob's rules let him read his own ticket and the public one, and the restrictive policy still holds him to the public one");
            (await database.TitlesAsync(Caller.Anonymous, Cancellation)).Should().Equal(["Everyone's"]);
        }
        finally
        {
            await database.RunAsOwnerAsync("""DROP POLICY IF EXISTS "Only public tickets, by hand" ON desk."Tickets";""", Cancellation);
        }
    }

    [Theory]
    [InlineData("BYPASSRLS")]
    [InlineData("SUPERUSER")]
    [InlineData("LOGIN")]
    public async Task The_prelude_refuses_a_system_in_role_that_can_bypass_or_log_in(string power)
    {
        database.Require();
        var role = await PowerfulRoleAsync("desk_prelude_", power);
        await using var model = DeskContext.Create();
        var before = await PoliciesAsync("Tickets");

        var run = () => database.RunAsOwnerAsync(
            PostgresRowAccess.Script(model, [DeskRules.Owners, ScopedWork], [], new RowAccessExport { Roles = RowAccessRoleNames.Default with { SystemIn = role } }),
            Cancellation);

        (await run.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(
            $"The role {role} exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.");
        (await PoliciesAsync("Tickets")).Should().Equal(before, "the script is one transaction, and its drop went back with it");
        (await ScalarAsync<long>($"SELECT count(*) FROM pg_catalog.pg_auth_members WHERE roleid = '{role}'::regrole"))
            .Should().Be(0, "nobody was given the role");
    }

    [Theory]
    [InlineData("BYPASSRLS")]
    [InlineData("SUPERUSER")]
    [InlineData("LOGIN")]
    public async Task SetupScript_refuses_a_system_in_role_that_can_bypass_or_log_in(string power)
    {
        database.Require();
        var role = await PowerfulRoleAsync("desk_setup_", power);

        var run = () => database.RunAsOwnerAsync(
            PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions { SystemInRole = role }, loginRole: PostgresRowAccessDatabase.LoginRole),
            Cancellation);

        (await run.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(
            $"The role {role} exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.");
        (await ScalarAsync<long>($"SELECT count(*) FROM pg_catalog.pg_auth_members WHERE roleid = '{role}'::regrole"))
            .Should().Be(0, "the setup stopped before it granted the role to the application");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("caller")]
    public async Task The_prelude_refuses_a_system_in_role_that_has_an_owners_privileges_or_whose_privileges_a_caller_has(string grant)
    {
        database.Require();
        var (role, message) = await GrantedRoleAsync("desk_prelude_", grant);
        await using var model = DeskContext.Create();
        var before = await PoliciesAsync("Tickets");

        try
        {
            var run = () => database.RunAsOwnerAsync(
                PostgresRowAccess.Script(model, [DeskRules.Owners, ScopedWork], [], new RowAccessExport { Roles = RowAccessRoleNames.Default with { SystemIn = role } }),
                Cancellation);

            (await run.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(message);
            (await PoliciesAsync("Tickets")).Should().Equal(before, "the script is one transaction, and its drop went back with it");
        }
        finally
        {
            await database.RunAsOwnerAsync($"REVOKE {role} FROM authenticated;", Cancellation);
        }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("caller")]
    public async Task SetupScript_refuses_a_system_in_role_that_has_an_owners_privileges_or_whose_privileges_a_caller_has(string grant)
    {
        database.Require();
        var (role, message) = await GrantedRoleAsync("desk_setup_", grant);

        try
        {
            var run = () => database.RunAsOwnerAsync(
                PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions { SystemInRole = role }, loginRole: PostgresRowAccessDatabase.LoginRole),
                Cancellation);

            (await run.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(message);
            (await ScalarAsync<long>($"SELECT count(*) FROM pg_catalog.pg_auth_members WHERE roleid = '{role}'::regrole AND member = '{PostgresRowAccessDatabase.LoginRole}'::regrole"))
                .Should().Be(0, "the setup stopped before it granted the role to the application");
        }
        finally
        {
            await database.RunAsOwnerAsync($"REVOKE {role} FROM authenticated;", Cancellation);
        }
    }

    [Fact]
    public async Task Two_scripts_that_make_the_system_in_role_at_the_same_moment_both_run()
    {
        database.Require();
        const string Raced = "desk_raced";
        await using var model = DeskContext.Create();
        var script = PostgresRowAccess.Script(model, [DeskRules.Owners, ScopedWork], [], new RowAccessExport { Roles = RowAccessRoleNames.Default with { SystemIn = Raced } });

        // Another script, for another database on the server, has made the role and has not committed yet.
        await using var other = await database.OpenAsOwnerAsync(Cancellation);
        await using var otherTransaction = await other.BeginTransactionAsync(Cancellation);
        await RunAsync(other, $"CREATE ROLE {Raced} NOLOGIN NOINHERIT;");

        var run = database.RunAsOwnerAsync(script, Cancellation);
        await UntilWaitingForAnotherTransactionAsync();
        await otherTransaction.CommitAsync(Cancellation);

        await run.WaitAsync(TimeSpan.FromMinutes(1), Cancellation);
        (await database.PolicyNamesAsync("Tickets", Cancellation)).Should().Contain(
            $"Scoped work reads every ticket (select) for {Raced}",
            "this script waited for the other, found the role made, and went on");
    }

    [Fact]
    public async Task A_rule_about_a_derived_type_holds_for_the_rows_of_that_type_only()
    {
        database.Require();
        var plain = Guid.NewGuid();
        var ownerless = Guid.NewGuid();
        var owned = Guid.NewGuid();
        await using var yard = YardContext.Create();

        await database.RunAsOwnerAsync("DROP SCHEMA IF EXISTS yard CASCADE;", Cancellation);
        await database.RunAsOwnerAsync(yard.Database.GenerateCreateScript(), Cancellation);
        await database.RunAsOwnerAsync(
            $"""
            INSERT INTO yard."Crates" ("Id", "Owner", "Discriminator", "Insured") VALUES
                ('{plain}', NULL, 'Crate', NULL),
                ('{ownerless}', NULL, 'ExpressCrate', FALSE),
                ('{owned}', '{PostgresRowAccessDatabase.Alice}', 'ExpressCrate', TRUE);
            INSERT INTO yard."CrateSticker" ("CrateId", "Id", "Text") VALUES
                ('{plain}', gen_random_uuid(), 'on the plain crate'),
                ('{ownerless}', gen_random_uuid(), 'on the ownerless express crate'),
                ('{owned}', gen_random_uuid(), 'on the owned express crate');
            GRANT USAGE ON SCHEMA yard TO anon, authenticated;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA yard TO anon, authenticated;
            """,
            Cancellation);
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(yard, [YardRules.Owners, YardRules.OwnerlessExpress]), Cancellation);

        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        try
        {
            await RunAsync(connection, "SET ROLE anon;");

            (await ScalarAsync<string>(connection, """SELECT string_agg("Discriminator", ',') FROM yard."Crates" """)).Should().Be(
                "ExpressCrate",
                "the plain crate has no owner either, but the rule is about express crates, and of those only the ownerless one is open");
            (await ScalarAsync<string>(connection, """SELECT string_agg("Text", ',') FROM yard."CrateSticker" """)).Should().Be("on the ownerless express crate");
            (await AffectedAsync(connection, """UPDATE yard."CrateSticker" SET "Text" = "Text" || ', changed' """)).Should().Be(
                1, "the stickers are written as the rules let a caller write the crate, each rule about its own type's rows");
        }
        finally
        {
            await RunAsync(connection, "RESET ROLE;");
        }
    }

    [Fact]
    public async Task The_migration_role_can_switch_to_the_system_in_role_it_created()
    {
        database.Require();

        // A role that runs the migrations the way a hosted Supabase project's postgres role does: it may
        // create roles, it owns the database it migrates, and it is no superuser.
        const string Migrator = "desk_migrator";
        const string Scoped = "desk_scoped_work";
        await database.RunAsOwnerAsync($"CREATE ROLE {Migrator} LOGIN CREATEROLE PASSWORD '{Migrator}';", Cancellation);
        await database.RunAsOwnerAsync($"CREATE DATABASE desk_migrated OWNER {Migrator};", Cancellation);

        await using var connection = new NpgsqlConnection(database.ConnectionStringFor(Migrator, "desk_migrated"));
        await connection.OpenAsync(Cancellation);

        // Without the grant the prelude writes, a role it creates is one it may administer but not switch to.
        await RunAsync(connection, "CREATE ROLE desk_created_only NOLOGIN;");
        var unaided = () => RunAsync(connection, "SET ROLE desk_created_only;");
        (await unaided.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        await using var model = DeskContext.Create();
        await RunAsync(connection, model.Database.GenerateCreateScript());
        await using (var owner = DeskContext.Create(database.ConnectionStringFor(Migrator, "desk_migrated")))
        {
            owner.Tickets.Add(new Ticket(TicketId.CreateSequential(), "Migrated", owner: null, team: null, TicketStatus.Open, isPublic: false));
            await owner.SaveChangesAsync(Cancellation);
        }

        var script = PostgresRowAccess.Script(model, [ScopedWork], [], new RowAccessExport { Roles = RowAccessRoleNames.Default with { SystemIn = Scoped } });
        await RunAsync(connection, script);
        await RunAsync(connection, $"GRANT USAGE ON SCHEMA desk TO {Scoped}; GRANT SELECT ON ALL TABLES IN SCHEMA desk TO {Scoped};");

        (await ScalarAsync<string>(connection, $"SELECT concat_ws(',', rolcanlogin, rolbypassrls, rolsuper, rolinherit) FROM pg_catalog.pg_roles WHERE rolname = '{Scoped}'"))
            .Should().Be("f,f,f,f", "the prelude made the role NOLOGIN NOINHERIT, without BYPASSRLS");
        await RunAsync(connection, $"SET ROLE {Scoped};");
        (await ScalarAsync<string>(connection, "SELECT current_user::text")).Should().Be(Scoped, "the prelude granted the migration role the role it created");
        (await ScalarAsync<long>(connection, """SELECT count(*) FROM desk."Tickets" """)).Should().Be(1, "the policy for the scoped role lets it read the tickets");
        await RunAsync(connection, "RESET ROLE;");

        var again = () => RunAsync(connection, script);
        await again.Should().NotThrowAsync("the role and the grant are there now, and a second file leaves them as they are");
    }

    /// <summary>A rule for the scoped system role, which the prelude makes the role for.</summary>
    private static readonly RowAccessRule ScopedWork = RowAccessRule.For<Ticket>("Scoped work reads every ticket", RowOperations.Read, "TRUE", RowAccessRoles.SystemIn);

    /// <summary>A new role named after <paramref name="prefix"/> with <paramref name="power"/>, which the scoped system role must not have.</summary>
    private async Task<string> PowerfulRoleAsync(string prefix, string power)
    {
        var role = prefix + power.ToLowerInvariant();
        await database.RunAsOwnerAsync($"CREATE ROLE {role} {(power == "LOGIN" ? "LOGIN" : "NOLOGIN " + power)};", Cancellation);
        return role;
    }

    /// <summary>
    /// A new role named after <paramref name="prefix"/> and <paramref name="grant"/>, which is harmless on its
    /// own and dangerous by a grant: <c>owner</c>, it is granted the role that owns the desk's tables;
    /// <c>caller</c>, it is granted to <c>authenticated</c>, which inherits its privileges. With the refusal
    /// a script or the setup gives for it.
    /// </summary>
    private async Task<(string Role, string Message)> GrantedRoleAsync(string prefix, string grant)
    {
        var role = prefix + grant;
        if (grant == "owner")
        {
            await database.RunAsOwnerAsync($"CREATE ROLE {role} NOLOGIN; GRANT {PostgresRowAccessDatabase.LoginRole} TO {role};", Cancellation);
            return (role, $"The role {role} has the privileges of a role that owns tables, and Postgres does not hold an owner to row level security. It must neither own a table nor be granted a role that does.");
        }

        await database.RunAsOwnerAsync($"CREATE ROLE {role} NOLOGIN; GRANT {role} TO authenticated WITH INHERIT TRUE;", Cancellation);
        return (role, $"The role {role} is granted to authenticated or anon, so their callers would get every policy written for it. Grant it only to the role the application logs in as.");
    }

    /// <summary>Until a connection to the desk waits for another transaction to end, as one that inserts a name another has inserted and not committed does.</summary>
    private async Task UntilWaitingForAnotherTransactionAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (await ScalarAsync<long>("SELECT count(*) FROM pg_catalog.pg_stat_activity WHERE wait_event_type = 'Lock' AND wait_event = 'transactionid'") == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }
    }

    private static async Task<int> AffectedAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(Cancellation);
    }

    /// <summary>The policies on <c>desk.<paramref name="table"/></c>, by name, with whether each is permissive or restrictive.</summary>
    private async Task<List<(string Name, string Kind)>> PoliciesAsync(string table)
    {
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT policyname::text, permissive FROM pg_catalog.pg_policies WHERE schemaname = 'desk' AND tablename = $1 ORDER BY policyname", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = table });

        var policies = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        while (await reader.ReadAsync(Cancellation))
        {
            policies.Add((reader.GetString(0), reader.GetString(1)));
        }

        return policies;
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Cancellation))!;
    }

    /// <summary>The version of <c>ddd.written_in_this_transaction</c>'s row in the catalog, which changes when it is replaced.</summary>
    private Task<string> FunctionVersionAsync()
        => ScalarAsync<string>("SELECT xmin::text FROM pg_catalog.pg_proc WHERE oid = 'ddd.written_in_this_transaction(xid)'::regprocedure");

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await database.OpenAsOwnerAsync(Cancellation);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Cancellation))!;
    }

    /// <summary>The script for the desk with <paramref name="rules"/>, which reads no database.</summary>
    private static string ScriptFor(params RowAccessRule[] rules)
    {
        using var model = DeskContext.Create();
        return PostgresRowAccess.Script(model, rules, [DeskRules.IsWatcher]);
    }

    /// <summary>The names of the policies <paramref name="script"/> makes on <c>desk.<paramref name="table"/></c>, in the order it makes them.</summary>
    private static List<string> PoliciesOn(string script, string table)
        => [.. Regex.Matches(script, $"^CREATE POLICY \"(?<name>[^\"]+)\" ON desk\\.\"{Regex.Escape(table)}\"", RegexOptions.Multiline).Select(match => match.Groups["name"].Value)];
}
