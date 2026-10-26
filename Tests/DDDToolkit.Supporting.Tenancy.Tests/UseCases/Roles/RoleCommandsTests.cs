namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Roles are the tenant's own, managed with the roles key for the whole tenant: names unique ignoring case,
/// keys from the catalogue, no change that takes the last administrator's key away, and a key that manages
/// access added to a role, taken out of it or archived with it by an administrator alone. A change of keys
/// reaches every seat holding the role when it is saved.
/// </summary>
public class RoleCommandsTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    [Fact]
    public async Task A_role_that_gains_a_key_that_manages_access_is_given_by_its_keys_from_then_on()
    {
        var harness = Harness.OfHarbor();
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);
        var desk = await harness.BySystemWork(h => h.Roles.CreateAsync("Grant desk", "Gives people their roles", [TenancyKeys.GrantsManage], default));
        var clerk = await harness.SeatAt("Fay", harness.Harbor.North);
        await harness.BySystemWork(h => h.Seats.GrantAsync(clerk, harness.Harbor.North, desk, null, null, default));
        var holder = await harness.SeatAt("Bert", harness.Harbor.North);
        var newcomer = await harness.SeatAt("Di", harness.Harbor.North);

        await harness.As(clerk, h => h.Seats.GrantAsync(holder, harness.Harbor.North, operatorRole, null, null, default));
        await harness.As(harness.Administrator, h => h.Roles.SetKeysAsync(operatorRole, [HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, TenancyKeys.UnitsManage], default));

        var give = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(clerk, h => h.Seats.GrantAsync(newcomer, harness.Harbor.North, operatorRole, null, null, default)));
        give.Arguments["Missing"].Should().Be(TenancyKeys.UnitsManage);
        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(clerk, h => h.Seats.RevokeAsync(holder, harness.Harbor.North, operatorRole, default)));

        harness.Store.Seat(holder).Placements.Single().Grants.Single().RoleId.Should().Be(operatorRole, "the holder keeps the role");
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == holder && right.Key == TenancyKeys.UnitsManage, "and gains what it grants now");
    }

    [Fact]
    public async Task A_key_that_manages_access_is_added_to_a_role_by_an_administrator_alone()
    {
        var harness = Harness.OfHarbor();
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: Now.AddDays(7));

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Roles.SetKeysAsync(watcher, [HostCatalogue.WidgetRead, TenancyKeys.UnitsManage], default)),
            "an administrator for a week does not give lasting power over access through a role");
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.RolesManage);
        refusal.Arguments["Role"].Should().Be(watcher);
        harness.Store.Role(watcher).Keys.Should().Equal(HostCatalogue.WidgetRead);

        await harness.As(temporary, h => h.Roles.SetKeysAsync(watcher, [HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate], default));
        harness.Store.Role(watcher).Keys.Should().Equal(HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);

        await harness.As(harness.Administrator, h => h.Roles.SetKeysAsync(watcher, [HostCatalogue.WidgetRead, TenancyKeys.UnitsManage], default));
        harness.Store.Role(watcher).Keys.Should().Equal(TenancyKeys.UnitsManage, HostCatalogue.WidgetRead);
        await harness.BySystemWork(h => h.Roles.SetKeysAsync(harness.RoleFromPack(HostCatalogue.OperatorPack), [HostCatalogue.WidgetRead, TenancyKeys.SeatsManage], default));
        harness.Store.Role(harness.RoleFromPack(HostCatalogue.OperatorPack)).Holds(TenancyKeys.SeatsManage).Should().BeTrue();
    }

    [Fact]
    public async Task A_key_that_manages_access_is_taken_out_of_a_role_or_archived_with_it_by_an_administrator_alone()
    {
        var harness = Harness.OfHarbor();
        var supervisors = harness.RoleFromPack(HostCatalogue.SupervisorPack);
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorPack, until: Now.AddDays(7));
        var lasting = await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.SupervisorPack);
        var before = harness.Store.Role(supervisors).Keys.ToArray();

        var removing = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Roles.SetKeysAsync(supervisors, [HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, TenancyKeys.SeatsManage, TenancyKeys.GrantsManage], default)),
            "a key taken out of a role goes from every holder, which an administrator for a week could not revoke grant by grant");
        removing.Arguments["Missing"].Should().Be(TenancyKeys.RolesManage);
        removing.Arguments["Role"].Should().Be(supervisors);
        var archiving = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Roles.ArchiveAsync(supervisors, default)));
        archiving.Arguments["Missing"].Should().Be(TenancyKeys.RolesManage);
        archiving.Arguments["Role"].Should().Be(supervisors);
        harness.Store.Role(supervisors).Keys.Should().Equal(before);
        harness.Store.Role(supervisors).Status.Should().Be(RoleStatus.Active);
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == lasting && right.Key == TenancyKeys.UnitsManage);

        // Keys that manage no access, and roles that manage none, stay the temporary administrator's to change.
        await harness.As(temporary, h => h.Roles.SetKeysAsync(supervisors, [HostCatalogue.WidgetChange, TenancyKeys.UnitsManage, TenancyKeys.SeatsManage, TenancyKeys.GrantsManage], default));
        harness.Store.Role(supervisors).Holds(HostCatalogue.WidgetCreate).Should().BeFalse();
        await harness.As(temporary, h => h.Roles.ArchiveAsync(harness.RoleFromPack(HostCatalogue.WatcherPack), default));
        harness.Store.Role(harness.RoleFromPack(HostCatalogue.WatcherPack)).Status.Should().Be(RoleStatus.Archived);

        // The lasting administrator and system work do both.
        await harness.As(harness.Administrator, h => h.Roles.SetKeysAsync(supervisors, [HostCatalogue.WidgetChange, TenancyKeys.SeatsManage, TenancyKeys.GrantsManage], default));
        harness.Store.Role(supervisors).Holds(TenancyKeys.UnitsManage).Should().BeFalse();
        await harness.BySystemWork(h => h.Roles.ArchiveAsync(supervisors, default));
        harness.Store.Role(supervisors).Status.Should().Be(RoleStatus.Archived);
    }

    [Fact]
    public async Task Creating_needs_roles_manage_tenant_wide()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.Root, HostCatalogue.SupervisorPack);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(supervisor, h => h.Roles.CreateAsync("Clerk", "Files widgets", [HostCatalogue.WidgetChange], default)));
        refusal.Arguments["Key"].Should().Be(TenancyKeys.RolesManage);

        var clerk = await harness.As(harness.Administrator, h => h.Roles.CreateAsync(" Clerk ", "Files widgets", [HostCatalogue.WidgetChange], default));

        var role = harness.Store.Role(clerk);
        role.Name.Should().Be("Clerk");
        role.Keys.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetRead);
        role.FromPack.Should().BeNull("a role made by hand comes from no pack");
        role.TenantId.Should().Be(harness.Tenant);
        await Refused.WithCodeAsync(TenancyRefusals.UnknownPermission,
            () => harness.As(harness.Administrator, h => h.Roles.CreateAsync("Pilot", string.Empty, ["widget.fly"], default)));
    }

    [Fact]
    public async Task Role_names_are_unique_per_tenant_ignoring_case()
    {
        var harness = Harness.OfHarbor();
        var other = harness.Seed(2, "quarry");
        var clerk = await harness.As(harness.Administrator, h => h.Roles.CreateAsync("Clerk", string.Empty, [HostCatalogue.WidgetRead], default));

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.RoleNameTaken,
            () => harness.As(harness.Administrator, h => h.Roles.CreateAsync("clerk", string.Empty, [HostCatalogue.WidgetRead], default)));
        refusal.Arguments["Name"].Should().Be("clerk");
        await Refused.WithCodeAsync(TenancyRefusals.RoleNameTaken,
            () => harness.As(harness.Administrator, h => h.Roles.CreateAsync("OPERATOR", string.Empty, [HostCatalogue.WidgetRead], default)));
        await Refused.WithCodeAsync(TenancyRefusals.RoleNameTaken,
            () => harness.As(harness.Administrator, h => h.Roles.RenameAsync(clerk, "Watcher", string.Empty, default)));

        await harness.As(harness.Administrator, h => h.Roles.RenameAsync(clerk, "CLERK", "Files widgets", default));
        harness.Store.Role(clerk).Name.Should().Be("CLERK", "a role may change the case of its own name");

        await harness.Run(HostCaller.InSeat(other.Tenant.Id, other.Administrator.Id),
            h => h.Roles.CreateAsync("Clerk", string.Empty, [HostCatalogue.WidgetRead], default));
        harness.Store.RolesOf(other.Tenant.Id).Should().Contain(role => role.Name == "Clerk", "names are unique within a tenant only");
    }

    [Fact]
    public async Task Removing_the_admin_key_from_the_last_administrators_role_is_refused()
    {
        var harness = Harness.OfHarbor();
        var administrators = harness.RoleFromPack(HostCatalogue.AdministratorPack);

        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin,
            () => harness.As(harness.Administrator, h => h.Roles.SetKeysAsync(administrators, [HostCatalogue.WidgetRead, TenancyKeys.SeatsManage], default)));
        harness.Store.Role(administrators).Holds(TenancyKeys.AdministratorKey).Should().BeTrue();

        // A second administrator, through a role of its own, lets the pack's role lose the key.
        var keepers = await harness.As(harness.Administrator, h => h.Roles.CreateAsync("Keepers", string.Empty, [TenancyKeys.RolesManage, TenancyKeys.SeatsManage], default));
        var keeper = await harness.SeatAt("Cy", harness.Harbor.Root);
        await harness.As(harness.Administrator, h => h.Seats.GrantAsync(keeper, harness.Harbor.Root, keepers, null, null, default));

        await harness.As(harness.Administrator, h => h.Roles.SetKeysAsync(administrators, [HostCatalogue.WidgetRead, TenancyKeys.SeatsManage], default));

        harness.Store.Role(administrators).Keys.Should().Equal(TenancyKeys.SeatsManage, HostCatalogue.WidgetRead);
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin,
            () => harness.As(keeper, h => h.Roles.SetKeysAsync(keepers, [TenancyKeys.SeatsManage], default)));
    }

    [Fact]
    public async Task Archiving_the_last_administrators_role_is_refused()
    {
        var harness = Harness.OfHarbor();
        var administrators = harness.RoleFromPack(HostCatalogue.AdministratorPack);

        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.As(harness.Administrator, h => h.Roles.ArchiveAsync(administrators, default)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.BySystemWork(h => h.Roles.ArchiveAsync(administrators, default)));

        harness.Store.Role(administrators).Status.Should().Be(RoleStatus.Active);
        await harness.As(harness.Administrator, h => h.Roles.ArchiveAsync(harness.RoleFromPack(HostCatalogue.WatcherPack), default));
        harness.Store.Role(harness.RoleFromPack(HostCatalogue.WatcherPack)).Status.Should().Be(RoleStatus.Archived);
    }

    [Fact]
    public async Task Changed_keys_reach_every_holder_at_the_next_save()
    {
        var harness = Harness.OfHarbor();
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);
        var north = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.OperatorPack);
        var south = await harness.SeatAt("Cy", harness.Harbor.South, HostCatalogue.OperatorPack);

        await harness.As(harness.Administrator, h => h.Roles.SetKeysAsync(operatorRole, [HostCatalogue.WidgetRead, TenancyKeys.UnitsManage], default));

        foreach (var (seat, unit) in new[] { (north, harness.Harbor.North), (south, harness.Harbor.South) })
        {
            harness.Store.SavedRights.Where(right => right.SeatId == seat).Select(right => (right.UnitId, right.Key))
                .Should().BeEquivalentTo([(unit, HostCatalogue.WidgetRead), (unit, TenancyKeys.UnitsManage)]);
        }

        var reached = await harness.As(north, h =>
            Task.FromResult(new TenancyAnswers<TenantId, SeatId, OrganizationUnitId, RoleId>(h.Catalogue, h.Clock)
                .Over(h.Store, h.Store).UnitsWhereIHold(TenancyKeys.UnitsManage).ToArray()));
        reached.Should().BeEquivalentTo([harness.Harbor.North, harness.Harbor.NorthCoast]);
    }
}
