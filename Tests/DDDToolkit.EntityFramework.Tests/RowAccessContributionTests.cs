using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Row level security a package or a module contributes: functions, policies and statements written from each
/// context's model, which a script writes next to the rules' policies, merged with them where they are for the
/// same table, command and role. And what every script now does with the functions it writes: it creates each
/// after those it asks, and grants it only to the roles that ask it. None of this opens a database.
/// </summary>
public sealed class RowAccessContributionTests
{
    private static RowAccessExport With(params IRowAccessContribution[] contributions) => new() { Contributions = contributions };

    [Fact]
    public void A_contribution_is_asked_per_context_and_null_writes_nothing()
    {
        using var desk = DeskContext.Create();
        using var yard = YardContext.Create();
        var duty = new DutyRowAccess();
        var recording = new SpotContribution("duty", context => duty.Contribute(context, new RowAccessExport()));

        var scripts = PostgresRowAccess.Scripts([desk, yard], [YardRules.Owners], [], With(recording));

        recording.Asked.Should().Equal([typeof(DeskContext), typeof(YardContext)], "a script asks every contribution about every context it writes, once");
        scripts.Single(each => each.Context == desk).Script.Should().Contain("CREATE OR REPLACE FUNCTION desk.on_duty() RETURNS boolean");
        scripts.Single(each => each.Context == yard).Script.Should().Be(
            PostgresRowAccess.Script(yard, [YardRules.Owners]),
            "the yard maps no tickets, so the contribution answers null there, and its script is what the rules alone make");
    }

    [Fact]
    public void Every_table_a_contributed_policy_names_has_row_level_security()
    {
        using var desk = DeskContext.Create();
        var watchers = new SpotContribution("watching", context => new(
            [],
            [new ContributedPolicy(SpotContribution.Of<TicketWatcher>(context), "Watchers see themselves", "SELECT", RowAccessRoles.User, "\"User\" = {caller:uid}", null)],
            [],
            [SpotContribution.Of<CommentReaction>(context)]));

        var script = PostgresRowAccess.Script(desk, [], [], With(new DutyRowAccess(), watchers));

        script.Should().Contain("ALTER TABLE desk.\"Tickets\" ENABLE ROW LEVEL SECURITY;", "the duty's policy is on the tickets");
        script.Should().Contain("ALTER TABLE desk.\"TicketWatcher\" ENABLE ROW LEVEL SECURITY;", "a table of an aggregate's entities, which no rule of the script is about");
        script.Should().Contain("ALTER TABLE desk.\"CommentReaction\" ENABLE ROW LEVEL SECURITY;", "a table a contribution keeps to itself has it too, with or without a policy");
        script.Should().Contain(
            "-- Watchers see themselves (select) for authenticated asks the policy 'Watchers see themselves' of the row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.SpotContribution in ");
        script.Should().Contain(
            "CREATE POLICY \"Watchers see themselves (select) for authenticated\" ON desk.\"TicketWatcher\" FOR SELECT TO authenticated\n" +
            "    USING (\"User\" = (SELECT ddd.caller_id()));\n" +
            "COMMENT ON POLICY \"Watchers see themselves (select) for authenticated\" ON desk.\"TicketWatcher\" IS 'DDDToolkit row access rule';",
            "a contributed policy is named like a rule's, and carries the comment the next script drops it by");
    }

    [Fact]
    public void Contributed_permissive_policies_merge_with_rules_and_restrictive_ones_stay_apart()
    {
        using var desk = DeskContext.Create();

        var script = PostgresRowAccess.Script(desk, [DeskRules.Public], [], With(new DutyRowAccess { OpenOnly = true }));

        script.Should().Contain(
            "-- Tickets (select) for authenticated asks the rule 'Public tickets are everyones' and the policy 'On duty reads the open tickets' of the row access contribution " + DutyRowAccess.Source + ": a row one of them allows is allowed.\n" +
            "CREATE POLICY \"Tickets (select) for authenticated\" ON desk.\"Tickets\" FOR SELECT TO authenticated\n" +
            "    USING ((\"IsPublic\") OR (\"Id\" = ANY (ARRAY(SELECT desk.duty_tickets()))));",
            "the rule and the contribution allow authenticated to read in one permissive policy");
        script.Should().Contain(
            "CREATE POLICY \"Public tickets are everyones (select) for anon\" ON desk.\"Tickets\" FOR SELECT TO anon\n    USING (\"IsPublic\");",
            "anon has the rule alone, whose policy keeps the rule's name");
        script.Should().Contain(
            "CREATE POLICY \"Open tickets only (select) for authenticated\" ON desk.\"Tickets\" AS RESTRICTIVE FOR SELECT TO authenticated\n" +
            "    USING (\"Status\" = 0);",
            "a restrictive policy is a policy of its own, and narrows what the merged one allows");
        Regex.Matches(script, "^CREATE POLICY .* ON desk\\.\"Tickets\" .*FOR SELECT TO authenticated$", RegexOptions.Multiline).Should().HaveCount(2);
    }

    [Fact]
    public void A_rule_on_a_table_a_contribution_keeps_to_itself_is_refused()
    {
        using var desk = DeskContext.Create();
        var keeper = new SpotContribution("keeper", context => new([], [], [], [SpotContribution.Of<Ticket>(context)]));

        var rule = () => PostgresRowAccess.Script(desk, [DeskRules.Public], [], With(keeper));
        var another = () => PostgresRowAccess.Script(desk, [], [], With(keeper, new DutyRowAccess()));
        var entity = () => PostgresRowAccess.Script(desk, [DeskRules.Public], [], With(new SpotContribution("keeper", context => new([], [], [], [SpotContribution.Of<TicketComment>(context)]))));

        rule.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Public tickets are everyones' would add a policy to desk.Tickets, which the row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.SpotContribution keeps to itself*");
        another.Should().Throw<InvalidOperationException>().WithMessage(
            "The row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.DutyRowAccess writes the policy 'On duty reads the open tickets' on desk.Tickets, which the row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.SpotContribution keeps to itself*");
        entity.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Public tickets are everyones' would add a policy to desk.TicketComment*",
            "a rule adds the policies of its aggregate's entities too");
    }

    [Fact]
    public void A_contributed_function_is_resolved_by_its_owners_logical_name()
    {
        using var desk = DeskContext.Create();
        using var yard = YardContext.Create();
        var cratesOnDuty = RowAccessRule.For<Crate>("Crates on duty", RowOperations.Read, "{fn:duty/on_duty}()", RowAccessRoles.User);
        var export = With(new DutyRowAccess());

        var scripts = PostgresRowAccess.Scripts([yard, desk], [cratesOnDuty], [], export);

        PostgresRowAccess.FunctionNamesOf([yard, desk], [], export).Should().Equal(
            new Dictionary<string, string> { [DutyRowAccess.OnDuty] = "desk.on_duty", [DutyRowAccess.DutyTickets] = "desk.duty_tickets" },
            "a contributed function lives in the default schema of the context the contribution answered for");
        scripts.Select(each => each.Context).Should().Equal([desk, yard], "the yard asks a function the desk's contribution writes");
        scripts[1].Script.Should().Contain("FOR SELECT TO authenticated\n    USING (desk.on_duty());", "the rule asks it by its logical name, and gets the name it has in the database");
        scripts[0].Script.Should().Contain("AND desk.on_duty()\n$function$;", "the contribution's own body asks it the same way");
    }

    private static readonly string TicketsSeen = ResourceAccessAnswer.NameOf(typeof(TicketId), ResourceAccessSet.Seen);

    private static readonly string TicketsHeldOn = ResourceAccessAnswer.NameOf(typeof(TicketId), ResourceAccessSet.HeldOn);

    /// <summary>
    /// A contribution that answers the tickets seen and those a key is held on, under names of its own, for rules
    /// that ask by the ticket's id; with <paramref name="also"/>, a third function that answers the tickets seen again.
    /// It answers for <paramref name="only"/> alone where one is given.
    /// </summary>
    private static SpotContribution Following(string owner = "following", string name = "tickets_followed", bool also = false, Type? only = null) => new(owner, context =>
    {
        if (context.Model.FindEntityType(typeof(Ticket)) is not { } tickets || (only is not null && context.GetType() != only))
        {
            return null;
        }

        var ids = "SETOF " + RowAccessModel.ColumnType(tickets, nameof(Ticket.Id));
        var all = $"SELECT t.{RowAccessModel.Column(tickets, nameof(Ticket.Id))} FROM {RowAccessModel.Table(tickets)} t";
        List<ContributedFunction> functions =
        [
            new(name, "", ids, all, SecurityDefiner: true, GrantTo: [RowAccessRoles.User], Answers: new(typeof(TicketId), ResourceAccessSet.Seen)),
            new(name + "_with", "key text", ids, all + " WHERE $1 IS NOT NULL", SecurityDefiner: true, GrantTo: [RowAccessRoles.User], Answers: new(typeof(TicketId), ResourceAccessSet.HeldOn)),
        ];
        if (also)
        {
            functions.Add(new("tickets_seen_too", "", ids, all, SecurityDefiner: true, GrantTo: [RowAccessRoles.User], Answers: new(typeof(TicketId), ResourceAccessSet.Seen)));
        }

        return new(functions, [], []);
    });

    [Fact]
    public void A_rule_asks_a_set_by_the_resources_id_and_gets_the_function_a_contribution_says_answers_it()
    {
        using var desk = DeskContext.Create();
        using var yard = YardContext.Create();
        var seen = RowAccessRule.For<Crate>("Crates of tickets I see", RowOperations.Read, $"({{col:Id}} = ANY (ARRAY(SELECT {{fn:{TicketsSeen}}}())))", RowAccessRoles.User);
        var held = RowAccessRule.For<Ticket>("Tickets I may close", RowOperations.Change, $"({{col:Id}} = ANY (ARRAY(SELECT {{fn:{TicketsHeldOn}}}('tickets.close'))))", RowAccessRoles.User);
        var export = With(Following());

        var scripts = PostgresRowAccess.Scripts([yard, desk], [seen, held], [], export);

        TicketsSeen.Should().Be("@DDDToolkit.EntityFramework.Tests.Infrastructure.TicketId/seen", "a set is named by the full name of the resource's id, and the set");
        PostgresRowAccess.FunctionNamesOf([yard, desk], [], export).Should().Equal(
            new Dictionary<string, string>
            {
                ["following/tickets_followed"] = "desk.tickets_followed",
                ["following/tickets_followed_with"] = "desk.tickets_followed_with",
                [TicketsSeen] = "desk.tickets_followed",
                [TicketsHeldOn] = "desk.tickets_followed_with",
            },
            "a set is another name for the function that answers it, wherever that function lives");
        scripts.Select(each => each.Context).Should().Equal([desk, yard], "the yard asks a set the desk's contribution answers, so the desk's script runs first");
        scripts[1].Script.Should().Contain("FOR SELECT TO authenticated\n    USING (\"Id\" = ANY (ARRAY(SELECT desk.tickets_followed())));", "the rule names no function, and gets the one that answers the set");
        scripts[0].Script.Should().Contain("(\"Id\" = ANY (ARRAY(SELECT desk.tickets_followed_with('tickets.close'))))", "the same in the module of the resource itself");
        scripts[0].Script.Should().Contain("GRANT EXECUTE ON FUNCTION desk.tickets_followed_with(key text) TO authenticated;")
            .And.NotContain("@DDDToolkit", "the name a rule asks by never reaches the database");
    }

    [Fact]
    public void SQL_a_contribution_writes_asks_a_set_the_same_way()
    {
        using var desk = DeskContext.Create();
        var asking = new SpotContribution("asking", _ => new([], [], [$"SELECT {{fn:{TicketsHeldOn}}}('tickets.close')"]));

        var script = PostgresRowAccess.Script(desk, [], [], With(Following(), asking));

        script.Should().Contain("SELECT desk.tickets_followed_with('tickets.close');");
    }

    [Fact]
    public void A_rule_that_asks_a_set_no_contribution_answers_is_refused_naming_the_rule_and_the_resource()
    {
        using var yard = YardContext.Create();
        var seen = RowAccessRule.For<Crate>("Crates of tickets I see", RowOperations.Read, $"({{col:Id}} = ANY (ARRAY(SELECT {{fn:{TicketsSeen}}}())))", RowAccessRoles.User);

        var refused = () => PostgresRowAccess.Script(yard, [seen], [], With());

        refused.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Crates of tickets I see' asks the resources the caller sees, by the id DDDToolkit.EntityFramework.Tests.Infrastructure.TicketId, "
            + "and no row access contribution answers it for the contexts this is written with. Hand the script the contribution that keeps that resource's access in RowAccessExport.Contributions, the Membership package's for a resource with members*");
    }

    [Fact]
    public void A_context_written_alone_whose_rule_asks_a_set_another_context_answers_hears_how_to_write_it()
    {
        using var desk = DeskContext.Create();
        using var yard = YardContext.Create();
        var seen = RowAccessRule.For<Crate>("Crates of tickets I see", RowOperations.Read, $"({{col:Id}} = ANY (ARRAY(SELECT {{fn:{TicketsSeen}}}())))", RowAccessRoles.User);
        var export = With(Following());

        // The contribution is listed, and answers for the desk, which maps the tickets: the yard alone cannot know its name.
        var alone = () => PostgresRowAccess.Script(yard, [seen], [], export);
        var told = new RowAccessExport { Contributions = export.Contributions, FunctionNames = PostgresRowAccess.FunctionNamesOf([yard, desk], [], export) };

        alone.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Crates of tickets I see' asks the resources the caller sees, *Where it answers for another context, the one that maps the resource, "
            + "write the contexts together with PostgresRowAccess.Scripts, or hand this script that context's names in RowAccessExport.FunctionNames, from PostgresRowAccess.FunctionNamesOf(contexts, functions, export).",
            "the host listed the contribution already, so what is missing is the context that keeps the resource");
        PostgresRowAccess.Script(yard, [seen], [], told).Should().Contain(
            "USING (\"Id\" = ANY (ARRAY(SELECT desk.tickets_followed())));", "handed the desk's names, the yard's script asks the desk's function");
    }

    [Fact]
    public void An_answer_that_cannot_be_named_still_prints_for_the_message_that_shows_it()
    {
        var noSet = new ResourceAccessAnswer(typeof(TicketId), (ResourceAccessSet)7);
        var noKey = new ResourceAccessAnswer(null!, ResourceAccessSet.Seen);
        var function = new ContributedFunction("tickets_somehow", "", "SETOF uuid", "SELECT NULL::uuid", Answers: noSet);

        noSet.ToString().Should().Be("ResourceAccessAnswer { Key = DDDToolkit.EntityFramework.Tests.Infrastructure.TicketId, Set = 7 }");
        noKey.ToString().Should().Be("ResourceAccessAnswer { Key = , Set = Seen }");
        function.ToString().Should().Contain(
            "Answers = ResourceAccessAnswer { Key = DDDToolkit.EntityFramework.Tests.Infrastructure.TicketId, Set = 7 }",
            "a log line or an assertion that prints the function shows what it says it answers");
        new ResourceAccessAnswer(typeof(TicketId), ResourceAccessSet.Seen).Should().Be(
            new ResourceAccessAnswer(typeof(TicketId), ResourceAccessSet.Seen), "an answer is its key and its set");
    }

    [Fact]
    public void One_function_answers_a_set_for_a_resource()
    {
        using var desk = DeskContext.Create();
        using var anotherDesk = DeskScaleContext.Create();

        var twice = () => PostgresRowAccess.Script(desk, [], [], With(Following(also: true)));
        var twoContributions = () => PostgresRowAccess.Script(desk, [], [], With(Following(), Following("watching", "tickets_watched")));
        var twoContexts = () => PostgresRowAccess.Scripts([desk, anotherDesk], [], [], With(Following(only: typeof(DeskContext)), Following("scaled", only: typeof(DeskScaleContext))));

        twice.Should().Throw<InvalidOperationException>().WithMessage(
            "The row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.SpotContribution answers the resources the caller sees, by the id *TicketId with two functions for DeskContext.*");
        twoContributions.Should().Throw<InvalidOperationException>().WithMessage("The row access contributions * and * both answer the resources the caller sees*");
        twoContexts.Should().Throw<InvalidOperationException>().WithMessage(
            "Two contexts answer the resources the caller sees, by the id *TicketId: DeskContext with desk.tickets_followed and DeskScaleContext with desk_scale.tickets_followed.*",
            "a rule that asks the set would otherwise be written with whichever came last");
    }

    [Theory]
    [InlineData(ResourceAccessSet.Seen, "key text", "SETOF uuid", "takes (key text), where the resources seen are asked without arguments")]
    [InlineData(ResourceAccessSet.HeldOn, "", "SETOF uuid", "takes (), where the resources a key is held on are asked with the key alone, as text")]
    [InlineData(ResourceAccessSet.HeldOn, "key text, more text", "SETOF uuid", "takes (key text, more text), where*")]
    [InlineData(ResourceAccessSet.HeldOn, "level integer", "SETOF uuid", "takes (level integer), where*")]
    [InlineData(ResourceAccessSet.Seen, "", "boolean", "returns boolean, where a set is answered with the resource's ids, SETOF their type")]
    [InlineData((ResourceAccessSet)7, "", "SETOF uuid", "'7' is no set: a resource access set is Seen or HeldOn")]
    public void A_function_that_cannot_answer_the_set_it_says_it_answers_is_refused(ResourceAccessSet set, string parameters, string returns, string problem)
    {
        using var desk = DeskContext.Create();
        var wrong = new SpotContribution("wrong", _ => new(
            [new("tickets_somehow", parameters, returns, "SELECT NULL::uuid", SecurityDefiner: true, GrantTo: [RowAccessRoles.User], Answers: new(typeof(TicketId), set))], [], []));

        var refused = () => PostgresRowAccess.Script(desk, [], [], With(wrong));

        refused.Should().Throw<InvalidOperationException>().WithMessage("The function 'tickets_somehow' of the row access contribution * says it answers *, and " + problem + ".");
    }

    [Fact]
    public void A_contributed_definer_statement_without_search_path_is_refused()
    {
        using var desk = DeskContext.Create();
        var careless = new SpotContribution("careless", _ => new([], [], ["CREATE OR REPLACE FUNCTION desk.careless() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER AS $body$ BEGIN RETURN NEW; END $body$"]));
        var careful = new SpotContribution("careful", _ => new([], [], ["CREATE OR REPLACE FUNCTION desk.careful() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$ BEGIN RETURN NEW; END $body$"]));

        var refused = () => PostgresRowAccess.Script(desk, [], [], With(careless));

        refused.Should().Throw<InvalidOperationException>().WithMessage(
            "The row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.SpotContribution has a statement that makes a SECURITY DEFINER function without SET search_path = ''*Pin it in the same statement: SET search_path = ''.");
        var publicPath = () => PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("careless", _ => new([], [], ["CREATE OR REPLACE FUNCTION desk.careless() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $body$ BEGIN RETURN NEW; END $body$"]))));
        publicPath.Should().Throw<InvalidOperationException>().WithMessage("*without SET search_path = ''*", "a search path other than the empty one is still the caller's to fill");
        var notATrigger = () => PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("careless", _ => new([], [], ["CREATE OR REPLACE FUNCTION desk.anything() RETURNS SETOF uuid LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $body$ SELECT t.\"Id\" FROM desk.\"Tickets\" t $body$"]))));
        notATrigger.Should().Throw<InvalidOperationException>().WithMessage(
            "The row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.SpotContribution has a statement that makes a SECURITY DEFINER function that is not a trigger's, which the script would leave executable by every role. Write it as a ContributedFunction*",
            "a statement's function gets no grants, and only a trigger's cannot be called on its own");
        PostgresRowAccess.Script(desk, [], [], With(careful)).Should().EndWith(
            "-- Written by the row access contribution " + DutyRowAccess.Source.Replace(nameof(DutyRowAccess), nameof(SpotContribution)) + ".\n" +
            "CREATE OR REPLACE FUNCTION desk.careful() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$ BEGIN RETURN NEW; END $body$;\n",
            "a statement that pins its search path is written as it is, last, under a comment that names its contribution");
    }

    [Fact]
    public void A_packages_contribution_the_build_made_is_named_by_the_packages_class_and_assembly_without_a_version()
    {
        using var desk = DeskContext.Create();
        const string Careful = "CREATE OR REPLACE FUNCTION desk.careful() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$ BEGIN RETURN NEW; END $body$";
        var made = new MadeByTheBuild(new PackageSpot<Ticket>("careful", _ => new([], [], [Careful])));

        PostgresRowAccess.Script(desk, [], [], With(made)).Should().EndWith(
            "-- Written by the row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.PackageSpot<DDDToolkit.EntityFramework.Tests.Infrastructure.Ticket> "
            + "in DDDToolkit.EntityFramework.Tests.\n" + Careful + ";\n",
            "the class the build wrote is the application's, whose version changes with each of its releases: the comment names what wrote the SQL, the package's class closed "
            + "as C# writes it, and its assembly, and a new access file follows a new version of the package only where the SQL it writes changed");

        var careless = () => PostgresRowAccess.Script(desk, [], [], With(new MadeByTheBuild(new PackageSpot<Ticket>("careless", _ => new([], [], [Careful.Replace(" SET search_path = ''", "", StringComparison.Ordinal)])))));
        careless.Should().Throw<InvalidOperationException>().WithMessage(
            "The row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.PackageSpot<DDDToolkit.EntityFramework.Tests.Infrastructure.Ticket> has a statement*",
            "a message names the package's class too, which is what a developer can look up");
    }

    [Fact]
    public void Contributed_definer_functions_pin_their_search_path_and_run_as_the_caller_by_default()
    {
        using var desk = DeskContext.Create();

        var script = PostgresRowAccess.Script(desk, [], [], With(new DutyRowAccess()));

        script.Should().Contain(
            "-- Written by the row access contribution " + DutyRowAccess.Source + ".\n" +
            "CREATE OR REPLACE FUNCTION desk.on_duty() RETURNS boolean\n" +
            "    LANGUAGE sql STABLE SET search_path = '' AS $function$\n" +
            "SELECT coalesce(((SELECT ddd.caller_claims()) ->> 'on_duty')::boolean, false)\n" +
            "$function$;\n" +
            "COMMENT ON FUNCTION desk.on_duty() IS 'DDDToolkit access function of DeskContext';\n",
            "a contributed function runs as its caller unless it says otherwise, and carries the context's function comment, so the next script keeps it");
        script.Should().Contain(
            "CREATE OR REPLACE FUNCTION desk.duty_tickets() RETURNS SETOF uuid\n" +
            "    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$\n");
        script.Should().Contain("WHERE d.description = 'DDDToolkit access function of DeskContext'\n          AND (n.nspname, p.proname) NOT IN (('desk', 'duty_tickets'), ('desk', 'on_duty'))",
            "the functions the contribution still writes are kept, by the names they have in the database");
    }

    [Fact]
    public void An_inlinable_function_is_written_without_settings()
    {
        using var desk = DeskContext.Create();
        const string Rows = "TABLE (\"Id\" uuid, \"Team\" text)";
        const string OpenTickets = "-- The open ones; a ';' in a comment, or in 'a literal; such as this', ends nothing.\nSELECT t.\"Id\", t.\"Team\" FROM desk.\"Tickets\" t WHERE t.\"Status\" = 0 AND {fn:reading/staffed}(t.\"Team\")";
        var reading = new SpotContribution("reading", _ => new(
            [
                new ContributedFunction("open_tickets", "", Rows, OpenTickets, GrantTo: [RowAccessRoles.User], Inlinable: true),
                new ContributedFunction("open_tickets_apart", "", Rows, OpenTickets, GrantTo: [RowAccessRoles.User]),
                new ContributedFunction(
                    "staffed",
                    "team text",
                    "boolean",
                    "WITH staffed AS (SELECT pg_catalog.unnest(ARRAY['support', 'sales']) AS team) SELECT $1 IN (SELECT staffed.team FROM staffed);",
                    GrantTo: [RowAccessRoles.User],
                    Volatility: "immutable",
                    Inlinable: true),
            ],
            [],
            []));

        var script = PostgresRowAccess.Script(desk, [], [], With(reading));

        script.Should().Contain(
            "CREATE OR REPLACE FUNCTION desk.open_tickets() RETURNS " + Rows + "\n" +
            "    LANGUAGE sql STABLE AS $function$\n" +
            OpenTickets.Replace("{fn:reading/staffed}", "desk.staffed") + "\n" +
            "$function$;\n" +
            "COMMENT ON FUNCTION desk.open_tickets() IS 'DDDToolkit access function of DeskContext';\n" +
            "REVOKE ALL ON FUNCTION desk.open_tickets() FROM PUBLIC;\n",
            "a function to fold into the queries that call it has no SET clause, which Postgres would not fold, and is otherwise written, commented and kept from every role as any other");
        script.Should().Contain("CREATE OR REPLACE FUNCTION desk.staffed(team text) RETURNS boolean\n    LANGUAGE sql IMMUTABLE AS $function$\n", "an IMMUTABLE one, whose body starts with WITH, is folded too");
        script.Should().Contain(
            "CREATE OR REPLACE FUNCTION desk.open_tickets_apart() RETURNS " + Rows + "\n    LANGUAGE sql STABLE SET search_path = '' AS $function$\n",
            "the same function without Inlinable keeps its empty search path");
        script.Should().Contain("GRANT EXECUTE ON FUNCTION desk.open_tickets() TO authenticated;\n", "its grants are its GrantTo, as any contributed function's");
        script.IndexOf("CREATE OR REPLACE FUNCTION desk.staffed(team text)", StringComparison.Ordinal)
            .Should().BeLessThan(script.IndexOf("CREATE OR REPLACE FUNCTION desk.open_tickets()", StringComparison.Ordinal), "and it is created after the functions it asks");

        // It runs as its caller, so what it asks, its callers must be allowed to ask.
        var askingForAnon = () => PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("reading", _ => new(
            [
                new ContributedFunction("open_tickets", "", Rows, OpenTickets, GrantTo: [RowAccessRoles.User, RowAccessRoles.Anonymous], Inlinable: true),
                new ContributedFunction("staffed", "team text", "boolean", "SELECT true", GrantTo: [RowAccessRoles.User]),
            ],
            [],
            []))));
        askingForAnon.Should().Throw<InvalidOperationException>().WithMessage("The function reading/open_tickets, which runs as its caller, asks reading/staffed for anon, which that function is not granted to.*");
    }

    [Fact]
    public void An_inlinable_definer_or_volatile_function_is_refused()
    {
        using var desk = DeskContext.Create();
        const string Select = "SELECT t.\"Id\" FROM desk.\"Tickets\" t";

        Problem(new ContributedFunction("ids", "", "SETOF uuid", Select, SecurityDefiner: true, Inlinable: true)).Should().Be(
            "The function 'ids' of the row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.SpotContribution is Inlinable, to be folded into the queries that call it, " +
            "but runs as its owner, and such a function keeps SET search_path = '', which Postgres does not fold. Leave Inlinable off, or write it as a function that can be folded.",
            "a function that runs as its owner always pins its search path");
        Problem(new ContributedFunction("ids", "", "SETOF uuid", Select, Volatility: "volatile", Inlinable: true)).Should().Contain("is Inlinable").And.Contain("but is VOLATILE");

        foreach (var body in new[]
                 {
                     "UPDATE desk.\"Tickets\" SET \"Team\" = 'x' RETURNING \"Id\"",
                     "VALUES (gen_random_uuid())",
                     "-- SELECT is only in the comment\nTABLE desk.\"Tickets\"",
                     "/* SELECT */ INSERT INTO desk.\"Tickets\" DEFAULT VALUES RETURNING \"Id\"",
                     Select + "; " + Select,
                 })
        {
            Problem(new ContributedFunction("ids", "", "SETOF uuid", body, Inlinable: true)).Should().Contain("is Inlinable").And.Contain("but has a body that is not one SELECT", body);
        }

        // One SELECT, however it is written: in parentheses, after a comment, ended once.
        foreach (var body in new[] { "(" + Select + ")", "/* every ticket; all of them */\n  select t.\"Id\" from desk.\"Tickets\" t where t.\"Team\" <> ';' ;\n", "WITH t AS (" + Select + ") SELECT * FROM t" })
        {
            PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("x", _ => new([new ContributedFunction("ids", "", "SETOF uuid", body, Inlinable: true)], [], []))))
                .Should().Contain("CREATE OR REPLACE FUNCTION desk.ids() RETURNS SETOF uuid\n    LANGUAGE sql STABLE AS $function$\n", body);
        }

        // Without Inlinable none of this is asked: a function apart may run as its owner, change rows, or be more than a SELECT.
        PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("x", _ => new([new ContributedFunction("ids", "", "SETOF uuid", "VALUES (gen_random_uuid())", SecurityDefiner: true, Volatility: "VOLATILE")], [], []))))
            .Should().Contain("LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path = '' AS $function$\n");

        string Problem(ContributedFunction function)
        {
            var script = () => PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("x", _ => new([function], [], []))));
            return script.Should().Throw<InvalidOperationException>().Which.Message;
        }
    }

    [Fact]
    public void A_function_that_calls_one_later_in_the_alphabet_is_written_after_it()
    {
        using var desk = DeskContext.Create();

        var script = PostgresRowAccess.Script(desk, [], [DeskRules.IsWatcher], With(new DutyRowAccess()));

        script.IndexOf("CREATE OR REPLACE FUNCTION desk.on_duty()", StringComparison.Ordinal)
            .Should().BeLessThan(script.IndexOf("CREATE OR REPLACE FUNCTION desk.duty_tickets()", StringComparison.Ordinal), "duty_tickets asks on_duty, so on_duty comes first");
        script.IndexOf("CREATE OR REPLACE FUNCTION desk.is_watcher(uuid)", StringComparison.Ordinal)
            .Should().BeLessThan(script.IndexOf("CREATE OR REPLACE FUNCTION desk.on_duty()", StringComparison.Ordinal), "the rest keep the order of their names");
    }

    [Fact]
    public void Functions_that_ask_each_other_in_a_circle_are_refused()
    {
        using var desk = DeskContext.Create();
        var circle = new SpotContribution("circle", _ => new(
            [
                new ContributedFunction("first", "", "boolean", "SELECT {fn:circle/second}()"),
                new ContributedFunction("second", "", "boolean", "SELECT {fn:circle/first}()"),
            ],
            [],
            []));

        var script = () => PostgresRowAccess.Script(desk, [], [], With(circle));

        script.Should().Throw<InvalidOperationException>().WithMessage(
            "The functions of DeskContext ask each other in a circle, desk.first asks desk.second asks desk.first: Postgres checks a function's body when it creates it*");
    }

    [Fact]
    public void Functions_are_revoked_from_public_and_anonymous_and_granted_to_the_roles_that_ask_them()
    {
        using var desk = DeskContext.Create();
        var scopedWatchers = RowAccessRule.For<Ticket>("Scoped work reads watched tickets", RowOperations.Read, "{call:desk.is_watcher}", RowAccessRoles.SystemIn);

        var script = PostgresRowAccess.Script(desk, [DeskRules.Watchers, scopedWatchers], [DeskRules.IsWatcher, DeskRules.ForTeam], With(new DutyRowAccess()));

        script.Should().Contain(
            "COMMENT ON FUNCTION desk.is_watcher(uuid) IS 'DDDToolkit access function of DeskContext';\n" +
            "REVOKE ALL ON FUNCTION desk.is_watcher(uuid) FROM PUBLIC;\n",
            "no role may execute a function the script writes just because every role may execute a new one");
        script.Should().Contain(
            "        WHERE d.description = 'DDDToolkit access function of DeskContext'\n" +
            "          AND (n.nspname, p.proname) IN (('desk', 'duty_tickets'), ('desk', 'is_watcher'), ('desk', 'on_duty'), ('desk', 'tickets_for_team'))\n" +
            "          AND acl.grantee <> 0 AND acl.grantee <> p.proowner\n" +
            "    LOOP\n" +
            "        EXECUTE pg_catalog.format('REVOKE ALL ON FUNCTION %s FROM %s', granted.function::pg_catalog.regprocedure, granted.grantee::pg_catalog.regrole);\n",
            "every other grant on them but their owner's is taken back, what an earlier script or a schema's default privileges gave alike, the anonymous caller's included");
        script.Should().Contain(
            "GRANT EXECUTE ON FUNCTION desk.is_watcher(uuid) TO anon, authenticated, ddd_system_in;\n",
            "every role whose policy asks it may execute it, and nobody else");
        script.Should().Contain("REVOKE ALL ON FUNCTION desk.tickets_for_team(text, boolean) FROM PUBLIC;\n")
            .And.NotContain("GRANT EXECUTE ON FUNCTION desk.tickets_for_team", "no policy asks it, so no caller may execute it");
        script.Should().Contain("GRANT EXECUTE ON FUNCTION desk.on_duty() TO authenticated;\n", "a contributed function is granted to its GrantTo, exactly");
        script.IndexOf("REVOKE ALL ON FUNCTION %s FROM %s", StringComparison.Ordinal).Should().BeLessThan(
            script.IndexOf("GRANT EXECUTE ON FUNCTION desk.is_watcher(uuid)", StringComparison.Ordinal), "the grants come after everything else is taken back");

        using var yard = YardContext.Create();
        using var scale = DeskScaleContext.Create();
        var scopedCrates = RowAccessRule.For<Crate>("Scoped work reads crates of watched tickets", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:desk/tickets_i_watch}())))", RowAccessRoles.SystemIn);
        var scripts = PostgresRowAccess.Scripts([yard, scale], [scopedCrates, DeskRules.WatchersBySet], [DeskRules.WatchedSet], new RowAccessExport());
        scripts.Single(each => each.Context == scale).Script.Should().Contain(
            "GRANT EXECUTE ON FUNCTION desk_scale.tickets_i_watch() TO anon, authenticated, ddd_system_in;",
            "written with the other contexts, a function is granted to the roles of their policies that ask it too");
    }

    [Fact]
    public void A_policy_for_a_role_a_contributed_function_is_not_granted_to_is_refused()
    {
        using var desk = DeskContext.Create();
        var everyone = RowAccessRule.For<Ticket>("Everyone on duty reads the open tickets", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:duty/duty_tickets}())))");
        var askingForAnon = new SpotContribution("asking", context => new(
            [],
            [new ContributedPolicy(SpotContribution.Of<Ticket>(context), "Anyone on duty", "SELECT", RowAccessRoles.Anonymous, "{fn:duty/on_duty}()", null)],
            []));

        var rule = () => PostgresRowAccess.Script(desk, [everyone], [], With(new DutyRowAccess()));
        var policy = () => PostgresRowAccess.Script(desk, [], [], With(new DutyRowAccess(), askingForAnon));

        rule.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Everyone on duty reads the open tickets' asks duty/duty_tickets for anon, which that function is not granted to. Set To = [RowAccessRoles.User].",
            "a rule without To is for anon as well, whom the function is kept from");
        policy.Should().Throw<InvalidOperationException>().WithMessage(
            "The policy 'Anyone on duty' of the row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.SpotContribution asks duty/on_duty for anon, which that function is not granted to.*");
        PostgresRowAccess.Script(desk, [RowAccessRule.For<Ticket>("Signed in on duty", RowOperations.Read, "{fn:duty/on_duty}()", RowAccessRoles.User)], [], With(new DutyRowAccess()))
            .Should().Contain("FOR SELECT TO authenticated\n    USING ((desk.on_duty()) OR (", "for a role it is granted to, a rule asks it");
    }

    [Fact]
    public void A_cycle_between_contexts_is_refused_naming_it()
    {
        using var desk = DeskContext.Create();
        using var yard = YardContext.Create();
        var crates = new SpotContribution("crates", context => context is YardContext
            ? new([new ContributedFunction("crate_ids", "", "SETOF uuid", "SELECT c.\"Id\" FROM \"yard\".\"Crates\" c WHERE {fn:duty/on_duty}()", SecurityDefiner: true, GrantTo: [RowAccessRoles.User])], [], [])
            : null);
        var ticketsOfCrates = RowAccessRule.For<Ticket>("Tickets of crates", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:crates/crate_ids}())))", RowAccessRoles.User);

        var scripts = () => PostgresRowAccess.Scripts([desk, yard], [ticketsOfCrates], [], With(new DutyRowAccess(), crates));

        scripts.Should().Throw<InvalidOperationException>().WithMessage(
            "The contexts ask each other's functions in a circle: DeskContext asks yard.crate_ids, which YardContext defines; YardContext asks desk.on_duty, which DeskContext defines. *Move one of the functions into the module that asks it.");
    }

    [Fact]
    public void A_contributed_function_asked_through_an_access_function_is_asked_for_the_roles_of_the_policy()
    {
        using var desk = DeskContext.Create();
        var onDutyHere = RowAccessFunction.For<Ticket>("desk.on_duty_here", "{fn:duty/on_duty}()");
        var everyone = RowAccessRule.For<Ticket>("Everyone on duty here", RowOperations.Read, "{call:desk.on_duty_here}");
        var signedIn = RowAccessRule.For<Ticket>("Signed in on duty here", RowOperations.Read, "{call:desk.on_duty_here}", RowAccessRoles.User);

        var refused = () => PostgresRowAccess.Script(desk, [everyone], [onDutyHere], With(new DutyRowAccess()));

        refused.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Everyone on duty here' asks duty/on_duty through desk.on_duty_here, which runs as its owner, for anon, which that function is not granted to. Set To = [RowAccessRoles.User].",
            "an access function runs as its owner, so what it asks, it asks for whoever its policy is for");
        PostgresRowAccess.Script(desk, [signedIn], [onDutyHere], With(new DutyRowAccess()))
            .Should().Contain("GRANT EXECUTE ON FUNCTION desk.on_duty_here(uuid) TO authenticated;\n", "for the roles the function is granted to, it is asked");

        var helping = new SpotContribution("helping", _ => new([new ContributedFunction("always", "", "boolean", "SELECT true")], [], []));
        var helpedHere = RowAccessFunction.For<Ticket>("desk.helped_here", "{fn:helping/always}()");
        PostgresRowAccess.Script(desk, [RowAccessRule.For<Ticket>("Everyone helped here", RowOperations.Read, "{call:desk.helped_here}")], [helpedHere], With(helping))
            .Should().Contain("GRANT EXECUTE ON FUNCTION desk.helped_here(uuid) TO anon, authenticated;\n",
                "a function granted to no role is a helper for the functions that run as their owner, and an access function is one");
    }

    [Fact]
    public void Two_functions_that_would_be_one_in_the_database_are_refused_across_contexts()
    {
        using var memos = MemosContext.Create();
        using var bookmarks = BookmarksContext.Create();
        var memoMembers = RowAccessFunction.For<Memo>("memos/is_member", "TRUE");
        var bookmarkMembers = RowAccessFunction.For<Bookmark>("bookmarks/is_member", "TRUE");

        var scripts = () => PostgresRowAccess.Scripts([memos, bookmarks], [], [memoMembers, bookmarkMembers], new RowAccessExport());
        var names = () => PostgresRowAccess.FunctionNamesOf([memos, bookmarks], [memoMembers, bookmarkMembers]);

        scripts.Should().Throw<InvalidOperationException>().WithMessage(
            "The functions bookmarks/is_member (about DDDToolkit.EntityFramework.Tests.Infrastructure.Bookmark, written by BookmarksContext) and memos/is_member (about DDDToolkit.EntityFramework.Tests.Infrastructure.Memo, written by MemosContext) would both be public.is_member. A function has one definition: give one of them another name, or give its context a schema of its own.",
            "neither context names a schema, so both functions would be public.is_member, and the file written later would replace the other's");
        names.Should().Throw<InvalidOperationException>().WithMessage("*would both be public.is_member*");

        var contributed = new SpotContribution("bookmarks", context => context is BookmarksContext ? new([new ContributedFunction("is_member", "", "boolean", "SELECT true")], [], []) : null);
        var withContribution = () => PostgresRowAccess.Scripts([memos, bookmarks], [], [memoMembers], With(contributed));
        withContribution.Should().Throw<InvalidOperationException>().WithMessage(
            "The functions bookmarks/is_member (from the row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.SpotContribution, written by BookmarksContext) and memos/is_member (*) would both be public.is_member.*",
            "a contributed function and an access function are no different");

        var single = () => PostgresRowAccess.Script(memos, [], [memoMembers], new RowAccessExport { FunctionNames = new Dictionary<string, string> { ["bookmarks/is_member"] = "public.is_member" } });
        single.Should().Throw<InvalidOperationException>().WithMessage(
            "The functions bookmarks/is_member (named in RowAccessExport.FunctionNames) and memos/is_member (*) would both be public.is_member.*",
            "a script of one context knows where the other's functions are from the export");
    }

    [Fact]
    public void A_function_schema_Postgres_would_not_keep_as_written_is_refused()
    {
        using var capitalized = CapitalizedMemosContext.Create();

        var relative = () => PostgresRowAccess.Script(capitalized, [], [RowAccessFunction.For<Memo>("memos/is_member", "TRUE")]);
        var contributed = () => PostgresRowAccess.Script(capitalized, [], [], With(new SpotContribution("memos", _ => new([new ContributedFunction("is_member", "", "boolean", "SELECT true")], [], []))));

        relative.Should().Throw<InvalidOperationException>().WithMessage(
            "The function memos/is_member would be created in the schema 'Memos' of CapitalizedMemosContext, and a script writes a function's name without quotes, so Postgres would read it as a function of another schema, or of none. Give the schema a name of lower case letters, digits and underscores, or name the function with its schema.");
        contributed.Should().Throw<InvalidOperationException>().WithMessage("The function memos/is_member would be created in the schema 'Memos'*digits and underscores.");
        PostgresRowAccess.Script(capitalized, [], [RowAccessFunction.For<Memo>("memos.is_member", "TRUE")])
            .Should().Contain("CREATE OR REPLACE FUNCTION memos.is_member(uuid) RETURNS boolean", "a function named with its schema is created as it is named");
    }

    [Fact]
    public void Two_policies_of_one_name_on_a_table_are_refused()
    {
        using var desk = DeskContext.Create();
        var narrowing = new SpotContribution("narrowing", context => new(
            [],
            [new ContributedPolicy(SpotContribution.Of<Ticket>(context), "Public tickets are everyones", "SELECT", RowAccessRoles.Anonymous, "\"IsPublic\"", null, Restrictive: true)],
            []));

        var script = () => PostgresRowAccess.Script(desk, [DeskRules.Public], [], With(narrowing));

        script.Should().Throw<InvalidOperationException>().WithMessage(
            "Two policies on desk.Tickets would be named \"Public tickets are everyones (select) for anon\": the permissive policy for SELECT to anon and the restrictive policy 'Public tickets are everyones' of the row access contribution DDDToolkit.EntityFramework.Tests.Infrastructure.SpotContribution in *. Postgres keeps one policy of a name on a table; give the rule or the contributed policy another name.",
            "Postgres would refuse the second where the script runs; this says so where it is written, naming both");
    }

    [Fact]
    public void A_statement_that_ends_in_a_line_comment_is_ended_on_a_line_of_its_own()
    {
        using var desk = DeskContext.Create();
        var commented = new SpotContribution("commented", _ => new([], [], ["CREATE OR REPLACE TRIGGER mark_changed BEFORE UPDATE ON desk.\"Tickets\" FOR EACH ROW EXECUTE FUNCTION desk.mark_changed() -- marks the change"]));

        PostgresRowAccess.Script(desk, [], [], With(commented)).Should().EndWith(
            "EXECUTE FUNCTION desk.mark_changed() -- marks the change\n;\n",
            "a ';' after the comment would be part of it, and the statement would run into the next one");
    }

    [Fact]
    public void A_set_function_over_entities_that_hold_an_alternate_key_starts_from_the_root()
    {
        using var parcels = ParcelsContext.Create();
        var watched = RowAccessFunction.For<Parcel>(
            "parcels/parcels_i_watch", "{exists:Watchers:e1}({col:e1:User} IS NOT DISTINCT FROM {caller:uid}){/exists}", owner: null, shape: AccessFunctionShape.Set);

        var sql = PostgresRowAccess.CreateStatements(parcels, [], [watched], new RowAccessExport());

        sql.Should().Contain("    SELECT root.\"Id\" FROM parcels.\"Parcels\" root\n", "the policy compares the answer with the parcels' ids")
            .And.Contain("e1.\"ParcelNumber\" = root.\"Number\"")
            .And.NotContain("SELECT DISTINCT e1.", "the watchers hold the parcels' numbers, which only the parcels' table turns into their ids");
    }

    [Fact]
    public void Custom_caller_functions_reach_the_contributions()
    {
        using var desk = DeskContext.Create();

        var script = PostgresRowAccess.Script(desk, [], [], new RowAccessExport
        {
            CallerFunctions = new PostgresCallerFunctions("who.id()", "who.role()", "who.claims()"),
            Contributions = [new DutyRowAccess()],
        });

        script.Should().Contain("SELECT coalesce(((SELECT who.claims()) ->> 'on_duty')::boolean, false)").And.NotContain("ddd.caller_claims()");
    }

    [Fact]
    public void A_contribution_that_writes_what_a_script_cannot_is_refused_naming_it()
    {
        using var desk = DeskContext.Create();
        Problem(new ContributedFunction("is-bad", "", "boolean", "SELECT true")).Should().Contain("is not named as a function a script can create");
        Problem(new ContributedFunction("ok", "key text; DROP TABLE x", "boolean", "SELECT true")).Should().Contain("are not SQL parameters");
        Problem(new ContributedFunction("ok", "", "boolean", "SELECT $function$")).Should().Contain("would end it early");
        Problem(new ContributedFunction("ok", "", "boolean", "SELECT {col:Id}")).Should().Contain("'{col:Id}', which contributed SQL cannot ask");
        Problem(new ContributedFunction("ok", "", "boolean", "SELECT true", GrantTo: ["public"])).Should().Contain("'public', which no policy or grant can be for");
        Problem(new ContributedFunction("ok", "", "boolean SECURITY DEFINER", "SELECT true")).Should().Contain("returns 'boolean SECURITY DEFINER', which is not an SQL type",
            "what it returns is written into the function as it is, so an option there would change how the function runs");
        PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("x", _ => new(
                [new ContributedFunction("due", "", "timestamp with time zone", "SELECT now()"), new ContributedFunction("share", "", "SETOF double precision", "SELECT 0.5")], [], []))))
            .Should().Contain("CREATE OR REPLACE FUNCTION desk.due() RETURNS timestamp with time zone\n").And.Contain("CREATE OR REPLACE FUNCTION desk.share() RETURNS SETOF double precision\n",
                "a type Postgres spells in more than one word is still a type");

        var insertWithUsing = () => PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("x", context => new([], [new ContributedPolicy(SpotContribution.Of<Ticket>(context), "Insert", "INSERT", RowAccessRoles.User, "TRUE", null)], []))));
        insertWithUsing.Should().Throw<InvalidOperationException>().WithMessage("*is for INSERT, which needs WithCheck.");

        string Problem(ContributedFunction function)
        {
            var script = () => PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("x", _ => new([function], [], []))));
            return script.Should().Throw<InvalidOperationException>().Which.Message;
        }
    }

    [Fact]
    public void A_contributed_function_returns_rows_of_named_columns()
    {
        using var desk = DeskContext.Create();
        const string Rows = "TABLE (\"TicketId\" uuid, due timestamp with time zone, weight numeric(10, 2), tags text[], team desk.team)";

        var script = PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("x", _ => new(
            [new ContributedFunction("due_tickets", "team text", Rows, "SELECT t.\"Id\", now(), 1.5, ARRAY['late'], NULL FROM desk.\"Tickets\" t WHERE t.\"Team\" = $1", SecurityDefiner: true, GrantTo: [RowAccessRoles.User])],
            [],
            []))));

        script.Should().Contain(
            "CREATE OR REPLACE FUNCTION desk.due_tickets(team text) RETURNS " + Rows + "\n" +
            "    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$\n",
            "the columns are written as they are given: a name, in quotes or plain, and a type each");
        script.Should().Contain("GRANT EXECUTE ON FUNCTION desk.due_tickets(team text) TO authenticated;");

        // What stands for the columns is written into the function as it is, so it is columns and nothing else.
        foreach (var refused in new[]
                 {
                     "TABLE ()",
                     "TABLE (uuid)",
                     "TABLE (\"Id\" SETOF uuid)",
                     "TABLE (\"Id\" uuid) SECURITY DEFINER",
                     "TABLE (\"Id\" uuid) LANGUAGE plpgsql AS $$ BEGIN END $$; --)",
                     "TABLE (\"Id\" uuid, \"Team\" text SECURITY DEFINER)",
                     "TABLE \"Id\" uuid",
                     "SETOF TABLE (\"Id\" uuid)",
                 })
        {
            var writing = () => PostgresRowAccess.Script(desk, [], [], With(new SpotContribution("x", _ => new([new ContributedFunction("ok", "", refused, "SELECT NULL")], [], []))));
            writing.Should().Throw<InvalidOperationException>(refused).WithMessage("*returns '" + refused + "', which is not an SQL type*nor rows of named columns*");
        }
    }

    [Fact]
    public void The_model_helpers_write_the_names_the_model_gives()
    {
        using var desk = DeskContext.Create();
        var tickets = SpotContribution.Of<Ticket>(desk);

        RowAccessModel.Table(tickets).Should().Be("\"desk\".\"Tickets\"");
        RowAccessModel.Column(tickets, nameof(Ticket.IsPublic)).Should().Be("\"IsPublic\"");
        RowAccessModel.ColumnType(tickets, nameof(Ticket.Id)).Should().Be("uuid");
        RowAccessModel.Stored(tickets, nameof(Ticket.Status), TicketStatus.Closed).Should().Be("1");
        RowAccessModel.Stored(tickets, nameof(Ticket.Team), "it's").Should().Be("'it''s'");
        RowAccessModel.Literal("it's").Should().Be("'it''s'");
        RowAccessModel.EntityTablesOf(tickets).Should().ContainEquivalentOf(new
        {
            Table = "\"desk\".\"CommentReaction\"",
            Links = new[]
            {
                new { Parent = "\"desk\".\"TicketComment\"", ParentColumns = new[] { "\"TicketId\"", "\"Id\"" }, Columns = new[] { "\"TicketCommentTicketId\"", "\"TicketCommentId\"" } },
                new { Parent = "\"desk\".\"Tickets\"", ParentColumns = new[] { "\"Id\"" }, Columns = new[] { "\"TicketId\"" } },
            },
        });

        var missing = () => RowAccessModel.Column(tickets, "Nothing");
        missing.Should().Throw<InvalidOperationException>().WithMessage("*maps Ticket.Nothing to no column of desk.Tickets.");
    }
}
