using System.Text.Json;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The directory tells a seat who it is and what it may do where, with every unit named by its path from the
/// root, lists the tenant's seats, roles and the units the caller reads, and answers what seats, roles and units
/// are called, by id, to whoever works in the tenant.
/// </summary>
public class DirectoryTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    [Fact]
    public async Task WhoAmI_lists_placements_roles_and_where_each_key_reaches_with_paths()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        await harness.Place(bert, harness.Harbor.South);
        await harness.Grant(bert, harness.Harbor.South, HostCatalogue.WatcherPack, until: Now.AddDays(7));

        var me = await harness.As(bert, h => h.Directory.WhoAmIAsync(default));

        me.Tenant.Should().Be(new HostTenancy.TenantSummary(harness.Tenant, "harbor", "Harbor Works", TenantShape.Hierarchical, TenantStatus.Active));
        me.Seat.Should().Be(new HostTenancy.SeatSummary(bert, "Bert", SeatStatus.Active));

        me.Placements.Select(placement => (placement.Unit.Path, placement.IsPrimary)).Should().Equal(
            ("Harbor Works / North", true), ("Harbor Works / South", false));
        var southGrant = me.Placements[1].Grants.Should().ContainSingle().Which;
        southGrant.Role.Should().Be("Watcher");
        southGrant.EndsAt.Should().Be(Now.AddDays(7));
        southGrant.AppliesNow.Should().BeTrue();

        me.Roles.Select(role => role.Name).Should().Equal("Supervisor", "Watcher");
        me.Keys.Select(key => key.Key).Should().Equal(
            TenancyKeys.GrantsManage, TenancyKeys.SeatsManage, TenancyKeys.UnitsManage,
            HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);

        var units = me.Keys.Single(key => key.Key == TenancyKeys.UnitsManage);
        units.WholeTenant.Should().BeFalse();
        units.GrantedAt.Select(unit => unit.Path).Should().Equal("Harbor Works / North");
        units.Reaches.Select(unit => unit.Path).Should().Equal("Harbor Works / North", "Harbor Works / North / North Coast");

        var read = me.Keys.Single(key => key.Key == HostCatalogue.WidgetRead);
        read.GrantedAt.Select(unit => unit.Id).Should().Equal(harness.Harbor.North, harness.Harbor.South);
        read.Reaches.Select(unit => unit.Path).Should().Equal("Harbor Works / North", "Harbor Works / North / North Coast", "Harbor Works / South");

        var administrator = await harness.As(harness.Administrator, h => h.Directory.WhoAmIAsync(default));
        administrator.Keys.Should().HaveCount(harness.Catalogue.LiveKeys.Count).And.OnlyContain(key => key.WholeTenant && key.Reaches.Count == 4);

        harness.Clock.Advance(TimeSpan.FromDays(7));
        var later = await harness.As(bert, h => h.Directory.WhoAmIAsync(default));
        later.Placements[1].Grants.Single().AppliesNow.Should().BeFalse();
        later.Keys.Single(key => key.Key == HostCatalogue.WidgetRead).GrantedAt.Select(unit => unit.Id).Should().Equal(harness.Harbor.North);
    }

    [Fact]
    public async Task WhoAmI_is_self_only_and_refused_to_system_and_nobody()
    {
        var harness = Harness.OfHarbor();

        (await harness.As(harness.Administrator, h => h.Directory.WhoAmIAsync(default))).Seat.Id.Should().Be(harness.Administrator);

        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => harness.Run(harness.SystemCaller(actingSeat: harness.Administrator), h => h.Directory.WhoAmIAsync(default)));
        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => harness.Run(HostCaller.System, h => h.Directory.WhoAmIAsync(default)));
        await Refused.WithCodeAsync(TenancyRefusals.NotSeated,
            () => harness.Run(HostCaller.Nobody(TenancyRefusals.TenantRequired), h => h.Directory.WhoAmIAsync(default)),
            "who am I answers every caller that is not a seat alike");
        await Refused.WithCodeAsync(TenancyRefusals.NotSeated,
            () => harness.Run(HostCaller.Nobody(TenancyRefusals.NotSeated), h => h.Directory.ListSeatsAsync(default)));
    }

    [Fact]
    public async Task Unit_paths_run_from_the_root()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        await harness.BySystemWork(h => h.Organization.ArchiveUnitAsync(harness.Harbor.NorthCoast, default));

        var all = await harness.BySystemWork(h => h.Directory.ListUnitsAsync(default));

        all.Select(unit => (unit.Path, unit.Depth)).Should().Equal(
            ("Harbor Works", 1), ("Harbor Works / North", 2), ("Harbor Works / North / North Coast", 3), ("Harbor Works / South", 2));
        all.Single(unit => unit.Id == harness.Harbor.NorthCoast).Should().Match<HostTenancy.UnitSummary>(
            unit => unit.Status == UnitStatus.Archived && unit.ParentId == harness.Harbor.North);

        var mine = await harness.As(bert, h => h.Directory.ListUnitsAsync(default));
        mine.Select(unit => unit.Path).Should().Equal("Harbor Works / North", "Harbor Works / North / North Coast");

        var seats = await harness.As(bert, h => h.Directory.ListSeatsAsync(default));
        seats.Select(seat => seat.DisplayName).Should().Equal("Ada", "Bert");

        await harness.BySystemWork(h => h.Roles.ArchiveAsync(harness.RoleFromPack(HostCatalogue.WatcherPack), default));
        var roles = await harness.As(bert, h => h.Directory.ListRolesAsync(default));
        roles.Select(role => (role.Name, role.Status)).Should().Equal(
            ("Administrator", RoleStatus.Active), ("Operator", RoleStatus.Active), ("Supervisor", RoleStatus.Active), ("Watcher", RoleStatus.Archived));
        harness.Directory.Catalogue.Should().BeSameAs(harness.Catalogue);
    }

    [Fact]
    public async Task Roles_say_whether_they_manage_access()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack, HostCatalogue.WatcherPack);

        var roles = await harness.As(bert, h => h.Directory.ListRolesAsync(default));
        var me = await harness.As(bert, h => h.Directory.WhoAmIAsync(default));

        roles.Select(role => (role.Name, role.ManagesAccess)).Should().Equal(
            ("Administrator", true), ("Operator", false), ("Supervisor", true), ("Watcher", false));
        me.Roles.Select(role => (role.Name, role.ManagesAccess)).Should().Equal(("Supervisor", true), ("Watcher", false));

        await harness.BySystemWork(h => h.Roles.ArchiveAsync(harness.RoleFromPack(HostCatalogue.SupervisorPack), default));
        var archived = await harness.As(bert, h => h.Directory.ListRolesAsync(default));
        archived.Single(role => role.Name == "Supervisor").ManagesAccess.Should().BeFalse("an archived role grants nothing, so manages nothing");
        (await harness.As(bert, h => h.Directory.WhoAmIAsync(default))).Roles.Single(role => role.Name == "Supervisor").ManagesAccess.Should().BeFalse();
    }

    [Fact]
    public async Task WhoAmI_shows_no_key_retired_since_its_rights_were_written()
    {
        var gauge = new Permission("gauges.read", "Gauges", "Read gauges");
        var harness = Harness.OfHarbor(New.Catalogue(gauge));
        var retired = New.Catalogue(gauge with { Retired = true });

        var before = await harness.As(harness.Administrator, h => h.Directory.WhoAmIAsync(default));
        var after = await harness.As(harness.Administrator, h => new HostTenancy.TenancyDirectory(h.Store, retired, h.Clock).WhoAmIAsync(default));

        before.Keys.Should().Contain(key => key.Key == "gauges.read");
        harness.Store.SavedRights.Should().Contain(right => right.Key == "gauges.read", "the rows are still there");
        after.Keys.Select(key => key.Key).Should().Equal(retired.LiveKeys, "a retired key holds nowhere");
    }

    [Fact]
    public async Task Seats_by_id_are_named_and_only_those_of_the_callers_tenant()
    {
        var harness = Harness.OfHarbor();
        var orchard = harness.Seed(2, "orchard");
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        var cy = await harness.SeatAt("Cy", harness.Harbor.South);
        await harness.BySystemWork(h => h.Seats.SuspendAsync(cy, default));

        // Bert holds no key at all: whoever works in a tenant reads its names.
        var named = await harness.As(bert, h => h.Directory.SeatsByIdAsync([cy, harness.Administrator, orchard.Administrator.Id, SeatId.CreateSequential(), cy], default));

        named.Should().Equal(
            new HostTenancy.SeatSummary(harness.Administrator, "Ada", SeatStatus.Active),
            new HostTenancy.SeatSummary(cy, "Cy", SeatStatus.Suspended));
        harness.Store.Calls.Should().Equal(["ListSeatsAsync"], "one read, of the seats themselves; the rows the access questions read have no name");

        (await harness.As(bert, h => h.Directory.SeatsByIdAsync([orchard.Administrator.Id, SeatId.CreateSequential()], default)))
            .Should().BeEmpty("a seat of another tenant and no seat at all are left out alike, without a word");
    }

    [Fact]
    public async Task Units_by_id_come_with_their_path_whichever_the_caller_is_placed_under()
    {
        var harness = Harness.OfHarbor();
        var orchard = harness.Seed(2, "orchard");
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        await harness.BySystemWork(h => h.Organization.ArchiveUnitAsync(harness.Harbor.NorthCoast, default));

        (await harness.As(bert, h => h.Directory.ListUnitsAsync(default))).Select(unit => unit.Path)
            .Should().Equal(["Harbor Works / North", "Harbor Works / North / North Coast"], "the list is still the units the caller is placed under");

        var named = await harness.As(bert, h => h.Directory.UnitsByIdAsync(
            [harness.Harbor.South, harness.Harbor.NorthCoast, harness.Harbor.Root, orchard.North, OrganizationUnitId.CreateSequential(), harness.Harbor.South], default));

        named.Should().Equal(
            new HostTenancy.UnitSummary(harness.Harbor.Root, null, "Harbor Works", UnitStatus.Active, "Harbor Works", 1),
            new HostTenancy.UnitSummary(harness.Harbor.NorthCoast, harness.Harbor.North, "North Coast", UnitStatus.Archived, "Harbor Works / North / North Coast", 3),
            new HostTenancy.UnitSummary(harness.Harbor.South, harness.Harbor.Root, "South", UnitStatus.Active, "Harbor Works / South", 2));
        harness.Store.Calls.Should().NotContain("ListSeatsAsync").And.Contain("FindOrganizationAsync", "the names are the organization's own units'");

        (await harness.As(bert, h => h.Directory.UnitsByIdAsync([orchard.North, orchard.Root], default)))
            .Should().BeEmpty("another tenant's units are left out without a word");
    }

    [Fact]
    public async Task A_view_answers_the_applications_own_field_beside_the_summary_from_the_units_the_directory_read()
    {
        var harness = Harness.OfHarbor();
        var orchard = harness.Seed(2, "orchard");
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        var bay = await harness.BySystemWork(h => h.Organization.AddUnitAsync(
            harness.Harbor.North, "North Bay", default, configure: unit => unit.SetCostCentre("NB-104")));

        // The units the caller reads, as the form without a view answers them, each with its cost centre.
        var summaries = await harness.As(bert, h => h.Directory.ListUnitsAsync(default));
        var asked = harness.Store.Calls.ToList();
        var listed = await harness.As(bert, h => h.Directory.ListUnitsAsync((unit, own) => (Summary: unit, own.CostCentre), default));

        listed.Select(unit => unit.Summary).Should().Equal(summaries, "the view is handed the summary the directory answers anyway");
        listed.Select(unit => (unit.Summary.Name, unit.CostCentre)).Should().Equal(("North", null), ("North Bay", "NB-104"), ("North Coast", null));
        harness.Store.Calls.Should().Equal(asked, "the field comes from the units the directory read, with no read more");

        // By id, whichever unit of the tenant the caller is placed under; another tenant's are left out as before.
        var named = await harness.As(bert, h => h.Directory.UnitsByIdAsync(
            [harness.Harbor.South, bay, orchard.North], (unit, own) => (unit.Path, own.CostCentre), default));
        named.Should().Equal(("Harbor Works / North / North Bay", "NB-104"), ("Harbor Works / South", null));

        await FluentActions.Awaiting(() => harness.As(bert, h => h.Directory.ListUnitsAsync<string>(null!, default))).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => harness.As(bert, h => h.Directory.UnitsByIdAsync<string>([bay], null!, default))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Roles_by_id_say_whether_they_manage_access()
    {
        var harness = Harness.OfHarbor();
        var orchard = harness.Seed(2, "orchard");
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        var supervisor = harness.RoleFromPack(HostCatalogue.SupervisorPack);
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);
        var operators = harness.RoleFromPack(HostCatalogue.OperatorPack);
        await harness.BySystemWork(h => h.Roles.ArchiveAsync(operators, default));

        var named = await harness.As(bert, h => h.Directory.RolesByIdAsync(
            [operators, watcher, supervisor, orchard.AdministratorRole.Id, RoleId.CreateSequential()], default));

        named.Select(role => (role.Id, role.Name, role.Status, role.ManagesAccess)).Should().Equal(
            (supervisor, "Supervisor", RoleStatus.Active, true),
            (watcher, "Watcher", RoleStatus.Active, false),
            (operators, "Operator", RoleStatus.Archived, false));
        named[0].FromPack.Should().Be(HostCatalogue.SupervisorPack);
        named[0].Keys.Should().Contain(TenancyKeys.GrantsManage);
        harness.Store.Calls.Should().Equal(["ListRolesAsync"], "a tenant's roles are few: one read, and the ones asked for are picked from it");
    }

    [Fact]
    public async Task An_empty_list_of_ids_asks_nothing_and_a_caller_without_a_seat_is_still_refused()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);

        (await harness.As(bert, h => h.Directory.SeatsByIdAsync([], default))).Should().BeEmpty();
        harness.Store.Calls.Should().BeEmpty("no ids, no statement");
        (await harness.As(bert, h => h.Directory.RolesByIdAsync([], default))).Should().BeEmpty();
        harness.Store.Calls.Should().BeEmpty();
        (await harness.As(bert, h => h.Directory.UnitsByIdAsync([], default))).Should().BeEmpty();
        harness.Store.Calls.Should().BeEmpty();

        // The caller is asked about before the list is looked at: nobody is refused with its own code.
        foreach (var code in new[] { TenancyRefusals.NotSeated, TenancyRefusals.SeatSuspended, TenancyRefusals.TenantRequired, TenancyRefusals.TenantInactive })
        {
            await Refused.WithCodeAsync(code, () => harness.Run(HostCaller.Nobody(code), h => h.Directory.SeatsByIdAsync([], default)));
            await Refused.WithCodeAsync(code, () => harness.Run(HostCaller.Nobody(code), h => h.Directory.RolesByIdAsync([], default)));
            await Refused.WithCodeAsync(code, () => harness.Run(HostCaller.Nobody(code), h => h.Directory.UnitsByIdAsync([], default)));
            harness.Store.Calls.Should().BeEmpty("a refused caller reads nothing");
        }
    }

    [Fact]
    public async Task A_missing_list_of_ids_is_a_programming_error()
    {
        var harness = Harness.OfHarbor();

        await FluentActions.Awaiting(() => harness.As(harness.Administrator, h => h.Directory.SeatsByIdAsync(null!, default))).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => harness.As(harness.Administrator, h => h.Directory.RolesByIdAsync(null!, default))).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => harness.As(harness.Administrator, h => h.Directory.UnitsByIdAsync(null!, default))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task More_ids_than_a_question_takes_are_refused()
    {
        var harness = Harness.OfHarbor();
        var most = HostTenancy.TenancyDirectory.MostIds;
        most.Should().Be(200);

        var seats = Enumerable.Range(0, most + 1).Select(_ => SeatId.CreateSequential()).ToArray();
        var roles = Enumerable.Range(0, most + 1).Select(_ => RoleId.CreateSequential()).ToArray();
        var units = Enumerable.Range(0, most + 1).Select(_ => OrganizationUnitId.CreateSequential()).ToArray();

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.TooManyIds, () => harness.As(harness.Administrator, h => h.Directory.SeatsByIdAsync(seats, default)));
        refusal.Arguments["Max"].Should().Be(most);
        refusal.Message.Should().Be("Ask for at most 200 ids at a time.");
        await Refused.WithCodeAsync(TenancyRefusals.TooManyIds, () => harness.As(harness.Administrator, h => h.Directory.RolesByIdAsync(roles, default)));
        await Refused.WithCodeAsync(TenancyRefusals.TooManyIds, () => harness.As(harness.Administrator, h => h.Directory.UnitsByIdAsync(units, default)));
        harness.Store.Calls.Should().BeEmpty("a question that is refused reads nothing");

        // As many as a question takes, and the same id again and again: it is the different ids that count.
        (await harness.As(harness.Administrator, h => h.Directory.SeatsByIdAsync([.. seats[..most]], default))).Should().BeEmpty();
        (await harness.As(harness.Administrator, h => h.Directory.SeatsByIdAsync([.. seats[..(most - 1)], harness.Administrator, harness.Administrator], default)))
            .Select(seat => seat.DisplayName).Should().Equal("Ada");

        // The limit is told to a caller that may ask, and to no other.
        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => harness.Run(HostCaller.Nobody(TenancyRefusals.NotSeated), h => h.Directory.SeatsByIdAsync(seats, default)));
    }

    [Fact]
    public async Task System_work_in_a_tenant_reads_its_names()
    {
        var harness = Harness.OfHarbor();
        var orchard = harness.Seed(2, "orchard");

        (await harness.BySystemWork(h => h.Directory.SeatsByIdAsync([harness.Administrator, orchard.Administrator.Id], default)))
            .Select(seat => seat.DisplayName).Should().Equal("Ada");
        (await harness.BySystemWork(h => h.Directory.RolesByIdAsync([harness.Harbor.AdministratorRole.Id, orchard.AdministratorRole.Id], default)))
            .Select(role => role.Name).Should().Equal("Administrator");
        (await harness.BySystemWork(h => h.Directory.UnitsByIdAsync([harness.Harbor.NorthCoast, orchard.NorthCoast], default)))
            .Select(unit => unit.Path).Should().Equal("Harbor Works / North / North Coast");

        // Outside any tenant there is no tenant whose names to read.
        await FluentActions.Awaiting(() => harness.Run(HostCaller.System, h => h.Directory.SeatsByIdAsync([harness.Administrator], default))).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => harness.Run(HostCaller.System, h => h.Directory.RolesByIdAsync([], default))).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => harness.Run(HostCaller.System, h => h.Directory.UnitsByIdAsync([], default))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_question_by_id_never_answers_an_identity()
    {
        var harness = Harness.OfHarbor();
        var identity = Guid.NewGuid();
        var bert = await harness.BySystemWork(h => h.Seats.AddSeatAsync(identity, "Bert", default));
        var ada = harness.Store.Seat(harness.Administrator).Identity;

        var named = await harness.As(harness.Administrator, h => h.Directory.SeatsByIdAsync([bert, harness.Administrator], default));
        var listed = await harness.As(harness.Administrator, h => h.Directory.ListSeatsAsync(default));

        named.Select(seat => seat.DisplayName).Should().Equal("Ada", "Bert");
        foreach (var answer in new[] { JsonSerializer.Serialize(named), JsonSerializer.Serialize(listed) })
        {
            answer.Should().Contain("Bert").And.NotContain(identity.ToString()).And.NotContain(ada.ToString());
        }

        typeof(HostTenancy.SeatSummary).GetProperties().Select(property => property.Name).Should().BeEquivalentTo(["Id", "DisplayName", "Status"]);
    }
}
