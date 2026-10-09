namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Who holds a key at a unit: the active seats with a right for it that applies now and reaches the unit. A
/// seat is answered each holder whose grant it may read: itself, any other seat at a unit where it manages grants,
/// seats or units, and every one when it manages roles for the whole tenant. System work in the tenant is answered
/// every one, and nobody none.
/// </summary>
public class HoldersTests
{
    private readonly Harness _harness = Harness.OfHarbor();

    private OrganizationUnitId North => _harness.Harbor.North;

    private OrganizationUnitId NorthCoast => _harness.Harbor.NorthCoast;

    private OrganizationUnitId South => _harness.Harbor.South;

    /// <summary>Asks as <paramref name="caller"/>, over <paramref name="source"/> or the store's rows, and runs the answer while the caller is current.</summary>
    private SeatId[] Holders(ITenancyCaller caller, string key, OrganizationUnitId unit, ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>? source = null)
    {
        using (TenancyCallers.Begin(caller))
        {
            var answers = new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(_harness.Catalogue, _harness.Clock);
            return [.. answers.Over(source ?? _harness.Store, _harness.Store).SeatsHoldingAt(key, unit)];
        }
    }

    [Fact]
    public async Task SeatsHoldingAt_answers_active_holders_at_the_unit_and_above()
    {
        var ada = _harness.Administrator;
        var bert = await _harness.SeatAt("Bert", North, HostCatalogue.SupervisorPack);
        var cy = await _harness.SeatAt("Cy", NorthCoast, HostCatalogue.OperatorPack);
        var di = await _harness.SeatAt("Di", South, HostCatalogue.WatcherPack);
        var asAda = _harness.SeatCaller(ada);

        // Ada holds every key at the root, Bert the supervisor's at North, Cy the operator's at North Coast, Di the
        // watcher's at South: a key held above a unit is held at it, and one held beside or below it is not.
        Holders(asAda, HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([ada, bert, cy]);
        Holders(asAda, HostCatalogue.WidgetRead, North).Should().BeEquivalentTo([ada, bert]);
        Holders(asAda, HostCatalogue.WidgetRead, South).Should().BeEquivalentTo([ada, di]);
        Holders(asAda, HostCatalogue.WidgetRead, _harness.Harbor.Root).Should().Equal(ada);
        Holders(asAda, TenancyKeys.UnitsManage, NorthCoast).Should().BeEquivalentTo([ada, bert]);
        Holders(asAda, HostCatalogue.WidgetChange, South).Should().Equal([ada], "a watcher only looks");
        Holders(asAda, HostCatalogue.WidgetRead, OrganizationUnitId.CreateSequential()).Should().BeEmpty("a unit of no tenant");

        // A seat that holds a key twice over, at a unit and above it, is one holder.
        await _harness.Place(bert, NorthCoast);
        await _harness.Grant(bert, NorthCoast, HostCatalogue.WatcherPack);
        Holders(asAda, HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([ada, bert, cy]);

        // The key is one of the catalogue: a typo in code is told, as with every question.
        FluentActions.Invoking(() => Holders(asAda, "widget.polish", North)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task A_seat_barred_from_others_grants_learns_only_about_itself()
    {
        var ada = _harness.Administrator;
        var bert = await _harness.SeatAt("Bert", North, HostCatalogue.SupervisorPack);
        var cy = await _harness.SeatAt("Cy", NorthCoast, HostCatalogue.OperatorPack);
        var di = await _harness.SeatAt("Di", South, HostCatalogue.WatcherPack);

        // Cy works with widgets and manages nothing: where Cy holds the key the answer is Cy, and where not, nobody,
        // whoever holds it there.
        Holders(_harness.SeatCaller(cy), HostCatalogue.WidgetRead, NorthCoast).Should().Equal(cy);
        Holders(_harness.SeatCaller(cy), HostCatalogue.WidgetRead, North).Should().BeEmpty();
        Holders(_harness.SeatCaller(cy), TenancyKeys.UnitsManage, NorthCoast).Should().BeEmpty();
        Holders(_harness.SeatCaller(di), HostCatalogue.WidgetRead, NorthCoast).Should().BeEmpty();

        // Bert manages units, seats and grants at North: he reads the grants at North and below it, and so learns who holds
        // the key from there, Cy and himself; not Ada, whose grant is at the root above him, nor Di, beside him at South.
        Holders(_harness.SeatCaller(bert), HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([bert, cy]);
        Holders(_harness.SeatCaller(bert), HostCatalogue.WidgetRead, North).Should().Equal(bert);
        Holders(_harness.SeatCaller(bert), HostCatalogue.WidgetRead, South).Should().BeEmpty();

        // Each of the three keys does where it is held, and only there; the key to manage roles does for the whole
        // tenant only.
        foreach (var key in new[] { TenancyKeys.GrantsManage, TenancyKeys.SeatsManage, TenancyKeys.UnitsManage })
        {
            var desk = await _harness.BySystemWork(h => h.Roles.CreateAsync("Desk for " + key, string.Empty, [key], default));
            var keeper = await _harness.SeatAt("Keeper of " + key, South);
            await _harness.BySystemWork(h => h.Seats.GrantAsync(keeper, South, desk, until: null, reason: null, default));
            Holders(_harness.SeatCaller(keeper), HostCatalogue.WidgetRead, South).Should().Equal([di], "{0} is held at South, and Ada's grant is at the root", key);
            Holders(_harness.SeatCaller(keeper), HostCatalogue.WidgetRead, NorthCoast).Should().BeEmpty("{0} is held at South alone", key);
        }

        var roles = await _harness.BySystemWork(h => h.Roles.CreateAsync("Roles desk", string.Empty, [TenancyKeys.RolesManage], default));
        var below = await _harness.SeatAt("Ed", North);
        await _harness.BySystemWork(h => h.Seats.GrantAsync(below, North, roles, until: null, reason: null, default));
        Holders(_harness.SeatCaller(below), HostCatalogue.WidgetRead, NorthCoast).Should().BeEmpty("the role key held below the root changes no role");
        Holders(_harness.SeatCaller(below), TenancyKeys.RolesManage, NorthCoast).Should().Equal(below);

        var whole = await _harness.SeatAt("Fay", _harness.Harbor.Root);
        await _harness.BySystemWork(h => h.Seats.GrantAsync(whole, _harness.Harbor.Root, roles, until: null, reason: null, default));
        Holders(_harness.SeatCaller(whole), HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([ada, bert, cy]);

        // A key that manages access held only in the past, or only later, reads nobody's grants now.
        var former = await _harness.SeatAt("Gil", South);
        await _harness.Grant(former, South, HostCatalogue.SupervisorPack, until: FixedClock.Start.AddHours(1));
        Holders(_harness.SeatCaller(former), HostCatalogue.WidgetRead, South).Should().BeEquivalentTo([di, former], "while it lasts, Gil reads the grants at South");
        _harness.Clock.Advance(TimeSpan.FromHours(2));
        Holders(_harness.SeatCaller(former), HostCatalogue.WidgetRead, South).Should().BeEmpty();
    }

    [Fact]
    public async Task System_work_learns_every_holder_in_its_tenant()
    {
        var ada = _harness.Administrator;
        var bert = await _harness.SeatAt("Bert", North, HostCatalogue.SupervisorPack);
        var cy = await _harness.SeatAt("Cy", NorthCoast, HostCatalogue.OperatorPack);
        var orchard = _harness.Seed(2, "orchard");

        Holders(_harness.SystemCaller(), HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([ada, bert, cy]);
        Holders(_harness.SystemCaller(actingSeat: cy), HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([ada, bert, cy], "system work acts for a seat, and is not held to what that seat may learn");

        // System work in another tenant learns that tenant's holders, and none of Harbor's; a seat of it none either.
        Holders(HostCaller.SystemIn(orchard.Tenant.Id), HostCatalogue.WidgetRead, NorthCoast).Should().BeEmpty();
        Holders(HostCaller.SystemIn(orchard.Tenant.Id), HostCatalogue.WidgetRead, orchard.Root).Should().Equal(orchard.Administrator.Id);
        Holders(HostCaller.InSeat(orchard.Tenant.Id, orchard.Administrator.Id), HostCatalogue.WidgetRead, NorthCoast).Should().BeEmpty();
        Holders(_harness.SystemCaller(), HostCatalogue.WidgetRead, NorthCoast, _harness.Store.Unfiltered).Should().BeEquivalentTo([ada, bert, cy], "the question keeps to the caller's tenant itself");

        // Nobody learns nothing, and system work outside any tenant has nothing to ask.
        Holders(HostCaller.Nobody(TenancyRefusals.NotSeated), HostCatalogue.WidgetRead, NorthCoast).Should().BeEmpty();
        FluentActions.Invoking(() => Holders(HostCaller.System, HostCatalogue.WidgetRead, NorthCoast)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task A_suspended_seat_and_a_grant_that_has_ended_hold_the_key_no_longer()
    {
        var ada = _harness.Administrator;
        var asAda = _harness.SeatCaller(ada);
        var bert = await _harness.SeatAt("Bert", North, HostCatalogue.SupervisorPack);
        var cy = await _harness.SeatAt("Cy", NorthCoast);
        await _harness.Grant(cy, NorthCoast, HostCatalogue.OperatorPack, until: FixedClock.Start.AddDays(1));
        var di = await _harness.SeatAt("Di", NorthCoast);
        await _harness.Grant(di, NorthCoast, HostCatalogue.WatcherPack, from: FixedClock.Start.AddDays(2));
        Holders(asAda, HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([ada, bert, cy], "Di's grant has not started");

        // A seat that is suspended holds nothing, and holds it again once it is active.
        await _harness.BySystemWork(h => h.Seats.SuspendAsync(bert, default));
        Holders(asAda, HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([ada, cy]);
        await _harness.BySystemWork(h => h.Seats.ReactivateAsync(bert, default));
        Holders(asAda, HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([ada, bert, cy]);

        // A grant counts from its start until its end, with nothing written in between.
        _harness.Clock.Advance(TimeSpan.FromDays(3));
        Holders(asAda, HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([ada, bert, di], "Cy's grant has ended, and Di's has started");

        // An archived role gives nothing, and neither does a key the catalogue has retired.
        await _harness.BySystemWork(h => h.Roles.ArchiveAsync(_harness.RoleFromPack(HostCatalogue.WatcherPack), default));
        Holders(asAda, HostCatalogue.WidgetRead, NorthCoast).Should().BeEquivalentTo([ada, bert]);

        var retired = TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                Permissions = [.. HostCatalogue.Permissions.Select(permission => permission.Key == HostCatalogue.WidgetCreate ? permission with { Retired = true } : permission)],
                Packs = [.. HostCatalogue.Application.Packs.Select(pack => pack with { Keys = [.. pack.Keys.Where(key => key != HostCatalogue.WidgetCreate)] })],
            },
            []);
        Holders(asAda, HostCatalogue.WidgetCreate, NorthCoast).Should().BeEquivalentTo([ada, bert]);
        using (TenancyCallers.Begin(asAda))
        {
            new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(retired, _harness.Clock).Over(_harness.Store, _harness.Store)
                .SeatsHoldingAt(HostCatalogue.WidgetCreate, NorthCoast).Should().BeEmpty("the rights for it are still stored, and a retired key holds nowhere");
        }
    }

    [Fact]
    public async Task A_source_that_answers_for_itself_is_asked_for_a_seat_and_not_for_system_work()
    {
        var bert = await _harness.SeatAt("Bert", North, HostCatalogue.SupervisorPack);
        var cy = await _harness.SeatAt("Cy", NorthCoast, HostCatalogue.OperatorPack);
        var answering = new Answering(_harness.Store, [cy]);

        // A storage that shows a seat only its own rights says who holds the key itself: the question passes its answer
        // on for a seat, whatever the rows would say.
        Holders(_harness.SeatCaller(bert), HostCatalogue.WidgetRead, NorthCoast, answering).Should().Equal(cy);
        answering.Asked.Should().Equal((HostCatalogue.WidgetRead, NorthCoast));

        // System work in the tenant reads every right of it, and is answered from the rows; nobody is answered nothing,
        // and a key that is retired or unknown never reaches the storage.
        Holders(_harness.SystemCaller(), HostCatalogue.WidgetRead, NorthCoast, answering).Should().BeEquivalentTo([_harness.Administrator, bert, cy]);
        Holders(HostCaller.Nobody(TenancyRefusals.NotSeated), HostCatalogue.WidgetRead, NorthCoast, answering).Should().BeEmpty();
        FluentActions.Invoking(() => Holders(_harness.SeatCaller(bert), "widget.polish", NorthCoast, answering)).Should().Throw<ArgumentException>();
        answering.Asked.Should().ContainSingle();

        // A storage without an answer of its own leaves it to the question.
        ((ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>)_harness.Store).SeatsHoldingAt(HostCatalogue.WidgetRead, NorthCoast).Should().BeNull();
    }

    /// <summary>The store's rows, with an answer of its own to who holds a key at a unit.</summary>
    private sealed class Answering(ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId> rows, SeatId[] holders)
        : ITenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>
    {
        public List<(string Key, OrganizationUnitId Unit)> Asked { get; } = [];

        public IQueryable<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>> SeatRights => rows.SeatRights;

        public IQueryable<OrganizationUnitPath<TenantId, OrganizationUnitId>> UnitPaths => rows.UnitPaths;

        public IQueryable<OrganizationUnitRow<TenantId, OrganizationUnitId>> Units => rows.Units;

        public IQueryable<RoleRow<TenantId, RoleId>> Roles => rows.Roles;

        public IQueryable<PlacementRow<SeatId, OrganizationUnitId>> Placements => rows.Placements;

        public IQueryable<SeatRow<TenantId, SeatId>> Seats => rows.Seats;

        public IQueryable<SeatId>? SeatsHoldingAt(string key, OrganizationUnitId unit)
        {
            Asked.Add((key, unit));
            return holders.AsQueryable();
        }
    }
}
