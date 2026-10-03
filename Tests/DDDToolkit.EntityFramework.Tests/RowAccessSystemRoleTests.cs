using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// A login role that owns nothing, and the role the toolkit's own bookkeeping runs as under it: a script makes
/// that role where it names it, never as one that could bypass the policies, and gives it the outbox, the inbox
/// and the migration history and nothing of a module's own. The outbox poller does its work as that role, and
/// the start-up check tells a login role that holds nothing from one that owns or holds something.
/// </summary>
public sealed class RowAccessSystemRoleTests(ExplicitCallersPostgres postgres)
{
    /// <summary>The apiary's roles: the defaults, and the role its rangers' tokens are mapped to.</summary>
    private static readonly RowAccessRoleNames Mapped = RowAccessRoleNames.Default with
    {
        TokenRoles = new Dictionary<string, string> { [ApiaryRules.Ranger] = ApiaryRules.RangerRole },
    };

    /// <summary>What every message of the login check ends with.</summary>
    private const string LoginCheckCloses =
        "A statement that reaches the connection can always go back to the role that logged in, and from there to every role it may switch to, so what those roles own or hold is outside every policy.";

    private static int roles;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A role name no other test uses: roles are the server's, and every test class shares one.</summary>
    private static string NewRole(string what) => $"apiary_{what}_{Interlocked.Increment(ref roles)}_{Guid.NewGuid():N}"[..40];

    [Fact]
    public void The_roles_of_a_script_take_the_system_role_from_the_options_and_name_it_by_its_symbol()
    {
        var names = RowAccessRoleNames.Of(new PostgresRowLevelSecurityOptions { SystemRole = "ddd_system" });

        names.System.Should().Be("ddd_system");
        names.Resolve(RowAccessRoles.System).Should().Be("ddd_system");
        names.Should().NotBe(RowAccessRoleNames.Default, "two sets of roles that differ in the bookkeeping role are not the same");
        names.Should().Be(RowAccessRoleNames.Default with { System = "ddd_system" });
        names.ToString().Should().EndWith(", System = ddd_system }");
        RowAccessRoleNames.Default.System.Should().BeNull("a host says so when its bookkeeping has a role of its own");

        var unconfigured = () => RowAccessRoleNames.Default.Resolve(RowAccessRoles.System);
        unconfigured.Should().Throw<ArgumentException>().WithMessage(
            "'@system' is the role the application's own bookkeeping runs as, and none is configured. Set PostgresRowLevelSecurityOptions.SystemRole*add 'system=<role>' to SupabaseRowAccessRoles.*");

        foreach (var unwritable in (string[])["PUBLIC", "none", "@system", "pg_read_all_data", "books$role", ""])
        {
            var set = () => RowAccessRoleNames.Default with { System = unwritable };
            set.Should().Throw<ArgumentException>().Which.ParamName.Should().Be(nameof(RowAccessRoleNames.System));
        }
    }

    [Theory]
    [InlineData("authenticated", "the role of a signed-in user")]
    [InlineData("anon", "the role of an anonymous caller")]
    [InlineData("ddd_system_in", "the scoped system role")]
    [InlineData(ApiaryRules.RangerRole, "the role of the token role 'ranger'")]
    public void A_system_role_a_caller_runs_as_is_refused_where_a_script_names_it(string system, string shared)
    {
        var names = Mapped with { System = system };
        var says = $"'{system}' is the role the application's own bookkeeping runs as, and {shared} as well. It holds the outbox, the inbox and the migration history, which no caller may reach, so it is a role of its own.";

        // A script that never names the role is the script it always was, whatever the role is.
        Script(new RowAccessExport { Roles = names }).Should().Be(Script(new RowAccessExport { Roles = names with { System = null } }));

        var withPrivileges = () => Script(new RowAccessExport { Roles = names, WriteGrants = true });
        withPrivileges.Should().Throw<InvalidOperationException>().WithMessage(says);

        var resolved = () => names.Resolve(RowAccessRoles.System);
        resolved.Should().Throw<ArgumentException>().WithMessage(says + "*");

        var ruled = () => Script(new RowAccessExport { Roles = names }, [.. ApiaryRules.All, RowAccessRule.For<Hive>("Bookkeeping counts the hives", RowOperations.Read, "TRUE", RowAccessRoles.System)]);
        ruled.Should().Throw<InvalidOperationException>().WithMessage("The rule 'Bookkeeping counts the hives' is for '@system', which no policy can be for. " + says);
    }

    [Fact]
    public void A_rule_for_the_system_role_is_refused_while_none_is_configured_and_written_for_it_once_one_is()
    {
        RowAccessRule[] rules = [.. ApiaryRules.All, RowAccessRule.For<Hive>("Bookkeeping counts the hives", RowOperations.Read, "TRUE", RowAccessRoles.System)];

        var unconfigured = () => Script(new RowAccessExport { Roles = Mapped }, rules);
        unconfigured.Should().Throw<InvalidOperationException>().WithMessage(
            "The rule 'Bookkeeping counts the hives' is for '@system', which no policy can be for. '@system' is the role the application's own bookkeeping runs as, and none is configured.*");

        var script = Script(new RowAccessExport { Roles = Mapped with { System = "ddd_system" } }, rules);

        script.Should().Contain("CREATE POLICY \"Bookkeeping counts the hives (select) for ddd_system\" ON apiary.\"Hives\" FOR SELECT TO ddd_system\n");
        script.Should().Contain("        CREATE ROLE ddd_system NOLOGIN NOINHERIT;\n", "the script makes the role a policy names, as it makes the scoped system role");
        script.Should().Contain("        GRANT USAGE ON SCHEMA ddd TO ddd_system;\n", "and lets it ask what the policies ask");
    }

    [Fact]
    public void A_role_spelled_out_is_the_hosts_own_even_where_it_is_the_system_callers()
    {
        // A host whose system caller runs as a role that bypasses the policies, and that names that role as the
        // database spells it: in a rule, and as the one role that may execute a function of a contribution.
        const string bypassing = "service_role";
        RowAccessRule[] rules = [.. ApiaryRules.All, RowAccessRule.For<Hive>("Background work reads every hive", RowOperations.Read, "TRUE", bypassing)];
        RowAccessExport With(RowAccessRoleNames names, string grantTo) => new()
        {
            Roles = names,
            Contributions = [new SpotContribution("apiary", _ => new([new ContributedFunction("in_season", "", "boolean", "SELECT true", GrantTo: [grantTo])], [], []))],
        };

        var script = Script(With(Mapped with { System = bypassing }, bypassing), rules);

        script.Should().Be(Script(With(Mapped, bypassing), rules), "a name spelled out is a role of the host's own, whatever else the host uses it for");
        script.Should().NotContain($"CREATE ROLE {bypassing}", "the script makes the roles it names by a symbol, and this one it never made");
        script.Should().NotContain($"The role {bypassing} exists, but it can bypass", "nor does it check it: it was never the bookkeeping role of this script");

        // Named by its symbol, in a grant alone, the bookkeeping role is the script's to make and to check.
        var bySymbol = Script(With(Mapped with { System = "ddd_system" }, RowAccessRoles.System));
        bySymbol.Should().Contain("        CREATE ROLE ddd_system NOLOGIN NOINHERIT;\n");
        bySymbol.Should().Contain("The role ddd_system exists, but it can bypass row level security or log in.");
        bySymbol.Should().Contain(" TO ddd_system;\n", "the function is granted to it");
    }

    [Fact]
    public async Task The_system_role_is_made_without_bypass_and_refused_when_it_can_bypass()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);
        using var model = database.ModelContext();
        RowAccessExport With(string system) => new() { Roles = database.Export.Roles with { System = system }, WriteGrants = true };

        // A role nobody made yet: the script makes it, as a role that can neither log in nor pass its privileges on.
        var made = NewRole("books");
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, ApiaryRules.All, [], With(made)), Cancellation);
        (await database.ListAsOwnerAsync(
            $"SELECT (NOT rolcanlogin AND NOT rolbypassrls AND NOT rolsuper AND NOT rolinherit)::text FROM pg_catalog.pg_roles WHERE rolname = '{made}'", Cancellation))
            .Should().Equal(["true"], "the bookkeeping role is held to its privileges like the scoped system role");
        (await database.ListAsOwnerAsync($"SELECT pg_catalog.has_table_privilege('{made}', 'ddd.\"OutboxMessages\"', 'SELECT')::text", Cancellation)).Should().Equal("true");

        // A role of that name that could go past the policies, or log in, is never the bookkeeping role.
        foreach (var power in (string[])["BYPASSRLS", "SUPERUSER", "LOGIN"])
        {
            var powerful = NewRole("mighty");
            await database.RunAsOwnerAsync($"CREATE ROLE {powerful} {(power == "LOGIN" ? "LOGIN" : "NOLOGIN " + power)};", Cancellation);

            var run = () => database.RunAsOwnerAsync(PostgresRowAccess.Script(model, ApiaryRules.All, [], With(powerful)), Cancellation);

            (await run.Should().ThrowAsync<PostgresException>(power)).Which.MessageText.Should().Be(
                $"The role {powerful} exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.");
            await database.RunAsOwnerAsync($"DROP ROLE {powerful};", Cancellation);
        }

        // Nor one a caller's role has the privileges of: every signed-in user would hold the outbox.
        var shared = NewRole("shared");
        await database.RunAsOwnerAsync($"CREATE ROLE {shared} NOLOGIN; GRANT {shared} TO authenticated WITH INHERIT TRUE;", Cancellation);
        var spread = () => database.RunAsOwnerAsync(PostgresRowAccess.Script(model, ApiaryRules.All, [], With(shared)), Cancellation);
        (await spread.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Be(
            $"The role {shared} is granted to authenticated, anon, ddd_system_in or apiary_ranger, so their callers would hold the outbox, the inbox and the migration history. Grant it only to the role the application logs in as.");
        await database.RunAsOwnerAsync($"DROP ROLE {shared};", Cancellation);

        // The refused scripts changed nothing: each ran in one transaction.
        (await database.PrivilegesAsync(Cancellation)).Should().NotContain(privilege => privilege.Contains("mighty", StringComparison.Ordinal) || privilege.Contains("shared", StringComparison.Ordinal));
        await database.RunAsOwnerAsync($"DROP OWNED BY {made}; DROP ROLE {made};", Cancellation);
    }

    [Fact]
    public async Task The_system_role_holds_the_bookkeeping_tables_and_nothing_else()
    {
        // A database that was migrated has a history of its migrations, which the bookkeeping role reads.
        var database = await ApiaryDatabase.CreateAsync(postgres, logged: true);
        using var model = database.ModelContext();
        await database.RunAsOwnerAsync(
            """
            CREATE TABLE public."__EFMigrationsHistory" ("MigrationId" character varying(150) PRIMARY KEY, "ProductVersion" character varying(32) NOT NULL);
            INSERT INTO public."__EFMigrationsHistory" VALUES ('20260901120000_CreateApiary', '10.0.0');
            """,
            Cancellation);
        await database.RunAsOwnerAsync(PostgresRowAccess.Script(model, ApiaryRules.All, [], database.Export), Cancellation);

        (await database.PrivilegesAsync(Cancellation)).Where(privilege => privilege.Contains(" ddd_system ", StringComparison.Ordinal)).Should().Equal(
            [
                "ddd.EventLog ddd_system DELETE",
                "ddd.EventLog ddd_system SELECT(Id)",
                "ddd.EventLog ddd_system SELECT(RecordedAt)",
                "ddd.InboxMessages ddd_system DELETE",
                "ddd.InboxMessages ddd_system INSERT",
                "ddd.InboxMessages ddd_system SELECT",
                "ddd.InboxMessages ddd_system UPDATE",
                "ddd.OutboxMessages ddd_system DELETE",
                "ddd.OutboxMessages ddd_system SELECT",
                "ddd.OutboxMessages ddd_system UPDATE(Attempts)",
                "ddd.OutboxMessages ddd_system UPDATE(LastError)",
                "ddd.OutboxMessages ddd_system UPDATE(NextAttemptAt)",
                "ddd.OutboxMessages ddd_system UPDATE(ProcessedAt)",
            ],
            "what reading, marking and deleting the toolkit's own rows asks, and no privilege on a table of the apiary's own");
        (await database.ListAsOwnerAsync("SELECT pg_catalog.has_table_privilege('ddd_system', 'public.\"__EFMigrationsHistory\"', 'SELECT')::text", Cancellation)).Should().Equal("true");

        await using var host = database.BuildHost();
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            context.Hives.Add(new Hive(HiveId.CreateSequential(), number: 1, "By the hedge", ApiaryDatabase.AliceId, isOpen: true));
            await context.SaveChangesAsync(Cancellation);
        });

        await ApiaryDatabase.AsAsync(host, Caller.System, async context =>
        {
            (await context.Database.SqlQueryRaw<string>("""SELECT current_user::text || ' of ' || session_user::text AS "Value" """).SingleAsync(Cancellation))
                .Should().Be("ddd_system of apiary_app", "the system caller runs as the bookkeeping role, on the login role's connection");

            (await context.Outbox.CountAsync(Cancellation)).Should().Be(1);
            (await context.Database.GetAppliedMigrationsAsync(Cancellation)).Should().Equal("20260901120000_CreateApiary");

            // A module's own tables are not the bookkeeping's to read: system work on them fails closed.
            var read = () => context.Hives.CountAsync(Cancellation);
            (await read.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

            // The log is not the bookkeeping's to read either, only to find the rows that may go by their age.
            var payloads = () => context.Database.SqlQueryRaw<string>("""SELECT "Payload" AS "Value" FROM ddd."EventLog" """).ToListAsync(Cancellation);
            (await payloads.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            (await context.Database.SqlQueryRaw<int>("""SELECT count("Id")::int AS "Value" FROM ddd."EventLog" """).SingleAsync(Cancellation)).Should().Be(1);
        });
    }

    [Fact]
    public async Task An_outbox_poller_marks_its_rows_as_the_system_role_under_a_login_that_owns_nothing()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);
        await using var host = database.BuildHost();
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            context.Hives.Add(new Hive(HiveId.CreateSequential(), number: 1, "By the hedge", ApiaryDatabase.AliceId, isOpen: true));
            await context.SaveChangesAsync(Cancellation);
        });

        // Who touched the row last, kept by the database in a column the model does not know.
        await database.RunAsOwnerAsync(
            """
            ALTER TABLE ddd."OutboxMessages" ADD COLUMN "MarkedBy" text;
            CREATE FUNCTION ddd.note_who_marked() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN NEW."MarkedBy" := current_user; RETURN NEW; END $$;
            CREATE TRIGGER note_who_marked BEFORE UPDATE ON ddd."OutboxMessages" FOR EACH ROW EXECUTE FUNCTION ddd.note_who_marked();
            """,
            Cancellation);

        // The poller begins nothing around itself: the processor begins the system caller for its own bookkeeping.
        int delivered;
        await using (var scope = host.CreateAsyncScope())
        {
            delivered = await scope.ServiceProvider.GetRequiredService<OutboxProcessor<ApiaryContext>>().ProcessPendingAsync(cancellationToken: Cancellation);
        }

        delivered.Should().Be(1);
        host.GetRequiredService<EventRecorder>().OfType<HiveSettled>().Should().ContainSingle("the event reached its handler");
        (await database.ListAsOwnerAsync("""SELECT "MarkedBy" || ' ' || ("ProcessedAt" IS NOT NULL)::text FROM ddd."OutboxMessages" """, Cancellation))
            .Should().Equal(["ddd_system true"], "the row was read and marked as the bookkeeping role, which the login role may switch to and nothing else may");
    }

    [Fact]
    public async Task The_login_check_passes_for_a_role_that_owns_nothing_and_names_what_another_owns()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);

        await using (var host = database.BuildHost())
        await using (var scope = host.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApiaryContext>();
            var check = () => PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, Cancellation);
            await check.Should().NotThrowAsync("the apiary logs in as a role that owns nothing, holds nothing and inherits nothing");
        }

        // The owner itself, as most applications log in: a superuser that made the schemas and owns every table.
        // It is the role the migrations run as, and that is the one thing said, naming it: not a line for every
        // table, function and schema the migrations made, each with a fix that hands it to the role it already is.
        await using (var host = database.BuildHost(connectionString: database.OwnerConnectionString))
        await using (var scope = host.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApiaryContext>();
            var check = () => PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, Cancellation);

            (await check.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(
                "The role 'postgres' that 'ApiaryContext' logs in as is meant to hold nothing, and it does:\n" +
                "- it owns the schemas apiary, ddd and what the migrations made there, as the role that runs them does. " +
                "Fix: log in as a role of its own, one that owns and holds nothing and may switch to the roles callers run as, and keep postgres for running the migrations.\n" +
                LoginCheckCloses);
        }

        // A login role of its own that is not quite empty-handed: it inherits a caller's role, owns one table,
        // holds a privilege on another, and reaches a third through PUBLIC.
        var login = NewRole("clerk");
        await database.RunAsOwnerAsync(
            $"""
            CREATE ROLE {login} LOGIN INHERIT PASSWORD '{login}';
            GRANT authenticated TO {login} WITH INHERIT TRUE;
            GRANT anon, ddd_system TO {login} WITH INHERIT FALSE;
            ALTER TABLE apiary."HiveBox" OWNER TO {login};
            GRANT SELECT, DELETE ON apiary."Hives" TO {login};
            GRANT UPDATE ("Label") ON apiary."Hives" TO {login};
            GRANT SELECT ON ddd."InboxMessages" TO PUBLIC;
            """,
            Cancellation);

        var connectionString = new NpgsqlConnectionStringBuilder(database.OwnerConnectionString) { Username = login, Password = login }.ConnectionString;
        await using (var host = database.BuildHost(connectionString: connectionString))
        await using (var scope = host.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApiaryContext>();
            var check = () => PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, Cancellation);

            (await check.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(
                $"The role '{login}' that 'ApiaryContext' logs in as is meant to hold nothing, and it does:\n" +
                $"- it has the privileges of authenticated without switching to it. Fix: ALTER ROLE {login} NOINHERIT; GRANT authenticated TO {login} WITH INHERIT FALSE;\n" +
                $"- it owns the table apiary.\"HiveBox\". Fix: ALTER TABLE apiary.\"HiveBox\" OWNER TO <the role that runs the migrations>;\n" +
                $"- it has SELECT on the table ddd.\"InboxMessages\" through PUBLIC. Fix: REVOKE ALL ON TABLE ddd.\"InboxMessages\" FROM PUBLIC;\n" +
                $"- it holds DELETE, SELECT, UPDATE of a column on the table apiary.\"Hives\". Fix: REVOKE ALL ON TABLE apiary.\"Hives\" FROM {login};\n" +
                LoginCheckCloses);
        }

        await database.RunAsOwnerAsync($"ALTER TABLE apiary.\"HiveBox\" OWNER TO postgres; DROP OWNED BY {login}; DROP ROLE {login};", Cancellation);
    }

    [Fact]
    public async Task The_login_check_names_a_role_the_login_may_switch_to_that_is_past_the_policies()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);

        // A login role that holds nothing itself and inherits nothing, as the check asks. But it may switch to the
        // role that owns the apiary's tables, to one that bypasses row level security, and, through a role in
        // between, to a second owner: one SET ROLE away from each.
        var login = NewRole("clerk");
        var owner = NewRole("owner");
        var bypassing = NewRole("mighty");
        var between = NewRole("between");
        var beyond = NewRole("beyond");
        await database.RunAsOwnerAsync(
            $"""
            CREATE ROLE {login} LOGIN NOINHERIT PASSWORD '{login}';
            CREATE ROLE {owner} NOLOGIN;
            CREATE ROLE {bypassing} NOLOGIN BYPASSRLS;
            CREATE ROLE {between} NOLOGIN NOINHERIT;
            CREATE ROLE {beyond} NOLOGIN;
            GRANT anon, authenticated, ddd_system, {owner}, {bypassing}, {between} TO {login} WITH INHERIT FALSE;
            GRANT {beyond} TO {between} WITH INHERIT FALSE;
            ALTER TABLE apiary."Hives" OWNER TO {owner};
            ALTER TABLE apiary."HiveBox" OWNER TO {owner};
            ALTER FUNCTION ddd.caller_id() OWNER TO {beyond};
            """,
            Cancellation);

        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(database.OwnerConnectionString) { Username = login, Password = login }.ConnectionString;
            await using var host = database.BuildHost(connectionString: connectionString);
            await using var scope = host.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApiaryContext>();

            // What the check is about: as the owner, no policy holds, though the login role itself holds nothing.
            (await database.ListAsOwnerAsync($"""SELECT pg_catalog.has_table_privilege('{login}', 'apiary."Hives"', 'SELECT')::text""", Cancellation))
                .Should().Equal(["false"], "the login role holds nothing on the hives");

            var check = () => PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, Cancellation);

            (await check.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(
                $"The role '{login}' that 'ApiaryContext' logs in as is meant to hold nothing, and it does:\n" +
                $"- it may switch to {beyond}, which owns tables, functions or schemas the application uses. Fix: REVOKE <the role that is a member of {beyond}> FROM {login};\n" +
                $"- it may switch to {bypassing}, which may bypass row level security. Fix: REVOKE {bypassing} FROM {login};\n" +
                $"- it may switch to {owner}, which owns tables, functions or schemas the application uses. Fix: REVOKE {owner} FROM {login};\n" +
                LoginCheckCloses,
                "the roles a caller runs as and the bookkeeping role are held to the policies, and are no finding");

            // Once the login role reaches none of them, it is the role that holds nothing again.
            await database.RunAsOwnerAsync($"REVOKE {owner}, {bypassing}, {between} FROM {login};", Cancellation);
            await check.Should().NotThrowAsync();
        }
        finally
        {
            await database.RunAsOwnerAsync(
                $"""
                ALTER TABLE apiary."Hives" OWNER TO postgres;
                ALTER TABLE apiary."HiveBox" OWNER TO postgres;
                ALTER FUNCTION ddd.caller_id() OWNER TO postgres;
                DROP OWNED BY {login}, {owner}, {bypassing}, {between}, {beyond};
                DROP ROLE {login}, {owner}, {bypassing}, {between}, {beyond};
                """,
                Cancellation);
        }
    }

    [Fact]
    public async Task The_login_check_leaves_out_a_system_role_the_host_chose_to_bypass()
    {
        // A host whose system caller runs as a role that bypasses row level security, as Supabase's service role
        // does: its scripts write no bookkeeping role, and the role is the host's own to make.
        var bypassing = NewRole("service");
        var database = await ApiaryDatabase.CreateAsync(postgres, configure: export => new RowAccessExport { Roles = export.Roles with { System = null }, WriteGrants = true });
        var login = NewRole("clerk");
        await database.RunAsOwnerAsync(
            $"""
            CREATE ROLE {bypassing} NOLOGIN NOINHERIT BYPASSRLS;
            CREATE ROLE {login} LOGIN NOINHERIT PASSWORD '{login}';
            GRANT anon, authenticated, {bypassing} TO {login} WITH INHERIT FALSE;
            """,
            Cancellation);

        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(database.OwnerConnectionString) { Username = login, Password = login }.ConnectionString;

            // The host says so: the system caller runs as that role, and the check, which runs as the system caller, passes.
            await using (var host = database.BuildHost(services: services => services.AddPostgresRowLevelSecurity(roles => roles.SystemRole = bypassing), connectionString: connectionString))
            await using (var scope = host.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApiaryContext>();
                await PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, Cancellation);
            }

            // A host that never named it has a login role that may get past the policies, and is told.
            await using (var host = database.BuildHost(connectionString: connectionString))
            await using (var scope = host.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApiaryContext>();
                var check = () => PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, Cancellation);

                (await check.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(
                    $"The role '{login}' that 'ApiaryContext' logs in as is meant to hold nothing, and it does:\n" +
                    $"- it may switch to {bypassing}, which may bypass row level security. Fix: REVOKE {bypassing} FROM {login};\n" +
                    LoginCheckCloses);
            }
        }
        finally
        {
            await database.RunAsOwnerAsync($"DROP OWNED BY {login}, {bypassing}; DROP ROLE {login}, {bypassing};", Cancellation);
        }
    }

    [Fact]
    public async Task The_login_check_names_a_schema_the_login_may_create_in()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);
        var login = NewRole("clerk");
        await database.RunAsOwnerAsync(
            $"""
            CREATE ROLE {login} LOGIN NOINHERIT PASSWORD '{login}';
            GRANT anon, authenticated, ddd_system TO {login} WITH INHERIT FALSE;
            GRANT CREATE ON SCHEMA apiary TO {login};
            GRANT CREATE ON SCHEMA ddd TO PUBLIC;
            """,
            Cancellation);

        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(database.OwnerConnectionString) { Username = login, Password = login }.ConnectionString;
            await using var host = database.BuildHost(connectionString: connectionString);
            await using var scope = host.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApiaryContext>();
            var check = () => PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, Cancellation);

            // Whoever may create in a schema makes a table or a function there and owns it, outside every policy.
            (await check.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(
                $"The role '{login}' that 'ApiaryContext' logs in as is meant to hold nothing, and it does:\n" +
                "- it has CREATE on the schema ddd through PUBLIC. Fix: REVOKE CREATE ON SCHEMA ddd FROM PUBLIC;\n" +
                $"- it holds CREATE on the schema apiary. Fix: REVOKE CREATE ON SCHEMA apiary FROM {login};\n" +
                LoginCheckCloses);

            await database.RunAsOwnerAsync($"REVOKE CREATE ON SCHEMA apiary FROM {login}; REVOKE CREATE ON SCHEMA ddd FROM PUBLIC;", Cancellation);
            await check.Should().NotThrowAsync("usage of a schema is what a caller's role needs, and the login role has not even that");
        }
        finally
        {
            await database.RunAsOwnerAsync($"REVOKE CREATE ON SCHEMA ddd FROM PUBLIC; DROP OWNED BY {login}; DROP ROLE {login};", Cancellation);
        }
    }

    [Fact]
    public async Task The_login_check_names_a_login_role_that_owns_the_database()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);

        // A database made for the application's own role, as CREATE DATABASE … OWNER does: the role owns no table
        // and is granted nothing. Yet whoever owns the database is pg_database_owner, which owns the schema
        // public since Postgres 15, and a schema's owner drops any table in it.
        var login = NewRole("clerk");
        var name = new NpgsqlConnectionStringBuilder(database.OwnerConnectionString).Database!;
        await database.RunAsOwnerAsync(
            $"""
            CREATE ROLE {login} LOGIN NOINHERIT PASSWORD '{login}';
            GRANT anon, authenticated, ddd_system TO {login} WITH INHERIT FALSE;
            ALTER DATABASE "{name}" OWNER TO {login};
            """,
            Cancellation);

        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(database.OwnerConnectionString) { Username = login, Password = login }.ConnectionString;

            // The apiary keeps its tables in schemas of its own, which the database's owner does not own.
            await using (var host = database.BuildHost(connectionString: connectionString))
            await using (var scope = host.CreateAsyncScope())
            {
                await PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(scope.ServiceProvider.GetRequiredService<ApiaryContext>(), Cancellation);
            }

            // A context whose tables are in public is another matter. No grant makes the login role that role, so
            // there is none to revoke: the fix is another owner for the database.
            await using var notes = new PublicNotes(new DbContextOptionsBuilder<PublicNotes>().UseNpgsql(connectionString).Options);
            var check = () => PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(notes, Cancellation);
            (await check.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(
                $"The role '{login}' that 'PublicNotes' logs in as is meant to hold nothing, and it does:\n" +
                $"- it may switch to pg_database_owner, which owns tables, functions or schemas the application uses. Fix: ALTER DATABASE {name} OWNER TO <the role that runs the migrations>;\n" +
                LoginCheckCloses);

            await database.RunAsOwnerAsync($"""ALTER DATABASE "{name}" OWNER TO postgres;""", Cancellation);
            await check.Should().NotThrowAsync();
        }
        finally
        {
            await database.RunAsOwnerAsync($"""ALTER DATABASE "{name}" OWNER TO postgres; DROP OWNED BY {login}; DROP ROLE {login};""", Cancellation);
        }
    }

    /// <summary>A context whose one table is in <c>public</c>, the schema every database has from the start.</summary>
    private sealed class PublicNotes(DbContextOptions<PublicNotes> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Note>().ToTable("Notes");
    }

    /// <summary>A row of <see cref="PublicNotes"/>.</summary>
    private sealed class Note
    {
        public Guid Id { get; set; }
    }

    private static string Script(RowAccessExport export, IReadOnlyList<RowAccessRule>? rules = null)
    {
        using var model = ApiaryContext.ForScripts();
        return PostgresRowAccess.Script(model, rules ?? ApiaryRules.All, [], export);
    }
}
