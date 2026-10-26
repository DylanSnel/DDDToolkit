using DDDToolkit.Exceptions;
using DDDToolkit.Testing;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// A role made from a pack follows it: it remembers what the pack gave it, so a later catalogue's pack is compared
/// with that, not with the role. A permission the pack gained is added, a permission it lost is taken out, and what the tenant
/// changed itself stays. Boards, a module that came later, stands in for the permissions a pack gains: a project lead's
/// pack first holds seeing and editing boards, and then closing them as well.
/// </summary>
public class RoleFollowsItsPackTests
{
    private const string View = "boards.view";
    private const string Edit = "boards.edit";
    private const string Close = "boards.close";
    private const string Archive = "boards.archive";
    private const string Lead = "project-lead";

    [Fact]
    public void A_role_made_from_a_pack_remembers_what_the_pack_gave_it()
    {
        var role = LeadRole(Boards(Edit));

        role.Permissions.Should().Equal(Edit, View);
        role.PermissionsFromPack.Should().Equal([Edit, View], "the permissions it starts with are the pack's, expanded");
        New.Role(Boards(Edit), "By hand", View).PermissionsFromPack.Should().BeNull("a role made by hand has no pack to remember");
        typeof(RoleAggregate<RoleId, TenantId>).GetProperty(nameof(role.PermissionsFromPack))!.SetMethod!.IsPrivate.Should().BeTrue();
    }

    [Fact]
    public void A_permission_the_pack_gains_is_added_and_the_event_says_so()
    {
        var role = LeadRole(Boards(Edit));
        var by = TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope);

        var followed = role.AsScenario()
            .When(candidate => candidate.FollowPack<SeatId>(Boards(Edit, Close), by).Should().BeTrue())
            .RaisedExactly<RoleFollowedItsPack<TenantId, RoleId, SeatId>>()
            .SingleEvent<RoleFollowedItsPack<TenantId, RoleId, SeatId>>();

        role.Permissions.Should().Equal(Close, Edit, View);
        role.PermissionsFromPack.Should().Equal(Close, Edit, View);
        (followed.TenantId, followed.RoleId, followed.Pack, followed.By).Should().Be((role.TenantId, role.Id, Lead, by));
        followed.Added.Should().Equal(Close);
        followed.Removed.Should().BeEmpty();
        followed.ManagingAccess.Should().BeEmpty();
        role.GetInvariantViolations().Should().BeEmpty();
    }

    [Fact]
    public void A_permission_the_pack_loses_is_taken_out_and_the_event_says_so()
    {
        var role = LeadRole(Boards(Edit, Close));

        var followed = role.AsScenario()
            .When(candidate => candidate.FollowPack<SeatId>(Boards(Edit)))
            .SingleEvent<RoleFollowedItsPack<TenantId, RoleId, SeatId>>();

        role.Permissions.Should().Equal(Edit, View);
        followed.Added.Should().BeEmpty();
        followed.Removed.Should().Equal(Close);
        followed.Pack.Should().Be(Lead);
    }

    [Fact]
    public void What_the_tenant_took_out_stays_out_while_the_pack_gains_another_permission()
    {
        // The tenant took closing out of its project lead; the pack later gains archiving.
        var role = LeadRole(Boards(Edit, Close));
        role.SetPermissions<SeatId>([Edit], Boards(Edit, Close));

        role.FollowPack<SeatId>(Boards(Edit, Close, Archive)).Should().BeTrue();

        role.Permissions.Should().Equal(Archive, Edit, View);
        role.Holds(Close).Should().BeFalse("the pack held closing all along, and the tenant took it out");
        role.PermissionsFromPack.Should().Equal(Archive, Close, Edit, View);
    }

    [Fact]
    public void What_the_tenant_added_stays_while_the_pack_never_held_it()
    {
        var role = LeadRole(Boards(View));
        role.SetPermissions<SeatId>([View, Close], Boards(View));

        role.FollowPack<SeatId>(Boards(Edit)).Should().BeTrue();

        role.Permissions.Should().Equal([Close, Edit, View], "edit came with the pack, view comes with edit, and close the tenant added");
    }

    [Fact]
    public void A_permission_the_tenant_added_is_the_packs_once_the_pack_gains_it_and_goes_when_the_pack_loses_it()
    {
        // The comparison cannot tell such a permission from one the pack gave: once the pack holds it, it is the pack's.
        var role = LeadRole(Boards(View));
        role.SetPermissions<SeatId>([View, Close], Boards(View));

        role.AsScenario().When(candidate => candidate.FollowPack<SeatId>(Boards(View, Close)).Should().BeTrue("the record changed")).RaisedNothing();
        role.Permissions.Should().Equal(Close, View);
        role.PermissionsFromPack.Should().Equal(Close, View);

        role.AsScenario().When(candidate => candidate.FollowPack<SeatId>(Boards(View)))
            .SingleEvent<RoleFollowedItsPack<TenantId, RoleId, SeatId>>().Removed.Should().Equal(Close);
        role.Permissions.Should().Equal(View);
    }

    [Fact]
    public void A_permission_the_tenant_took_out_comes_back_when_the_pack_loses_it_and_gains_it_again()
    {
        // Once the pack lost it, the record no longer holds it, so the pack gaining it again is a gain like any other.
        // A permission that manages access is no exception, and the event names it.
        var role = LeadRole(Boards(Edit, TenancyPermissions.GrantsManage));
        role.SetPermissions<SeatId>([Edit], Boards(Edit, TenancyPermissions.GrantsManage));

        role.AsScenario().When(candidate => candidate.FollowPack<SeatId>(Boards(Edit)).Should().BeTrue("the record changed")).RaisedNothing();
        role.Holds(TenancyPermissions.GrantsManage).Should().BeFalse();
        role.PermissionsFromPack.Should().Equal(Edit, View);

        var followed = role.AsScenario()
            .When(candidate => candidate.FollowPack<SeatId>(Boards(Edit, TenancyPermissions.GrantsManage)))
            .SingleEvent<RoleFollowedItsPack<TenantId, RoleId, SeatId>>();

        followed.Added.Should().Equal(TenancyPermissions.GrantsManage);
        followed.ManagingAccess.Should().Equal([TenancyPermissions.GrantsManage], "the tenant's administrators are told it gives power over access again");
        role.Holds(TenancyPermissions.GrantsManage).Should().BeTrue("the pack gained it after it had lost it, and the role forgot the tenant took it out");
    }

    [Fact]
    public void A_role_imported_from_a_pack_with_permissions_of_its_own_remembers_the_packs_permissions_and_keeps_its_own()
    {
        // An import makes the project lead with archiving added and closing left out: the tenant's changes, not the pack's.
        var catalogue = Boards(Edit, Close);
        var imported = TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(
            RoleId.CreateSequential(), new TenantId(1), new RoleDraft("Project lead", "Imported", [Edit, Archive], Lead), catalogue);
        imported.DrainEvents();

        imported.Permissions.Should().Equal(Archive, Edit, View);
        imported.PermissionsFromPack.Should().Equal([Close, Edit, View], "what the pack gave it is the pack's permissions, whatever the import gave the role");

        imported.AsScenario().When(role => role.FollowPack<SeatId>(catalogue).Should().BeFalse("the pack did not change")).RaisedNothing();
        imported.Permissions.Should().Equal([Archive, Edit, View], "archiving stays in and closing stays out, as the tenant had them");

        // The pack then loses closing, which the import had left out already: only the record changes.
        imported.AsScenario().When(role => role.FollowPack<SeatId>(Boards(Edit)).Should().BeTrue()).RaisedNothing();
        imported.Permissions.Should().Equal(Archive, Edit, View);
        imported.PermissionsFromPack.Should().Equal(Edit, View);
    }

    [Fact]
    public void A_role_made_from_a_pack_the_catalogue_does_not_have_remembers_nothing_and_follows_it_once_declared()
    {
        var imported = TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(
            RoleId.CreateSequential(), new TenantId(1), new RoleDraft("Project lead", "Imported", [Edit, Archive], Lead), New.Catalogue(Permissions));
        imported.DrainEvents();

        imported.FromPack.Should().Be(Lead);
        imported.PermissionsFromPack.Should().BeNull("there was no pack to remember");

        // Declared later, the pack is followed as by a role made before roles remembered their pack: it gains, and loses nothing.
        var followed = imported.AsScenario()
            .When(role => role.FollowPack<SeatId>(Boards(Close)))
            .SingleEvent<RoleFollowedItsPack<TenantId, RoleId, SeatId>>();

        followed.Added.Should().Equal(Close);
        followed.Removed.Should().BeEmpty();
        imported.Permissions.Should().Equal(Archive, Close, Edit, View);
        imported.PermissionsFromPack.Should().Equal(Close);
    }

    [Fact]
    public void Permissions_stored_in_another_order_are_no_change_and_are_saved_in_order_without_an_event()
    {
        // A seat's copy of a pack may hold the permissions in any order; a role keeps them in ordinal order.
        var catalogue = Boards(Edit, Close);
        var role = LeadRole(catalogue);
        Store(role, [View, Edit, Close]);

        role.AsScenario().When(candidate => candidate.FollowPack<SeatId>(catalogue).Should().BeTrue("the permissions are saved in order")).RaisedNothing();
        role.Permissions.Should().Equal(Close, Edit, View);

        role.AsScenario().When(candidate => candidate.FollowPack<SeatId>(catalogue).Should().BeFalse()).RaisedNothing();
    }

    [Fact]
    public void A_permission_that_is_no_longer_live_is_never_taken_out()
    {
        var role = LeadRole(Boards(Edit, Close));

        // Retired: no pack may list it, so the pack lost it; and a permission removed from code against the rule.
        role.FollowPack<SeatId>(Boards([Edit], retired: Close)).Should().BeTrue();
        role.Permissions.Should().Equal([Close, Edit, View], "a retired permission grants nothing, and retiring a permission changes no role");
        role.PermissionsFromPack.Should().Equal(Edit, View);

        var removed = LeadRole(Boards(Edit, Close));
        removed.FollowPack<SeatId>(Boards([Edit], without: Close)).Should().BeTrue();
        removed.Permissions.Should().Contain(Close);
    }

    [Fact]
    public void A_role_made_before_roles_remembered_their_pack_gets_every_permission_it_lacks_and_loses_none()
    {
        var role = LeadRole(Boards(Edit));
        role.SetPermissions<SeatId>([Edit, Archive], Boards(Edit));
        Forget(role);

        var followed = role.AsScenario()
            .When(candidate => candidate.FollowPack<SeatId>(Boards(View, Close)))
            .SingleEvent<RoleFollowedItsPack<TenantId, RoleId, SeatId>>();

        followed.Added.Should().Equal(Close);
        followed.Removed.Should().BeEmpty("with no record, nothing is known to be the pack's to take");
        role.Permissions.Should().Equal(Archive, Close, Edit, View);
        role.PermissionsFromPack.Should().Equal([Close, View], "from now on it remembers");
    }

    [Fact]
    public void Following_twice_changes_nothing_the_second_time()
    {
        var role = LeadRole(Boards(Edit));
        role.FollowPack<SeatId>(Boards(Edit, Close));
        role.DrainEvents();

        role.AsScenario().When(candidate => candidate.FollowPack<SeatId>(Boards(Edit, Close)).Should().BeFalse()).RaisedNothing();
        role.Permissions.Should().Equal(Close, Edit, View);

        // A role whose pack did not change since it was made follows it already.
        LeadRole(Boards(Edit)).AsScenario().When(fresh => fresh.FollowPack<SeatId>(Boards(Edit)).Should().BeFalse()).RaisedNothing();
    }

    [Fact]
    public void A_role_made_by_hand_or_whose_pack_is_gone_follows_nothing_and_an_archived_role_refuses()
    {
        var catalogue = Boards(Edit);

        var byHand = New.Role(catalogue, "By hand", View);
        byHand.AsScenario().IgnorePendingEvents().When(role => role.FollowPack<SeatId>(Boards(Edit, Close)).Should().BeFalse()).RaisedNothing();

        var orphan = LeadRole(catalogue);
        orphan.AsScenario().IgnorePendingEvents().When(role => role.FollowPack<SeatId>(New.Catalogue(Permissions)).Should().BeFalse()).RaisedNothing();
        orphan.Permissions.Should().Equal([Edit, View], "a pack the catalogue no longer has leaves the role as it is");
        orphan.PermissionsFromPack.Should().Equal(Edit, View);

        var archived = LeadRole(catalogue);
        archived.Archive<SeatId>();
        archived.AsScenario().IgnorePendingEvents().WhenThrows<RefusalException>(role => role.FollowPack<SeatId>(Boards(Edit, Close))).Code.Should().Be(TenancyRefusals.RoleArchived);
    }

    [Fact]
    public void A_permission_that_manages_access_follows_like_any_other_and_the_event_names_it()
    {
        var role = LeadRole(Boards(Edit));

        var followed = role.AsScenario()
            .When(candidate => candidate.FollowPack<SeatId>(Boards(Edit, TenancyPermissions.GrantsManage, Close)))
            .SingleEvent<RoleFollowedItsPack<TenantId, RoleId, SeatId>>();

        followed.Added.Should().Equal(Close, TenancyPermissions.GrantsManage);
        followed.ManagingAccess.Should().Equal([TenancyPermissions.GrantsManage], "only the application's code puts it in a pack, and whoever is told can see it");
        role.Holds(TenancyPermissions.GrantsManage).Should().BeTrue();

        role.AsScenario().When(candidate => candidate.FollowPack<SeatId>(Boards(Edit, Close)))
            .SingleEvent<RoleFollowedItsPack<TenantId, RoleId, SeatId>>().ManagingAccess.Should().Equal(TenancyPermissions.GrantsManage);
    }

    [Fact]
    public void A_gained_permission_brings_what_it_implies_and_a_lost_permission_that_a_kept_permission_implies_stays()
    {
        var role = LeadRole(Boards(Close));

        role.FollowPack<SeatId>(Boards(Close, Edit)).Should().BeTrue();
        role.Permissions.Should().Equal(Close, Edit, View);

        // The pack lists seeing only now; the tenant had added editing besides. Editing still implies seeing.
        var added = LeadRole(Boards(View));
        added.SetPermissions<SeatId>([Edit], Boards(View));
        added.FollowPack<SeatId>(Boards(Close)).Should().BeTrue();
        added.Permissions.Should().Equal([Close, Edit, View], "the tenant's edit permission brings seeing with it, whatever the pack lost");
    }

    [Fact]
    public void An_administrators_pack_that_lists_no_permissions_gives_its_role_a_permission_a_module_declares_later()
    {
        var before = New.Catalogue([.. Permissions.Where(permission => permission.Name != Close)]);
        var after = New.Catalogue(Permissions);
        var pack = before.Packs.Single(candidate => candidate.Key == HostCatalogue.AdministratorPack);
        var administrators = TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(
            RoleId.CreateSequential(), new TenantId(1), new RoleDraft(pack.Name, pack.Description, pack.Permissions, pack.Key), before);
        administrators.Holds(Close).Should().BeFalse();

        administrators.AsScenario().IgnorePendingEvents().When(role => role.FollowPack<SeatId>(after))
            .SingleEvent<RoleFollowedItsPack<TenantId, RoleId, SeatId>>().Added.Should().Equal(Close);
        administrators.Permissions.Should().BeEquivalentTo(after.LivePermissions);
    }

    /// <summary>The boards' permissions, which a module contributes: editing implies seeing.</summary>
    private static Permission[] Permissions { get; } =
    [
        new(View, "Boards", "See boards", Order: 10),
        new(Edit, "Boards", "Change boards", Implies: [View], Order: 20),
        new(Close, "Boards", "Close boards", Order: 30),
        new(Archive, "Boards", "Archive boards", Order: 40),
    ];

    /// <summary>The host's catalogue with the boards' permissions and a project lead's pack that lists <paramref name="permissions"/>.</summary>
    private static TenancyCatalogue Boards(params string[] permissions) => Boards(permissions, retired: null);

    /// <summary>
    /// The same, with one of the boards' permissions <paramref name="retired"/>, or left out of the catalogue altogether
    /// (<paramref name="without"/>).
    /// </summary>
    private static TenancyCatalogue Boards(string[] permissions, string? retired = null, string? without = null)
        => TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                Packs = [.. HostCatalogue.Application.Packs, new RolePack(Lead, "Project lead", "Leads a project", permissions, Order: 60)],
            },
            Permissions
                .Where(permission => permission.Name != without)
                .Select(permission => permission.Name == retired ? permission with { Retired = true } : permission));

    /// <summary>The project lead's role, made from its pack in <paramref name="catalogue"/> as provisioning makes it, its creation drained.</summary>
    private static HostRole LeadRole(TenancyCatalogue catalogue)
    {
        var pack = catalogue.Packs.Single(candidate => candidate.Key == Lead);
        var role = TenancyInstances.NewRole<HostRole, RoleId, TenantId, SeatId>(
            RoleId.CreateSequential(), new TenantId(1), new RoleDraft(pack.Name, pack.Description, pack.Permissions, pack.Key), catalogue);
        role.DrainEvents();
        return role;
    }

    /// <summary>The role as a row stored before roles remembered their pack's permissions reads: with no record.</summary>
    private static void Forget(HostRole role)
        => typeof(RoleAggregate<RoleId, TenantId>).GetProperty(nameof(role.PermissionsFromPack))!.SetValue(role, null);

    /// <summary>The role as a row reads that holds <paramref name="permissions"/> as they were written, in that order.</summary>
    private static void Store(HostRole role, string[] permissions)
        => typeof(RoleAggregate<RoleId, TenantId>).GetProperty(nameof(role.Permissions))!.SetValue(role, permissions);
}
