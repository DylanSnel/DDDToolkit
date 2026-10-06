using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Following the packs is system work in one tenant: every active role made from a pack follows it, as the catalogue
/// the application runs with now builds it, in one save, with one event for each role whose keys changed. A second
/// run writes nothing. No seat runs it, and the last-administrator rule holds for it as for anyone.
/// <para>
/// Harbor is provisioned from the host's catalogue; the application then ships another, in which the watcher's pack
/// also creates widgets and the operator's no longer does.
/// </para>
/// </summary>
public class FollowPacksTests
{
    [Fact]
    public async Task System_work_makes_every_role_of_its_tenant_follow_its_pack_in_one_save()
    {
        var harness = Harness.OfHarbor();
        var orchard = harness.Seed(2, "orchard");
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);
        var later = Later();
        var saves = harness.Store.SaveCount;

        var followed = await harness.BySystemWork(h => Roles(h, later).FollowPacksAsync(default));

        followed.Changed.Should().Equal(operatorRole, watcher);
        followed.KeptForAnAdministrator.Should().BeEmpty();
        followed.WithoutTheirPack.Should().BeEmpty();
        harness.Store.SaveCount.Should().Be(saves + 1, "every role of the tenant changes in one save");
        harness.Store.Role(watcher).Keys.Should().Equal(HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
        harness.Store.Role(operatorRole).Keys.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetRead);

        var events = harness.Store.SavedEvents.OfType<RoleFollowedItsPack<TenantId, RoleId, SeatId>>().ToArray();
        events.Select(followedPack => (followedPack.RoleId, followedPack.Pack, string.Join(",", followedPack.Added), string.Join(",", followedPack.Removed)))
            .Should().BeEquivalentTo([
                (watcher, HostCatalogue.WatcherPack, HostCatalogue.WidgetCreate, string.Empty),
                (operatorRole, HostCatalogue.OperatorPack, string.Empty, HostCatalogue.WidgetCreate),
            ]);
        events.Should().OnlyContain(followedPack => followedPack.TenantId == harness.Tenant && followedPack.By == TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope, null));
        harness.Store.SavedRights.Where(right => right.RoleId == watcher).Select(right => right.Key).Should().BeEmpty("nobody holds the watcher's role");

        // Orchard's roles are Orchard's: system work in Harbor reaches none of them.
        harness.Store.Role(orchard.RolesByPack[HostCatalogue.WatcherPack].Id).Keys.Should().Equal(HostCatalogue.WidgetRead);

        // A second run reads, and writes nothing.
        var again = await harness.BySystemWork(h => Roles(h, later).FollowPacksAsync(default));
        again.Changed.Should().BeEmpty();
        harness.Store.SaveCount.Should().Be(saves + 1);
        harness.Store.Calls.Should().Contain(nameof(InMemoryTenancyStore.SerializeAccessChangesAsync), "it takes the access revision before it reads anything");
    }

    [Fact]
    public async Task What_the_tenant_changed_itself_stays_through_the_save()
    {
        var harness = Harness.OfHarbor();
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);
        await harness.As(harness.Administrator, h => h.Roles.SetKeysAsync(operatorRole, [HostCatalogue.WidgetChange], default));

        await harness.BySystemWork(h => Roles(h, Later()).FollowPacksAsync(default));

        harness.Store.Role(operatorRole).Keys.Should().Equal([HostCatalogue.WidgetChange, HostCatalogue.WidgetRead], "the tenant had taken creating out already");
        harness.Store.Role(operatorRole).KeysFromPack.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetRead);
        harness.Store.SavedEvents.OfType<RoleFollowedItsPack<TenantId, RoleId, SeatId>>().Should().NotContain(followed => followed.RoleId == operatorRole);
    }

    [Fact]
    public async Task No_seat_follows_the_packs_not_even_an_administrator()
    {
        var harness = Harness.OfHarbor();

        await Refused.WithCodeAsync(ToolkitRefusals.SystemOnly, () => harness.As(harness.Administrator, h => Roles(h, Later()).FollowPacksAsync(default)));
        (await FluentActions.Awaiting(() => harness.Run(HostCaller.System, h => Roles(h, Later()).FollowPacksAsync(default)))
            .Should().ThrowAsync<InvalidOperationException>("system work outside any tenant only provisions"))
            .WithMessage("A tenant's roles are made to follow their packs by system work in that tenant. Begin TenancyWork.BeginSystemIn(tenant) for it.");
        harness.Store.Role(harness.RoleFromPack(HostCatalogue.WatcherPack)).Keys.Should().Equal(HostCatalogue.WidgetRead);
    }

    [Fact]
    public async Task A_role_whose_keys_were_stored_in_another_order_is_saved_in_order_and_no_change_is_told()
    {
        // A seat's copy of a pack may hold the keys in any order: the same keys are not a change of the pack's.
        var harness = Harness.OfHarbor();
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);
        typeof(RoleAggregate<RoleId, TenantId>).GetProperty(nameof(HostRole.Keys))!
            .SetValue(harness.Store.Role(operatorRole), new[] { HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate, HostCatalogue.WidgetChange });
        var saves = harness.Store.SaveCount;

        var followed = await harness.BySystemWork(h => Roles(h, harness.Catalogue).FollowPacksAsync(default));

        followed.Changed.Should().BeEmpty();
        harness.Store.SavedEvents.OfType<RoleFollowedItsPack<TenantId, RoleId, SeatId>>().Should().BeEmpty();
        harness.Store.SaveCount.Should().Be(saves + 1, "the keys are saved in the order a role keeps them");
        harness.Store.Role(operatorRole).Keys.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
    }

    [Fact]
    public async Task A_key_that_manages_access_reaches_the_role_and_its_holders_without_an_administrator()
    {
        var harness = Harness.OfHarbor();
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);
        var holder = await harness.SeatAt("Bert", harness.Harbor.North, HostCatalogue.OperatorPack);
        var later = Catalogue(packs => packs.Select(pack => pack.Key == HostCatalogue.OperatorPack ? pack with { Keys = [.. pack.Keys, TenancyKeys.UnitsManage] } : pack));

        var followed = await harness.Run(harness.SystemCaller(harness.Administrator), h => Roles(h, later).FollowPacksAsync(default));

        followed.Changed.Should().Equal(operatorRole);
        var raised = harness.Store.SavedEvents.OfType<RoleFollowedItsPack<TenantId, RoleId, SeatId>>().Single();
        raised.Added.Should().Equal(TenancyKeys.UnitsManage);
        raised.ManagingAccess.Should().Equal(TenancyKeys.UnitsManage);
        raised.By.Should().Be(TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope, harness.Administrator), "the event names the work and the seat it was done for");
        harness.Store.SavedRights.Should().Contain(right => right.SeatId == holder && right.Key == TenancyKeys.UnitsManage && right.UnitId == harness.Harbor.North);
    }

    [Fact]
    public async Task Archived_and_hand_made_roles_are_passed_by_and_a_role_whose_pack_is_gone_keeps_its_keys()
    {
        var harness = Harness.OfHarbor();
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);
        var desk = await harness.BySystemWork(h => h.Roles.CreateAsync("Desk", "Answers the phone", [HostCatalogue.WidgetRead], default));
        await harness.BySystemWork(h => h.Roles.ArchiveAsync(watcher, default));
        var later = Catalogue(packs => packs
            .Where(pack => pack.Key != HostCatalogue.OperatorPack)
            .Select(pack => pack.Key == HostCatalogue.WatcherPack ? pack with { Keys = [HostCatalogue.WidgetCreate] } : pack));

        var followed = await harness.BySystemWork(h => Roles(h, later).FollowPacksAsync(default));

        followed.Changed.Should().BeEmpty();
        followed.WithoutTheirPack.Should().Equal(operatorRole);
        harness.Store.Role(operatorRole).Keys.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, HostCatalogue.WidgetRead);
        harness.Store.Role(watcher).Keys.Should().Equal([HostCatalogue.WidgetRead], "an archived role grants nothing, and follows nothing");
        harness.Store.Role(desk).Keys.Should().Equal(HostCatalogue.WidgetRead);
    }

    [Fact]
    public async Task A_role_whose_following_would_leave_no_administrator_waits_for_another()
    {
        // Bert administers through the operator's role alone, whose pack held the administrator key; the
        // application then takes that key out of the pack.
        var earlier = Catalogue(packs => packs.Select(pack => pack.Key == HostCatalogue.OperatorPack ? pack with { Keys = [.. pack.Keys, TenancyKeys.RolesManage] } : pack));
        var harness = Harness.OfHarbor(earlier);
        var root = harness.Harbor.Root;
        var operatorRole = harness.RoleFromPack(HostCatalogue.OperatorPack);
        var administrators = harness.Harbor.AdministratorRole.Id;
        var bert = await harness.SeatAt("Bert", root, HostCatalogue.OperatorPack);
        await harness.BySystemWork(h => h.Seats.RevokeAsync(harness.Administrator, root, administrators, default));
        var later = New.Catalogue();

        var held = await harness.BySystemWork(h => Roles(h, later).FollowPacksAsync(default));

        held.KeptForAnAdministrator.Should().Equal(operatorRole);
        held.Changed.Should().BeEmpty();
        harness.Store.Role(operatorRole).Holds(TenancyKeys.RolesManage).Should().BeTrue("Bert is the tenant's last administrator, through this role");
        harness.Store.Role(operatorRole).KeysFromPack.Should().Contain(TenancyKeys.RolesManage, "the role is left as it was, record and all, to follow later");

        await harness.Grant(bert, root, HostCatalogue.AdministratorPack);
        var followed = await harness.BySystemWork(h => Roles(h, later).FollowPacksAsync(default));

        followed.Changed.Should().Equal(operatorRole);
        harness.Store.Role(operatorRole).Holds(TenancyKeys.RolesManage).Should().BeFalse();
    }

    /// <summary>The role use cases over the harness's store, as the application runs them with <paramref name="catalogue"/>.</summary>
    private static HostTenancy.RoleCommands Roles(Harness harness, TenancyCatalogue catalogue)
        => new(harness.Store, catalogue, harness.Clock);

    /// <summary>The catalogue a later version of the application ships: the watcher's pack creates widgets too, and the operator's no longer does.</summary>
    private static TenancyCatalogue Later()
        => Catalogue(packs => packs.Select(pack => pack.Key switch
        {
            HostCatalogue.WatcherPack => pack with { Keys = [HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate] },
            HostCatalogue.OperatorPack => pack with { Keys = [HostCatalogue.WidgetChange] },
            _ => pack,
        }));

    /// <summary>The host's catalogue with its packs changed.</summary>
    private static TenancyCatalogue Catalogue(Func<IEnumerable<RolePack>, IEnumerable<RolePack>> packs)
        => TenancyCatalogue.Build(HostCatalogue.Application with { Packs = [.. packs(HostCatalogue.Application.Packs)] }, []);
}
