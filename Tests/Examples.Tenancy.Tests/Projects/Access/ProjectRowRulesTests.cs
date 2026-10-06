using Examples.Tenancy.Projects.Contracts.RowAccess;
using Examples.Tenancy.Projects.Infrastructure.Access;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Access;

/// <summary>
/// What the database itself lets a seat read and change of the projects and their roles, as Projects writes it for
/// a database that checks every row: the rule classes, each translated to the SQL an export fills in. These pin
/// that text, without a database: a seat reads the projects the projects' rules say it sees, changes one where it
/// holds a key on it, and reads its tenant's project roles, which only whoever manages the tenant's roles changes.
/// What the functions answer once they are in a database is <c>SampleOnPostgresTests</c>' to show.
/// </summary>
public sealed class ProjectRowRulesTests
{
    [Fact]
    public void Projects_publishes_what_a_seat_sees_and_holds_by_the_project_s_id_and_names_no_function()
    {
        // What a rule asks by: the project's id and the set. The export writes it as the function the Membership
        // package writes from the projects' rules, under whatever name those rules give it.
        ProjectsISee.Name.Should().Be("@Examples.Tenancy.Projects.Contracts.ValueObjects.ProjectId/seen");
        ProjectsWhereIHold.Name.Should().Be("@Examples.Tenancy.Projects.Contracts.ValueObjects.ProjectId/held_on");
    }

    [Fact]
    public void A_seat_reads_the_projects_it_sees()
    {
        // One set, asked once per statement: the function the Membership package writes from the projects' rules,
        // which answers the crew, the owner and the organization at once, asked through the module's own contract.
        SeatsSeeTheProjectsTheyReach.RowAccessSql.Should().Be(
            $"({{col:Id}} = ANY (ARRAY(SELECT {{fn:{ProjectsISee.Name}}}())))");
    }

    [Fact]
    public void A_seat_changes_a_project_where_it_holds_any_key_that_changes_one()
    {
        // Each key a command of the module asks on a project, through the same function, and opening a project at
        // a unit, which Tenancy answers.
        var held = "{fn:" + ProjectsWhereIHold.Name + "}";
        SeatsChangeTheProjectsTheyWorkOn.RowAccessSql.Should().Be(
            $"((((({{col:Id}} = ANY (ARRAY(SELECT {held}('projects.edit'))))"
            + $" OR ({{col:Id}} = ANY (ARRAY(SELECT {held}('projects.close')))))"
            + $" OR ({{col:Id}} = ANY (ARRAY(SELECT {held}('projects.crew.manage')))))"
            + $" OR ({{col:Id}} = ANY (ARRAY(SELECT {held}('projects.owner.change')))))"
            + " OR ({col:UnitId} = ANY (ARRAY(SELECT {fn:tenancy/units_where_i_hold}('projects.open')))))");
    }

    [Fact]
    public void A_seat_reads_its_tenant_s_project_roles_and_only_who_manages_roles_changes_them()
    {
        SeatsReadTheProjectRolesOfTheirTenant.RowAccessSql.Should().Be("({col:TenantId} = (SELECT {fn:tenancy/caller_tenant}()))");
        // One question and nothing else: the tenant of the row is Tenancy's own policy on the table to keep.
        RoleManagersChangeTheProjectRoles.RowAccessSql.Should().Be("(SELECT {fn:tenancy/holds_tenant_wide}('tenancy.roles.manage'))");
    }
}
