using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// What Tenancy reads before any tenant is known, it reads with nothing running past the policies: as scoped system
/// work in no tenant, which the policies show no row of any table, through three functions of the database that run as
/// their owner and answer ids or keys only. The keys stored on every tenant's roles and the seats a person has are
/// answered to Tenancy's own work and to no other scope's; the tenants to visit to the system work of every module.
/// No seat and no anonymous caller may ask any of them, and nothing of Tenancy's begins the application itself. The
/// start-up check that proves the functions are <see cref="StartUpCheckTests"/>.
/// </summary>
public abstract class SystemReadFunctionTests(TenancyPostgres postgres, TenancyNaming names)
{
    private const string ScopedRole = "ddd_system_in";

    private const string KeysInUse = "SELECT f.v AS \"Value\" FROM tenancy.role_keys_in_use() AS f(v)";

    private const string TenantsToSweep = "SELECT f.v AS \"TenantId\" FROM tenancy.tenants_to_sweep() AS f(v)";

    /// <summary>Held while a test makes a role of the server's, which the same test under the other naming makes too.</summary>
    private static readonly SemaphoreSlim MakingTheRole = new(1, 1);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_keys_in_use_are_read_across_tenants_by_tenancys_own_system_work()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Keys the code no longer declares, stored on roles of two tenants, one of them suspended.
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ExecuteAsync("UPDATE tenancy.\"Roles\" SET \"Keys\" = pg_catalog.array_append(\"Keys\", 'widget.polish') WHERE \"Id\" = $1", Cancellation, OrchardRoles.Watcher.Value)).Should().Be(1);
            (await owner.ExecuteAsync("UPDATE tenancy.\"Roles\" SET \"Keys\" = pg_catalog.array_append(\"Keys\", 'widget.paint') WHERE \"Id\" = $1", Cancellation, QuayRoles.Operator.Value)).Should().Be(1);
            await owner.CommitAsync(Cancellation);
        }

        var recorder = new CommandRecorder();
        var sessions = new SessionRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder, sessions));

        // Asked at start-up, before anyone calls: the package begins its own scoped system work for that one query.
        Callers.Ambient.Should().BeNull();
        var unknown = await services.InScopeAsync(scoped => TenancyChecks.UnknownStoredKeysAsync<HostRole, RoleId, TenantId>(
            scoped.Tenancy(), scoped.GetRequiredService<TenancyCatalogue>(), Cancellation));

        unknown.Should().Equal("widget.paint", "widget.polish");
        var read = recorder.Sent.Should().ContainSingle("one query, on a context of its own").Subject;
        read.Caller!.Kind.Should().Be(CallerKind.SystemIn, "scoped system work, which the policies hold, and not the application itself");
        read.Caller.Scope.Should().Be(TenancyWork.SystemScope, "the scope of Tenancy's work");
        read.TenancyCaller.Should().BeNull("in no tenant");
        read.Text.Trim().Should().Be(KeysInUse, "it asks the function, which answers keys and nothing else of any role, and names no table");
        sessions.Seen.Should().Equal([new RecordedSession(KeysInUse, ScopedRole, Tenant: "", TenancyWork.SystemScope)], "and so the database saw it");

        // The caller's own work is untouched by it.
        Callers.Ambient.Should().BeNull();

        // By a query, as that work: the function answers every tenant's keys, each once, on a connection the policies
        // show no role at all.
        await using var work = await AsCaller.SystemInAsync(database, tenant: null, TenancyWork.SystemScope, Cancellation);
        (await work.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Roles\"", Cancellation)).Should().Be(0, "in no tenant, the policies show system work no role");
        var keys = await work.ListAsync<string>("SELECT tenancy.role_keys_in_use()", Cancellation);

        await using var stored = await AsCaller.OwnerAsync(database, Cancellation);
        keys.Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(await stored.ListAsync<string>("SELECT DISTINCT pg_catalog.unnest(\"Keys\") FROM tenancy.\"Roles\"", Cancellation))
            .And.Contain(["widget.paint", "widget.polish"]).And.Contain(TenancyPostgres.Catalogue.LiveKeys);
    }

    [Fact]
    public async Task A_read_across_tenants_in_another_scope_answers_nothing()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Another module's system work, in no tenant and in one: it is answered no key and nobody's seats.
        foreach (var tenant in new TenantId?[] { null, Harbor })
        {
            await using var other = await AsCaller.SystemInAsync(database, tenant, "widgets", Cancellation);
            (await other.ListAsync<string>("SELECT tenancy.role_keys_in_use()", Cancellation)).Should().BeEmpty("the keys in use are for Tenancy's own work to read");
            (await other.ScalarAsync<long>("SELECT count(*) FROM tenancy.seats_of_identity($1)", Cancellation, Oli.Identity)).Should().Be(0, "and so are a person's seats");
        }

        // Tenancy's own work is answered both: the scope is what kept them from the other.
        await using var own = await AsCaller.SystemInAsync(database, tenant: null, TenancyWork.SystemScope, Cancellation);
        (await own.ListAsync<string>("SELECT tenancy.role_keys_in_use()", Cancellation)).Should().NotBeEmpty();
        (await own.ScalarAsync<long>("SELECT count(*) FROM tenancy.seats_of_identity($1)", Cancellation, Oli.Identity)).Should().Be(2);
    }

    [Fact]
    public async Task The_tenants_to_sweep_are_answered_to_system_work_in_any_scope()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        var sessions = new SessionRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder, sessions));
        string[] scopes = ["widgets", TenancyWork.SystemScope, "inspections"];

        // The round of any module asks, in its own scope and in no tenant, and learns the ids.
        foreach (var scope in scopes)
        {
            var asked = await services.InScopeAsync(scoped => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Tenancy(), scope, Cancellation));
            asked.Should().BeEquivalentTo([Harbor, Orchard, Quay], "asked in the scope {0}", scope);
        }

        recorder.Sent.Should().HaveCount(3).And.OnlyContain(command => command.Text.Trim() == TenantsToSweep && command.TenancyCaller == null, "each is one query of the function, in no tenant");
        recorder.Sent.Select(command => command.Caller!.ToString()).Should().Equal(scopes.Select(scope => "system in " + scope));
        sessions.Seen.Should().Equal(scopes.Select(scope => new RecordedSession(TenantsToSweep, ScopedRole, Tenant: "", scope)));

        // A scope that is none is refused before anything is asked.
        recorder.Clear();
        await FluentActions.Awaiting(() => services.InScopeAsync(scoped => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Tenancy(), "Not a scope", Cancellation)))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*'Not a scope'*");
        recorder.Sent.Should().BeEmpty();

        // What the ids are for: the module's own system work in each tenant in turn, under its own scope, which reads
        // that tenant's rows and no other's.
        var visited = new Dictionary<TenantId, List<string>>();
        foreach (var tenant in await services.InScopeAsync(scoped => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Tenancy(), "widgets", Cancellation)))
        {
            visited[tenant] = await services.BySystemIn(
                tenant,
                scoped => scoped.Widgets().Widgets.OrderBy(widget => widget.Name).Select(widget => widget.Name).ToListAsync(Cancellation),
                scope: "widgets");
        }

        visited.Should().HaveCount(3);
        visited[Harbor].Should().Equal("Gauge", "Pump", "Valve");
        visited[Orchard].Should().Equal("Crate");
        visited[Quay].Should().BeEmpty();

        // By a query, in whichever scope, and whatever tenant the connection names.
        foreach (var (tenant, scope) in new (TenantId?, string)[] { (null, "widgets"), (null, TenancyWork.SystemScope), (Orchard, "inspections") })
        {
            await using var work = await AsCaller.SystemInAsync(database, tenant, scope, Cancellation);
            (await work.ListAsync<long>("SELECT tenancy.tenants_to_sweep()", Cancellation)).Should().BeEquivalentTo([Harbor.Value, Orchard.Value, Quay.Value]);
        }
    }

    [Fact]
    public async Task A_read_across_tenants_and_a_start_up_check_take_their_context_as_their_own_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // A host may make a context for whoever is calling: another data source, say. What is current when a
        // context's options are built is what such a host would see, and a scope builds them as it takes the context.
        var taken = new List<(Caller? Caller, ITenancyCaller? Tenancy)>();
        await using var services = new TenancyServices(database, contexts: _ => taken.Add((Callers.Ambient, TenancyCallers.Ambient)));

        // In the middle of Oli's work: the read's own context is taken as scoped system work in no tenant, not as Oli.
        (await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped =>
            {
                var like = scoped.Tenancy();
                taken.Clear();
                return TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(like, "widgets", Cancellation);
            }))
            .Should().BeEquivalentTo([Harbor, Orchard, Quay]);
        var asked = taken.Should().ContainSingle().Subject;
        asked.Caller!.ToString().Should().Be("system in widgets");
        asked.Tenancy.Should().BeNull();

        // At start-up nothing has begun a caller: every context a check takes, it takes as the caller it asks as.
        taken.Clear();
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(services.Provider, Cancellation);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);
        taken.Should().NotBeEmpty().And.OnlyContain(current => current.Caller != null && current.Caller.Kind == CallerKind.System);

        taken.Clear();
        await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation);
        taken.Should().NotBeEmpty().And.OnlyContain(current => current.Caller != null && (current.Caller.Kind == CallerKind.System || current.Caller.Kind == CallerKind.SystemIn) && current.Tenancy == null);
        taken.Select(current => current.Caller!.ToString()).Should().Contain("system in " + TenancyWork.SystemScope);
    }

    [Fact]
    public async Task The_tenants_to_sweep_are_the_active_and_suspended_ids()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var services = new TenancyServices(database);

        Task<IReadOnlyList<TenantId>> ToSweepAsync()
            => services.InScopeAsync(scoped => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Tenancy(), "widgets", Cancellation));

        // Harbor and Orchard are active, Quay is suspended: a suspended tenant is still visited, to end what ends in it.
        (await ToSweepAsync()).Should().BeEquivalentTo([Harbor, Orchard, Quay]);

        // A closed tenant is not: nothing changes in it any more.
        await services.BySystemIn(Orchard, scoped => scoped.Tenants().CloseAsync("Wound up", Cancellation));
        (await ToSweepAsync()).Should().BeEquivalentTo([Harbor, Quay]);

        // Nor is one still being set up, a status no use case leaves a tenant in.
        await TenancyPostgres.ExecuteAsync(database.ConnectionString, names.Sql("UPDATE tenancy.\"Tenants\" SET \"Status\" = 'Provisioning' WHERE \"Id\" = 3"), Cancellation);
        (await ToSweepAsync()).Should().Equal(Harbor);

        // Ids, and nothing else of a tenant.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<string>(
                """
                SELECT pg_catalog.pg_get_function_identity_arguments(p.oid) || '|' || pg_catalog.pg_get_function_result(p.oid)
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'tenancy' AND p.proname = 'tenants_to_sweep'
                """,
                Cancellation))
            .Should().Be("|SETOF bigint");
    }

    [Fact]
    public async Task A_persons_seats_are_found_across_tenants_as_ids()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var work = await AsCaller.SystemInAsync(database, tenant: null, TenancyWork.SystemScope, Cancellation);

        Task<List<string>> SeatsOfAsync(Guid identity)
            => work.ListAsync<string>("SELECT found.\"TenantId\"::text || ' ' || found.\"SeatId\"::text FROM tenancy.seats_of_identity($1) AS found ORDER BY 1", Cancellation, identity);

        static string Found(TenantId tenant, SeatId seat) => $"{tenant.Value} {seat.Value:D}";

        // Every seat of the person, in every tenant and whatever its status or the tenant's: what is erased is erased
        // in each of them.
        (await SeatsOfAsync(Oli.Identity)).Should().Equal(Found(Harbor, Oli.Seat), Found(Orchard, OliInOrchard));
        (await SeatsOfAsync(Sue.Identity)).Should().Equal([Found(Harbor, Sue.Seat)], "a suspended seat is a seat");
        (await SeatsOfAsync(Quin.Identity)).Should().Equal([Found(Quay, Quin.Seat)], "and so is a seat in a suspended tenant");
        (await SeatsOfAsync(Guid.Parse("d0000000-0000-4000-8000-000000000099"))).Should().BeEmpty("someone without a seat anywhere");

        // The connection that asked reads no seat itself.
        (await work.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"Seats\"", Cancellation)).Should().Be(0);

        // A tenant and a seat, as ids: no name, and not the identity back.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<string>(
                """
                SELECT pg_catalog.pg_get_function_identity_arguments(p.oid) || '|' || pg_catalog.pg_get_function_result(p.oid)
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'tenancy' AND p.proname = 'seats_of_identity'
                """,
                Cancellation))
            .Should().Be("identity uuid|TABLE(\"TenantId\" bigint, \"SeatId\" uuid)");
    }

    [Fact]
    public async Task A_seat_cannot_execute_the_reads_across_tenants()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        (string Function, string Asked)[] reads =
        [
            (TenancyFunctionNames.RoleKeysInUse, "SELECT tenancy.role_keys_in_use()"),
            (TenancyFunctionNames.TenantsToSweep, "SELECT tenancy.tenants_to_sweep()"),
            (TenancyFunctionNames.SeatsOfIdentity, $"SELECT count(*) FROM tenancy.seats_of_identity('{Oli.Identity:D}')"),
        ];

        // Not an administrator, whatever she holds; not a plain member; and nobody who has not signed in.
        await using var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation);
        await using var oli = await AsCaller.PersonAsync(database, Oli.Identity, Orchard, Cancellation);
        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);
        foreach (var (who, caller) in new[] { ("an administrator", ada), ("a plain member", oli), ("an anonymous caller", anonymous) })
        {
            foreach (var (function, asked) in reads)
            {
                var refusal = await FluentActions.Awaiting(() => caller.AttemptAsync(asked, Cancellation)).Should().ThrowAsync<PostgresException>("{0} asks {1}", who, function);
                refusal.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
                refusal.Which.MessageText.Should().Be($"permission denied for function {function}");
            }
        }

        // As the catalog has it: the scoped system role alone, and not every role at once.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>(
                """
                SELECT p.proname || ' ' || pg_catalog.has_function_privilege('ddd_system_in', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('authenticated', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('anon', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('public', p.oid, 'EXECUTE')
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'tenancy' AND p.proname = ANY ($1) ORDER BY p.proname
                """,
                Cancellation,
                (object)reads.Select(read => read.Function).ToArray()))
            .Should().Equal(
                "role_keys_in_use true false false false",
                "seats_of_identity true false false false",
                "tenants_to_sweep true false false false");
    }

    [Fact]
    public async Task A_read_across_tenants_made_inside_a_seats_work_runs_in_no_tenant()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var sessions = new SessionRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(sessions));

        // Oli, a plain member of Harbor, in the middle of a request that asks both. The tenant of his seat does not
        // travel with either read: with it, the first would be Tenancy's own system work in Harbor, which reads and
        // writes all of it.
        var (unknown, tenants) = await services.BySeat(Oli.Identity, Harbor, Oli.Seat, async scoped =>
        {
            var keys = await TenancyChecks.UnknownStoredKeysAsync<HostRole, RoleId, TenantId>(scoped.Tenancy(), scoped.GetRequiredService<TenancyCatalogue>(), Cancellation);
            var ids = await TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Tenancy(), "widgets", Cancellation);

            // His own work goes on as it was: as him, in his seat.
            Callers.Ambient!.UserId.Should().Be(Oli.Identity);
            TenancyCallers.Ambient.Should().Be(HostCaller.InSeat(Harbor, Oli.Seat));
            (await scoped.Tenancy().Set<SeatRow<TenantId, SeatId>>().CountAsync(Cancellation)).Should().Be(6, "the seats of Harbor");
            return (keys, ids);
        });

        unknown.Should().BeEmpty();
        tenants.Should().BeEquivalentTo([Harbor, Orchard, Quay]);
        sessions.Seen.Select(session => (session.Role, session.Tenant, session.Scope)).Should().Equal(
            (ScopedRole, "", TenancyWork.SystemScope),
            (ScopedRole, "", "widgets"),
            ("authenticated", "1", null));
    }

    [Fact]
    public async Task The_reads_across_tenants_answer_on_contexts_taken_from_a_pool()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var sessions = new SessionRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(sessions), pooled: true);
        var catalogue = services.Provider.GetRequiredService<TenancyCatalogue>();

        // A pool hands the read a context somebody else had, and hands it on afterwards: seats and the reads across
        // tenants take turns, twelve at once, and each is answered as itself.
        var flows = Enumerable.Range(0, 12).Select(index => Task.Run(async () =>
        {
            for (var round = 0; round < 3; round++)
            {
                (await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Tenancy(), "widgets", Cancellation)))
                    .Should().BeEquivalentTo([Harbor, Orchard, Quay]);
                (await services.BySeat(Odette.Identity, Orchard, Odette.Seat, scoped => scoped.Tenancy().Set<SeatRow<TenantId, SeatId>>().CountAsync(Cancellation)))
                    .Should().Be(2, "Orchard's seats, read by a seat on a context a read across tenants may just have given back");
                (await services.InScopeAsync(scoped => TenancyChecks.UnknownStoredKeysAsync<HostRole, RoleId, TenantId>(scoped.Tenancy(), catalogue, Cancellation)))
                    .Should().BeEmpty();
            }
        }));
        await Task.WhenAll(flows);

        sessions.Seen.Should().HaveCount(12 * 3 * 3);
        sessions.Seen.Should().OnlyContain(session =>
            session.Text == TenantsToSweep ? session == new RecordedSession(TenantsToSweep, ScopedRole, "", "widgets")
            : session.Text == KeysInUse ? session == new RecordedSession(KeysInUse, ScopedRole, "", TenancyWork.SystemScope)
            : session.Role == "authenticated" && session.Tenant == "2" && session.Scope == null);

        // And the start-up check that proves them finds a context that comes from a pool.
        await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation);
    }

    [Fact]
    public async Task Tenancy_begins_no_system_caller_when_the_database_keeps_the_rights()
    {
        // The tables and the access files, and no data yet: seeded here, under watch.
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Plain, Cancellation, names);
        foreach (var script in TenancyPostgres.AccessScripts(names: names))
        {
            await TenancyPostgres.ExecuteAsync(database.ConnectionString, script, Cancellation);
        }

        // The application itself, should anything begin it, runs here as a role that may read no table of Tenancy's nor
        // of the widgets', and use neither schema: whatever needed its reach past the policies would fail.
        // A role is the server's, which this test shares with the same test under the other naming: one at a time.
        await MakingTheRole.WaitAsync(Cancellation);
        try
        {
            await TenancyPostgres.ExecuteAsync(database.SuperuserConnectionString, $"""
                DO $do$ BEGIN CREATE ROLE tenancy_beside_the_tables NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
                GRANT tenancy_beside_the_tables TO {TenancyPostgres.LoginRole};
                """, Cancellation);
        }
        finally
        {
            MakingTheRole.Release();
        }

        var callers = new ObservedCallers();
        await using var services = new TenancyServices(
            database,
            configure: collection => collection.AddSingleton<ICallerAccessor>(callers),
            roles: roles => roles.SystemRole = "tenancy_beside_the_tables");
        var catalogue = services.Provider.GetRequiredService<TenancyCatalogue>();

        const string Seeding = "seeding", UseCases = "every use case", Reads = "the reads across tenants", Checks = "the start-up checks";

        callers.During = Seeding;
        await TenancySeed.SeedAsync(services, await TenancySeed.DatabaseNowAsync(database.ConnectionString, Cancellation), Cancellation);

        callers.During = UseCases;
        await EveryUseCase.RunAsync(services, Cancellation);

        callers.During = Reads;
        (await services.InScopeAsync(scoped => TenancyChecks.UnknownStoredKeysAsync<HostRole, RoleId, TenantId>(scoped.Tenancy(), catalogue, Cancellation))).Should().BeEmpty();
        (await services.InScopeAsync(scoped => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Tenancy(), "widgets", Cancellation)))
            .Should().BeEquivalentTo([Harbor, Orchard, Quay]);

        callers.During = Checks;
        TenancyPostgresChecks.EnsureExplicitCallers(services.Provider);
        await TenancyPostgresChecks.EnsureSystemInRoleIsConfinedAsync(services.Provider, Cancellation);
        await TenancyPostgresChecks.EnsureSystemReadsAcrossTenantsAsync(services.Provider, Cancellation);
        await TenancyPostgresChecks.EnsurePoliciesAreInPlaceAsync(services.Provider, Cancellation);

        // Every connection ran as a person in a seat or as scoped system work, which the policies hold. The application
        // itself was begun by the start-up checks alone, which read Postgres's catalog and no table of Tenancy's: they
        // passed as a role that reaches none.
        callers.Answered.Select(answered => answered.During).Distinct().Should().BeEquivalentTo([Seeding, UseCases, Reads, Checks]);
        callers.Answered.Where(answered => answered.Caller.IsSystem).Select(answered => answered.During).Distinct().Should().Equal(Checks);
        callers.Answered.Where(answered => answered.During != Checks).Select(answered => answered.Caller.Kind).Distinct()
            .Should().BeEquivalentTo([CallerKind.User, CallerKind.SystemIn]);
        callers.Answered.Where(answered => answered.During == Reads).Select(answered => answered.Caller.ToString()).Distinct()
            .Should().BeEquivalentTo(["system in " + TenancyWork.SystemScope, "system in widgets"]);
    }
}

/// <summary>Tenancy's reads across tenants, and the start-up checks after every use case, under the names Entity Framework gives the tables and columns.</summary>
public sealed class SystemReadFunctionTestsOnDefaultNames(TenancyPostgres postgres) : SystemReadFunctionTests(postgres, TenancyNaming.Default);

/// <summary>Tenancy's reads across tenants, and the start-up checks after every use case, under snake_case names with enums stored as snake_case text.</summary>
public sealed class SystemReadFunctionTestsOnSnakeCase(TenancyPostgres postgres) : SystemReadFunctionTests(postgres, TenancyNaming.SnakeCase);
