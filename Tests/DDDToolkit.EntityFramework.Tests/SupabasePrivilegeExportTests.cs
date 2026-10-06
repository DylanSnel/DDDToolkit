using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What the Supabase export writes into a module's access file besides the policies: the tables' privileges and
/// forced row level security, which it writes unless the project turns them off, the role the application's
/// bookkeeping runs as, where the project names one, and the guard of an event log. With both switches off, every
/// file is the policies alone, as it was before there were switches. None of this opens a database.
/// </summary>
public sealed class SupabasePrivilegeExportTests : IDisposable
{
    private static readonly SupabaseMigrationSource Shelves = SupabaseMigrationSource.For(() => SupabaseShelfContext.Create());

    private static readonly RowAccessRule ShelvesByName = RowAccessRule.For<SupabaseShelf>(
        "Shelves by name", RowOperations.All, "({col:Name} IS NOT DISTINCT FROM {caller:claim:shelf})", RowAccessRoles.User);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ddd-privileges-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static SupabaseMigrationOptions Options(Action<SupabaseMigrationOptions>? configure = null, params RowAccessRule[] rules)
    {
        var options = new SupabaseMigrationOptions();
        foreach (var rule in rules)
        {
            options.RowAccessRules.Add(rule);
        }

        configure?.Invoke(options);
        return options;
    }

    private SupabaseMigrationReport Export(SupabaseMigrationOptions options)
    {
        using var context = SupabaseShelfContext.Create();
        return SupabaseMigrations.Export(context, _directory, options);
    }

    private string[] AccessFiles()
        => Directory.Exists(_directory) ? [.. Directory.GetFiles(_directory, "*_access.*.ddd.sql").Order(StringComparer.Ordinal)] : [];

    private string NewestAccessFile() => File.ReadAllText(AccessFiles()[^1]);

    private (int ExitCode, string Output) Build(RowAccessRule[] rules, string? roles = null, string? grants = null, string? force = null)
    {
        using var output = new StringWriter();
        var exitCode = SupabaseMigrationBuild.Run("Write", [Shelves], rules, [], [], _directory, start: null, roles, callerFunctions: null, grants, force, output);
        return (exitCode, output.ToString());
    }

    /// <summary>Turns off both switches the options have on by default.</summary>
    private static void BothOff(SupabaseMigrationOptions options)
    {
        options.WriteGrants = false;
        options.ForceRowLevelSecurity = false;
    }

    [Fact]
    public void By_default_the_access_file_writes_the_privileges_and_forces_the_policies()
    {
        new SupabaseMigrationOptions().Should().BeEquivalentTo(new { WriteGrants = true, ForceRowLevelSecurity = true });

        Export(Options(null, ShelvesByName)).IsInSync.Should().BeTrue();
        var written = NewestAccessFile();

        written.Should().Contain("-- Privileges, from the policies above")
            .And.Contain("GRANT SELECT, INSERT, DELETE ON TABLE public.\"Shelves\" TO authenticated;\n")
            .And.Contain("ALTER TABLE public.\"Shelves\" ENABLE ROW LEVEL SECURITY;\nALTER TABLE public.\"Shelves\" FORCE ROW LEVEL SECURITY;\n");

        // A build that leaves both properties out, or empty, or asks for both out loud, writes what the options do.
        Build([ShelvesByName]).Output.Should().Contain("Unchanged").And.NotContain("Created");
        Build([ShelvesByName], grants: " ", force: "").ExitCode.Should().Be(0);
        Build([ShelvesByName], grants: "write", force: "TRUE").ExitCode.Should().Be(0);

        AccessFiles().Should().ContainSingle("every one of them found the file in sync");
        NewestAccessFile().Should().Be(written);
    }

    [Fact]
    public void With_both_switches_off_the_access_file_is_the_policies_alone()
    {
        Export(Options(BothOff, ShelvesByName));
        var plain = NewestAccessFile();

        plain.Should().NotContain("-- Privileges").And.NotContain(" ON TABLE ").And.NotContain("FORCE");

        // A bookkeeping role alone changes no such file, and the build's two properties turn off what the options do.
        Export(Options(options => { BothOff(options); options.Roles = options.Roles with { System = "ddd_system" }; }, ShelvesByName)).IsInSync.Should().BeTrue();
        Build([ShelvesByName], roles: "system=ddd_system", grants: "None", force: "false").ExitCode.Should().Be(0);
        Build([ShelvesByName], grants: " none ", force: "FALSE").ExitCode.Should().Be(0);

        AccessFiles().Should().ContainSingle("every one of them found the file in sync");
        NewestAccessFile().Should().Be(plain);
    }

    [Fact]
    public void The_access_file_writes_the_privileges_of_every_role_the_build_names()
    {
        var (exitCode, output) = Build([ShelvesByName], roles: "system=ddd_system");

        exitCode.Should().Be(0, output);
        var sql = NewestAccessFile();
        sql.Should().Contain("-- Privileges, from the policies above")
            .And.Contain("REVOKE ALL ON TABLE public.\"Shelves\" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in;\n")
            .And.Contain("GRANT SELECT, INSERT, DELETE ON TABLE public.\"Shelves\" TO authenticated;\n")
            .And.Contain("GRANT UPDATE (\"Capacity\", \"Name\") ON TABLE public.\"Shelves\" TO authenticated;\n")
            .And.Contain("GRANT INSERT ON TABLE ddd.\"OutboxMessages\" TO authenticated, ddd_system_in;\n")
            .And.Contain("GRANT SELECT, DELETE ON TABLE ddd.\"OutboxMessages\" TO ddd_system;\n")
            .And.Contain("GRANT UPDATE (\"Attempts\", \"LastError\", \"NextAttemptAt\", \"ProcessedAt\") ON TABLE ddd.\"OutboxMessages\" TO ddd_system;\n")
            .And.Contain("        CREATE ROLE ddd_system NOLOGIN NOINHERIT;\n", "system=ddd_system reached the export, which makes the role");
        sql.Should().NotContain("TO anon;", "no rule is for a visitor, so a visitor gets nothing");

        // Asked for out loud, nothing changes; with the privileges left to the host, the file no longer says what the build wants.
        Build([ShelvesByName], roles: "system=ddd_system", grants: "write").Output.Should().Contain("Unchanged").And.NotContain("Created");
        using var context = SupabaseShelfContext.Create();
        SupabaseMigrations.Compare(context, _directory, Options(options => options.WriteGrants = false, ShelvesByName)).IsInSync
            .Should().BeFalse("a file written with privileges is not the file a build without them writes");
    }

    [Fact]
    public void A_module_without_a_rule_gets_an_access_file_for_its_outbox_unless_the_host_grants_the_privileges()
    {
        Export(Options(options => options.WriteGrants = false)).IsInSync.Should().BeTrue();
        AccessFiles().Should().BeEmpty("a module with an outbox and no rule has nothing to say while the host grants the privileges itself");

        Export(Options()).IsInSync.Should().BeTrue();

        AccessFiles().Should().ContainSingle();
        var sql = NewestAccessFile();
        sql.Should().StartWith("-- Written by DDDToolkit from the row access rules of SupabaseShelfContext.");
        sql.Should().Contain("REVOKE ALL ON TABLE ddd.\"OutboxMessages\" FROM PUBLIC, anon, authenticated, ddd_system_in;\n")
            .And.Contain("GRANT INSERT ON TABLE ddd.\"OutboxMessages\" TO authenticated, ddd_system_in;\n");
        sql.Should().NotContain("public.\"Shelves\"\n", "a table without a rule is not the script's to give or take");
        sql.Should().NotContain("ENABLE ROW LEVEL SECURITY");

        // The exported migrations do not start with a drop of policies for it: there are none to take off.
        Directory.GetFiles(_directory, "*.supabaseshelf.ddd.sql").Where(path => !path.Contains("_access.", StringComparison.Ordinal))
            .Select(File.ReadAllText).Should().OnlyContain(migration => !migration.Contains("DROP POLICY", StringComparison.Ordinal));
    }

    [Fact]
    public void A_module_with_an_event_log_and_no_rule_gets_an_access_file_for_its_guard_even_with_the_privileges_left_to_the_host()
    {
        using var annals = new AnnalsContext(new DbContextOptionsBuilder<AnnalsContext>().UseNpgsql("Host=nowhere.invalid").Options);

        var report = SupabaseMigrations.Export(annals, _directory, new SupabaseMigrationOptions { WriteGrants = false });

        report.IsInSync.Should().BeTrue();
        NewestAccessFile().Should().Contain("CREATE OR REPLACE TRIGGER ddd_kept_rows BEFORE UPDATE OR DELETE ON archive.\"Annals\"\n")
            .And.Contain("    FOR EACH ROW EXECUTE FUNCTION ddd.refuse_changes_to_kept_rows('', 'RecordedAt');\n", "a log kept for good lets no row go")
            .And.NotContain(" ON TABLE ", "the guard needs no privileges to be written");
        SupabaseMigrations.Compare(annals, _directory, new SupabaseMigrationOptions { WriteGrants = false }).IsInSync.Should().BeTrue();
    }

    [Fact]
    public void Force_follows_every_enable_of_an_access_file_and_leaves_the_migrations_alone()
    {
        Export(Options(options => options.ForceRowLevelSecurity = false, ShelvesByName));
        NewestAccessFile().Should().NotContain("FORCE", "the project turned it off");
        var migrations = Directory.GetFiles(_directory).Where(path => !path.Contains("_access.", StringComparison.Ordinal)).ToDictionary(path => path, File.ReadAllText);
        migrations.Values.Should().Contain(migration => migration.Contains("ALTER TABLE \"Shelves\" ENABLE ROW LEVEL SECURITY;", StringComparison.Ordinal), "a new table of an exposed schema gets row level security in its migration");

        // A build that leaves the property out forces it.
        var (exitCode, output) = Build([ShelvesByName]);

        exitCode.Should().Be(0, output);
        AccessFiles().Should().HaveCount(2, "the file the build wants says more than the one there");
        NewestAccessFile().Should().Contain("ALTER TABLE public.\"Shelves\" ENABLE ROW LEVEL SECURITY;\nALTER TABLE public.\"Shelves\" FORCE ROW LEVEL SECURITY;\n");
        foreach (var (path, before) in migrations)
        {
            File.ReadAllText(path).Should().Be(before, "an exported migration is never rewritten");
        }
    }

    [Fact]
    public void A_bookkeeping_role_of_the_platforms_is_refused_before_anything_is_written_while_the_files_write_the_privileges()
    {
        // By hand, through either form, as the build refuses the pair: every file would make the role and refuse it
        // where it is applied, since service_role bypasses row level security.
        var options = Options(options => options.Roles = options.Roles with { System = "service_role" }, ShelvesByName);
        const string refused = "The bookkeeping role, Roles.System, is not one the access files can make. 'service_role' is one of Postgres's or Supabase's own roles.*Leave it unset where the system caller runs as service_role, or name a role of the application's own, such as ddd_system.";
        ((Action)(() => Export(options))).Should().Throw<InvalidOperationException>().WithMessage(refused);
        ((Action)(() => SupabaseMigrations.Export([Shelves], _directory, options))).Should().Throw<InvalidOperationException>().WithMessage(refused);
        Directory.Exists(_directory).Should().BeFalse("nothing is written for a role no file could make");

        // With the privileges left to the host, a file makes the bookkeeping role only where something is for it by
        // its symbol, so the pair changes nothing: the file is the one a build without it writes.
        var (exitCode, output) = Build([ShelvesByName], roles: "system=service_role", grants: "None");

        exitCode.Should().Be(0, output);
        NewestAccessFile().Should().NotContain("service_role");
        Build([ShelvesByName], grants: "None").Output.Should().Contain("Unchanged").And.NotContain("Created");
    }

    [Theory]
    [InlineData(null, "Yes", null, "error : SupabaseRowAccessGrants is 'Yes'. Use Write, the default, to have the access files write the tables' privileges from the policies, or None to grant them yourself.")]
    [InlineData(null, null, "maybe", "error : SupabaseForceRowLevelSecurity is 'maybe'. Use true, the default, to force row level security on every table an access file turns it on for, or false to leave the tables' owner outside the policies.")]
    [InlineData("system=authenticated", null, null, "error : SupabaseRowAccessRoles has 'system=authenticated'. 'authenticated' is the role the application's own bookkeeping runs as, and the role of a signed-in user as well. It holds the outbox, the inbox and the migration history, which no caller may reach, so it is a role of its own.")]
    [InlineData("system-in=worker|system=worker", null, null, "error : SupabaseRowAccessRoles has 'system=worker'. 'worker' is the role the application's own bookkeeping runs as, and the scoped system role as well. It holds the outbox, the inbox and the migration history, which no caller may reach, so it is a role of its own.")]
    [InlineData("token:analyst=shelf_analyst|system=shelf_analyst", null, null, "error : SupabaseRowAccessRoles has 'system=shelf_analyst'. 'shelf_analyst' is the role the application's own bookkeeping runs as, and the role of the token role 'analyst' as well. It holds the outbox, the inbox and the migration history, which no caller may reach, so it is a role of its own.")]
    [InlineData("system=service_role", null, null, "error : SupabaseRowAccessRoles has 'system=service_role'. 'service_role' is one of Postgres's or Supabase's own roles. While the access files write the privileges, each makes the bookkeeping role, without a login and held to row level security, and gives it the outbox, the inbox and the migration history: a role of the platform's is none the application makes, and one that can log in or bypass row level security fails every file where it is applied. Leave the pair out where the system caller runs as service_role, or name a role of the application's own, such as ddd_system.")]
    [InlineData("system=supabase_admin", "Write", null, "error : SupabaseRowAccessRoles has 'system=supabase_admin'. 'supabase_admin' is one of Postgres's or Supabase's own roles. While the access files write the privileges, each makes the bookkeeping role, without a login and held to row level security, and gives it the outbox, the inbox and the migration history: a role of the platform's is none the application makes, and one that can log in or bypass row level security fails every file where it is applied. Leave the pair out where the system caller runs as service_role, or name a role of the application's own, such as ddd_system.")]
    [InlineData("system=PUBLIC", null, null, "error : SupabaseRowAccessRoles has 'system=PUBLIC'. PUBLIC is every role there is, the application's own and the system's included, and no policy is ever for it. Use a role such as ddd_system.")]
    [InlineData("system=ddd_system|System=other", null, null, "error : SupabaseRowAccessRoles has the key 'System' twice. It takes user, anonymous, system-in and system, each at most once and separated by '|', as in user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system, and token:<role> for each token role the host mapped, as in token:analyst=desk_analyst.")]
    public void A_malformed_switch_or_system_role_is_an_error_line_and_exit_code_2(string? roles, string? grants, string? force, string error)
    {
        var (exitCode, output) = Build([ShelvesByName], roles, grants, force);

        exitCode.Should().Be(2, "the export could not run as the project asked");
        output.TrimEnd().Should().Be(error, "the whole line is what the build shows");
        Directory.Exists(_directory).Should().BeFalse("nothing is written with settings the project did not mean");
    }
}
