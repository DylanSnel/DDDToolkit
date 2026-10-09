using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// The sync of the role packs on Postgres, with every policy forced on the tables' owner: the database lets its writes
/// through, as Tenancy's own system work in each tenant, and keeps the rights of every holder of a role that changed;
/// every other writer is held to the roles' guards as before; and two syncs at once change each role once.
/// <para>
/// Harbor's Operator role is stored as a version of the application made it whose operator's pack did not create
/// widgets yet: without the key, and remembering a pack without it. The application syncs with the pack as it is now.
/// </para>
/// </summary>
public sealed class RolePackSyncTests(TenancyPostgres postgres)
{
    private const string RoleFollowedItsPack = "tenancy.role-followed-its-pack";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sync_writes_under_forced_policies_and_the_holders_rights_follow()
    {
        var database = await BehindAndForcedAsync();
        await using var services = new TenancyServices(database);
        (await OlisKeysAsync(services)).Should().NotContain(HostCatalogue.WidgetCreate, "Oli is an Operator at North Pier, of the older pack");

        var report = await services.Provider.GetRequiredService<IRolePackSync>().SyncAsync(Cancellation);

        report.Failed.Should().BeEmpty();
        report.Should().BeEquivalentTo(new { Tenants = 3, RolesChanged = 1, RolesKeptForAnAdministrator = 0, RolesWithoutTheirPack = 0 }, "Quay is suspended, and visited all the same");
        var operatorRole = await services.BySystemIn(Harbor, scoped => scoped.Tenancy().Set<HostRole>().AsNoTracking().SingleAsync(role => role.Id == HarborRoles.Operator, Cancellation));
        operatorRole.Keys.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
        operatorRole.KeysFromPack.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
        (await OlisKeysAsync(services)).Should().Contain(HostCatalogue.WidgetCreate, "the database wrote the rights of every holder as the role changed");

        var kept = await FollowedAsync(services);
        var row = kept.Should().ContainSingle().Which;
        row.ActedByKind.Should().Be(TenancyActorKinds.System);
        row.Payload.Should().ContainAll("\"Pack\":\"operator\"", "\"Added\":[\"widget.create\"]", "\"Removed\":[]", "\"ManagingAccess\":[]");

        // The next start of the host reads, and writes nothing.
        var again = await services.Provider.GetRequiredService<IRolePackSync>().SyncAsync(Cancellation);
        again.RolesChanged.Should().Be(0);
        (await FollowedAsync(services)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Every_other_writer_is_held_to_the_roles_guards_as_before()
    {
        var database = await BehindAndForcedAsync();
        string Remembering(string role) => $"UPDATE tenancy.\"Roles\" SET \"KeysFromPack\" = ARRAY['widget.change', 'widget.create', 'widget.read'] WHERE \"Id\" = '{role}'";
        var operatorRole = HarborRoles.Operator.Value.ToString();

        // The administrator changes a role's keys, and nothing of what its pack gave it or which pack that was.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ExecuteAsync($"UPDATE tenancy.\"Roles\" SET \"Keys\" = ARRAY['widget.read'] WHERE \"Id\" = '{operatorRole}'", Cancellation)).Should().Be(1);
            var remembered = await FluentActions.Awaiting(() => ada.ExecuteAsync(Remembering(operatorRole), Cancellation)).Should().ThrowAsync<PostgresException>();
            remembered.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            remembered.Which.ConstraintName.Should().Be("tenancy_role_follows_its_pack");
        }

        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await FluentActions.Awaiting(() => ada.ExecuteAsync($"UPDATE tenancy.\"Roles\" SET \"FromPack\" = 'watcher' WHERE \"Id\" = '{operatorRole}'", Cancellation))
                    .Should().ThrowAsync<PostgresException>())
                .Which.ConstraintName.Should().Be("tenancy_role_pack_is_fixed");
        }

        // Another module's system work writes no role; Tenancy's own writes what a sync writes.
        await using (var widgets = await AsCaller.SystemInAsync(database, Harbor, "widgets", Cancellation))
        {
            (await widgets.ExecuteAsync(Remembering(operatorRole), Cancellation)).Should().Be(0);
        }

        await using (var tenancy = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            (await tenancy.ExecuteAsync(Remembering(operatorRole), Cancellation)).Should().Be(1);
        }

        // And Tenancy's own work is held to the tenant's last administrator, as everyone is: what the use case keeps a
        // role for, the database refuses at commit.
        await using var lastAdministrator = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation);
        (await lastAdministrator.ExecuteAsync(
                $"UPDATE tenancy.\"Roles\" SET \"Keys\" = pg_catalog.array_remove(\"Keys\", 'tenancy.roles.manage') WHERE \"Id\" = '{HarborRoles.Administrator.Value}'",
                Cancellation))
            .Should().Be(1);
        (await FluentActions.Awaiting(() => lastAdministrator.CommitAsync(Cancellation)).Should().ThrowAsync<PostgresException>())
            .Which.ConstraintName.Should().Be("tenancy_administrator_remains");
    }

    [Fact]
    public async Task Two_syncs_at_once_change_each_role_once()
    {
        var database = await BehindAndForcedAsync();

        // Two instances of the host: the second syncs while the first is saving what it decided in Harbor.
        await using var second = new TenancyServices(database);
        RolePackSyncReport? other = null;
        var firstSave = new FirstSave(async () => other = await second.Provider.GetRequiredService<IRolePackSync>().SyncAsync(Cancellation));
        await using var first = new TenancyServices(database, contexts: options => options.AddInterceptors(firstSave));

        var report = await first.Provider.GetRequiredService<IRolePackSync>().SyncAsync(Cancellation);

        firstSave.Fired.Should().BeTrue("the second instance committed while the first was saving");
        other!.RolesChanged.Should().Be(1);
        report.Failed.Should().BeEmpty("a save that lost the race on the tenant's access revision is read again");
        report.RolesChanged.Should().Be(0, "read again, there was nothing left to change");
        (await FollowedAsync(first)).Should().HaveCount(1, "the role followed its pack once");
        (await OlisKeysAsync(first)).Should().Contain(HostCatalogue.WidgetCreate);
    }

    /// <summary>
    /// A seeded database whose Operator role in Harbor an older version of the application made, then forced
    /// (<see cref="TenancyPostgres.ForceAsync"/>): every policy holds the tables' owner, and the tables belong to a
    /// role that bypasses them, as the role that runs an application's migrations does.
    /// </summary>
    private async Task<TestDatabase> BehindAndForcedAsync()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation);
        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ExecuteAsync(
                    $"""
                    UPDATE tenancy."Roles"
                    SET "Keys" = pg_catalog.array_remove("Keys", 'widget.create'), "KeysFromPack" = pg_catalog.array_remove("KeysFromPack", 'widget.create')
                    WHERE "Id" = '{HarborRoles.Operator.Value}'
                    """,
                    Cancellation))
                .Should().Be(1);
            await owner.CommitAsync(Cancellation);
        }

        await TenancyPostgres.ForceAsync(database, Cancellation);
        return database;
    }

    /// <summary>The keys Oli holds by Harbor's Operator role at North Pier, as the database wrote them, read by system work in Harbor.</summary>
    private static Task<List<string>> OlisKeysAsync(TenancyServices services)
        => services.BySystemIn(Harbor, scoped => scoped.Tenancy().Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>()
            .Where(right => right.SeatId == Oli.Seat && right.RoleId == HarborRoles.Operator && right.UnitId == NorthPier)
            .Select(right => right.Key)
            .ToListAsync(Cancellation));

    /// <summary>Harbor's history rows of a role that followed its pack: who acted, and what it says.</summary>
    private static async Task<List<(string ActedByKind, string Payload)>> FollowedAsync(TenancyServices services)
    {
        var rows = await services.BySystemIn(Harbor, scoped => scoped.Tenancy().Set<EventLogEntry>()
            .Where(entry => entry.EventName == RoleFollowedItsPack)
            .Select(entry => new { entry.ActedByKind, entry.Payload })
            .ToListAsync(Cancellation));
        return [.. rows.Select(row => (row.ActedByKind, row.Payload))];
    }

    /// <summary>Runs a race once, at the first save of a context of the services it is added to, before anything is written.</summary>
    private sealed class FirstSave(Func<Task> race) : SaveChangesInterceptor
    {
        private int _fired;

        public bool Fired => Volatile.Read(ref _fired) == 1;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await race();
            }

            return result;
        }
    }
}
