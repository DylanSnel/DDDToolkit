namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The questions that answer for several permissions at once, and for a part of the tree: every pair of a unit and a
/// permission the caller holds there, every pair of a role and a permission it grants, and the units under one unit. They
/// answer what the questions for one permission answer, permission by permission, and no more to anyone.
/// </summary>
public class PermissionSetQuestionTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    private readonly Harbor _harbor;
    private readonly InMemoryTenancyStore _store;
    private readonly FixedClock _clock = new();

    public PermissionSetQuestionTests()
    {
        _harbor = new HarborBuilder().Build();
        _store = new InMemoryTenancyStore(_harbor.Catalogue);
        _store.Seed(_harbor.Aggregates);
    }

    private HostSeat SeatAt(OrganizationUnitId unit, string pack, DateTimeOffset? until = null)
    {
        var seat = _harbor.NewSeat();
        var role = _harbor.RolesByPack[pack];
        seat.Place(unit, primary: true, Now, placedBy: null);
        seat.Grant(unit, role.Id, role.Facts, GrantPeriod.Between(Now, until), null, null);
        _store.Seed(seat);
        return seat;
    }

    private T Ask<T>(HostCaller caller, Func<ITenancyQueries<TenantId, SeatId, OrganizationUnitId, RoleId>, T> ask, TenancyCatalogue? catalogue = null)
    {
        using (TenancyCallers.Begin(caller))
        {
            return ask(new CurrentTenancy<TenantId, SeatId, OrganizationUnitId, RoleId>(catalogue ?? _harbor.Catalogue, _clock).In(_store, _store));
        }
    }

    private static HostCaller As(HostSeat seat) => HostCaller.InSeat(seat.TenantId, seat.Id);

    private static UnitPermission<OrganizationUnitId> Pair(OrganizationUnitId unit, string permission) => new(unit, permission);

    [Fact]
    public void WhereIHold_answers_each_permission_at_every_unit_it_reaches()
    {
        var supervisor = As(SeatAt(_harbor.North, HostCatalogue.SupervisorPack));
        string[] permissions = [HostCatalogue.WidgetChange, TenancyPermissions.UnitsManage, TenancyPermissions.RolesManage];

        Ask(supervisor, queries => queries.WhereIHold(permissions).ToArray()).Should().BeEquivalentTo(
            [
                Pair(_harbor.North, HostCatalogue.WidgetChange), Pair(_harbor.NorthCoast, HostCatalogue.WidgetChange),
                Pair(_harbor.North, TenancyPermissions.UnitsManage), Pair(_harbor.NorthCoast, TenancyPermissions.UnitsManage),
            ],
            "the supervisor holds two of the three permissions, at its unit and below, and neither at the root nor beside it");

        // Permission by permission it is what the question for one permission answers.
        foreach (var permission in permissions)
        {
            Ask(supervisor, queries => queries.WhereIHold(permissions).Where(pair => pair.Permission == permission).Select(pair => pair.Unit).ToArray())
                .Should().BeEquivalentTo(Ask(supervisor, queries => queries.UnitsWhereIHold(permission).ToArray()));
        }

        // A permission asked for twice is answered once, and a seat placed twice holds a permission at each unit once.
        Ask(supervisor, queries => queries.WhereIHold([TenancyPermissions.UnitsManage, TenancyPermissions.UnitsManage]).ToArray())
            .Should().BeEquivalentTo([Pair(_harbor.North, TenancyPermissions.UnitsManage), Pair(_harbor.NorthCoast, TenancyPermissions.UnitsManage)]);
        Ask(supervisor, queries => queries.WhereIHold([]).ToArray()).Should().BeEmpty("no permissions asked about, nothing answered");
    }

    [Fact]
    public void WhereIHold_counts_a_grant_only_inside_its_period()
    {
        var watcher = As(SeatAt(_harbor.South, HostCatalogue.WatcherPack, until: Now.AddDays(1)));

        Ask(watcher, queries => queries.WhereIHold([HostCatalogue.WidgetRead]).ToArray()).Should().Equal(Pair(_harbor.South, HostCatalogue.WidgetRead));

        _clock.Advance(TimeSpan.FromDays(2));
        Ask(watcher, queries => queries.WhereIHold([HostCatalogue.WidgetRead]).ToArray()).Should().BeEmpty("the grant has ended, and nothing was written for it");
    }

    [Fact]
    public void WhereIHold_refuses_an_unknown_permission()
    {
        var administrator = As(_harbor.Administrator);

        // Every permission is checked, wherever it stands in the list, and for every caller: a typo is a typo.
        FluentActions.Invoking(() => Ask(administrator, queries => queries.WhereIHold([HostCatalogue.WidgetRead, "widget.fly"])))
            .Should().Throw<ArgumentException>().WithMessage("*widget.fly*");
        FluentActions.Invoking(() => Ask(HostCaller.Nobody(TenancyRefusals.NotSeated), queries => queries.WhereIHold(["widget.fly"])))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Ask(HostCaller.SystemIn(_harbor.Tenant.Id), queries => queries.RolePermissions(["widget.fly"])))
            .Should().Throw<ArgumentException>().WithMessage("*widget.fly*");
        FluentActions.Invoking(() => Ask(administrator, queries => queries.WhereIHold(null!))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Ask(administrator, queries => queries.RolePermissions(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_retired_permission_is_held_nowhere_and_granted_by_no_role()
    {
        // The catalogue retires the permission after the roles and the rights were written with it.
        var retired = TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                Permissions = [.. HostCatalogue.Permissions.Select(permission => permission.Name == HostCatalogue.WidgetCreate ? permission with { Retired = true } : permission)],
                Packs = [.. HostCatalogue.Application.Packs.Select(pack => pack with { Permissions = [.. pack.Permissions.Where(permission => permission != HostCatalogue.WidgetCreate)] })],
            },
            []);
        var operatorSeat = As(SeatAt(_harbor.South, HostCatalogue.OperatorPack));
        string[] permissions = [HostCatalogue.WidgetCreate, HostCatalogue.WidgetChange];

        Ask(operatorSeat, queries => queries.WhereIHold(permissions).ToArray(), retired)
            .Should().Equal(Pair(_harbor.South, HostCatalogue.WidgetChange));
        Ask(operatorSeat, queries => queries.RolePermissions(permissions).Select(pair => pair.Permission).Distinct().ToArray(), retired)
            .Should().Equal(HostCatalogue.WidgetChange);
        Ask(HostCaller.SystemIn(_harbor.Tenant.Id), queries => queries.WhereIHold([HostCatalogue.WidgetCreate]).ToArray(), retired)
            .Should().BeEmpty("system work holds every live permission, and a retired one is none");
    }

    [Fact]
    public void System_work_holds_every_permission_asked_at_every_unit_of_its_tenant_and_nobody_none()
    {
        var other = new HarborBuilder().Tenant(2, "quarry").Build();
        _store.Seed(other.Aggregates);
        string[] permissions = [HostCatalogue.WidgetRead, TenancyPermissions.RolesManage];
        OrganizationUnitId[] units = [_harbor.Root, _harbor.North, _harbor.NorthCoast, _harbor.South];

        Ask(HostCaller.SystemIn(_harbor.Tenant.Id), queries => queries.WhereIHold(permissions).ToArray())
            .Should().BeEquivalentTo(units.SelectMany(unit => permissions.Select(permission => Pair(unit, permission))), "every permission at every unit of its own tenant, and at none of another");

        var nobody = HostCaller.Nobody(TenancyRefusals.NotSeated);
        Ask(nobody, queries => queries.WhereIHold(permissions).ToArray()).Should().BeEmpty();
        Ask(nobody, queries => queries.RolePermissions(permissions).ToArray()).Should().BeEmpty();
        Ask(nobody, queries => queries.UnitsUnder(_harbor.Root).ToArray()).Should().BeEmpty();

        // System work outside any tenant has nothing to ask about.
        foreach (var ask in new Action<ITenancyQueries<TenantId, SeatId, OrganizationUnitId, RoleId>>[]
                 {
                     queries => queries.WhereIHold(permissions), queries => queries.RolePermissions(permissions), queries => queries.UnitsUnder(_harbor.Root),
                 })
        {
            FluentActions.Invoking(() => Ask<object?>(HostCaller.System, queries =>
                {
                    ask(queries);
                    return null;
                }))
                .Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void RoleKeys_pairs_every_active_role_of_the_tenant_with_the_permissions_it_grants()
    {
        var other = new HarborBuilder().Tenant(2, "quarry").Build();
        _store.Seed(other.Aggregates);
        var watcher = As(SeatAt(_harbor.South, HostCatalogue.WatcherPack));
        string[] permissions = [HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate, TenancyPermissions.RolesManage];

        var pairs = Ask(watcher, queries => queries.RolePermissions(permissions).ToArray());

        // A seat is answered about its tenant's roles whatever it holds itself, as it is for one permission.
        RoleId Role(string pack) => _harbor.RolesByPack[pack].Id;
        pairs.Should().BeEquivalentTo(
            new RoleWithPermission<RoleId>[]
            {
                new(Role(HostCatalogue.AdministratorPack), HostCatalogue.WidgetRead), new(Role(HostCatalogue.AdministratorPack), HostCatalogue.WidgetCreate),
                new(Role(HostCatalogue.AdministratorPack), TenancyPermissions.RolesManage),
                new(Role(HostCatalogue.SupervisorPack), HostCatalogue.WidgetRead), new(Role(HostCatalogue.SupervisorPack), HostCatalogue.WidgetCreate),
                new(Role(HostCatalogue.OperatorPack), HostCatalogue.WidgetRead), new(Role(HostCatalogue.OperatorPack), HostCatalogue.WidgetCreate),
                new(Role(HostCatalogue.WatcherPack), HostCatalogue.WidgetRead),
            });
        pairs.Select(pair => pair.Role).Should().NotIntersectWith(other.RolesByPack.Values.Select(role => role.Id), "no role of another tenant");

        foreach (var permission in permissions)
        {
            pairs.Where(pair => pair.Permission == permission).Select(pair => pair.Role)
                .Should().BeEquivalentTo(Ask(watcher, queries => queries.RolesWithPermission(permission).ToArray()));
        }

        // An archived role grants nothing.
        var archived = _harbor.RolesByPack[HostCatalogue.OperatorPack];
        archived.Archive<SeatId>();
        _store.Seed(archived);
        Ask(watcher, queries => queries.RolePermissions(permissions).Select(pair => pair.Role).ToArray()).Should().NotContain(archived.Id);
    }

    [Fact]
    public void UnitsUnder_is_the_unit_and_every_unit_below_it_in_the_callers_tenant()
    {
        var other = new HarborBuilder().Tenant(2, "quarry").Build();
        _store.Seed(other.Aggregates);
        var watcher = As(SeatAt(_harbor.South, HostCatalogue.WatcherPack));

        Ask(watcher, queries => queries.UnitsUnder(_harbor.Root).ToArray()).Should().BeEquivalentTo([_harbor.Root, _harbor.North, _harbor.NorthCoast, _harbor.South]);
        Ask(watcher, queries => queries.UnitsUnder(_harbor.North).ToArray())
            .Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast], "the tree is the tenant's to read, wherever the seat is placed in it");
        Ask(watcher, queries => queries.UnitsUnder(_harbor.NorthCoast).ToArray()).Should().Equal(_harbor.NorthCoast);
        Ask(HostCaller.SystemIn(_harbor.Tenant.Id), queries => queries.UnitsUnder(_harbor.North).ToArray()).Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast]);

        Ask(watcher, queries => queries.UnitsUnder(other.Root).ToArray()).Should().BeEmpty("a unit of another tenant answers nothing");
        Ask(watcher, queries => queries.UnitsUnder(OrganizationUnitId.CreateSequential()).ToArray()).Should().BeEmpty("nor does a unit of none");
    }
}
