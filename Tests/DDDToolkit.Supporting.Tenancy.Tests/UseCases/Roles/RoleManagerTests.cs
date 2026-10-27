namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Roles are the tenant's own, managed with the roles permission for the whole tenant: names unique ignoring case,
/// permissions from the catalogue, no change that takes the last administrator's permission away, and a permission that manages
/// access added to a role, taken out of it or archived with it by an administrator alone. A change of permissions
/// reaches every seat holding the role when it is saved.
/// </summary>
public class RoleManagerTests
{
    private static readonly DateTimeOffset Now = FixedClock.Start;

    [Fact]
    public async Task A_role_that_gains_a_permission_that_manages_access_is_given_by_its_permissions_from_then_on()
    {
        var harness = Harness.OfHarbor();
        var operatorRole = harness.RoleFromStarterRole(HostCatalogue.OperatorStarterRole);
        var desk = (await harness.BySystemWork(h => h.Roles.CreateAsync("Grant desk", "Gives people their roles", [TenancyPermissions.GrantsManage], default))).Id;
        var clerk = await harness.SeatAt("Fay", harness.Harbor.North);
        await harness.BySystemWork(h => h.Seats.GrantAsync(clerk, desk, harness.Harbor.North, null, null, default));
        var holder = await harness.SeatAt("Bert", harness.Harbor.North);
        var newcomer = await harness.SeatAt("Di", harness.Harbor.North);

        await harness.As(clerk, h => h.Seats.GrantAsync(holder, operatorRole, harness.Harbor.North, null, null, default));
        await harness.As(harness.Administrator, h => h.Roles.SetPermissionsAsync(operatorRole, [HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, TenancyPermissions.UnitsManage], default));

        var give = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(clerk, h => h.Seats.GrantAsync(newcomer, operatorRole, harness.Harbor.North, null, null, default)));
        give.Arguments["Missing"].Should().Be(TenancyPermissions.UnitsManage);
        await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(clerk, h => h.Seats.RevokeAsync(holder, operatorRole, harness.Harbor.North, default)));

        harness.Store.Seat(holder).Placements.Single().Grants.Single().RoleId.Should().Be(operatorRole, "the holder keeps the role");
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == holder && right.Permission == TenancyPermissions.UnitsManage, "and gains what it grants now");
    }

    [Fact]
    public async Task A_permission_that_manages_access_is_added_to_a_role_by_an_administrator_alone()
    {
        var harness = Harness.OfHarbor();
        var watcher = harness.RoleFromStarterRole(HostCatalogue.WatcherStarterRole);
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorStarterRole, until: Now.AddDays(7));

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Roles.SetPermissionsAsync(watcher, [HostCatalogue.WidgetRead, TenancyPermissions.UnitsManage], default)),
            "an administrator for a week does not give lasting power over access through a role");
        refusal.Arguments["Missing"].Should().Be(TenancyPermissions.RolesManage);
        refusal.Arguments["Role"].Should().Be(watcher);
        harness.Store.Role(watcher).Permissions.Should().Equal(HostCatalogue.WidgetRead);

        await harness.As(temporary, h => h.Roles.SetPermissionsAsync(watcher, [HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate], default));
        harness.Store.Role(watcher).Permissions.Should().Equal(HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);

        await harness.As(harness.Administrator, h => h.Roles.SetPermissionsAsync(watcher, [HostCatalogue.WidgetRead, TenancyPermissions.UnitsManage], default));
        harness.Store.Role(watcher).Permissions.Should().Equal(TenancyPermissions.UnitsManage, HostCatalogue.WidgetRead);
        await harness.BySystemWork(h => h.Roles.SetPermissionsAsync(harness.RoleFromStarterRole(HostCatalogue.OperatorStarterRole), [HostCatalogue.WidgetRead, TenancyPermissions.SeatsManage], default));
        harness.Store.Role(harness.RoleFromStarterRole(HostCatalogue.OperatorStarterRole)).Holds(TenancyPermissions.SeatsManage).Should().BeTrue();
    }

    [Fact]
    public async Task A_permission_that_manages_access_is_taken_out_of_a_role_or_archived_with_it_by_an_administrator_alone()
    {
        var harness = Harness.OfHarbor();
        var supervisors = harness.RoleFromStarterRole(HostCatalogue.SupervisorStarterRole);
        var temporary = await harness.SeatAt("Bert", harness.Harbor.Root);
        await harness.Grant(temporary, harness.Harbor.Root, HostCatalogue.AdministratorStarterRole, until: Now.AddDays(7));
        var lasting = await harness.SeatAt("Di", harness.Harbor.North, HostCatalogue.SupervisorStarterRole);
        var before = harness.Store.Role(supervisors).Permissions.ToArray();

        var removing = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Roles.SetPermissionsAsync(supervisors, [HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, TenancyPermissions.SeatsManage, TenancyPermissions.GrantsManage], default)),
            "a permission taken out of a role goes from every holder, which an administrator for a week could not revoke grant by grant");
        removing.Arguments["Missing"].Should().Be(TenancyPermissions.RolesManage);
        removing.Arguments["Role"].Should().Be(supervisors);
        var archiving = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn,
            () => harness.As(temporary, h => h.Roles.ArchiveAsync(supervisors, default)));
        archiving.Arguments["Missing"].Should().Be(TenancyPermissions.RolesManage);
        archiving.Arguments["Role"].Should().Be(supervisors);
        harness.Store.Role(supervisors).Permissions.Should().Equal(before);
        harness.Store.Role(supervisors).Status.Should().Be(RoleStatus.Active);
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == lasting && right.Permission == TenancyPermissions.UnitsManage);

        // Permissions that manage no access, and roles that manage none, stay the temporary administrator's to change.
        await harness.As(temporary, h => h.Roles.SetPermissionsAsync(supervisors, [HostCatalogue.WidgetChange, TenancyPermissions.UnitsManage, TenancyPermissions.SeatsManage, TenancyPermissions.GrantsManage], default));
        harness.Store.Role(supervisors).Holds(HostCatalogue.WidgetCreate).Should().BeFalse();
        await harness.As(temporary, h => h.Roles.ArchiveAsync(harness.RoleFromStarterRole(HostCatalogue.WatcherStarterRole), default));
        harness.Store.Role(harness.RoleFromStarterRole(HostCatalogue.WatcherStarterRole)).Status.Should().Be(RoleStatus.Archived);

        // The lasting administrator and system work do both.
        await harness.As(harness.Administrator, h => h.Roles.SetPermissionsAsync(supervisors, [HostCatalogue.WidgetChange, TenancyPermissions.SeatsManage, TenancyPermissions.GrantsManage], default));
        harness.Store.Role(supervisors).Holds(TenancyPermissions.UnitsManage).Should().BeFalse();
        await harness.BySystemWork(h => h.Roles.ArchiveAsync(supervisors, default));
        harness.Store.Role(supervisors).Status.Should().Be(RoleStatus.Archived);
    }

    [Fact]
    public async Task Creating_needs_roles_manage_tenant_wide()
    {
        var harness = Harness.OfHarbor();
        var supervisor = await harness.SeatAt("Bert", harness.Harbor.Root, HostCatalogue.SupervisorStarterRole);

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.NotPermitted,
            () => harness.As(supervisor, h => h.Roles.CreateAsync("Clerk", "Files widgets", [HostCatalogue.WidgetChange], default)));
        refusal.Arguments["Permission"].Should().Be(TenancyPermissions.RolesManage);

        var clerk = (await harness.As(harness.Administrator, h => h.Roles.CreateAsync(" Clerk ", "Files widgets", [HostCatalogue.WidgetChange], default))).Id;

        var role = harness.Store.Role(clerk);
        role.Name.Should().Be("Clerk");
        role.Permissions.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetRead);
        role.FromStarterRole.Should().BeNull("a role made by hand comes from no starter role");
        role.TenantId.Should().Be(harness.Tenant);
        await Refused.WithCodeAsync(TenancyRefusals.UnknownPermission,
            () => harness.As(harness.Administrator, h => h.Roles.CreateAsync("Pilot", string.Empty, ["widget.fly"], default)));
    }

    [Fact]
    public async Task Role_names_are_unique_per_tenant_ignoring_case()
    {
        var harness = Harness.OfHarbor();
        var other = harness.Seed(2, "quarry");
        var clerk = (await harness.As(harness.Administrator, h => h.Roles.CreateAsync("Clerk", string.Empty, [HostCatalogue.WidgetRead], default))).Id;

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
    public async Task Removing_the_admin_permission_from_the_last_administrators_role_is_refused()
    {
        var harness = Harness.OfHarbor();
        var administrators = harness.RoleFromStarterRole(HostCatalogue.AdministratorStarterRole);

        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin,
            () => harness.As(harness.Administrator, h => h.Roles.SetPermissionsAsync(administrators, [HostCatalogue.WidgetRead, TenancyPermissions.SeatsManage], default)));
        harness.Store.Role(administrators).Holds(TenancyPermissions.AdministratorPermission).Should().BeTrue();

        // A second administrator, through a role of its own, lets the role made from the starter role lose the permission.
        var keepers = (await harness.As(harness.Administrator, h => h.Roles.CreateAsync("Keepers", string.Empty, [TenancyPermissions.RolesManage, TenancyPermissions.SeatsManage], default))).Id;
        var keeper = await harness.SeatAt("Cy", harness.Harbor.Root);
        await harness.As(harness.Administrator, h => h.Seats.GrantAsync(keeper, keepers, harness.Harbor.Root, null, null, default));

        await harness.As(harness.Administrator, h => h.Roles.SetPermissionsAsync(administrators, [HostCatalogue.WidgetRead, TenancyPermissions.SeatsManage], default));

        harness.Store.Role(administrators).Permissions.Should().Equal(TenancyPermissions.SeatsManage, HostCatalogue.WidgetRead);
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin,
            () => harness.As(keeper, h => h.Roles.SetPermissionsAsync(keepers, [TenancyPermissions.SeatsManage], default)));
    }

    [Fact]
    public async Task Archiving_the_last_administrators_role_is_refused()
    {
        var harness = Harness.OfHarbor();
        var administrators = harness.RoleFromStarterRole(HostCatalogue.AdministratorStarterRole);

        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.As(harness.Administrator, h => h.Roles.ArchiveAsync(administrators, default)));
        await Refused.WithCodeAsync(TenancyRefusals.LastAdmin, () => harness.BySystemWork(h => h.Roles.ArchiveAsync(administrators, default)));

        harness.Store.Role(administrators).Status.Should().Be(RoleStatus.Active);
        await harness.As(harness.Administrator, h => h.Roles.ArchiveAsync(harness.RoleFromStarterRole(HostCatalogue.WatcherStarterRole), default));
        harness.Store.Role(harness.RoleFromStarterRole(HostCatalogue.WatcherStarterRole)).Status.Should().Be(RoleStatus.Archived);
    }

    [Fact]
    public async Task Changed_permissions_reach_every_holder_at_the_next_save()
    {
        var harness = Harness.OfHarbor();
        var operatorRole = harness.RoleFromStarterRole(HostCatalogue.OperatorStarterRole);
        var north = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.OperatorStarterRole);
        var south = await harness.SeatAt("Cy", harness.Harbor.South, HostCatalogue.OperatorStarterRole);

        await harness.As(harness.Administrator, h => h.Roles.SetPermissionsAsync(operatorRole, [HostCatalogue.WidgetRead, TenancyPermissions.UnitsManage], default));

        foreach (var (seat, unit) in new[] { (north, harness.Harbor.North), (south, harness.Harbor.South) })
        {
            harness.Store.SavedRights.Where(right => right.SeatId == seat).Select(right => (right.UnitId, right.Permission))
                .Should().BeEquivalentTo([(unit, HostCatalogue.WidgetRead), (unit, TenancyPermissions.UnitsManage)]);
        }

        var reached = await harness.As(north, h =>
            Task.FromResult(new TenancyAccessManager(h.Catalogue, h.Clock, NoServices.Instance)
                .In(h.Store, h.Store).UnitsWhereIHold(TenancyPermissions.UnitsManage).ToArray()));
        reached.Should().BeEquivalentTo([harness.Harbor.North, harness.Harbor.NorthCoast]);
    }
}
