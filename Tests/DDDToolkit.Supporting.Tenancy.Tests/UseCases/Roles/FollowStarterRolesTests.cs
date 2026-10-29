using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Following the starter roles is system work in one tenant: every active role made from a starter role follows it, as the catalogue
/// the application runs with now builds it, in one save, with one event for each role whose permissions changed. A second
/// run writes nothing. No seat runs it, and the last-administrator rule holds for it as for anyone.
/// <para>
/// Harbor is provisioned from the host's catalogue; the application then ships another, in which the watcher's starter role
/// also creates widgets and the operator's no longer does.
/// </para>
/// </summary>
public class FollowStarterRolesTests
{
    [Fact]
    public async Task System_work_makes_every_role_of_its_tenant_follow_its_starter_role_in_one_save()
    {
        var harness = Harness.OfHarbor();
        var orchard = harness.Seed(2, "orchard");
        var watcher = harness.RoleFromStarterRole(HostCatalogue.WatcherStarterRole);
        var operatorRole = harness.RoleFromStarterRole(HostCatalogue.OperatorStarterRole);
        var later = Later();
        var saves = harness.Store.SaveCount;

        var followed = await harness.BySystemWork(h => Roles(h, later).FollowStarterRolesAsync(default));

        followed.Changed.Should().Equal(operatorRole, watcher);
        followed.KeptForAnAdministrator.Should().BeEmpty();
        followed.WithoutTheirStarterRole.Should().BeEmpty();
        harness.Store.SaveCount.Should().Be(saves + 1, "every role of the tenant changes in one save");
        harness.Store.Role(watcher).Permissions.Should().Equal(HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
        harness.Store.Role(operatorRole).Permissions.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetRead);

        var events = harness.Store.SavedEvents.OfType<RoleFollowedItsStarterRole<TenantId, RoleId, SeatId>>().ToArray();
        events.Select(followedStarterRole => (followedStarterRole.RoleId, followedStarterRole.StarterRole, string.Join(",", followedStarterRole.Added), string.Join(",", followedStarterRole.Removed)))
            .Should().BeEquivalentTo([
                (watcher, HostCatalogue.WatcherStarterRole, HostCatalogue.WidgetCreate, string.Empty),
                (operatorRole, HostCatalogue.OperatorStarterRole, string.Empty, HostCatalogue.WidgetCreate),
            ]);
        events.Should().OnlyContain(followedStarterRole => followedStarterRole.TenantId == harness.Tenant && followedStarterRole.By == TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope, null));
        harness.Store.SavedRights.Where(right => right.RoleId == watcher).Select(right => right.Permission).Should().BeEmpty("nobody holds the watcher's role");

        // Orchard's roles are Orchard's: system work in Harbor reaches none of them.
        harness.Store.Role(orchard.RolesByStarterRole[HostCatalogue.WatcherStarterRole].Id).Permissions.Should().Equal(HostCatalogue.WidgetRead);

        // A second run reads, and writes nothing.
        var again = await harness.BySystemWork(h => Roles(h, later).FollowStarterRolesAsync(default));
        again.Changed.Should().BeEmpty();
        harness.Store.SaveCount.Should().Be(saves + 1);
        harness.Store.Calls.Should().Contain(nameof(InMemoryTenancyStore.SerializeAccessChangesAsync), "it takes the access revision before it reads anything");
    }

    [Fact]
    public async Task What_the_tenant_changed_itself_stays_through_the_save()
    {
        var harness = Harness.OfHarbor();
        var operatorRole = harness.RoleFromStarterRole(HostCatalogue.OperatorStarterRole);
        await harness.As(harness.Administrator, h => h.Roles.SetPermissionsAsync(operatorRole, [HostCatalogue.WidgetChange], default));

        await harness.BySystemWork(h => Roles(h, Later()).FollowStarterRolesAsync(default));

        harness.Store.Role(operatorRole).Permissions.Should().Equal([HostCatalogue.WidgetChange, HostCatalogue.WidgetRead], "the tenant had taken creating out already");
        harness.Store.Role(operatorRole).PermissionsFromStarterRole.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetRead);
        harness.Store.SavedEvents.OfType<RoleFollowedItsStarterRole<TenantId, RoleId, SeatId>>().Should().NotContain(followed => followed.RoleId == operatorRole);
    }

    [Fact]
    public async Task No_seat_follows_the_starter_roles_not_even_an_administrator()
    {
        var harness = Harness.OfHarbor();

        await Refused.WithCodeAsync(ToolkitRefusals.SystemOnly, () => harness.As(harness.Administrator, h => Roles(h, Later()).FollowStarterRolesAsync(default)));
        (await FluentActions.Awaiting(() => harness.Run(HostUser.System, h => Roles(h, Later()).FollowStarterRolesAsync(default)))
            .Should().ThrowAsync<InvalidOperationException>("system work outside any tenant only provisions"))
            .WithMessage("A tenant's roles are made to follow their starter roles by system work in that tenant. Begin access.BeginSystemIn(tenant) for it, "
                + "on the TenancyAccessManager your code takes.");
        harness.Store.Role(harness.RoleFromStarterRole(HostCatalogue.WatcherStarterRole)).Permissions.Should().Equal(HostCatalogue.WidgetRead);
    }

    [Fact]
    public async Task A_role_whose_permissions_were_stored_in_another_order_is_saved_in_order_and_no_change_is_told()
    {
        // A seat's copy of a starter role may hold the permissions in any order: the same permissions are not a change of the starter role's.
        var harness = Harness.OfHarbor();
        var operatorRole = harness.RoleFromStarterRole(HostCatalogue.OperatorStarterRole);
        typeof(RoleAggregate<RoleId, TenantId>).GetProperty(nameof(HostRole.Permissions))!
            .SetValue(harness.Store.Role(operatorRole), new[] { HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate, HostCatalogue.WidgetChange });
        var saves = harness.Store.SaveCount;

        var followed = await harness.BySystemWork(h => Roles(h, harness.Catalogue).FollowStarterRolesAsync(default));

        followed.Changed.Should().BeEmpty();
        harness.Store.SavedEvents.OfType<RoleFollowedItsStarterRole<TenantId, RoleId, SeatId>>().Should().BeEmpty();
        harness.Store.SaveCount.Should().Be(saves + 1, "the permissions are saved in the order a role keeps them");
        harness.Store.Role(operatorRole).Permissions.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
    }

    [Fact]
    public async Task A_permission_that_manages_access_reaches_the_role_and_its_holders_without_an_administrator()
    {
        var harness = Harness.OfHarbor();
        var operatorRole = harness.RoleFromStarterRole(HostCatalogue.OperatorStarterRole);
        var holder = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.OperatorStarterRole);
        var later = Catalogue(starterRoles => starterRoles.Select(starterRole => starterRole.Key == HostCatalogue.OperatorStarterRole ? starterRole with { Permissions = [.. starterRole.Permissions, TenancyPermissions.UnitsManage] } : starterRole));

        var followed = await harness.Run(harness.SystemUser(harness.Administrator), h => Roles(h, later).FollowStarterRolesAsync(default));

        followed.Changed.Should().Equal(operatorRole);
        var raised = harness.Store.SavedEvents.OfType<RoleFollowedItsStarterRole<TenantId, RoleId, SeatId>>().Single();
        raised.Added.Should().Equal(TenancyPermissions.UnitsManage);
        raised.ManagingAccess.Should().Equal(TenancyPermissions.UnitsManage);
        raised.By.Should().Be(TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope, harness.Administrator), "the event names the work and the seat it was done for");
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == holder && right.Permission == TenancyPermissions.UnitsManage && right.UnitId == harness.Harbor.North);
    }

    [Fact]
    public async Task Archived_and_hand_made_roles_are_passed_by_and_a_role_whose_starter_role_is_gone_keeps_its_permissions()
    {
        var harness = Harness.OfHarbor();
        var watcher = harness.RoleFromStarterRole(HostCatalogue.WatcherStarterRole);
        var operatorRole = harness.RoleFromStarterRole(HostCatalogue.OperatorStarterRole);
        var desk = (await harness.BySystemWork(h => h.Roles.CreateAsync("Desk", "Answers the phone", [HostCatalogue.WidgetRead], default))).Id;
        await harness.BySystemWork(h => h.Roles.ArchiveAsync(watcher, default));
        var later = Catalogue(starterRoles => starterRoles
            .Where(starterRole => starterRole.Key != HostCatalogue.OperatorStarterRole)
            .Select(starterRole => starterRole.Key == HostCatalogue.WatcherStarterRole ? starterRole with { Permissions = [HostCatalogue.WidgetCreate] } : starterRole));

        var followed = await harness.BySystemWork(h => Roles(h, later).FollowStarterRolesAsync(default));

        followed.Changed.Should().BeEmpty();
        followed.WithoutTheirStarterRole.Should().Equal(operatorRole);
        harness.Store.Role(operatorRole).Permissions.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
        harness.Store.Role(watcher).Permissions.Should().Equal([HostCatalogue.WidgetRead], "an archived role grants nothing, and follows nothing");
        harness.Store.Role(desk).Permissions.Should().Equal(HostCatalogue.WidgetRead);
    }

    [Fact]
    public async Task A_role_whose_following_would_leave_no_administrator_waits_for_another()
    {
        // Bert administers through the operator's role alone, whose starter role held the administrator permission; the
        // application then takes that permission out of the starter role.
        var earlier = Catalogue(starterRoles => starterRoles.Select(starterRole => starterRole.Key == HostCatalogue.OperatorStarterRole ? starterRole with { Permissions = [.. starterRole.Permissions, TenancyPermissions.RolesManage] } : starterRole));
        var harness = Harness.OfHarbor(earlier);
        var root = harness.Harbor.Root;
        var operatorRole = harness.RoleFromStarterRole(HostCatalogue.OperatorStarterRole);
        var administrators = harness.Harbor.AdministratorRole.Id;
        var bert = await harness.SeatAt("Bert", root, HostCatalogue.OperatorStarterRole);
        await harness.BySystemWork(h => h.Seats.RevokeAsync(harness.Administrator, administrators, root, default));
        var later = New.Catalogue();

        var held = await harness.BySystemWork(h => Roles(h, later).FollowStarterRolesAsync(default));

        held.KeptForAnAdministrator.Should().Equal(operatorRole);
        held.Changed.Should().BeEmpty();
        harness.Store.Role(operatorRole).Holds(TenancyPermissions.RolesManage).Should().BeTrue("Bert is the tenant's last administrator, through this role");
        harness.Store.Role(operatorRole).PermissionsFromStarterRole.Should().Contain(TenancyPermissions.RolesManage, "the role is left as it was, record and all, to follow later");

        await harness.Grant(bert, root, HostCatalogue.AdministratorStarterRole);
        var followed = await harness.BySystemWork(h => Roles(h, later).FollowStarterRolesAsync(default));

        followed.Changed.Should().Equal(operatorRole);
        harness.Store.Role(operatorRole).Holds(TenancyPermissions.RolesManage).Should().BeFalse();
    }

    /// <summary>The role use cases over the harness's store, as the application runs them with <paramref name="catalogue"/>.</summary>
    private static HostRoleManager Roles(Harness harness, TenancyCatalogue catalogue)
        => new(harness.Store, catalogue, harness.Clock);

    /// <summary>The catalogue a later version of the application ships: the watcher's starter role creates widgets too, and the operator's no longer does.</summary>
    private static TenancyCatalogue Later()
        => Catalogue(starterRoles => starterRoles.Select(starterRole => starterRole.Key switch
        {
            HostCatalogue.WatcherStarterRole => starterRole with { Permissions = [HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate] },
            HostCatalogue.OperatorStarterRole => starterRole with { Permissions = [HostCatalogue.WidgetChange] },
            _ => starterRole,
        }));

    /// <summary>The host's catalogue with its starter roles changed.</summary>
    private static TenancyCatalogue Catalogue(Func<IEnumerable<StarterRole>, IEnumerable<StarterRole>> starterRoles)
        => TenancyCatalogue.Build(HostCatalogue.Application with { StarterRoles = [.. starterRoles(HostCatalogue.Application.StarterRoles)] }, []);
}
