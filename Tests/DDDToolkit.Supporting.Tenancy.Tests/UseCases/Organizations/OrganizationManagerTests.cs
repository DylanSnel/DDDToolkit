namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Changing the tree needs the units permission where the change is made: at the parent to add or archive, at the
/// unit to rename, at both parents to move. A move takes the access revision before it reads anything, gives
/// the mover no permission it lacks at the unit, nor any for longer than it holds it there, and gives or takes away
/// from anyone a permission that manages access only when the mover holds it at the unit for as long.
/// </summary>
public class OrganizationManagerTests
{
    [Fact]
    public async Task Adding_needs_units_manage_at_the_parent()
    {
        var harness = Harness.OfHarbor();
        var watcher = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.WatcherStarterRole);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(watcher, h => h.Organization.AddUnitAsync(harness.Harbor.North, "North Bay", default)));
        refusal.Arguments["Permission"].Should().Be(TenancyPermissions.UnitsManage);
        refusal.Arguments["Unit"].Should().Be(harness.Harbor.North);

        var added = (await harness.As(harness.Administrator, h => h.Organization.AddUnitAsync(harness.Harbor.North, " North Bay ", default))).Id;

        var unit = harness.Store.Organization(harness.Tenant).FindUnit(added)!;
        unit.Name.Should().Be("North Bay");
        unit.ParentId.Should().Be(harness.Harbor.North);
        unit.Should().BeOfType<HostUnit>();
        harness.Store.SavedPaths.Should().Contain(path => path.AncestorId == harness.Harbor.Root && path.DescendantId == added && path.Distance == 2,
            "the closure is written with the unit");

        var given = OrganizationUnitId.CreateSequential();
        (await harness.BySystemWork(h => h.Organization.AddUnitAsync(harness.Harbor.South, "South Bay", default, given))).Id.Should().Be(given);
    }

    [Fact]
    public async Task A_supervisor_adds_below_their_region_only()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorStarterRole);

        await harness.As(supervisor, h => h.Organization.AddUnitAsync(harness.Harbor.North, "North Bay", default));
        await harness.As(supervisor, h => h.Organization.AddUnitAsync(harness.Harbor.NorthCoast, "Pier", default));

        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => harness.As(supervisor, h => h.Organization.AddUnitAsync(harness.Harbor.South, "South Bay", default)));
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => harness.As(supervisor, h => h.Organization.AddUnitAsync(harness.Harbor.Root, "East", default)));

        harness.Store.Organization(harness.Tenant).Units.Should().HaveCount(6);
    }

    [Fact]
    public async Task Renaming_a_unit_needs_the_permission_at_the_unit()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorStarterRole);

        await harness.As(supervisor, h => h.Organization.RenameUnitAsync(harness.Harbor.NorthCoast, "North Shore", default));
        await harness.As(supervisor, h => h.Organization.RenameUnitAsync(harness.Harbor.North, "Northern Region", default));
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => harness.As(supervisor, h => h.Organization.RenameUnitAsync(harness.Harbor.South, "Down South", default)));

        var organization = harness.Store.Organization(harness.Tenant);
        organization.FindUnit(harness.Harbor.NorthCoast)!.Name.Should().Be("North Shore");
        organization.FindUnit(harness.Harbor.North)!.Name.Should().Be("Northern Region");
        organization.FindUnit(harness.Harbor.South)!.Name.Should().Be("South");
    }

    [Fact]
    public async Task Moving_needs_the_permission_at_both_parents()
    {
        var harness = Harness.OfHarbor();
        var north = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorStarterRole);
        var south = await harness.SeatAt("Cy", harness.Harbor.South, HostCatalogue.SupervisorStarterRole);
        var northBay = (await harness.As(north, h => h.Organization.AddUnitAsync(harness.Harbor.North, "North Bay", default))).Id;

        await harness.As(north, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, northBay, default));
        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.NorthCoast)!.ParentId.Should().Be(northBay);

        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(north, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default)));
        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(south, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default)));

        await harness.As(harness.Administrator, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default));

        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.NorthCoast)!.ParentId.Should().Be(harness.Harbor.South);
        harness.Store.SavedPaths.Should().Contain(path => path.AncestorId == harness.Harbor.South && path.DescendantId == harness.Harbor.NorthCoast)
            .And.NotContain(path => path.AncestorId == harness.Harbor.North && path.DescendantId == harness.Harbor.NorthCoast);
        await Refused.WithCodeAsync(TenancyRefusals.RootImmovable,
            () => harness.As(harness.Administrator, h => h.Organization.MoveUnitAsync(harness.Harbor.Root, harness.Harbor.South, default)));
        await Refused.WithCodeAsync(TenancyRefusals.UnitNotFound,
            () => harness.As(harness.Administrator, h => h.Organization.MoveUnitAsync(OrganizationUnitId.CreateSequential(), harness.Harbor.South, default)));
    }

    [Fact]
    public async Task Moving_serializes_on_the_access_revision_first()
    {
        var harness = Harness.OfHarbor();
        var revision = harness.Store.RevisionOf(harness.Tenant);

        await harness.As(harness.Administrator, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default));

        harness.Store.Calls[0].Should().Be(nameof(InMemoryTenancyStore.SerializeAccessChangesAsync), string.Join(", ", harness.Store.Calls));
        harness.Store.RevisionOf(harness.Tenant).Should().Be(revision + 1);

        await harness.As(harness.Administrator, h => h.Organization.RenameUnitAsync(harness.Harbor.South, "Southern Region", default));
        harness.Store.Calls.Should().NotContain(nameof(InMemoryTenancyStore.SerializeAccessChangesAsync), "a rename changes no rights and nothing they reach");
        harness.Store.RevisionOf(harness.Tenant).Should().Be(revision + 1);
    }

    [Fact]
    public async Task Archiving_a_unit_needs_the_permission_at_its_parent()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorStarterRole);

        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => harness.As(supervisor, h => h.Organization.ArchiveUnitAsync(harness.Harbor.North, default)));

        await harness.As(supervisor, h => h.Organization.ArchiveUnitAsync(harness.Harbor.NorthCoast, default));

        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.NorthCoast)!.Status.Should().Be(UnitStatus.Archived);
        await Refused.WithCodeAsync(TenancyRefusals.RootNotArchivable, () => harness.As(harness.Administrator, h => h.Organization.ArchiveUnitAsync(harness.Harbor.Root, default)));
        await Refused.WithCodeAsync(TenancyRefusals.UnitNotActive,
            () => harness.As(harness.Administrator, h => h.Organization.AddUnitAsync(harness.Harbor.NorthCoast, "Pier", default)));
    }

    [Fact]
    public async Task A_flat_tenant_refuses_adding_until_its_shape_changes()
    {
        var harness = new Harness(New.Catalogue());
        var provisioned = await harness.Run(HostUser.System, h => h.Tenants.ProvisionWithIdsAsync(h.Catalogue,
            new TenantToProvision("kiosk", "Kiosk", TenantShape.Flat, "Kiosk", Guid.NewGuid()), default));
        var administrator = HostUser.InSeat(provisioned.Tenant, provisioned.AdminSeat);

        await Refused.WithCodeAsync(TenancyRefusals.FlatTenant,
            () => harness.Run(administrator, h => h.Organization.AddUnitAsync(provisioned.RootUnit, "Workshop", default)));

        await harness.Run(administrator, h => h.Tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, default));
        await harness.Run(administrator, h => h.Organization.AddUnitAsync(provisioned.RootUnit, "Workshop", default));

        harness.Store.Organization(provisioned.Tenant).Units.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_application_sets_its_own_fields_on_a_new_unit_in_the_save_that_adds_it()
    {
        var harness = Harness.OfHarbor();
        var saves = harness.Store.SaveCount;
        var seen = new List<string>();

        var added = (await harness.As(harness.Administrator, h => h.Organization.AddUnitAsync(
            harness.Harbor.North,
            "North Bay",
            default,
            configure: unit =>
            {
                seen.Add(unit.Name + " below " + (unit.ParentId == harness.Harbor.North ? "North" : "somewhere else"));
                unit.SetCostCentre("NB-104");
            }))).Id;

        seen.Should().Equal(["North Bay below North"], "the callback runs once, on the new unit placed below its parent");
        harness.Store.Organization(harness.Tenant).FindUnit(added)!.CostCentre.Should().Be("NB-104");
        harness.Store.SaveCount.Should().Be(saves + 1, "the application's field is written with the unit");
        harness.Store.SavedEvents.OfType<OrganizationUnitAdded<TenantId, OrganizationUnitId, SeatId>>().Should().Contain(unit => unit.UnitId == added);
    }

    [Fact]
    public async Task A_unit_whose_callback_throws_is_not_added()
    {
        var harness = Harness.OfHarbor();
        var saves = harness.Store.SaveCount;

        await FluentActions.Awaiting(() => harness.As(harness.Administrator, h => h.Organization.AddUnitAsync(
                harness.Harbor.North, "North Bay", default, configure: _ => throw new InvalidOperationException("no such kind"))))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("no such kind");

        harness.Store.SaveCount.Should().Be(saves);
        harness.Store.Organization(harness.Tenant).Units.Should().HaveCount(4);
    }

    [Fact]
    public async Task A_unit_is_added_without_a_kind_and_keeps_the_application_fields_at_their_defaults()
    {
        var harness = Harness.OfHarbor();

        var added = (await harness.As(harness.Administrator, h => h.Organization.AddUnitAsync(harness.Harbor.North, "North Bay", default))).Id;

        harness.Store.Organization(harness.Tenant).FindUnit(added)!.CostCentre.Should().BeNull();
        typeof(OrganizationUnitEntity<OrganizationUnitId>).GetProperty("Kind").Should().BeNull("what kind of unit a unit is, is the application's to keep");
    }

    [Fact]
    public async Task A_move_gives_the_moving_seat_no_permission_it_lacks_at_the_unit()
    {
        var harness = Harness.OfHarbor();
        var keepers = (await harness.BySystemWork(h => h.Roles.CreateAsync("Tree keeper", "Shapes the tree", [TenancyPermissions.UnitsManage], default))).Id;
        var bert = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorStarterRole);
        await harness.Place(bert, harness.Harbor.South);
        await harness.BySystemWork(h => h.Seats.GrantAsync(bert, keepers, harness.Harbor.South, null, null, default));
        var pier = (await harness.As(bert, h => h.Organization.AddUnitAsync(harness.Harbor.South, "South Pier", default))).Id;

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(bert, h => h.Organization.MoveUnitAsync(pier, harness.Harbor.North, default)),
            "moving the pier under North would give Bert the supervisor's permissions over it");

        refusal.Arguments["Missing"].Should().Be(string.Join(", ", new[]
        {
            TenancyPermissions.GrantsManage, TenancyPermissions.SeatsManage, HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead,
        }.Order(StringComparer.Ordinal)));
        harness.Store.Organization(harness.Tenant).FindUnit(pier)!.ParentId.Should().Be(harness.Harbor.South);

        await harness.As(harness.Administrator, h => h.Organization.MoveUnitAsync(pier, harness.Harbor.North, default));
        harness.Store.Organization(harness.Tenant).FindUnit(pier)!.ParentId.Should().Be(harness.Harbor.North);
    }

    [Fact]
    public async Task A_move_gives_the_moving_seat_no_permission_for_longer_than_it_holds_it_at_the_unit()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.SupervisorStarterRole);
        await harness.Place(bert, harness.Harbor.South);
        await harness.Grant(bert, harness.Harbor.South, HostCatalogue.SupervisorStarterRole, until: FixedClock.Start.AddDays(7));
        var pier = (await harness.As(bert, h => h.Organization.AddUnitAsync(harness.Harbor.South, "South Pier", default))).Id;

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(bert, h => h.Organization.MoveUnitAsync(pier, harness.Harbor.North, default)),
            "Bert holds the same permissions at both parents, but at South only for a week: under North the pier would be his for good");

        refusal.Arguments["Missing"].Should().Be(string.Join(", ", new[]
        {
            TenancyPermissions.GrantsManage, TenancyPermissions.SeatsManage, TenancyPermissions.UnitsManage, HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead,
        }.Order(StringComparer.Ordinal)));
        harness.Store.Organization(harness.Tenant).FindUnit(pier)!.ParentId.Should().Be(harness.Harbor.South);

        // The other way round his hold only gets shorter, which gives him nothing.
        await harness.As(bert, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default));
        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.NorthCoast)!.ParentId.Should().Be(harness.Harbor.South);

        await harness.As(harness.Administrator, h => h.Organization.MoveUnitAsync(pier, harness.Harbor.North, default));
        harness.Store.Organization(harness.Tenant).FindUnit(pier)!.ParentId.Should().Be(harness.Harbor.North);
    }

    [Fact]
    public async Task A_move_counts_the_movers_grants_still_to_start()
    {
        var harness = Harness.OfHarbor();
        var bert = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(bert, harness.Harbor.Root, HostCatalogue.SupervisorStarterRole, until: FixedClock.Start.AddDays(30));
        await harness.Place(bert, harness.Harbor.South);
        await harness.Grant(bert, harness.Harbor.South, HostCatalogue.SupervisorStarterRole, from: FixedClock.Start.AddDays(1));

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(bert, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default)),
            "from tomorrow Bert supervises South for good, so under South North Coast would be his for good, where he holds it for a month");

        refusal.Arguments["Missing"].Should().Be(string.Join(", ", new[]
        {
            TenancyPermissions.GrantsManage, TenancyPermissions.SeatsManage, TenancyPermissions.UnitsManage, HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead,
        }.Order(StringComparer.Ordinal)));
        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.NorthCoast)!.ParentId.Should().Be(harness.Harbor.North);
    }

    [Fact]
    public async Task A_move_gives_or_takes_away_anyones_permissions_that_manage_access_only_from_a_seat_that_holds_them_for_as_long()
    {
        var harness = Harness.OfHarbor();
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorStarterRole, until: FixedClock.Start.AddDays(7));
        await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.SupervisorStarterRole);
        var pier = (await harness.As(harness.Administrator, h => h.Organization.AddUnitAsync(harness.Harbor.South, "South Pier", default))).Id;
        var supervisors = string.Join(", ", TenancyPermissions.GrantsManage, TenancyPermissions.SeatsManage, TenancyPermissions.UnitsManage);

        var taking = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default)),
            "away from North, North Coast would no longer be Di's to supervise, which she is for good, and Bert runs the tenant for a week");
        taking.Arguments["Missing"].Should().Be(supervisors);
        taking.Arguments["Role"].Should().BeNull();
        var giving = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Organization.MoveUnitAsync(pier, harness.Harbor.North, default)),
            "under North the pier would be Di's to supervise for good");
        giving.Arguments["Missing"].Should().Be(supervisors);
        var organization = harness.Store.Organization(harness.Tenant);
        organization.FindUnit(harness.Harbor.NorthCoast)!.ParentId.Should().Be(harness.Harbor.North);
        organization.FindUnit(pier)!.ParentId.Should().Be(harness.Harbor.South);

        // Watcher manages no access, so its permissions follow a move as freely as the role is given.
        var ed = await harness.SeatAt("Ed", harness.Harbor.South, HostCatalogue.WatcherStarterRole);
        var east = (await harness.As(harness.Administrator, h => h.Organization.AddUnitAsync(harness.Harbor.Root, "East", default))).Id;
        await harness.Place(ed, east);
        await harness.Grant(ed, east, HostCatalogue.WatcherStarterRole);
        await harness.As(temporary, h => h.Organization.MoveUnitAsync(pier, east, default));
        harness.Store.Organization(harness.Tenant).FindUnit(pier)!.ParentId.Should().Be(east);

        await harness.As(harness.Administrator, h => h.Organization.MoveUnitAsync(harness.Harbor.NorthCoast, harness.Harbor.South, default));
        harness.Store.Organization(harness.Tenant).FindUnit(harness.Harbor.NorthCoast)!.ParentId.Should().Be(harness.Harbor.South);
    }

    [Fact]
    public async Task A_listing_administrator_moves_a_unit_unless_the_move_gives_it_a_permission_its_starter_role_does_not_list()
    {
        // The same tenant twice: Ada's starter role lists its permissions, with none to work with widgets, or lists nothing.
        async Task<(Harness Harness, OrganizationUnitId Pier)> HarborWith(TenancyCatalogue catalogue, params string[] starterRolesOfDi)
        {
            var harness = Harness.OfHarbor(catalogue);
            await harness.SeatAt("Di", harness.Harbor.North, starterRolesOfDi);
            var pier = (await harness.As(harness.Administrator, h => h.Organization.AddUnitAsync(harness.Harbor.South, "South Pier", default))).Id;
            return (harness, pier);
        }

        var (listing, pier) = await HarborWith(New.ListingCatalogue(), HostCatalogue.SupervisorStarterRole, New.KeeperStarterRole);
        var ada = listing.Administrator;

        // Under North the pier becomes Di's to supervise and to hand widgets out at, and away from it that ends.
        // Those are permissions that manage access, and Ada's starter role has to list every one of them, so she holds each at
        // the root for good: no move is refused her over what it gives or takes away from someone else.
        await listing.As(ada, h => h.Organization.MoveUnitAsync(pier, listing.Harbor.North, default));
        await listing.As(ada, h => h.Organization.MoveUnitAsync(pier, listing.Harbor.South, default));
        listing.Store.Organization(listing.Tenant).FindUnit(pier)!.ParentId.Should().Be(listing.Harbor.South);

        // What a move would give herself counts as it does for every seat. As an operator at North she would
        // work with the widgets of the pier once it hangs there, and her role at the root holds no such permission.
        await listing.Place(ada, listing.Harbor.North);
        await listing.Grant(ada, listing.Harbor.North, HostCatalogue.OperatorStarterRole);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => listing.As(ada, h => h.Organization.MoveUnitAsync(pier, listing.Harbor.North, default)));
        refusal.Arguments["Missing"].Should().Be(HostCatalogue.WidgetChange + ", " + HostCatalogue.WidgetCreate + ", " + HostCatalogue.WidgetRead);
        refusal.Arguments["Role"].Should().BeNull();
        listing.Store.Organization(listing.Tenant).FindUnit(pier)!.ParentId.Should().Be(listing.Harbor.South);

        // An administrator whose starter role lists nothing holds every permission at the root, and moves any unit.
        var (unlisted, otherPier) = await HarborWith(New.Catalogue(), HostCatalogue.SupervisorStarterRole);
        await unlisted.Place(unlisted.Administrator, unlisted.Harbor.North);
        await unlisted.Grant(unlisted.Administrator, unlisted.Harbor.North, HostCatalogue.OperatorStarterRole);

        await unlisted.As(unlisted.Administrator, h => h.Organization.MoveUnitAsync(otherPier, unlisted.Harbor.North, default));
        unlisted.Store.Organization(unlisted.Tenant).FindUnit(otherPier)!.ParentId.Should().Be(unlisted.Harbor.North);
    }
}
