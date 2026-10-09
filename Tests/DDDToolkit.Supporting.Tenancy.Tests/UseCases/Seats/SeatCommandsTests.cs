namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Seats are placed and granted where the caller holds the key. Whoever manages grants there gives and takes
/// away any role that manages no access, itself included for no longer than it manages grants, while a role
/// that manages access goes only from a seat that holds its keys that do, for long enough, and never to that
/// seat itself; suspending, deactivating or reactivating a seat that holds one follows the same rule. A grant a
/// seat makes starts now, nothing leaves the tenant without an administrator, and every
/// command that changes rights takes the access revision before it reads anything.
/// </summary>
public class SeatCommandsTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    [Fact]
    public async Task Placing_needs_seats_manage_at_that_unit()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var newcomer = await harness.BySystemWork(h => h.Seats.AddSeatAsync(Guid.NewGuid(), default));

        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => harness.As(supervisor, h => h.Seats.PlaceAsync(newcomer, harness.Harbor.South, true, default)));

        await harness.As(supervisor, h => h.Seats.PlaceAsync(newcomer, harness.Harbor.NorthCoast, true, default));

        var placement = harness.Store.Seat(newcomer).Placements.Should().ContainSingle().Which;
        placement.UnitId.Should().Be(harness.Harbor.NorthCoast);
        placement.PlacedBy.Should().Be(supervisor);
        placement.PlacedAt.Should().Be(Now);
        await Refused.WithCodeAsync(TenancyRefusals.SelfAssignment, () => harness.As(supervisor, h => h.Seats.PlaceAsync(supervisor, harness.Harbor.NorthCoast, false, default)));
    }

    [Fact]
    public async Task Placing_in_an_archived_unit_is_refused()
    {
        var harness = Harness.OfHarbor();
        var seat = await harness.SeatAt("Bert", harness.Harbor.North);
        await harness.BySystemWork(h => h.Organization.ArchiveUnitAsync(harness.Harbor.NorthCoast, default));

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.UnitNotActive,
            () => harness.As(harness.Administrator, h => h.Seats.PlaceAsync(seat, harness.Harbor.NorthCoast, false, default)));

        refusal.Arguments["Unit"].Should().Be(harness.Harbor.NorthCoast);
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(harness.Administrator, h => h.Seats.PlaceAsync(seat, OrganizationUnitId.CreateSequential(), false, default)),
            "a seat holds no key at a unit that is not there");
        await Refused.WithCodeAsync(TenancyRefusals.UnitNotFound,
            () => harness.BySystemWork(h => h.Seats.PlaceAsync(seat, OrganizationUnitId.CreateSequential(), false, default)));
        harness.Store.Seat(seat).Placements.Should().ContainSingle();
    }

    [Fact]
    public async Task Granting_needs_grants_manage_at_the_unit()
    {
        var harness = Harness.OfHarbor();
        var worker = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.OperatorPack);
        var supervisor = await harness.SeatAt("Cy", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var newcomer = await harness.SeatAt("Di", harness.Harbor.NorthCoast);
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(worker, h => h.Seats.GrantAsync(newcomer, harness.Harbor.NorthCoast, watcher, null, null, default)));
        refusal.Arguments["Key"].Should().Be(TenancyKeys.GrantsManage);

        await harness.As(supervisor, h => h.Seats.GrantAsync(newcomer, harness.Harbor.NorthCoast, watcher, Now.AddDays(7), " covers the pier ", default));

        var grant = harness.Store.Seat(newcomer).Placements.Single().Grants.Should().ContainSingle().Which;
        grant.RoleId.Should().Be(watcher);
        grant.GrantedBy.Should().Be(supervisor);
        grant.StartsAt.Should().Be(Now, "a grant a seat makes starts now");
        grant.EndsAt.Should().Be(Now.AddDays(7));
        grant.Reason.Should().Be("covers the pier");
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == newcomer && right.Key == HostCatalogue.WidgetRead && right.UnitId == harness.Harbor.NorthCoast);
    }

    [Fact]
    public async Task Granting_a_role_that_manages_access_needs_its_access_managing_keys_there()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var newcomer = await harness.SeatAt("Di", harness.Harbor.North);
        var administrators = harness.RoleFromPack(HostCatalogue.AdministratorPack);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(supervisor, h => h.Seats.GrantAsync(newcomer, harness.Harbor.North, administrators, null, null, default)));

        refusal.Arguments["Role"].Should().Be(administrators);
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.RolesManage + ", " + TenancyKeys.SettingsManage);
        refusal.Message.Should().Contain(TenancyKeys.RolesManage);
        harness.Store.Seat(newcomer).Placements.Single().Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task Granting_what_you_hold_at_an_ancestor_is_allowed()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var newcomer = await harness.SeatAt("Di", harness.Harbor.NorthCoast);

        // Supervisor manages access: its keys that do are held at North, above North Coast, for good.
        await harness.As(supervisor, h => h.Seats.GrantAsync(newcomer, harness.Harbor.NorthCoast, harness.RoleFromPack(HostCatalogue.SupervisorPack), null, null, default));

        harness.Store.Seat(newcomer).Placements.Single().Grants.Single().RoleId.Should().Be(harness.RoleFromPack(HostCatalogue.SupervisorPack));
    }

    [Fact]
    public async Task A_role_holding_a_retired_key_is_still_grantable()
    {
        var gauge = new Permission("gauges.read", "Gauges", "Read gauges");
        var gauges = New.Role(New.Catalogue(gauge), "Gauge watcher", "gauges.read", HostCatalogue.WidgetRead);
        var harness = Harness.OfHarbor(New.Catalogue(gauge with { Retired = true }));
        harness.Store.Seed(gauges);
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var newcomer = await harness.SeatAt("Di", harness.Harbor.North);

        await harness.As(supervisor, h => h.Seats.GrantAsync(newcomer, harness.Harbor.North, gauges.Id, null, null, default));

        harness.Store.SavedRights.Where(right => right.SeatId == newcomer).Select(right => right.Key)
            .Should().Equal([HostCatalogue.WidgetRead], "the retired key is carried, and conveys nothing");
    }

    [Fact]
    public async Task A_seat_caller_choosing_a_start_is_refused()
    {
        var harness = Harness.OfHarbor();
        var newcomer = await harness.SeatAt("Di", harness.Harbor.North);
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);

        await Refused.WithCodeAsync(TenancyRefusals.StartSystemOnly,
            () => harness.As(harness.Administrator, h => h.Seats.GrantAsync(newcomer, harness.Harbor.North, watcher, null, null, default, from: Now.AddDays(-30))));
        harness.Store.Calls.Should().BeEmpty("the refusal needs nothing from the store");

        await harness.Grant(newcomer, harness.Harbor.North, HostCatalogue.WatcherPack, from: Now.AddDays(-30));

        harness.Store.Seat(newcomer).Placements.Single().Grants.Single().StartsAt.Should().Be(Now.AddDays(-30), "imports and seeding choose when a grant started");
    }

    [Fact]
    public async Task SystemInTenant_skips_containment_but_not_last_admin()
    {
        var harness = Harness.OfHarbor();
        var newcomer = await harness.SeatAt("Di", harness.Harbor.Root);
        var administrators = harness.RoleFromPack(HostCatalogue.AdministratorPack);

        await harness.Run(harness.SystemCaller(actingSeat: harness.Administrator),
            h => h.Seats.GrantAsync(newcomer, harness.Harbor.Root, administrators, null, "second pair of hands", default));

        harness.Store.Calls.Should().NotContain(["Reads", "Queries"], "system work in a tenant asks nothing about its own keys");
        harness.Store.Seat(newcomer).Placements.Single().Grants.Single().GrantedBy.Should().Be(harness.Administrator, "the seat the work was done for is recorded");

        await harness.BySystemWork(h => h.Seats.RevokeAsync(harness.Administrator, harness.Harbor.Root, administrators, default));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.BySystemWork(h => h.Seats.RevokeAsync(newcomer, harness.Harbor.Root, administrators, default)));
        harness.Store.Seat(newcomer).Placements.Single().Grants.Should().ContainSingle("the refused revoke saved nothing");
    }

    [Fact]
    public async Task Revoking_a_role_that_manages_access_needs_its_access_managing_keys()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var deputy = await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.AdministratorPack, HostCatalogue.OperatorPack);

        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(supervisor, h => h.Seats.RevokeAsync(deputy, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.AdministratorPack), default)));

        await harness.As(supervisor, h => h.Seats.RevokeAsync(deputy, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.OperatorPack), default));

        harness.Store.Seat(deputy).Placements.Single().Grants.Select(grant => grant.RoleId).Should().Equal(harness.RoleFromPack(HostCatalogue.AdministratorPack));
        await Refused.WithCodeAsync(TenancyRefusals.GrantNotFound,
            () => harness.As(supervisor, h => h.Seats.RevokeAsync(deputy, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.OperatorPack), default)));
    }

    [Fact]
    public async Task Withdrawing_a_placement_needs_the_access_managing_keys_of_its_roles()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var deputy = await harness.SeatAt("Di", harness.Harbor.NorthCoast, HostCatalogue.AdministratorPack);
        var worker = await harness.SeatAt("Ed", harness.Harbor.NorthCoast, HostCatalogue.OperatorPack);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(supervisor, h => h.Seats.WithdrawAsync(deputy, harness.Harbor.NorthCoast, default)));
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.RolesManage + ", " + TenancyKeys.SettingsManage);
        refusal.Arguments["Role"].Should().Be(harness.RoleFromPack(HostCatalogue.AdministratorPack), "each grant is taken away by the rule for its role");

        await harness.As(supervisor, h => h.Seats.WithdrawAsync(worker, harness.Harbor.NorthCoast, default));

        harness.Store.Seat(deputy).Placements.Should().ContainSingle();
        harness.Store.Seat(worker).Placements.Should().BeEmpty();
        harness.Store.SavedRights.Should().NotContain(right => right.SeatId == worker, "withdrawing takes the placement's grants with it");
    }

    [Fact]
    public async Task Withdrawing_a_placement_with_grants_needs_grants_manage()
    {
        var harness = Harness.OfHarbor();
        var placer = await harness.BySystemWork(h => h.Roles.CreateAsync("Placer", "Places people", [TenancyKeys.SeatsManage, HostCatalogue.WidgetRead], default));
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        await harness.BySystemWork(h => h.Seats.GrantAsync(bert, harness.Harbor.North, placer, null, null, default));
        var granted = await harness.SeatAt("Di", harness.Harbor.NorthCoast, HostCatalogue.WatcherPack);
        var placedOnly = await harness.SeatAt("Ed", harness.Harbor.NorthCoast);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(bert, h => h.Seats.WithdrawAsync(granted, harness.Harbor.NorthCoast, default)));
        refusal.Arguments["Key"].Should().Be(TenancyKeys.GrantsManage);

        await harness.As(bert, h => h.Seats.WithdrawAsync(placedOnly, harness.Harbor.NorthCoast, default));
        harness.Store.Seat(placedOnly).Placements.Should().BeEmpty();
        await Refused.WithCodeAsync(TenancyRefusals.PlacementNotFound, () => harness.As(bert, h => h.Seats.WithdrawAsync(placedOnly, harness.Harbor.NorthCoast, default)));
    }

    [Fact]
    public async Task Revoking_the_last_administrators_grant_is_refused()
    {
        var harness = Harness.OfHarbor();
        var administrators = harness.RoleFromPack(HostCatalogue.AdministratorPack);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.LastAdmin,
            () => harness.As(harness.Administrator, h => h.Seats.RevokeAsync(harness.Administrator, harness.Harbor.Root, administrators, default)));

        refusal.Kind.Should().Be(Exceptions.RefusalKind.Conflict);
        harness.Store.Seat(harness.Administrator).Placements.Single().Grants.Should().ContainSingle();
        await harness.As(harness.Administrator, h => h.Roles.CreateAsync("Still in charge", string.Empty, [HostCatalogue.WidgetRead], default));
    }

    [Fact]
    public async Task Suspending_or_deactivating_the_last_administrator_is_refused()
    {
        var harness = Harness.OfHarbor();

        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.BySystemWork(h => h.Seats.SuspendAsync(harness.Administrator, default)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.BySystemWork(h => h.Seats.DeactivateAsync(harness.Administrator, default)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.As(harness.Administrator, h => h.Seats.SuspendAsync(harness.Administrator, default)));

        harness.Store.Seat(harness.Administrator).Status.Should().Be(SeatStatus.Active);
    }

    [Fact]
    public async Task Withdrawing_the_last_administrators_root_placement_is_refused()
    {
        var harness = Harness.OfHarbor();

        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.BySystemWork(h => h.Seats.WithdrawAsync(harness.Administrator, harness.Harbor.Root, default)));

        harness.Store.Seat(harness.Administrator).Placements.Should().ContainSingle();
    }

    [Fact]
    public async Task A_second_administrator_can_be_removed()
    {
        var harness = Harness.OfHarbor();
        var second = await harness.SeatAt("Cy", harness.Harbor.Root, HostCatalogue.AdministratorPack);

        await harness.As(second, h => h.Seats.SuspendAsync(harness.Administrator, default));

        harness.Store.Seat(harness.Administrator).Status.Should().Be(SeatStatus.Suspended);
        harness.Store.SavedRights.Should().NotContain(right => right.SeatId == harness.Administrator, "a suspended seat holds nothing");
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.BySystemWork(h => h.Seats.DeactivateAsync(second, default)));

        await harness.As(second, h => h.Seats.ReactivateAsync(harness.Administrator, default));
        await harness.As(harness.Administrator, h => h.Seats.DeactivateAsync(second, default));
        harness.Store.Seat(second).Status.Should().Be(SeatStatus.Deactivated);
    }

    [Fact]
    public async Task An_administrator_grant_with_an_end_date_does_not_count()
    {
        var harness = Harness.OfHarbor();
        var temporary = await harness.SeatAt("Cy", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: Now.AddDays(30));

        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.BySystemWork(h => h.Seats.SuspendAsync(harness.Administrator, default)));

        var future = await harness.SeatAt("Di", harness.Harbor.Root);
        await harness.Grant(future, harness.Harbor.Root, HostCatalogue.AdministratorPack, from: Now.AddDays(1));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.BySystemWork(h => h.Seats.SuspendAsync(harness.Administrator, default)));

        harness.Store.Seat(harness.Administrator).Status.Should().Be(SeatStatus.Active);
    }

    [Fact]
    public async Task An_identity_gets_one_seat_per_tenant()
    {
        var harness = Harness.OfHarbor();
        var other = harness.Seed(2, "quarry");
        var identity = Guid.NewGuid();

        await harness.As(harness.Administrator, h => h.Seats.AddSeatAsync(identity, default));
        await Refused.WithCodeAsync(TenancyRefusals.IdentityHasSeat, () => harness.As(harness.Administrator, h => h.Seats.AddSeatAsync(identity, default)));
        await Refused.WithCodeAsync(TenancyRefusals.IdentityRequired, () => harness.As(harness.Administrator, h => h.Seats.AddSeatAsync(Guid.Empty, default)));

        var elsewhere = await harness.Run(HostCaller.SystemIn(other.Tenant.Id), h => h.Seats.AddSeatAsync(identity, default));

        harness.Store.Seat(elsewhere).TenantId.Should().Be(other.Tenant.Id, "one seat per tenant, and a person may sit in several tenants");
        harness.Store.SeatsIn(harness.Tenant).Count(seat => seat.Identity == identity).Should().Be(1);
    }

    [Fact]
    public async Task Adding_a_seat_sets_the_hosts_own_fields_in_its_callback_in_the_same_save()
    {
        var harness = Harness.OfHarbor();
        var saves = harness.Store.SaveCount;
        var events = harness.Store.SavedEvents.Count;

        var di = await harness.As(harness.Administrator, h => h.Seats.AddSeatAsync(Guid.NewGuid(), default, configure: seat =>
        {
            seat.Rename("Di");
            seat.ChangeJobTitle("Rigger");
        }));

        var saved = harness.Store.Seat(di);
        saved.DisplayName.Should().Be("Di", "a seat has no name of Tenancy's: the host keeps one, and sets it here");
        saved.JobTitle.Should().Be("Rigger");
        harness.Store.SaveCount.Should().Be(saves + 1, "the host's fields go in the save that adds the seat");
        harness.Store.SavedEvents.Skip(events).Should().ContainSingle().Which.Should().BeOfType<SeatAdded<TenantId, SeatId>>();
    }

    [Fact]
    public async Task An_event_the_hosts_seat_raises_in_the_callback_goes_out_with_the_seat_it_added()
    {
        var harness = Harness.OfHarbor();
        var saves = harness.Store.SaveCount;
        var events = harness.Store.SavedEvents.Count;

        var di = await harness.As(harness.Administrator, h => h.Seats.AddSeatAsync(Guid.NewGuid(), default, configure: seat => seat.Welcome()));

        harness.Store.SaveCount.Should().Be(saves + 1, "the host's event leaves with the save that adds the seat, and in no save of its own");
        harness.Store.SavedEvents.Skip(events).Should().SatisfyRespectively(
            added => added.Should().BeOfType<SeatAdded<TenantId, SeatId>>(),
            welcomed => welcomed.Should().BeOfType<HostSeatWelcomed>().Which.SeatId.Should().Be(di, "the seat was added before the callback ran"));
    }

    [Fact]
    public async Task The_callback_runs_once_the_caller_is_checked_and_one_that_throws_adds_no_seat()
    {
        var harness = Harness.OfHarbor();
        var watcher = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.WatcherPack);
        var identity = Guid.NewGuid();
        var called = 0;

        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => harness.As(watcher, h => h.Seats.AddSeatAsync(identity, default, configure: _ => called++)));
        called.Should().Be(0, "a caller that may not add the seat gets nothing of the host's run");

        var saves = harness.Store.SaveCount;
        await FluentActions.Awaiting(() => harness.As(harness.Administrator, h => h.Seats.AddSeatAsync(identity, default, configure: _ => throw new InvalidOperationException("no such person"))))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("no such person");
        harness.Store.SaveCount.Should().Be(saves);
        harness.Store.SeatsIn(harness.Tenant).Should().NotContain(seat => seat.Identity == identity);

        // So the identity has no seat yet, and is given one once the callback holds.
        var added = await harness.As(harness.Administrator, h => h.Seats.AddSeatAsync(identity, default, configure: seat => seat.Rename("Di")));
        harness.Store.Seat(added).DisplayName.Should().Be("Di");
    }

    [Fact]
    public async Task Making_primary_needs_seats_manage_at_the_unit()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Cy", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var seat = await harness.SeatAt("Bert", harness.Harbor.North);
        await harness.Place(seat, harness.Harbor.NorthCoast);
        await harness.Place(seat, harness.Harbor.South);

        await harness.As(supervisor, h => h.Seats.MakePrimaryAsync(seat, harness.Harbor.NorthCoast, default));
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => harness.As(supervisor, h => h.Seats.MakePrimaryAsync(seat, harness.Harbor.South, default)));

        harness.Store.Seat(seat).Placements.Single(placement => placement.IsPrimary).UnitId.Should().Be(harness.Harbor.NorthCoast);
    }

    [Fact]
    public async Task Reactivating_a_seat_needs_seats_manage_tenant_wide()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Cy", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var seat = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.WatcherPack);

        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => harness.As(supervisor, h => h.Seats.SuspendAsync(seat, default)));
        await harness.As(harness.Administrator, h => h.Seats.SuspendAsync(seat, default));
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => harness.As(supervisor, h => h.Seats.ReactivateAsync(seat, default)));

        await harness.As(harness.Administrator, h => h.Seats.ReactivateAsync(seat, default));

        harness.Store.Seat(seat).Status.Should().Be(SeatStatus.Active);
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == seat, "the seat's grants count again");
    }

    [Theory]
    [InlineData("moving a unit")]
    [InlineData("granting")]
    [InlineData("revoking")]
    [InlineData("withdrawing a placement")]
    [InlineData("suspending a seat")]
    [InlineData("reactivating a seat")]
    [InlineData("deactivating a seat")]
    [InlineData("changing a role's keys")]
    [InlineData("archiving roles")]
    [InlineData("placing a seat")]
    [InlineData("archiving a unit")]
    public async Task Every_guarded_command_serializes_before_its_first_read(string command)
    {
        var harness = Harness.OfHarbor();
        var seat = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.WatcherPack);
        if (command == "reactivating a seat")
        {
            await harness.BySystemWork(h => h.Seats.SuspendAsync(seat, default));
        }

        var revision = harness.Store.RevisionOf(harness.Tenant);
        Func<Harness, Task> act = command switch
        {
            "moving a unit" => h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default),
            "granting" => h => h.Seats.GrantAsync(seat, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.OperatorPack), null, null, default),
            "revoking" => h => h.Seats.RevokeAsync(seat, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.WatcherPack), default),
            "withdrawing a placement" => h => h.Seats.WithdrawAsync(seat, harness.Harbor.North, default),
            "suspending a seat" => h => h.Seats.SuspendAsync(seat, default),
            "reactivating a seat" => h => h.Seats.ReactivateAsync(seat, default),
            "deactivating a seat" => h => h.Seats.DeactivateAsync(seat, default),
            "changing a role's keys" => h => h.Roles.SetKeysAsync(harness.RoleFromPack(HostCatalogue.OperatorPack), [HostCatalogue.WidgetRead], default),
            "archiving roles" => h => h.Roles.ArchiveAsync(harness.RoleFromPack(HostCatalogue.WatcherPack), default),
            "placing a seat" => h => h.Seats.PlaceAsync(seat, harness.Harbor.South, false, default),
            "archiving a unit" => h => h.Organization.ArchiveUnitAsync(harness.Harbor.NorthCoast, default),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        // A seat without the key is refused, after the revision was taken, and nothing is saved.
        await FluentActions.Awaiting(() => harness.As(seat, act)).Should().ThrowAsync<Exceptions.RefusalException>();
        harness.Store.Calls[0].Should().Be(nameof(InMemoryTenancyStore.SerializeAccessChangesAsync), "the first call of a refused " + command + " was " + string.Join(", ", harness.Store.Calls));
        harness.Store.RevisionOf(harness.Tenant).Should().Be(revision);

        await harness.As(harness.Administrator, act);

        harness.Store.Calls[0].Should().Be(nameof(InMemoryTenancyStore.SerializeAccessChangesAsync), "the first call of " + command + " was " + string.Join(", ", harness.Store.Calls));
        harness.Store.Calls.Should().Contain(nameof(InMemoryTenancyStore.SaveAsync));
        harness.Store.RevisionOf(harness.Tenant).Should().Be(revision + 1);
    }

    [Fact]
    public async Task A_seat_of_another_tenant_is_not_found()
    {
        var harness = Harness.OfHarbor();
        var other = harness.Seed(2, "quarry");

        await Refused.WithCodeAsync(TenancyRefusals.SeatNotFound,
            () => harness.As(harness.Administrator, h => h.Seats.PlaceAsync(other.Administrator.Id, harness.Harbor.North, false, default)));
        await Refused.WithCodeAsync(TenancyRefusals.SeatNotFound,
            () => harness.As(harness.Administrator, h => h.Seats.SuspendAsync(other.Administrator.Id, default)));
        await Refused.WithCodeAsync(TenancyRefusals.RoleNotFound,
            () => harness.As(harness.Administrator, h => h.Seats.GrantAsync(harness.Administrator, harness.Harbor.Root, other.AdministratorRole.Id, null, null, default)));

        harness.Store.Seat(other.Administrator.Id).Status.Should().Be(SeatStatus.Active);
    }

    [Theory]
    [InlineData("revoking")]
    [InlineData("withdrawing")]
    [InlineData("suspending")]
    [InlineData("deactivating")]
    [InlineData("archiving the role")]
    [InlineData("changing the role's keys")]
    public async Task A_refused_command_leaves_nothing_for_a_later_save_in_its_unit_of_work(string command)
    {
        var harness = Harness.OfHarbor();
        var administrators = harness.RoleFromPack(HostCatalogue.AdministratorPack);
        var ada = harness.Administrator;
        var root = harness.Harbor.Root;
        Func<Harness, Task> refused = command switch
        {
            "revoking" => h => h.Seats.RevokeAsync(ada, root, administrators, default),
            "withdrawing" => h => h.Seats.WithdrawAsync(ada, root, default),
            "suspending" => h => h.Seats.SuspendAsync(ada, default),
            "deactivating" => h => h.Seats.DeactivateAsync(ada, default),
            "archiving the role" => h => h.Roles.ArchiveAsync(administrators, default),
            "changing the role's keys" => h => h.Roles.SetKeysAsync(administrators, [HostCatalogue.WidgetRead], default),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        // An endpoint that catches the refusal and goes on saves the same unit of work again.
        await harness.BySystemWork(async h =>
        {
            await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => refused(h));
            await h.Organization.RenameUnitAsync(harness.Harbor.South, "Southern Region", default);
        });

        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.South)!.Name.Should().Be("Southern Region", "the later command was saved");
        var seat = harness.Store.Seat(ada);
        seat.Status.Should().Be(SeatStatus.Active);
        seat.Placements.Should().ContainSingle().Which.Grants.Should().ContainSingle(grant => grant.RoleId == administrators);
        harness.Store.Role(administrators).Status.Should().Be(RoleStatus.Active);
        harness.Store.Role(administrators).Keys.Should().Contain(TenancyKeys.AdministratorKey);
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == ada && right.Key == TenancyKeys.AdministratorKey, "the tenant keeps its administrator");
    }

    [Fact]
    public async Task A_role_that_manages_access_is_granted_for_no_longer_than_the_granter_holds_those_keys()
    {
        var harness = Harness.OfHarbor();
        var administrators = harness.RoleFromPack(HostCatalogue.AdministratorPack);
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: Now.AddDays(7));
        var newcomer = await harness.SeatAt("Di", harness.Harbor.Root);

        var forGood = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Seats.GrantAsync(newcomer, harness.Harbor.Root, administrators, null, null, default)),
            "an administrator for a week does not make one for good");
        forGood.Arguments["Missing"].Should().Be(string.Join(", ", harness.Catalogue.AccessManagingKeys), "only the keys that manage access are counted");
        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Seats.GrantAsync(newcomer, harness.Harbor.Root, administrators, Now.AddDays(8), null, default)));
        harness.Store.Seat(newcomer).Placements.Single().Grants.Should().BeEmpty();

        await harness.As(temporary, h => h.Seats.GrantAsync(newcomer, harness.Harbor.Root, administrators, Now.AddDays(7), null, default));

        harness.Store.Seat(newcomer).Placements.Single().Grants.Single().EndsAt.Should().Be(Now.AddDays(7));

        // A role that manages no access goes for as long as the granter says.
        await harness.As(temporary, h => h.Seats.GrantAsync(newcomer, harness.Harbor.Root, harness.RoleFromPack(HostCatalogue.WatcherPack), null, null, default));
        harness.Store.Seat(newcomer).Placements.Single().Grants.Should().Contain(grant => grant.RoleId == harness.RoleFromPack(HostCatalogue.WatcherPack) && grant.EndsAt == null);
    }

    [Fact]
    public async Task Granting_a_role_that_manages_no_access_needs_grant_management_and_none_of_its_keys()
    {
        var harness = Harness.OfHarbor();
        var clerk = await GrantDeskAt(harness, "Fay", harness.Harbor.North);
        var newcomer = await harness.SeatAt("Di", harness.Harbor.NorthCoast);
        var southerner = await harness.SeatAt("Ed", harness.Harbor.South);
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);

        await harness.As(clerk, h => h.Seats.GrantAsync(newcomer, harness.Harbor.NorthCoast, operatorRole, null, null, default));

        harness.Store.Seat(newcomer).Placements.Single().Grants.Single().GrantedBy.Should().Be(clerk);
        harness.Store.SavedRights.Where(right => right.SeatId == newcomer).Select(right => right.Key)
            .Should().BeEquivalentTo([HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead], "the clerk holds none of them, and gives them all");

        var outside = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(clerk, h => h.Seats.GrantAsync(southerner, harness.Harbor.South, operatorRole, null, null, default)));
        outside.Arguments["Key"].Should().Be(TenancyKeys.GrantsManage, "roles are given where the key is held, and below");

        var contained = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(clerk, h => h.Seats.GrantAsync(newcomer, harness.Harbor.NorthCoast, harness.RoleFromPack(HostCatalogue.SupervisorPack), null, null, default)));
        contained.Arguments["Missing"].Should().Be(TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage, "a role that manages access still needs its keys that do");
        contained.Arguments["Role"].Should().Be(harness.RoleFromPack(HostCatalogue.SupervisorPack));
    }

    [Fact]
    public async Task A_role_that_manages_no_access_is_granted_to_another_seat_for_as_long_as_the_granter_says()
    {
        var harness = Harness.OfHarbor();
        var clerk = await GrantDeskAt(harness, "Fay", harness.Harbor.North, until: Now.AddDays(7));
        var newcomer = await harness.SeatAt("Di", harness.Harbor.North);

        await harness.As(clerk, h => h.Seats.GrantAsync(newcomer, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.WatcherPack), null, null, default));
        await harness.As(clerk, h => h.Seats.GrantAsync(newcomer, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.OperatorPack), Now.AddDays(30), null, default));

        harness.Store.Seat(newcomer).Placements.Single().Grants.Select(grant => grant.EndsAt).Should().BeEquivalentTo([null, (DateTimeOffset?)Now.AddDays(30)]);
    }

    [Fact]
    public async Task Revoking_a_role_that_manages_no_access_needs_none_of_its_keys()
    {
        var harness = Harness.OfHarbor();
        var clerk = await GrantDeskAt(harness, "Fay", harness.Harbor.North);
        var worker = await harness.SeatAt("Di", harness.Harbor.NorthCoast, HostCatalogue.OperatorPack, HostCatalogue.SupervisorPack);

        await harness.As(clerk, h => h.Seats.RevokeAsync(worker, harness.Harbor.NorthCoast, harness.RoleFromPack(HostCatalogue.OperatorPack), default));

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(clerk, h => h.Seats.RevokeAsync(worker, harness.Harbor.NorthCoast, harness.RoleFromPack(HostCatalogue.SupervisorPack), default)));
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage);
        harness.Store.Seat(worker).Placements.Single().Grants.Select(grant => grant.RoleId).Should().Equal(harness.RoleFromPack(HostCatalogue.SupervisorPack));
    }

    [Fact]
    public async Task Withdrawing_a_placement_whose_roles_manage_no_access_needs_none_of_their_keys()
    {
        var harness = Harness.OfHarbor();
        var placer = await harness.BySystemWork(h => h.Roles.CreateAsync("Placer", "Places people and gives them roles", [TenancyKeys.SeatsManage, TenancyKeys.GrantsManage], default));
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);
        await harness.BySystemWork(h => h.Seats.GrantAsync(bert, harness.Harbor.North, placer, null, null, default));
        var worker = await harness.SeatAt("Di", harness.Harbor.NorthCoast, HostCatalogue.OperatorPack, HostCatalogue.WatcherPack);
        var supervisor = await harness.SeatAt("Ed", harness.Harbor.NorthCoast, HostCatalogue.SupervisorPack);

        await harness.As(bert, h => h.Seats.WithdrawAsync(worker, harness.Harbor.NorthCoast, default));

        harness.Store.Seat(worker).Placements.Should().BeEmpty();
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(bert, h => h.Seats.WithdrawAsync(supervisor, harness.Harbor.NorthCoast, default)));
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.UnitsManage);
        harness.Store.Seat(supervisor).Placements.Should().ContainSingle();
    }

    [Fact]
    public async Task A_seat_gives_itself_a_role_that_manages_no_access_where_it_is_placed()
    {
        var harness = Harness.OfHarbor();
        var clerk = await GrantDeskAt(harness, "Fay", harness.Harbor.North);
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);

        await harness.As(clerk, h => h.Seats.GrantAsync(clerk, harness.Harbor.North, operatorRole, null, "covers the widgets", default));

        var grant = harness.Store.Seat(clerk).Placements.Single().Grants.Single(candidate => candidate.RoleId == operatorRole);
        grant.GrantedBy.Should().Be(clerk, "a seat's grant to itself records it as the granter");
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == clerk && right.Key == HostCatalogue.WidgetChange && right.UnitId == harness.Harbor.North);
        await Refused.WithCodeAsync(TenancyRefusals.PlacementNotFound,
            () => harness.As(clerk, h => h.Seats.GrantAsync(clerk, harness.Harbor.NorthCoast, harness.RoleFromPack(HostCatalogue.WatcherPack), null, null, default)),
            "a seat gives itself roles only where someone else placed it");
    }

    [Fact]
    public async Task A_seat_gives_itself_a_role_for_no_longer_than_it_holds_grant_management()
    {
        var harness = Harness.OfHarbor();
        var clerk = await GrantDeskAt(harness, "Fay", harness.Harbor.North, until: Now.AddDays(7));
        var newcomer = await harness.SeatAt("Di", harness.Harbor.North);
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);

        var forGood = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(clerk, h => h.Seats.GrantAsync(clerk, harness.Harbor.North, watcher, null, null, default)));
        forGood.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage);
        forGood.Arguments["Role"].Should().Be(watcher);
        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(clerk, h => h.Seats.GrantAsync(clerk, harness.Harbor.North, watcher, Now.AddDays(8), null, default)));

        await harness.As(clerk, h => h.Seats.GrantAsync(clerk, harness.Harbor.North, watcher, Now.AddDays(7), null, default));
        await harness.As(clerk, h => h.Seats.GrantAsync(newcomer, harness.Harbor.North, watcher, null, null, default));

        harness.Store.Seat(clerk).Placements.Single().Grants.Single(grant => grant.RoleId == watcher).EndsAt.Should().Be(Now.AddDays(7));
        harness.Store.Seat(newcomer).Placements.Single().Grants.Single().EndsAt.Should().BeNull("to another seat, the granter's own end does not count");
    }

    [Fact]
    public async Task A_seat_never_gives_itself_a_role_that_manages_access_whatever_it_holds()
    {
        var harness = Harness.OfHarbor();
        var clerk = await GrantDeskAt(harness, "Fay", harness.Harbor.North);
        var supervisors = harness.RoleFromPack(HostCatalogue.SupervisorPack);

        var administrator = await Refused.WithCodeAsync(TenancyRefusals.SelfAppointment,
            () => harness.As(harness.Administrator, h => h.Seats.GrantAsync(harness.Administrator, harness.Harbor.Root, supervisors, null, null, default)),
            "holding every key does not let a seat appoint itself");
        administrator.Arguments["Role"].Should().Be(supervisors);
        await Refused.WithCodeAsync(TenancyRefusals.SelfAppointment,
            () => harness.As(clerk, h => h.Seats.GrantAsync(clerk, harness.Harbor.North, supervisors, null, null, default)),
            "self-appointment is decided before the keys are counted");

        harness.Store.Seat(harness.Administrator).Placements.Single().Grants.Should().ContainSingle();
        harness.Store.Seat(clerk).Placements.Single().Grants.Should().ContainSingle();
    }

    [Fact]
    public async Task A_seat_may_take_away_its_current_roles()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.OperatorPack, HostCatalogue.SupervisorPack);

        await harness.As(bert, h => h.Seats.RevokeAsync(bert, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.OperatorPack), default));
        await harness.As(bert, h => h.Seats.RevokeAsync(bert, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.SupervisorPack), default));

        harness.Store.Seat(bert).Placements.Single().Grants.Should().BeEmpty("a seat's own grant that applies now is its own hold of the keys");
        harness.Store.SavedRights.Should().NotContain(right => right.SeatId == bert);

        // A grant of its own that is still to start is no hold: taking it away needs the keys like anyone else's.
        var clerk = await GrantDeskAt(harness, "Fay", harness.Harbor.North);
        await harness.Grant(clerk, harness.Harbor.North, HostCatalogue.SupervisorPack, from: Now.AddDays(1));
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(clerk, h => h.Seats.RevokeAsync(clerk, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.SupervisorPack), default)));
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage);
    }

    [Fact]
    public async Task Taking_away_a_role_that_manages_access_needs_its_keys_for_as_long_as_the_grant_runs()
    {
        var harness = Harness.OfHarbor();
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: Now.AddDays(7));
        var lasting = await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var brief = await harness.SeatAt("Ed", harness.Harbor.North);
        await harness.Grant(brief, harness.Harbor.North, HostCatalogue.SupervisorPack, until: Now.AddDays(3));
        var supervisors = harness.RoleFromPack(HostCatalogue.SupervisorPack);

        var revoke = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Seats.RevokeAsync(lasting, harness.Harbor.North, supervisors, default)),
            "a grant for good is taken away only by a seat that holds its keys for good");
        revoke.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage);
        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Seats.WithdrawAsync(lasting, harness.Harbor.North, default)));

        await harness.As(temporary, h => h.Seats.RevokeAsync(brief, harness.Harbor.North, supervisors, default));

        harness.Store.Seat(lasting).Placements.Single().Grants.Should().ContainSingle();
        harness.Store.Seat(brief).Placements.Single().Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task A_key_the_application_marks_makes_its_roles_contained()
    {
        var gauge = new Permission("gauges.assign", "Gauges", "Hand gauges to people");
        var marking = HostCatalogue.Application with { AccessManagingKeys = ["gauges.assign"] };
        var harness = Harness.OfHarbor(TenancyCatalogue.Build(marking, [gauge]));
        var keepers = await harness.BySystemWork(h => h.Roles.CreateAsync("Gauge keepers", string.Empty, ["gauges.assign", HostCatalogue.WidgetRead], default));
        var clerk = await GrantDeskAt(harness, "Fay", harness.Harbor.North);
        var newcomer = await harness.SeatAt("Di", harness.Harbor.North);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(clerk, h => h.Seats.GrantAsync(newcomer, harness.Harbor.North, keepers, null, null, default)));
        refusal.Arguments["Missing"].Should().Be("gauges.assign");

        // Retired, the key manages nothing, and the role is given like any other.
        var retired = TenancyCatalogue.Build(marking, [gauge with { Retired = true }]);
        await harness.As(clerk, h => new HostTenancy.SeatCommands(h.Store, retired, h.Clock).GrantAsync(newcomer, harness.Harbor.North, keepers, null, null, default));
        harness.Store.Seat(newcomer).Placements.Single().Grants.Single().RoleId.Should().Be(keepers);
    }

    [Fact]
    public async Task A_listing_administrator_gives_every_role_that_manages_access()
    {
        // Ada's pack lists Tenancy's keys and the one other key that manages access, and no key to work with widgets.
        var harness = Harness.OfHarbor(New.ListingCatalogue());
        var ada = harness.Administrator;
        var root = harness.Harbor.Root;
        var newcomer = await harness.SeatAt("Di", root);
        var stock = await harness.As(ada, h => h.Roles.CreateAsync("Stock keeper", string.Empty, [New.WidgetAssign, HostCatalogue.WidgetCreate], default));
        RoleId[] roles = [.. harness.Harbor.RolesByPack.Values.Select(role => role.Id), stock];

        harness.Store.SavedRights.Where(right => right.SeatId == ada).Select(right => right.Key)
            .Should().Contain(harness.Catalogue.AccessManagingKeys)
            .And.NotContain([HostCatalogue.WidgetRead, HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate], "she holds what her pack lists");

        // Every role of the tenant, as a seat and not as system work: the administrators' own, the supervisor's
        // and the two that hold the marked key, which go only from a seat holding their keys that manage access,
        // and the rest, which whoever manages grants gives without holding their keys.
        foreach (var role in roles)
        {
            await harness.As(ada, h => h.Seats.GrantAsync(newcomer, root, role, null, null, default));
        }

        harness.Store.Seat(newcomer).Placements.Single().Grants.Select(grant => grant.RoleId).Should().BeEquivalentTo(roles);
        harness.Store.Seat(newcomer).Placements.Single().Grants.Should().OnlyContain(grant => grant.GrantedBy == ada);
        roles.Where(role => harness.Catalogue.AccessManagingKeysOf(harness.Store.Role(role).Facts).Count > 0)
            .Should().BeEquivalentTo(
                [harness.RoleFromPack(HostCatalogue.AdministratorPack), harness.RoleFromPack(HostCatalogue.SupervisorPack), harness.RoleFromPack(New.KeeperPack), stock],
                "these are the roles that manage access");
        harness.Store.SavedRights.Where(right => right.SeatId == newcomer).Select(right => right.Key).Distinct()
            .Should().BeEquivalentTo(harness.Catalogue.LiveKeys, "between them the roles give every live key, the ones Ada does not hold included");

        // And she takes each away again, by the same rule.
        foreach (var role in roles)
        {
            await harness.As(ada, h => h.Seats.RevokeAsync(newcomer, root, role, default));
        }

        harness.Store.Seat(newcomer).Placements.Single().Grants.Should().BeEmpty();

        // What the catalogue asks of her pack is what makes this hold: a seat that manages grants without the
        // marked key is refused the role that holds it.
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var northerner = await harness.SeatAt("Ed", harness.Harbor.North);
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(supervisor, h => h.Seats.GrantAsync(northerner, harness.Harbor.North, harness.RoleFromPack(New.KeeperPack), null, null, default)));
        refusal.Arguments["Missing"].Should().Be(New.WidgetAssign);
    }

    [Fact]
    public async Task A_listing_administrator_holds_a_key_it_does_not_list_only_through_another_role()
    {
        // The list is what the administrators' role starts with, not a wall around the seat.
        var harness = Harness.OfHarbor(New.ListingCatalogue());
        var ada = harness.Administrator;
        var root = harness.Harbor.Root;
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);
        string[] work = [HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead];

        IEnumerable<string> Held() => harness.Store.SavedRights.Where(right => right.SeatId == ada).Select(right => right.Key).Distinct();

        Held().Should().NotContain(work, "her own role lists none of the keys to work with widgets");

        // A role that manages no access she gives herself, as every seat that manages grants does where it is
        // placed, and the grant says who gave it.
        await harness.As(ada, h => h.Seats.GrantAsync(ada, root, operatorRole, null, "covers the widgets", default));

        Held().Should().Contain(work).And.Contain(harness.Catalogue.AccessManagingKeys);
        harness.Store.Seat(ada).Placements.Single().Grants.Single(grant => grant.RoleId == operatorRole).GrantedBy.Should().Be(ada);

        // A role that manages access she never gives herself, although she holds its keys that do.
        var refusal = await Refused.WithCodeAsync(TenancyRefusals.SelfAppointment,
            () => harness.As(ada, h => h.Seats.GrantAsync(ada, root, harness.RoleFromPack(New.KeeperPack), null, null, default)));
        refusal.Arguments["Role"].Should().Be(harness.RoleFromPack(New.KeeperPack));

        // Without the other role she is back to what her pack lists.
        await harness.As(ada, h => h.Seats.RevokeAsync(ada, root, operatorRole, default));

        Held().Should().BeEquivalentTo(harness.Catalogue.AdministratorPackFor(TenantShape.Hierarchical).Keys);
    }

    [Fact]
    public async Task An_archived_role_is_taken_away_without_its_keys()
    {
        var harness = Harness.OfHarbor();
        var clerk = await GrantDeskAt(harness, "Fay", harness.Harbor.North);
        var supervisor = await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var supervisors = harness.RoleFromPack(HostCatalogue.SupervisorPack);
        await harness.BySystemWork(h => h.Roles.ArchiveAsync(supervisors, default));

        await harness.As(clerk, h => h.Seats.RevokeAsync(supervisor, harness.Harbor.North, supervisors, default));

        harness.Store.Seat(supervisor).Placements.Single().Grants.Should().BeEmpty("an archived role manages nothing");
    }

    [Fact]
    public async Task System_work_for_a_seat_may_give_that_seat_a_role_that_manages_access()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North);

        await harness.Run(harness.SystemCaller(actingSeat: bert),
            h => h.Seats.GrantAsync(bert, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.SupervisorPack), null, null, default));

        var grant = harness.Store.Seat(bert).Placements.Single().Grants.Single();
        grant.RoleId.Should().Be(harness.RoleFromPack(HostCatalogue.SupervisorPack));
        grant.GrantedBy.Should().BeNull("system work acting on the very seat it acts for records nobody");
    }

    [Fact]
    public async Task Granting_at_an_archived_unit_is_refused()
    {
        var harness = Harness.OfHarbor();
        var seat = await harness.SeatAt("Bert", harness.Harbor.NorthCoast);
        await harness.BySystemWork(h => h.Organization.ArchiveUnitAsync(harness.Harbor.NorthCoast, default));
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.UnitNotActive,
            () => harness.As(harness.Administrator, h => h.Seats.GrantAsync(seat, harness.Harbor.NorthCoast, watcher, null, null, default)));

        refusal.Arguments["Unit"].Should().Be(harness.Harbor.NorthCoast);
        await Refused.WithCodeAsync(TenancyRefusals.UnitNotActive, () => harness.Grant(seat, harness.Harbor.NorthCoast, HostCatalogue.WatcherPack));
        harness.Store.Seat(seat).Placements.Single().Grants.Should().BeEmpty("nothing new goes to an archived unit");
    }

    [Fact]
    public async Task Suspending_or_deactivating_a_seat_needs_the_keys_of_its_roles_that_manage_access()
    {
        var harness = Harness.OfHarbor();
        var oli = await SeatDeskAtRoot(harness, "Oli");
        var supervisor = await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var worker = await harness.SeatAt("Ed", harness.Harbor.NorthCoast, HostCatalogue.OperatorPack, HostCatalogue.WatcherPack);

        var suspend = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(oli, h => h.Seats.SuspendAsync(supervisor, default)),
            "suspending takes the supervisor's grant away, which only a seat holding its keys that manage access may");
        suspend.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.UnitsManage, "Oli holds seats.manage, and no other of them");
        suspend.Arguments["Role"].Should().Be(harness.RoleFromPack(HostCatalogue.SupervisorPack));
        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => harness.As(oli, h => h.Seats.DeactivateAsync(supervisor, default)));
        harness.Store.Seat(supervisor).Status.Should().Be(SeatStatus.Active);
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == supervisor && right.Key == TenancyKeys.UnitsManage);

        await harness.As(oli, h => h.Seats.SuspendAsync(worker, default));
        await harness.As(oli, h => h.Seats.ReactivateAsync(worker, default));
        await harness.As(oli, h => h.Seats.DeactivateAsync(worker, default));

        harness.Store.Seat(worker).Status.Should().Be(SeatStatus.Deactivated, "a seat whose roles manage no access is stopped with seats.manage alone");
    }

    [Fact]
    public async Task Reactivating_a_seat_needs_the_keys_of_its_roles_that_manage_access()
    {
        var harness = Harness.OfHarbor();
        var oli = await SeatDeskAtRoot(harness, "Oli");
        var supervisor = await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.SupervisorPack);
        await harness.BySystemWork(h => h.Seats.SuspendAsync(supervisor, default));

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(oli, h => h.Seats.ReactivateAsync(supervisor, default)),
            "reactivating gives the supervisor's grant back, which only a seat holding its keys that manage access may");
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.UnitsManage);
        harness.Store.Seat(supervisor).Status.Should().Be(SeatStatus.Suspended);
        harness.Store.SavedRights.Should().NotContain(right => right.SeatId == supervisor);

        await harness.As(harness.Administrator, h => h.Seats.ReactivateAsync(supervisor, default));

        harness.Store.Seat(supervisor).Status.Should().Be(SeatStatus.Active);
    }

    [Fact]
    public async Task A_status_change_counts_each_grant_that_has_not_ended_against_its_own_end()
    {
        var harness = Harness.OfHarbor();
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: Now.AddDays(7));
        var oli = await SeatDeskAtRoot(harness, "Oli");
        var lasting = await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var brief = await harness.SeatAt("Ed", harness.Harbor.North);
        await harness.Grant(brief, harness.Harbor.North, HostCatalogue.SupervisorPack, until: Now.AddDays(3));
        var upcoming = await harness.SeatAt("Fay", harness.Harbor.North);
        await harness.Grant(upcoming, harness.Harbor.North, HostCatalogue.SupervisorPack, from: Now.AddDays(1));
        var former = await harness.SeatAt("Wes", harness.Harbor.North);
        await harness.Grant(former, harness.Harbor.North, HostCatalogue.SupervisorPack, from: Now.AddDays(-30), until: Now.AddDays(-1));

        var forGood = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Seats.SuspendAsync(lasting, default)),
            "an administrator for a week does not stop a grant for good");
        forGood.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage);
        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Seats.SuspendAsync(upcoming, default)),
            "a grant still to start is one the seat would hold, so it counts");

        await harness.As(temporary, h => h.Seats.SuspendAsync(brief, default));
        await harness.As(oli, h => h.Seats.SuspendAsync(former, default));
        await harness.As(temporary, h => h.Seats.SuspendAsync(temporary, default));

        harness.Store.Seat(brief).Status.Should().Be(SeatStatus.Suspended, "its grant ends before the suspender's own");
        harness.Store.Seat(former).Status.Should().Be(SeatStatus.Suspended, "an ended grant is skipped: suspended or active, the seat gets nothing from it");
        harness.Store.Seat(temporary).Status.Should().Be(SeatStatus.Suspended, "a seat's own grant that applies now is its own hold, so it may stop itself");
        harness.Store.Seat(lasting).Status.Should().Be(SeatStatus.Active);
        harness.Store.Seat(upcoming).Status.Should().Be(SeatStatus.Active);
    }

    /// <summary>
    /// A new seat placed at the root, granted there for good by system work a role made by hand with nothing but
    /// <see cref="TenancyKeys.SeatsManage"/>: someone whose work is keeping the tenant's list of people.
    /// </summary>
    private static async Task<SeatId> SeatDeskAtRoot(Harness harness, string name)
    {
        var desk = await harness.BySystemWork(h => h.Roles.CreateAsync("Seat desk", "Keeps the list of people", [TenancyKeys.SeatsManage], default));
        var seat = await harness.SeatAt(name, harness.Harbor.Root);
        await harness.BySystemWork(h => h.Seats.GrantAsync(seat, harness.Harbor.Root, desk, null, null, default));
        return seat;
    }

    /// <summary>
    /// A new seat whose primary placement is <paramref name="unit"/>, granted there by system work a role made by
    /// hand with nothing but <see cref="TenancyKeys.GrantsManage"/>: someone whose work is giving people their roles.
    /// </summary>
    private static async Task<SeatId> GrantDeskAt(Harness harness, string name, OrganizationUnitId unit, DateTimeOffset? until = null)
    {
        var desk = await harness.BySystemWork(h => h.Roles.CreateAsync("Grant desk", "Gives people their roles", [TenancyKeys.GrantsManage], default));
        var seat = await harness.SeatAt(name, unit);
        await harness.BySystemWork(h => h.Seats.GrantAsync(seat, unit, desk, until, null, default));
        return seat;
    }
}
