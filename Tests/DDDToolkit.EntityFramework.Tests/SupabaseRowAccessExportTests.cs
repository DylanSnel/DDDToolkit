using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Row access rules written into <c>supabase/migrations</c> next to the exported migrations: a file per
/// module that says what the rules are now, written again when they change or a migration comes after it.
/// None of this opens a database.
/// </summary>
public sealed class SupabaseRowAccessExportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ddd-row-access-" + Guid.NewGuid().ToString("N"));

    private static readonly RowAccessRule ShelvesByName = RowAccessRule.For<SupabaseShelf>(
        "Shelves by name", RowOperations.Read, "({col:Name} IS NOT DISTINCT FROM {caller:claim:shelf})");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private SupabaseMigrationReport Export(DateTimeOffset? now = null, params RowAccessRule[] rules) => Export(Options(now, rules));

    private SupabaseMigrationReport Export(SupabaseMigrationOptions options)
    {
        using var context = SupabaseShelfContext.Create();
        return SupabaseMigrations.Export(context, _directory, options);
    }

    private SupabaseMigrationReport Compare(params RowAccessRule[] rules) => Compare(Options(null, rules));

    private SupabaseMigrationReport Compare(SupabaseMigrationOptions options)
    {
        using var context = SupabaseShelfContext.Create();
        return SupabaseMigrations.Compare(context, _directory, options);
    }

    private static SupabaseMigrationOptions Options(DateTimeOffset? now, RowAccessRule[] rules)
    {
        var options = new SupabaseMigrationOptions { TimeProvider = new FixedClock(now ?? new DateTimeOffset(2026, 9, 25, 16, 0, 0, TimeSpan.Zero)) };
        foreach (var rule in rules)
        {
            options.RowAccessRules.Add(rule);
        }

        return options;
    }

    private string[] AccessFiles() => [.. Directory.GetFiles(_directory, "*_access.*.ddd.sql").Select(Path.GetFileName).Order(StringComparer.Ordinal).Select(name => name!)];

    [Fact]
    public void Rules_become_a_file_of_their_own_after_the_migrations()
    {
        var report = Export(rules: ShelvesByName);

        report.IsInSync.Should().BeTrue();
        AccessFiles().Should().Equal("20260925160000_access.supabaseshelf.ddd.sql");

        var sql = File.ReadAllText(Path.Combine(_directory, AccessFiles()[0]));
        sql.Should().StartWith("-- Written by DDDToolkit from the row access rules of SupabaseShelfContext.");
        sql.Should().Contain("AND (n.nspname, c.relname) IN (('ddd', 'OutboxMessages'), ('public', 'Shelves'))", "the previous file's policies come off every table of the module first");
        sql.Should().Contain("ALTER TABLE public.\"Shelves\" ENABLE ROW LEVEL SECURITY;");
        sql.Should().Contain(
            "CREATE POLICY \"Shelves by name (select) for anon\" ON public.\"Shelves\" FOR SELECT TO anon\n" +
            "    USING (\"Name\" IS NOT DISTINCT FROM (SELECT auth.jwt() ->> 'shelf'));\n" +
            "COMMENT ON POLICY \"Shelves by name (select) for anon\" ON public.\"Shelves\" IS 'DDDToolkit row access rule';\n" +
            "\n" +
            "CREATE POLICY \"Shelves by name (select) for authenticated\" ON public.\"Shelves\" FOR SELECT TO authenticated\n" +
            "    USING (\"Name\" IS NOT DISTINCT FROM (SELECT auth.jwt() ->> 'shelf'));\n",
            "a rule without To is a policy for each of Supabase's two roles");
    }

    [Fact]
    public void A_second_export_with_the_same_rules_changes_nothing()
    {
        Export(rules: ShelvesByName);

        var again = Export(now: new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero), ShelvesByName);

        again.Entries.Should().OnlyContain(entry => entry.Status == SupabaseMigrationStatus.Unchanged);
        AccessFiles().Should().HaveCount(1);
    }

    [Fact]
    public void A_changed_rule_is_a_new_file_and_the_old_one_stays_because_it_may_have_been_applied()
    {
        Export(rules: ShelvesByName);
        var changed = RowAccessRule.For<SupabaseShelf>("Shelves by name", RowOperations.Read, "({col:Name} IS NOT DISTINCT FROM {caller:claim:app_metadata.shelf})");

        var report = Export(now: new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero), changed);

        report.Created.Should().ContainSingle();
        AccessFiles().Should().Equal("20260925160000_access.supabaseshelf.ddd.sql", "20260926090000_access.supabaseshelf.ddd.sql");
        File.ReadAllText(Path.Combine(_directory, AccessFiles()[1])).Should().Contain("#>> '{app_metadata,shelf}'");
    }

    [Fact]
    public void Rules_that_come_later_leave_the_exported_migrations_as_they_were()
    {
        Export();
        var migrations = Directory.GetFiles(_directory).ToDictionary(path => path, File.ReadAllText);

        var report = Export(rules: ShelvesByName);

        report.Entries.Where(entry => !entry.MigrationId.EndsWith("_access", StringComparison.Ordinal))
            .Should().OnlyContain(entry => entry.Status == SupabaseMigrationStatus.Unchanged, "a file Supabase may already have applied is never rewritten");
        migrations.Should().OnlyContain(file => File.ReadAllText(file.Key) == file.Value);
        AccessFiles().Should().HaveCount(2, "the first export gave the module's outbox its privileges, and the rules are a file after it");
        File.ReadAllText(Path.Combine(_directory, AccessFiles()[1])).Should().Contain("CREATE POLICY \"Shelves by name (select) for anon\"");
    }

    [Fact]
    public void A_migration_of_a_module_with_rules_first_takes_their_policies_off()
    {
        Export(rules: ShelvesByName);

        var migration = File.ReadAllText(Directory.GetFiles(_directory, "*_CreateShelves.*").Single());

        migration.Should().Contain("-- This module's row access rules come off before its schema changes");
        migration.Should().Contain("EXECUTE format('DROP POLICY %I ON %I.%I'");
        migration.IndexOf("DROP POLICY", StringComparison.Ordinal).Should().BeLessThan(migration.IndexOf("CREATE TABLE", StringComparison.Ordinal));
    }

    [Fact]
    public void A_check_names_rules_that_have_no_file_saying_what_they_are_now()
    {
        Export();

        var report = Compare(ShelvesByName);

        report.IsInSync.Should().BeFalse();
        report.Problems.Should().ContainSingle().Which.MigrationId.Should().Be("supabaseshelf row access rules");
        new SupabaseMigrationsOutOfSyncException([report]).Message.Should().Contain("a rule changed, or a migration of the module came after the last file");
    }

    [Fact]
    public void Taking_every_rule_out_writes_a_file_that_only_takes_their_policies_off()
    {
        Export(rules: ShelvesByName);

        Export(now: new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero));

        var last = File.ReadAllText(Path.Combine(_directory, AccessFiles()[^1]));
        AccessFiles().Should().HaveCount(2);
        last.Should().Contain("DROP POLICY").And.NotContain("CREATE POLICY");
    }

    [Fact]
    public void The_file_sorts_after_everything_else_in_the_directory_whatever_the_clock_says()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "29990101000000_from_the_future.sql"), "SELECT 1;");

        Export(rules: ShelvesByName);

        AccessFiles().Should().Equal("29990101000001_access.supabaseshelf.ddd.sql");
    }

    [Fact]
    public void A_rule_that_is_one_column_still_gets_the_parentheses_postgres_wants()
    {
        using var context = DeskContext.Create();

        SupabaseMigrations.Export(context, _directory, Options(null, [DeskRules.Public]));

        File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single()).Should().Contain("FOR SELECT TO anon\n    USING (\"IsPublic\");");
    }

    [Fact]
    public void The_aggregates_own_tables_follow_it()
    {
        using var context = DeskContext.Create();
        var options = Options(null, [DeskRules.Owners]);

        SupabaseMigrations.Export(context, _directory, options);

        var sql = File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single());
        sql.Should().Contain("ALTER TABLE desk.\"TicketComment\" ENABLE ROW LEVEL SECURITY;");
        sql.Should().Contain(
            "CREATE POLICY \"TicketComment (select) for anon\" ON desk.\"TicketComment\" FOR SELECT TO anon\n" +
            "    USING (EXISTS (SELECT 1 FROM desk.\"Tickets\" parent WHERE parent.\"Id\" = desk.\"TicketComment\".\"TicketId\"));",
            "reading an entity follows the root row, which answers under its own policies");
        sql.Should().Contain(
            "CREATE POLICY \"TicketComment (insert) for authenticated\" ON desk.\"TicketComment\" FOR INSERT TO authenticated\n" +
            "    WITH CHECK (EXISTS (SELECT 1 FROM desk.\"Tickets\" r WHERE r.\"Id\" = desk.\"TicketComment\".\"TicketId\" AND (((SELECT auth.uid()) IS NOT NULL) AND (r.\"Owner\" IS NOT DISTINCT FROM (SELECT auth.uid())))));",
            "writing one asks the root's write rules, with Supabase's caller functions, about the root row");
        sql.Should().NotContain("FOR ALL TO anon, authenticated\n    USING (EXISTS", "no entity policy lets whoever reads the root write its entities");
        sql.Should().Contain("CREATE OR REPLACE FUNCTION ddd.written_in_this_transaction(row_xmin xid)", "the access file brings what its policies ask");
        sql.Should().Contain("FOR UPDATE TO authenticated\n    USING (((SELECT auth.uid()) IS NOT NULL) AND (\"Owner\" IS NOT DISTINCT FROM (SELECT auth.uid())))\n    WITH CHECK");
    }

    [Fact]
    public void An_access_function_is_written_before_the_policies_that_call_it()
    {
        using var context = DeskContext.Create();
        var options = Options(null, [DeskRules.Watchers]);
        options.RowAccessFunctions.Add(DeskRules.IsWatcher);

        SupabaseMigrations.Export(context, _directory, options);

        var sql = File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single());
        sql.Should().Contain(
            "CREATE OR REPLACE FUNCTION desk.is_watcher(uuid) RETURNS boolean\n" +
            "    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$\n" +
            "    SELECT EXISTS (SELECT 1 FROM desk.\"Tickets\" root\n" +
            "                   WHERE root.\"Id\" = $1 AND (EXISTS (SELECT 1 FROM desk.\"TicketWatcher\" e1 WHERE e1.\"TicketId\" = root.\"Id\" AND ((e1.\"User\" IS NOT DISTINCT FROM (SELECT auth.uid()))))))\n" +
            "$function$;\n" +
            "COMMENT ON FUNCTION desk.is_watcher(uuid) IS 'DDDToolkit access function of DeskContext';");
        sql.Should().Contain("FOR SELECT TO anon\n    USING (desk.is_watcher(\"Id\"));", "the rule asks the function about the row");
        sql.IndexOf("CREATE OR REPLACE FUNCTION", StringComparison.Ordinal).Should().BeLessThan(sql.IndexOf("CREATE POLICY", StringComparison.Ordinal));
    }

    [Fact]
    public void Custom_caller_functions_reach_every_policy_of_the_export()
    {
        using var context = DeskContext.Create();
        var byRole = RowAccessRule.For<Ticket>("Scoped work reads by role", RowOperations.Read, "({caller:role} IS NOT NULL)", RowAccessRoles.SystemIn);
        var options = Options(null, [DeskRules.Owners, DeskRules.Teammates, DeskRules.Watchers, DeskRules.TeamFiles, byRole]);
        options.RowAccessFunctions.Add(DeskRules.IsWatcher);
        options.CallerFunctions = new PostgresCallerFunctions("who.id()", "who.role()", "who.claims()");

        SupabaseMigrations.Export(context, _directory, options);

        var sql = File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single());
        sql.Should().NotContain("auth.", "none of Supabase's own caller functions is left");
        sql.Should().Contain("WHERE root.\"Id\" = $1 AND (EXISTS (SELECT 1 FROM desk.\"TicketWatcher\" e1 WHERE e1.\"TicketId\" = root.\"Id\" AND ((e1.\"User\" IS NOT DISTINCT FROM (SELECT who.id()))))))", "the access function asks it too");
        sql.Should().Contain("FOR SELECT TO ddd_system_in\n    USING ((SELECT who.role()) IS NOT NULL);");

        var policies = Regex.Matches(sql, "^CREATE POLICY .*?;$", RegexOptions.Multiline | RegexOptions.Singleline)
            .Select(match => match.Value)
            .ToList();
        policies.Where(policy => policy.Contains("desk.\"TicketComment\" FOR UPDATE", StringComparison.Ordinal)).Should().NotBeEmpty("the entities' write policies ask the root's rules")
            .And.OnlyContain(policy => policy.Contains("(SELECT who.id())", StringComparison.Ordinal));
        policies.Where(policy => policy.Contains("\"Owner\"", StringComparison.Ordinal)).Should().NotBeEmpty()
            .And.OnlyContain(policy => policy.Contains("(SELECT who.id())", StringComparison.Ordinal));
        policies.Where(policy => policy.Contains("\"Team\"", StringComparison.Ordinal)).Should().NotBeEmpty()
            .And.OnlyContain(policy => policy.Contains("(SELECT who.claims() #>> '{app_metadata,team}')", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_context_that_maps_the_aggregate_writes_its_access_function()
    {
        var options = Options(null, [ShelvesByName]);
        options.RowAccessFunctions.Add(DeskRules.IsWatcher);

        using var context = SupabaseShelfContext.Create();
        SupabaseMigrations.Export(context, _directory, options);

        File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single()).Should().NotContain("desk.is_watcher", "shelves know no tickets");
    }

    [Fact]
    public void Two_access_functions_with_one_name_are_refused()
    {
        var options = Options(null, [DeskRules.Watchers]);
        options.RowAccessFunctions.Add(DeskRules.IsWatcher);
        options.RowAccessFunctions.Add(RowAccessFunction.For<SupabaseShelf>("desk.is_watcher", "TRUE"));

        using var context = DeskContext.Create();
        var export = () => SupabaseMigrations.Export(context, _directory, options);

        export.Should().Throw<InvalidOperationException>().WithMessage("Two access functions are called desk.is_watcher*");
    }

    [Fact]
    public void A_rule_asks_an_access_function_by_a_key_it_holds()
    {
        using var context = DeskContext.Create();
        var options = Options(null, [RowAccessRule.For<Ticket>("Watched by key", RowOperations.Read, "{fn:desk.is_watcher}({col:Id})")]);
        options.RowAccessFunctions.Add(DeskRules.IsWatcher);

        SupabaseMigrations.Export(context, _directory, options);

        File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single()).Should().Contain("FOR SELECT TO authenticated\n    USING (desk.is_watcher(\"Id\"));");
    }

    [Fact]
    public void A_rule_that_asks_a_function_no_module_defines_is_refused_when_its_file_is_written()
    {
        using var context = DeskContext.Create();

        var export = () => SupabaseMigrations.Export(context, _directory, Options(null, [DeskRules.Watchers]));

        export.Should().Throw<InvalidOperationException>()
            .WithMessage("The rule 'Watchers read their tickets' asks the access function desk.is_watcher, which no [AccessFunction] in the modules this host references defines.*");
    }

    [Fact]
    public void A_rule_asks_a_function_another_module_defines_by_its_logical_name()
    {
        var cratesOfWatchedTickets = RowAccessRule.For<Crate>(
            "Crates of watched tickets", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:desk/tickets_i_watch}())))");
        var options = Options(null, [cratesOfWatchedTickets, DeskRules.WatchersBySet]);
        options.RowAccessFunctions.Add(DeskRules.WatchedSet);

        SupabaseMigrations.Export(
            [SupabaseMigrationSource.For(() => YardContext.Create(), "yard"), SupabaseMigrationSource.For(() => DeskScaleContext.Create(), "desk")],
            _directory,
            options);

        AccessFiles().Should().HaveCount(2);
        var tickets = File.ReadAllText(Directory.GetFiles(_directory, "*_access.desk.ddd.sql").Single());
        var crates = File.ReadAllText(Directory.GetFiles(_directory, "*_access.yard.ddd.sql").Single());
        tickets.Should().Contain("CREATE OR REPLACE FUNCTION desk_scale.tickets_i_watch() RETURNS SETOF uuid", "the module that maps the tickets writes the function, in its own schema");
        crates.Should().Contain("USING (\"Id\" = ANY (ARRAY(SELECT desk_scale.tickets_i_watch())));", "the other module asks it by its logical name");
        string.CompareOrdinal(AccessFiles()[0], AccessFiles()[1]).Should().BeLessThan(0);
        AccessFiles()[0].Should().EndWith("_access.desk.ddd.sql", "the module that defines the function comes first");

        using var yard = YardContext.Create();
        var alone = () => SupabaseMigrations.Export(yard, Path.Combine(_directory, "alone"), options);
        alone.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Crates of watched tickets' asks the access function desk/tickets_i_watch, which no [AccessFunction] in the modules this host references defines.*",
            "exported on its own, the yard knows no module that defines it");
    }

    [Fact]
    public void A_rule_asks_a_set_another_modules_contribution_answers_by_the_resources_id()
    {
        var seen = ResourceAccessAnswer.NameOf(typeof(TicketId), ResourceAccessSet.Seen);
        var cratesOfTicketsISee = RowAccessRule.For<Crate>(
            "Crates of tickets I see", RowOperations.Read, $"({{col:Id}} = ANY (ARRAY(SELECT {{fn:{seen}}}())))", RowAccessRoles.User);
        var options = Options(null, [cratesOfTicketsISee]);
        options.RowAccessContributions.Add(new SpotContribution("following", context => context.Model.FindEntityType(typeof(Ticket)) is { } tickets
            ? new(
                [new ContributedFunction("tickets_i_follow", "", "SETOF " + RowAccessModel.ColumnType(tickets, nameof(Ticket.Id)), $"SELECT t.\"Id\" FROM {RowAccessModel.Table(tickets)} t",
                    SecurityDefiner: true, GrantTo: [RowAccessRoles.User], Answers: new(typeof(TicketId), ResourceAccessSet.Seen))],
                [],
                [])
            : null));

        SupabaseMigrations.Export(
            [SupabaseMigrationSource.For(() => YardContext.Create(), "yard"), SupabaseMigrationSource.For(() => DeskScaleContext.Create(), "desk")],
            _directory,
            options);

        var crates = File.ReadAllText(Directory.GetFiles(_directory, "*_access.yard.ddd.sql").Single());
        crates.Should().Contain("USING (\"Id\" = ANY (ARRAY(SELECT desk_scale.tickets_i_follow())));", "the rule names no function, and the file writes the one that answers the set");
        AccessFiles()[0].Should().EndWith("_access.desk.ddd.sql", "the module whose contribution answers the set comes first");

        using var yard = YardContext.Create();
        var alone = () => SupabaseMigrations.Export(yard, Path.Combine(_directory, "alone"), Options(null, [cratesOfTicketsISee]));
        alone.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Crates of tickets I see' asks the resources the caller sees, by the id DDDToolkit.EntityFramework.Tests.Infrastructure.TicketId, "
            + "and no row access contribution this host uses answers that set for the modules exported. Reference the package that keeps the resource's access, Membership on Postgres for a resource with members, and mark the resource's rules [MembershipRules<TMember>]*",
            "a host that leaves out the contribution of the resource's access hears which resource, and what to use");

        // The contribution listed, and the yard exported without the desk, whose context maps the tickets.
        var withoutTheDesk = () => SupabaseMigrations.Export(yard, Path.Combine(_directory, "without-the-desk"), options);
        withoutTheDesk.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Crates of tickets I see' asks the resources the caller sees, *Where it answers for another module, the one whose context maps the resource, "
            + "export the modules together, SupabaseMigrations.Export with a source for each, as the build that exports every module does.",
            "a host that lists the contribution already hears that the module keeping the resource is missing from the export");
    }

    [Fact]
    public void Contributed_functions_policies_and_statements_land_in_the_contexts_access_file_in_order()
    {
        using var context = DeskContext.Create();
        var options = Options(null, [DeskRules.Public]);
        options.RowAccessContributions.Add(new DutyRowAccess { Stamps = true });

        SupabaseMigrations.Export(context, _directory, options);

        var sql = File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single());
        int At(string text)
        {
            var at = sql.IndexOf(text, StringComparison.Ordinal);
            at.Should().BeGreaterThanOrEqualTo(0, $"the file has {text}");
            return at;
        }

        new[]
        {
            At("EXECUTE format('DROP POLICY %I ON %I.%I'"),
            At("-- What the policies below ask"),
            At("EXECUTE format('DROP FUNCTION %s', generated.signature)"),
            At("CREATE OR REPLACE FUNCTION desk.on_duty() RETURNS boolean"),
            At("CREATE OR REPLACE FUNCTION desk.duty_tickets() RETURNS SETOF uuid"),
            At("CREATE POLICY \"Tickets (select) for authenticated\""),
            At("CREATE OR REPLACE FUNCTION desk.duty_stamp() RETURNS trigger"),
            At("CREATE OR REPLACE TRIGGER duty_stamp BEFORE UPDATE ON \"desk\".\"Tickets\""),
        }.Should().BeInAscendingOrder("the policies come off, the prelude makes what they ask, then the functions, each after what it asks, then the policies, and the contribution's own statements last");
    }

    [Fact]
    public void Each_contributed_block_names_its_assembly_and_version()
    {
        using var context = DeskContext.Create();
        var options = Options(null, []);
        options.RowAccessContributions.Add(new DutyRowAccess { OpenOnly = true, Stamps = true });

        SupabaseMigrations.Export(context, _directory, options);

        var sql = File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single());
        DutyRowAccess.Source.Should().MatchRegex(@"^DDDToolkit\.EntityFramework\.Tests\.Infrastructure\.DutyRowAccess in DDDToolkit\.EntityFramework\.Tests [0-9]+\.[0-9]+\.[0-9]+[^+]*$", "the version leaves out the commit the build came from, so a new commit is not a new file");
        Regex.Matches(sql, "^-- Written by the row access contribution " + Regex.Escape(DutyRowAccess.Source) + @"\.$", RegexOptions.Multiline)
            .Should().HaveCount(3, "one above each of its two functions and one above its statements");
        sql.Should().Contain("-- On duty reads the open tickets (select) for authenticated asks the policy 'On duty reads the open tickets' of the row access contribution " + DutyRowAccess.Source + ".\n");
        sql.Should().Contain("-- Open tickets only (select) for authenticated is the policy 'Open tickets only' of the row access contribution " + DutyRowAccess.Source + ", which narrows what the permissive policies allow.\n");
    }

    [Fact]
    public void A_context_with_only_a_contribution_gets_an_access_file()
    {
        var options = Options(null, []);
        options.RowAccessContributions.Add(Shelving());

        var report = Export(options);

        report.IsInSync.Should().BeTrue();
        AccessFiles().Should().Equal("20260925160000_access.supabaseshelf.ddd.sql");
        var sql = File.ReadAllText(Path.Combine(_directory, AccessFiles()[0]));
        sql.Should().Contain("ALTER TABLE public.\"Shelves\" ENABLE ROW LEVEL SECURITY;")
            .And.Contain("CREATE POLICY \"Shelves are read by the signed in (select) for authenticated\" ON public.\"Shelves\" FOR SELECT TO authenticated\n    USING ((SELECT auth.uid()) IS NOT NULL);");

        options.RowAccessContributions.Clear();
        Compare(options).IsInSync.Should().BeFalse("without the contribution the module's policies are what a new file would take off");
    }

    [Fact]
    public void A_contribution_written_with_windows_line_endings_is_exported_with_the_files_own_and_unchanged_the_next_time()
    {
        // SQL a contribution takes from Entity Framework carries the platform's line endings. The file never does,
        // so what is written on Windows is what a check on Linux expects, and a second export finds it as it is.
        var options = Options(null, []);
        options.RowAccessContributions.Add(new SpotContribution("noting", context => context is SupabaseShelfContext
            ? new([], [], ["COMMENT ON TABLE public.\"Shelves\" IS 'What is on\r\nthe shelves'"])
            : null));

        Export(options).IsInSync.Should().BeTrue();

        var file = Path.Combine(_directory, AccessFiles().Should().ContainSingle().Subject);
        File.ReadAllText(file).Should().Contain("IS 'What is on\nthe shelves'").And.NotContain("\r");
        Export(options).Entries.Should().OnlyContain(entry => entry.Status == SupabaseMigrationStatus.Unchanged);
        Compare(options).IsInSync.Should().BeTrue();
        AccessFiles().Should().ContainSingle("nothing changed, so no second file says so");
    }

    [Fact]
    public void A_context_with_only_a_contribution_starts_its_migrations_with_the_drop()
    {
        var options = Options(null, []);
        options.RowAccessContributions.Add(Shelving());

        Export(options);

        var migration = File.ReadAllText(Directory.GetFiles(_directory, "*_CreateShelves.*").Single());
        migration.Should().Contain("-- This module's row access rules come off before its schema changes");
        migration.IndexOf("DROP POLICY", StringComparison.Ordinal).Should().BeLessThan(migration.IndexOf("CREATE TABLE", StringComparison.Ordinal));
    }

    [Fact]
    public void A_changed_function_signature_is_refused_with_the_rename_advice()
    {
        using var context = DeskContext.Create();
        var options = Options(null, [DeskRules.Watchers]);
        options.RowAccessFunctions.Add(DeskRules.IsWatcher);
        SupabaseMigrations.Export(context, _directory, options);

        var changed = Options(new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero), [RowAccessRule.For<Ticket>("Watchers read their tickets", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:desk.is_watcher}())))")]);
        changed.RowAccessFunctions.Add(RowAccessFunction.For<Ticket>("desk.is_watcher", TicketsIWatch.RowAccessSql, owner: null, shape: AccessFunctionShape.Set));
        var export = () => SupabaseMigrations.Export(context, _directory, changed);

        export.Should().Throw<InvalidOperationException>().WithMessage(
            "The function desk.is_watcher changed from (uuid) RETURNS boolean to () RETURNS SETOF uuid. Postgres cannot change it in place while other policies use it; give it a new name.");
        AccessFiles().Should().HaveCount(1, "nothing is written");

        var renamed = Options(new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero), [DeskRules.WatchersBySet]);
        renamed.RowAccessFunctions.Add(DeskRules.WatchedSet);
        SupabaseMigrations.Export(context, _directory, renamed).IsInSync.Should().BeTrue("under a new name it is a new function, and the old one is dropped with its policies");
        AccessFiles().Should().HaveCount(2);
    }

    [Fact]
    public void A_function_a_statement_writes_on_one_line_may_change_its_body()
    {
        using var context = DeskContext.Create();
        var body = "BEGIN RETURN NEW; END";
        var returns = "trigger";
        var retitling = new SpotContribution("retitling", _ => new([], [], [$"CREATE OR REPLACE FUNCTION desk.retitle() RETURNS {returns} LANGUAGE plpgsql SET search_path = '' AS $body$ {body} $body$"]));
        var first = Options(null, []);
        first.RowAccessContributions.Add(retitling);
        SupabaseMigrations.Export(context, _directory, first);

        body = "BEGIN NEW.\"Title\" := trim(NEW.\"Title\"); RETURN NEW; END";
        var second = Options(new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero), []);
        second.RowAccessContributions.Add(retitling);
        SupabaseMigrations.Export(context, _directory, second).IsInSync.Should().BeTrue("only its body changed, which Postgres replaces in place, whatever it says after RETURNS on that line");
        AccessFiles().Should().HaveCount(2);

        returns = "boolean";
        var third = Options(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero), []);
        third.RowAccessContributions.Add(retitling);
        var export = () => SupabaseMigrations.Export(context, _directory, third);
        export.Should().Throw<InvalidOperationException>().WithMessage(
            "The function desk.retitle changed from () RETURNS trigger to () RETURNS boolean.*", "what it returns is still its signature");
    }

    [Fact]
    public void A_function_that_returns_rows_keeps_its_columns()
    {
        using var context = DeskContext.Create();
        var columns = "\"TicketId\" uuid, \"Team\" text";
        var body = "SELECT t.\"Id\", t.\"Team\" FROM desk.\"Tickets\" t";
        var listing = new SpotContribution("listing", _ => new(
            [new ContributedFunction("tickets_listed", "", $"TABLE ({columns})", body, SecurityDefiner: true, GrantTo: [RowAccessRoles.User])], [], []));
        var first = Options(null, []);
        first.RowAccessContributions.Add(listing);
        SupabaseMigrations.Export(context, _directory, first);

        body += " WHERE t.\"IsPublic\"";
        var second = Options(new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero), []);
        second.RowAccessContributions.Add(listing);
        SupabaseMigrations.Export(context, _directory, second).IsInSync.Should().BeTrue("only its body changed, which Postgres replaces in place");
        AccessFiles().Should().HaveCount(2);

        // The columns a function returns are what it returns: Postgres cannot change them in place either.
        columns += ", \"Title\" text";
        body = "SELECT t.\"Id\", t.\"Team\", t.\"Title\" FROM desk.\"Tickets\" t";
        var third = Options(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero), []);
        third.RowAccessContributions.Add(listing);
        var export = () => SupabaseMigrations.Export(context, _directory, third);
        export.Should().Throw<InvalidOperationException>().WithMessage(
            "The function desk.tickets_listed changed from () RETURNS TABLE (\"TicketId\" uuid, \"Team\" text) to () RETURNS TABLE (\"TicketId\" uuid, \"Team\" text, \"Title\" text). Postgres cannot change it in place*");
    }

    [Fact]
    public void Access_files_are_written_in_dependency_order()
    {
        var cratesOfWatchedTickets = RowAccessRule.For<Crate>(
            "Crates of watched tickets", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:desk/tickets_i_watch}())))");
        var watchedOnShelves = RowAccessRule.For<Ticket>(
            "Watched tickets of the signed in", RowOperations.Read, "({col:Id} = ANY (ARRAY(SELECT {fn:desk/tickets_i_watch}())) AND {fn:shelving/any_shelf}())", RowAccessRoles.User);
        var options = Options(null, [cratesOfWatchedTickets, DeskRules.WatchersBySet, watchedOnShelves]);
        options.RowAccessFunctions.Add(DeskRules.WatchedSet);
        options.RowAccessContributions.Add(Shelving());

        var reports = SupabaseMigrations.Export(
            [
                SupabaseMigrationSource.For(() => YardContext.Create(), "yard"),
                SupabaseMigrationSource.For(() => DeskScaleContext.Create(), "desk"),
                SupabaseMigrationSource.For(() => SupabaseShelfContext.Create(), "shelves"),
            ],
            _directory,
            options);

        reports.Should().OnlyContain(report => report.IsInSync);
        AccessFiles().Should().Equal(
            [
                "20260925160000_access.shelves.ddd.sql",
                "20260925160001_access.desk.ddd.sql",
                "20260925160002_access.yard.ddd.sql",
            ],
            "the desk asks the shelves' contributed function, and the yard the desk's: each file comes after the files of the functions it asks, whatever order the modules came in");
        File.ReadAllText(Path.Combine(_directory, AccessFiles()[1])).Should().Contain("AND public.any_shelf()", "the desk asks the function by the name it has in the shelves' schema");
    }

    /// <summary>
    /// A contribution for the shelves: a policy that lets the signed in read every shelf, and a function other
    /// modules ask whether there is a shelf at all.
    /// </summary>
    private static SpotContribution Shelving() => new("shelving", context => context is SupabaseShelfContext
        ? new(
            [new ContributedFunction("any_shelf", "", "boolean", $"SELECT EXISTS (SELECT 1 FROM {RowAccessModel.Table(SpotContribution.Of<SupabaseShelf>(context))})", SecurityDefiner: true, GrantTo: [RowAccessRoles.User])],
            [new ContributedPolicy(SpotContribution.Of<SupabaseShelf>(context), "Shelves are read by the signed in", "SELECT", RowAccessRoles.User, "{caller:signedin}", null)],
            [])
        : null);

    [Fact]
    public void Asked_by_key_in_csharp_an_access_function_says_only_the_database_can_answer()
    {
        var ticket = new Ticket(TicketId.CreateSequential(), "Watched", owner: null, team: null, TicketStatus.Open, isPublic: false);

        var ask = () => TicketWatchers.Allows(ticket.Id);

        ask.Should().Throw<DatabaseOnlyException>().Which.SqlText.Should().Be("desk.is_watcher(ticketId)");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
