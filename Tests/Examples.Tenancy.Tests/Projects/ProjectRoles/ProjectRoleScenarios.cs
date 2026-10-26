using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.ProjectRoles;

/// <summary>
/// The roles a tenant's crews hold are its project roles, the Projects module's own: every tenant starts with the
/// starter roles, made once when it is set up, and makes, renames, re-permissions and archives its own from then on.
/// Whoever manages the tenant's roles does that; anybody who works in it reads what they are called.
/// </summary>
/// <remarks>
/// A test that is refused and changes nothing shares the class's host; a test that changes something makes its own.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class ProjectRoleScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task A_customer_makes_a_project_role_gives_it_on_a_project_and_archives_it()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);

        // Ada manages harbor's roles, and makes a role for whoever records what is found on a site.
        using var made = await ada.PostAsJsonAsync(
            "/project-roles",
            new { name = "Recorder", description = "Records what is found", permissions = new[] { InspectionPermissionNames.Record, ProjectPermissionNames.View } },
            Cancellation);
        made.StatusCode.Should().Be(HttpStatusCode.Created);
        var recorder = new ProjectRoleId((await made.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid());

        var listed = (await ada.ProjectRolesAsync()).Single(role => role.GetProperty("id").GetGuid() == recorder.Value);
        (listed.Text("name"), listed.Text("description"), listed.Text("madeFrom"), listed.Text("status")).Should().Be(("Recorder", "Records what is found", null, "active"));
        listed.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()).Should().Equal([InspectionPermissionNames.Record, ProjectPermissionNames.View], "the permissions are kept in the order of their text");

        // Leo manages Pier 7's crew, and gives it to Vic there, next to the observer's role he has: he records.
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), recorder))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await vic.WithNamesAsync(await vic.ProjectDetailAsync(PierSeven))).MyRole.Should().Be("Observer, Recorder");
        (await PermissionHeldAsync(vic, InspectionPermissionNames.Record)).Should().Be((true, "crew"));

        // Renamed, it is shown by its new name on every crew that holds it; given other permissions, it gives those.
        using (var renamed = await ada.PutAsJsonAsync($"/project-roles/{recorder.Value}", new { name = "Site recorder", description = "Records what is found" }, Cancellation))
        {
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var permissionsSet = await ada.PutAsJsonAsync($"/project-roles/{recorder.Value}/permissions", new { permissions = new[] { ProjectPermissionNames.View } }, Cancellation))
        {
            permissionsSet.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await vic.WithNamesAsync(await vic.ProjectDetailAsync(PierSeven))).MyRole.Should().Be("Observer, Site recorder");
        (await PermissionHeldAsync(vic, InspectionPermissionNames.Record)).Allowed.Should().BeFalse("the role records no more");

        // Archived, it is given to nobody, and changes no more. Vic keeps it on the crew, where it gives nothing.
        using (var archived = await ada.PostAsync($"/project-roles/{recorder.Value}/archive", content: null, Cancellation))
        {
            archived.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await ada.ProjectRolesAsync()).Single(role => role.GetProperty("id").GetGuid() == recorder.Value).Text("status").Should().Be("archived");
        using (var again = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Juno), recorder))
        {
            await again.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.RoleNotForMembers);
        }

        using (var renamedAgain = await ada.PutAsJsonAsync($"/project-roles/{recorder.Value}", new { name = "Recorder" }, Cancellation))
        {
            await renamedAgain.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.RoleArchived);
        }

        (await vic.WithNamesAsync(await vic.ProjectDetailAsync(PierSeven))).MyRole.Should().Be("Observer, Site recorder");
        (await PermissionHeldAsync(vic, ProjectPermissionNames.View)).Should().Be((true, "crew"), "he is on the crew still, as its observer");
    }

    [Fact]
    public async Task Only_who_manages_the_tenants_roles_makes_and_changes_project_roles_and_everybody_reads_their_names()
    {
        var surveyor = Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value;

        // Rhea runs North and Hana gives people their roles; neither manages the roles themselves. Leo owns a project.
        foreach (var person in new[] { "rhea", "hana", "leo" })
        {
            using var client = await sample.ClientAsync(person, Harbor.Slug);

            using (var made = await client.PostAsJsonAsync("/project-roles", new { name = "Rigger", permissions = new[] { ProjectPermissionNames.View } }, Cancellation))
            {
                (await made.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted)).Argument("Permission").Should().Be(TenancyPermissions.RolesManage);
            }

            using (var renamed = await client.PutAsJsonAsync($"/project-roles/{surveyor}", new { name = "Rigger" }, Cancellation))
            {
                await renamed.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted);
            }

            using (var permissionsSet = await client.PutAsJsonAsync($"/project-roles/{surveyor}/permissions", new { permissions = new[] { ProjectPermissionNames.View } }, Cancellation))
            {
                await permissionsSet.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted);
            }

            using (var archived = await client.PostAsync($"/project-roles/{surveyor}/archive", content: null, Cancellation))
            {
                await archived.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted);
            }

            // Each reads what the roles are called, which a crew is shown by, and not what they give.
            var roles = await client.ProjectRolesAsync();
            roles.Select(role => role.Text("name")).Should().BeEquivalentTo(["Crew lead", "Surveyor", "Observer"], person);
            roles.Should().OnlyContain(role => role.GetProperty("permissions").ValueKind == JsonValueKind.Null, person);
        }

        // The GraphQL field says the same, and says why for the permissions: the refusal a command would give.
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);
        var answer = await leo.GraphQLAsync("{ projectRoles { name permissions } }");
        answer.GetProperty("data").GetProperty("projectRoles").EnumerateArray().Select(role => role.Text("name")).Should().BeEquivalentTo(["Crew lead", "Surveyor", "Observer"]);
        answer.GetProperty("errors").EnumerateArray().Should().HaveCount(3).And.OnlyContain(error => error.Code() == TenancyRefusals.NotPermitted);

        // Meadow's roles are meadow's: none of them is harbor's to read.
        (await leo.ProjectRolesAsync()).Select(role => role.GetProperty("id").GetGuid()).Should().NotIntersectWith(DemoData.Meadow.ProjectRoles.Values.Select(role => role.Value));
    }

    [Fact]
    public async Task Maud_who_manages_the_roles_makes_one_through_the_graphql_mutation()
    {
        await using var host = await sample.StartAsync();
        using var maud = await host.ClientAsync("maud", Harbor.Slug);

        var made = await maud.GraphQLDataAsync("""mutation { projectRoleCreate(input: { name: "Rigger", description: "Rigs the hoists", permissions: ["projects.view"] }) { projectRole { name madeFrom status permissions } } }""");

        var role = made.GetProperty("projectRoleCreate").GetProperty("projectRole");
        (role.Text("name"), role.Text("madeFrom"), role.Text("status")).Should().Be(("Rigger", null, "active"));
        role.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString()).Should().Equal(ProjectPermissionNames.View);
        (await maud.ProjectRolesAsync()).Select(listed => listed.Text("name")).Should().Contain("Rigger");
    }

    [Fact]
    public async Task A_project_role_gives_only_what_a_crew_can_give_and_is_named_once_in_its_tenant()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        // The organization's permissions go on no project role: naming owners, opening projects, managing seats.
        using (var heavy = await ada.PostAsJsonAsync("/project-roles", new { name = "Area lead", permissions = new[] { ProjectPermissionNames.Edit, ProjectPermissionNames.ChangeOwner, TenancyPermissions.SeatsManage } }, Cancellation))
        {
            var refused = await heavy.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.PermissionNotForMembers);
            refused.Argument("Permissions").Should().Contain(ProjectPermissionNames.ChangeOwner).And.Contain(TenancyPermissions.SeatsManage).And.NotContain(ProjectPermissionNames.Edit);
        }

        using (var permissionsSet = await ada.PutAsJsonAsync($"/project-roles/{Harbor.ProjectRoles[SampleCatalogue.Observer].Value}/permissions", new { permissions = new[] { ProjectPermissionNames.Open } }, Cancellation))
        {
            await permissionsSet.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.PermissionNotForMembers);
        }

        // A permission nobody knows is a wrong request, as anywhere in the tenant.
        using (var unknown = await ada.PostAsJsonAsync("/project-roles", new { name = "Rigger", permissions = new[] { "projects.fly" } }, Cancellation))
        {
            await unknown.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.UnknownPermission);
        }

        // A name is there and unique among the tenant's project roles.
        using (var blank = await ada.PostAsJsonAsync("/project-roles", new { name = " ", permissions = new[] { ProjectPermissionNames.View } }, Cancellation))
        {
            await blank.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.RoleNameInvalid);
        }

        using (var taken = await ada.PostAsJsonAsync("/project-roles", new { name = "Surveyor", permissions = new[] { ProjectPermissionNames.View } }, Cancellation))
        {
            (await taken.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.RoleNameTaken)).Argument("Name").Should().Be("Surveyor");
        }

        // A role of another tenant is no role of this one.
        using (var foreign = await ada.PostAsync($"/project-roles/{DemoData.Meadow.ProjectRoles[SampleCatalogue.Observer].Value}/archive", content: null, Cancellation))
        {
            await foreign.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.RoleNotFound);
        }

        (await ada.ProjectRolesAsync()).Select(role => role.Text("name")).Should().BeEquivalentTo(["Crew lead", "Surveyor", "Observer"], "nothing refused was made or changed");
    }

    [Fact]
    public async Task The_crew_leads_role_is_archived_as_any_other_since_an_owner_holds_no_role()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var lead = Harbor.ProjectRoles[SampleCatalogue.CrewLead].Value;

        // No project role is the owner's: an owner holds what an owner does by owning the project, so the crew lead's
        // role goes as any other does.
        using (var archived = await ada.PostAsync($"/project-roles/{lead}/archive", content: null, Cancellation))
        {
            archived.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var opened = await ada.PostAsJsonAsync("/projects", new { number = "P-100", name = "Harbor wall", unitId = Harbor.UnitNamed("North").Value }, Cancellation);
        opened.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = (await ada.VisibleProjectsAsync()).Named("Harbor wall");
        (await ada.WithNamesAsync(project)).Crew.Should().ContainSingle()
            .Which.Should().Match<NamedCrewMember>(owner => owner.Name == "Ada" && owner.IsOwner && owner.Role == null);
    }

    [Fact]
    public async Task A_tenant_made_while_the_application_runs_is_set_up_with_the_starter_roles_once()
    {
        await using var host = await sample.StartAsync();

        // Provisioned as an operator's command or a sign-up would, and set up as whatever made it does next: the
        // starter project roles, as system work in the new tenant, under the database's policies.
        var quarry = await SampleTenants.ProvisionAsync(host, "quarry", "Quarry");
        (await SampleTenants.AskAsSystemAsync(host, quarry.Tenant, new TenantProjectRoles())).Should().BeEmpty("nothing is made before the tenant is set up");

        await SampleTenants.SendAsSystemAsync(host, quarry.Tenant, new SetUpProjectRoles());
        var made = await SampleTenants.AskAsSystemAsync(host, quarry.Tenant, new TenantProjectRoles());
        made.Select(role => (role.MadeFrom, role.Name, role.Description, role.Status, Keys: string.Join(" ", role.Permissions!)))
            .Should().BeEquivalentTo(SampleCatalogue.ProjectRoles.Select(starter => ((string?)starter.Key, starter.Name, starter.Description, CustomRoleStatus.Active, string.Join(" ", starter.Permissions.Order(StringComparer.Ordinal)))));

        // Once: set up again, it makes nothing.
        await SampleTenants.SendAsSystemAsync(host, quarry.Tenant, new SetUpProjectRoles());
        (await SampleTenants.AskAsSystemAsync(host, quarry.Tenant, new TenantProjectRoles())).Select(role => role.Id).Should().BeEquivalentTo(made.Select(role => role.Id));

        // And it opens projects: the owner is on the crew, holding no role, as in a tenant with no project roles.
        var opened = await SampleTenants.SendAsSystemAsync(host, quarry.Tenant, new OpenProject("P-100", "Harbor wall", quarry.RootUnit, quarry.AdminSeat));
        var crew = await SampleTenants.AskAsSystemAsync(host, quarry.Tenant, new AllCrewMembers(opened));
        crew.Should().ContainSingle().Which.Roles.Should().BeNullOrEmpty();

        // Harbor's roles are as they were.
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        (await ada.ProjectRolesAsync()).Should().HaveCount(SampleCatalogue.ProjectRoles.Count);
    }

    /// <summary>Whether Pier 7 gives the client's caller <paramref name="permission"/>, and through what, from <c>GET /access/projects/{id}?permission=</c>.</summary>
    private static async Task<(bool Allowed, string? Via)> PermissionHeldAsync(HttpClient client, string permission)
    {
        var answer = await client.GetFromJsonAsync<JsonElement>($"/access/projects/{PierSeven.Id.Value}?permission={permission}", Cancellation);
        return (answer.GetProperty("allowed").GetBoolean(), answer.Text("via"));
    }
}
