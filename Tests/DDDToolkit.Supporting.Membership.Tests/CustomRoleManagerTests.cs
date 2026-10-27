using DDDToolkit.Exceptions;

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

        using (TestUser.BeginAs(Guid.NewGuid()))
        {
            var refused = await Refused.WithCodeAsync(
                PlotMembership.Codes,
                MembershipRefusals.NotPermitted,
                () => roles.CreateAsync(new PlotRole(PlotRoleId.CreateSequential(), Meadow, new CustomRoleDraft("Pruner", null, [PlotPermissions.Plant])), Cancellation));
            refused.Message.Should().Contain("managedWith");
            store.Kept.Should().BeEmpty();
        }

        using (TestUser.BeginSystem())
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

        using (TestUser.BeginAs(Guid.NewGuid()))
        {
            // Nothing answers whether the caller holds it for the whole of what the roles are kept for.
            (await Refused.WithCodeAsync(ShedMembership.Codes, MembershipRefusals.NotPermitted, () => new ShedRoleManager(store, access, TestUser).CreateStarterRolesAsync(Keeper, Cancellation)))
                .Arguments.Should().Contain("Permission", ShedMembership.ManageRoles);

            // What answers is asked for the permission the rules name, and refuses as it refuses.
            await FluentActions.Awaiting(() => new ShedRoleManager(store, access, TestUser, asked).CreateStarterRolesAsync(Keeper, Cancellation)).Should().ThrowAsync<RefusalException>();
            asked.Held = true;
            var made = await new ShedRoleManager(store, access, TestUser, asked).CreateStarterRolesAsync(Keeper, Cancellation);
            made.Select(role => role.MadeFrom).Should().Equal(ShedMembership.Keeper);
            asked.Asked.Should().Equal(ShedMembership.ManageRoles, ShedMembership.ManageRoles);
        }
    }

    // ---------------------------------------------------------------- the starter roles

    [Fact]
    public async Task A_starter_role_is_made_once_for_a_scope_whatever_became_of_it()
    {
        var store = new StubRoleStore<PlotRole, PlotRoleId>();
        var roles = Plots(store);
        using var system = TestUser.BeginSystem();

        // The starter roles the rules declare, each through the host's constructor, which says whose it is.
        var made = await roles.CreateStarterRolesAsync(draft => new PlotRole(PlotRoleId.CreateSequential(), Meadow, draft), Cancellation, scope: role => role.GardenId == Meadow);
        made.Select(role => (role.Name, role.MadeFrom, role.GardenId)).Should().Equal(
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
            .Select(role => role.MadeFrom).Should().Equal(PlotMembership.Tender, PlotMembership.Waterer);
    }

    // ---------------------------------------------------------------- the changes

    [Fact]
    public async Task Each_change_loads_the_role_hands_it_the_rules_and_the_role_calls_its_hooks()
    {
        var store = new StubRoleStore<PlotRole, PlotRoleId>();
        var roles = Plots(store);
        using var system = TestUser.BeginSystem();
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

        using (TestUser.BeginAs(Guid.NewGuid()))
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
