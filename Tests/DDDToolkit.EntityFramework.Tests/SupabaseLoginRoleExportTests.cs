using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The migration the Supabase export writes for the role the application logs in as, <c>SupabaseLoginRole</c>:
/// what it says, where it goes among the other files, when another one is written, which names are refused, and
/// that without the property nothing changes. Nothing here opens a database: <c>LoginRoleFileTests</c>, in the
/// Tenancy sample's tests, applies such a file to Supabase's own Postgres.
/// </summary>
public sealed class SupabaseLoginRoleExportTests : IDisposable
{
    /// <summary>The roles of a host whose login role holds nothing: Supabase's two, the scoped system role and the bookkeeping role.</summary>
    private const string Roles = "user=authenticated|anonymous=anon|system-in=ddd_system_in|system=ddd_system";

    /// <summary>What a build that only checks says last when something is missing.</summary>
    private const string OnlyChecks = "error : This build only checks. Build it locally, where SupabaseMigrationsExport is Write, and commit the files it writes.";

    private static readonly SupabaseMigrationSource Shelves = SupabaseMigrationSource.For(() => SupabaseShelfContext.Create());

    private static readonly RowAccessRule ShelvesByName = RowAccessRule.For<SupabaseShelf>(
        "Shelves by name", RowOperations.Read, "({col:Name} IS NOT DISTINCT FROM {caller:claim:shelf})", RowAccessRoles.User, RowAccessRoles.SystemIn);

    /// <summary>
    /// The whole file for <see cref="Roles"/> and <c>sample_api</c>, the first one written for the role: what the
    /// documentation shows, and what a project writes by hand without the property.
    /// </summary>
    private const string SampleApi =
        """
        -- Written by DDDToolkit for the role the application logs in as, sample_api.
        -- Written from SupabaseLoginRole and SupabaseRowAccessRoles; change those, not this file. Every file like it
        -- says what the role is now: it makes it, takes back what the one before it granted and no caller runs as
        -- any more, and grants the roles callers run as.
        --
        -- The role owns nothing and is given no privilege on a table, a schema or a function. All it holds is the
        -- right to switch to the roles its callers run as, each held to its policies and its privileges:
        --   anon           a caller without a token
        --   authenticated  a signed-in user
        --   ddd_system_in  the application's own work, inside the policies
        --   ddd_system     the toolkit's bookkeeping: the outbox, the inbox and which migrations ran
        -- It is NOINHERIT, so it has none of their privileges until it switches to one of them. A statement that
        -- reaches the application's connection can always go back to the role that logged in; this is what makes
        -- that role worth nothing.
        --
        -- No LOGIN and no password here: a migration is kept in a repository, and a password is not. Whoever deploys
        -- turns the login on once, as the database's owner, with a secret of that deployment:
        --     ALTER ROLE sample_api WITH LOGIN PASSWORD '...';

        DO $ddd$
        DECLARE
            attributes text;
            inherited text;
        BEGIN
            IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'sample_api') THEN
                BEGIN
                    CREATE ROLE sample_api NOLOGIN NOINHERIT;
                EXCEPTION WHEN duplicate_object OR unique_violation THEN
                    NULL; -- made by a migration that ran at the same time
                END;
            END IF;
            SELECT pg_catalog.concat_ws(' ', CASE WHEN rolsuper THEN 'NOSUPERUSER' END, CASE WHEN rolbypassrls THEN 'NOBYPASSRLS' END,
                       CASE WHEN rolcreaterole THEN 'NOCREATEROLE' END, CASE WHEN rolreplication THEN 'NOREPLICATION' END,
                       CASE WHEN rolinherit THEN 'NOINHERIT' END) INTO attributes
            FROM pg_catalog.pg_roles WHERE rolname = 'sample_api';
            IF attributes <> '' THEN
                RAISE EXCEPTION USING
                    MESSAGE = 'The role sample_api exists, and is a superuser, may bypass row level security, may create roles, may replicate or has the privileges of the roles it is granted without switching to them: the hint says which. Whoever reaches its connection would not be held to the policies.',
                    HINT = 'ALTER ROLE sample_api ' || attributes || ';';
            END IF;
            SELECT pg_catalog.string_agg(DISTINCT member_of, ', ' ORDER BY member_of) INTO inherited
            FROM (SELECT m.roleid::pg_catalog.regrole::pg_catalog.text AS member_of FROM pg_catalog.pg_auth_members m
                  WHERE m.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = 'sample_api')
                    AND coalesce((pg_catalog.to_jsonb(m) ->> 'inherit_option')::pg_catalog.bool, false)) inheriting;
            IF inherited IS NOT NULL THEN
                RAISE EXCEPTION USING
                    MESSAGE = 'The role sample_api has the privileges of ' || inherited || ' without switching to them: they were granted while it inherited, and NOINHERIT holds only for the grants after it. Whoever reaches its connection would not be held to their policies.',
                    HINT = 'GRANT ' || inherited || ' TO sample_api WITH INHERIT FALSE;';
            END IF;
            IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'ddd_system_in') THEN
                BEGIN
                    CREATE ROLE ddd_system_in NOLOGIN NOINHERIT;
                EXCEPTION WHEN duplicate_object OR unique_violation THEN
                    NULL; -- made by a migration that ran at the same time
                END;
            END IF;
            IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'ddd_system') THEN
                BEGIN
                    CREATE ROLE ddd_system NOLOGIN NOINHERIT;
                EXCEPTION WHEN duplicate_object OR unique_violation THEN
                    NULL; -- made by a migration that ran at the same time
                END;
            END IF;
        END
        $ddd$;

        DO $ddd$
        BEGIN
            IF NOT pg_catalog.pg_has_role('sample_api'::pg_catalog.name, 'anon'::pg_catalog.name,
                    CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
                BEGIN
                    GRANT anon TO sample_api;
                EXCEPTION WHEN unique_violation THEN
                    NULL; -- granted by a migration that ran at the same time
                END;
            END IF;
            IF NOT pg_catalog.pg_has_role('sample_api'::pg_catalog.name, 'authenticated'::pg_catalog.name,
                    CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
                BEGIN
                    GRANT authenticated TO sample_api;
                EXCEPTION WHEN unique_violation THEN
                    NULL; -- granted by a migration that ran at the same time
                END;
            END IF;
            IF NOT pg_catalog.pg_has_role('sample_api'::pg_catalog.name, 'ddd_system_in'::pg_catalog.name,
                    CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
                BEGIN
                    GRANT ddd_system_in TO sample_api;
                EXCEPTION WHEN unique_violation THEN
                    NULL; -- granted by a migration that ran at the same time
                END;
            END IF;
            IF NOT pg_catalog.pg_has_role('sample_api'::pg_catalog.name, 'ddd_system'::pg_catalog.name,
                    CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
                BEGIN
                    GRANT ddd_system TO sample_api;
                EXCEPTION WHEN unique_violation THEN
                    NULL; -- granted by a migration that ran at the same time
                END;
            END IF;
        END
        $ddd$;

        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ddd-login-role-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>The build's export, as the generated module initializer runs it, into <see cref="_directory"/>.</summary>
    private (int ExitCode, string Output) Build(string mode, string? loginRole, string? roles = Roles, params RowAccessRule[] rules)
    {
        using var output = new StringWriter();
        var exitCode = SupabaseMigrationBuild.Run(mode, [Shelves], rules, [], [], _directory, start: null, roles, callerFunctions: null, grants: null, force: null, loginRole, output);
        return (exitCode, output.ToString());
    }

    /// <summary>Every file in the directory, in the order the Supabase CLI applies them.</summary>
    private string[] Files()
        => Directory.Exists(_directory) ? [.. Directory.GetFiles(_directory).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)] : [];

    /// <summary>The login role files, oldest first.</summary>
    private string[] LoginRoleFiles() => [.. Files().Where(file => file.Contains("_login_role.", StringComparison.Ordinal))];

    private string Read(string file) => File.ReadAllText(Path.Combine(_directory, file));

    /// <summary>The roles a login role file grants <paramref name="login"/>, in the order it grants them: one statement each.</summary>
    private static string[] Granted(string sql, string login = "sample_api")
        => [.. sql.Split('\n').Select(line => line.Trim())
            .Where(line => line.StartsWith("GRANT ", StringComparison.Ordinal) && line.EndsWith($" TO {login};", StringComparison.Ordinal))
            .Select(line => line["GRANT ".Length..^$" TO {login};".Length])];

    [Fact]
    public void The_file_makes_the_role_without_a_login_and_grants_it_the_roles_callers_run_as_after_every_other_file()
    {
        var (exitCode, output) = Build("Write", "sample_api", rules: [ShelvesByName]);

        exitCode.Should().Be(0, output);
        var login = LoginRoleFiles().Should().ContainSingle().Subject;
        login.Should().MatchRegex(@"^\d{14}_login_role\.sample_api\.ddd\.sql$", "it is named after the role, as an access file is after its module");
        Files()[^1].Should().Be(login, "it comes after the migrations and after the access file that makes the roles it grants");
        Files().Should().Contain(file => file.Contains("_access.", StringComparison.Ordinal));
        output.Should().Contain($"Supabase migrations: Created      {login}");

        Read(login).Should().Be(SampleApi.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void The_file_sets_no_login_no_password_and_no_privilege_and_grants_nothing_but_the_roles_callers_run_as()
    {
        Build("Write", "sample_api", roles: Roles + "|token:analyst=desk_analyst|token:auditor=desk_analyst|token:member=authenticated").ExitCode.Should().Be(0);

        var statements = Read(LoginRoleFiles().Single()).Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)).ToList();

        statements.Should().NotContain(line => line.Contains("PASSWORD", StringComparison.Ordinal), "a password is the deployment's, never a repository's");
        statements.Where(line => line.Contains("LOGIN", StringComparison.Ordinal)).Should().OnlyContain(line => line.Contains("NOLOGIN NOINHERIT;", StringComparison.Ordinal), "the role is made without a login, and every role it makes is");
        statements.Select(line => line.Trim()).Where(line => line.StartsWith("GRANT", StringComparison.Ordinal)).Should().Equal(
            ["GRANT anon TO sample_api;", "GRANT authenticated TO sample_api;", "GRANT ddd_system_in TO sample_api;", "GRANT ddd_system TO sample_api;", "GRANT desk_analyst TO sample_api;"],
            "a grant of each role callers run as, a token role mapped to the user's role being that role, and a role two token roles share granted once");
        statements.Should().NotContain(line => line.Contains(" ON ", StringComparison.Ordinal), "no privilege on a table, a schema or a function");
        statements.Should().Contain("            CREATE ROLE desk_analyst NOLOGIN NOINHERIT;", "a token role's role is made where an access file has not made it yet");
        Read(LoginRoleFiles().Single()).Should().Contain("--   desk_analyst   a signed-in user whose token carries the role analyst or auditor\n");
    }

    [Fact]
    public void A_role_of_the_projects_own_for_its_users_is_required_rather_than_made()
    {
        Build("Write", "desk_api", roles: "user=desk_user|anonymous=desk_guest").ExitCode.Should().Be(0);
        var sql = Read(LoginRoleFiles().Single());

        Granted(sql, "desk_api").Should().Equal("desk_guest", "desk_user", "ddd_system_in", "ddd_system");
        sql.Should().Contain("    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'desk_user') THEN\n        RAISE EXCEPTION USING MESSAGE = 'The role desk_user, which a signed-in user runs as, does not exist. It is the project''s own to make, in a migration before this one.';\n");
        sql.Should().NotContain("CREATE ROLE desk_user").And.NotContain("CREATE ROLE desk_guest", "the toolkit makes the roles the access files make, and no others");
        SampleApi.Should().NotContain("RAISE EXCEPTION USING MESSAGE = 'The role anon", "Supabase's own roles are always there");
    }

    [Fact]
    public void A_second_build_finds_the_file_unchanged_and_a_build_that_checks_passes()
    {
        Build("Write", "sample_api", rules: [ShelvesByName]).ExitCode.Should().Be(0);
        var files = Files();

        var (exitCode, output) = Build("Write", "sample_api", rules: [ShelvesByName]);

        exitCode.Should().Be(0, output);
        output.Should().Contain($"Supabase migrations: Unchanged    {LoginRoleFiles().Single()}");
        Files().Should().Equal(files, "a file already written is never written again");
        Build("Check", "sample_api", rules: [ShelvesByName]).ExitCode.Should().Be(0);
    }

    [Fact]
    public void A_build_that_checks_fails_while_the_file_is_missing_and_writes_nothing()
    {
        Build("Write", loginRole: null, rules: [ShelvesByName]).ExitCode.Should().Be(0);
        var files = Files();

        var (exitCode, output) = Build("Check", "sample_api", rules: [ShelvesByName]);

        exitCode.Should().Be(1);
        output.Should().Contain("Supabase migrations: Missing      _login_role.sample_api.ddd.sql");
        output.Should().Contain(
            "error : sample_api login role: has no file that says what it is now: SupabaseLoginRole was set, or the roles callers run as changed. " +
            "A build with SupabaseMigrationsExport=Write, or SupabaseMigrations.Export, writes a new one.");
        output.Should().Contain(OnlyChecks);
        Files().Should().Equal(files);
    }

    [Fact]
    public void A_token_role_mapped_later_gets_a_new_file_after_every_other_and_the_first_stays_as_it_was()
    {
        Build("Write", "sample_api").ExitCode.Should().Be(0);
        var first = LoginRoleFiles().Single();
        var written = Read(first);

        // The roles changed: a build that only checks says the file is missing, and writes nothing.
        var withAnalysts = Roles + "|token:analyst=desk_analyst";
        var (checkedOnly, said) = Build("Check", "sample_api", withAnalysts);
        checkedOnly.Should().Be(1);
        said.Should().Contain("Supabase migrations: Missing      _login_role.sample_api.ddd.sql");
        LoginRoleFiles().Should().Equal([first]);

        // A build that writes adds a file, numbered after everything else, and leaves the first alone: a database
        // that applied it never reads it again, and one that is only as far as it runs it as it is.
        var (exitCode, output) = Build("Write", "sample_api", withAnalysts);
        exitCode.Should().Be(0, output);
        var second = LoginRoleFiles().Should().HaveCount(2).And.Subject.Last();
        string.CompareOrdinal(second, first).Should().BePositive();
        Files()[^1].Should().Be(second);
        Read(first).Should().Be(written);
        Granted(Read(second)).Should().Equal("anon", "authenticated", "ddd_system_in", "ddd_system", "desk_analyst");
        Read(second).Should().Contain("            CREATE ROLE desk_analyst NOLOGIN NOINHERIT;\n")
            .And.NotContain("REVOKE", "nothing the first granted is gone");

        Build("Write", "sample_api", withAnalysts).Output.Should().Contain($"Unchanged    {second}");
        Build("Check", "sample_api", withAnalysts).ExitCode.Should().Be(0);
    }

    [Fact]
    public void A_role_no_longer_mapped_is_taken_back_by_the_next_file_and_by_that_one_alone()
    {
        Build("Write", "sample_api", Roles + "|token:analyst=desk_analyst").ExitCode.Should().Be(0);

        Build("Write", "sample_api").ExitCode.Should().Be(0);

        var second = Read(LoginRoleFiles().Should().HaveCount(2).And.Subject.Last());
        second.Should().Contain(
            """
            -- Granted by the file before this one, and no role a caller runs as any more: desk_analyst.
            DO $ddd$
            BEGIN
                IF pg_catalog.to_regrole('desk_analyst') IS NOT NULL THEN
                    REVOKE desk_analyst FROM sample_api;
                END IF;
            END
            $ddd$;

            DO $ddd$
            BEGIN

            """.ReplaceLineEndings("\n"), "it is taken back before the roles callers run as now are granted");
        Granted(second).Should().Equal("anon", "authenticated", "ddd_system_in", "ddd_system");
        second.Should().NotContain("CREATE ROLE desk_analyst", "a role nobody runs as is not made");

        // What the file says the role is now is what the build says: the next build finds it so, takes nothing back
        // again, and a build that checks passes.
        Build("Write", "sample_api").Output.Should().Contain("Unchanged");
        LoginRoleFiles().Should().HaveCount(2);
        Build("Check", "sample_api").ExitCode.Should().Be(0);
    }

    [Fact]
    public void Another_login_role_gets_a_file_of_its_own_and_the_old_role_is_left_to_the_deployment()
    {
        Build("Write", "sample_api").ExitCode.Should().Be(0);
        var first = LoginRoleFiles().Single();

        Build("Write", "shop_api").ExitCode.Should().Be(0);

        LoginRoleFiles().Should().HaveCount(2).And.Contain(first);
        var second = Read(LoginRoleFiles().Single(file => file.EndsWith("_login_role.shop_api.ddd.sql", StringComparison.Ordinal)));
        Granted(second, "shop_api").Should().Equal("anon", "authenticated", "ddd_system_in", "ddd_system");
        second.Should().NotContain("sample_api", "a role a deployment logs in as is the deployment's to drop, and nothing it held is taken back here");
    }

    [Theory]
    [InlineData("Sample_Api", null, "error : SupabaseLoginRole is 'Sample_Api'. 'Sample_Api' is not a plain lowercase identifier, which is how the migration writes it: a lowercase letter or '_', then lowercase letters, digits and '_'. Use a name such as sample_api.")]
    [InlineData("sample-api", null, "error : SupabaseLoginRole is 'sample-api'. 'sample-api' is not a plain lowercase identifier, which is how the migration writes it: a lowercase letter or '_', then lowercase letters, digits and '_'. Use a name such as sample_api.")]
    [InlineData("9api", null, "error : SupabaseLoginRole is '9api'. '9api' is not a plain lowercase identifier, which is how the migration writes it: a lowercase letter or '_', then lowercase letters, digits and '_'. Use a name such as sample_api.")]
    [InlineData("api$1", null, "error : SupabaseLoginRole is 'api$1'. 'api$1' is not a plain lowercase identifier, which is how the migration writes it: a lowercase letter or '_', then lowercase letters, digits and '_'. Use a name such as sample_api.")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null, "error : SupabaseLoginRole is 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'. 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' is longer than the 63 bytes of a name Postgres keeps, so the role it made would not be the one the migration asks about. Use a shorter name, such as sample_api.")]
    [InlineData("user", null, "error : SupabaseLoginRole is 'user'. 'user' is a word SQL keeps for itself, which names no role where the migration writes it. Use another name, such as sample_api.")]
    [InlineData("postgres", null, "error : SupabaseLoginRole is 'postgres'. 'postgres' is one of Postgres's or Supabase's own roles. The application logs in as a role of its own, which owns nothing and only switches to the roles its callers run as; use a name such as sample_api.")]
    [InlineData("authenticated", null, "error : SupabaseLoginRole is 'authenticated'. 'authenticated' is one of Postgres's or Supabase's own roles. The application logs in as a role of its own, which owns nothing and only switches to the roles its callers run as; use a name such as sample_api.")]
    [InlineData("anon", null, "error : SupabaseLoginRole is 'anon'. 'anon' is one of Postgres's or Supabase's own roles. The application logs in as a role of its own, which owns nothing and only switches to the roles its callers run as; use a name such as sample_api.")]
    [InlineData("service_role", null, "error : SupabaseLoginRole is 'service_role'. 'service_role' is one of Postgres's or Supabase's own roles. The application logs in as a role of its own, which owns nothing and only switches to the roles its callers run as; use a name such as sample_api.")]
    [InlineData("authenticator", null, "error : SupabaseLoginRole is 'authenticator'. 'authenticator' is one of Postgres's or Supabase's own roles. The application logs in as a role of its own, which owns nothing and only switches to the roles its callers run as; use a name such as sample_api.")]
    [InlineData("supabase_admin", null, "error : SupabaseLoginRole is 'supabase_admin'. 'supabase_admin' is one of Postgres's or Supabase's own roles. The application logs in as a role of its own, which owns nothing and only switches to the roles its callers run as; use a name such as sample_api.")]
    [InlineData("pg_monitor", null, "error : SupabaseLoginRole is 'pg_monitor'. 'pg_monitor' is one of Postgres's or Supabase's own roles. The application logs in as a role of its own, which owns nothing and only switches to the roles its callers run as; use a name such as sample_api.")]
    [InlineData("ddd_system_in", null, "error : SupabaseLoginRole is 'ddd_system_in', the role of system-in in SupabaseRowAccessRoles: a role callers run as. The role the application logs in as only switches to those, and is a role of its own; name another, such as sample_api.")]
    [InlineData("ddd_system", Roles, "error : SupabaseLoginRole is 'ddd_system', the role of system in SupabaseRowAccessRoles: a role callers run as. The role the application logs in as only switches to those, and is a role of its own; name another, such as sample_api.")]
    [InlineData("desk_analyst", "token:analyst=desk_analyst", "error : SupabaseLoginRole is 'desk_analyst', the role of token:analyst in SupabaseRowAccessRoles: a role callers run as. The role the application logs in as only switches to those, and is a role of its own; name another, such as sample_api.")]
    [InlineData("desk_user", "user=desk_user", "error : SupabaseLoginRole is 'desk_user', the role of user in SupabaseRowAccessRoles: a role callers run as. The role the application logs in as only switches to those, and is a role of its own; name another, such as sample_api.")]
    public void A_login_role_the_migration_cannot_make_is_an_error_line_and_exit_code_2(string loginRole, string? roles, string error)
    {
        var (exitCode, output) = Build("Write", loginRole, roles, ShelvesByName);

        exitCode.Should().Be(2, "the export could not run as the project asked");
        output.TrimEnd().Should().Be(error, "the whole line is what the build shows");
        Directory.Exists(_directory).Should().BeFalse("nothing is written for a login role the project did not mean");
    }

    [Fact]
    public void Without_the_property_every_file_is_what_it_was_byte_for_byte()
    {
        // Through the overload every build used before the property, and through the one that takes it, unset and
        // white space, the privileges and the forced policies left to their defaults in both: the same files, saying
        // the same, and no login role file.
        using (var before = new StringWriter())
        {
            SupabaseMigrationBuild.Run("Write", [Shelves], [ShelvesByName], [], [], _directory, start: null, Roles, callerFunctions: null, grants: null, force: null, before)
                .Should().Be(0, before.ToString());
        }

        var files = Files();
        var contents = files.Select(Read).ToList();
        Directory.Delete(_directory, recursive: true);

        foreach (var unset in (string?[])[null, "", "  "])
        {
            using var output = new StringWriter();
            SupabaseMigrationBuild.Run("Write", [Shelves], [ShelvesByName], [], [], _directory, start: null, Roles, callerFunctions: null, grants: null, force: null, loginRole: unset, output)
                .Should().Be(0, output.ToString());

            Files().Select(VersionLess).Should().Equal(files.Select(VersionLess), "the same files, the access file's version being the moment it was written");
            Files().Select(Read).Should().Equal(contents);
            LoginRoleFiles().Should().BeEmpty();
            Directory.Delete(_directory, recursive: true);
        }

        static string VersionLess(string file) => file.Contains("_access.", StringComparison.Ordinal) ? file[file.IndexOf('_', StringComparison.Ordinal)..] : file;
    }

    [Fact]
    public void Set_by_hand_the_name_is_checked_when_it_is_set_and_the_roles_when_the_file_is_written()
    {
        var options = new SupabaseMigrationOptions();

        var notPlain = () => options.LoginRole = "Shop App";
        notPlain.Should().Throw<ArgumentException>().WithMessage("'Shop App' is not a plain lowercase identifier*");
        var platform = () => options.LoginRole = "supabase_auth_admin";
        platform.Should().Throw<ArgumentException>().WithMessage("'supabase_auth_admin' is one of Postgres's or Supabase's own roles*");

        // A role callers run as may become one after the name was set, so that is asked when the file is written,
        // before anything is.
        options.LoginRole = "shop_api";
        options.Roles = RowAccessRoleNames.Default with { System = "shop_api" };
        var export = () => SupabaseMigrations.Export([Shelves], _directory, options);
        export.Should().Throw<InvalidOperationException>().WithMessage("The login role 'shop_api' is the role of system among the roles callers run as.*");
        Directory.Exists(_directory).Should().BeFalse();

        options.Roles = RowAccessRoleNames.Default;
        var reports = SupabaseMigrations.Export([Shelves], _directory, options);
        reports.Should().HaveCount(2, "one report per source, and one for the login role's file, which belongs to no module");
        reports[^1].Entries.Should().ContainSingle().Which.Status.Should().Be(SupabaseMigrationStatus.Created);
        SupabaseMigrations.EnsureInSync([Shelves], _directory, options);
    }

    [Fact]
    public void The_export_of_a_single_context_leaves_the_login_role_file_out()
    {
        using var context = SupabaseShelfContext.Create();

        var report = SupabaseMigrations.Export(context, _directory, new SupabaseMigrationOptions { LoginRole = "sample_api" });

        report.Entries.Should().NotContain(entry => entry.Path.Contains("_login_role.", StringComparison.Ordinal), "the file is about every module at once, which one context does not know");
        LoginRoleFiles().Should().BeEmpty();
    }
}
