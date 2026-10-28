using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The help desk in <c>"Help_Desk"</c> on a real Postgres: a schema with capitals and an underscore, which Entity
/// Framework creates in double quotes and Postgres would fold to lower case without them. The policies, the access
/// function the rules ask by its logical name, the column rule's trigger and the privileges are all in that schema,
/// each caller reads what the rules say in C#, a script written again finds what the first made, and the start-up
/// checks look in the schema the model names. <see cref="QuotedSchemaTests"/> reads the same scripts.
/// </summary>
public sealed class QuotedSchemaPostgresTests(HelpDeskDatabase database) : IClassFixture<HelpDeskDatabase>
{
    private const string Tickets = "\"Help_Desk\".\"Tickets\"";

    private const string AlicesTicket = "Alice's, open for the north";

    private static readonly CurrentUserSnapshot Alice = CurrentUserSnapshot.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Alice, team: "north"));

    private static readonly CurrentUserSnapshot Bob = CurrentUserSnapshot.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Bob, team: "north"));

    private static readonly CurrentUserSnapshot Carol = CurrentUserSnapshot.FromClaims(PostgresRowAccessDatabase.ClaimsOf(PostgresRowAccessDatabase.Carol, team: "south"));

    public static TheoryData<string> CallerNames => ["Alice", "Bob", "Carol", "anonymous"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static CurrentUserSnapshot Named(string name) => name switch
    {
        "Alice" => Alice,
        "Bob" => Bob,
        "Carol" => Carol,
        _ => CurrentUserSnapshot.Anonymous,
    };

    [Theory]
    [MemberData(nameof(CallerNames))]
    public async Task Each_caller_reads_what_the_rules_say_in_csharp_in_a_schema_postgres_keeps_only_in_quotes(string name)
    {
        database.Require();
        var caller = Named(name);

        var all = await database.TicketsAsync(CurrentUserSnapshot.System, Cancellation);
        var seen = await database.TicketsAsync(caller, Cancellation);

        all.Should().HaveCount(5, "the application itself is not subject to the rules");
        seen.Select(ticket => ticket.Title).Should().BeEquivalentTo(
            all.Where(ticket => DeskRules.Reads(ticket, caller) || TeammatesWorkOnTheTeamsTickets.Allows(ticket, Answering(caller))).Select(ticket => ticket.Title),
            "Postgres answers what the rules answer, the watchers' through the set the access function in \"Help_Desk\" answers");
    }

    [Fact]
    public async Task A_watcher_reads_the_ticket_through_the_set_function_made_once_in_the_quoted_schema()
    {
        database.Require();

        (await database.TicketsAsync(Carol, Cancellation)).Select(ticket => ticket.Title).Should().Contain("Nobody's", "Carol watches it");
        (await database.TicketsAsync(Bob, Cancellation)).Select(ticket => ticket.Title).Should().NotContain("Nobody's");

        // Made once although the script ran twice, in the schema of the tables, run as its owner, and executable by the
        // roles whose policies ask it.
        (await database.AskAsOwnerAsync(
                """
                SELECT p.oid::pg_catalog.regprocedure::pg_catalog.text || ' ' || p.prosecdef::text
                       || ' ' || pg_catalog.has_function_privilege('authenticated', p.oid, 'EXECUTE')::text
                       || ' ' || pg_catalog.has_function_privilege('anon', p.oid, 'EXECUTE')::text
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'Help_Desk' AND p.proname = 'tickets_i_watch'
                """,
                Cancellation))
            .Should().Equal(["\"Help_Desk\".tickets_i_watch() true true true"]);
        (await database.AskAsOwnerAsync("SELECT pg_catalog.count(*) FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'help_desk'", Cancellation))
            .Should().Equal(["0"], "nothing went into a schema of the name folded to lower case");
    }

    [Fact]
    public async Task A_teammate_changes_the_title_and_not_the_status_which_the_trigger_in_the_quoted_schema_keeps_for_the_owner()
    {
        database.Require();
        try
        {
            (await database.RunAsAsync(Bob, $"UPDATE {Tickets} SET \"Title\" = {{0}} WHERE \"Title\" = {{1}}", Cancellation, AlicesTicket + ", retitled", AlicesTicket))
                .Should().Be(1, "the rules for the row let a teammate change it");

            var closing = () => database.RunAsAsync(Bob, $"UPDATE {Tickets} SET \"Status\" = 1 WHERE \"Title\" = {{0}}", Cancellation, AlicesTicket + ", retitled");
            var refused = (await closing.Should().ThrowAsync<PostgresException>()).Which;
            (refused.SqlState, refused.ConstraintName, refused.MessageText).Should().Be((
                PostgresErrorCodes.InsufficientPrivilege,
                "tickets_status_column_rule",
                $"The column rule 'Owners close their tickets' does not let this caller change \"Status\" of {Tickets}."));

            (await database.RunAsAsync(Alice, $"UPDATE {Tickets} SET \"Status\" = 1 WHERE \"Title\" = {{0}}", Cancellation, AlicesTicket + ", retitled"))
                .Should().Be(1, "the column rule allows the owner");
        }
        finally
        {
            await database.RunAsOwnerAsync(
                $"UPDATE {Tickets} SET \"Title\" = '{AlicesTicket.Replace("'", "''", StringComparison.Ordinal)}', \"Status\" = 0 WHERE \"Owner\" = '{PostgresRowAccessDatabase.Alice}';",
                Cancellation);
        }

        (await database.AskAsOwnerAsync(
                """
                SELECT t.tgname || ' ' || p.oid::pg_catalog.regprocedure::pg_catalog.text
                FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_proc p ON p.oid = t.tgfoid
                JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'Help_Desk' AND NOT t.tgisinternal
                """,
                Cancellation))
            .Should().Equal(["tickets_status_column_rule \"Help_Desk\".tickets_status_column_rule()"], "the trigger and its function were made once, in the schema of the table");
    }

    [Fact]
    public async Task A_script_written_again_drops_and_makes_its_policies_once_in_the_quoted_schema()
    {
        database.Require();

        var policies = await database.AskAsOwnerAsync("SELECT tablename || ': ' || policyname FROM pg_catalog.pg_policies WHERE schemaname = 'Help_Desk' ORDER BY 1", Cancellation);
        policies.Should().Contain("Tickets: Tickets (select) for anon").And.Contain("Tickets: Tickets (update) for authenticated").And.OnlyHaveUniqueItems();

        await database.RunAsOwnerAsync(HelpDeskDatabase.Script(), Cancellation);

        (await database.AskAsOwnerAsync("SELECT tablename || ': ' || policyname FROM pg_catalog.pg_policies WHERE schemaname = 'Help_Desk' ORDER BY 1", Cancellation))
            .Should().Equal(policies, "the script dropped every policy it made, found by the schema the catalog keeps, and made each again");
        (await database.AskAsOwnerAsync("SELECT pg_catalog.has_schema_privilege('authenticated', 'Help_Desk', 'USAGE')::text || ' ' || pg_catalog.has_table_privilege('anon', '\"Help_Desk\".\"Tickets\"', 'SELECT')::text", Cancellation))
            .Should().Equal(["true true"], "the privileges the script writes from the policies name the schema quoted");
    }

    [Fact]
    public async Task A_script_written_again_keeps_what_it_still_writes_in_place_and_drops_what_it_no_longer_writes()
    {
        database.Require();
        const string AnyTicket = "SELECT p.oid::pg_catalog.text FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'Help_Desk' AND p.proname = 'any_ticket'";

        var before = await database.AskAsOwnerAsync(AnyTicket, Cancellation);
        before.Should().ContainSingle("the fixture's scripts made it, and nothing asks it");

        await database.RunAsOwnerAsync(HelpDeskDatabase.Script(), Cancellation);
        (await database.AskAsOwnerAsync(AnyTicket, Cancellation)).Should().Equal(
            before, "a function the script still writes is found by the schema and the name the catalog keeps, and replaced in place rather than dropped");

        try
        {
            await database.RunAsOwnerAsync(HelpDeskDatabase.Script(withAnyTicket: false), Cancellation);
            (await database.AskAsOwnerAsync(AnyTicket, Cancellation)).Should().BeEmpty("one it no longer writes, which nothing asks, is dropped");
        }
        finally
        {
            await database.RunAsOwnerAsync(HelpDeskDatabase.Script(), Cancellation);
        }
    }

    [Fact]
    public async Task The_login_role_check_finds_the_schema_the_model_names_capitals_and_all()
    {
        database.Require();
        await using var context = database.CreateContext(CurrentUserSnapshot.System);
        await PostgresRowAccessChecks.EnsureLoginRoleMaySwitchToUserRolesAsync(context, Cancellation);

        // The login role owns the schema, which the check finds by the name the catalog keeps, and names as SQL writes it.
        (await FluentActions.Awaiting(() => PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"The role '{HelpDeskDatabase.LoginRole}' that 'HelpDeskContext' logs in as is meant to hold nothing, and it does:\n- it owns the schema \"Help_Desk\" and what the migrations made there*");
    }

    [Fact]
    public async Task The_definer_owner_check_finds_a_function_in_the_schema_the_model_names_once_a_table_there_is_forced()
    {
        database.Require();
        await using var context = database.CreateContext(CurrentUserSnapshot.System);
        const string Forced = "SELECT c.relname FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'Help_Desk' AND c.relforcerowsecurity ORDER BY 1";

        try
        {
            // Forced by the script, on the tables of the schema it names in double quotes. The superuser made the
            // functions, and a superuser is held to no policy, so there is nothing to say.
            await database.RunAsOwnerAsync(HelpDeskDatabase.Script(force: true), Cancellation);
            (await database.AskAsOwnerAsync(Forced, Cancellation)).Should().Contain("Tickets");
            await PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(context, Cancellation);

            // Handed to the login role, which may not bypass row level security, the function would read the forced
            // tables under its owner's policies. The check finds it by the schema the catalog keeps, and names it as SQL does.
            await database.RunAsOwnerAsync($"ALTER FUNCTION \"Help_Desk\".tickets_i_watch() OWNER TO {HelpDeskDatabase.LoginRole};", Cancellation);
            (await FluentActions.Awaiting(() => PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(context, Cancellation))
                    .Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Be(
                    "Functions in the schemas of 'HelpDeskContext' run as their owner, and their owner is held to row level security, so on a table that forces it they would read no row:\n" +
                    $"- \"Help_Desk\".tickets_i_watch() is owned by {HelpDeskDatabase.LoginRole}. Fix: ALTER FUNCTION \"Help_Desk\".tickets_i_watch() OWNER TO <a role with BYPASSRLS>; or ALTER ROLE {HelpDeskDatabase.LoginRole} BYPASSRLS;");
        }
        finally
        {
            // As the other tests find the desk: the function the superuser's, and nothing forced, which a script does not take back.
            await database.RunAsOwnerAsync(
                """
                ALTER FUNCTION "Help_Desk".tickets_i_watch() OWNER TO CURRENT_USER;
                DO $$
                DECLARE
                    t record;
                BEGIN
                    FOR t IN SELECT c.relname FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                             WHERE n.nspname = 'Help_Desk' AND c.relforcerowsecurity LOOP
                        EXECUTE format('ALTER TABLE %I.%I NO FORCE ROW LEVEL SECURITY', 'Help_Desk', t.relname);
                    END LOOP;
                END
                $$;
                """,
                Cancellation);
            await database.RunAsOwnerAsync(HelpDeskDatabase.Script(), Cancellation);
        }

        (await database.AskAsOwnerAsync(Forced, Cancellation)).Should().BeEmpty();
        await PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(context, Cancellation);
    }
}
