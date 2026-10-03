namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The questions that answer for several keys at once, and for a part of the tree: every pair of a unit and a
/// key the caller holds there, every pair of a role and a key it grants, and the units under one unit. They
/// answer what the questions for one key answer, key by key, and no more to anyone.
/// </summary>
public class KeySetQuestionTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    private readonly Harbor _harbor;
    private readonly InMemoryTenancyStore _store;
    private readonly FixedClock _clock = new();

    public KeySetQuestionTests()
    {
        _harbor = new HarborBuilder().Build();
        _store = new InMemoryTenancyStore(_harbor.Catalogue);
        _store.Seed(_harbor.Aggregates);
    }

    private HostSeat SeatAt(OrganizationUnitId unit, string pack, DateTimeOffset? until = null)
    {
        var seat = _harbor.NewSeat("Bert");
        var role = _harbor.RolesByPack[pack];
        seat.Place(unit, primary: true, Now, placedBy: null);
        seat.Grant(unit, role.Id, role.Facts, GrantPeriod.Between(Now, until), null, null);
        _store.Seed(seat);
        return seat;
    }

    private T Ask<T>(HostCaller caller, Func<ITenancyQuestions<TenantId, SeatId, OrganizationUnitId, RoleId>, T> ask, TenancyCatalogue? catalogue = null)
    {
        using (TenancyCallers.Begin(caller))
        {
            return ask(new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(catalogue ?? _harbor.Catalogue, _clock).Over(_store, _store));
        }
    }

    private static HostCaller As(HostSeat seat) => HostCaller.InSeat(seat.TenantId, seat.Id);

    private static UnitKey<OrganizationUnitId> Pair(OrganizationUnitId unit, string key) => new(unit, key);

    [Fact]
    public void WhereIHold_answers_each_key_at_every_unit_it_reaches()
    {
        var supervisor = As(SeatAt(_harbor.North, HostCatalogue.SupervisorPack));
        string[] keys = [HostCatalogue.WidgetChange, TenancyKeys.UnitsManage, TenancyKeys.RolesManage];

        Ask(supervisor, questions => questions.WhereIHold(keys).ToArray()).Should().BeEquivalentTo(
            [
                Pair(_harbor.North, HostCatalogue.WidgetChange), Pair(_harbor.NorthCoast, HostCatalogue.WidgetChange),
                Pair(_harbor.North, TenancyKeys.UnitsManage), Pair(_harbor.NorthCoast, TenancyKeys.UnitsManage),
            ],
            "the supervisor holds two of the three keys, at its unit and below, and neither at the root nor beside it");

        // Key by key it is what the question for one key answers.
        foreach (var key in keys)
        {
            Ask(supervisor, questions => questions.WhereIHold(keys).Where(pair => pair.Key == key).Select(pair => pair.Unit).ToArray())
                .Should().BeEquivalentTo(Ask(supervisor, questions => questions.UnitsWhereIHold(key).ToArray()));
        }

        // A key asked for twice is answered once, and a seat placed twice holds a key at each unit once.
        Ask(supervisor, questions => questions.WhereIHold([TenancyKeys.UnitsManage, TenancyKeys.UnitsManage]).ToArray())
            .Should().BeEquivalentTo([Pair(_harbor.North, TenancyKeys.UnitsManage), Pair(_harbor.NorthCoast, TenancyKeys.UnitsManage)]);
        Ask(supervisor, questions => questions.WhereIHold([]).ToArray()).Should().BeEmpty("no keys asked about, nothing answered");
    }

    [Fact]
    public void WhereIHold_counts_a_grant_only_inside_its_period()
    {
        var watcher = As(SeatAt(_harbor.South, HostCatalogue.WatcherPack, until: Now.AddDays(1)));

        Ask(watcher, questions => questions.WhereIHold([HostCatalogue.WidgetRead]).ToArray()).Should().Equal(Pair(_harbor.South, HostCatalogue.WidgetRead));

        _clock.Advance(TimeSpan.FromDays(2));
        Ask(watcher, questions => questions.WhereIHold([HostCatalogue.WidgetRead]).ToArray()).Should().BeEmpty("the grant has ended, and nothing was written for it");
    }

    [Fact]
    public void WhereIHold_refuses_an_unknown_key()
    {
        var administrator = As(_harbor.Administrator);

        // Every key is checked, wherever it stands in the list, and for every caller: a typo is a typo.
        FluentActions.Invoking(() => Ask(administrator, questions => questions.WhereIHold([HostCatalogue.WidgetRead, "widget.fly"])))
            .Should().Throw<ArgumentException>().WithMessage("*widget.fly*");
        FluentActions.Invoking(() => Ask(HostCaller.Nobody(TenancyRefusals.NotSeated), questions => questions.WhereIHold(["widget.fly"])))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Ask(HostCaller.SystemIn(_harbor.Tenant.Id), questions => questions.RoleKeys(["widget.fly"])))
            .Should().Throw<ArgumentException>().WithMessage("*widget.fly*");
        FluentActions.Invoking(() => Ask(administrator, questions => questions.WhereIHold(null!))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => Ask(administrator, questions => questions.RoleKeys(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_retired_key_is_held_nowhere_and_granted_by_no_role()
    {
        // The catalogue retires the key after the roles and the rights were written with it.
        var retired = TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                Permissions = [.. HostCatalogue.Permissions.Select(permission => permission.Key == HostCatalogue.WidgetCreate ? permission with { Retired = true } : permission)],
                Packs = [.. HostCatalogue.Application.Packs.Select(pack => pack with { Keys = [.. pack.Keys.Where(key => key != HostCatalogue.WidgetCreate)] })],
            },
            []);
        var operatorSeat = As(SeatAt(_harbor.South, HostCatalogue.OperatorPack));
        string[] keys = [HostCatalogue.WidgetCreate, HostCatalogue.WidgetChange];

        Ask(operatorSeat, questions => questions.WhereIHold(keys).ToArray(), retired)
            .Should().Equal(Pair(_harbor.South, HostCatalogue.WidgetChange));
        Ask(operatorSeat, questions => questions.RoleKeys(keys).Select(pair => pair.Key).Distinct().ToArray(), retired)
            .Should().Equal(HostCatalogue.WidgetChange);
        Ask(HostCaller.SystemIn(_harbor.Tenant.Id), questions => questions.WhereIHold([HostCatalogue.WidgetCreate]).ToArray(), retired)
            .Should().BeEmpty("system work holds every live key, and a retired one is none");
    }

    [Fact]
    public void System_work_holds_every_key_asked_at_every_unit_of_its_tenant_and_nobody_none()
    {
        var other = new HarborBuilder().Tenant(2, "quarry").Build();
        _store.Seed(other.Aggregates);
        string[] keys = [HostCatalogue.WidgetRead, TenancyKeys.RolesManage];
        OrganizationUnitId[] units = [_harbor.Root, _harbor.North, _harbor.NorthCoast, _harbor.South];

        Ask(HostCaller.SystemIn(_harbor.Tenant.Id), questions => questions.WhereIHold(keys).ToArray())
            .Should().BeEquivalentTo(units.SelectMany(unit => keys.Select(key => Pair(unit, key))), "every key at every unit of its own tenant, and at none of another");

        var nobody = HostCaller.Nobody(TenancyRefusals.NotSeated);
        Ask(nobody, questions => questions.WhereIHold(keys).ToArray()).Should().BeEmpty();
        Ask(nobody, questions => questions.RoleKeys(keys).ToArray()).Should().BeEmpty();
        Ask(nobody, questions => questions.UnitsUnder(_harbor.Root).ToArray()).Should().BeEmpty();

        // System work outside any tenant has nothing to ask about.
        foreach (var ask in new Action<ITenancyQuestions<TenantId, SeatId, OrganizationUnitId, RoleId>>[]
                 {
                     questions => questions.WhereIHold(keys), questions => questions.RoleKeys(keys), questions => questions.UnitsUnder(_harbor.Root),
                 })
        {
            FluentActions.Invoking(() => Ask<object?>(HostCaller.System, questions =>
                {
                    ask(questions);
                    return null;
                }))
                .Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void RoleKeys_pairs_every_active_role_of_the_tenant_with_the_keys_it_grants()
    {
        var other = new HarborBuilder().Tenant(2, "quarry").Build();
        _store.Seed(other.Aggregates);
        var watcher = As(SeatAt(_harbor.South, HostCatalogue.WatcherPack));
        string[] keys = [HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate, TenancyKeys.RolesManage];

        var pairs = Ask(watcher, questions => questions.RoleKeys(keys).ToArray());

        // A seat is answered about its tenant's roles whatever it holds itself, as it is for one key.
        RoleId Role(string pack) => _harbor.RolesByPack[pack].Id;
        pairs.Should().BeEquivalentTo(
            new RoleWithKey<RoleId>[]
            {
                new(Role(HostCatalogue.AdministratorPack), HostCatalogue.WidgetRead), new(Role(HostCatalogue.AdministratorPack), HostCatalogue.WidgetCreate),
                new(Role(HostCatalogue.AdministratorPack), TenancyKeys.RolesManage),
                new(Role(HostCatalogue.SupervisorPack), HostCatalogue.WidgetRead), new(Role(HostCatalogue.SupervisorPack), HostCatalogue.WidgetCreate),
                new(Role(HostCatalogue.OperatorPack), HostCatalogue.WidgetRead), new(Role(HostCatalogue.OperatorPack), HostCatalogue.WidgetCreate),
                new(Role(HostCatalogue.WatcherPack), HostCatalogue.WidgetRead),
            });
        pairs.Select(pair => pair.Role).Should().NotIntersectWith(other.RolesByPack.Values.Select(role => role.Id), "no role of another tenant");

        foreach (var key in keys)
        {
            pairs.Where(pair => pair.Key == key).Select(pair => pair.Role)
                .Should().BeEquivalentTo(Ask(watcher, questions => questions.RolesWithKey(key).ToArray()));
        }

        // An archived role grants nothing.
        var archived = _harbor.RolesByPack[HostCatalogue.OperatorPack];
        archived.Archive<SeatId>();
        _store.Seed(archived);
        Ask(watcher, questions => questions.RoleKeys(keys).Select(pair => pair.Role).ToArray()).Should().NotContain(archived.Id);
    }

    [Fact]
    public void UnitsUnder_is_the_unit_and_every_unit_below_it_in_the_callers_tenant()
    {
        var other = new HarborBuilder().Tenant(2, "quarry").Build();
        _store.Seed(other.Aggregates);
        var watcher = As(SeatAt(_harbor.South, HostCatalogue.WatcherPack));

        Ask(watcher, questions => questions.UnitsUnder(_harbor.Root).ToArray()).Should().BeEquivalentTo([_harbor.Root, _harbor.North, _harbor.NorthCoast, _harbor.South]);
        Ask(watcher, questions => questions.UnitsUnder(_harbor.North).ToArray())
            .Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast], "the tree is the tenant's to read, wherever the seat is placed in it");
        Ask(watcher, questions => questions.UnitsUnder(_harbor.NorthCoast).ToArray()).Should().Equal(_harbor.NorthCoast);
        Ask(HostCaller.SystemIn(_harbor.Tenant.Id), questions => questions.UnitsUnder(_harbor.North).ToArray()).Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast]);

        Ask(watcher, questions => questions.UnitsUnder(other.Root).ToArray()).Should().BeEmpty("a unit of another tenant answers nothing");
        Ask(watcher, questions => questions.UnitsUnder(OrganizationUnitId.CreateSequential()).ToArray()).Should().BeEmpty("nor does a unit of none");
    }
}
