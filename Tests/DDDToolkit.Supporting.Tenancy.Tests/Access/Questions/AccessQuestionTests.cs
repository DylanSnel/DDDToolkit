namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The access questions over read rows in memory: a key granted at a unit covers that unit's subtree and
/// nothing beside or above it, a key held at the root holds for the whole tenant, a grant counts only inside
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

    /// <summary>A new seat of Harbor, placed at <paramref name="unit"/> and granted the role of <paramref name="pack"/> there, saved.</summary>
    private HostSeat SeatAt(OrganizationUnitId unit, string? pack, DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        var seat = _harbor.NewSeat();
        seat.Place(unit, primary: true, Now, placedBy: null);
        if (pack is not null)
        {
            var role = _harbor.RolesByPack[pack];
            seat.Grant(unit, role.Id, role.Facts, GrantPeriod.Between(from ?? Now, until), null, null);
        }

        _store.Seed(seat);
        return seat;
    }

    /// <summary>Asks as <paramref name="caller"/>, over <paramref name="source"/> or the filtered store, and runs the answer while the caller is current.</summary>
    private T Ask<T>(HostCaller caller, Func<ITenancyQuestions<TenantId, SeatId, OrganizationUnitId, RoleId>, T> ask,
        ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>? source = null, TenancyCatalogue? catalogue = null)
    {
        using (TenancyCallers.Begin(caller))
        {
            var answers = new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(catalogue ?? _harbor.Catalogue, _clock);
            return ask(answers.Over(source ?? _store, _store));
        }
    }

    private OrganizationUnitId[] UnitsWhere(HostCaller caller, string key, ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>? source = null)
        => Ask(caller, questions => questions.UnitsWhereIHold(key).ToArray(), source);

    private bool HoldsAt(HostCaller caller, string key, OrganizationUnitId unit, ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>? source = null)
        => Ask(caller, questions => questions.HoldsAtAsync(key, unit, default).GetAwaiter().GetResult(), source);

    private bool HoldsTenantWide(HostCaller caller, string key)
        => Ask(caller, questions => questions.HoldsTenantWideAsync(key, default).GetAwaiter().GetResult());

    private HostCaller As(HostSeat seat) => HostCaller.InSeat(seat.TenantId, seat.Id);

    [Fact]
    public void A_key_held_at_a_unit_reaches_its_whole_subtree()
    {
        var supervisor = As(SeatAt(_harbor.North, HostCatalogue.SupervisorPack));

        UnitsWhere(supervisor, TenancyKeys.UnitsManage).Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast]);
        UnitsWhere(supervisor, HostCatalogue.WidgetRead).Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast], "an implied key reaches as far");
        UnitsWhere(As(_harbor.Administrator), TenancyKeys.UnitsManage)
            .Should().BeEquivalentTo([_harbor.Root, _harbor.North, _harbor.NorthCoast, _harbor.South]);
    }

    [Fact]
    public void It_does_not_reach_siblings_or_ancestors()
    {
        var supervisor = As(SeatAt(_harbor.North, HostCatalogue.SupervisorPack));

        UnitsWhere(supervisor, TenancyKeys.UnitsManage).Should().NotContain([_harbor.Root, _harbor.South]);
        HoldsAt(supervisor, TenancyKeys.UnitsManage, _harbor.South).Should().BeFalse();
        HoldsAt(supervisor, TenancyKeys.UnitsManage, _harbor.Root).Should().BeFalse();
        UnitsWhere(supervisor, TenancyKeys.RolesManage).Should().BeEmpty("the supervisor's role does not grant it");
    }

    [Fact]
    public void HoldsTenantWideAsync_only_for_a_key_held_at_the_root()
    {
        var supervisor = As(SeatAt(_harbor.North, HostCatalogue.SupervisorPack));
        var administrator = As(_harbor.Administrator);

        HoldsTenantWide(supervisor, TenancyKeys.UnitsManage).Should().BeFalse("a key held below the root is not tenant-wide, however far it reaches");
        HoldsTenantWide(administrator, TenancyKeys.UnitsManage).Should().BeTrue();
        HoldsTenantWide(administrator, TenancyKeys.RolesManage).Should().BeTrue();
        HoldsTenantWide(As(SeatAt(_harbor.Root, HostCatalogue.WatcherPack)), HostCatalogue.WidgetRead).Should().BeTrue();
        HoldsTenantWide(As(SeatAt(_harbor.Root, HostCatalogue.WatcherPack)), HostCatalogue.WidgetChange).Should().BeFalse();
    }

    [Fact]
    public void HoldsAtAsync_answers_for_one_unit()
    {
        var supervisor = As(SeatAt(_harbor.North, HostCatalogue.SupervisorPack));

        HoldsAt(supervisor, TenancyKeys.UnitsManage, _harbor.North).Should().BeTrue();
        HoldsAt(supervisor, TenancyKeys.UnitsManage, _harbor.NorthCoast).Should().BeTrue();
        HoldsAt(supervisor, HostCatalogue.WidgetCreate, _harbor.NorthCoast).Should().BeTrue();
        HoldsAt(supervisor, TenancyKeys.RolesManage, _harbor.North).Should().BeFalse();
        HoldsAt(supervisor, TenancyKeys.UnitsManage, OrganizationUnitId.CreateSequential()).Should().BeFalse("a unit of no tenant");
    }

    [Fact]
    public void An_expired_grant_answers_nothing_without_any_write()
    {
        var worker = As(SeatAt(_harbor.North, HostCatalogue.OperatorPack, until: Now.AddHours(1)));
        var rows = _store.SavedRights.Count;

        UnitsWhere(worker, HostCatalogue.WidgetChange).Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast]);

        _clock.Advance(TimeSpan.FromHours(1));

        UnitsWhere(worker, HostCatalogue.WidgetChange).Should().BeEmpty("a grant no longer applies at the moment it ends");
        HoldsAt(worker, HostCatalogue.WidgetChange, _harbor.North).Should().BeFalse();
        Ask(worker, questions => questions.KeysIHoldAt(_harbor.North).ToArray()).Should().BeEmpty();
        _store.SaveCount.Should().Be(0, "nothing was saved to make the grant expire");
        _store.SavedRights.Should().HaveCount(rows, "the rows stay; they are read against the clock");
    }

    [Fact]
    public void A_future_grant_answers_nothing_yet()
    {
        var worker = As(SeatAt(_harbor.North, HostCatalogue.OperatorPack, from: Now.AddDays(1)));

        UnitsWhere(worker, HostCatalogue.WidgetChange).Should().BeEmpty();
        HoldsAt(worker, HostCatalogue.WidgetChange, _harbor.North).Should().BeFalse();

        _clock.Advance(TimeSpan.FromDays(1));

        UnitsWhere(worker, HostCatalogue.WidgetChange).Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast]);
    }

    [Fact]
    public void Rights_at_an_archived_unit_keep_working()
    {
        var worker = As(SeatAt(_harbor.NorthCoast, HostCatalogue.OperatorPack));

        _harbor.Organization.ArchiveUnit<SeatId>(_harbor.NorthCoast);
        _store.Seed(_harbor.Organization);

        _store.Organization(_harbor.Tenant.Id).IsActiveUnit(_harbor.NorthCoast).Should().BeFalse();
        HoldsAt(worker, HostCatalogue.WidgetChange, _harbor.NorthCoast).Should().BeTrue("a unit is archived, not taken away from the people who work there");
        UnitsWhere(worker, HostCatalogue.WidgetChange).Should().Equal(_harbor.NorthCoast);
        Ask(worker, questions => questions.ReadableUnits().ToArray()).Should().Equal(_harbor.NorthCoast);
    }

    [Fact]
    public void RolesWithKey_lists_active_roles_only()
    {
        var administrator = As(_harbor.Administrator);
        var roles = _harbor.RolesByPack;

        Ask(administrator, questions => questions.RolesWithKey(TenancyKeys.UnitsManage).ToArray())
            .Should().BeEquivalentTo([roles[HostCatalogue.AdministratorPack].Id, roles[HostCatalogue.SupervisorPack].Id]);
        Ask(administrator, questions => questions.RolesWithKey(HostCatalogue.WidgetRead).ToArray())
            .Should().HaveCount(4, "every pack of the host grants seeing widgets");

        roles[HostCatalogue.WatcherPack].Archive<SeatId>();
        _store.Seed(roles[HostCatalogue.WatcherPack]);

        Ask(administrator, questions => questions.RolesWithKey(HostCatalogue.WidgetRead).ToArray())
            .Should().NotContain(roles[HostCatalogue.WatcherPack].Id).And.HaveCount(3);
    }

    [Fact]
    public void ReadableUnits_are_the_subtrees_of_my_placements()
    {
        var seat = SeatAt(_harbor.North, pack: null);
        seat.Place(_harbor.South, primary: false, Now, placedBy: null);
        _store.Seed(seat);

        Ask(As(seat), questions => questions.ReadableUnits().ToArray())
            .Should().BeEquivalentTo([_harbor.North, _harbor.NorthCoast, _harbor.South], "a placement is where a seat belongs, with or without a role");
        UnitsWhere(As(seat), HostCatalogue.WidgetRead).Should().BeEmpty("belonging somewhere grants no key");
        Ask(As(_harbor.Administrator), questions => questions.ReadableUnits().ToArray()).Should().HaveCount(4);
    }

    [Fact]
    public void KeysIHoldAt_lists_the_keys_reaching_that_unit()
    {
        var seat = SeatAt(_harbor.North, HostCatalogue.SupervisorPack);
        var watcher = _harbor.RolesByPack[HostCatalogue.WatcherPack];
        seat.Place(_harbor.South, primary: false, Now, placedBy: null);
        seat.Grant(_harbor.South, watcher.Id, watcher.Facts, GrantPeriod.Open(Now), null, null);
        _store.Seed(seat);

        Ask(As(seat), questions => questions.KeysIHoldAt(_harbor.NorthCoast).ToArray()).Should().BeEquivalentTo(
        [
            TenancyKeys.GrantsManage, TenancyKeys.SeatsManage, TenancyKeys.UnitsManage,
            HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead,
        ]);
        Ask(As(seat), questions => questions.KeysIHoldAt(_harbor.South).ToArray()).Should().Equal(HostCatalogue.WidgetRead);
        Ask(As(seat), questions => questions.KeysIHoldAt(_harbor.Root).ToArray()).Should().BeEmpty();
    }

    [Fact]
    public void Nobody_holds_nothing()
    {
        var nobody = HostCaller.Nobody(TenancyRefusals.NotSeated);

        UnitsWhere(nobody, HostCatalogue.WidgetRead).Should().BeEmpty();
        HoldsAt(nobody, HostCatalogue.WidgetRead, _harbor.Root).Should().BeFalse();
        HoldsTenantWide(nobody, TenancyKeys.RolesManage).Should().BeFalse();
        Ask(nobody, questions => questions.ReadableUnits().ToArray()).Should().BeEmpty();
        Ask(nobody, questions => questions.RolesWithKey(HostCatalogue.WidgetRead).ToArray()).Should().BeEmpty();
        Ask(nobody, questions => questions.KeysIHoldAt(_harbor.Root).ToArray()).Should().BeEmpty();
        Ask(nobody, questions => questions.Units().ToArray()).Should().BeEmpty();
        Ask(nobody, questions => questions.Roles().ToArray()).Should().BeEmpty();
        Ask(nobody, questions => questions.Seats().ToArray()).Should().BeEmpty();

        using (TenancyCallers.Begin(nobody))
        {
            var answers = new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(_harbor.Catalogue, _clock);
            Refused.With(TenancyRefusals.NotSeated, () => answers.RequireTenant());
        }
    }

    [Fact]
    public void SystemInTenant_holds_everything_in_its_own_tenant_only()
    {
        var other = new HarborBuilder().Tenant(2, "quarry").Build();
        _store.Seed(other.Aggregates);
        var system = HostCaller.SystemIn(_harbor.Tenant.Id);
        var harborUnits = new[] { _harbor.Root, _harbor.North, _harbor.NorthCoast, _harbor.South };

        foreach (var source in new[] { _store.Filtered, _store.Unfiltered })
        {
            UnitsWhere(system, HostCatalogue.WidgetCreate, source).Should().BeEquivalentTo(harborUnits);
            HoldsAt(system, TenancyKeys.RolesManage, _harbor.NorthCoast, source).Should().BeTrue();
            HoldsAt(system, TenancyKeys.RolesManage, other.NorthCoast, source).Should().BeFalse("system work in one tenant holds nothing in another");
            Ask(system, questions => questions.Units().Select(unit => unit.Id).ToArray(), source).Should().BeEquivalentTo(harborUnits);
            Ask(system, questions => questions.Roles().ToArray(), source).Should().OnlyContain(role => role.TenantId == _harbor.Tenant.Id);
            Ask(system, questions => questions.Seats().ToArray(), source).Should().OnlyContain(seat => seat.TenantId == _harbor.Tenant.Id);
        }

        HoldsTenantWide(system, TenancyKeys.RolesManage).Should().BeTrue();
        Ask(system, questions => questions.KeysIHoldAt(_harbor.North).ToArray()).Should().BeEquivalentTo(_harbor.Catalogue.LiveKeys);

        // A seat's own questions keep to its tenant too, even over rows that are not filtered.
        UnitsWhere(As(_harbor.Administrator), TenancyKeys.UnitsManage, _store.Unfiltered).Should().BeEquivalentTo(harborUnits);

        using (TenancyCallers.Begin(system))
        {
            new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(_harbor.Catalogue, _clock).RequireTenant()
                .Should().Be(new TenantInScope<TenantId, SeatId>(_harbor.Tenant.Id, null, BySystem: true));
        }
    }

    [Fact]
    public void Global_system_asking_is_a_programming_error()
    {
        var system = HostCaller.System;

        var questions = new Action[]
        {
            () => UnitsWhere(system, HostCatalogue.WidgetRead),
            () => HoldsAt(system, HostCatalogue.WidgetRead, _harbor.Root),
            () => HoldsTenantWide(system, HostCatalogue.WidgetRead),
            () => Ask(system, ask => ask.ReadableUnits()),
            () => Ask(system, ask => ask.RolesWithKey(HostCatalogue.WidgetRead)),
            () => Ask(system, ask => ask.KeysIHoldAt(_harbor.Root)),
            () => Ask(system, ask => ask.Units()),
            () => Ask(system, ask => ask.Roles()),
            () => Ask(system, ask => ask.Seats()),
        };

        foreach (var question in questions)
        {
            question.Should().Throw<InvalidOperationException>().WithMessage("*BeginSystemIn*");
        }

        using (TenancyCallers.Begin(system))
        {
            FluentActions.Invoking(() => new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(_harbor.Catalogue, _clock).RequireTenant())
                .Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void An_unknown_key_is_a_programming_error()
    {
        var administrator = As(_harbor.Administrator);

        FluentActions.Invoking(() => UnitsWhere(administrator, "widget.fly")).Should().Throw<ArgumentException>().WithMessage("*widget.fly*");
        FluentActions.Invoking(() => HoldsTenantWide(administrator, "widget.fly")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => HoldsAt(administrator, "widget.fly", _harbor.Root)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Ask(administrator, questions => questions.RolesWithKey("widget.fly"))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => UnitsWhere(HostCaller.Nobody(TenancyRefusals.NotSeated), "widget.fly"))
            .Should().Throw<ArgumentException>("a typo is a typo, whoever runs it");
    }

    [Fact]
    public void A_retired_key_is_askable_and_holds_nowhere()
    {
        var gauge = new Permission("gauges.read", "Gauges", "Read gauges");
        var harbor = new HarborBuilder().With(New.Catalogue(gauge)).Build();
        var store = new InMemoryTenancyStore(harbor.Catalogue);
        store.Seed(harbor.Aggregates);
        var retired = New.Catalogue(gauge with { Retired = true });
        var administrator = HostCaller.InSeat(harbor.Tenant.Id, harbor.Administrator.Id);

        using (TenancyCallers.Begin(administrator))
        {
            var live = new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(harbor.Catalogue, _clock).Over(store, store);
            var now = new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(retired, _clock).Over(store, store);

            live.UnitsWhereIHold("gauges.read").Should().HaveCount(4, "the administrators' role was made while the key was live");
            now.UnitsWhereIHold("gauges.read").Should().BeEmpty("the rows are still there, and the key is retired");
            now.RolesWithKey("gauges.read").Should().BeEmpty();
            live.KeysIHoldAt(harbor.North).Should().Contain("gauges.read");
            now.KeysIHoldAt(harbor.North).Should().NotContain("gauges.read").And.Contain(TenancyKeys.AdministratorKey);
        }
    }
}
