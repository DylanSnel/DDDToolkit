using DDDToolkit.Abstractions.Access;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Access;

/// <summary>
/// The projects' rules, as the application makes them from its starter project roles, with no host and no
/// database: what an owner holds, what a crew role may give, which keys the database holds a crew and an owner
/// to, whose work holds every key, the codes a crew is refused under, and starter roles that could not become a
/// tenant's first project roles refused where the application declares them.
/// </summary>
public sealed class ProjectMembershipTests
{
    private static MembershipRules Rules => SampleCatalogue.Projects.Rules;

    [Fact]
    public void The_projects_rules_are_made_from_the_starter_roles_the_application_declares()
    {
        Rules.Name.Should().Be(ProjectMembership.Name);
        Rules.RolesKept.Should().BeTrue("a crew holds the tenant's project roles, which the package keeps");
        Rules.Roles.Select(role => (role.Name, string.Join(" ", role.Keys)))
            .Should().Equal(SampleCatalogue.ProjectRoles.Select(starter => (starter.Key, string.Join(" ", starter.Keys))));
        Rules.OwnerRole.Should().Be(ProjectMembership.CrewLead);
        Rules.Keys.Should().BeEquivalentTo(ProjectCatalogue.LeadKeys, "an owner holds a lead's keys by owning the project");
        Rules.SeeKey.Should().Be(ProjectKeys.View, "being on a crew is enough to see the project");

        // The functions keep the names the row rules of this module and of others have always asked.
        Rules.Functions.Should().Be(new MembershipFunctions("crew_member_project_ids", "crew_project_ids", "project_ids_i_see", "project_ids_where_i_hold"));
    }

    [Fact]
    public void A_crew_role_gives_every_key_that_acts_on_a_project_and_none_of_the_organizations()
    {
        string[] every = [.. ProjectCatalogue.Permissions.Concat(InspectionCatalogue.Permissions).Concat(TenancyKeys.Permissions).Select(permission => permission.Key)];

        // The rules say what the module's catalogue says, for every key there is, so the access checks, the
        // database's functions and the commands that make project roles all draw the line in one place.
        every.Should().OnlyContain(key => Rules.MemberKeys.Allows(key) == ProjectCatalogue.CrewGives(key));
        every.Where(Rules.MemberKeys.Allows).Should().BeEquivalentTo(
            [ProjectKeys.View, ProjectKeys.Edit, ProjectKeys.Close, ProjectKeys.ManageCrew, InspectionKeys.Record]);
    }

    [Fact]
    public void The_database_holds_a_crew_and_an_owner_to_the_keys_the_commands_require()
    {
        Rules.ChangeMembersKey.Should().Be(AddCrewMember.RequiredKey);
        new[] { GiveCrewRole.RequiredKey, TakeCrewRole.RequiredKey, RemoveCrewMember.RequiredKey }.Should().OnlyContain(key => key == Rules.ChangeMembersKey);
        Rules.ChangeOwnerKey.Should().Be(ChangeProjectOwner.RequiredKey);
    }

    [Fact]
    public void Only_the_applications_work_in_a_tenant_and_in_this_module_holds_every_key_on_a_project()
    {
        Rules.SystemScopes.Should().BeEquivalentTo([TenancyWork.SystemScope, ProjectMembership.Scope]);

        Rules.IsOwnWork(Caller.System).Should().BeTrue();
        Rules.IsOwnWork(Caller.SystemIn(TenancyWork.SystemScope)).Should().BeTrue("the seeding and whatever sets a tenant up work in the tenant");
        Rules.IsOwnWork(Caller.SystemIn(ProjectMembership.Scope)).Should().BeTrue("a seat that gives up its own place on a crew is saved as this module's work");
        Rules.IsOwnWork(Caller.SystemIn("inspections")).Should().BeFalse("another module's work holds nothing on a project");
    }

    [Fact]
    public void A_crew_is_refused_under_projects_own_codes_each_of_the_packages_kind()
    {
        foreach (var rule in MembershipRefusals.Codes)
        {
            var code = ProjectRefusals.Membership[rule];

            ProjectRefusals.Codes.Should().Contain(code, "a crew's refusal has a text of the module's, in both languages: {0}", rule);
            ProjectRefusals.Of(code).Kind.Should().Be(MembershipRefusals.KindOf(rule), "the module says of {0} what the package refuses it as", code);
        }

        // The ones a crew had before the package keep the codes they had, and name the seat.
        ProjectRefusals.Membership[MembershipRefusals.AlreadyMember].Should().Be(ProjectRefusals.AlreadyOnCrew);
        ProjectRefusals.Membership[MembershipRefusals.MemberNotActive].Should().Be(ProjectRefusals.SeatNotActive);
        ProjectRefusals.Membership[MembershipRefusals.NoOwnerRole].Should().Be(ProjectRefusals.NoLeadRole);
        ProjectRefusals.Membership.MemberArgument.Should().Be("Seat");
    }

    [Fact]
    public void Starter_roles_that_could_not_be_a_tenants_first_project_roles_are_refused_where_they_are_declared()
    {
        StarterProjectRole Lead(string name = "Crew lead") => new(ProjectMembership.CrewLead, name, "Leads", [ProjectKeys.View]);

        // A key no crew role gives: naming an owner is the organization's.
        var beyond = () => new ProjectMembership([Lead(), new("site-keeper", "Site keeper", "Keeps the site", [ProjectKeys.View, ProjectKeys.ChangeOwner])]);
        beyond.Should().Throw<ArgumentException>().WithMessage("*'" + ProjectKeys.ChangeOwner + "'*");

        // No crew lead's role, which every owner holds.
        var leaderless = () => new ProjectMembership([new("site-keeper", "Site keeper", "Keeps the site", [ProjectKeys.View])]);
        leaderless.Should().Throw<ArgumentException>().WithMessage("*'" + ProjectMembership.CrewLead + "'*");

        // Two of one name, which a tenant's project roles never have.
        var twice = () => new ProjectMembership([Lead(), new("site-keeper", "Crew lead", "Keeps the site", [ProjectKeys.View])]);
        twice.Should().Throw<ArgumentException>().WithMessage("*'Crew lead'*");

        // One key, which a role is made from and found by, twice.
        var again = () => new ProjectMembership([Lead(), Lead("Lead again")]);
        again.Should().Throw<ArgumentException>().WithMessage("*'" + ProjectMembership.CrewLead + "'*");

        new ProjectMembership([Lead()]).Rules.Roles.Should().ContainSingle();
    }
}
