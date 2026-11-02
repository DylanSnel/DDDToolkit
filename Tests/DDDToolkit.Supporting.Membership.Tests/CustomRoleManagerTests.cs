using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.Tests.Support;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// The role manager the generator writes for a role class, <c>PlotRoleManager</c> and <c>ShedRoleManager</c> for the host's
/// two: who may change the roles, the starter roles made once for a scope, the changes, each with the hooks the role
/// calls, and the reads. Over a store kept in memory.
/// </summary>
public sealed class CustomRoleManagerTests
{
    private static readonly GardenId Meadow = GardenId.CreateSequential();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static PlotRoleManager Plots(StubRoleStore<PlotRole, PlotRoleId> store)
        => new(store, new StubAccess<Plot, PlotId>(PlotMembership.Rules), TestUser);

    // ---------------------------------------------------------------- who may change the roles

    [Fact]
    public async Task Rules_that_name_no_permission_for_the_roles_let_only_system_work_change_them()
    {
        var store = new StubRoleStore<PlotRole, PlotRoleId>();
        var roles = Plots(store);

        using (TestAccess.BeginAs(Guid.NewGuid()))
        {
            var refused = await Refused.WithCodeAsync(
                PlotMembership.Codes,
                MembershipRefusals.NotPermitted,
                () => roles.CreateAsync(new PlotRole(PlotRoleId.CreateSequential(), Meadow, new CustomRoleDraft("Pruner", null, [PlotPermissions.Plant])), Cancellation));
            refused.Message.Should().Contain("managedWith");
            store.Kept.Should().BeEmpty();
        }

        using (TestAccess.BeginSystemBypassingRowAccess())
        {
            var made = await roles.CreateAsync(new PlotRole(PlotRoleId.CreateSequential(), Meadow, new CustomRoleDraft("Pruner", null, [PlotPermissions.Plant])), Cancellation);
            store.Kept.Should().Equal(made);
            store.Saves.Should().Be(1);
        }
    }

    [Fact]
    public async Task Rules_that_name_the_permission_ask_what_the_application_registered_and_only_system_work_where_it_registered_nothing()
    {
        var store = new StubRoleStore<ShedRole, ShedRoleId>();
        var access = new StubAccess<Shed, ShedId>(ShedMembership.Rules);
        var asked = new RoleAccess();

        using (TestAccess.BeginAs(Guid.NewGuid()))
        {
            // Nothing answers whether the caller holds it for the whole of what the roles are kept for.
            (await Refused.WithCodeAsync(ShedMembership.Codes, MembershipRefusals.NotPermitted, () => new ShedRoleManager(store, access, TestUser).CreateStarterRolesAsync(Keeper, Cancellation)))
                .Arguments.Should().Contain("Permission", ShedMembership.ManageRoles);

            // What answers is asked for the permission the rules name, and refuses as it refuses.
            await FluentActions.Awaiting(() => new ShedRoleManager(store, access, TestUser, asked).CreateStarterRolesAsync(Keeper, Cancellation)).Should().ThrowAsync<RefusalException>();
            asked.Held = true;
            var made = await new ShedRoleManager(store, access, TestUser, asked).CreateStarterRolesAsync(Keeper, Cancellation);
            made.Select(role => role.FromStarterRole).Should().Equal(ShedMembership.Keeper);
            asked.Asked.Should().Equal(ShedMembership.ManageRoles, ShedMembership.ManageRoles);
        }
    }

    // ---------------------------------------------------------------- the starter roles

    [Fact]
    public async Task A_starter_role_is_made_once_for_a_scope_whatever_became_of_it()
    {
        var store = new StubRoleStore<PlotRole, PlotRoleId>();
        var roles = Plots(store);
        using var system = TestAccess.BeginSystemBypassingRowAccess();

        // The starter roles the rules declare, each through the host's constructor, which says whose it is.
        var made = await roles.CreateStarterRolesAsync(draft => new PlotRole(PlotRoleId.CreateSequential(), Meadow, draft), Cancellation, scope: role => role.GardenId == Meadow);
        made.Select(role => (role.Name, role.FromStarterRole, role.GardenId)).Should().Equal(
            [(PlotMembership.Tender, PlotMembership.Tender, Meadow), (PlotMembership.Waterer, PlotMembership.Waterer, Meadow)],
            "the starter roles the rules declare, and none for the owner, which holds no role");
        made[0].Permissions.Should().Equal(PlotPermissions.Plant, PlotPermissions.See, PlotPermissions.Water);

        // Asked again: nothing is missing. Renamed and archived: told by what it was made from, so still there.
        (await roles.CreateStarterRolesAsync(draft => new PlotRole(PlotRoleId.CreateSequential(), Meadow, draft), Cancellation, scope: role => role.GardenId == Meadow)).Should().BeEmpty();
        await roles.RenameAsync(made[0], "Grower", null, Cancellation);
        await roles.ArchiveAsync(made[1].Id, Cancellation);
        (await roles.CreateStarterRolesAsync(draft => new PlotRole(PlotRoleId.CreateSequential(), Meadow, draft), Cancellation, scope: role => role.GardenId == Meadow)).Should().BeEmpty();

        // Another garden, as its scope says, has none of them yet; and a role a customer made with a starter role's name is no starter role.
        var orchard = GardenId.CreateSequential();
        store.Add(new PlotRole(PlotRoleId.CreateSequential(), orchard, new CustomRoleDraft(PlotMembership.Tender, null, [PlotPermissions.See])));
        (await roles.CreateStarterRolesAsync(draft => new PlotRole(PlotRoleId.CreateSequential(), orchard, draft), Cancellation, scope: role => role.GardenId == orchard))
            .Select(role => role.FromStarterRole).Should().Equal(PlotMembership.Tender, PlotMembership.Waterer);
    }

    // ---------------------------------------------------------------- following the starter roles

    private static MembershipRules WhereTheKeeperHolds(params string[] permissions)
        => MembershipRules.For<Shed>(
            Roles.Custom(starters: [new(ShedMembership.Keeper, permissions)], otherPermissions: OtherPermissions.All, managedWith: ShedMembership.ManageRoles));

    [Fact]
    public async Task Following_makes_what_is_missing_and_makes_the_rest_follow_in_one_save()
    {
        var store = new StubRoleStore<PlotRole, PlotRoleId>();
        var kept = new PlotRole(
            PlotRoleId.CreateSequential(),
            Meadow,
            new CustomRoleDraft(PlotMembership.Waterer, null, [PlotPermissions.See], PlotMembership.Waterer));
        Break.Set(kept, nameof(kept.PermissionsFromStarterRole), new[] { PlotPermissions.See });
        store.Add(kept);
        using var system = TestAccess.BeginSystemBypassingRowAccess();

        var followed = await Plots(store).FollowStarterRolesAsync(draft => new PlotRole(PlotRoleId.CreateSequential(), Meadow, draft), Cancellation);

        followed.Made.Select(role => role.FromStarterRole).Should().Equal(PlotMembership.Tender);
        followed.Changed.Should().Equal(kept.Id);
        followed.WithoutTheirStarterRole.Should().BeEmpty();
        kept.Permissions.Should().Equal(PlotPermissions.See, PlotPermissions.Water);
        store.Kept.Should().HaveCount(2);
        store.Saves.Should().Be(1, "making and following are one save");
    }

    [Fact]
    public async Task A_second_run_in_a_row_saves_nothing()
    {
        var store = new StubRoleStore<PlotRole, PlotRoleId>();
        var roles = Plots(store);
        using var system = TestAccess.BeginSystemBypassingRowAccess();
        await roles.FollowStarterRolesAsync(draft => new PlotRole(PlotRoleId.CreateSequential(), Meadow, draft), Cancellation);
        store.Saves.Should().Be(1);

        var again = await roles.FollowStarterRolesAsync(draft => new PlotRole(PlotRoleId.CreateSequential(), Meadow, draft), Cancellation);

        (again.Made.Count, again.Changed.Count, again.WithoutTheirStarterRole.Count).Should().Be((0, 0, 0));
        store.Saves.Should().Be(1, "nothing changed, so nothing is saved");
    }

    [Fact]
    public async Task Following_is_system_works_alone()
    {
        var store = new StubRoleStore<ShedRole, ShedRoleId>();
        var asked = new RoleAccess { Held = true };
        var roles = new ShedRoleManager(store, new StubAccess<Shed, ShedId>(ShedMembership.Rules), TestUser, asked);

        using (TestAccess.BeginAs(Guid.NewGuid()))
        {
            var refused = await FluentActions.Awaiting(() => roles.FollowStarterRolesAsync(Keeper, Cancellation)).Should().ThrowAsync<RefusalException>();
            refused.Which.Code.Should().Be(ToolkitRefusals.SystemOnly, "whoever holds the permission that manages the roles changes a role, and what a starter role holds is the application's");
        }

        store.Kept.Should().BeEmpty();
        store.Saves.Should().Be(0);
        asked.Asked.Should().BeEmpty();

        // System work in any scope follows: Tenancy's own runs it for every module.
        using (TestAccess.BeginSystem("tenancy"))
        {
            (await roles.FollowStarterRolesAsync(Keeper, Cancellation)).Made.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task An_archived_role_follows_nothing_and_is_not_made_again_and_a_renamed_role_follows()
    {
        var store = new StubRoleStore<ShedRole, ShedRoleId>();
        using var system = TestAccess.BeginSystemBypassingRowAccess();
        var keeper = Keeper(new CustomRoleDraft(ShedMembership.Keeper, null, [ShedPermissions.Open, ShedPermissions.Stock], ShedMembership.Keeper));
        store.Add(keeper);
        keeper.Archive(ShedMembership.Rules);

        var followed = await new ShedRoleManager(store, new StubAccess<Shed, ShedId>(WhereTheKeeperHolds(ShedPermissions.Open)), TestUser)
            .FollowStarterRolesAsync(Keeper, Cancellation);

        (followed.Made.Count, followed.Changed.Count, followed.WithoutTheirStarterRole.Count).Should().Be((0, 0, 0));
        keeper.Permissions.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock);
        store.Saves.Should().Be(0);

        var renamedStore = new StubRoleStore<ShedRole, ShedRoleId>();
        var renamed = Keeper(new CustomRoleDraft(ShedMembership.Keeper, null, [ShedPermissions.Open, ShedPermissions.Stock], ShedMembership.Keeper));
        renamed.Rename("Shed boss", null, ShedMembership.Rules);
        renamedStore.Add(renamed);

        followed = await new ShedRoleManager(renamedStore, new StubAccess<Shed, ShedId>(WhereTheKeeperHolds(ShedPermissions.Open)), TestUser)
            .FollowStarterRolesAsync(Keeper, Cancellation);

        followed.Changed.Should().Equal(renamed.Id);
        renamed.Permissions.Should().Equal(ShedPermissions.Open);
        renamedStore.Kept.Should().ContainSingle("told by what it was made from, so it is not made again");
    }

    [Fact]
    public async Task A_role_whose_starter_role_the_rules_no_longer_declare_is_counted_and_keeps_its_permissions()
    {
        var store = new StubRoleStore<ShedRole, ShedRoleId>();
        using var system = TestAccess.BeginSystemBypassingRowAccess();
        var keeper = Keeper(new CustomRoleDraft(ShedMembership.Keeper, null, [ShedPermissions.Open, ShedPermissions.Stock], ShedMembership.Keeper));
        store.Add(keeper);
        var withoutIt = MembershipRules.For<Shed>(Roles.Custom(otherPermissions: OtherPermissions.All, managedWith: ShedMembership.ManageRoles));

        var followed = await new ShedRoleManager(store, new StubAccess<Shed, ShedId>(withoutIt), TestUser).FollowStarterRolesAsync(Keeper, Cancellation);

        followed.WithoutTheirStarterRole.Should().Equal(keeper.Id);
        followed.Made.Should().BeEmpty();
        store.Saves.Should().Be(0, "nothing changed");
        keeper.Permissions.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock);
    }

    [Fact]
    public async Task Rules_with_no_starter_roles_do_nothing()
    {
        var store = new StubRoleStore<ShedRole, ShedRoleId>();
        using var system = TestAccess.BeginSystemBypassingRowAccess();
        var none = MembershipRules.For<Shed>(Roles.Custom(otherPermissions: OtherPermissions.All, managedWith: ShedMembership.ManageRoles));

        var followed = await new ShedRoleManager(store, new StubAccess<Shed, ShedId>(none), TestUser).FollowStarterRolesAsync(Keeper, Cancellation);

        followed.Made.Should().BeEmpty();
        store.Saves.Should().Be(0);
    }

    // ---------------------------------------------------------------- the changes

    [Fact]
    public async Task Each_change_loads_the_role_hands_it_the_rules_and_the_role_calls_its_hooks()
    {
        var store = new StubRoleStore<PlotRole, PlotRoleId>();
        var roles = Plots(store);
        using var system = TestAccess.BeginSystemBypassingRowAccess();
        var pruner = await roles.CreateAsync(new PlotRole(PlotRoleId.CreateSequential(), Meadow, new CustomRoleDraft("Pruner", null, [PlotPermissions.Plant])), Cancellation);

        (await roles.RenameAsync(pruner.Id, " Hedger ", "Trims.", Cancellation)).Should().BeSameAs(pruner);
        await roles.RenameAsync(pruner, "Hedger", "Trims.", Cancellation);
        await roles.SetPermissionsAsync(pruner.Id, [PlotPermissions.Plant, PlotPermissions.Fence], Cancellation);
        await roles.ArchiveAsync(pruner, Cancellation);

        (pruner.Name, pruner.Description, pruner.Status).Should().Be(("Hedger", "Trims.", CustomRoleStatus.Archived));
        pruner.DomainEvents.Select(raised => raised.GetType()).Should().Equal(
            [typeof(PlotRoleMade), typeof(PlotRoleRenamed), typeof(PlotRolePermissionsChanged), typeof(PlotRolePutAway)],
            "the hooks run after a change that changed something, and a rename to the same name changed nothing");
        store.Saves.Should().Be(5);

        // The role's own rules, under the resource's codes; and a role that is not there for the caller.
        await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleIsArchived, () => roles.RenameAsync(pruner.Id, "Pruner", null, Cancellation));
        (await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleNotFound, () => roles.ArchiveAsync(PlotRoleId.CreateSequential(), Cancellation)))
            .Code.Should().Be("plots.role-not-found");
    }

    // ---------------------------------------------------------------- the reads

    [Fact]
    public async Task The_roles_are_read_by_name_and_by_id_asking_no_permission()
    {
        var store = new StubRoleStore<PlotRole, PlotRoleId>();
        var waterer = new PlotRole(PlotRoleId.CreateSequential(), Meadow, new CustomRoleDraft("Waterer", null, [PlotPermissions.Water]));
        var fencer = new PlotRole(PlotRoleId.CreateSequential(), GardenId.CreateSequential(), new CustomRoleDraft("Fencer", null, [PlotPermissions.Fence]));
        store.Add(waterer);
        store.Add(fencer);
        var roles = Plots(store);

        using (TestAccess.BeginAs(Guid.NewGuid()))
        {
            (await roles.ListAsync(Cancellation)).Should().Equal(fencer, waterer);
            (await roles.ListAsync(Cancellation, scope: role => role.GardenId == Meadow)).Should().Equal(waterer);
            (await roles.ByIdAsync([waterer.Id, PlotRoleId.CreateSequential(), waterer.Id], Cancellation)).Should().Equal(waterer);
            (await roles.ByIdAsync([], Cancellation)).Should().BeEmpty();
        }

        await FluentActions.Awaiting(() => roles.ByIdAsync([.. Enumerable.Range(0, PlotRoleManager.MostIds + 1).Select(_ => PlotRoleId.CreateSequential())], Cancellation))
            .Should().ThrowAsync<ArgumentException>();
    }

    private static ShedRole Keeper(CustomRoleDraft draft) => new(ShedRoleId.CreateSequential(), draft);

    /// <summary>What the host answers about the permission that changes the sheds' roles.</summary>
    private sealed class RoleAccess : ICustomRoleAccess<Shed>
    {
        public bool Held { get; set; }

        public List<string> Asked { get; } = [];

        public ValueTask RequireAsync(string permission, CancellationToken cancellationToken)
        {
            Asked.Add(permission);
            return Held ? ValueTask.CompletedTask : throw ShedMembership.Codes.Refuse(MembershipRefusals.NotPermitted, ("Permission", permission));
        }
    }
}
