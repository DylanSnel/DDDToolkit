using DDDToolkit.Abstractions.Access;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Access;

/// <summary>
/// The projects' rules, as the application makes them from its starter project roles and the permissions on a
/// project, with no host and no database: what an owner holds, what a crew role may give, which permissions the
/// database holds a crew and an owner to, whose work holds every permission, the codes a crew is refused under, and
/// starter roles that could not become a tenant's first project roles refused where the application declares them.
/// </summary>
public sealed class ProjectMembershipTests
{
    private static MembershipRules Rules => SampleCatalogue.Projects.Rules;

    [Fact]
    public void The_projects_rules_are_made_through_the_project_and_from_the_starter_roles_the_application_declares()
    {
        Rules.Name.Should().Be("projects", "the class's name, made plural");
        Rules.Roles.Kind.Should().Be(RolesKind.Custom, "a crew holds the tenant's project roles, which the package keeps");
        Rules.Roles.Declared.Select(role => (role.Name, string.Join(" ", role.Permissions)))
            .Should().Equal(SampleCatalogue.ProjectRoles.Select(starter => (starter.Key, string.Join(" ", starter.Permissions))));
        Rules.OwnerPermissions.Should().BeEquivalentTo(
            [ProjectPermissionNames.View, ProjectPermissionNames.Edit, ProjectPermissionNames.Close, ProjectPermissionNames.ManageCrew],
            "an owner holds a lead's permissions by owning the project, and not the one that names an owner");
        Rules.EveryMemberPermission.Should().Be(ProjectPermissionNames.View, "being on a crew is enough to see the project");
        Rules.ReachFromAbove.Should().NotBeNull("a permission held at the project's unit, or above it, reaches the project");
        Rules.Codes.Should().BeSameAs(ProjectRefusals.Membership, "the codes the project declares");

        // The functions are named after the project, as every row rule that asks them by the project's id reads them.
        Rules.Functions.Should().Be(new MembershipFunctions("projects_as_member", "projects_as_member_with", "projects_i_see", "projects_where_i_hold", "projects_fellow_members"));
    }

    [Fact]
    public void A_crew_role_gives_every_permission_that_acts_on_a_project_and_none_of_the_organizations()
    {
        string[] every =
        [
            .. Examples.Tenancy.Projects.Application.DeclaredPermissions.All
                .Concat(Examples.Tenancy.Inspections.Application.DeclaredPermissions.All)
                .Concat(TenancyPermissions.All)
                .Select(permission => permission.Name),
        ];

        // The rules say what a crew role may give, for every permission there is: the permissions on a project a role can give,
        // and the permissions of other modules, but none of Tenancy's, nor the one Projects asks at a unit.
        every.Where(Rules.RolePermissions.Allows).Should().BeEquivalentTo(
            [ProjectPermissionNames.View, ProjectPermissionNames.Edit, ProjectPermissionNames.Close, ProjectPermissionNames.ManageCrew, InspectionPermissionNames.Record]);
        Rules.RolePermissions.Allows(ProjectPermissionNames.ChangeOwner).Should().BeFalse("naming an owner is the organization's: no crew role gives it");
        Rules.RolePermissions.Allows(ProjectPermissionNames.Open).Should().BeFalse("opening a project is asked at a unit, where there is no crew");
        Rules.ReachesFromAbove(ProjectPermissionNames.ChangeOwner).Should().BeTrue("the organization names an owner from above");
    }

    [Fact]
    public void The_database_holds_a_crew_and_an_owner_to_the_permissions_the_commands_require()
    {
        Rules.MembersLockPermission.Should().Be(AddCrewMember.RequiredPermission);
        new[] { GiveCrewRole.RequiredPermission, TakeCrewRole.RequiredPermission, RemoveCrewMember.RequiredPermission }.Should().OnlyContain(permission => permission == Rules.MembersLockPermission);
        Rules.OwnerLockPermission.Should().Be(ChangeProjectOwner.RequiredPermission);
    }

    [Fact]
    public void Only_the_applications_work_and_this_modules_own_scope_hold_every_permission_on_a_project()
    {
        Rules.SystemScopes.Should().Equal([ProjectMembership.Scope], "the module's own scope, which the rules let through without saying it");

        Rules.IsOwnWork(Caller.System).Should().BeTrue();
        Rules.IsOwnWork(Caller.SystemIn(ProjectMembership.Scope)).Should().BeTrue("the seeding and a seat that gives up its own place on a crew work in it");
        Rules.IsOwnWork(Caller.SystemIn(TenancyWork.SystemScope)).Should().BeFalse("Tenancy's work runs the organization, and holds nothing on a project");
        Rules.IsOwnWork(Caller.SystemIn("inspections")).Should().BeFalse("another module's work holds nothing on a project");
    }

    [Fact]
    public void A_crew_is_refused_under_projects_own_codes_each_of_the_packages_kind()
    {
        foreach (var rule in MembershipRefusals.Codes)
        {
            var code = ProjectRefusals.Membership[rule];

            ProjectRefusals.Codes.Should().Contain(code, "a crew's refusal has a text of the module's, in both languages: {0}", rule);
            ProjectRefusals.Refuse(code).Kind.Should().Be(MembershipRefusals.KindOf(rule), "the module says of {0} what the package refuses it as", code);
        }

        // The ones a crew had before the package keep the codes they had, and name the seat.
        ProjectRefusals.Membership[MembershipRefusals.AlreadyMember].Should().Be(ProjectRefusals.AlreadyOnCrew);
        ProjectRefusals.Membership[MembershipRefusals.MemberNotActive].Should().Be(ProjectRefusals.SeatNotActive);
        ProjectRefusals.Membership.MemberArgument.Should().Be("Seat");
    }

    [Fact]
    public void Starter_roles_that_could_not_be_a_tenants_first_project_roles_are_refused_where_they_are_declared()
    {
        StarterProjectRole Lead(string name = "Crew lead") => new("crew-lead", name, "Leads", [ProjectPermissionNames.View]);

        // A permission no crew role gives: naming an owner is the organization's.
        var beyond = () => new ProjectMembership([Lead(), new("site-keeper", "Site keeper", "Keeps the site", [ProjectPermissionNames.View, ProjectPermissionNames.ChangeOwner])]);
        beyond.Should().Throw<ArgumentException>().WithMessage("*'" + ProjectPermissionNames.ChangeOwner + "'*");

        // Nor one of Tenancy's, which manage the organization.
        var organization = () => new ProjectMembership([new("site-keeper", "Site keeper", "Keeps the site", [ProjectPermissionNames.View, TenancyPermissions.RolesManage])]);
        organization.Should().Throw<ArgumentException>().WithMessage("*'" + TenancyPermissions.RolesManage + "'*");

        // Two of one name, which a tenant's project roles never have.
        var twice = () => new ProjectMembership([Lead(), new("site-keeper", "Crew lead", "Keeps the site", [ProjectPermissionNames.View])]);
        twice.Should().Throw<ArgumentException>().WithMessage("*'Crew lead'*");

        // One permission, which a role is made from and found by, twice.
        var again = () => new ProjectMembership([Lead(), Lead("Lead again")]);
        again.Should().Throw<ArgumentException>().WithMessage("*'crew-lead'*");

        // No starter role is the owner's: a tenant without the crew lead's role opens projects all the same.
        new ProjectMembership([new("site-keeper", "Site keeper", "Keeps the site", [ProjectPermissionNames.View])]).Rules.Roles.Declared.Should().ContainSingle();
    }
}
