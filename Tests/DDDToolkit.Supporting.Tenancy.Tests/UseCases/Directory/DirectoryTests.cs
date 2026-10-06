using System.Text.Json;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// The directory tells a seat who it is and what it may do where, with every unit named by its path from the
/// root, lists the tenant's seats, roles and the units the caller reads, and answers what seats, roles and units
/// are called, by id, to whoever works in the tenant. Seats and units come as the host's own classes, whole and
/// tracked by nobody, with only what is not on them beside them.
/// </summary>
public class DirectoryTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    [Fact]
    public async Task WhoAmI_hands_the_hosts_own_seat_with_the_paths_roles_and_keys_that_are_not_on_it()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        await harness.Place(bert, harness.Harbor.South);
        await harness.Grant(bert, harness.Harbor.South, HostCatalogue.WatcherPack, until: Now.AddDays(7));

        var me = await harness.As(bert, h => h.Directory.WhoAmIAsync(default));

        me.Tenant.Should().Be(new HostTenancy.TenantSummary(harness.Tenant, "harbor", "Harbor Works", TenantShape.Hierarchical, TenantStatus.Active));
        (me.Seat.Id, me.Seat.DisplayName, me.Seat.Status).Should().Be((bert, "Bert", SeatStatus.Active), "the name is the host's own field, read with the seat");
        me.Seat.Identity.Should().Be(harness.Store.Seat(bert).Identity);
        me.AsOf.Should().Be(Now);

        // Where it is placed is the seat's own; the paths and the role names are what is not on it.
        me.Units.Select(unit => (unit.Id, unit.Path)).Should().Equal((harness.Harbor.North, "Harbor Works / North"), (harness.Harbor.South, "Harbor Works / South"));
        me.Roles.Select(role => role.Name).Should().Equal("Supervisor", "Watcher");
        var south = me.Seat.Placements.Single(placement => placement.UnitId == harness.Harbor.South);
        south.IsPrimary.Should().BeFalse();
        var watcher = south.Grants.Should().ContainSingle().Which;
        me.RoleOf(watcher.RoleId)?.Name.Should().Be("Watcher");
        me.UnitOf(south.UnitId).Path.Should().Be("Harbor Works / South");
        watcher.EndsAt.Should().Be(Now.AddDays(7));
        watcher.AppliesAt(me.AsOf).Should().BeTrue();

        // What a host shows is a plain select over the seat, with the paths and role names beside it.
        me.Seat.Placements
            .OrderByDescending(placement => placement.IsPrimary)
            .Select(placement => (me.UnitOf(placement.UnitId).Path, placement.IsPrimary, Roles: string.Join(", ", placement.Grants.Select(grant => me.RoleOf(grant.RoleId)?.Name))))
            .Should().Equal(("Harbor Works / North", true, "Supervisor"), ("Harbor Works / South", false, "Watcher"));

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
        later.AsOf.Should().Be(Now.AddDays(7));
        later.Seat.Placements.Single(placement => placement.UnitId == harness.Harbor.South).Grants.Single().AppliesAt(later.AsOf).Should().BeFalse();
        later.Roles.Select(role => role.Name).Should().Equal(["Supervisor", "Watcher"], "the roles its grants name, whether or not a grant applies now");
        later.Keys.Single(key => key.Key == HostCatalogue.WidgetRead).GrantedAt.Select(unit => unit.Id).Should().Equal(harness.Harbor.North);
    }

    [Fact]
    public async Task An_overview_names_the_units_and_roles_of_the_seats_own_placements_and_grants_and_no_others()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.WatcherPack);

        var me = await harness.As(bert, h => h.Directory.WhoAmIAsync(default));

        me.Units.Select(unit => unit.Id).Should().Equal(harness.Harbor.North);
        me.Roles.Select(role => role.Id).Should().Equal(harness.RoleFromPack(HostCatalogue.WatcherPack));
        FluentActions.Invoking(() => me.UnitOf(harness.Harbor.South)).Should().Throw<KeyNotFoundException>("the seat is not placed at South, so the overview read nothing of it");
        FluentActions.Invoking(() => me.RoleOf(harness.RoleFromPack(HostCatalogue.SupervisorPack))).Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public async Task A_grant_whose_role_the_store_does_not_answer_is_still_shown_with_no_role_made_up()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack, HostCatalogue.WatcherPack);
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);

        // A filter of the host's own on its role class, a soft delete say, hides the role; Bert's grant still names it.
        harness.Store.HiddenRoles.Add(watcher);
        var me = await harness.As(bert, h => h.Directory.WhoAmIAsync(default));

        me.Roles.Select(role => role.Name).Should().Equal(["Supervisor"], "a role the store does not answer is left out rather than made up");
        me.Seat.Placements.Single().Grants.Select(grant => grant.RoleId).Should().Contain(watcher, "the grant is the seat's own");
        me.RoleOf(watcher).Should().BeNull("a grant of the seat names it, so it is no programming error to ask");
        me.Seat.Placements.Single().Grants
            .Select(grant => me.RoleOf(grant.RoleId)?.Name ?? "(a role no longer shown)")
            .Should().BeEquivalentTo(["Supervisor", "(a role no longer shown)"], "the host shows every grant, with what it chooses for a role it cannot name");
    }

    [Fact]
    public async Task What_is_done_to_a_seat_or_a_unit_the_directory_answered_is_never_saved()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);

        // Each answer is read for the question and tracked by nobody: a save later in the same unit of work writes
        // nothing done to it.
        await harness.As(bert, async h =>
        {
            (await h.Directory.WhoAmIAsync(default)).Seat.Rename("changed by the overview");
            foreach (var seat in await h.Directory.ListSeatsAsync(default))
            {
                seat.Rename("changed by the list");
            }

            foreach (var seat in await h.Directory.SeatsByIdAsync([bert, harness.Administrator], default))
            {
                seat.ChangeJobTitle("changed by the question by id");
            }

            foreach (var unit in await h.Directory.ListUnitsAsync(default))
            {
                unit.Unit.SetCostCentre("XX-999");
            }

            await h.Store.SaveAsync(default);
        });

        harness.Store.Seat(bert).DisplayName.Should().Be("Bert");
        harness.Store.Seat(harness.Administrator).DisplayName.Should().Be("Ada");
        harness.Store.Seat(bert).JobTitle.Should().BeNull();
        harness.Store.Organization(harness.Tenant).Units.Select(unit => unit.CostCentre).Should().OnlyContain(costCentre => costCentre == null);
        harness.Store.Calls.Should().Contain("ReadOrganizationAsync").And.NotContain("FindOrganizationAsync", "the directory reads the organization, and loads it for no save");
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
    public async Task Units_come_whole_with_their_path_and_depth_from_the_root()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        await harness.BySystemWork(h => h.Organization.ArchiveUnitAsync(harness.Harbor.NorthCoast, default));

        var all = await harness.BySystemWork(h => h.Directory.ListUnitsAsync(default));

        all.Select(unit => (unit.Path, unit.Depth)).Should().Equal(
            ("Harbor Works", 1), ("Harbor Works / North", 2), ("Harbor Works / North / North Coast", 3), ("Harbor Works / South", 2));
        all.Single(unit => unit.Unit.Id == harness.Harbor.NorthCoast).Unit.Should().Match<HostUnit>(
            unit => unit.Status == UnitStatus.Archived && unit.ParentId == harness.Harbor.North && unit.Name == "North Coast", "the host's own unit, whole");

        var mine = await harness.As(bert, h => h.Directory.ListUnitsAsync(default));
        mine.Select(unit => unit.Path).Should().Equal("Harbor Works / North", "Harbor Works / North / North Coast");

        var seats = await harness.As(bert, h => h.Directory.ListSeatsAsync(default));
        seats.Select(seat => seat.DisplayName).Should().BeEquivalentTo(["Ada", "Bert"]);

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
    public async Task Seats_by_id_are_the_hosts_own_and_only_those_of_the_callers_tenant()
    {
        var harness = Harness.OfHarbor();
        var orchard = harness.Seed(2, "orchard");
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        var cy = await harness.SeatAt("Cy", harness.Harbor.South);
        await harness.BySystemWork(h => h.Seats.SuspendAsync(cy, default));

        // Bert holds no key at all: whoever works in a tenant reads its seats. The name is the host's own field, on
        // the seats the directory read.
        var seats = await harness.As(bert, h => h.Directory.SeatsByIdAsync([cy, harness.Administrator, orchard.Administrator.Id, SeatId.CreateSequential(), cy], default));

        seats.Select(seat => (seat.Id, seat.DisplayName, seat.Status)).Should().BeEquivalentTo([(harness.Administrator, "Ada", SeatStatus.Active), (cy, "Cy", SeatStatus.Suspended)]);
        harness.Store.Calls.Should().Equal(["ListSeatsAsync"], "one read, of the seats themselves; the rows the access questions read have no name");

        (await harness.As(bert, h => h.Directory.SeatsByIdAsync([orchard.Administrator.Id, SeatId.CreateSequential()], default)))
            .Should().BeEmpty("a seat of another tenant and no seat at all are left out alike, without a word");
    }

    [Fact]
    public async Task Seats_come_whole_in_the_order_of_their_ids()
    {
        var harness = Harness.OfHarbor();
        var seats = new List<(SeatId Id, string? Name)> { (harness.Administrator, "Ada") };
        foreach (var name in new[] { "Zed", "Bert", "Mo" })
        {
            seats.Add((await harness.SeatAt(name, harness.Harbor.North), name));
        }

        var listed = await harness.As(harness.Administrator, h => h.Directory.ListSeatsAsync(default));

        listed.Select(seat => (seat.Id, seat.DisplayName)).Should().Equal(
            seats.OrderBy(seat => seat.Id), "a seat has nothing of Tenancy's a person would order it by, so the order is the ids', with the host's own field on each");
        listed.Select(seat => seat.Identity).Should().OnlyHaveUniqueItems().And.NotContain(Guid.Empty, "the host's own seat comes whole, its identity included");
        listed.Should().OnlyContain(seat => seat.Placements.Count == 1, "with where it is placed, as loading one reads it");
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

        named.Select(unit => (unit.Unit.Id, unit.Unit.ParentId, unit.Unit.Name, unit.Unit.Status, unit.Path, unit.Depth)).Should().Equal(
            (harness.Harbor.Root, (OrganizationUnitId?)null, "Harbor Works", UnitStatus.Active, "Harbor Works", 1),
            (harness.Harbor.NorthCoast, harness.Harbor.North, "North Coast", UnitStatus.Archived, "Harbor Works / North / North Coast", 3),
            (harness.Harbor.South, harness.Harbor.Root, "South", UnitStatus.Active, "Harbor Works / South", 2));
        harness.Store.Calls.Should().NotContain("ListSeatsAsync").And.Contain("ReadOrganizationAsync", "the names are the organization's own units'");

        (await harness.As(bert, h => h.Directory.UnitsByIdAsync([orchard.North, orchard.Root], default)))
            .Should().BeEmpty("another tenant's units are left out without a word");
    }

    [Fact]
    public async Task A_field_the_host_added_to_its_unit_comes_with_the_unit_from_the_one_read()
    {
        var harness = Harness.OfHarbor();
        var orchard = harness.Seed(2, "orchard");
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        var bay = await harness.BySystemWork(h => h.Organization.AddUnitAsync(
            harness.Harbor.North, "North Bay", default, configure: unit => unit.SetCostCentre("NB-104")));

        // The units the caller reads, each the host's own, with its cost centre: a plain select shows it.
        var listed = await harness.As(bert, h => h.Directory.ListUnitsAsync(default));

        listed.Select(unit => (unit.Unit.Name, unit.Unit.CostCentre)).Should().Equal(("North", null), ("North Bay", "NB-104"), ("North Coast", null));
        harness.Store.Calls.Where(call => call.EndsWith("Async", StringComparison.Ordinal) && call != "ListAsync")
            .Should().Equal(["ReadOrganizationAsync"], "the units come from the organization the directory read once; the closure and what the seat reads are queries over the read rows");

        // By id, whichever unit of the tenant the caller is placed under; another tenant's are left out as before.
        var named = await harness.As(bert, h => h.Directory.UnitsByIdAsync([harness.Harbor.South, bay, orchard.North], default));
        named.Select(unit => (unit.Path, unit.Unit.CostCentre)).Should().Equal(("Harbor Works / North / North Bay", "NB-104"), ("Harbor Works / South", null));
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
            .Select(seat => seat.Id).Should().Equal(harness.Administrator);

        // The limit is told to a caller that may ask, and to no other.
        await Refused.WithCodeAsync(TenancyRefusals.NotSeated, () => harness.Run(HostCaller.Nobody(TenancyRefusals.NotSeated), h => h.Directory.SeatsByIdAsync(seats, default)));
    }

    [Fact]
    public async Task System_work_in_a_tenant_reads_its_names()
    {
        var harness = Harness.OfHarbor();
        var orchard = harness.Seed(2, "orchard");

        (await harness.BySystemWork(h => h.Directory.SeatsByIdAsync([harness.Administrator, orchard.Administrator.Id], default)))
            .Select(seat => seat.Id).Should().Equal(harness.Administrator);
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
    public async Task What_leaves_of_a_seat_is_what_the_host_selects()
    {
        var harness = Harness.OfHarbor();
        var identity = Guid.NewGuid();
        var bert = await harness.BySystemWork(h => h.Seats.AddSeatAsync(identity, default, configure: seat => seat.Rename("Bert")));

        // The directory answers the host's seat whole, the identity with it: the host keeps it, and a select picks
        // what it shows.
        var seats = await harness.As(harness.Administrator, h => h.Directory.SeatsByIdAsync([bert, harness.Administrator], default));
        seats.Single(seat => seat.Id == bert).Identity.Should().Be(identity);

        var shown = JsonSerializer.Serialize(seats.Select(seat => new { seat.Id, seat.DisplayName, seat.Status }));

        shown.Should().Contain("Bert", "the host chose the name").And.NotContain(identity.ToString());
        shown.Should().NotContain(harness.Store.Seat(harness.Administrator).Identity.ToString());
    }
}
