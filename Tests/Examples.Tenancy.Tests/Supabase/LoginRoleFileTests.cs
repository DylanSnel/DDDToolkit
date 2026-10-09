using System.Xml.Linq;
using DDDToolkit.EntityFramework.Supabase;
using Examples.Tenancy.Inspections.Infrastructure.Persistence;
using Examples.Tenancy.Projects.Infrastructure.Persistence;
using Examples.Tenancy.Tenants.Infrastructure.Persistence;
using FluentAssertions;
using Npgsql;

namespace Examples.Tenancy.Tests.Supabase;

/// <summary>
/// The migration the Supabase export writes for the role an application logs in as, <c>SupabaseLoginRole</c>,
/// applied where such a file is applied: to Supabase's own Postgres, after the access files that make the roles it
/// grants, by the role the Supabase CLI applies migrations as, which is no superuser. The file is written for a role
/// of the test's own, from the exporter's own roles, because roles are the server's: the sample's login role has had
/// its login turned on by the run.
/// </summary>
/// <remarks>
/// It needs Docker and Supabase's Postgres image, so it carries the samples' traits and stays out of the runs that
/// have neither. The export's own tests say what the file holds and when another is written; this says what it does.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class LoginRoleFileTests(SampleSupabaseStack stack)
{
    /// <summary>The roles the exporter maps, which the file makes the login role a member of, by name.</summary>
    private static readonly string[] MappedRoles = ["anon", "authenticated", "ddd_system", "ddd_system_in", "tenancy_operator"];

    private static int probes;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_file_makes_a_role_without_a_login_that_may_only_become_the_callers_and_the_host_starts_as_it_once_it_logs_in()
    {
        var login = ProbeRole();
        var database = await SampleOnPostgres.MigratedAsync(stack, Cancellation);
        var sql = ExportedFor(login);

        try
        {
            // As the CLI applies a file, after every file of the sample's, and again: a later file that says the same
            // does the same, which is what the next one written for a role that changed nothing else does.
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, sql, Cancellation);
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, sql, Cancellation);

            await using var migration = new NpgsqlConnection(database.AsMigrationRole);
            await migration.OpenAsync(Cancellation);

            (await RowAsync(migration, $"SELECT rolcanlogin, rolinherit, rolsuper, rolbypassrls, rolcreaterole, rolcreatedb, rolreplication FROM pg_roles WHERE rolname = '{login}'"))
                .Should().Equal([false, false, false, false, false, false, false], "the role cannot log in until the deployment says so, and has none of the privileges of the roles it is given");

            (await MembershipsAsync(database, login)).Should().Equal(MappedRoles, "it is a member of the roles the exporter maps, and of nothing else");

            // Of none of them has it the privileges without switching to it first.
            (await ListAsync(migration, $"SELECT rolname::text FROM pg_roles WHERE rolname <> '{login}' AND pg_has_role('{login}', oid, 'USAGE')"))
                .Should().BeEmpty();

            // Nothing is granted to the role itself: no table, column, sequence, schema or function.
            (await ListAsync(migration, $"""
                SELECT c.oid::regclass::text FROM pg_class c CROSS JOIN LATERAL aclexplode(c.relacl) acl WHERE acl.grantee = '{login}'::regrole
                UNION ALL
                SELECT a.attrelid::regclass::text || '.' || a.attname FROM pg_attribute a CROSS JOIN LATERAL aclexplode(a.attacl) acl WHERE acl.grantee = '{login}'::regrole
                UNION ALL
                SELECT n.nspname::text FROM pg_namespace n CROSS JOIN LATERAL aclexplode(n.nspacl) acl WHERE acl.grantee = '{login}'::regrole
                UNION ALL
                SELECT p.oid::regprocedure::text FROM pg_proc p CROSS JOIN LATERAL aclexplode(p.proacl) acl WHERE acl.grantee = '{login}'::regrole
                """)).Should().BeEmpty();

            // Nor does it reach a table of the sample through the roles it is given, short of switching to one of them.
            (await ListAsync(migration, $"""
                SELECT c.oid::regclass::text FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname IN ('{TenantsContext.Schema}', '{ProjectsContext.Schema}', '{InspectionsContext.Schema}', 'ddd') AND c.relkind IN ('r', 'p')
                  AND (has_table_privilege('{login}', c.oid, 'SELECT') OR has_table_privilege('{login}', c.oid, 'INSERT')
                       OR has_table_privilege('{login}', c.oid, 'UPDATE') OR has_table_privilege('{login}', c.oid, 'DELETE'))
                """)).Should().BeEmpty();

            // The deployment turns the login on, once, and the host starts as that role: every start-up check passes,
            // the one that asks whether it may become every caller among them, and a person's request is answered.
            var password = Guid.NewGuid().ToString("N");
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"ALTER ROLE {login} WITH LOGIN PASSWORD '{password}'", Cancellation);

            await using var sample = await SampleOnPostgres.CreateAsync(stack, Cancellation, connectAs: seeded => seeded.As(login, password));
            using var tove = await sample.Host.ClientAsync("tove", DemoData.Harbor.Slug);
            (await tove.VisibleProjectsAsync()).Names().Should().Equal("Bay bridge");
        }
        finally
        {
            // Roles are the server's: the next test finds the server as this one did. Its memberships go with it.
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"DROP ROLE IF EXISTS {login}", CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_role_callers_no_longer_run_as_is_taken_back_by_the_next_file()
    {
        var login = ProbeRole();
        var database = await SampleOnPostgres.MigratedAsync(stack, Cancellation);

        // The exporter's roles, and then the same without the operators' token role: the second build writes a
        // second file, which says what the role is now.
        var roles = ExporterRoles();
        var withoutOperators = string.Join('|', roles.Split('|').Where(pair => !pair.StartsWith("token:", StringComparison.Ordinal)));
        var files = ExportedFor(login, roles, withoutOperators);

        try
        {
            foreach (var file in files)
            {
                await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, file, Cancellation);
            }

            (await MembershipsAsync(database, login))
                .Should().Equal(["anon", "authenticated", "ddd_system", "ddd_system_in"], "the role no caller runs as any more is one the login role may no longer switch to");

            // And applied once more, as a database that is as far as the second file and meets a later one that says the same.
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, files[^1], Cancellation);
        }
        finally
        {
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"DROP ROLE IF EXISTS {login}", CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_role_of_that_name_that_may_create_roles_or_replicate_is_refused_and_the_hint_names_what_to_alter()
    {
        var login = ProbeRole();
        var database = await SampleOnPostgres.MigratedAsync(stack, Cancellation);
        var sql = ExportedFor(login);

        try
        {
            // Made by hand before the file, owning and holding nothing. Each attribute is a way past the policies that
            // a statement on its connection takes as the role itself: before Postgres 16 a role that may create roles
            // grants itself service_role, and one that may replicate reads every change to every table from a slot.
            foreach (var attribute in (string[])["CREATEROLE", "REPLICATION"])
            {
                await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"DROP ROLE IF EXISTS {login}; CREATE ROLE {login} NOLOGIN NOINHERIT {attribute};", Cancellation);

                var apply = () => SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, sql, Cancellation);
                var refused = (await apply.Should().ThrowAsync<PostgresException>()).Which;
                refused.MessageText.Should().StartWith($"The role {login} exists, and is a superuser, may bypass row level security, may create roles, may replicate or has the privileges");
                refused.Hint.Should().Be($"ALTER ROLE {login} NO{attribute};", "the hint names what the role has, and so nothing only a superuser may write");
                (await MembershipsAsync(database, login)).Should().BeEmpty("the file stops before it grants anything");
            }

            // Altered as the hint says, by the role the migrations run as, and the file applies.
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"ALTER ROLE {login} NOREPLICATION;", Cancellation);
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, sql, Cancellation);
            (await MembershipsAsync(database, login)).Should().Equal(MappedRoles);
        }
        finally
        {
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"DROP ROLE IF EXISTS {login}", CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_role_of_that_name_with_a_grant_it_inherits_is_refused_though_it_was_altered_to_inherit_no_more()
    {
        var login = ProbeRole();
        var database = await SampleOnPostgres.MigratedAsync(stack, Cancellation);
        var sql = ExportedFor(login);

        try
        {
            // Made by hand as a role is by default, given a caller's role, and then altered as the file would have it.
            // Since Postgres 16 a grant says whether it is inherited, as the role was when it was granted, and the
            // alteration holds only for the grants after it: the role has anon's privileges without switching.
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"CREATE ROLE {login} NOLOGIN INHERIT; GRANT anon TO {login}; ALTER ROLE {login} NOINHERIT;", Cancellation);
            await using (var migration = new NpgsqlConnection(database.AsMigrationRole))
            {
                await migration.OpenAsync(Cancellation);
                (await RowAsync(migration, $"SELECT rolinherit, pg_has_role('{login}', 'anon', 'USAGE') FROM pg_roles WHERE rolname = '{login}'"))
                    .Should().Equal([false, true], "the role says it inherits nothing, and has anon's privileges all the same");
            }

            var apply = () => SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, sql, Cancellation);
            var refused = (await apply.Should().ThrowAsync<PostgresException>()).Which;
            refused.MessageText.Should().StartWith($"The role {login} has the privileges of anon without switching to them");
            refused.Hint.Should().Be($"GRANT anon TO {login} WITH INHERIT FALSE;");
            (await MembershipsAsync(database, login)).Should().Equal(["anon"], "the file stops before it grants anything");

            // As the hint says, and the file applies.
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, refused.Hint, Cancellation);
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, sql, Cancellation);
            (await MembershipsAsync(database, login)).Should().Equal(MappedRoles);
        }
        finally
        {
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"DROP ROLE IF EXISTS {login}", CancellationToken.None);
        }
    }

    [Fact]
    public async Task Two_migrations_that_apply_the_file_at_the_same_moment_both_finish()
    {
        var login = ProbeRole();
        var database = await SampleOnPostgres.MigratedAsync(stack, Cancellation);
        var sql = ExportedFor(login);

        try
        {
            // Roles are the server's, so the migrations of two databases on one server make and grant the same ones.
            // First on a server without the role: the second waits for the first to make it, and finds it made.
            await AppliedAtTheSameMomentAsync(database, sql);

            // Then with the role there and given nothing, as it is where a later file grants a role mapped since: the
            // second waits for the first to grant them, and finds them granted. Postgres 16 and later make it wait
            // before it grants, on the role granted; before 16 the slower grant itself failed on the catalog's index.
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"REVOKE {string.Join(", ", MappedRoles)} FROM {login};", Cancellation);
            await AppliedAtTheSameMomentAsync(database, sql);

            (await MembershipsAsync(database, login)).Should().Equal(MappedRoles);
        }
        finally
        {
            await SampleSupabaseStack.ExecuteAsync(database.AsMigrationRole, $"DROP ROLE IF EXISTS {login}", CancellationToken.None);
        }
    }

    /// <summary>A role of the calling test's own, to write a file for: roles are the server's, and every test drops its own.</summary>
    private static string ProbeRole() => $"tenancy_probe_{Interlocked.Increment(ref probes)}_{Guid.NewGuid():N}"[..40];

    /// <summary>
    /// <paramref name="sql"/> applied in two transactions at once, as two migrations of one server apply it: the second
    /// starts while the first has not committed, and waits for it on a lock. Both have to finish.
    /// </summary>
    private static async Task AppliedAtTheSameMomentAsync(SupabaseDatabase database, string sql)
    {
        await using var first = new NpgsqlConnection(database.AsMigrationRole);
        await using var second = new NpgsqlConnection(database.AsMigrationRole);
        await using var watching = new NpgsqlConnection(database.AsMigrationRole);
        await first.OpenAsync(Cancellation);
        await second.OpenAsync(Cancellation);
        await watching.OpenAsync(Cancellation);

        await using var firstTransaction = await first.BeginTransactionAsync(Cancellation);
        await using (var applied = new NpgsqlCommand(sql, first, firstTransaction))
        {
            await applied.ExecuteNonQueryAsync(Cancellation);
        }

        await using var secondTransaction = await second.BeginTransactionAsync(Cancellation);
        await using var applying = new NpgsqlCommand(sql, second, secondTransaction);
        var running = applying.ExecuteNonQueryAsync(Cancellation);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using var waiting = new NpgsqlCommand($"SELECT wait_event_type FROM pg_stat_activity WHERE pid = {second.ProcessID}", watching);
            if (await waiting.ExecuteScalarAsync(Cancellation) is "Lock")
            {
                break;
            }

            running.IsCompleted.Should().BeFalse("the second migration waits for the first, which has not committed");
            DateTime.UtcNow.Should().BeBefore(deadline, "the second migration comes to wait on a lock the first holds");
            await Task.Delay(50, Cancellation);
        }

        await firstTransaction.CommitAsync(Cancellation);
        await running;
        await secondTransaction.CommitAsync(Cancellation);
    }

    /// <summary>The roles the exporter's project maps, its <c>SupabaseRowAccessRoles</c>.</summary>
    private static string ExporterRoles()
        => XDocument.Load(SampleLayout.ProgramProjectFile(SampleLayout.Exporter)).Descendants("SupabaseRowAccessRoles").Single().Value;

    /// <summary>The login role file the exporter's build would write for <paramref name="login"/>, with its own roles.</summary>
    private static string ExportedFor(string login) => ExportedFor(login, ExporterRoles())[0];

    /// <summary>
    /// The login role files the exporter's build would write for <paramref name="login"/>, one build after another
    /// with each of <paramref name="roles"/> as its <c>SupabaseRowAccessRoles</c>, oldest first: the build's own
    /// export into a directory of its own, from one of the sample's modules. Which module does not matter: the file is
    /// written from the roles alone.
    /// </summary>
    private static string[] ExportedFor(string login, params string[] roles)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tenancy-login-role-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var mapped in roles)
            {
                using var output = new StringWriter();
                SupabaseMigrationBuild.Run(
                    "Write", [SupabaseMigrationSource.For<InspectionsContext, InspectionsContextDesignTimeFactory>()], [], [], [], directory, start: null,
                    mapped, callerFunctions: null, grants: null, force: null, loginRole: login, output)
                    .Should().Be(0, output.ToString());
            }

            return [.. Directory.GetFiles(directory, $"*_login_role.{login}.ddd.sql").Should().HaveCount(roles.Length).And.Subject
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText)];
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The roles <paramref name="login"/> is a member of, by name.</summary>
    private static async Task<List<string>> MembershipsAsync(SupabaseDatabase database, string login)
    {
        await using var connection = new NpgsqlConnection(database.AsMigrationRole);
        await connection.OpenAsync(Cancellation);
        return await ListAsync(connection, $"SELECT DISTINCT granted.rolname::text FROM pg_auth_members m JOIN pg_roles granted ON granted.oid = m.roleid WHERE m.member = '{login}'::regrole ORDER BY 1");
    }

    /// <summary>The one row <paramref name="sql"/> answers.</summary>
    private static async Task<object?[]> RowAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        (await reader.ReadAsync(Cancellation)).Should().BeTrue("'{0}' answers a row", sql);

        var row = new object?[reader.FieldCount];
        for (var column = 0; column < row.Length; column++)
        {
            row[column] = await reader.IsDBNullAsync(column, Cancellation) ? null : reader.GetValue(column);
        }

        return row;
    }

    /// <summary>The first column of every row <paramref name="sql"/> answers, as text.</summary>
    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        var values = new List<string>();
        while (await reader.ReadAsync(Cancellation))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
