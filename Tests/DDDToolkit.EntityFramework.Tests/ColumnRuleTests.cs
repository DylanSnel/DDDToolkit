using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What a script writes for a column rule, which needs no database to read: a trigger before an update of the
/// columns it holds, no policy; the roles the trigger holds and those it lets through; the columns a value object is
/// stored in; one trigger for the columns the same rules hold; a rule about one type of a hierarchy; and what is
/// refused. <see cref="ColumnRulePostgresTests"/> runs the same scripts.
/// </summary>
public sealed class ColumnRuleTests
{
    /// <summary>The shelf's roles: the defaults, and the role its auditors' tokens are mapped to.</summary>
    private static readonly RowAccessExport Shelf = new()
    {
        Roles = RowAccessRoleNames.Default with { TokenRoles = new Dictionary<string, string> { ["auditor"] = "shelf_auditor" } },
    };

    private static readonly RowAccessRule PackersChangeCrates = RowAccessRule.For<Crate>("Packers change crates", RowOperations.All, "{caller:signedin}");

    [Fact]
    public void A_column_rule_is_a_trigger_on_its_column_and_writes_no_policy()
    {
        using var desk = DeskContext.Create();

        var without = PostgresRowAccess.Script(desk, [DeskRules.Owners, DeskRules.TeamWork]);
        var with = PostgresRowAccess.Script(desk, [DeskRules.Owners, DeskRules.TeamWork, DeskRules.OwnersClose]);

        Policies(with).Should().Equal(Policies(without), "a policy is about a row, and the rules for the row are what they were");
        with.Should().Contain(
            "\n-- A change of \"Status\" of desk.\"Tickets\" is held to the column rule 'Owners close their tickets'.\n" +
            "CREATE OR REPLACE FUNCTION desk.tickets_status_column_rule() RETURNS trigger\n" +
            "    LANGUAGE plpgsql SET search_path = '' AS $body$\n" +
            "BEGIN\n" +
            "    -- The roles a caller's statement runs as are held; the application's own work and the tables' owner are not.\n" +
            "    IF CURRENT_USER IN ('anon', 'authenticated') THEN\n" +
            "        IF (((SELECT ddd.caller_id()) IS NOT NULL) AND (OLD.\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id()))) IS NOT TRUE" +
            " OR (((SELECT ddd.caller_id()) IS NOT NULL) AND (NEW.\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id()))) IS NOT TRUE THEN\n" +
            "            RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'tickets_status_column_rule', MESSAGE = 'The column rule ''Owners close their tickets'' does not let this caller change \"Status\" of desk.\"Tickets\".';\n" +
            "        END IF;\n" +
            "    END IF;\n" +
            "    RETURN NEW;\n" +
            "END\n" +
            "$body$;\n" +
            "COMMENT ON FUNCTION desk.tickets_status_column_rule() IS 'DDDToolkit access function of DeskContext';\n" +
            "REVOKE ALL ON FUNCTION desk.tickets_status_column_rule() FROM PUBLIC;\n" +
            "DROP TRIGGER IF EXISTS tickets_status_column_rule ON desk.\"Tickets\";\n" +
            "CREATE TRIGGER tickets_status_column_rule BEFORE UPDATE OF \"Status\" ON desk.\"Tickets\"\n" +
            "    FOR EACH ROW WHEN (OLD.\"Status\" IS DISTINCT FROM NEW.\"Status\")\n" +
            "    EXECUTE FUNCTION desk.tickets_status_column_rule();\n" +
            "COMMENT ON TRIGGER tickets_status_column_rule ON desk.\"Tickets\" IS 'DDDToolkit column rule';\n",
            "the rule is asked of the row as it was and as it is about to be, as a policy for UPDATE asks it, for the roles it is for, and the trigger fires only where the status changes");
        without.Should().NotContain("CREATE TRIGGER", "a script without column rules writes no trigger");
    }

    [Fact]
    public void Several_column_rules_on_one_column_are_asked_in_one_trigger_and_a_change_one_of_them_allows_is_allowed()
    {
        using var desk = DeskContext.Create();

        var script = PostgresRowAccess.Script(desk, [DeskRules.Owners, DeskRules.TeamWork, DeskRules.OwnersClose, DeskRules.LeadsClose]);

        Regex.Matches(script, "CREATE TRIGGER ").Should().HaveCount(1, "both rules hold the status");
        script.Should().Contain("-- A change of \"Status\" of desk.\"Tickets\" is held to the column rules 'Leads close the teams tickets' and 'Owners close their tickets': a change one of them allows is allowed.\n");
        script.Should().Contain(
            "    IF CURRENT_USER = 'anon' THEN\n" +
            "        IF (((SELECT ddd.caller_id()) IS NOT NULL) AND (OLD.\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id()))) IS NOT TRUE",
            "the leads' rule is for signed-in users alone, so a caller without a user is asked the owners' rule");
        script.Should().Contain(
            "    ELSIF CURRENT_USER = 'authenticated' THEN\n" +
            "        IF ((((OLD.\"Team\" IS NOT NULL) AND (OLD.\"Team\" IS NOT DISTINCT FROM (SELECT ddd.caller_claims() #>> '{app_metadata,team}'))) AND ((SELECT ddd.caller_claims() #>> '{app_metadata,lead}') IS NOT DISTINCT FROM 'yes'))" +
            " OR (((SELECT ddd.caller_id()) IS NOT NULL) AND (OLD.\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id())))) IS NOT TRUE",
            "a signed-in user is asked both, OR-ed together");
        script.Should().Contain("MESSAGE = 'None of the column rules ''Leads close the teams tickets'' and ''Owners close their tickets'' lets this caller change \"Status\" of desk.\"Tickets\".';");
    }

    [Fact]
    public void Every_role_a_callers_statement_runs_as_is_held_and_the_applications_own_work_only_where_a_rule_names_it()
    {
        using var shelf = ShelfContext.Create();

        var script = PostgresRowAccess.Script(shelf, [PackersChangeCrates, RowAccessRule.ForColumns<Crate>("Anyone labels", [nameof(Crate.Label)], "TRUE")], null, Shelf);
        var named = PostgresRowAccess.Script(shelf, [PackersChangeCrates, RowAccessRule.ForColumns<Crate>("Anyone labels", [nameof(Crate.Label)], "TRUE", RowAccessRoles.User, RowAccessRoles.SystemIn)], null, Shelf);

        Function(script, "crates_label_column_rule").Should().Contain(
            "    IF CURRENT_USER IN ('anon', 'authenticated') THEN\n" +
            "        IF (TRUE) IS NOT TRUE THEN\n",
            "a rule that reads nothing of the row is asked once");
        Function(script, "crates_label_column_rule").Should().Contain(
            "    ELSIF CURRENT_USER = 'shelf_auditor' THEN\n" +
            "        RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'crates_label_column_rule', MESSAGE = 'No column rule is for this caller''s role, so it may not change \"Label\" of shelf.\"Crates\".';\n",
            "a mapped token role is a caller's, and no rule for it lets it change the column");
        Function(script, "crates_label_column_rule").Should().NotContain("ddd_system_in", "the scoped system role is the application's own work, and passes");

        Function(named, "crates_label_column_rule").Should().Contain(
            "    IF CURRENT_USER IN ('authenticated', 'ddd_system_in') THEN\n",
            "a rule that names the scoped system role holds it");
        Function(named, "crates_label_column_rule").Should().Contain(
            "    ELSIF CURRENT_USER IN ('anon', 'shelf_auditor') THEN\n        RAISE EXCEPTION",
            "the anonymous caller's role is held, and no rule for it is there");
    }

    [Fact]
    public void A_value_object_is_every_column_it_is_stored_in_and_one_of_its_properties_is_one()
    {
        using var shelf = ShelfContext.Create();

        Trigger(PostgresRowAccess.Script(shelf, [PackersChangeCrates, RowAccessRule.ForColumns<Crate>("Measurers size crates", [nameof(Crate.Size)], "TRUE")], null, Shelf), "crates_size_depth_size_width_column_rule")
            .Should().StartWith("CREATE TRIGGER crates_size_depth_size_width_column_rule BEFORE UPDATE OF \"Size_Depth\", \"Size_Width\" ON shelf.\"Crates\"\n" +
                                "    FOR EACH ROW WHEN ((OLD.\"Size_Depth\", OLD.\"Size_Width\") IS DISTINCT FROM (NEW.\"Size_Depth\", NEW.\"Size_Width\"))\n",
                "a complex property is the columns of its properties");
        Trigger(PostgresRowAccess.Script(shelf, [PackersChangeCrates, RowAccessRule.ForColumns<Crate>("Sealers seal crates", [nameof(Crate.Seal)], "TRUE")], null, Shelf), "crates_seal_code_seal_until_column_rule")
            .Should().StartWith("CREATE TRIGGER crates_seal_code_seal_until_column_rule BEFORE UPDATE OF \"Seal_Code\", \"Seal_Until\" ON shelf.\"Crates\"",
                "an owned type stored in the row is the columns of its properties, and not the key it shares with the row");
        Trigger(PostgresRowAccess.Script(shelf, [PackersChangeCrates, RowAccessRule.ForColumns<Crate>("Sealers recode crates", ["Seal.Code"], "TRUE")], null, Shelf), "crates_seal_code_column_rule")
            .Should().StartWith("CREATE TRIGGER crates_seal_code_column_rule BEFORE UPDATE OF \"Seal_Code\" ON shelf.\"Crates\"",
                "a property of a value object is its one column");
        Trigger(PostgresRowAccess.Script(shelf, [PackersChangeCrates, RowAccessRule.ForColumns<Crate>("Taggers tag crates", [nameof(Crate.Tags)], "TRUE")], null, Shelf), "crates_tags_column_rule")
            .Should().StartWith("CREATE TRIGGER crates_tags_column_rule BEFORE UPDATE OF \"Tags\" ON shelf.\"Crates\"",
                "a collection of values is stored in the row, as an array, and is its one column");
    }

    [Fact]
    public void Rules_that_ask_the_same_for_different_roles_each_name_their_own_rule_when_they_refuse()
    {
        using var shelf = ShelfContext.Create();

        var script = PostgresRowAccess.Script(
            shelf,
            [
                PackersChangeCrates,
                RowAccessRule.ForColumns<Crate>("Users close crates", [nameof(Crate.State)], "{caller:signedin}", RowAccessRoles.User),
                RowAccessRule.ForColumns<Crate>("Auditors close crates", [nameof(Crate.State)], "{caller:signedin}", RowAccessRoles.Token("auditor")),
            ],
            null,
            Shelf);

        var function = Function(script, "crates_state_column_rule");
        function.Should().Contain("    IF CURRENT_USER = 'authenticated' THEN\n", "the same question, for another role, is another rule");
        function.Should().Contain("MESSAGE = 'The column rule ''Users close crates'' does not let this caller change \"State\" of shelf.\"Crates\".';");
        function.Should().Contain("    ELSIF CURRENT_USER = 'shelf_auditor' THEN\n");
        function.Should().Contain("MESSAGE = 'The column rule ''Auditors close crates'' does not let this caller change \"State\" of shelf.\"Crates\".';");
        function.Should().NotContain("None of the column rules", "each refusal names the rule that was asked of the caller");
    }

    [Fact]
    public void A_role_that_a_rule_or_a_contribution_lets_change_the_table_is_held_where_no_column_rule_is_for_it()
    {
        using var shelf = ShelfContext.Create();
        var moving = new SpotContribution("shelf-moving", context => new(
            [],
            [
                new ContributedPolicy(SpotContribution.Of<Crate>(context), "Movers move crates", "UPDATE", "shelf_mover", "TRUE", null),
                new ContributedPolicy(SpotContribution.Of<Crate>(context), "Keepers keep crates", "ALL", "shelf_keeper", "TRUE", null),
                new ContributedPolicy(SpotContribution.Of<Crate>(context), "Inspectors narrow crates", "UPDATE", "shelf_inspector", "TRUE", null, Restrictive: true),
            ],
            []));
        var export = new RowAccessExport { Roles = Shelf.Roles, Contributions = [moving] };

        var script = PostgresRowAccess.Script(
            shelf,
            [
                PackersChangeCrates,
                RowAccessRule.For<Crate>("Clerks change crates", RowOperations.Read | RowOperations.Change, "TRUE", "shelf_clerk"),
                RowAccessRule.For<Crate>("Counters read crates", RowOperations.Read, "TRUE", "shelf_counter"),
                RowAccessRule.ForColumns<Crate>("Packers label crates", [nameof(Crate.Label)], "{caller:signedin}", RowAccessRoles.User),
            ],
            null,
            export);

        Function(script, "crates_label_column_rule").Should().Contain(
            "    ELSIF CURRENT_USER IN ('anon', 'shelf_auditor', 'shelf_clerk', 'shelf_keeper', 'shelf_mover') THEN\n" +
            "        RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'crates_label_column_rule', MESSAGE = 'No column rule is for this caller''s role, so it may not change \"Label\" of shelf.\"Crates\".';\n",
            "a role that a rule for Change, a contribution's policy for UPDATE or for ALL lets change a crate is a caller's, and no column rule is for it");
        Function(script, "crates_label_column_rule").Should().NotContain("shelf_counter", "a role that only reads changes no column")
            .And.NotContain("shelf_inspector", "a restrictive policy lets nobody change anything");
    }

    [Fact]
    public void The_columns_the_same_rules_hold_share_a_trigger_and_others_get_one_of_their_own()
    {
        using var shelf = ShelfContext.Create();

        var script = PostgresRowAccess.Script(
            shelf,
            [
                PackersChangeCrates,
                RowAccessRule.ForColumns<Crate>("Measurers size crates", [nameof(Crate.Size), "Seal.Code"], "TRUE"),
                RowAccessRule.ForColumns<Crate>("Anyone labels", [nameof(Crate.Label)], "TRUE"),
                RowAccessRule.ForColumns<Crate>("Checkers relabel crates", [nameof(Crate.Label), nameof(Crate.State)], "TRUE"),
            ],
            null,
            Shelf);

        Regex.Matches(script, @"CREATE TRIGGER (\S+) BEFORE UPDATE OF ([^\n]+) ON").Select(match => match.Groups[1].Value + ": " + match.Groups[2].Value).Should().Equal(
            "crates_label_column_rule: \"Label\"",
            "crates_seal_code_size_depth_size_width_column_rule: \"Seal_Code\", \"Size_Depth\", \"Size_Width\"",
            "crates_state_column_rule: \"State\"");
        script.Should().Contain("-- A change of \"Label\" of shelf.\"Crates\" is held to the column rules 'Anyone labels' and 'Checkers relabel crates': a change one of them allows is allowed.\n");
        script.Should().Contain("-- A change of \"State\" of shelf.\"Crates\" is held to the column rule 'Checkers relabel crates'.\n");
    }

    [Fact]
    public void A_rule_about_one_type_of_a_hierarchy_holds_the_rows_of_that_type_alone()
    {
        using var shelf = ShelfContext.Create();

        var script = PostgresRowAccess.Script(shelf, [PackersChangeCrates, RowAccessRule.ForColumns<ColdCrate>("Cold keepers set the temperature", [nameof(ColdCrate.Temperature)], "{caller:signedin}")], null, Shelf);

        Trigger(script, "crates_temperature_column_rule").Should().StartWith(
            "CREATE TRIGGER crates_temperature_column_rule BEFORE UPDATE OF \"Temperature\" ON shelf.\"Crates\"\n" +
            "    FOR EACH ROW WHEN ((OLD.\"Temperature\" IS DISTINCT FROM NEW.\"Temperature\") AND (OLD.\"Kind\" = 'cold' OR NEW.\"Kind\" = 'cold'))\n",
            "the other crates in the table are no business of a rule about cold ones");
        Function(script, "crates_temperature_column_rule").Should().Contain(
            "IF (((SELECT ddd.caller_id()) IS NOT NULL) AND OLD.\"Kind\" = 'cold') IS NOT TRUE OR (((SELECT ddd.caller_id()) IS NOT NULL) AND NEW.\"Kind\" = 'cold') IS NOT TRUE THEN",
            "a crate that stops being a cold one is no longer one the rule allows");
    }

    [Fact]
    public void A_rule_that_reads_the_key_alone_is_asked_of_the_row_as_it_will_be_only_where_the_key_changes()
    {
        using var desk = DeskContext.Create();
        var watchersRetitle = RowAccessRule.ForColumns<Ticket>("Watchers retitle tickets", [nameof(Ticket.Title)], WatchersReadTheirTickets.RowAccessSql, RowAccessRoles.User);

        var script = PostgresRowAccess.Script(desk, [DeskRules.Owners, watchersRetitle], [DeskRules.IsWatcher]);

        Function(script, "tickets_title_column_rule").Should().Contain(
            "        IF (desk.is_watcher(OLD.\"Id\")) IS NOT TRUE OR (OLD.\"Id\" IS DISTINCT FROM NEW.\"Id\" AND (desk.is_watcher(NEW.\"Id\")) IS NOT TRUE) THEN\n",
            "Entity Framework never changes a key, so the function is asked once a change");
        script.Should().Contain("GRANT EXECUTE ON FUNCTION desk.is_watcher(uuid) TO authenticated;", "the role a column rule is for asks the access function its rule asks");
    }

    [Theory]
    [InlineData("Volume", "Crate.Volume")]
    [InlineData("Nothing", "Crate.Nothing")]
    [InlineData("Size.Height", "Crate.Size.Height")]
    public void A_property_the_model_stores_in_no_column_of_the_row_is_refused_naming_the_rule(string column, string property)
    {
        using var shelf = ShelfContext.Create();

        var write = () => PostgresRowAccess.Script(shelf, [PackersChangeCrates, RowAccessRule.ForColumns<Crate>("Weighers weigh crates", [column], "TRUE")], null, Shelf);

        write.Should().Throw<InvalidOperationException>()
            .WithMessage($"The column rule 'Weighers weigh crates' holds {property}, which the Entity Framework model maps to no column of shelf.Crates.*");
    }

    [Fact]
    public void A_name_that_is_no_plain_identifier_gets_a_hash_of_what_it_holds()
    {
        using var shelf = ShelfContext.Create();

        var script = PostgresRowAccess.Script(shelf, [RowAccessRule.ForColumns<Rack>("Fitters raise racks", [nameof(Rack.Height)], "TRUE")], null, Shelf);

        Regex.Match(script, @"CREATE TRIGGER (\S+) BEFORE UPDATE OF ""Height""").Groups[1].Value.Should().MatchRegex("^racks_of_the_north_wing_height_column_rule_[0-9a-f]{8}$");
    }

    [Fact]
    public void The_drop_at_the_start_of_a_script_takes_the_triggers_of_column_rules_away_too()
    {
        using var desk = DeskContext.Create();

        var drop = PostgresRowAccess.DropStatement(desk);

        drop.Should().Contain(
            "        JOIN pg_catalog.pg_description d ON d.objoid = t.oid AND d.classoid = 'pg_catalog.pg_trigger'::regclass\n" +
            "        WHERE d.description = 'DDDToolkit column rule'\n",
            "a trigger is found by its comment, as a policy is, so a rule taken out takes its trigger along");
        drop.Should().Contain("        EXECUTE format('DROP TRIGGER %I ON %I.%I', generated.tgname, generated.nspname, generated.relname);\n");
        PostgresRowAccess.Script(desk, [DeskRules.Owners]).Should().StartWith(drop);
    }

    [Fact]
    public void A_column_rule_names_a_column()
    {
        var none = () => RowAccessRule.ForColumns<Ticket>("Nobody closes", [], "TRUE");
        var empty = () => RowAccessRule.ForColumns<Ticket>("Nobody closes", [" "], "TRUE");

        none.Should().Throw<ArgumentException>().WithMessage("The column rule 'Nobody closes' names no column.*").Which.ParamName.Should().Be("columns");
        empty.Should().Throw<ArgumentException>().WithMessage("The column rule 'Nobody closes' names an empty column.*");

        var rule = RowAccessRule.ForColumns<Ticket>("Owners close", [nameof(Ticket.Status), nameof(Ticket.Status)], "TRUE");
        (rule.Operations, rule.IsColumnRule, rule.Columns.Count).Should().Be((RowOperations.Change, true, 1), "a column rule is about a change, and names each column once");
        DeskRules.Owners.IsColumnRule.Should().BeFalse();
    }

    [Fact]
    public void A_column_rules_generated_SQL_says_so_and_makes_no_rule_about_whole_rows()
    {
        OwnersCloseTheirTickets.RowAccessSql.Should().StartWith("{columns}", "the generator marks a column rule's SQL");
        DeskRules.OwnersClose.Sql.Should().Be(OwnersCloseTheirTickets.RowAccessSql["{columns}".Length..], "a column rule takes the SQL without the mark");

        var asRowRule = () => RowAccessRule.For<Ticket>("Owners close their tickets", RowOperations.Change, OwnersCloseTheirTickets.RowAccessSql);

        asRowRule.Should().Throw<ArgumentException>()
            .WithMessage("The SQL of 'Owners close their tickets' is a column rule's, which holds a change of some columns alone: as a rule about whole rows it would let whoever it allows change every column.*")
            .Which.ParamName.Should().Be("sql");
    }

    [Fact]
    public void An_export_that_does_not_know_a_placeholder_stops_at_it_which_is_how_one_without_column_rules_meets_one()
    {
        using var desk = DeskContext.Create();

        // What an export of 3.1 does with a column rule's SQL, which it takes for a rule about whole rows: it fills
        // the template, and {columns} is a placeholder it does not know.
        var write = () => PostgresRowAccess.Script(desk, [DeskRules.Owners, RowAccessRule.For<Ticket>("Owners close their tickets", RowOperations.Change, "TRUE AND {columns}")]);

        write.Should().Throw<InvalidOperationException>().WithMessage("*asks for '{columns}', which this version of DDDToolkit.EntityFramework.Postgres does not know. Use the same version of the toolkit's packages everywhere.");
    }

    /// <summary>The <c>CREATE POLICY</c> lines of <paramref name="script"/>, in order.</summary>
    private static List<string> Policies(string script)
        => [.. script.Split('\n').Where(line => line.StartsWith("CREATE POLICY ", StringComparison.Ordinal))];

    /// <summary>The <c>CREATE FUNCTION</c> statement of <paramref name="script"/> for the trigger <paramref name="name"/>.</summary>
    private static string Function(string script, string name)
    {
        var start = script.IndexOf($"CREATE OR REPLACE FUNCTION shelf.{name}()", StringComparison.Ordinal) is var shelf and >= 0
            ? shelf
            : script.IndexOf($"CREATE OR REPLACE FUNCTION desk.{name}()", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the script writes the function {0}", name);
        return script[start..(script.IndexOf("$body$;", start, StringComparison.Ordinal) + "$body$;".Length)];
    }

    /// <summary>The <c>CREATE TRIGGER</c> statement of <paramref name="script"/> named <paramref name="name"/>.</summary>
    private static string Trigger(string script, string name)
    {
        var start = script.IndexOf($"CREATE TRIGGER {name} ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the script writes the trigger {0}", name);
        return script[start..(script.IndexOf(";\n", start, StringComparison.Ordinal) + 2)];
    }

    // A shelf of crates: a hierarchy in one table, a value object stored as columns of the row, an owned type stored
    // in the row, a property the model leaves out, and a table whose name is no plain identifier.

    public class Crate
    {
        public Guid Id { get; set; }

        public string Label { get; set; } = "";

        public string State { get; set; } = "";

        public CrateSize Size { get; set; } = new();

        public CrateSeal Seal { get; set; } = new();

        public List<string> Tags { get; set; } = [];

        public decimal Volume => Size.Width * Size.Depth;
    }

    public sealed class ColdCrate : Crate
    {
        public decimal Temperature { get; set; }
    }

    public sealed class CrateSize
    {
        public int Width { get; set; }

        public int Depth { get; set; }
    }

    public sealed class CrateSeal
    {
        public string Code { get; set; } = "";

        public DateOnly Until { get; set; }
    }

    public sealed class Rack
    {
        public Guid Id { get; set; }

        public int Height { get; set; }
    }

    internal sealed class ShelfContext(DbContextOptions<ShelfContext> options) : DbContext(options)
    {
        public DbSet<Crate> Crates => Set<Crate>();

        public DbSet<Rack> Racks => Set<Rack>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("shelf");
            modelBuilder.Entity<Crate>(crate =>
            {
                crate.ToTable("Crates");
                crate.HasDiscriminator<string>("Kind").HasValue<Crate>("crate").HasValue<ColdCrate>("cold");
                crate.ComplexProperty(row => row.Size);
                crate.OwnsOne(row => row.Seal);
                crate.Ignore(row => row.Volume);
            });
            modelBuilder.Entity<Rack>().ToTable("Racks of the north wing");
        }

        /// <summary>The shelf's model on Npgsql, for a script to be written from: it never connects.</summary>
        public static ShelfContext Create() => new(new DbContextOptionsBuilder<ShelfContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
    }
}
