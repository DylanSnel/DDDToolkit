using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Tenants.Access;

/// <summary>
/// The administrators' role of a hierarchical tenant runs access and does none of the work. Maud holds nothing but
/// Access admin, at Harbor Works' root: the permissions of Tenancy and every permission that manages access, and of the
/// application's work only seeing projects. So she gives every role, names owners and manages crews, and renames,
/// closes and records on no project, until she is on its crew with a role that lets her. A flat tenant's
/// administrator still holds every permission.
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
public sealed class AccessAdminScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task An_access_admin_holds_the_permissions_its_starter_role_lists_and_no_others()
    {
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);

        var me = await maud.GetFromJsonAsync<JsonElement>("/me", Cancellation);

        // Tenancy's six, the five that manage access and the one that reads the access history, the two the host
        // marks as managing access, and seeing projects. Every one for the whole tenant, since the role is held at
        // the root.
        me.GetProperty("permissions").EnumerateArray().Select(permission => (Key: permission.GetProperty("permission").GetString(), Everywhere: permission.GetProperty("atTenant").GetBoolean()))
            .Should().BeEquivalentTo(
            [
                (TenancyPermissions.SettingsManage, true), (TenancyPermissions.UnitsManage, true), (TenancyPermissions.SeatsManage, true),
                (TenancyPermissions.GrantsManage, true), (TenancyPermissions.RolesManage, true), (TenancyPermissions.HistoryView, true),
                (ProjectPermissionNames.ChangeOwner, true), (ProjectPermissionNames.ManageCrew, true), (ProjectPermissionNames.View, true),
            ]);
        me.GetProperty("placements").EnumerateArray().Should().ContainSingle().Which.GetProperty("grants").EnumerateArray()
            .Select(grant => grant.GetProperty("roleId").GetGuid()).Should().Equal(Harbor.Roles[SampleCatalogue.AccessAdmin].Value);

        // The starter role as the catalogue built it holds exactly those: a starter role that lists its permissions is not given the rest.
        var starterRole = (await sample.SharedAsync()).Services.GetRequiredService<TenancyCatalogue>().StarterRoles.Single(candidate => candidate.Key == SampleCatalogue.AccessAdmin);
        starterRole.Administers.Should().BeTrue();
        starterRole.Permissions.Should().BeEquivalentTo(me.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetProperty("permission").GetString()));
    }

    [Fact]
    public async Task An_access_admin_names_an_owner()
    {
        await using var host = await sample.StartAsync();
        using var maud = await host.ClientAsync("maud", Harbor.Slug);

        // Naming an owner is the organization's, and one of the permissions that manage access: hers, for every project.
        using (var named = await maud.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/owner", new { seatId = Harbor.SeatOf(DemoPeople.Juno).Value }, Cancellation))
        {
            named.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var pier = await maud.WithNamesAsync(await maud.ProjectDetailAsync(PierSeven));
        pier.Member("Juno").IsOwner.Should().BeTrue();
        pier.Member("Juno").Role.Should().BeNull("an owner holds what an owner does by owning, and her surveyor's role went");
        pier.Crew.Should().NotContain(member => member.Name == "Leo", "named with no role for him, the owner before leaves the crew");
    }

    [Fact]
    public async Task An_access_admin_gives_crew_lead_at_a_unit()
    {
        await using var host = await sample.StartAsync();
        using var maud = await host.ClientAsync("maud", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        // Crew lead manages access through projects.crew.manage. She holds that permission, as every permission that manages
        // access, at the root for good, so she gives the role anywhere, without holding the rest of its permissions.
        using (var granted = await maud.PostAsJsonAsync(
                   $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants",
                   new { unitId = Harbor.UnitNamed("North Coast").Value, roleId = Harbor.Roles[SampleCatalogue.CrewLead].Value },
                   Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var me = await leo.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetProperty("permission").GetString())
            .Should().Contain([ProjectPermissionNames.Edit, ProjectPermissionNames.Close, ProjectPermissionNames.ManageCrew], "he now leads every crew at North Coast");

        // And every other role, the administrators' own included: to somebody else, never to herself.
        using (var second = await maud.PostAsJsonAsync(
                   $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Hana).Value}/grants",
                   new { unitId = Harbor.Root.Value, roleId = Harbor.Roles[SampleCatalogue.AccessAdmin].Value },
                   Cancellation))
        {
            second.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var herself = await maud.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Maud).Value}/grants",
            new { unitId = Harbor.Root.Value, roleId = Harbor.Roles[SampleCatalogue.AreaManager].Value },
            Cancellation);
        await herself.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.SelfAppointment);
    }

    [Fact]
    public async Task An_access_admin_cannot_rename_a_project_off_its_crew()
    {
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);

        // She sees every project of harbor, through the organization, and what she may do to one says the rest.
        (await maud.VisibleProjectsAsync()).Select(project => project.Text("number")).Should().Equal(Harbor.Projects.Select(project => project.Number));
        var pier = await maud.ProjectDetailAsync(PierSeven);
        pier.Text("via").Should().Be("organization");
        var can = pier.GetProperty("can");
        (
            Rename: can.GetProperty("rename").GetBoolean(),
            Move: can.GetProperty("move").GetBoolean(),
            Close: can.GetProperty("close").GetBoolean(),
            ManageCrew: can.GetProperty("manageCrew").GetBoolean(),
            ChangeOwner: can.GetProperty("changeOwner").GetBoolean())
            .Should().Be((false, false, false, true, true));

        using (var renamed = await maud.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/name", new { name = "Pier Seven" }, Cancellation))
        {
            (await renamed.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted)).Argument("Permission").Should().Be(ProjectPermissionNames.Edit);
        }

        using (var closed = await maud.PostAsync($"/projects/{PierSeven.Id.Value}/close", content: null, Cancellation))
        {
            (await closed.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted)).Argument("Permission").Should().Be(ProjectPermissionNames.Close);
        }

        using (var opened = await maud.PostAsJsonAsync("/projects", new { number = "P-100", name = "Harbor wall", unitId = Harbor.Root.Value }, Cancellation))
        {
            (await opened.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted)).Argument("Permission").Should().Be(ProjectPermissionNames.Open);
        }

        using var recorded = await maud.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Loose railing" }, Cancellation);
        (await recorded.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, InspectionRefusals.ProjectNotPermitted)).Argument("Permission").Should().Be(InspectionPermissionNames.Record);
        (await maud.ProjectDetailAsync(PierSeven)).Text("name").Should().Be("Pier 7");
    }

    [Fact]
    public async Task An_access_admin_placed_on_the_crew_as_lead_renames_it()
    {
        await using var host = await sample.StartAsync();
        using var maud = await host.ClientAsync("maud", Harbor.Slug);

        // She manages every crew, so she may put anyone on one in any crew role, herself included: the list of permissions
        // is what her role starts with, not a wall around her seat. What she does then, she does as the crew's lead,
        // and the crew shows who put her there.
        using (var added = await maud.PostAsJsonAsync(
                   $"/projects/{PierSeven.Id.Value}/crew",
                   new { seatId = Harbor.SeatOf(DemoPeople.Maud).Value, roleId = Harbor.ProjectRoles[SampleCatalogue.CrewLead].Value },
                   Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var renamed = await maud.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/name", new { name = "Pier Seven" }, Cancellation))
        {
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var pier = await maud.ProjectDetailAsync(PierSeven);
        pier.Text("name").Should().Be("Pier Seven");
        pier.Text("via").Should().Be("crew", "the crew comes first when both reach the project");
        (await maud.WithNamesAsync(pier)).MyRole.Should().Be("Crew lead");

        // On that one project. Bay bridge, whose crew she is not on, she still only sees.
        using var other = await maud.PutAsJsonAsync($"/projects/{Harbor.ProjectNamed("Bay bridge").Id.Value}/name", new { name = "Bay crossing" }, Cancellation);
        await other.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
    }

    [Fact]
    public async Task A_flat_tenants_admin_still_holds_every_permission()
    {
        var meadow = DemoData.Meadow;
        using var tove = await sample.ClientAsync("tove", meadow.Slug);

        var me = await tove.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        var catalogue = (await sample.SharedAsync()).Services.GetRequiredService<TenancyCatalogue>();

        // Meadow is flat, so its first seat was granted Tenant admin, which lists no permissions and holds them all.
        me.GetProperty("placements").EnumerateArray().Should().ContainSingle().Which.GetProperty("grants").EnumerateArray()
            .Select(grant => grant.GetProperty("roleId").GetGuid()).Should().Equal(meadow.Roles[SampleCatalogue.TenantAdmin].Value);
        me.GetProperty("permissions").EnumerateArray().Where(permission => permission.GetProperty("atTenant").GetBoolean()).Select(permission => permission.GetProperty("permission").GetString())
            .Should().BeEquivalentTo(catalogue.LivePermissions);
        catalogue.StarterRoles.Single(starterRole => starterRole.Key == SampleCatalogue.TenantAdmin).Permissions.Should().BeEquivalentTo(catalogue.LivePermissions);

        // Each shape has an administrators' starter role seeded for it, and a tenant's roles hold the one seeded for its shape.
        var roles = await tove.GetFromJsonAsync<JsonElement>("/tenancy/roles", Cancellation);
        roles.EnumerateArray().Select(role => role.Text("fromStarterRole")).Should().Contain(SampleCatalogue.TenantAdmin).And.NotContain(SampleCatalogue.AccessAdmin);
        using var harbors = await sample.ClientAsync("ada", Harbor.Slug);
        (await harbors.GetFromJsonAsync<JsonElement>("/tenancy/roles", Cancellation)).EnumerateArray().Select(role => role.Text("fromStarterRole"))
            .Should().Contain(SampleCatalogue.AccessAdmin).And.NotContain(SampleCatalogue.TenantAdmin);
    }
}
