using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The checks a host runs at start-up pass on a database and a host set up as Tenancy's policies rely on, and fail,
/// naming what is wrong and how to put it right, on one that is not: a host that no longer requires explicit
/// callers, reads across tenants whose functions would not answer Tenancy's own system work, or would answer
/// someone else, a scoped system role that could escape its tenant, policies, functions or the trigger that
/// writes the rights missing, written from another catalogue, or changed from how the contribution writes them, and
/// a function a module reads through that answers other columns than the read model has.
/// </summary>
/// <remarks>
/// Roles are the server's, and the other tests of the run share the server, so a test that needs a scoped system
/// role that is wrong makes one of its own and points only its own services at it.
/// </remarks>
public sealed class StartUpCheckTests(TenancyPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_host_that_no_longer_requires_explicit_callers_is_refused()
    {
        await using (var registered = new ServiceCollection().AddPostgresRowLevelSecurity().AddTenancyPostgres().BuildServiceProvider())
        {
            TenancyPostgresChecks.EnsureExplicitCallers(registered);
        }

        // Options registered again after AddTenancyPostgres() shadow the ones it turned on.
        await using (var shadowed = new ServiceCollection().AddPostgresRowLevelSecurity().AddTenancyPostgres().AddSingleton(new CallerOptions()).BuildServiceProvider())
        {
            FluentActions.Invoking(() => TenancyPostgresChecks.EnsureExplicitCallers(shadowed))
                .Should().Throw<InvalidOperationException>().WithMessage("*do not require explicit callers*");
        }

        // An interceptor made without the options, as one made by hand is, would log a caller that changes inside a
        // transaction rather than refuse it.
        await using (var handMade = new ServiceCollection()
                         .AddPostgresRowLevelSecurity()
                         .AddTenancyPostgres()
                         .AddSingleton(services => new PostgresRowLevelSecurityInterceptor(
                             services.GetRequiredService<ICallerAccessor>(), services.GetRequiredService<PostgresRowLevelSecurityOptions>()))
                         .BuildServiceProvider())
        {
            FluentActions.Invoking(() => TenancyPostgresChecks.EnsureExplicitCallers(handMade))
                .Should().Throw<InvalidOperationException>().WithMessage("*interceptor was built without explicit callers*");
        }

        await using var withoutRowLevelSecurity = new ServiceCollection().AddTenancyPostgres().BuildServiceProvider();
        FluentActions.Invoking(() => TenancyPostgresChecks.EnsureExplicitCallers(withoutRowLevelSecurity))
            .Should().Throw<InvalidOperationException>().WithMessage("Row level security is not registered*");
    }

    [Fact]
    public async Task A_seated_token_role_that_reaches_the_database_as_another_role_is_refused()
    {
        static ServiceProvider Host(Action<TenantSelectionOptions>? seated = null, Action<PostgresRowLevelSecurityOptions>? roles = null)
        {
            var services = new ServiceCollection();
            TestHostTenancy.Add(services, options => seated?.Invoke(options.TenantSelection));
            return services.AddPostgresRowLevelSecurity(roles).AddTenancyPostgres().BuildServiceProvider();
        }

        // As registered, only a signed-in user is seated, and that is the role the policies are for.
        await using (var registered = Host())
        {
            TenancyPostgresChecks.EnsureSeatedTokenRolesAreSignedInUsers(registered);
        }

        // A token role of the host's own, mapped to the role of a signed-in user: seated, and a signed-in user.
        await using (var mapped = Host(seated => seated.SeatedTokenRoles.Add("member"), roles => roles.TokenRoles["member"] = roles.UserRole))
        {
            TenancyPostgresChecks.EnsureSeatedTokenRolesAreSignedInUsers(mapped);
        }

        // A seated role that is on no list: refused before it connects, or run as an anonymous caller, and never as a seat.
        await using (var unlisted = Host(seated => seated.SeatedTokenRoles.Add("member")))
        {
            FluentActions.Invoking(() => TenancyPostgresChecks.EnsureSeatedTokenRolesAreSignedInUsers(unlisted))
                .Should().Throw<InvalidOperationException>().WithMessage("*lists the token role 'member', which the database is given no role for*TokenRoles[\"member\"] = \"authenticated\"*");
        }

        // A seated role mapped to a role of its own, which Tenancy's tables are closed to.
        await using (var elsewhere = Host(seated => seated.SeatedTokenRoles.Add("member"), roles => roles.TokenRoles["member"] = "desk_member"))
        {
            FluentActions.Invoking(() => TenancyPostgresChecks.EnsureSeatedTokenRolesAreSignedInUsers(elsewhere))
                .Should().Throw<InvalidOperationException>().WithMessage("*'member', which runs as the database role 'desk_member'*");
        }

        await using var withoutTenancy = new ServiceCollection().AddPostgresRowLevelSecurity().AddTenancyPostgres().BuildServiceProvider();
        FluentActions.Invoking(() => TenancyPostgresChecks.EnsureSeatedTokenRolesAreSignedInUsers(withoutTenancy))
            .Should().Throw<InvalidOperationException>().WithMessage("Tenancy is not registered*");
    }

    [Fact]
    public async Task The_policies_check_says_how_to_register_the_context_it_looks_for()
    {
        // Tenancy is registered and no context is: the check finds none that maps Tenancy's tables, before it asks
        // a database anything, and names both ways to register one, a pool included.
        var services = new ServiceCollection();
        TestHostTenancy.Add(services, options => options.Catalogue = HostCatalogue.Application);
        services.AddPostgresRowLevelSecurity();
        services.AddTenancyPostgres();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No context the services make maps Tenancy's tables*with AddDbContext, or with a context pool and services.AddScopedFromPool<TContext>().");
    }

    [Fact]
    public async Task The_start_up_check_proves_the_reads_across_tenants_through_the_functions()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using var services = new TenancyServices(database);
        await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation);

        // The application itself needs no reach past the policies for it: the check passes where it runs as a role
        // that reads no table of Tenancy's.
        await TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, $"""
            DO $do$ BEGIN CREATE ROLE tenancy_reader NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            GRANT tenancy_reader TO {TenancyPostgres.LoginRole};
            """, Cancellation);
        await using (var reader = new TenancyServices(database, roles: roles => roles.SystemRole = "tenancy_reader"))
        {
            await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(reader.Provider, Cancellation);
        }

        // Breaks the database one way, sees the check name it, and puts it right again.
        async Task FailsAsync(string broken, string message, string? mended = null, bool asSuperuser = false)
        {
            var connection = asSuperuser ? database.SuperuserConnectionString : database.ConnectionString;
            await TenancyPostgres.ExecuteAsync(connection, broken, Cancellation);
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>(broken).WithMessage("Tenancy's reads across tenants would not answer as they should: " + message);

            if (mended is not null)
            {
                await TenancyPostgres.ExecuteAsync(connection, mended, Cancellation);
            }
            else
            {
                // The access files applied again write each function as it was, with its grants.
                foreach (var script in TenancyPostgres.AccessScripts())
                {
                    await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
                }
            }

            await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation);
        }

        // A function that is not there, one that runs as whoever asks, and one whose search path a session decides.
        await FailsAsync("DROP FUNCTION tenancy.tenants_to_sweep()", "- tenants_to_sweep is missing:*apply them.*role_keys_in_use, tenants_to_sweep, seats_of_identity, invitation_of_digest of tenancy*");
        await FailsAsync("ALTER FUNCTION tenancy.role_keys_in_use() SECURITY INVOKER", "- tenancy.role_keys_in_use() does not run as its owner*");
        await FailsAsync("ALTER FUNCTION tenancy.seats_of_identity(uuid) RESET search_path", "- tenancy.seats_of_identity(uuid) runs as its owner without an empty search path*SET search_path = '';*");
        await FailsAsync("ALTER FUNCTION tenancy.tenants_to_sweep() SET search_path = public", "- tenancy.tenants_to_sweep() runs as its owner without an empty search path*");

        // An owner the policies hold: the tables' owner, held to them after all, or a role that never read past them.
        await FailsAsync(
            "ALTER TABLE tenancy.\"Roles\" FORCE ROW LEVEL SECURITY",
            $"- tenancy.role_keys_in_use() is owned by {TenancyPostgres.LoginRole}, which row level security holds back on tenancy.\"Roles\", so it would answer for no tenant:*",
            mended: "ALTER TABLE tenancy.\"Roles\" NO FORCE ROW LEVEL SECURITY");
        await FailsAsync(
            "ALTER FUNCTION tenancy.tenants_to_sweep() OWNER TO tenancy_reader",
            "- tenancy.tenants_to_sweep() is owned by tenancy_reader, which row level security holds back on tenancy.\"Tenants\"*OWNER TO*",
            mended: $"ALTER FUNCTION tenancy.tenants_to_sweep() OWNER TO {TenancyPostgres.LoginRole}",
            asSuperuser: true);

        // The scoped system role without the right to ask, and anyone else with it.
        await FailsAsync(
            "REVOKE EXECUTE ON FUNCTION tenancy.role_keys_in_use() FROM ddd_system_in",
            "- ddd_system_in may not execute tenancy.role_keys_in_use(), which Tenancy's reads across tenants ask as it: GRANT EXECUTE ON FUNCTION tenancy.role_keys_in_use() TO ddd_system_in;*");
        foreach (var function in new[] { "tenancy.role_keys_in_use()", "tenancy.tenants_to_sweep()", "tenancy.seats_of_identity(uuid)" })
        {
            await FailsAsync(
                $"GRANT EXECUTE ON FUNCTION {function} TO authenticated",
                $"- authenticated may execute {function}, which answers across tenants: REVOKE EXECUTE ON FUNCTION {function} FROM PUBLIC, authenticated;*",
                mended: $"REVOKE EXECUTE ON FUNCTION {function} FROM PUBLIC, authenticated");
            await FailsAsync(
                $"GRANT EXECUTE ON FUNCTION {function} TO anon",
                $"- anon may execute {function}, which answers across tenants:*",
                mended: $"REVOKE EXECUTE ON FUNCTION {function} FROM PUBLIC, anon");
            await FailsAsync(
                $"GRANT EXECUTE ON FUNCTION {function} TO PUBLIC",
                $"- every role may execute {function}, which answers across tenants: REVOKE EXECUTE ON FUNCTION {function} FROM PUBLIC;*",
                mended: $"REVOKE EXECUTE ON FUNCTION {function} FROM PUBLIC");
        }

        // A function that answers whoever asks it, written by hand over the one the contribution writes.
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            "CREATE OR REPLACE FUNCTION tenancy.role_keys_in_use() RETURNS SETOF text LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$ SELECT DISTINCT pg_catalog.unnest(r.\"Keys\") FROM tenancy.\"Roles\" r $$",
            Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage($"The function role_keys_in_use answered {TenancyPostgres.Catalogue.LiveKeys.Count} keys to system work in another scope than Tenancy's own*");
        foreach (var script in TenancyPostgres.AccessScripts())
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation);
    }

    [Fact]
    public async Task The_start_up_check_of_the_reads_across_tenants_fails_on_a_host_that_would_not_ask_the_functions_as_tenancy_does()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);

        // A context that does not say who is calling: the read would run as the role the application logs in as, which
        // the functions answer nothing, in silence.
        await using (var silent = new TenancyServices(database, rowLevelSecurity: false))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(silent.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage(
                    $"The context that maps Tenancy's tables asked the database as {TenancyPostgres.LoginRole}, and not as the scoped system role ddd_system_in*UsePostgresRowLevelSecurity(serviceProvider)*");
        }

        // No scoped system role at all, one nobody made, and one the application may not switch to.
        await using (var without = new TenancyServices(database, roles: roles => roles.SystemInRole = null))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(without.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("PostgresRowLevelSecurityOptions.SystemInRole is null*");
        }

        await using (var missing = new TenancyServices(database, roles: roles => roles.SystemInRole = "scoped_nobody_made"))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(missing.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*- the scoped system role scoped_nobody_made does not exist:*");
        }

        await TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, """
            DO $do$ BEGIN CREATE ROLE scoped_out_of_reach NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            GRANT EXECUTE ON FUNCTION tenancy.role_keys_in_use(), tenancy.tenants_to_sweep(), tenancy.seats_of_identity(uuid), tenancy.invitation_of_digest(bytea) TO scoped_out_of_reach;
            """, Cancellation);
        await using (var unreachable = new TenancyServices(database, roles: roles => roles.SystemInRole = "scoped_out_of_reach"))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(unreachable.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("System work in the scope 'tenancy' could not ask tenancy.role_keys_in_use() as the scoped system role: permission denied to set role \"scoped_out_of_reach\".*switch to the scoped system role*");
        }

        // A store that writes the rights itself reads across tenants as the application itself, not through the functions.
        await using (var writing = new TenancyServices(database, databaseKeepsRights: false))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(writing.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("TenancyStoreOptions.DatabaseKeepsRights is off, so Tenancy's reads across tenants would run as the application itself*AddTenancyPostgres()*");
        }

        await using var withoutRowLevelSecurity = new ServiceCollection().AddTenancyPostgres().BuildServiceProvider();
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(withoutRowLevelSecurity, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Row level security is not registered*");
    }

    [Fact]
    public async Task The_confinement_check_fails_for_a_system_in_role_that_can_bypass()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using (var services = new TenancyServices(database))
        {
            await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(services.Provider, Cancellation);
        }

        await TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, """
            DO $do$ BEGIN CREATE ROLE scoped_bypassing NOLOGIN BYPASSRLS; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            DO $do$ BEGIN CREATE ROLE scoped_logging_in LOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            DO $do$ BEGIN CREATE ROLE scoped_behind_the_api NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            DO $do$ BEGIN CREATE ROLE scoped_owning NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            DO $do$ BEGIN CREATE ROLE scoped_as_the_owner NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            CREATE TABLE public.owned_by_the_scoped_role (id integer);
            ALTER TABLE public.owned_by_the_scoped_role OWNER TO scoped_owning;
            DO $do$ BEGIN CREATE ROLE authenticator NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            GRANT scoped_behind_the_api TO authenticator;
            GRANT tenancy_app TO scoped_as_the_owner;
            """, Cancellation);

        await ConfinementFailsAsync(database, "scoped_bypassing", "*scoped_bypassing*can bypass row level security*NOBYPASSRLS*");
        await ConfinementFailsAsync(database, "scoped_logging_in", "*scoped_logging_in*can log in*NOLOGIN*");
        await ConfinementFailsAsync(database, "scoped_behind_the_api", "*authenticator, the Data API's login role*REVOKE scoped_behind_the_api FROM authenticator*");
        await ConfinementFailsAsync(database, "scoped_nobody_made", "*does not exist*");
        await ConfinementFailsAsync(database, "scoped_owning", "*it owns tables*");
        await ConfinementFailsAsync(database, "scoped_as_the_owner", "*has the privileges of tenancy_app, which owns tables*REVOKE tenancy_app FROM scoped_as_the_owner*");

        // And for a function of Tenancy's that runs as its owner, which the anonymous role may execute.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "GRANT EXECUTE ON FUNCTION tenancy.caller_seat() TO anon", Cancellation);
        await using var granted = new TenancyServices(database);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*anon may execute tenancy.caller_seat()*REVOKE EXECUTE*");

        // And for each function that answers about other seats' rights, writes the rights, reads across tenants, or
        // takes the tenant as an argument, given to the anonymous role or to every role at once.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "REVOKE EXECUTE ON FUNCTION tenancy.caller_seat() FROM anon", Cancellation);
        foreach (var function in new[]
                 {
                     "tenancy.tenant_administrators()", "tenancy.rights_a_move_changes(uuid,uuid)", "tenancy.seats_holding_at(text,uuid)",
                     "tenancy.rewrite_tenant_rights()", "tenancy.rights_follow_grants()",
                     "tenancy.role_keys_in_use()", "tenancy.tenants_to_sweep()", "tenancy.seats_of_identity(uuid)",
                     "tenancy.seat_in_tenant(bigint)", "tenancy.seated_in_tenant(bigint)", "tenancy.holds_key_in_tenant(bigint,text)",
                     "tenancy.units_where_i_hold_in_tenant(bigint,text)", "tenancy.roles_with_key_in_tenant(bigint,text)",
                 })
        {
            foreach (var (to, says) in new[] { ("anon", "*anon may execute " + function + "*REVOKE EXECUTE*"), ("PUBLIC", "*every role may execute " + function + "*FROM PUBLIC*") })
            {
                await TenancyPostgres.ExecuteAsync(database.ConnectionString, $"GRANT EXECUTE ON FUNCTION {function} TO {to}", Cancellation);
                await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation))
                    .Should().ThrowAsync<InvalidOperationException>().WithMessage(says);
                await TenancyPostgres.ExecuteAsync(database.ConnectionString, $"REVOKE EXECUTE ON FUNCTION {function} FROM {to}", Cancellation);
            }
        }

        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(granted.Provider, Cancellation);
    }

    [Fact]
    public async Task The_policies_check_fails_where_they_are_missing_or_written_from_another_catalogue()
    {
        var secured = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using (var services = new TenancyServices(secured))
        {
            await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
        }

        // The access files were never applied: no row level security on Tenancy's tables.
        var plain = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation);
        await using (var services = new TenancyServices(plain))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Row level security is off on Tenancy's tables*tenancy.\"Seats\",*UseRowAccessContribution*");
        }

        // The application marks a key, and the access file written with the mark is not applied yet.
        await using var marked = new TenancyServices(secured, catalogue: HostCatalogue.Application with { AccessManagingKeys = [HostCatalogue.WidgetCreate] });
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(marked.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*manages_access in the database was written from another catalogue*widget.create*");

        // A pack gains a key, and the access file written with it is not applied yet.
        await using var wider = new TenancyServices(
            secured,
            catalogue: HostCatalogue.Application with
            {
                Packs = [.. HostCatalogue.Application.Packs.Select(pack => pack.Key == HostCatalogue.WatcherPack ? pack with { Keys = [HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate] } : pack)],
            });
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(wider.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*pack_keys in the database was written from another catalogue*watcher*");

        // The application retires a key, and the access file written without it is not applied yet: the database would
        // go on writing rights for it.
        await using var retired = new TenancyServices(
            secured,
            catalogue: HostCatalogue.Application with
            {
                Permissions = [.. HostCatalogue.Permissions.Select(permission => permission.Key == HostCatalogue.WidgetCreate ? permission with { Retired = true } : permission)],
                Packs = [.. HostCatalogue.Application.Packs.Select(pack => pack with { Keys = [.. pack.Keys.Where(key => key != HostCatalogue.WidgetCreate)] })],
            });
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(retired.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*pack_keys in the database was written from another catalogue*");
        await TenancyPostgres.ExecuteAsync(secured.ConnectionString, "CREATE OR REPLACE FUNCTION tenancy.key_is_live(key text) RETURNS boolean LANGUAGE sql IMMUTABLE AS $$ SELECT true $$", Cancellation);
        await using (var services = new TenancyServices(secured))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*key_is_live in the database was written from another catalogue*the live keys are*widget.read*");
        }
    }

    [Fact]
    public async Task The_policies_check_fails_where_the_database_does_not_keep_the_rights_as_the_store_expects()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using var services = new TenancyServices(database);

        // The store would write the rights itself, which the policies let no caller do.
        await using (var writing = new TenancyServices(database, databaseKeepsRights: false))
        {
            await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(writing.Provider, Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*DatabaseKeepsRights is off*AddTenancyPostgres()*");
        }

        // The trigger that writes the rights, gone from one of the tables it follows, or switched off there.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "DROP TRIGGER tenancy_rights_follow_grants ON tenancy.\"Roles\"", Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenancy_rights_follow_grants, which writes the rights, is missing or disabled on tenancy.\"Roles\",*");
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "ALTER TABLE tenancy.\"Seats\" DISABLE TRIGGER tenancy_rights_follow_grants", Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*is missing or disabled on tenancy.\"Roles\", tenancy.\"Seats\",*");

        // The access files applied again put both back.
        foreach (var script in TenancyPostgres.AccessScripts())
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "ALTER TABLE tenancy.\"Seats\" ENABLE TRIGGER tenancy_rights_follow_grants", Cancellation);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);

        // A function the store asks about other seats' rights, missing, or made to run as its caller, who reads no
        // other seat's rights.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "ALTER FUNCTION tenancy.tenant_administrators() SECURITY INVOKER", Cancellation);
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "DROP FUNCTION tenancy.seats_holding_at(text, uuid)", Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(
                "The functions of tenancy that answer about other seats' rights or write a tenant's rights again are not as Tenancy's contribution writes them: " +
                "seats_holding_at (missing), tenant_administrators (it does not run as its owner). The store and the questions ask the first three*");

        // A function system work reads across tenants through, likewise: in no tenant, it reads no row itself.
        await MendedAsync();
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "ALTER FUNCTION tenancy.tenants_to_sweep() SECURITY INVOKER; DROP FUNCTION tenancy.seats_of_identity(uuid);", Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(
                "The functions of tenancy that read across tenants are not as Tenancy's contribution writes them: " +
                "seats_of_identity (missing), tenants_to_sweep (it does not run as its owner). System work asks them in no tenant*");

        // And a question that takes the tenant as an argument: as its caller, it would be held to the tenant the
        // connection names, which is none where such a question is asked.
        await MendedAsync();
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "ALTER FUNCTION tenancy.roles_with_key_in_tenant(bigint, text) SECURITY INVOKER; DROP FUNCTION tenancy.seated_in_tenant(bigint);", Cancellation);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(
                "The functions of tenancy that take the tenant as an argument are not as Tenancy's contribution writes them: " +
                "roles_with_key_in_tenant (it does not run as its owner), seated_in_tenant (missing). Policies ask them where the connection names no tenant*");
        await MendedAsync();

        // The access files applied again write every function as it was.
        async Task MendedAsync()
        {
            foreach (var script in TenancyPostgres.AccessScripts())
            {
                await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
            }

            await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
        }
    }

    [Fact]
    public async Task The_policies_check_fails_where_a_function_modules_read_through_is_missing_or_not_as_it_was_written()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using var services = new TenancyServices(database);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);

        // Each of these keeps a function a module reads through from being folded into the module's query, or lets it
        // answer past the policies on Tenancy's tables: a search path of its own, running as its owner, strict, and
        // volatile. And one of them gone.
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            """
            ALTER FUNCTION tenancy.caller_rights() SET search_path = '';
            ALTER FUNCTION tenancy.tenant_roles() SECURITY DEFINER;
            ALTER FUNCTION tenancy.tenant_units() STRICT;
            ALTER FUNCTION tenancy.tenant_unit_paths() VOLATILE;
            DROP FUNCTION tenancy.tenant_seats();
            """,
            Cancellation);

        const string Changed = " (it runs as its owner, has settings of its own, or is strict or volatile)";
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(
                "The functions of tenancy that modules read Tenancy through are not as Tenancy's contribution writes them: " +
                $"caller_rights{Changed}, tenant_roles{Changed}, tenant_seats (missing), tenant_unit_paths{Changed}, tenant_units{Changed}.*AddTenancyReadFunctions*");

        // The access files applied again write each as it was: replacing a function takes its settings off as well.
        foreach (var script in TenancyPostgres.AccessScripts())
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
    }

    [Fact]
    public async Task A_read_function_that_answers_another_column_fails_the_start_up_check()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using var services = new TenancyServices(database);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);

        // A database whose functions were written for another read model: the seats answered with their display
        // names, and the units' columns in another order. Each still runs as its caller and folds into a query, so
        // nothing but its columns is wrong with it.
        await TenancyPostgres.ExecuteAsync(
            database.ConnectionString,
            """
            DROP FUNCTION tenancy.tenant_seats();
            CREATE FUNCTION tenancy.tenant_seats() RETURNS TABLE ("Id" uuid, "TenantId" bigint, "DisplayName" text, "Status" text)
                LANGUAGE sql STABLE AS 'SELECT t."Id", t."TenantId", t."DisplayName"::text, t."Status"::text FROM tenancy."Seats" t';
            DROP FUNCTION tenancy.tenant_units();
            CREATE FUNCTION tenancy.tenant_units() RETURNS TABLE ("Id" uuid, "ParentId" uuid, "TenantId" bigint, "Status" text)
                LANGUAGE sql STABLE AS 'SELECT t."Id", t."ParentId", t."TenantId", t."Status"::text FROM tenancy."OrganizationUnits" t';
            """,
            Cancellation);

        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(
                "The functions of tenancy that modules read Tenancy through do not answer the columns of the read model: " +
                "tenant_seats answers Id, TenantId, DisplayName, Status where the read model has Id, TenantId, Status; " +
                "tenant_units answers Id, ParentId, TenantId, Status where the read model has Id, TenantId, ParentId, Status. " +
                "*access facts only, never a name*drop the ones named here first. Export the access files with Tenancy's contribution, and apply them.");

        // Postgres replaces no function by one that answers other columns, so the access files alone do not put it right.
        var refused = await FluentActions.Awaiting(async () =>
        {
            foreach (var script in TenancyPostgres.AccessScripts())
            {
                await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
            }
        }).Should().ThrowAsync<Npgsql.PostgresException>();
        refused.Which.SqlState.Should().Be(Npgsql.PostgresErrorCodes.InvalidFunctionDefinition);

        // With the two functions out of the way first, they are written as the read model has them.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, "DROP FUNCTION tenancy.tenant_seats(); DROP FUNCTION tenancy.tenant_units();", Cancellation);
        foreach (var script in TenancyPostgres.AccessScripts())
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
    }

    private static async Task ConfinementFailsAsync(TestDatabase database, string role, string message)
    {
        await using var services = new TenancyServices(database, roles: roles => roles.SystemInRole = role);
        await FluentActions.Awaiting(() => TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(services.Provider, Cancellation))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
    }
}
