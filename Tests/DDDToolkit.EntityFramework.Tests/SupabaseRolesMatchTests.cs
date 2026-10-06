using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Startup;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The start-up check that the roles a host switches to are the ones the access files in its database were written
/// for: what an exported access file records on the <c>ddd</c> schema, read back from a database it was applied to,
/// and compared with the host's options. The database decides, so these run on Postgres.
/// </summary>
public sealed class SupabaseRolesMatchTests(ExplicitCallersPostgres postgres) : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>What every message of the check ends with.</summary>
    private const string CheckCloses =
        "A policy for a role no caller runs as lets nobody in, and a caller whose role the files never named finds nothing, or is refused. " +
        "Where the project that exports says these roles already, the database has not had the access files its last build wrote: apply them.";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ddd-roles-match-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_host_that_switches_to_the_roles_the_files_record_starts()
    {
        var database = await postgres.CreateDatabaseAsync(Cancellation);
        await ApplyRecordAsync(database, SupabaseRowLevelSecurity.DefaultRoles with { TokenRoles = new Dictionary<string, string> { ["analyst"] = "desk_analyst" } });

        // The defaults, and in code the token role the exporter maps, as a host whose export runs elsewhere writes it.
        await RunCheckAsync(database, options => options.TokenRoles["analyst"] = "desk_analyst");
        (await CommentAsync(database)).Should().StartWith("DDDToolkit row access roles: {\"user\":\"authenticated\"", "the record is the comment on the toolkit's own schema");
    }

    [Fact]
    public async Task A_database_without_a_record_has_nothing_to_compare_and_the_host_starts()
    {
        var database = await postgres.CreateDatabaseAsync(Cancellation);

        await RunCheckAsync(database, options => options.UserRole = "members");

        await ExecuteAsync(database, "CREATE SCHEMA ddd; COMMENT ON SCHEMA ddd IS 'the toolkit''s schema';");
        await RunCheckAsync(database, options => options.UserRole = "members");
    }

    [Fact]
    public async Task A_host_that_differs_is_stopped_with_every_difference_and_both_fixes()
    {
        var database = await postgres.CreateDatabaseAsync(Cancellation);
        await ApplyRecordAsync(database, SupabaseRowLevelSecurity.DefaultRoles with
        {
            System = "desk_books",
            TokenRoles = new Dictionary<string, string> { ["analyst"] = "desk_analyst", ["auditor"] = "desk_auditor" },
        });

        var stopped = () => RunCheckAsync(database, options =>
        {
            options.UserRole = "members";
            options.TokenRoles["analyst"] = "desk_reader";
            options.TokenRoles["intern"] = "desk_intern";
        });

        (await stopped.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(
            "The access files in the database of 'SupabaseShelfContext' were written for other roles than the ones this host switches to:\n" +
            "- a signed-in user: the policies are for authenticated, and this host runs one as members. Fix: user=authenticated in this host's SupabaseRowAccessRoles, or options.UserRole = \"authenticated\" in its code; or user=members in SupabaseRowAccessRoles of the project that exports.\n" +
            "- the system caller: the files give the outbox, the inbox and the migration history to desk_books, and this host's system caller runs as ddd_system. Fix: system=desk_books in this host's SupabaseRowAccessRoles, or options.SystemRole = \"desk_books\" in its code; or system=ddd_system in SupabaseRowAccessRoles of the project that exports.\n" +
            "- the token role 'analyst': the policies are for desk_analyst, and this host runs it as desk_reader. Fix: token:analyst=desk_analyst in this host's SupabaseRowAccessRoles, or options.TokenRoles[\"analyst\"] = \"desk_analyst\" in its code; or token:analyst=desk_reader in SupabaseRowAccessRoles of the project that exports.\n" +
            "- the token role 'auditor': the policies are for desk_auditor, and this host maps it to no role. Fix: token:auditor=desk_auditor in this host's SupabaseRowAccessRoles, or options.TokenRoles[\"auditor\"] = \"desk_auditor\" in its code; or take token:auditor=desk_auditor out of SupabaseRowAccessRoles of the project that exports.\n" +
            "- the token role 'intern': this host runs it as desk_intern, and the files write nothing for it, so no policy is for that role and no file made it. Fix: token:intern=desk_intern in SupabaseRowAccessRoles of the project that exports; or take token:intern=desk_intern out of this host's SupabaseRowAccessRoles, or options.TokenRoles[\"intern\"] out of its code.\n" +
            CheckCloses);
    }

    [Fact]
    public async Task Files_without_a_bookkeeping_role_expect_the_login_role_or_one_of_the_platforms()
    {
        var database = await postgres.CreateDatabaseAsync(Cancellation);
        await ApplyRecordAsync(database, SupabaseRowLevelSecurity.DefaultRoles with { System = null });

        await RunCheckAsync(database, options => options.SystemRole = null);
        await RunCheckAsync(database, options => options.SystemRole = SupabaseRowLevelSecurity.ServiceRole);

        var stopped = () => RunCheckAsync(database, configure: null);
        (await stopped.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(
            "- the system caller: the files make no bookkeeping role (system=none), and this host's system caller runs as ddd_system. Fix: system=none in this host's SupabaseRowAccessRoles, or options.SystemRole = null in its code; or system=ddd_system in SupabaseRowAccessRoles of the project that exports.");

        // And the other way round: files that gave the bookkeeping to ddd_system, and a host that runs it as the login role.
        await ApplyRecordAsync(database, SupabaseRowLevelSecurity.DefaultRoles);
        var asLogin = () => RunCheckAsync(database, options => options.SystemRole = null);
        (await asLogin.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(
            "- the system caller: the files give the outbox, the inbox and the migration history to ddd_system, and this host's system caller runs as the role it logs in as. Fix: system=ddd_system in this host's SupabaseRowAccessRoles, or options.SystemRole = \"ddd_system\" in its code; or system=none in SupabaseRowAccessRoles of the project that exports.");
    }

    [Fact]
    public async Task The_newest_file_applied_says_the_roles_and_a_host_without_a_scoped_system_role_is_not_asked_about_it()
    {
        var database = await postgres.CreateDatabaseAsync(Cancellation);
        await ApplyRecordAsync(database, SupabaseRowLevelSecurity.DefaultRoles with { User = "desk_user" });
        await ApplyRecordAsync(database, SupabaseRowLevelSecurity.DefaultRoles with { SystemIn = "desk_scoped" });

        await RunCheckAsync(database, options => options.SystemInRole = "desk_scoped");
        await RunCheckAsync(database, options => options.SystemInRole = null);

        // By hand, as a host that runs its checks itself calls it: with the context alone, whose interceptor has the roles.
        var services = new ServiceCollection();
        services.AddSupabaseRowLevelSecurity();
        services.AddDbContext<SupabaseShelfContext>((provider, options) => options.UseNpgsql(database).UseSupabaseRowLevelSecurity(provider));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SupabaseShelfContext>();
        var byHand = () => SupabaseRowAccessChecks.EnsureRolesMatchAccessFilesAsync(context, Cancellation);
        (await byHand.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(
            "- the application's own work inside the policies: they are for desk_scoped, and this host runs it as ddd_system_in. Fix: system-in=desk_scoped in this host's SupabaseRowAccessRoles, or options.SystemInRole = \"desk_scoped\" in its code; or system-in=ddd_system_in in SupabaseRowAccessRoles of the project that exports.");

        // A context that runs as the role the application logs in as switches to no caller's role, so it is told how to wire one.
        await using var unwired = new SupabaseShelfContext(new DbContextOptionsBuilder<SupabaseShelfContext>().UseNpgsql(database).Options);
        var asked = () => SupabaseRowAccessChecks.EnsureRolesMatchAccessFilesAsync(unwired, Cancellation);
        (await asked.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().StartWith(
            "'SupabaseShelfContext' does not run its commands as the caller: its options have no PostgresRowLevelSecurityInterceptor, so it switches to no role the access files could have been written for.");
    }

    /// <summary>
    /// Exports the shelf context's access file with <paramref name="roles"/> and runs, as the owner, the block it ends
    /// with: the record, as every access file writes it.
    /// </summary>
    private async Task ApplyRecordAsync(string database, RowAccessRoleNames roles)
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        using (var context = SupabaseShelfContext.Create())
        {
            SupabaseMigrations.Export(context, _directory, new SupabaseMigrationOptions { Roles = roles }).IsInSync.Should().BeTrue();
        }

        var sql = File.ReadAllText(Directory.GetFiles(_directory, "*_access.*.ddd.sql").Should().ContainSingle().Subject);
        var record = sql.IndexOf("-- The roles the policies and the privileges above are written for", StringComparison.Ordinal);
        record.Should().BePositive("every access file ends with the record");
        await ExecuteAsync(database, sql[record..]);
    }

    /// <summary>
    /// Runs the check as the runner would, over a host with the shelf context on <paramref name="database"/> and row
    /// level security registered with <paramref name="configure"/>.
    /// </summary>
    private static async Task RunCheckAsync(string database, Action<PostgresRowLevelSecurityOptions>? configure)
    {
        var services = new ServiceCollection();
        services.AddSupabaseRowLevelSecurity(configure);
        services.AddDbContext<SupabaseShelfContext>((provider, options) => options.UseNpgsql(database).UseSupabaseRowLevelSecurity(provider));

        var check = services.GetStartupChecks().InOrder().Single(registered => registered.Name == SupabaseRowAccessChecks.RolesMatchAccessFilesCheck);
        check.Stage.Should().Be(StartupCheckStage.Login);
        check.RunsBefore.Should().Equal(PostgresRowAccessChecks.LoginRoleMaySwitchToCallersCheck);

        await using var provider = services.BuildServiceProvider();
        await check.RunAsync(provider, Cancellation);
    }

    private static async Task<string?> CommentAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT obj_description('ddd'::regnamespace, 'pg_namespace')", connection);
        return await command.ExecuteScalarAsync(Cancellation) as string;
    }

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }
}
