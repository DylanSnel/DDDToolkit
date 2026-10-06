using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The sync of the role packs over Tenancy's context (<see cref="IRolePackSync"/>), as <c>AddTenancy</c> registers
/// it: it visits every active and suspended tenant, and in each the roles follow the packs of the catalogue the
/// application runs with now, as Tenancy's system work in that tenant. The tenants are provisioned by one version of
/// the application, and synced by the next, on the same database, whose watcher's pack also creates widgets and whose
/// operator's no longer does.
/// </summary>
public abstract class RolePackSyncTests(TestDatabases databases) : IAsyncLifetime
{
    private const string RoleFollowedItsPack = "tenancy.role-followed-its-pack";

    private TestServices _services = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _services = await databases.ServicesAsync();

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Every_active_or_suspended_tenant_follows_the_packs_the_application_runs_with_now()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var orchard = await _services.ProvisionAsync("orchard", TenantShape.Flat);
        var quay = await _services.ProvisionAsync("quay");
        await _services.BySystemIn(orchard.Tenant, services => services.Tenants().SuspendAsync("unpaid", Cancellation));
        await _services.BySystemIn(quay.Tenant, services => services.Tenants().CloseAsync("wound up", Cancellation));
        var lin = await _services.SeatAtAsync(harbor, "Lin", harbor.RootUnit, HostCatalogue.OperatorPack);

        using var later = Later();
        var report = await later.Provider.GetRequiredService<IRolePackSync>().SyncAsync(Cancellation);

        report.Should().BeEquivalentTo(new { Tenants = 2, RolesChanged = 4, RolesKeptForAnAdministrator = 0, RolesWithoutTheirPack = 0, Succeeded = true });
        (await KeysAsync(harbor, HostCatalogue.WatcherPack)).Should().Equal(HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
        (await KeysAsync(orchard, HostCatalogue.OperatorPack)).Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetRead);
        (await KeysAsync(quay, HostCatalogue.WatcherPack)).Should().Equal([HostCatalogue.WidgetRead], "a closed tenant is closed for good");
        (await _services.StoredRightsAsync(lin)).Select(right => right.Key).Should().BeEquivalentTo(
            [HostCatalogue.WidgetChange, HostCatalogue.WidgetRead], "the operator's holder lost creating widgets with the save that changed the role");

        var kept = await FollowedAsync();
        kept.Should().HaveCount(4).And.OnlyContain(entry => entry.ActedByKind == TenancyActorKinds.System);
        kept.Select(entry => entry.Tenant).Should().BeEquivalentTo([harbor.Tenant, harbor.Tenant, orchard.Tenant, orchard.Tenant]);

        // A second run, or the next start of the host, reads and changes nothing.
        var again = await later.Provider.GetRequiredService<IRolePackSync>().SyncAsync(Cancellation);
        again.RolesChanged.Should().Be(0);
        again.Failed.Should().BeEmpty();
        (await FollowedAsync()).Should().HaveCount(4);
    }

    [Fact]
    public async Task A_tenant_another_run_changed_meanwhile_is_read_again_and_changes_nothing_twice()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        using var later = Later();
        var sync = later.Provider.GetRequiredService<IRolePackSync>();

        // Another instance of the host syncs while this one is saving what it decided.
        RolePackSyncReport? other = null;
        later.Hook.BeforeAnySave(async () => other = await sync.SyncAsync(Cancellation));

        var report = await sync.SyncAsync(Cancellation);

        later.Hook.Fired.Should().BeTrue("the other run committed while this one was saving");
        other!.RolesChanged.Should().Be(2);
        report.RolesChanged.Should().Be(0, "its save failed on the tenant's access revision, and read again it found nothing left to change");
        report.Failed.Should().BeEmpty();
        (await FollowedAsync()).Should().HaveCount(2, "each role followed its pack once");
        (await KeysAsync(harbor, HostCatalogue.WatcherPack)).Should().Equal(HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
    }

    /// <summary>The next version of the application, on the same database: its catalogue's packs have changed.</summary>
    private TestServices Later()
        => new(database: _services.Database, afterTenancy: services => services.AddSingleton(TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                Packs = [.. HostCatalogue.Application.Packs.Select(pack => pack.Key switch
                {
                    HostCatalogue.WatcherPack => pack with { Keys = [HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate] },
                    HostCatalogue.OperatorPack => pack with { Keys = [HostCatalogue.WidgetChange] },
                    _ => pack,
                })],
            },
            [])));

    /// <summary>The keys of a tenant's role made from <paramref name="pack"/>, as saved.</summary>
    private Task<List<string>> KeysAsync(HostTenancy.ProvisionedTenant tenant, string pack)
        => _services.BySystemIn(tenant.Tenant, async services =>
            (await services.Tenancy().Set<HostRole>().SingleAsync(role => role.Id == tenant.RolesByPack[pack], Cancellation)).Keys.ToList());

    /// <summary>The access history's rows of roles that followed their pack, in every tenant.</summary>
    private async Task<List<(TenantId Tenant, string ActedByKind)>> FollowedAsync()
    {
        await using var scope = _services.Scope();
        var rows = await scope.ServiceProvider.Tenancy().Set<EventLogEntry>()
            .Where(entry => entry.EventName == RoleFollowedItsPack)
            .Select(entry => new { Tenant = EF.Property<TenantId>(entry, TenancyEventLogTable.TenantId), entry.ActedByKind })
            .ToListAsync(Cancellation);
        return [.. rows.Select(row => (row.Tenant, row.ActedByKind))];
    }
}

/// <summary>The sync of the role packs, on SQLite in memory.</summary>
public sealed class RolePackSyncTestsOnSqlite() : RolePackSyncTests(TestDatabases.Sqlite);

/// <summary>The sync of the role packs, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql, not the policies.</summary>
public sealed class RolePackSyncTestsOnPostgres(PostgresDatabases postgres) : RolePackSyncTests(postgres);
