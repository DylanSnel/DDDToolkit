using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.Tests.Support;

namespace DDDToolkit.Supporting.Membership.Tests;

/// <summary>
/// A role made from a starter role follows it, by the same rules as a role of Tenancy's: what the starter role gained or
/// lost reaches the role, and what the customer changed stays. Each test changes the keeper's starter role by running the
/// role under other rules, as a new version of the application does.
/// </summary>
public sealed class FollowStarterRoleTests
{
    private static MembershipRules Sheds => ShedMembership.Rules;

    /// <summary>The sheds' rules in a version of the application whose keeper holds <paramref name="permissions"/>.</summary>
    private static MembershipRules WhereTheKeeperHolds(params string[] permissions)
        => MembershipRules.For<Shed>(
            Roles.Custom(starters: [new(ShedMembership.Keeper, permissions)], otherPermissions: OtherPermissions.All, managedWith: ShedMembership.ManageRoles));

    private static ShedRole NewKeeper()
        => new(ShedRoleId.CreateSequential(), new CustomRoleDraft(ShedMembership.Keeper, null, Sheds.PermissionsOf(new NamedRole(ShedMembership.Keeper)), ShedMembership.Keeper));

    [Fact]
    public void A_role_made_from_a_starter_role_remembers_what_the_starter_role_gave_it_and_one_a_customer_made_remembers_nothing()
    {
        NewKeeper().PermissionsFromStarterRole.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock);

        new ShedRole(ShedRoleId.CreateSequential(), new CustomRoleDraft("Lender", null, [ShedPermissions.Open])).PermissionsFromStarterRole.Should().BeNull();

        // The record is the starter role's, and permissions the draft has beyond it are the customer's.
        var own = new ShedRole(ShedRoleId.CreateSequential(), new CustomRoleDraft("Boss", null, [ToolPermissions.LendTools], ShedMembership.Keeper));
        own.Permissions.Should().Equal(ToolPermissions.LendTools);
        own.PermissionsFromStarterRole.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock);
    }

    [Fact]
    public void A_permission_the_starter_role_gains_reaches_the_role()
    {
        var role = NewKeeper();

        role.FollowStarterRole(WhereTheKeeperHolds(ShedPermissions.Open, ShedPermissions.Stock, ToolPermissions.LendTools)).Should().BeTrue();

        role.Permissions.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock, ToolPermissions.LendTools);
        role.PermissionsFromStarterRole.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock, ToolPermissions.LendTools);
    }

    [Fact]
    public void A_permission_the_starter_role_loses_leaves_the_role()
    {
        var role = NewKeeper();

        role.FollowStarterRole(WhereTheKeeperHolds(ShedPermissions.Open)).Should().BeTrue();

        role.Permissions.Should().Equal(ShedPermissions.Open);
        role.PermissionsFromStarterRole.Should().Equal(ShedPermissions.Open);
    }

    [Fact]
    public void A_permission_the_customer_took_out_stays_out_while_the_starter_role_holds_it()
    {
        var role = NewKeeper();
        role.SetPermissions([ShedPermissions.Open], Sheds);

        role.FollowStarterRole(Sheds).Should().BeFalse("the starter role did not change, and neither does the role");

        role.Permissions.Should().Equal(ShedPermissions.Open);
    }

    [Fact]
    public void A_permission_the_customer_added_stays()
    {
        var role = NewKeeper();
        role.SetPermissions([ShedPermissions.Open, ShedPermissions.Stock, ToolPermissions.LendTools], Sheds);

        role.FollowStarterRole(WhereTheKeeperHolds(ShedPermissions.Open)).Should().BeTrue();

        role.Permissions.Should().Equal(ShedPermissions.Open, ToolPermissions.LendTools);
    }

    [Fact]
    public void A_role_with_no_record_gets_what_it_lacks_and_loses_nothing()
    {
        // A row made before roles remembered what the starter role gave them.
        var role = NewKeeper();
        role.SetPermissions([ShedPermissions.Open, ToolPermissions.LendTools], Sheds);
        Break.Set(role, nameof(role.PermissionsFromStarterRole), null);

        role.FollowStarterRole(Sheds).Should().BeTrue();

        role.Permissions.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock, ToolPermissions.LendTools);
        role.PermissionsFromStarterRole.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock);
    }

    [Fact]
    public void A_role_that_follows_already_changes_nothing_and_says_so()
    {
        var role = NewKeeper();

        role.FollowStarterRole(Sheds).Should().BeFalse();
        role.FollowStarterRole(WhereTheKeeperHolds(ShedPermissions.Stock, ShedPermissions.Open)).Should().BeFalse("the same permissions in another order are the same permissions");
    }

    [Fact]
    public void An_archived_role_follows_nothing_and_a_renamed_role_follows()
    {
        var archived = NewKeeper();
        archived.Archive(Sheds);
        Refused.With(ShedMembership.Codes, MembershipRefusals.RoleIsArchived, () => archived.FollowStarterRole(WhereTheKeeperHolds(ShedPermissions.Open)));
        archived.Permissions.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock);

        var renamed = NewKeeper();
        renamed.Rename("Shed boss", null, Sheds);
        renamed.FollowStarterRole(WhereTheKeeperHolds(ShedPermissions.Open)).Should().BeTrue();
        (renamed.Name, renamed.FromStarterRole).Should().Be(("Shed boss", ShedMembership.Keeper));
        renamed.Permissions.Should().Equal(ShedPermissions.Open);
    }

    [Fact]
    public void A_role_whose_starter_role_the_rules_no_longer_declare_keeps_its_permissions()
    {
        var role = NewKeeper();
        var withoutIt = MembershipRules.For<Shed>(Roles.Custom(starters: [new("foreman", [ShedPermissions.Open])], otherPermissions: OtherPermissions.All, managedWith: ShedMembership.ManageRoles));

        role.FollowStarterRole(withoutIt).Should().BeFalse();

        role.Permissions.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock);
        role.PermissionsFromStarterRole.Should().Equal([ShedPermissions.Open, ShedPermissions.Stock], "if the rules declare it again, the role follows it from what it remembers");
    }

    [Fact]
    public void A_role_a_customer_made_follows_nothing()
    {
        var role = new ShedRole(ShedRoleId.CreateSequential(), new CustomRoleDraft("Lender", null, [ShedPermissions.Open]));

        role.FollowStarterRole(WhereTheKeeperHolds(ShedPermissions.Stock)).Should().BeFalse();

        role.Permissions.Should().Equal(ShedPermissions.Open);
        role.PermissionsFromStarterRole.Should().BeNull();
    }

    [Fact]
    public void A_permission_the_rules_no_longer_let_a_role_give_stays_on_the_role_and_gives_nothing()
    {
        // The keeper held a permission of another module, in a version of the application whose rules let a role hold those.
        var older = WhereTheKeeperHolds(ShedPermissions.Open, ToolPermissions.LendTools);
        var role = NewKeeper();
        role.FollowStarterRole(older);
        role.Gives(ToolPermissions.LendTools, older).Should().BeTrue();

        // This version's rules let a role hold a shed's permissions alone.
        var now = MembershipRules.For<Shed>(Roles.Custom(starters: [new(ShedMembership.Keeper, [ShedPermissions.Open])], managedWith: ShedMembership.ManageRoles));
        role.FollowStarterRole(now).Should().BeTrue("what the role remembers of its starter role changed");

        role.Permissions.Should().Equal(ShedPermissions.Open, ToolPermissions.LendTools);
        role.PermissionsFromStarterRole.Should().Equal(ShedPermissions.Open);
        role.Gives(ToolPermissions.LendTools, now).Should().BeFalse("what a role gives is asked of the rules each time");

        // Back in the rules, it gives again, and it is the customer's from then on: the starter role no longer holds it.
        role.Gives(ToolPermissions.LendTools, older).Should().BeTrue();
    }

    [Fact]
    public void A_permission_the_customer_took_out_that_the_starter_role_loses_and_gains_again_comes_back()
    {
        var role = NewKeeper();
        role.SetPermissions([ShedPermissions.Open], Sheds);

        role.FollowStarterRole(WhereTheKeeperHolds(ShedPermissions.Open));
        role.Permissions.Should().Equal(ShedPermissions.Open);

        role.FollowStarterRole(Sheds).Should().BeTrue();
        role.Permissions.Should().Equal(ShedPermissions.Open, ShedPermissions.Stock);
    }

    [Fact]
    public void A_change_of_permissions_calls_the_hook_and_a_change_of_the_record_alone_does_not()
    {
        var role = new PlotRole(
            PlotRoleId.CreateSequential(),
            GardenId.CreateSequential(),
            new CustomRoleDraft(PlotMembership.Waterer, null, PlotMembership.Rules.PermissionsOf(new NamedRole(PlotMembership.Waterer)), PlotMembership.Waterer));

        var moved = MembershipRules.For<Plot>(Roles.Custom(starters: [new(PlotMembership.Waterer, [PlotPermissions.See, PlotPermissions.Plant])]));
        role.FollowStarterRole(moved).Should().BeTrue();
        role.DomainEvents.OfType<PlotRolePermissionsChanged>().Should().ContainSingle()
            .Which.Should().Match<PlotRolePermissionsChanged>(changed => changed.Added.SequenceEqual(new[] { PlotPermissions.Plant }) && changed.Removed.SequenceEqual(new[] { PlotPermissions.Water }));

        // The record holds a permission the role does not: only the record changes, and no hook is called.
        Break.Set(role, nameof(role.PermissionsFromStarterRole), new[] { PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water });
        role.FollowStarterRole(moved).Should().BeTrue("what the role remembers changed");
        role.DomainEvents.OfType<PlotRolePermissionsChanged>().Should().ContainSingle("the permissions did not change, so the hook was not called again");
    }

    [Fact]
    public void It_refuses_rules_that_do_not_say_the_roles_are_kept()
    {
        FluentActions.Invoking(() => NewKeeper().FollowStarterRole(DocumentMembership.Rules))
            .Should().Throw<ArgumentException>().WithParameterName("rules");
        FluentActions.Invoking(() => NewKeeper().FollowStarterRole(null!)).Should().Throw<ArgumentNullException>();
    }
}
