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
            h.Seats.RevokeAsync(harness.Administrator, harness.Harbor.Root, harness.RoleFromPack(HostCatalogue.AdministratorPack), default)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.As(harness.Administrator, h => h.Roles.ArchiveAsync(harness.RoleFromPack(HostCatalogue.AdministratorPack), default)));

        // With a second administrator it may: a seat that read the rights itself would find its own alone, and take
        // itself for the last one.
        var second = await harness.SeatAt("Cy", harness.Harbor.Root, HostCatalogue.AdministratorPack);
        using (TenancyCallers.Begin(harness.SeatCaller(harness.Administrator)))
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
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: FixedClock.Start.AddDays(7));
        var di = await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var supervisors = string.Join(", ", TenancyKeys.GrantsManage, TenancyKeys.SeatsManage, TenancyKeys.UnitsManage);

        // Away from North, North Coast would no longer be Di's to supervise, which she is for good, and Bert runs the
        // tenant for a week: Di's rights are not Bert's to read, and the move is refused for them all the same.
        var taking = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default)));
        taking.Arguments["Missing"].Should().Be(supervisors);
        taking.Arguments["Role"].Should().BeNull();
        harness.Store.Calls.Should().Contain(nameof(InMemoryTenancyStore.RightsAMoveChangesAsync));
        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.NorthCoast)!.ParentId.Should().Be(harness.Harbor.North);

        // The mover's own keys count as before: Di supervises South for a week, and a unit of South moved under North
        // would be hers for good.
        await harness.Place(di, harness.Harbor.South);
        await harness.Grant(di, harness.Harbor.South, HostCatalogue.SupervisorPack, until: FixedClock.Start.AddDays(7));
        var pier = await harness.As(di, h => h.Organization.AddUnitAsync(harness.Harbor.South, "South Pier", default));
        var longer = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => harness.As(di, h => h.Organization.MoveUnitAsync(pier, harness.Harbor.North, default)));
        longer.Arguments["Missing"].As<string>().Should().Contain(TenancyKeys.UnitsManage);
        await harness.As(di, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default));

        // An administrator holds every key for good, and moves it back.
        await harness.As(harness.Administrator, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.North, default));
        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.NorthCoast)!.ParentId.Should().Be(harness.Harbor.North);
    }

    [Fact]
    public async Task What_the_store_answers_is_what_the_rights_say()
    {
        var harness = Harness.OfHarbor();
        var administrators = harness.RoleFromPack(HostCatalogue.AdministratorPack);
        var second = await harness.SeatAt("Cy", harness.Harbor.Root, HostCatalogue.AdministratorPack);
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: FixedClock.Start.AddDays(7));
        var later = await harness.SeatAt("Di", harness.Harbor.Root);
        await harness.Grant(later, harness.Harbor.Root, HostCatalogue.AdministratorPack, from: FixedClock.Start.AddDays(1));
        await harness.SeatAt("Ed", harness.Harbor.North, HostCatalogue.SupervisorPack, HostCatalogue.WatcherPack);
        await harness.SeatAt("Fay", harness.Harbor.South, HostCatalogue.OperatorPack);

        await harness.Run(harness.SeatCaller(harness.Administrator), async h =>
        {
            // An administrator holds the role key at the root, with no end, from now or earlier: one that ends, or
            // is still to start, is none.
            var pairs = await h.Store.AdministratorsAsync(h.Tenant, FixedClock.Start, default);
            pairs.Should().BeEquivalentTo([(harness.Administrator, administrators), (second, administrators)]);
            (await h.Store.AdministratorsAsync(h.Tenant, FixedClock.Start.AddDays(2), default)).Should().HaveCount(3, "the one that was still to start has started");

            // A move from under North to under South: the mover's own rights, whatever their key, and every seat's of a
            // key that manages access, at each parent and above it, once for each parent they reach.
            var reaches = await h.Store.RightsAMoveChangesAsync(h.Tenant, harness.Administrator, h.Harbor.North, h.Harbor.South, h.Catalogue.AccessManagingKeys, FixedClock.Start, default);
            reaches.Where(reach => reach.OfCaller).Should().HaveCount(2 * h.Catalogue.LiveKeys.Count, "every key of the administrator, held at the root, reaches both");
            reaches.Where(reach => !reach.OfCaller).Should().OnlyContain(reach => h.Catalogue.ManagesAccess(reach.Key));
            reaches.Where(reach => reach.UnitId == h.Harbor.North).Select(reach => (reach.Key, reach.Parent))
                .Should().BeEquivalentTo(
                    [(TenancyKeys.GrantsManage, h.Harbor.North), (TenancyKeys.SeatsManage, h.Harbor.North), (TenancyKeys.UnitsManage, h.Harbor.North)],
                    "the supervisor's keys that manage access, held at North, reach North alone, and the widget keys of the two roles are nobody's business");
            reaches.Should().NotContain(reach => reach.UnitId == h.Harbor.South, "an operator at South holds no key that manages access");
            reaches.Should().Contain(reach => reach.EndsAt == FixedClock.Start.AddDays(7), "a right that ends later has not ended");
            reaches.Should().Contain(reach => !reach.OfCaller && reach.UnitId == h.Harbor.Root && reach.Parent == h.Harbor.South);
        });
    }
}
