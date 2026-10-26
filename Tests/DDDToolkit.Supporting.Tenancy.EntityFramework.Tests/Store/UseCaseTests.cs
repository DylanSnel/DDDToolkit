using System.Globalization;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// The use cases run end to end over the database, each command as one caller in a scope of its own, the way an
/// application runs them: the rules that read the stored rights hold because the writer keeps those rights in
/// step with every save. The races between commands are in <see cref="ConcurrencyTests"/>.
/// <para>
/// Harbor: North and South under the root, Coast under North. Grace supervises North; Lin is placed at Coast
/// and at South with no role.
/// </para>
/// </summary>
public sealed class UseCaseTests : IDisposable
{
    private readonly FixedClock _clock = new();
    private readonly TestServices _services;

    private HostTenancy.ProvisionedTenant _harbor = null!;
    private OrganizationUnitId _north;
    private OrganizationUnitId _south;
    private OrganizationUnitId _coast;
    private SeatId _grace;
    private SeatId _lin;

    public UseCaseTests() => _services = new TestServices(configure: services => services.AddSingleton<TimeProvider>(_clock));

    public void Dispose() => _services.Dispose();

    private RoleId Operator => _harbor.RolesByPack[HostCatalogue.OperatorPack];

    [Fact]
    public async Task Grants_are_positional_except_for_roles_that_manage_access()
    {
        await BuildHarborAsync();

        await AsGrace(services => services.Seats().GrantAsync(_lin, _coast, Operator, until: null, reason: null, TestContext.Current.CancellationToken));
        (await KeysAsync(_lin)).Should().BeEquivalentTo([(_coast, HostCatalogue.WidgetChange), (_coast, HostCatalogue.WidgetCreate), (_coast, HostCatalogue.WidgetRead)],
            "Operator manages no access, so managing grants at North is enough to give it below it");

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => AsGrace(services =>
            services.Seats().GrantAsync(_lin, _coast, _harbor.AdministratorRole, until: null, reason: null, TestContext.Current.CancellationToken)));
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.RolesManage + ", " + TenancyKeys.SettingsManage);

        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => AsGrace(services =>
            services.Seats().GrantAsync(_lin, _south, Operator, until: null, reason: null, TestContext.Current.CancellationToken)));
        await Refused.WithCodeAsync(TenancyRefusals.StartSystemOnly, () => AsGrace(services =>
            services.Seats().GrantAsync(_lin, _coast, _harbor.RolesByPack[HostCatalogue.WatcherPack], until: null, reason: null, TestContext.Current.CancellationToken, from: _clock.Now)));

        await AsGrace(services => services.Seats().RevokeAsync(_lin, _coast, Operator, TestContext.Current.CancellationToken));
        (await KeysAsync(_lin)).Should().BeEmpty();

        // Someone who manages grants at North and holds nothing else gives Operator all the same.
        var desk = await BySystem(services => services.Roles().CreateAsync("Grant desk", "Gives people their roles", [TenancyKeys.GrantsManage], TestContext.Current.CancellationToken));
        var nell = await _services.SeatAtAsync(_harbor, "Nell", _north);
        await BySystem(services => services.Seats().GrantAsync(nell, _north, desk, until: null, reason: null, TestContext.Current.CancellationToken));

        await _services.BySeat(_harbor.Tenant, nell, services =>
            services.Seats().GrantAsync(_lin, _coast, Operator, until: null, reason: null, TestContext.Current.CancellationToken));
        (await KeysAsync(_lin)).Should().HaveCount(3);
        var supervisor = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => _services.BySeat(_harbor.Tenant, nell, services =>
            services.Seats().GrantAsync(_lin, _coast, _harbor.RolesByPack[HostCatalogue.SupervisorPack], until: null, reason: null, TestContext.Current.CancellationToken)));
        supervisor.Arguments["Missing"].Should().Be(TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage);
    }

    [Fact]
    public async Task A_role_that_manages_access_is_granted_for_no_longer_than_the_granter_holds_those_keys()
    {
        await BuildHarborAsync();
        var supervisors = _harbor.RolesByPack[HostCatalogue.SupervisorPack];
        var hal = await _services.SeatAtAsync(_harbor, "Hal", _north);
        await _services.GrantAsync(_harbor, hal, _north, HostCatalogue.SupervisorPack, until: _clock.Now.AddDays(7));

        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => _services.BySeat(_harbor.Tenant, hal, services =>
            services.Seats().GrantAsync(_lin, _coast, supervisors, until: null, reason: null, TestContext.Current.CancellationToken)));
        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => _services.BySeat(_harbor.Tenant, hal, services =>
            services.Seats().GrantAsync(_lin, _coast, supervisors, until: _clock.Now.AddDays(8), reason: null, TestContext.Current.CancellationToken)));

        await _services.BySeat(_harbor.Tenant, hal, services =>
            services.Seats().GrantAsync(_lin, _coast, supervisors, until: _clock.Now.AddDays(7), reason: "for the week", TestContext.Current.CancellationToken));
        (await _services.StoredRightsAsync(_lin)).Should().NotBeEmpty().And.OnlyContain(right => right.EndsAt == _clock.Now.AddDays(7));

        // Operator manages no access, so it goes for good from a granter whose own grant ends next week.
        await _services.BySeat(_harbor.Tenant, hal, services =>
            services.Seats().GrantAsync(_lin, _coast, Operator, until: null, reason: null, TestContext.Current.CancellationToken));
        (await _services.StoredRightsAsync(_lin)).Where(right => right.RoleId == Operator).Should().NotBeEmpty().And.OnlyContain(right => right.EndsAt == null);
    }

    [Fact]
    public async Task A_seat_gives_itself_a_role_that_manages_no_access_but_never_one_that_does()
    {
        await BuildHarborAsync();

        await AsGrace(services => services.Seats().GrantAsync(_grace, _north, Operator, until: null, reason: null, TestContext.Current.CancellationToken));

        (await _services.StoredRightsAsync(_grace)).Should().Contain(right => right.RoleId == Operator && right.Key == HostCatalogue.WidgetCreate && right.UnitId == _north);
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.SelfAppointment, () => AsGrace(services =>
            services.Seats().GrantAsync(_grace, _north, _harbor.AdministratorRole, until: null, reason: null, TestContext.Current.CancellationToken)));
        refusal.Arguments["Role"].Should().Be(_harbor.AdministratorRole);
        (await _services.StoredRightsAsync(_grace)).Should().NotContain(right => right.RoleId == _harbor.AdministratorRole);
    }

    [Fact]
    public async Task The_tenant_keeps_its_administrator_and_a_second_one_can_go()
    {
        await BuildHarborAsync();
        var ada = _harbor.AdminSeat;
        var root = _harbor.RootUnit;
        var administrators = _harbor.AdministratorRole;

        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => BySystem(services => services.Seats().RevokeAsync(ada, root, administrators, TestContext.Current.CancellationToken)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => BySystem(services => services.Seats().SuspendAsync(ada, TestContext.Current.CancellationToken)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => BySystem(services => services.Seats().DeactivateAsync(ada, TestContext.Current.CancellationToken)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => BySystem(services => services.Seats().WithdrawAsync(ada, root, TestContext.Current.CancellationToken)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => BySystem(services => services.Roles().ArchiveAsync(administrators, TestContext.Current.CancellationToken)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => BySystem(services =>
            services.Roles().SetKeysAsync(administrators, [TenancyKeys.SeatsManage], TestContext.Current.CancellationToken)));

        // An administrator whose grant ends does not count.
        var cy = await _services.SeatAtAsync(_harbor, "Cy", root);
        await _services.GrantAsync(_harbor, cy, root, HostCatalogue.AdministratorPack, until: _clock.Now.AddDays(30));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => BySystem(services => services.Seats().SuspendAsync(ada, TestContext.Current.CancellationToken)));

        var beth = await _services.SeatAtAsync(_harbor, "Beth", root, HostCatalogue.AdministratorPack);
        await _services.BySeat(_harbor.Tenant, beth, services => services.Seats().RevokeAsync(ada, root, administrators, TestContext.Current.CancellationToken));

        (await _services.StoredRightsAsync(ada)).Should().BeEmpty();
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => BySystem(services => services.Seats().SuspendAsync(beth, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_moved_unit_is_reached_from_its_new_parent_only()
    {
        await BuildHarborAsync();
        (await GraceHoldsAtAsync(TenancyKeys.UnitsManage, _coast)).Should().BeTrue();

        await BySystem(services => services.Organization().MoveUnitAsync(_coast, _south, TestContext.Current.CancellationToken));

        (await GraceHoldsAtAsync(TenancyKeys.UnitsManage, _coast)).Should().BeFalse("Coast is under South now");
        (await AsGrace(services => services.Directory().ListUnitsAsync(TestContext.Current.CancellationToken))).Select(unit => unit.Path).Should().Equal("Harbor / North");
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => AsGrace(services =>
            services.Organization().RenameUnitAsync(_coast, "Coastline", TestContext.Current.CancellationToken)));

        // Lin was placed at Coast, and still is: placements do not move, what they reach does.
        (await _services.BySeat(_harbor.Tenant, _lin, services => services.Directory().ListUnitsAsync(TestContext.Current.CancellationToken)))
            .Select(unit => unit.Path).Should().BeEquivalentTo(["Harbor / South", "Harbor / South / Coast"]);
    }

    [Fact]
    public async Task An_expired_grant_stops_a_command_without_any_write()
    {
        await BuildHarborAsync();
        var hal = await _services.SeatAtAsync(_harbor, "Hal", _north);
        await _services.GrantAsync(_harbor, hal, _north, HostCatalogue.SupervisorPack, until: _clock.Now.AddHours(1));
        await _services.BySeat(_harbor.Tenant, hal, services => services.Organization().AddUnitAsync(_north, "Bay", TestContext.Current.CancellationToken));
        var stored = await _services.StoredRightsAsync(hal);

        _clock.Advance(TimeSpan.FromHours(2));
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, hal, services =>
            services.Organization().AddUnitAsync(_north, "Cove", TestContext.Current.CancellationToken)));

        (await _services.StoredRightsAsync(hal)).Should().BeEquivalentTo(stored, "nothing was written when the grant ended");
        var overview = await _services.BySeat(_harbor.Tenant, hal, services => services.Directory().WhoAmIAsync(TestContext.Current.CancellationToken));
        overview.Keys.Should().BeEmpty();
        overview.Seat.Placements.Single().Grants.Single().AppliesAt(overview.AsOf).Should().BeFalse();
    }

    [Fact]
    public async Task A_suspended_seat_holds_nothing_until_reactivated()
    {
        await BuildHarborAsync();

        await BySystem(services => services.Seats().SuspendAsync(_grace, TestContext.Current.CancellationToken));
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => AsGrace(services =>
            services.Organization().AddUnitAsync(_north, "Bay", TestContext.Current.CancellationToken)));
        (await AsGrace(services => services.Directory().WhoAmIAsync(TestContext.Current.CancellationToken))).Keys.Should().BeEmpty();

        await BySystem(services => services.Seats().ReactivateAsync(_grace, TestContext.Current.CancellationToken));
        await AsGrace(services => services.Organization().AddUnitAsync(_north, "Bay", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Suspending_a_seat_that_holds_a_role_that_manages_access_needs_its_keys()
    {
        await BuildHarborAsync();
        var desk = await BySystem(services => services.Roles().CreateAsync("Seat desk", "Keeps the list of people", [TenancyKeys.SeatsManage], TestContext.Current.CancellationToken));
        var oli = await _services.SeatAtAsync(_harbor, "Oli", _harbor.RootUnit);
        await BySystem(services => services.Seats().GrantAsync(oli, _harbor.RootUnit, desk, until: null, reason: null, TestContext.Current.CancellationToken));
        await _services.GrantAsync(_harbor, _lin, _coast, HostCatalogue.WatcherPack);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => _services.BySeat(_harbor.Tenant, oli, services =>
            services.Seats().SuspendAsync(_grace, TestContext.Current.CancellationToken)));
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.UnitsManage, "Supervisor manages access, and Oli holds none of its keys that do but seats.manage");
        (await _services.StoredRightsAsync(_grace)).Should().NotBeEmpty("the refused suspension wrote nothing");

        await _services.BySeat(_harbor.Tenant, oli, services => services.Seats().SuspendAsync(_lin, TestContext.Current.CancellationToken));
        (await _services.StoredRightsAsync(_lin)).Should().BeEmpty("Watcher manages no access, so seats.manage is enough to stop it");
    }

    [Fact]
    public async Task A_move_that_would_hold_the_movers_keys_longer_is_refused()
    {
        await BuildHarborAsync();
        await BySystem(services => services.Seats().PlaceAsync(_grace, _south, primary: false, TestContext.Current.CancellationToken));
        await _services.GrantAsync(_harbor, _grace, _south, HostCatalogue.SupervisorPack, until: _clock.Now.AddDays(7));
        var pier = await AsGrace(services => services.Organization().AddUnitAsync(_south, "Pier", TestContext.Current.CancellationToken));

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => AsGrace(services =>
            services.Organization().MoveUnitAsync(pier, _north, TestContext.Current.CancellationToken)));
        refusal.Arguments["Missing"].As<string>().Should().Contain(TenancyKeys.UnitsManage, "Grace holds South's keys for a week, and North's for good");

        await AsGrace(services => services.Organization().MoveUnitAsync(_coast, _south, TestContext.Current.CancellationToken));
        (await GraceHoldsAtAsync(TenancyKeys.UnitsManage, _coast)).Should().BeTrue("the other way round her hold only gets shorter");
    }

    [Fact]
    public async Task A_move_takes_no_key_that_manages_access_from_someone_whose_hold_the_mover_does_not_match()
    {
        await BuildHarborAsync();
        var hal = await _services.SeatAtAsync(_harbor, "Hal", _harbor.RootUnit);
        await _services.GrantAsync(_harbor, hal, _harbor.RootUnit, HostCatalogue.AdministratorPack, until: _clock.Now.AddDays(7));

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => _services.BySeat(_harbor.Tenant, hal, services =>
            services.Organization().MoveUnitAsync(_coast, _south, TestContext.Current.CancellationToken)));
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage,
            "Grace supervises North for good, and Hal runs the tenant for a week");
        (await GraceHoldsAtAsync(TenancyKeys.UnitsManage, _coast)).Should().BeTrue("the refused move wrote nothing");

        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, services =>
            services.Organization().MoveUnitAsync(_coast, _south, TestContext.Current.CancellationToken));
        (await GraceHoldsAtAsync(TenancyKeys.UnitsManage, _coast)).Should().BeFalse("an administrator holds every key for good");
    }

    [Fact]
    public async Task Withdrawing_a_placement_takes_its_rights()
    {
        await BuildHarborAsync();
        await _services.GrantAsync(_harbor, _lin, _coast, HostCatalogue.WatcherPack);
        await _services.GrantAsync(_harbor, _lin, _south, HostCatalogue.OperatorPack);

        await AsGrace(services => services.Seats().WithdrawAsync(_lin, _coast, TestContext.Current.CancellationToken));

        (await KeysAsync(_lin)).Should().OnlyContain(pair => pair.Unit == _south).And.HaveCount(3);
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => AsGrace(services =>
            services.Seats().WithdrawAsync(_lin, _south, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Tenants_are_kept_apart()
    {
        await BuildHarborAsync();
        var orchard = await _services.ProvisionAsync("orchard", administratorName: "Odette");

        await _services.BySeat(orchard.Tenant, orchard.AdminSeat, async services =>
        {
            (await services.Directory().ListSeatsAsync(TestContext.Current.CancellationToken)).Select(seat => seat.DisplayName).Should().Equal("Odette");
            (await services.Directory().ListUnitsAsync(TestContext.Current.CancellationToken)).Select(unit => unit.Unit.Id).Should().Equal(orchard.RootUnit);
        });

        await Refused.WithCodeAsync(TenancyRefusals.SeatNotFound, () => _services.BySeat(orchard.Tenant, orchard.AdminSeat, services =>
            services.Seats().SuspendAsync(_grace, TestContext.Current.CancellationToken)));
        await Refused.WithCodeAsync(TenancyRefusals.UnitNotFound, () => _services.BySystemIn(orchard.Tenant, services =>
            services.Seats().PlaceAsync(orchard.AdminSeat, _north, primary: false, TestContext.Current.CancellationToken)));

        // A caller that names a tenant its seat is not in holds nothing there.
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(orchard.Tenant, _harbor.AdminSeat, services =>
            services.Organization().AddUnitAsync(orchard.RootUnit, "Grove", TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task The_directory_reads_over_the_database()
    {
        await BuildHarborAsync();

        var units = await AsGrace(services => services.Directory().ListUnitsAsync(TestContext.Current.CancellationToken));
        units.Select(unit => (unit.Path, unit.Depth)).Should().Equal(("Harbor / North", 2), ("Harbor / North / Coast", 3));

        var overview = await AsGrace(services => services.Directory().WhoAmIAsync(TestContext.Current.CancellationToken));
        overview.Seat.DisplayName.Should().Be("Grace", "the host's own name, on the seat the directory read");
        overview.UnitOf(overview.Seat.Placements.Should().ContainSingle().Which.UnitId).Path.Should().Be("Harbor / North");
        overview.Keys.Single(reach => reach.Key == TenancyKeys.UnitsManage).Reaches.Select(unit => unit.Path)
            .Should().Equal("Harbor / North", "Harbor / North / Coast");

        // Lin holds no role, and the directory asks none: with no policy on this database, Grace's overview comes to
        // him whole, the same as her own, so who may ask is the request's to say.
        var asked = await _services.BySeat(_harbor.Tenant, _lin, services => services.Directory().SeatOverviewAsync(_grace, TestContext.Current.CancellationToken));
        (asked.Seat.Id, asked.Seat.DisplayName).Should().Be((_grace, "Grace"));
        asked.Keys.Should().BeEquivalentTo(overview.Keys, options => options.WithStrictOrdering());
        asked.Roles.Should().BeEquivalentTo(overview.Roles, options => options.WithStrictOrdering());

        (await BySystem(services => services.Directory().ListSeatsAsync(TestContext.Current.CancellationToken))).Select(seat => seat.DisplayName)
            .Should().BeEquivalentTo(["Ada", "Grace", "Lin"]);
        (await BySystem(services => services.Directory().ListRolesAsync(TestContext.Current.CancellationToken))).Select(role => role.FromPack)
            .Should().BeEquivalentTo(_harbor.RolesByPack.Keys);
    }

    [Fact]
    public async Task The_directory_answers_names_by_id_from_tenancys_own_tables()
    {
        await BuildHarborAsync();
        var orchard = await _services.ProvisionAsync("orchard", administratorName: "Odette");
        var supervisor = _harbor.RolesByPack[HostCatalogue.SupervisorPack];

        // Lin is placed at Coast and at South and holds no role: every name of the tenant is Lin's to read all the same.
        await _services.BySeat(_harbor.Tenant, _lin, async services =>
        {
            var directory = services.Directory();

            _services.Commands.Reset();
            (await directory.SeatsByIdAsync([_grace, _harbor.AdminSeat, orchard.AdminSeat], TestContext.Current.CancellationToken))
                .Select(seat => (seat.Id, seat.DisplayName, seat.Status))
                .Should().BeEquivalentTo([(_harbor.AdminSeat, "Ada", SeatStatus.Active), (_grace, "Grace", SeatStatus.Active)]);
            _services.Commands.Count.Should().Be(1, "the seats are one statement");
            _services.Commands.Commands.Single().Should().Contain(_services.Database.TenancyTable("Seats"), "the host's name is read from the seats' own table");

            _services.Commands.Reset();
            (await directory.RolesByIdAsync([Operator, supervisor, orchard.AdministratorRole], TestContext.Current.CancellationToken))
                .Select(role => (role.Name, role.ManagesAccess)).Should().Equal(("Operator", false), ("Supervisor", true));
            _services.Commands.Count.Should().Be(1, "the roles are one statement");

            _services.Commands.Reset();
            var units = await directory.UnitsByIdAsync([_north, _harbor.RootUnit, orchard.RootUnit], TestContext.Current.CancellationToken);
            units.Select(unit => (unit.Unit.Name, unit.Path, unit.Depth)).Should().Equal(("Harbor", "Harbor", 1), ("North", "Harbor / North", 2));
            _services.Commands.Count.Should().Be(2, "the organization with the application's own units, and the closure that orders a path");

            _services.Commands.Reset();
            (await directory.SeatsByIdAsync([], TestContext.Current.CancellationToken)).Should().BeEmpty();
            (await directory.RolesByIdAsync([], TestContext.Current.CancellationToken)).Should().BeEmpty();
            (await directory.UnitsByIdAsync([], TestContext.Current.CancellationToken)).Should().BeEmpty();
            _services.Commands.Count.Should().Be(0, "no ids, no statement");
        });

        // More ids cost no more statements.
        var many = Enumerable.Range(0, HostTenancy.TenancyDirectory.MostIds - 2).Select(_ => OrganizationUnitId.CreateSequential()).Append(_coast).Append(_south).ToArray();
        await _services.BySeat(_harbor.Tenant, _lin, async services =>
        {
            _services.Commands.Reset();
            (await services.Directory().UnitsByIdAsync(many, TestContext.Current.CancellationToken)).Select(unit => unit.Path).Should().Equal("Harbor / North / Coast", "Harbor / South");
            _services.Commands.Count.Should().Be(2);
        });

        await Refused.WithCodeAsync(TenancyRefusals.TooManyIds, () => _services.BySeat(_harbor.Tenant, _lin, services =>
            services.Directory().UnitsByIdAsync([.. many, OrganizationUnitId.CreateSequential()], TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_flat_tenant_grows_units_once_its_shape_changes()
    {
        var flat = await _services.ProvisionAsync("meadow", TenantShape.Flat);
        flat.RolesByPack.Should().NotContainKey(HostCatalogue.SupervisorPack, "the supervisors' pack is for hierarchical tenants");

        await Refused.WithCodeAsync(TenancyRefusals.FlatTenant, () => _services.BySeat(flat.Tenant, flat.AdminSeat, services =>
            services.Organization().AddUnitAsync(flat.RootUnit, "East", TestContext.Current.CancellationToken)));

        await _services.BySeat(flat.Tenant, flat.AdminSeat, services => services.Tenants().ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, TestContext.Current.CancellationToken));
        var east = await _services.BySeat(flat.Tenant, flat.AdminSeat, services =>
            services.Organization().AddUnitAsync(flat.RootUnit, "East", TestContext.Current.CancellationToken));

        (await _services.BySeat(flat.Tenant, flat.AdminSeat, services => services.Directory().ListRolesAsync(TestContext.Current.CancellationToken)))
            .Select(role => role.FromPack).Should().Contain(HostCatalogue.SupervisorPack);
        (await _services.StoredPathsAsync(flat.Tenant)).Should().Contain(path => path.AncestorId == flat.RootUnit && path.DescendantId == east && path.Distance == 1);
    }

    [Fact]
    public async Task A_field_the_application_sets_on_a_new_unit_is_written_in_the_save_that_adds_it()
    {
        await BuildHarborAsync();

        var bay = await AsGrace(services => services.Organization().AddUnitAsync(
            _north, "Bay", TestContext.Current.CancellationToken, configure: unit => unit.SetCostCentre("BA-001")));

        // Read again, by a scope of its own: the unit's own column went with the unit.
        await _services.BySeat(_harbor.Tenant, _grace, async scoped =>
            (await scoped.Tenancy().Set<HostOrganization>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken))
                .FindUnit(bay)!.CostCentre.Should().Be("BA-001"));

        // The application's rule about its own field holds as it does on every save: nothing is added.
        await FluentActions.Awaiting(() => AsGrace(services => services.Organization().AddUnitAsync(
                _north, "Cove", TestContext.Current.CancellationToken, configure: unit => unit.SetCostCentre("cove"))))
            .Should().ThrowAsync<DDDToolkit.Exceptions.InvariantViolationException>();
        (await _services.BySeat(_harbor.Tenant, _grace, scoped => scoped.Directory().ListUnitsAsync(TestContext.Current.CancellationToken)))
            .Select(unit => unit.Unit.Name).Should().Contain("Bay").And.NotContain("Cove");
    }

    [Fact]
    public async Task A_unit_whose_callback_throws_is_not_written_by_a_later_save_in_the_same_scope()
    {
        await BuildHarborAsync();
        var units = _services.Database.CountRows("OrganizationUnits");

        // One scope, as one request or one GraphQL operation with two mutations: the callback throws, the caller
        // goes on, and the next command saves the organization it loaded, tracked, in the same scope.
        await BySystem(async services =>
        {
            await FluentActions.Awaiting(() => services.Organization().AddUnitAsync(
                    _north, "Ghost", TestContext.Current.CancellationToken, configure: _ => throw new InvalidOperationException("no such kind")))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("no such kind");
            await services.Organization().RenameUnitAsync(_north, "Northern", TestContext.Current.CancellationToken);
        });

        _services.Database.CountRows("OrganizationUnits").Should().Be(units, "the organization never took the unit in, so the rename's save had none to write");
        (await BySystem(services => services.Directory().ListUnitsAsync(TestContext.Current.CancellationToken)))
            .Select(unit => unit.Unit.Name).Should().Contain("Northern").And.NotContain("Ghost");
    }

    [Fact]
    public async Task A_tenant_provisioned_in_a_language_is_saved_with_its_roles_in_it_and_the_applications_own_fields()
    {
        var dutch = CultureInfo.GetCultureInfo("nl");
        using var services = new TestServices(configure: registered => registered.AddSingleton<IRolePackTexts>(new DutchPacks()));

        var polder = await services.RunAsync(HostCaller.System, scoped => scoped.Tenants().ProvisionAsync(
            new HostTenancy.TenantToProvision(
                "polder", "Polder Werken", TenantShape.Flat, "Polder", Guid.NewGuid(),
                Language: dutch,
                ConfigureTenant: tenant => tenant.MarkAsDemo(),
                ConfigureRoot: root => root.SetCostCentre("PO-001"),
                ConfigureFirstSeat: seat => seat.ChangeJobTitle("Dijkgraaf")),
            TestContext.Current.CancellationToken));

        // Read again, by a scope of its own: one save wrote the roles' texts and the application's columns.
        await services.BySeat(polder.Tenant, polder.AdminSeat, async scoped =>
        {
            (await scoped.Directory().ListRolesAsync(TestContext.Current.CancellationToken)).Select(role => (role.Name, role.FromPack)).Should().Equal(
                ("Bediener", HostCatalogue.OperatorPack), ("Hoofdgebruiker", HostCatalogue.AdministratorPack), ("Toeschouwer", HostCatalogue.WatcherPack));
            (await scoped.Tenancy().Set<HostTenant>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).IsDemo.Should().BeTrue();
            (await scoped.Tenancy().Set<HostSeat>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).JobTitle.Should().Be("Dijkgraaf");
            (await scoped.Tenancy().Set<HostOrganization>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Root.CostCentre.Should().Be("PO-001");
        });

        // A change of shape names the new roles in the language it is given, and the unique index on a tenant's
        // role names still sees them: the translated name is taken from then on.
        await services.BySeat(polder.Tenant, polder.AdminSeat, scoped =>
            scoped.Tenants().ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, dutch, TestContext.Current.CancellationToken));
        await Refused.WithCodeAsync(TenancyRefusals.RoleNameTaken, () => services.BySeat(polder.Tenant, polder.AdminSeat, scoped =>
            scoped.Roles().CreateAsync("afdelingshoofd", "Made by hand", [HostCatalogue.WidgetRead], TestContext.Current.CancellationToken)));

        // A callback that throws leaves no row behind, so the slug is free for the next attempt.
        await FluentActions.Awaiting(() => services.RunAsync(HostCaller.System, scoped => scoped.Tenants().ProvisionAsync(
                new HostTenancy.TenantToProvision(
                    "molen", "Molen Werken", TenantShape.Flat, "Molen", Guid.NewGuid(),
                    ConfigureTenant: _ => throw new InvalidOperationException("no such plan")),
                TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("no such plan");
        (await services.ProvisionAsync("molen", TenantShape.Flat)).Tenant.Should().NotBe(polder.Tenant);
    }

    /// <summary>The host's packs in Dutch, as an application would read them from its own resources.</summary>
    private sealed class DutchPacks : IRolePackTexts
    {
        public (string Name, string Description)? For(RolePack pack, CultureInfo culture)
            => culture.TwoLetterISOLanguageName != "nl"
                ? null
                : pack.Key switch
                {
                    HostCatalogue.AdministratorPack => ("Hoofdgebruiker", "Regelt de tenant"),
                    HostCatalogue.SupervisorPack => ("Afdelingshoofd", "Leidt een deel van de organisatie"),
                    HostCatalogue.OperatorPack => ("Bediener", "Werkt met widgets"),
                    HostCatalogue.WatcherPack => ("Toeschouwer", "Bekijkt widgets"),
                    _ => null,
                };
    }

    private async Task BuildHarborAsync()
    {
        _harbor = await _services.ProvisionAsync("harbor");
        _north = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "North");
        _south = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "South");
        _coast = await _services.AddUnitAsync(_harbor.Tenant, _north, "Coast");
        _grace = await _services.SeatAtAsync(_harbor, "Grace", _north, HostCatalogue.SupervisorPack);
        _lin = await _services.SeatAtAsync(_harbor, "Lin", _coast);
        await BySystem(services => services.Seats().PlaceAsync(_lin, _south, primary: false, TestContext.Current.CancellationToken));
    }

    private Task<T> AsGrace<T>(Func<IServiceProvider, Task<T>> act) => _services.BySeat(_harbor.Tenant, _grace, act);

    private Task AsGrace(Func<IServiceProvider, Task> act) => _services.BySeat(_harbor.Tenant, _grace, act);

    private Task<T> BySystem<T>(Func<IServiceProvider, Task<T>> act) => _services.BySystemIn(_harbor.Tenant, act);

    private Task BySystem(Func<IServiceProvider, Task> act) => _services.BySystemIn(_harbor.Tenant, act);

    private Task<bool> GraceHoldsAtAsync(string key, OrganizationUnitId unit)
        => AsGrace(services => services.Answers().Over(services.Tenancy()).HoldsAtAsync(key, unit, TestContext.Current.CancellationToken));

    private async Task<List<(OrganizationUnitId Unit, string Key)>> KeysAsync(SeatId seat)
        => [.. (await _services.StoredRightsAsync(seat)).Select(right => (right.UnitId, right.Key))];
}
