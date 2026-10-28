namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The access questions over read rows in memory: a permission granted at a unit covers that unit's subtree and
/// nothing beside or above it, a permission held at the root holds for the whole tenant, a grant counts only inside
/// its period without anything being written, and system work in a tenant holds everything there and nothing
/// elsewhere.
/// </summary>
public class AccessQuestionTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    private readonly Harbor _harbor;
    private readonly InMemoryTenancyStore _store;
    private readonly FixedClock _clock = new();

    public AccessQuestionTests()
    {
        _harbor = new HarborBuilder().Build();
        _store = new InMemoryTenancyStore(_harbor.Catalogue);
        _store.Seed(_harbor.Aggregates);
    }

    /// <summary>A new seat of Harbor, placed at <paramref name="unit"/> and granted the role of <paramref name="starterRole"/> there, saved.</summary>
    private HostSeat SeatAt(OrganizationUnitId unit, string? starterRole, DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        var seat = _harbor.NewSeat();
        seat.Place(unit, primary: true, Now, placedBy: null);
        if (starterRole is not null)
        {
            var role = _harbor.RolesByStarterRole[starterRole];
            seat.Grant(unit, role.Id, role.Facts, GrantPeriod.Between(from ?? Now, until), null, null);
        }

        _store.Seed(seat);
        return seat;
    }

    /// <summary>Asks as <paramref name="caller"/>, over <paramref name="source"/> or the filtered store, and runs the answer while the caller is current.</summary>
    private T Ask<T>(HostUser caller, Func<ITenancyQueries<TenantId, SeatId, OrganizationUnitId, RoleId>, T> ask,
        ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>? source = null, TenancyCatalogue? catalogue = null)
    {
        using (TenancyUsers.Begin(caller))
        {
            var tenancy = new TenancyAccessManager(catalogue ?? _harbor.Catalogue, _clock, NoServices.Instance);
            return ask(tenancy.In(source ?? _store, _store));
        }
    }

    private OrganizationUnitId[] UnitsWhere(HostUser caller, string permission, ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>? source = null)
        => Ask(caller, queries => queries.UnitsWhereIHold(permission).ToArray(), source);

    private bool HoldsAt(HostUser caller, string permission, OrganizationUnitId unit, ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>? source = null)
        => Ask(caller, queries => queries.HoldsAtAsync(permission, unit, default).GetAwaiter().GetResult(), source);

    private bool HoldsAtTenant(HostUser caller, string permission)
        => Ask(caller, queries => queries.HoldsAtTenantAsync(permission, default).GetAwaiter().GetResult());

    private HostUser As(HostSeat seat) => HostUser.InSeat(seat.TenantId, seat.Id);

    [Fact]
    public void A_permission_held_at_a_unit_reaches_its_whole_subtree()
    {
        var supervisor = As(SeatAt(_harbor.North, HostCatalogue.SupervisorStarterRole));

        UnitsWhere(supervisor, TenancyPermissions.UnitsManage).Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast]);
        UnitsWhere(supervisor, HostCatalogue.WidgetRead).Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast], "an implied permission reaches as far");
        UnitsWhere(As(_harbor.Administrator), TenancyPermissions.UnitsManage)
            .Should().BeEquivalentTo([_harbor.Root, _harbor.North, _harbor.NorthCoast, _harbor.South]);
    }

    [Fact]
    public void It_does_not_reach_siblings_or_ancestors()
    {
        var supervisor = As(SeatAt(_harbor.North, HostCatalogue.SupervisorStarterRole));

        UnitsWhere(supervisor, TenancyPermissions.UnitsManage).Should().NotContain([_harbor.Root, _harbor.South]);
        HoldsAt(supervisor, TenancyPermissions.UnitsManage, _harbor.South).Should().BeFalse();
        HoldsAt(supervisor, TenancyPermissions.UnitsManage, _harbor.Root).Should().BeFalse();
        UnitsWhere(supervisor, TenancyPermissions.RolesManage).Should().BeEmpty("the supervisor's role does not grant it");
    }

    [Fact]
    public void HoldsAtTenantAsync_only_for_a_permission_held_at_the_root()
    {
        var supervisor = As(SeatAt(_harbor.North, HostCatalogue.SupervisorStarterRole));
        var administrator = As(_harbor.Administrator);

        HoldsAtTenant(supervisor, TenancyPermissions.UnitsManage).Should().BeFalse("a permission held below the root is not tenant-wide, however far it reaches");
        HoldsAtTenant(administrator, TenancyPermissions.UnitsManage).Should().BeTrue();
        HoldsAtTenant(administrator, TenancyPermissions.RolesManage).Should().BeTrue();
        HoldsAtTenant(As(SeatAt(_harbor.Root, HostCatalogue.WatcherStarterRole)), HostCatalogue.WidgetRead).Should().BeTrue();
        HoldsAtTenant(As(SeatAt(_harbor.Root, HostCatalogue.WatcherStarterRole)), HostCatalogue.WidgetChange).Should().BeFalse();
    }

    [Fact]
    public void HoldsAtAsync_answers_for_one_unit()
    {
        var supervisor = As(SeatAt(_harbor.North, HostCatalogue.SupervisorStarterRole));

        HoldsAt(supervisor, TenancyPermissions.UnitsManage, _harbor.North).Should().BeTrue();
        HoldsAt(supervisor, TenancyPermissions.UnitsManage, _harbor.NorthCoast).Should().BeTrue();
        HoldsAt(supervisor, HostCatalogue.WidgetCreate, _harbor.NorthCoast).Should().BeTrue();
        HoldsAt(supervisor, TenancyPermissions.RolesManage, _harbor.North).Should().BeFalse();
        HoldsAt(supervisor, TenancyPermissions.UnitsManage, OrganizationUnitId.CreateSequential()).Should().BeFalse("a unit of no tenant");
    }

    [Fact]
    public void An_expired_grant_answers_nothing_without_any_write()
    {
        var worker = As(SeatAt(_harbor.North, HostCatalogue.OperatorStarterRole, until: Now.AddHours(1)));
        var rows = _store.SavedRights.Count;

        UnitsWhere(worker, HostCatalogue.WidgetChange).Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast]);

        _clock.Advance(TimeSpan.FromHours(1));

        UnitsWhere(worker, HostCatalogue.WidgetChange).Should().BeEmpty("a grant no longer applies at the moment it ends");
        HoldsAt(worker, HostCatalogue.WidgetChange, _harbor.North).Should().BeFalse();
        Ask(worker, queries => queries.PermissionsIHoldAt(_harbor.North).ToArray()).Should().BeEmpty();
        _store.SaveCount.Should().Be(0, "nothing was saved to make the grant expire");
        _store.SavedRights.Should().HaveCount(rows, "the rows stay; they are read against the clock");
    }

    [Fact]
    public void A_future_grant_answers_nothing_yet()
    {
        var worker = As(SeatAt(_harbor.North, HostCatalogue.OperatorStarterRole, from: Now.AddDays(1)));

        UnitsWhere(worker, HostCatalogue.WidgetChange).Should().BeEmpty();
        HoldsAt(worker, HostCatalogue.WidgetChange, _harbor.North).Should().BeFalse();

        _clock.Advance(TimeSpan.FromDays(1));

        UnitsWhere(worker, HostCatalogue.WidgetChange).Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast]);
    }

    [Fact]
    public void Rights_at_an_archived_unit_keep_working()
    {
        var worker = As(SeatAt(_harbor.NorthCoast, HostCatalogue.OperatorStarterRole));

        _harbor.Organization.ArchiveUnit<SeatId>(_harbor.NorthCoast);
        _store.Seed(_harbor.Organization);

        _store.Organization(_harbor.Tenant.Id).IsActiveUnit(_harbor.NorthCoast).Should().BeFalse();
        HoldsAt(worker, HostCatalogue.WidgetChange, _harbor.NorthCoast).Should().BeTrue("a unit is archived, not taken away from the people who work there");
        UnitsWhere(worker, HostCatalogue.WidgetChange).Should().Equal(_harbor.NorthCoast);
        Ask(worker, queries => queries.ReadableUnits().ToArray()).Should().Equal(_harbor.NorthCoast);
    }

    [Fact]
    public void RolesWithKey_lists_active_roles_only()
    {
        var administrator = As(_harbor.Administrator);
        var roles = _harbor.RolesByStarterRole;

        Ask(administrator, queries => queries.RolesWithPermission(TenancyPermissions.UnitsManage).ToArray())
            .Should().BeEquivalentTo([roles[HostCatalogue.AdministratorStarterRole].Id, roles[HostCatalogue.SupervisorStarterRole].Id]);
        Ask(administrator, queries => queries.RolesWithPermission(HostCatalogue.WidgetRead).ToArray())
            .Should().HaveCount(4, "every starter role of the host grants seeing widgets");

        roles[HostCatalogue.WatcherStarterRole].Archive<SeatId>();
        _store.Seed(roles[HostCatalogue.WatcherStarterRole]);

        Ask(administrator, queries => queries.RolesWithPermission(HostCatalogue.WidgetRead).ToArray())
            .Should().NotContain(roles[HostCatalogue.WatcherStarterRole].Id).And.HaveCount(3);
    }

    [Fact]
    public void ReadableUnits_are_the_subtrees_of_my_placements()
    {
        var seat = SeatAt(_harbor.North, starterRole: null);
        seat.Place(_harbor.South, primary: false, Now, placedBy: null);
        _store.Seed(seat);

        Ask(As(seat), queries => queries.ReadableUnits().ToArray())
            .Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast, _harbor.South], "a placement is where a seat belongs, with or without a role");
        UnitsWhere(As(seat), HostCatalogue.WidgetRead).Should().BeEmpty("belonging somewhere grants no permission");
        Ask(As(_harbor.Administrator), queries => queries.ReadableUnits().ToArray()).Should().HaveCount(4);
    }

    [Fact]
    public void PermissionsIHoldAt_lists_the_permissions_reaching_that_unit()
    {
        var seat = SeatAt(_harbor.North, HostCatalogue.SupervisorStarterRole);
        var watcher = _harbor.RolesByStarterRole[HostCatalogue.WatcherStarterRole];
        seat.Place(_harbor.South, primary: false, Now, placedBy: null);
        seat.Grant(_harbor.South, watcher.Id, watcher.Facts, GrantPeriod.Open(Now), null, null);
        _store.Seed(seat);

        Ask(As(seat), queries => queries.PermissionsIHoldAt(_harbor.NorthCoast).ToArray()).Should().BeEquivalentTo(
        [
            TenancyPermissions.GrantsManage, TenancyPermissions.SeatsManage, TenancyPermissions.UnitsManage,
            HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead,
        ]);
        Ask(As(seat), queries => queries.PermissionsIHoldAt(_harbor.South).ToArray()).Should().Equal(HostCatalogue.WidgetRead);
        Ask(As(seat), queries => queries.PermissionsIHoldAt(_harbor.Root).ToArray()).Should().BeEmpty();
    }

    [Fact]
    public void Nobody_holds_nothing()
    {
        var nobody = HostUser.Nobody(TenancyRefusals.NotSeated);

        UnitsWhere(nobody, HostCatalogue.WidgetRead).Should().BeEmpty();
        HoldsAt(nobody, HostCatalogue.WidgetRead, _harbor.Root).Should().BeFalse();
        HoldsAtTenant(nobody, TenancyPermissions.RolesManage).Should().BeFalse();
        Ask(nobody, queries => queries.ReadableUnits().ToArray()).Should().BeEmpty();
        Ask(nobody, queries => queries.RolesWithPermission(HostCatalogue.WidgetRead).ToArray()).Should().BeEmpty();
        Ask(nobody, queries => queries.PermissionsIHoldAt(_harbor.Root).ToArray()).Should().BeEmpty();
        Ask(nobody, queries => queries.Units().ToArray()).Should().BeEmpty();
        Ask(nobody, queries => queries.Roles().ToArray()).Should().BeEmpty();
        Ask(nobody, queries => queries.Seats().ToArray()).Should().BeEmpty();

        using (TenancyUsers.Begin(nobody))
        {
            var access = new TenancyAccessManager(_harbor.Catalogue, _clock, NoServices.Instance);
            Refused.With(TenancyRefusals.NotSeated, () => access.CurrentTenant.Require());
        }
    }

    [Fact]
    public void SystemInTenant_holds_everything_in_its_own_tenant_only()
    {
        var other = new HarborBuilder().Tenant(2, "quarry").Build();
        _store.Seed(other.Aggregates);
        var system = HostUser.SystemIn(_harbor.Tenant.Id);
        var harborUnits = new[] { _harbor.Root, _harbor.North, _harbor.NorthCoast, _harbor.South };

        foreach (var source in new[] { _store.Filtered, _store.Unfiltered })
        {
            UnitsWhere(system, HostCatalogue.WidgetCreate, source).Should().BeEquivalentTo(harborUnits);
            HoldsAt(system, TenancyPermissions.RolesManage, _harbor.NorthCoast, source).Should().BeTrue();
            HoldsAt(system, TenancyPermissions.RolesManage, other.NorthCoast, source).Should().BeFalse("system work in one tenant holds nothing in another");
            Ask(system, queries => queries.Units().Select(unit => unit.Id).ToArray(), source).Should().BeEquivalentTo(harborUnits);
            Ask(system, queries => queries.Roles().ToArray(), source).Should().OnlyContain(role => role.TenantId == _harbor.Tenant.Id);
            Ask(system, queries => queries.Seats().ToArray(), source).Should().OnlyContain(seat => seat.TenantId == _harbor.Tenant.Id);
        }

        HoldsAtTenant(system, TenancyPermissions.RolesManage).Should().BeTrue();
        Ask(system, queries => queries.PermissionsIHoldAt(_harbor.North).ToArray()).Should().BeEquivalentTo(_harbor.Catalogue.LivePermissions);

        // A seat's own questions keep to its tenant too, even over rows that are not filtered.
        UnitsWhere(As(_harbor.Administrator), TenancyPermissions.UnitsManage, _store.Unfiltered).Should().BeEquivalentTo(harborUnits);

        using (TenancyUsers.Begin(system))
        {
            new TenancyAccessManager(_harbor.Catalogue, _clock, NoServices.Instance).CurrentTenant.Require()
                .Should().Be(new TenantInScope<TenantId, SeatId>(_harbor.Tenant.Id, null, BySystem: true));
        }
    }

    [Fact]
    public void Global_system_asking_is_a_programming_error()
    {
        var system = HostUser.System;

        var questions = new Action[]
        {
            () => UnitsWhere(system, HostCatalogue.WidgetRead),
            () => HoldsAt(system, HostCatalogue.WidgetRead, _harbor.Root),
            () => HoldsAtTenant(system, HostCatalogue.WidgetRead),
            () => Ask(system, ask => ask.ReadableUnits()),
            () => Ask(system, ask => ask.RolesWithPermission(HostCatalogue.WidgetRead)),
            () => Ask(system, ask => ask.PermissionsIHoldAt(_harbor.Root)),
            () => Ask(system, ask => ask.Units()),
            () => Ask(system, ask => ask.Roles()),
            () => Ask(system, ask => ask.Seats()),
        };

        foreach (var question in questions)
        {
            question.Should().Throw<InvalidOperationException>().WithMessage("*Begin access.BeginSystemIn(tenant) for work inside one, on the TenancyAccessManager your code takes.");
        }

        using (TenancyUsers.Begin(system))
        {
            FluentActions.Invoking(() => new TenancyAccessManager(_harbor.Catalogue, _clock, NoServices.Instance).CurrentTenant.Require())
                .Should().Throw<InvalidOperationException>()
                .WithMessage("System work outside any tenant has no tenant. Begin access.BeginSystemIn(tenant) for work inside one, "
                    + "on the TenancyAccessManager your code takes.");
        }
    }

    [Fact]
    public void An_unknown_permission_is_a_programming_error()
    {
        var administrator = As(_harbor.Administrator);

        FluentActions.Invoking(() => UnitsWhere(administrator, "widget.fly")).Should().Throw<ArgumentException>().WithMessage("*widget.fly*");
        FluentActions.Invoking(() => HoldsAtTenant(administrator, "widget.fly")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => HoldsAt(administrator, "widget.fly", _harbor.Root)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Ask(administrator, queries => queries.RolesWithPermission("widget.fly"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => UnitsWhere(HostUser.Nobody(TenancyRefusals.NotSeated), "widget.fly"))
            .Should().Throw<ArgumentException>("a typo is a typo, whoever runs it");
    }

    [Fact]
    public void A_retired_permission_is_askable_and_holds_nowhere()
    {
        var gauge = new Permission("gauges.read", "Gauges", "Read gauges");
        var harbor = new HarborBuilder().With(New.Catalogue(gauge)).Build();
        var store = new InMemoryTenancyStore(harbor.Catalogue);
        store.Seed(harbor.Aggregates);
        var retired = New.Catalogue(gauge with { Retired = true });
        var administrator = HostUser.InSeat(harbor.Tenant.Id, harbor.Administrator.Id);

        using (TenancyUsers.Begin(administrator))
        {
            var live = new TenancyAccessManager(harbor.Catalogue, _clock, NoServices.Instance).In(store, store);
            var now = new TenancyAccessManager(retired, _clock, NoServices.Instance).In(store, store);

            live.UnitsWhereIHold("gauges.read").Should().HaveCount(4, "the administrators' role was made while the permission was live");
            now.UnitsWhereIHold("gauges.read").Should().BeEmpty("the rows are still there, and the permission is retired");
            now.RolesWithPermission("gauges.read").Should().BeEmpty();
            live.PermissionsIHoldAt(harbor.North).Should().Contain("gauges.read");
            now.PermissionsIHoldAt(harbor.North).Should().NotContain("gauges.read").And.Contain(TenancyPermissions.AdministratorPermission);
        }
    }
}
