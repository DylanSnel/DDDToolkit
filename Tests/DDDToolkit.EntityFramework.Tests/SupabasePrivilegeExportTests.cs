using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What the Supabase export writes into a module's access file when the build asks for more than the policies:
/// the tables' privileges, forced row level security, the role the application's bookkeeping runs as, and the
/// guard of an event log. All of it is off unless asked for, and then every file is what it was. None of this
/// opens a database.
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

    [Fact]
    public void Without_the_option_the_export_writes_no_privileges()
    {
        Export(Options(null, ShelvesByName));
        var plain = NewestAccessFile();

        plain.Should().NotContain("-- Privileges").And.NotContain(" ON TABLE ").And.NotContain("FORCE");

        // A bookkeeping role alone changes no file, and neither does saying no to both switches out loud.
        Export(Options(options => options.Roles = options.Roles with { System = "ddd_system" }, ShelvesByName)).IsInSync.Should().BeTrue();
        Build([ShelvesByName], roles: "system=ddd_system", grants: "None", force: "false").ExitCode.Should().Be(0);
        Build([ShelvesByName], grants: " ", force: "FALSE").ExitCode.Should().Be(0);

        AccessFiles().Should().ContainSingle("every one of them found the file in sync");
        NewestAccessFile().Should().Be(plain);
    }

    [Fact]
    public void The_access_file_writes_privileges_when_the_build_asks()
    {
        var (exitCode, output) = Build([ShelvesByName], roles: "system=ddd_system", grants: "Write");

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

        // Asked for again, nothing changes; asked for without the privileges, the file no longer says what the build wants.
        Build([ShelvesByName], roles: "system=ddd_system", grants: "write").Output.Should().Contain("Unchanged").And.NotContain("Created");
        using var context = SupabaseShelfContext.Create();
        SupabaseMigrations.Compare(context, _directory, Options(null, ShelvesByName)).IsInSync.Should().BeFalse("a file written with privileges is not the file a build without them writes");
    }

    [Fact]
    public void A_module_without_a_rule_gets_an_access_file_for_its_outbox_once_privileges_are_written()
    {
        Export(Options()).IsInSync.Should().BeTrue();
        AccessFiles().Should().BeEmpty("a module with an outbox and no rule has nothing to say while the host grants the privileges itself");

        Export(Options(options => options.WriteGrants = true)).IsInSync.Should().BeTrue();

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
    public void A_module_with_an_event_log_and_no_rule_gets_an_access_file_for_its_guard()
    {
        using var annals = new AnnalsContext(new DbContextOptionsBuilder<AnnalsContext>().UseNpgsql("Host=nowhere.invalid").Options);

        var report = SupabaseMigrations.Export(annals, _directory, new SupabaseMigrationOptions());

        report.IsInSync.Should().BeTrue();
        NewestAccessFile().Should().Contain("CREATE OR REPLACE TRIGGER ddd_kept_rows BEFORE UPDATE OR DELETE ON archive.\"Annals\"\n")
            .And.Contain("    FOR EACH ROW EXECUTE FUNCTION ddd.refuse_changes_to_kept_rows('', 'RecordedAt');\n", "a log kept for good lets no row go")
            .And.NotContain(" ON TABLE ", "the guard needs no privileges to be written");
        SupabaseMigrations.Compare(annals, _directory, new SupabaseMigrationOptions()).IsInSync.Should().BeTrue();
    }

    [Fact]
    public void Force_follows_every_enable_of_an_access_file_and_leaves_the_migrations_alone()
    {
        Export(Options(null, ShelvesByName));
        var migrations = Directory.GetFiles(_directory).Where(path => !path.Contains("_access.", StringComparison.Ordinal)).ToDictionary(path => path, File.ReadAllText);
        migrations.Values.Should().Contain(migration => migration.Contains("ALTER TABLE \"Shelves\" ENABLE ROW LEVEL SECURITY;", StringComparison.Ordinal), "a new table of an exposed schema gets row level security in its migration");

        var (exitCode, output) = Build([ShelvesByName], force: "true");

        exitCode.Should().Be(0, output);
        NewestAccessFile().Should().Contain("ALTER TABLE public.\"Shelves\" ENABLE ROW LEVEL SECURITY;\nALTER TABLE public.\"Shelves\" FORCE ROW LEVEL SECURITY;\n");
        foreach (var (path, before) in migrations)
        {
            File.ReadAllText(path).Should().Be(before, "an exported migration is never rewritten");
        }
    }

    [Theory]
    [InlineData(null, "Yes", null, "error : SupabaseRowAccessGrants is 'Yes'. Use Write to have the access files write the tables' privileges from the policies, or None to grant them yourself.")]
    [InlineData(null, null, "maybe", "error : SupabaseForceRowLevelSecurity is 'maybe'. Use true to force row level security on every table an access file turns it on for, or false to leave the tables' owner outside the policies.")]
    [InlineData("system=authenticated", null, null, "error : SupabaseRowAccessRoles has 'system=authenticated'. 'authenticated' is the role the application's own bookkeeping runs as, and the role of a signed-in user as well. It holds the outbox, the inbox and the migration history, which no caller may reach, so it is a role of its own.")]
    [InlineData("system-in=worker|system=worker", null, null, "error : SupabaseRowAccessRoles has 'system=worker'. 'worker' is the role the application's own bookkeeping runs as, and the scoped system role as well. It holds the outbox, the inbox and the migration history, which no caller may reach, so it is a role of its own.")]
    [InlineData("token:analyst=shelf_analyst|system=shelf_analyst", null, null, "error : SupabaseRowAccessRoles has 'system=shelf_analyst'. 'shelf_analyst' is the role the application's own bookkeeping runs as, and the role of the token role 'analyst' as well. It holds the outbox, the inbox and the migration history, which no caller may reach, so it is a role of its own.")]
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
