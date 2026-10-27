using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Projects.Access;

/// <summary>
/// What a client fills its navigation and its buttons from: the permissions its caller holds for the whole tenant, and
/// the permissions it holds on each of a page of projects, through a crew or through the organization, asked once.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class PermissionSetScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    /// <summary>
    /// Where a statement reads what a seat holds in the organization: the function Tenancy gives another module
    /// to ask, in place of its tables.
    /// </summary>
    private const string SeatRights = """tenancy\.caller_permissions\(\)""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static Guid PierSeven => Harbor.ProjectNamed("Pier 7").Id.Value;

    private static Guid InlandDepot => Harbor.ProjectNamed("Inland depot").Id.Value;

    private static Guid BayBridge => Harbor.ProjectNamed("Bay bridge").Id.Value;

    [Fact]
    public async Task The_tenant_wide_set_names_only_permissions_held_at_the_root()
    {
        // Ada is an area manager at harbor's root, so what that role gives she holds everywhere. Rhea is one at
        // North: she opens projects there, and holds nothing for the whole tenant. Hana gives roles everywhere.
        var asked = $"{ProjectPermissionNames.Open},{ProjectPermissionNames.Close}, {TenancyPermissions.GrantsManage},{TenancyPermissions.RolesManage}";
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        (await HeldAtRootAsync(ada, asked)).Should().Equal(ProjectPermissionNames.Close, ProjectPermissionNames.Open, TenancyPermissions.GrantsManage, TenancyPermissions.RolesManage);
        (await HeldAtRootAsync(rhea, asked)).Should().BeEmpty();
        (await HeldAtRootAsync(hana, asked)).Should().Equal(TenancyPermissions.GrantsManage);
        (await HeldAtRootAsync(ada, string.Empty)).Should().BeEmpty("no permission asked about, none answered");
    }

    [Fact]
    public async Task Permission_sets_for_a_page_come_from_crew_and_organization()
    {
        string[] permissions = [ProjectPermissionNames.View, ProjectPermissionNames.Edit, ProjectPermissionNames.ManageCrew, ProjectPermissionNames.ChangeOwner, InspectionPermissionNames.Record];
        Guid[] projects = [PierSeven, InlandDepot, BayBridge];

        // Rhea is an area manager at North, which reaches Pier 7 and Inland depot, whose crew she also leads. Bay
        // bridge hangs in the south, out of her sight: it is not in the answer, exactly as an id that names nothing.
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);
        var hers = await PermissionsOnAsync(rhea, permissions, [.. projects, Guid.NewGuid()]);
        hers.Keys.Should().BeEquivalentTo([PierSeven, InlandDepot]);
        hers[PierSeven].Should().BeEquivalentTo(permissions);
        hers[InlandDepot].Should().BeEquivalentTo(permissions);

        // Leo owns Pier 7 and holds nothing in the organization: what its owner holds, and never the permission that names
        // an owner, which no crew gives, nor the one that records, which is the Inspections module's.
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);
        var his = await PermissionsOnAsync(leo, permissions, projects);
        his.Should().ContainSingle().Which.Key.Should().Be(PierSeven);
        his[PierSeven].Should().BeEquivalentTo([ProjectPermissionNames.View, ProjectPermissionNames.Edit, ProjectPermissionNames.ManageCrew]);

        // Juno surveys there and Vic looks: each holds what the role held on the crew gives.
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        (await PermissionsOnAsync(juno, permissions, projects))[PierSeven].Should().BeEquivalentTo([ProjectPermissionNames.View, InspectionPermissionNames.Record]);
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);
        (await PermissionsOnAsync(vic, permissions, projects))[PierSeven].Should().BeEquivalentTo([ProjectPermissionNames.View]);

        // Hana gives roles and sees no project: an empty answer, not a refusal.
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);
        (await PermissionsOnAsync(hana, permissions, projects)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_member_without_a_role_holds_the_permission_that_sees_the_project_and_no_other()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", Harbor.Slug);
        using (var added = await leo.PostAsJsonAsync($"/projects/{PierSeven}/crew", new { seatId = Harbor.SeatOf(DemoPeople.Tove).Value }, Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var hers = await PermissionsOnAsync(tove, [ProjectPermissionNames.View, ProjectPermissionNames.Edit, InspectionPermissionNames.Record], [PierSeven, BayBridge]);

        hers[PierSeven].Should().BeEquivalentTo([ProjectPermissionNames.View], "being on the crew is seeing the project");
        hers[BayBridge].Should().BeEquivalentTo([ProjectPermissionNames.View, InspectionPermissionNames.Record], "and there she surveys");

        // Asked without the permission that sees, a project she holds nothing else on is simply not answered about.
        (await PermissionsOnAsync(tove, [ProjectPermissionNames.Edit], [PierSeven, BayBridge])).Should().BeEmpty();
    }

    [Fact]
    public async Task The_permissions_on_a_page_of_projects_are_one_statement()
    {
        var counter = new CommandCounter();
        await using var host = await sample.StartAsync(services => services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter)));
        ProjectId[] projects = [.. Harbor.Projects.Select(project => project.Id)];

        // Rhea holds permissions both ways. What the application does: it asks Tenancy's questions for all the permissions at
        // once, starter roles the answers, still sets and not lists, and the port sends them with the projects as one
        // statement: the organization's permissions, the crew's and the permission that sees, put together where it runs. Asked
        // as a request asks: as the person, in her seat.
        using (Callers.Begin(Caller.User(DemoPeople.Rhea.Id)))
        using (TenancyCallers.Begin(TenancyCaller<TenantId, SeatId>.InSeat(Harbor.Id, Harbor.SeatOf(DemoPeople.Rhea))))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var access = scope.ServiceProvider.GetRequiredService<ProjectAccess>();

            counter.WatchThisFlow();
            await using var reading = scope.ServiceProvider.GetRequiredService<IProjectReads>().Open();
            var held = await reading.PermissionsOnAsync(projects, access.KnownPermissions([ProjectPermissionNames.View, ProjectPermissionNames.Edit, ProjectPermissionNames.ChangeOwner]), Cancellation);

            var statement = counter.Commands.Should().ContainSingle("the permissions of every project asked about come in one statement").Which;
            statement.Should().MatchRegex(SeatRights, "the organization's permissions are Tenancy's answer, inside it")
                .And.Contain($"\"{ProjectsContext.CrewRolesTable}\"", "the crew's permissions are the roles held on each crew, inside it")
                .And.Contain("UNION ALL");
            held.Keys.Should().BeEquivalentTo([Harbor.ProjectNamed("Pier 7").Id, Harbor.ProjectNamed("Inland depot").Id]);
        }

        // The projects' own system work in the tenant holds every permission on every project of it, and asks Tenancy nothing.
        using (SampleAccess.Manager.BeginSystemIn(Harbor.Id, scope: ProjectMembership.Scope))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var access = scope.ServiceProvider.GetRequiredService<ProjectAccess>();

            counter.WatchThisFlow();
            await using var reading = scope.ServiceProvider.GetRequiredService<IProjectReads>().Open();
            var held = await reading.PermissionsOnAsync([.. projects, DemoData.Meadow.ProjectNamed("Garden shed").Id], access.KnownPermissions([ProjectPermissionNames.View, ProjectPermissionNames.ChangeOwner]), Cancellation);

            counter.Commands.Should().ContainSingle().Which.Should().NotMatchRegex(SeatRights);
            held.Keys.Should().BeEquivalentTo(projects, "a project of another tenant is not there");
            held.Values.Should().AllSatisfy(permissions => permissions.Should().BeEquivalentTo([ProjectPermissionNames.View, ProjectPermissionNames.ChangeOwner]));
        }
    }

    [Fact]
    public async Task More_than_two_hundred_ids_are_refused()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        var many = Enumerable.Range(0, PermissionsOnProjects.MostProjects + 1).Select(_ => Guid.NewGuid()).ToArray();

        using var response = await ada.PostAsJsonAsync("/access/projects/permissions", new { permissions = new[] { ProjectPermissionNames.View }, projects = many }, Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.TooManyIds);
        refused.Argument("Max").Should().Be(PermissionsOnProjects.MostProjects.ToString());

        // The same id many times is one id.
        using var repeated = await ada.PostAsJsonAsync(
            "/access/projects/permissions",
            new { permissions = new[] { ProjectPermissionNames.View }, projects = Enumerable.Repeat(PierSeven, PermissionsOnProjects.MostProjects + 1) },
            Cancellation);
        repeated.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_permission_is_refused()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        using (var tenantWide = await rhea.GetAsync($"/access/permissions?permissions={ProjectPermissionNames.View},projects.nothing", Cancellation))
        {
            var refused = await tenantWide.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.UnknownPermission);
            refused.Argument("Permissions").Should().Be("projects.nothing");
        }

        using (var onProjects = await rhea.PostAsJsonAsync("/access/projects/permissions", new { permissions = new[] { "projects.nothing" }, projects = new[] { PierSeven } }, Cancellation))
        {
            await onProjects.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.UnknownPermission);
        }

        // A body without its lists is no question at all.
        using var bare = await rhea.PostAsJsonAsync("/access/projects/permissions", new { permissions = new[] { ProjectPermissionNames.View } }, Cancellation);
        await bare.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
    }

    private static async Task<IReadOnlyList<string?>> HeldAtRootAsync(HttpClient client, string permissions)
    {
        var answer = await client.GetFromJsonAsync<JsonElement>("/access/permissions?permissions=" + Uri.EscapeDataString(permissions), Cancellation);
        return [.. answer.GetProperty("heldAtRoot").EnumerateArray().Select(permission => permission.GetString())];
    }

    /// <summary>The permissions the client's caller holds on each project, from <c>POST /access/projects/permissions</c>.</summary>
    private static async Task<IReadOnlyDictionary<Guid, string[]>> PermissionsOnAsync(HttpClient client, string[] permissions, Guid[] projects)
    {
        using var response = await client.PostAsJsonAsync("/access/projects/permissions", new { permissions, projects }, Cancellation);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Cancellation));

        var answer = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        return answer.GetProperty("onProjects").EnumerateObject()
            .ToDictionary(held => Guid.Parse(held.Name), held => held.Value.EnumerateArray().Select(permission => permission.GetString()!).ToArray());
    }
}
