namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// What the use cases ask about every seat's rights, they ask the store, not the read rows: the tenant's
/// administrators, and the rights a move of a unit changes. A storage may show a seat only its own rights, as a
/// database that keeps the rights itself does, and the rules that read other seats' rights hold all the same.
/// </summary>
public class OtherSeatsRightsTests
{
    /// <summary>Harbor, over a store that shows a seat only its own rights.</summary>
    private static Harness OwnRightsOnly()
    {
        var harness = Harness.OfHarbor();
        harness.Store.KeepsOtherSeatsRights = true;
        return harness;
    }

    [Fact]
    public async Task The_last_administrator_check_asks_the_store_for_the_administrators()
    {
        var harness = OwnRightsOnly();

        // Alone, the administrator cannot stop its own seat, give up its role or withdraw from the root.
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.As(harness.Administrator, h => h.Seats.SuspendAsync(harness.Administrator, default)));
        harness.Store.Calls.Should().Contain(nameof(InMemoryTenancyStore.AdministratorsAsync));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.As(harness.Administrator, h =>
            h.Seats.RevokeAsync(harness.Administrator, harness.RoleFromStarterRole(HostCatalogue.AdministratorStarterRole), harness.Harbor.Root, default)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.As(harness.Administrator, h => h.Roles.ArchiveAsync(harness.RoleFromStarterRole(HostCatalogue.AdministratorStarterRole), default)));

        // With a second administrator it may: a seat that read the rights itself would find its own alone, and take
        // itself for the last one.
        var second = await harness.SeatAt("Cy", harness.Harbor.Root, HostCatalogue.AdministratorStarterRole);
        using (TenancyUsers.Begin(harness.SeatUser(harness.Administrator)))
        {
            harness.Store.Filtered.SeatRights.Select(right => right.SeatId).Distinct().Should().Equal([harness.Administrator], "the rows show the seat its own rights alone");
        }

        await harness.As(harness.Administrator, h => h.Seats.SuspendAsync(harness.Administrator, default));
        harness.Store.Seat(harness.Administrator).Status.Should().Be(SeatStatus.Suspended);

        // And the one that is left cannot.
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.As(second, h => h.Seats.DeactivateAsync(second, default)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.BySystemWork(h => h.Seats.DeactivateAsync(second, default)));
    }

    [Fact]
    public async Task A_move_asks_the_store_what_it_changes_and_refuses_as_before()
    {
        var harness = OwnRightsOnly();
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorStarterRole, until: FixedClock.Start.AddDays(7));
        var di = await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.SupervisorStarterRole);
        var supervisors = string.Join(", ", TenancyPermissions.GrantsManage, TenancyPermissions.SeatsManage, TenancyPermissions.UnitsManage);

        // Away from North, North Coast would no longer be Di's to supervise, which she is for good, and Bert runs the
        // tenant for a week: Di's rights are not Bert's to read, and the move is refused for them all the same.
        var taking = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default)));
        taking.Arguments["Missing"].Should().Be(supervisors);
        taking.Arguments["Role"].Should().BeNull();
        harness.Store.Calls.Should().Contain(nameof(InMemoryTenancyStore.RightsAMoveChangesAsync));
        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.NorthCoast)!.ParentId.Should().Be(harness.Harbor.North);

        // The mover's own permissions count as before: Di supervises South for a week, and a unit of South moved under North
        // would be hers for good.
        await harness.Place(di, harness.Harbor.South);
        await harness.Grant(di, harness.Harbor.South, HostCatalogue.SupervisorStarterRole, until: FixedClock.Start.AddDays(7));
        var pier = (await harness.As(di, h => h.Organization.AddUnitAsync(harness.Harbor.South, "South Pier", default))).Id;
        var longer = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => harness.As(di, h => h.Organization.MoveUnitAsync(pier, harness.Harbor.North, default)));
        longer.Arguments["Missing"].As<string>().Should().Contain(TenancyPermissions.UnitsManage);
        await harness.As(di, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default));

        // An administrator holds every permission for good, and moves it back.
        await harness.As(harness.Administrator, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.North, default));
        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.NorthCoast)!.ParentId.Should().Be(harness.Harbor.North);
    }

    [Fact]
    public async Task What_the_store_answers_is_what_the_rights_say()
    {
        var harness = Harness.OfHarbor();
        var administrators = harness.RoleFromStarterRole(HostCatalogue.AdministratorStarterRole);
        var second = await harness.SeatAt("Cy", harness.Harbor.Root, HostCatalogue.AdministratorStarterRole);
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorStarterRole, until: FixedClock.Start.AddDays(7));
        var later = await harness.SeatAt("Di", harness.Harbor.Root);
        await harness.Grant(later, harness.Harbor.Root, HostCatalogue.AdministratorStarterRole, from: FixedClock.Start.AddDays(1));
        await harness.SeatAt("Ed", harness.Harbor.North, HostCatalogue.SupervisorStarterRole, HostCatalogue.WatcherStarterRole);
        var acting = await harness.SeatAt("Gus", harness.Harbor.North);
        await harness.Grant(acting, harness.Harbor.North, HostCatalogue.SupervisorStarterRole, until: FixedClock.Start.AddDays(7));
        await harness.SeatAt("Fay", harness.Harbor.South, HostCatalogue.OperatorStarterRole);

        await harness.Run(harness.SeatUser(harness.Administrator), async h =>
        {
            // An administrator holds the role permission at the root, with no end, from now or earlier: one that ends, or
            // is still to start, is none.
            var pairs = await h.Store.AdministratorsAsync(h.Tenant, FixedClock.Start, default);
            pairs.Should().BeEquivalentTo([(harness.Administrator, administrators), (second, administrators)]);
            (await h.Store.AdministratorsAsync(h.Tenant, FixedClock.Start.AddDays(2), default)).Should().HaveCount(3, "the one that was still to start has started");

            // A move from under North to under South: the mover's own rights, whatever their permission, at each parent and
            // above it, once for each parent they reach; and another seat's of a permission that manages access where it
            // reaches one parent and not the other, which is where the move changes it.
            var reaches = await h.Store.RightsAMoveChangesAsync(h.Tenant, harness.Administrator, h.Harbor.North, h.Harbor.South, h.Catalogue.AccessManagingPermissions, FixedClock.Start, default);
            reaches.Where(reach => reach.OfCurrentSeat).Should().HaveCount(2 * h.Catalogue.LivePermissions.Count, "every permission of the administrator, held at the root, reaches both");
            reaches.Where(reach => !reach.OfCurrentSeat)
                .Should().OnlyContain(
                    reach => h.Catalogue.ManagesAccess(reach.Permission) && reach.UnitId == h.Harbor.North && reach.Parent == h.Harbor.North,
                    "the supervisors' permissions that manage access, held at North, reach North alone, and the widget permissions of their roles are nobody's business")
                .And.HaveCount(6, "Ed's three and Gus's three");
            reaches.Should().NotContain(reach => reach.UnitId == h.Harbor.South, "an operator at South holds no permission that manages access");
            reaches.Should().NotContain(reach => !reach.OfCurrentSeat && reach.UnitId == h.Harbor.Root, "the other administrators' rights at the root reach both parents, wherever the unit hangs");
            reaches.Should().Contain(reach => reach.EndsAt == FixedClock.Start.AddDays(7), "Gus's, which end later, have not ended");
        });
    }
}
