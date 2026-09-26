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

    private SupabaseMigrationReport Export(DateTimeOffset? now = null, params RowAccessRule[] rules)
    {
        using var context = SupabaseShelfContext.Create();
        return SupabaseMigrations.Export(context, _directory, Options(now, rules));
    }

    private SupabaseMigrationReport Compare(params RowAccessRule[] rules)
    {
        using var context = SupabaseShelfContext.Create();
        return SupabaseMigrations.Compare(context, _directory, Options(null, rules));
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
            "CREATE POLICY \"Shelves by name\" ON public.\"Shelves\" FOR SELECT TO anon, authenticated\n" +
            "    USING (\"Name\" IS NOT DISTINCT FROM (SELECT auth.jwt() ->> 'shelf'));\n" +
            "COMMENT ON POLICY \"Shelves by name\" ON public.\"Shelves\" IS 'DDDToolkit row access rule';");
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
        AccessFiles().Should().ContainSingle();
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

        File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single()).Should().Contain("FOR SELECT TO anon, authenticated\n    USING (\"IsPublic\");");
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
            "CREATE POLICY \"TicketComment goes with its Tickets\" ON desk.\"TicketComment\" FOR ALL TO anon, authenticated\n" +
            "    USING (EXISTS (SELECT 1 FROM desk.\"Tickets\" parent WHERE parent.\"Id\" = \"TicketComment\".\"TicketId\"))");
        sql.Should().Contain("FOR ALL TO anon, authenticated\n    USING (((SELECT auth.uid()) IS NOT NULL) AND (\"Owner\" IS NOT DISTINCT FROM (SELECT auth.uid())))\n    WITH CHECK");
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
        sql.Should().Contain("FOR SELECT TO anon, authenticated\n    USING (desk.is_watcher(\"Id\"));", "the rule asks the function about the row");
        sql.IndexOf("CREATE OR REPLACE FUNCTION", StringComparison.Ordinal).Should().BeLessThan(sql.IndexOf("CREATE POLICY", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_context_that_maps_the_aggregate_writes_its_access_function()
    {
        var options = Options(null, [ShelvesByName]);
        options.RowAccessFunctions.Add(DeskRules.IsWatcher);

        using var context = SupabaseShelfContext.Create();
        SupabaseMigrations.Export(context, _directory, options);

        File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single()).Should().NotContain("CREATE OR REPLACE FUNCTION", "shelves know no tickets");
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

        File.ReadAllText(Directory.GetFiles(_directory, "*_access.*").Single()).Should().Contain("FOR SELECT TO anon, authenticated\n    USING (desk.is_watcher(\"Id\"));");
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
