using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Whom a script's policies are for: every policy one role, the rules' symbolic roles written as the roles
/// the host configured, and the rules that allow one command to one role asked in one policy. None of this
/// opens a database; the desk's tests on Postgres run the same scripts.
/// </summary>
public sealed class PostgresRowAccessRoleTests
{
    /// <summary>The desk's roles under other names, as a host of its own might call them.</summary>
    private static readonly RowAccessRoleNames Renamed = new("desk_user", "desk_guest", "desk_scoped");

    private static readonly RowAccessRule ScopedWork = RowAccessRule.For<Ticket>(
        "Scoped work reads every ticket", RowOperations.Read, "TRUE", RowAccessRoles.SystemIn);

    [Fact]
    public void Symbolic_roles_resolve_to_the_configured_roles()
    {
        Renamed.Resolve(RowAccessRoles.User).Should().Be("desk_user");
        Renamed.Resolve(RowAccessRoles.Anonymous).Should().Be("desk_guest");
        Renamed.Resolve(RowAccessRoles.SystemIn).Should().Be("desk_scoped");
        RowAccessRoleNames.Default.Should().Be(new RowAccessRoleNames("authenticated", "anon", "ddd_system_in"));
        RowAccessRoleNames.Of(new PostgresRowLevelSecurityOptions { UserRole = "members", AnonymousRole = "visitors", SystemInRole = null })
            .Should().Be(new RowAccessRoleNames("members", "visitors", "ddd_system_in"), "the policies are for the roles the options configure, and a rule for the scoped role still has one");

        var script = ScriptFor(Renamed, DeskRules.Owners, ScopedWork);

        PoliciesOn(script, "Tickets").Should().Equal(
            [
                "Owners have their tickets (select) for desk_guest",
                "Scoped work reads every ticket (select) for desk_scoped",
                "Owners have their tickets (select) for desk_user",
                "Owners have their tickets (insert) for desk_guest",
                "Owners have their tickets (insert) for desk_user",
                "Owners have their tickets (update) for desk_guest",
                "Owners have their tickets (update) for desk_user",
                "Owners have their tickets (delete) for desk_guest",
                "Owners have their tickets (delete) for desk_user",
            ],
            "a rule without To is for the user and the anonymous caller, whatever the host calls them");
        PoliciesOn(script, "TicketComment").Should().Contain(["TicketComment (select) for desk_scoped", "TicketComment (insert) for desk_user"]);
        script.Should().NotContain(" authenticated").And.NotContain(" anon\n").And.NotContain("ddd_system_in");
    }

    [Fact]
    public void A_literal_role_is_kept_and_public_is_refused()
    {
        RowAccessRoleNames.Default.Resolve("moderator").Should().Be("moderator");
        RowAccessRoleNames.Default.Resolve("Moderator").Should().Be("Moderator", "a role is named as the database spells it");

        foreach (var everyone in (string[])["public", "PUBLIC", "Public"])
        {
            var resolve = () => RowAccessRoleNames.Default.Resolve(everyone);
            resolve.Should().Throw<ArgumentException>().WithMessage("A policy is never for PUBLIC*");
        }

        var unknown = () => RowAccessRoleNames.Default.Resolve("@admin");
        unknown.Should().Throw<ArgumentException>().WithMessage("'@admin' is not a role a rule can name. The symbolic roles are @user, @anonymous and @system-in*");

        var configured = () => RowAccessRoleNames.Default with { Anonymous = "public" };
        configured.Should().Throw<ArgumentException>().WithMessage("PUBLIC is every role there is*").Which.ParamName.Should().Be("Anonymous");
        var shared = () => new RowAccessRoleNames("authenticated", "anon", "authenticated");
        shared.Should().Throw<ArgumentException>().WithMessage("'authenticated' is the role of a user or an anonymous caller as well, and the scoped system role must be one of its own*");

        var script = ScriptFor(RowAccessRoleNames.Default, RowAccessRule.For<Ticket>("Moderators change any ticket", RowOperations.Change, "TRUE", "moderator", "Help Desk"));
        script.Should().Contain("CREATE POLICY \"Moderators change any ticket (update) for moderator\" ON desk.\"Tickets\" FOR UPDATE TO moderator\n");
        script.Should().Contain(
            "CREATE POLICY \"Moderators change any ticket (update) for Help Desk\" ON desk.\"Tickets\" FOR UPDATE TO \"Help Desk\"\n",
            "a role's name is written as it is spelled, quoted where Postgres would otherwise fold or refuse it");
        script.Should().Contain("pg_catalog.to_regrole('\"Help Desk\"')", "the prelude asks about the same role the policy is for");

        var forEveryone = () => ScriptFor(RowAccessRoleNames.Default, RowAccessRule.For<Ticket>("Everybody reads tickets", RowOperations.Read, "TRUE", "PUBLIC"));
        forEveryone.Should().Throw<InvalidOperationException>()
            .WithMessage("The rule 'Everybody reads tickets' is for 'PUBLIC', which no policy can be for. A policy is never for PUBLIC*");
    }

    [Fact]
    public void A_root_rule_for_two_roles_is_two_policies()
    {
        var script = ScriptFor(RowAccessRoleNames.Default, DeskRules.Owners, DeskRules.Teammates);

        PoliciesOn(script, "Tickets").Should().Equal(
            [
                "Tickets (select) for anon",
                "Tickets (select) for authenticated",
                "Owners have their tickets (insert) for anon",
                "Owners have their tickets (insert) for authenticated",
                "Owners have their tickets (update) for anon",
                "Owners have their tickets (update) for authenticated",
                "Owners have their tickets (delete) for anon",
                "Owners have their tickets (delete) for authenticated",
            ],
            "every command a rule grants is a policy for each of its roles, named after the rule, the command and the role");
        script.Should().Contain(
            "\n" +
            "CREATE POLICY \"Owners have their tickets (update) for anon\" ON desk.\"Tickets\" FOR UPDATE TO anon\n" +
            "    USING (((SELECT ddd.caller_id()) IS NOT NULL) AND (\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id())))\n" +
            "    WITH CHECK (((SELECT ddd.caller_id()) IS NOT NULL) AND (\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id())));\n" +
            "COMMENT ON POLICY \"Owners have their tickets (update) for anon\" ON desk.\"Tickets\" IS 'DDDToolkit row access rule';\n" +
            "\n" +
            "CREATE POLICY \"Owners have their tickets (update) for authenticated\" ON desk.\"Tickets\" FOR UPDATE TO authenticated\n",
            "the rule's condition is written as it was, once for each role");
        script.Should().NotContain("FOR ALL").And.NotContain("TO anon, authenticated");
    }

    [Fact]
    public void Permissive_policies_for_one_table_command_and_role_are_merged()
    {
        var auditors = RowAccessRule.For<Ticket>("Auditors read every ticket", RowOperations.Read, "TRUE", RowAccessRoles.User);

        var script = ScriptFor(RowAccessRoleNames.Default, DeskRules.Owners, DeskRules.Public, auditors);

        PoliciesOn(script, "Tickets").Should().Equal(
            [
                "Tickets (select) for anon",
                "Tickets (select) for authenticated",
                "Owners have their tickets (insert) for anon",
                "Owners have their tickets (insert) for authenticated",
                "Owners have their tickets (update) for anon",
                "Owners have their tickets (update) for authenticated",
                "Owners have their tickets (delete) for anon",
                "Owners have their tickets (delete) for authenticated",
            ],
            "three rules let a caller read, so reading is one policy per role, named after the table");
        script.Should().Contain(
            "\n" +
            "-- Tickets (select) for anon asks the rules 'Owners have their tickets' and 'Public tickets are everyones': a row one of them allows is allowed.\n" +
            "CREATE POLICY \"Tickets (select) for anon\" ON desk.\"Tickets\" FOR SELECT TO anon\n" +
            "    USING ((((SELECT ddd.caller_id()) IS NOT NULL) AND (\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id()))) OR (\"IsPublic\"));\n" +
            "COMMENT ON POLICY \"Tickets (select) for anon\" ON desk.\"Tickets\" IS 'DDDToolkit row access rule';\n",
            "the rules' conditions are OR-ed, as Postgres ORs permissive policies, and the comment names the rules");
        script.Should().Contain(
            "-- Tickets (select) for authenticated asks the rules 'Auditors read every ticket', 'Owners have their tickets' and 'Public tickets are everyones': a row one of them allows is allowed.\n" +
            "CREATE POLICY \"Tickets (select) for authenticated\" ON desk.\"Tickets\" FOR SELECT TO authenticated\n" +
            "    USING ((TRUE) OR (((SELECT ddd.caller_id()) IS NOT NULL) AND (\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id()))) OR (\"IsPublic\"));\n",
            "a rule for one role merges into that role's policy only");
        Regex.Matches(script, "CREATE POLICY [^\\n]* FOR SELECT TO authenticated\\n").Count.Should().Be(1 + 3, "one on the tickets, and one on each table of their entities");
    }

    [Fact]
    public void The_prelude_makes_the_system_in_role_only_when_a_policy_names_it()
    {
        var without = ScriptFor(RowAccessRoleNames.Default, DeskRules.Owners);
        var with = ScriptFor(RowAccessRoleNames.Default, DeskRules.Owners, ScopedWork);

        without.Should().NotContain("ddd_system_in", "no policy is for the scoped role, so it is neither made nor granted");
        with.Should().Contain(
            "    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'ddd_system_in') THEN\n" +
            "        BEGIN\n" +
            "            CREATE ROLE ddd_system_in NOLOGIN NOINHERIT;\n" +
            "        EXCEPTION WHEN duplicate_object OR unique_violation THEN\n" +
            "            NULL; -- made by a script that ran at the same time\n" +
            "        END;\n" +
            "    END IF;\n" +
            "    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'ddd_system_in'\n" +
            "               AND (rolbypassrls OR rolsuper OR rolcanlogin)) THEN\n" +
            "        RAISE EXCEPTION USING MESSAGE = 'The role ddd_system_in exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.';\n" +
            "    END IF;\n" +
            "    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_class c\n" +
            "               WHERE scoped.rolname = 'ddd_system_in' AND c.relkind IN ('r', 'p')\n" +
            "                 AND pg_catalog.pg_has_role(scoped.oid, c.relowner, 'USAGE')) THEN\n" +
            "        RAISE EXCEPTION USING MESSAGE = 'The role ddd_system_in has the privileges of a role that owns tables, and Postgres does not hold an owner to row level security. It must neither own a table nor be granted a role that does.';\n" +
            "    END IF;\n" +
            "    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_roles callers\n" +
            "               WHERE scoped.rolname = 'ddd_system_in' AND callers.rolname IN ('authenticated', 'anon')\n" +
            "                 AND pg_catalog.pg_has_role(callers.oid, scoped.oid, 'USAGE')) THEN\n" +
            "        RAISE EXCEPTION USING MESSAGE = 'The role ddd_system_in is granted to authenticated or anon, so their callers would get every policy written for it. Grant it only to the role the application logs in as.';\n" +
            "    END IF;\n" +
            "    IF NOT pg_catalog.pg_has_role(CURRENT_USER, 'ddd_system_in'::pg_catalog.name,\n" +
            "            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN\n" +
            "        BEGIN\n" +
            "            GRANT ddd_system_in TO CURRENT_USER;\n" +
            "        EXCEPTION WHEN unique_violation THEN\n" +
            "            NULL; -- granted by a script that ran at the same time\n" +
            "        END;\n" +
            "    END IF;\n",
            "made where it is missing, refused where it could void or spread the policies, and granted to whoever runs the script");
        with.Should().Contain("        GRANT USAGE ON SCHEMA ddd TO ddd_system_in;\n");
        with.IndexOf("CREATE ROLE ddd_system_in", StringComparison.Ordinal).Should().BeLessThan(
            with.IndexOf("GRANT USAGE ON SCHEMA ddd TO ddd_system_in", StringComparison.Ordinal), "the role exists before anything is granted to it");
        ScriptFor(Renamed, ScopedWork).Should().Contain("            CREATE ROLE desk_scoped NOLOGIN NOINHERIT;\n")
            .And.Contain("callers.rolname IN ('desk_user', 'desk_guest')", "the check asks about the roles the host configured")
            .And.NotContain("ddd_system_in");
        ScriptFor(RowAccessRoleNames.Default, RowAccessRule.For<Ticket>("Named by its own name", RowOperations.Read, "TRUE", "ddd_system_in"))
            .Should().Contain("CREATE ROLE ddd_system_in", "a rule that names the scoped role by its own name is for that role too");

        var setup = PostgresRowAccess.SetupScript();
        setup.Should().Contain("            CREATE ROLE \"ddd_system_in\" NOLOGIN NOINHERIT;\n")
            .And.Contain("RAISE EXCEPTION USING MESSAGE = 'The role ddd_system_in exists")
            .And.Contain("'The role ddd_system_in has the privileges of a role that owns tables")
            .And.Contain("'The role ddd_system_in is granted to authenticated or anon");
        setup.Should().Contain("GRANT \"anon\", \"authenticated\", \"ddd_system_in\" TO CURRENT_USER;");
        PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions { SystemInRole = null }).Should().NotContain("ddd_system_in", "a host without the scoped role gets none");
    }

    [Fact]
    public void Roles_a_script_could_not_write_as_they_are_or_a_host_should_not_configure_are_refused()
    {
        var dollar = () => RowAccessRoleNames.Default.Resolve("desk$ddd$; DROP SCHEMA desk CASCADE; --");
        dollar.Should().Throw<ArgumentException>().WithMessage("*has a '$', which a script cannot write into the dollar-quoted blocks it puts role names in.*");
        var tooLong = () => RowAccessRoleNames.Default.Resolve(new string('r', 64));
        tooLong.Should().Throw<ArgumentException>().WithMessage("*is longer than the 63 bytes of a name Postgres keeps*");
        RowAccessRoleNames.Default.Resolve(new string('r', 63)).Should().HaveLength(63, "Postgres keeps a name of 63 bytes whole");

        var predefined = () => RowAccessRoleNames.Default with { SystemIn = "pg_write_all_data" };
        predefined.Should().Throw<ArgumentException>().WithMessage("'pg_write_all_data' starts with pg_, which Postgres keeps for its own roles.*")
            .Which.ParamName.Should().Be("SystemIn");
        var symbolic = () => new RowAccessRoleNames(RowAccessRoles.User, "anon", "ddd_system_in");
        symbolic.Should().Throw<ArgumentException>().WithMessage("'@user' starts with '@'*").Which.ParamName.Should().Be("User");
        var none = () => RowAccessRoleNames.Default with { SystemIn = "none" };
        none.Should().Throw<ArgumentException>().WithMessage("'none' is no role: switching to it goes back to the role the application logged in as*")
            .Which.ParamName.Should().Be("SystemIn");

        foreach (var (options, parameter) in (IEnumerable<(PostgresRowLevelSecurityOptions, string)>)
            [
                (new() { SystemInRole = "authenticated" }, "SystemInRole"),
                (new() { SystemInRole = "anon" }, "SystemInRole"),
                (new() { SystemInRole = "service_role", SystemRole = "service_role" }, "SystemInRole"),
                (new() { SystemInRole = "@x" }, "SystemInRole"),
                (new() { SystemInRole = "Public" }, "SystemInRole"),
                (new() { SystemInRole = "none" }, "SystemInRole"),
                (new() { AnonymousRole = "NONE" }, "AnonymousRole"),
                (new() { UserRole = "desk$user" }, "UserRole"),
            ])
        {
            var setup = () => PostgresRowAccess.SetupScript(options);
            setup.Should().Throw<ArgumentException>("the setup makes the roles a script writes policies for, and refuses what it refuses")
                .Which.ParamName.Should().Be(parameter);
        }
    }

    [Fact]
    public void A_with_expression_may_change_several_roles_at_once_in_any_order()
    {
        (RowAccessRoleNames.Default with { User = "ddd_system_in", SystemIn = "worker" })
            .Should().Be(new RowAccessRoleNames("ddd_system_in", "anon", "worker"), "only where it ends counts");
        (RowAccessRoleNames.Default with { SystemIn = "anon", Anonymous = "guest" })
            .Should().Be(new RowAccessRoleNames("authenticated", "guest", "anon"));

        var shared = RowAccessRoleNames.Default with { User = "ddd_system_in" };

        var resolve = () => shared.Resolve(RowAccessRoles.User);
        resolve.Should().Throw<InvalidOperationException>().WithMessage("'ddd_system_in' is the role of a user or an anonymous caller as well*");
        var script = () => ScriptFor(shared, DeskRules.Owners);
        script.Should().Throw<InvalidOperationException>("where it ends, the scoped system role is the user's as well");
    }

    [Fact]
    public void Types_of_a_hierarchy_on_one_table_share_one_policy_per_table_command_and_role()
    {
        using var yard = YardContext.Create();

        var script = PostgresRowAccess.Script(yard, [YardRules.Owners, YardRules.OwnerlessExpress]);

        var policies = Regex.Matches(script, "^CREATE POLICY \"[^\"]+\" ON (?<table>\\S+) FOR (?<command>\\w+) TO (?<role>\\S+)$", RegexOptions.Multiline)
            .Select(match => $"{match.Groups["table"].Value} {match.Groups["command"].Value} {match.Groups["role"].Value}")
            .ToList();
        policies.Should().OnlyHaveUniqueItems("a table gets one policy per command and role, whichever types of the hierarchy grant it")
            .And.Contain(["yard.\"Crates\" SELECT anon", "yard.\"CrateSticker\" SELECT anon", "yard.\"CrateSticker\" UPDATE anon", "yard.\"CrateSticker\" INSERT authenticated"]);
        Regex.Matches(script, "ENABLE ROW LEVEL SECURITY").Count.Should().Be(2, "the crates' table and the stickers' table, once each");

        script.Should().Contain(
            "-- Crates (select) for anon asks the rules 'Ownerless express crates are open' and 'Owners have their crates': a row one of them allows is allowed.\n" +
            "CREATE POLICY \"Crates (select) for anon\" ON yard.\"Crates\" FOR SELECT TO anon\n" +
            "    USING (((\"Owner\" IS NULL) AND \"Discriminator\" = 'ExpressCrate') OR (((SELECT ddd.caller_id()) IS NOT NULL) AND (\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id()))));\n",
            "a rule about express crates holds for the rows of express crates only, and the crates' own rule for every crate");
        script.Should().Contain(
            "CREATE POLICY \"CrateSticker (update) for anon\" ON yard.\"CrateSticker\" FOR UPDATE TO anon\n" +
            "    USING (EXISTS (SELECT 1 FROM yard.\"Crates\" r WHERE r.\"Id\" = yard.\"CrateSticker\".\"CrateId\" AND (((r.\"Owner\" IS NULL) AND r.\"Discriminator\" = 'ExpressCrate') OR (((SELECT ddd.caller_id()) IS NOT NULL) AND (r.\"Owner\" IS NOT DISTINCT FROM (SELECT ddd.caller_id()))))))\n",
            "the stickers are written as the rules of both types let a caller write the crate, each about its own rows");
    }

    [Fact]
    public void A_name_that_would_end_the_block_it_is_written_into_is_refused()
    {
        using var odd = OddlyNamedYard.Create();

        var script = () => PostgresRowAccess.Script(odd, [YardRules.Owners]);

        script.Should().Throw<InvalidOperationException>().WithMessage("A table, schema, function or role name has '$ddd$' in it*");
    }

    /// <summary>The script for the desk with <paramref name="rules"/> and <paramref name="roles"/>, which reads no database.</summary>
    private static string ScriptFor(RowAccessRoleNames roles, params RowAccessRule[] rules)
    {
        using var model = DeskContext.Create();
        return PostgresRowAccess.Script(model, rules, [DeskRules.IsWatcher], new RowAccessExport { Roles = roles });
    }

    /// <summary>The names of the policies <paramref name="script"/> makes on <c>desk.<paramref name="table"/></c>, in the order it makes them.</summary>
    private static List<string> PoliciesOn(string script, string table)
        => [.. Regex.Matches(script, $"^CREATE POLICY \"(?<name>[^\"]+)\" ON desk\\.\"{Regex.Escape(table)}\"", RegexOptions.Multiline).Select(match => match.Groups["name"].Value)];

    /// <summary>The yard's crates in a table whose name spells the tag a script quotes its blocks with.</summary>
    private sealed class OddlyNamedYard(DbContextOptions<OddlyNamedYard> options) : DbContext(options)
    {
        public DbSet<Crate> Crates => Set<Crate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Crate>().ToTable("Crates$ddd$").OwnsMany(crate => crate.Stickers);

        public static OddlyNamedYard Create()
            => new(new DbContextOptionsBuilder<OddlyNamedYard>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);
    }
}
