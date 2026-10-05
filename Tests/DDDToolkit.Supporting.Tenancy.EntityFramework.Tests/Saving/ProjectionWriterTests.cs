using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The projection writer: the closure and the rights are written by the save that changes the aggregates, in its
/// transaction, from what the change tracker says changed. A role's holders are rewritten without being loaded,
/// and a save that fails leaves the rows as they were.
/// </summary>
public abstract class ProjectionWriterTests(TestDatabases databases) : IAsyncLifetime
{
    private readonly FixedClock _clock = new();
    private TestServices _services = null!;

    public async ValueTask InitializeAsync() => _services = await databases.ServicesAsync(services => services.AddSingleton<TimeProvider>(_clock));

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Adding_and_moving_units_rewrite_the_closure_in_the_same_save()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var root = harbor.RootUnit;
        (await PathsAsync(harbor.Tenant)).Should().BeEquivalentTo([(root, root, 0)], "provisioning writes the root's own pair");

        var north = await _services.AddUnitAsync(harbor.Tenant, root, "North");
        var south = await _services.AddUnitAsync(harbor.Tenant, root, "South");
        var coast = await _services.AddUnitAsync(harbor.Tenant, north, "Coast");

        (await PathsAsync(harbor.Tenant)).Should().BeEquivalentTo(
        [
            (root, root, 0), (north, north, 0), (south, south, 0), (coast, coast, 0),
            (root, north, 1), (root, south, 1), (north, coast, 1), (root, coast, 2),
        ]);

        _services.Commands.Reset();
        await _services.BySystemIn(harbor.Tenant, services => services.Organization().MoveUnitAsync(coast, south, TestContext.Current.CancellationToken));

        var moved = await PathsAsync(harbor.Tenant);
        moved.Should().Contain([(south, coast, 1), (root, coast, 2)]).And.NotContain((north, coast, 1));
        moved.Should().BeEquivalentTo(await ClosureOfSavedOrganizationAsync(harbor.Tenant), "the table is the closure of the tree as saved");

        // The pairs go into the same transaction as the moved unit.
        var writes = _services.Commands.Sent.Where(command => command.Writes).ToList();
        writes.Should().Contain(command => command.Text.Contains("\"OrganizationUnitPaths\""));
        writes.Should().Contain(command => command.Text.Contains("\"OrganizationUnits\""));
        writes.Select(command => command.Transaction).Distinct().Should().ContainSingle().Which.Should().NotBeNull();
    }

    [Fact]
    public async Task Granting_writes_rights_rows_in_the_same_transaction()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", north);
        (await _services.StoredRightsAsync(grace)).Should().BeEmpty("a placement alone grants nothing");

        _services.Commands.Reset();
        await _services.GrantAsync(harbor, grace, north, HostCatalogue.OperatorPack);

        var operatorRole = harbor.RolesByPack[HostCatalogue.OperatorPack];
        (await _services.StoredRightsAsync(grace)).Select(right => (right.TenantId, right.UnitId, right.RoleId, right.Key, right.StartsAt, right.EndsAt))
            .Should().BeEquivalentTo(
            [
                (harbor.Tenant, north, operatorRole, HostCatalogue.WidgetChange, _clock.Now, (DateTimeOffset?)null),
                (harbor.Tenant, north, operatorRole, HostCatalogue.WidgetCreate, _clock.Now, (DateTimeOffset?)null),
                (harbor.Tenant, north, operatorRole, HostCatalogue.WidgetRead, _clock.Now, (DateTimeOffset?)null),
            ], "one row per key of the role, the implied one included, with the grant's period");

        var writes = _services.Commands.Sent.Where(command => command.Writes).ToList();
        writes.Should().Contain(command => command.Text.Contains("\"SeatRights\""));
        writes.Should().Contain(command => command.Text.Contains("\"SeatRoleGrants\""));
        writes.Select(command => command.Transaction).Distinct().Should().ContainSingle("the grant and its rights are one save").Which.Should().NotBeNull();
    }

    [Fact]
    public async Task A_failed_save_leaves_no_rights()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", north);
        var before = await _services.StoredRightsAsync();

        // An invariant fails in the same save as a grant: nothing is written, the rights included.
        await _services.BySystemIn(harbor.Tenant, async services =>
        {
            var context = services.Tenancy();
            var seat = await context.Set<HostSeat>().SingleAsync(row => row.Id == grace, TestContext.Current.CancellationToken);
            var role = await context.Set<HostRole>().SingleAsync(row => row.Id == harbor.RolesByPack[HostCatalogue.OperatorPack], TestContext.Current.CancellationToken);

            seat.Grant(north, role.Id, role.Facts, GrantPeriod.Open(_clock.Now), grantedBy: null, reason: null);
            seat.ChangeJobTitle(new string('x', HostSeat.MaxJobTitleLength + 1));

            await FluentActions.Awaiting(() => context.SaveChangesAsync(TestContext.Current.CancellationToken)).Should().ThrowAsync<InvariantViolationException>();
        });

        (await _services.StoredRightsAsync()).Should().BeEquivalentTo(before);

        // The save fails after the rights were worked out: another save changed the seat in the meantime.
        await _services.BySystemIn(harbor.Tenant, async services =>
        {
            var context = services.Tenancy();
            var seat = await context.Set<HostSeat>().SingleAsync(row => row.Id == grace, TestContext.Current.CancellationToken);
            var role = await context.Set<HostRole>().SingleAsync(row => row.Id == harbor.RolesByPack[HostCatalogue.OperatorPack], TestContext.Current.CancellationToken);
            seat.Grant(north, role.Id, role.Facts, GrantPeriod.Open(_clock.Now), grantedBy: null, reason: null);

            await _services.BySystemIn(harbor.Tenant, other => other.Seats().RenameAsync(grace, "Grace Hopper", TestContext.Current.CancellationToken));

            await FluentActions.Awaiting(() => context.SaveChangesAsync(TestContext.Current.CancellationToken)).Should().ThrowAsync<ConcurrencyConflictException>();
        });

        (await _services.StoredRightsAsync()).Should().BeEquivalentTo(before, "the rights of a save that failed are rolled back with it");
    }

    [Fact]
    public async Task Revoking_removes_the_rows()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", north, HostCatalogue.OperatorPack, HostCatalogue.WatcherPack);
        (await KeysAsync(grace)).Should().HaveCount(4);

        await _services.BySystemIn(harbor.Tenant, services =>
            services.Seats().RevokeAsync(grace, north, harbor.RolesByPack[HostCatalogue.OperatorPack], TestContext.Current.CancellationToken));

        (await KeysAsync(grace)).Should().Equal((harbor.RolesByPack[HostCatalogue.WatcherPack], HostCatalogue.WidgetRead));
    }

    [Fact]
    public async Task Suspending_removes_every_row_and_reactivating_restores_them()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", north, HostCatalogue.OperatorPack);
        var granted = await _services.StoredRightsAsync(grace);
        granted.Should().HaveCount(3);

        _clock.Advance(TimeSpan.FromDays(1));
        await _services.BySystemIn(harbor.Tenant, services => services.Seats().SuspendAsync(grace, TestContext.Current.CancellationToken));
        (await _services.StoredRightsAsync(grace)).Should().BeEmpty("a seat that is not active holds nothing");

        await _services.BySystemIn(harbor.Tenant, services => services.Seats().ReactivateAsync(grace, TestContext.Current.CancellationToken));
        (await _services.StoredRightsAsync(grace)).Should().BeEquivalentTo(granted, "the grants never went away, with their periods");
    }

    [Fact]
    public async Task Changing_a_roles_keys_rewrites_every_holder_without_loading_them()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var south = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "South");
        var grace = await _services.SeatAtAsync(harbor, "Grace", north, HostCatalogue.OperatorPack);
        var lin = await _services.SeatAtAsync(harbor, "Lin", south, HostCatalogue.OperatorPack, HostCatalogue.WatcherPack);
        var hal = await _services.SeatAtAsync(harbor, "Hal", south, HostCatalogue.WatcherPack);
        var operatorRole = harbor.RolesByPack[HostCatalogue.OperatorPack];
        var watcherRole = harbor.RolesByPack[HostCatalogue.WatcherPack];
        var untouched = await _services.StoredRightsAsync(hal);

        _services.Commands.Reset();
        await _services.BySystemIn(harbor.Tenant, services => services.Roles().SetKeysAsync(operatorRole, [HostCatalogue.WidgetChange], TestContext.Current.CancellationToken));

        (await KeysAsync(grace)).Should().Equal((operatorRole, HostCatalogue.WidgetChange), (operatorRole, HostCatalogue.WidgetRead));
        (await KeysAsync(lin)).Should().BeEquivalentTo(
            [(operatorRole, HostCatalogue.WidgetChange), (operatorRole, HostCatalogue.WidgetRead), (watcherRole, HostCatalogue.WidgetRead)]);
        (await _services.StoredRightsAsync(hal)).Should().BeEquivalentTo(untouched, "a seat that does not hold the role is not touched");

        // The holders' grants are read as rows; no seat is loaded, which would read its name and identity.
        var sent = _services.Commands.Commands;
        sent.Should().NotContain(command => command.Contains("\"DisplayName\"") || command.Contains("\"Identity\"") || command.Contains("\"JobTitle\""));
        sent.Should().ContainSingle(command => command.StartsWith("SELECT") && command.Contains("\"SeatRoleGrants\""), "one query finds the holders");
    }

    [Fact]
    public async Task Archiving_a_role_removes_its_rows()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", north, HostCatalogue.OperatorPack, HostCatalogue.WatcherPack);
        var lin = await _services.SeatAtAsync(harbor, "Lin", north, HostCatalogue.OperatorPack);

        await _services.BySystemIn(harbor.Tenant, services => services.Roles().ArchiveAsync(harbor.RolesByPack[HostCatalogue.OperatorPack], TestContext.Current.CancellationToken));

        (await KeysAsync(grace)).Should().Equal((harbor.RolesByPack[HostCatalogue.WatcherPack], HostCatalogue.WidgetRead));
        (await KeysAsync(lin)).Should().BeEmpty("an archived role grants nothing, though it stays granted");

        await _services.BySystemIn(harbor.Tenant, async services =>
            (await services.Tenancy().Set<HostSeat>().SingleAsync(seat => seat.Id == lin, TestContext.Current.CancellationToken)).Placements.Single().Grants.Should().ContainSingle());
    }

    [Fact]
    public async Task The_synchronous_save_writes_the_same_rows()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", north);
        OrganizationUnitId coast = default;

        await _services.BySystemIn(harbor.Tenant, async services =>
        {
            var context = services.Tenancy();
            var seat = await context.Set<HostSeat>().SingleAsync(row => row.Id == grace, TestContext.Current.CancellationToken);
            var role = await context.Set<HostRole>().SingleAsync(row => row.Id == harbor.RolesByPack[HostCatalogue.OperatorPack], TestContext.Current.CancellationToken);
            var organization = await context.Set<HostOrganization>().SingleAsync(TestContext.Current.CancellationToken);

            seat.Grant(north, role.Id, role.Facts, GrantPeriod.Open(_clock.Now), grantedBy: null, reason: null);
            coast = organization.AddUnit<SeatId>(OrganizationUnitId.CreateSequential(), north, "Coast", TenantShape.Hierarchical).Id;

            context.SaveChanges();
        });

        (await KeysAsync(grace)).Select(pair => pair.Key).Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
        (await PathsAsync(harbor.Tenant)).Should().Contain([(north, coast, 1), (harbor.RootUnit, coast, 2)]);
    }

    [Fact]
    public async Task A_changed_grant_is_traced_to_its_seat_without_the_version_interceptor()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", north);

        // A context with Tenancy's interceptor only: nothing marks the seat changed when only its grant is.
        await using (var scope = _services.Scope())
        {
            var options = _services.Database.Options<TestTenancyContext>();
            options.UseTenancy(scope.ServiceProvider);
            await using var context = new TestTenancyContext(options.Options);

            using (TenancyWork.BeginSystemIn<TenantId, SeatId>(harbor.Tenant))
            {
                var seat = await context.Set<HostSeat>().SingleAsync(row => row.Id == grace, TestContext.Current.CancellationToken);
                var role = await context.Set<HostRole>().SingleAsync(row => row.Id == harbor.RolesByPack[HostCatalogue.WatcherPack], TestContext.Current.CancellationToken);
                seat.Grant(north, role.Id, role.Facts, GrantPeriod.Open(_clock.Now), grantedBy: null, reason: null);

                context.ChangeTracker.DetectChanges();
                context.Entry(seat).State.Should().Be(EntityState.Unchanged);
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        (await KeysAsync(grace)).Should().Equal((harbor.RolesByPack[HostCatalogue.WatcherPack], HostCatalogue.WidgetRead));
    }

    [Fact]
    public async Task Saves_in_one_unit_of_work_build_on_the_rows_the_last_one_wrote()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", north);
        var operatorRole = harbor.RolesByPack[HostCatalogue.OperatorPack];

        // One scope, one context: the rows the first save wrote are tracked when the next ones run.
        await _services.BySystemIn(harbor.Tenant, async services =>
        {
            await services.Seats().GrantAsync(grace, north, operatorRole, until: _clock.Now.AddDays(1), reason: null, TestContext.Current.CancellationToken);
            await services.Seats().RevokeAsync(grace, north, operatorRole, TestContext.Current.CancellationToken);
            await services.Seats().GrantAsync(grace, north, operatorRole, until: null, reason: null, TestContext.Current.CancellationToken);
            await services.Seats().SuspendAsync(grace, TestContext.Current.CancellationToken);
            await services.Seats().ReactivateAsync(grace, TestContext.Current.CancellationToken);
        });

        (await _services.StoredRightsAsync(grace)).Should().HaveCount(3).And.OnlyContain(right => right.EndsAt == null && right.RoleId == operatorRole);
    }

    [Fact]
    public async Task A_filter_of_the_application_on_seats_or_roles_hides_nothing_from_the_writer()
    {
        var harbor = await _services.ProvisionAsync("harbor");
        var north = await _services.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var grace = await _services.SeatAtAsync(harbor, "Grace", north, HostCatalogue.OperatorPack);
        var lin = await _services.SeatAtAsync(harbor, "Lin", north, HostCatalogue.OperatorPack);
        var operatorRole = harbor.RolesByPack[HostCatalogue.OperatorPack];
        var watcherRole = harbor.RolesByPack[HostCatalogue.WatcherPack];

        // Lin's seat is hidden from the application's reads, and the role both hold loses a key.
        await _services.BySystemIn(harbor.Tenant, async services =>
        {
            var context = services.Tenancy();
            (await context.Set<HostSeat>().SingleAsync(seat => seat.Id == lin, TestContext.Current.CancellationToken)).ChangeJobTitle(HostFilteredTenancyContext.Hidden);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        await FilteredAsync(harbor.Tenant, async (context, catalogue) =>
        {
            (await context.Set<HostRole>().SingleAsync(role => role.Id == operatorRole, TestContext.Current.CancellationToken)).SetKeys<SeatId>([HostCatalogue.WidgetChange], catalogue);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        (await KeysAsync(grace)).Should().Equal((operatorRole, HostCatalogue.WidgetChange), (operatorRole, HostCatalogue.WidgetRead));
        (await KeysAsync(lin)).Should().Equal([(operatorRole, HostCatalogue.WidgetChange), (operatorRole, HostCatalogue.WidgetRead)], "a seat the application hides still holds the role");

        // The role is hidden next, and Grace is granted another one, which writes her rights again in full.
        await _services.BySystemIn(harbor.Tenant, async services =>
        {
            var role = await services.Tenancy().Set<HostRole>().SingleAsync(row => row.Id == operatorRole, TestContext.Current.CancellationToken);
            role.Rename<SeatId>(role.Name, HostFilteredTenancyContext.Hidden);
            await services.Tenancy().SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        await FilteredAsync(harbor.Tenant, async (context, _) =>
        {
            var seat = await context.Set<HostSeat>().SingleAsync(row => row.Id == grace, TestContext.Current.CancellationToken);
            var watcher = await context.Set<HostRole>().SingleAsync(row => row.Id == watcherRole, TestContext.Current.CancellationToken);
            seat.Grant(north, watcher.Id, watcher.Facts, GrantPeriod.Open(_clock.Now), grantedBy: null, reason: null);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        (await KeysAsync(grace)).Should().BeEquivalentTo(
            [(operatorRole, HostCatalogue.WidgetChange), (operatorRole, HostCatalogue.WidgetRead), (watcherRole, HostCatalogue.WidgetRead)],
            "a role the application hides still gives its keys");
    }

    /// <summary>
    /// Runs <paramref name="act"/> as system work in <paramref name="tenant"/>, over a context with the application's
    /// own filters on seats and roles and Tenancy's interceptor.
    /// </summary>
    private async Task FilteredAsync(TenantId tenant, Func<HostFilteredTenancyContext, TenancyCatalogue, Task> act)
    {
        await using var scope = _services.Scope();
        var options = _services.Database.Options<HostFilteredTenancyContext>();
        options.UseTenancy(scope.ServiceProvider);
        await using var context = new HostFilteredTenancyContext(options.Options);

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant))
        {
            await act(context, scope.ServiceProvider.GetRequiredService<TenancyCatalogue>());
        }
    }

    [Fact]
    public async Task The_save_writes_no_rights_when_the_database_keeps_them()
    {
        // The option a package for one database turns on, here on a database with nothing to write the rights in the
        // save's place: what the save leaves alone shows as rights that are not there.
        using var keeping = await databases.ServicesAsync(services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.Configure<TenancyStoreOptions>(options => options.DatabaseKeepsRights = true);
        });

        var harbor = await keeping.ProvisionAsync("harbor");
        var north = await keeping.AddUnitAsync(harbor.Tenant, harbor.RootUnit, "North");
        var oli = await keeping.SeatAtAsync(harbor, "Oli", north, HostCatalogue.OperatorPack);
        await keeping.BySystemIn(harbor.Tenant, services => services.Roles().SetKeysAsync(harbor.RolesByPack[HostCatalogue.OperatorPack], [HostCatalogue.WidgetRead], TestContext.Current.CancellationToken));
        await keeping.BySystemIn(harbor.Tenant, services => services.Seats().SuspendAsync(oli, TestContext.Current.CancellationToken));

        (await keeping.StoredRightsAsync()).Should().BeEmpty("the administrator's, the operator's and the role's holders' are all the database's to write");
        keeping.Database.CountRows("SeatRights").Should().Be(0);

        // Everything else the save writes with the aggregates, it still writes: the closure, a placement's tenant and a
        // role's normalized name.
        (await keeping.StoredPathsAsync(harbor.Tenant)).Select(path => (path.AncestorId, path.DescendantId, path.Distance))
            .Should().BeEquivalentTo([(harbor.RootUnit, harbor.RootUnit, 0), (north, north, 0), (harbor.RootUnit, north, 1)]);
        await keeping.BySystemIn(harbor.Tenant, async services =>
        {
            (await services.Tenancy().Set<PlacementRow<SeatId, OrganizationUnitId>>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(2, "each placement has its tenant, which its row is filtered on");
            (await services.Tenancy().Set<HostRole>().Select(role => EF.Property<string>(role, "NormalizedName")).ToListAsync(TestContext.Current.CancellationToken))
                .Should().OnlyContain(name => name == name.ToUpperInvariant() && name.Length > 0);
        });

        // Without the option, the same commands leave the rights written, as on every database that does not keep them.
        var same = await _services.ProvisionAsync("harbor");
        (await _services.StoredRightsAsync(same.AdminSeat)).Should().HaveCount(TenancyCatalogue.Build(HostCatalogue.Application, []).LiveKeys.Count);
    }

    private async Task<List<(OrganizationUnitId Ancestor, OrganizationUnitId Descendant, int Distance)>> PathsAsync(TenantId tenant)
        => [.. (await _services.StoredPathsAsync(tenant)).Select(path => (path.AncestorId, path.DescendantId, path.Distance))];

    private async Task<List<(RoleId Role, string Key)>> KeysAsync(SeatId seat)
        => [.. (await _services.StoredRightsAsync(seat)).Select(right => (right.RoleId, right.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal)];

    private Task<List<(OrganizationUnitId Ancestor, OrganizationUnitId Descendant, int Distance)>> ClosureOfSavedOrganizationAsync(TenantId tenant)
        => _services.BySystemIn(tenant, async services =>
        {
            var organization = await services.Tenancy().Set<HostOrganization>().SingleAsync(TestContext.Current.CancellationToken);
            return TenancyProjection.ClosureOf(organization).Select(path => (path.AncestorId, path.DescendantId, path.Distance)).ToList();
        });
}

/// <summary>The projection writer, on SQLite in memory.</summary>
public sealed class ProjectionWriterTestsOnSqlite() : ProjectionWriterTests(TestDatabases.Sqlite);

/// <summary>The projection writer, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql, not the policies.</summary>
public sealed class ProjectionWriterTestsOnPostgres(PostgresDatabases postgres) : ProjectionWriterTests(postgres);
