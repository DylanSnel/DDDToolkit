using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Tenancy on a database set up the strict way, all of it at once: the tables and the functions belong to a role
/// that runs the migrations and may bypass row level security, the application logs in as a role that owns
/// nothing, the toolkit's bookkeeping has a role of its own, every policy is forced on the tables' owner, and
/// the privileges are the ones written from the policies. Every use case runs there, and every start-up check
/// passes.
/// </summary>
/// <remarks>
/// The other tests of this suite log in as the role that owns the tables, as a host that migrates its own
/// database does. Roles are the server's, and those tests share the server, so the three roles here are this
/// class's own and are made only if they are not there yet.
/// </remarks>
public sealed class LoginThatOwnsNothingTests(TenancyPostgres postgres)
{
    /// <summary>The role that owns the tables and the functions, as the role that runs an application's migrations does.</summary>
    private const string Owner = "tenancy_migrations";

    /// <summary>The role the application logs in as here: it owns nothing, and only switches to its callers' roles.</summary>
    private const string Login = "tenancy_login";

    /// <summary>The role the toolkit's own bookkeeping runs as, since the login role can do nothing itself.</summary>
    private const string Bookkeeping = "tenancy_bookkeeping";

    private const string LoginPassword = "tenancy_login";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_use_case_runs_and_every_check_passes_as_a_login_that_owns_nothing_under_forced_policies()
    {
        var made = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        var superuser = made.SuperuserConnectionString;

        // The access files, with everything an export may turn on: the privileges from the policies, the policies
        // forced on the owner, and a role for the bookkeeping. Then the deployment: the login role is given the
        // roles its callers run as and nothing else, and everything the application's old login owned goes to the
        // role that runs migrations.
        var roles = RowAccessRoleNames.Default with { System = Bookkeeping };
        foreach (var script in TenancyPostgres.AccessScripts(
                     roles: roles,
                     export: written => new RowAccessExport { Roles = written.Roles, Contributions = written.Contributions, WriteGrants = true, ForceRowLevelSecurity = true }))
        {
            await TenancyPostgres.ExecuteAsync(superuser, script, Cancellation);
        }

        await TenancyPostgres.ExecuteAsync(superuser, $"""
            DO $roles$
            DECLARE
                owned record;
            BEGIN
                IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '{Owner}') THEN
                    CREATE ROLE {Owner} NOLOGIN BYPASSRLS;
                END IF;
                IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '{Login}') THEN
                    CREATE ROLE {Login} LOGIN NOINHERIT PASSWORD '{LoginPassword}';
                END IF;

                FOR owned IN
                    SELECT pg_catalog.format('ALTER TABLE %I.%I OWNER TO {Owner}', n.nspname, c.relname) AS statement
                    FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname IN ('ddd', '{TestTenancyContext.Schema}', '{WidgetContext.Schema}') AND c.relkind = 'r'
                    UNION ALL
                    SELECT pg_catalog.format('ALTER ROUTINE %s OWNER TO {Owner}', p.oid::pg_catalog.regprocedure)
                    FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname IN ('ddd', '{TestTenancyContext.Schema}', '{WidgetContext.Schema}')
                    UNION ALL
                    SELECT pg_catalog.format('ALTER SCHEMA %I OWNER TO {Owner}', n.nspname)
                    FROM pg_catalog.pg_namespace n
                    WHERE n.nspname IN ('ddd', '{TestTenancyContext.Schema}', '{WidgetContext.Schema}')
                LOOP
                    EXECUTE owned.statement;
                END LOOP;
            END
            $roles$;
            GRANT anon, authenticated, ddd_system_in, {Bookkeeping} TO {Login};
            """, Cancellation);

        var database = made with
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(made.ConnectionString) { Username = Login, Password = LoginPassword }.ConnectionString,
        };

        // The login role reads no table as itself: what it may do is become one of its callers' roles.
        await using (var login = new NpgsqlConnection(database.ConnectionString))
        {
            await login.OpenAsync(Cancellation);
            await using var read = new NpgsqlCommand($"SELECT count(*) FROM {TestTenancyContext.Schema}.\"Seats\"", login);
            (await FluentActions.Awaiting(() => read.ExecuteScalarAsync(Cancellation)).Should().ThrowAsync<PostgresException>())
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        // Seeded through the use cases, then every command of the use cases, each as a seat that may run it. All
        // of it runs as a caller's role under the forced policies; nothing runs as the role that logged in.
        await using var services = new TenancyServices(database, roles: options => options.SystemRole = Bookkeeping);
        await TenancySeed.SeedAsync(services, await TenancySeed.DatabaseNowAsync(superuser, Cancellation), Cancellation);
        await EveryUseCase.RunAsync(services, Cancellation);
        (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Directory().WhoAmIAsync(Cancellation))).Seat.Id.Should().Be(Ada.Seat);

        // And the checks a host runs at start-up, the core's and Tenancy's, all pass on it.
        await services.InScopeAsync(async scoped =>
        {
            foreach (var context in new DbContext[] { scoped.Tenancy(), scoped.Widgets() })
            {
                PostgresRowAccessChecks.EnsureRowLevelSecurityWired(context);
                await PostgresRowAccessChecks.EnsureLoginRoleOwnsNothingAsync(context, Cancellation);
                await PostgresRowAccessChecks.EnsureDefinerOwnersBypassAsync(context, Cancellation);
            }
        });
        TenancyPostgresChecks.EnsureExplicitCallers(services.Provider);
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(services.Provider, Cancellation);
        await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);

        // And it was the strict set-up they passed on: no table of Tenancy's with policies leaves its owner out.
        await using var owner = new NpgsqlConnection(superuser);
        await owner.OpenAsync(Cancellation);
        await using var forced = new NpgsqlCommand(
            $"SELECT count(*) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = '{TestTenancyContext.Schema}' AND c.relkind = 'r' AND c.relrowsecurity AND NOT c.relforcerowsecurity",
            owner);
        ((long)(await forced.ExecuteScalarAsync(Cancellation))!).Should().Be(0, "every table of Tenancy's that has policies holds its owner to them");
    }
}
