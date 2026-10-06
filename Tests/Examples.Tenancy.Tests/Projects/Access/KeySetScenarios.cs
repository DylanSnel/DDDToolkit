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
/// What a client fills its navigation and its buttons from: the keys its caller holds for the whole tenant, and
/// the keys it holds on each of a page of projects, through a crew or through the organization, asked once.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class KeySetScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    /// <summary>
    /// Where a statement reads what a seat holds in the organization: the function Tenancy gives another module
    /// to ask, in place of its tables.
    /// </summary>
    private const string SeatRights = """tenancy\.caller_rights\(\)""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static Guid PierSeven => Harbor.ProjectNamed("Pier 7").Id.Value;

    private static Guid InlandDepot => Harbor.ProjectNamed("Inland depot").Id.Value;

    private static Guid BayBridge => Harbor.ProjectNamed("Bay bridge").Id.Value;

    [Fact]
    public async Task The_tenant_wide_set_names_only_keys_held_at_the_root()
    {
        // Ada is an area manager at harbor's root, so what that role gives she holds everywhere. Rhea is one at
        // North: she opens projects there, and holds nothing for the whole tenant. Hana gives roles everywhere.
        var asked = $"{ProjectKeys.Open},{ProjectKeys.Close}, {TenancyKeys.GrantsManage},{TenancyKeys.RolesManage}";
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        (await HeldAtRootAsync(ada, asked)).Should().Equal(ProjectKeys.Close, ProjectKeys.Open, TenancyKeys.GrantsManage, TenancyKeys.RolesManage);
        (await HeldAtRootAsync(rhea, asked)).Should().BeEmpty();
        (await HeldAtRootAsync(hana, asked)).Should().Equal(TenancyKeys.GrantsManage);
        (await HeldAtRootAsync(ada, string.Empty)).Should().BeEmpty("no key asked about, none answered");
    }

    [Fact]
    public async Task Key_sets_for_a_page_come_from_crew_and_organization()
    {
        string[] keys = [ProjectKeys.View, ProjectKeys.Edit, ProjectKeys.ManageCrew, ProjectKeys.ChangeOwner, InspectionKeys.Record];
        Guid[] projects = [PierSeven, InlandDepot, BayBridge];

        // Rhea is an area manager at North, which reaches Pier 7 and Inland depot, whose crew she also leads. Bay
        // bridge hangs in the south, out of her sight: it is not in the answer, exactly as an id that names nothing.
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);
        var hers = await KeysOnAsync(rhea, keys, [.. projects, Guid.NewGuid()]);
        hers.Keys.Should().BeEquivalentTo([PierSeven, InlandDepot]);
        hers[PierSeven].Should().BeEquivalentTo(keys);
        hers[InlandDepot].Should().BeEquivalentTo(keys);

        // Leo leads Pier 7's crew and holds nothing in the organization: what a lead's role gives, and never the
        // key that names an owner, which no crew gives.
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);
        var his = await KeysOnAsync(leo, keys, projects);
        his.Should().ContainSingle().Which.Key.Should().Be(PierSeven);
        his[PierSeven].Should().BeEquivalentTo([ProjectKeys.View, ProjectKeys.Edit, ProjectKeys.ManageCrew, InspectionKeys.Record]);

        // Juno surveys there and Vic looks: each holds what the role held on the crew gives.
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        (await KeysOnAsync(juno, keys, projects))[PierSeven].Should().BeEquivalentTo([ProjectKeys.View, InspectionKeys.Record]);
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);
        (await KeysOnAsync(vic, keys, projects))[PierSeven].Should().BeEquivalentTo([ProjectKeys.View]);

        // Hana gives roles and sees no project: an empty answer, not a refusal.
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);
        (await KeysOnAsync(hana, keys, projects)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_member_without_a_role_holds_the_key_that_sees_the_project_and_no_other()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", Harbor.Slug);
        using (var added = await leo.PostAsJsonAsync($"/projects/{PierSeven}/crew", new { seatId = Harbor.SeatOf(DemoPeople.Tove).Value }, Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var hers = await KeysOnAsync(tove, [ProjectKeys.View, ProjectKeys.Edit, InspectionKeys.Record], [PierSeven, BayBridge]);

        hers[PierSeven].Should().BeEquivalentTo([ProjectKeys.View], "being on the crew is seeing the project");
        hers[BayBridge].Should().BeEquivalentTo([ProjectKeys.View, InspectionKeys.Record], "and there she surveys");

        // Asked without the key that sees, a project she holds nothing else on is simply not answered about.
        (await KeysOnAsync(tove, [ProjectKeys.Edit], [PierSeven, BayBridge])).Should().BeEmpty();
    }

    [Fact]
    public async Task The_keys_on_a_page_of_projects_are_one_statement()
    {
        var counter = new CommandCounter();
        await using var host = await sample.StartAsync(services => services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter)));
        ProjectId[] projects = [.. Harbor.Projects.Select(project => project.Id)];

        // Rhea holds keys both ways. What the application does: it asks Tenancy's questions for all the keys at
        // once, packs the answers, still sets and not lists, and the port sends them with the projects as one
        // statement: the organization's keys, the crew's and the key that sees, put together where it runs. Asked
        // as a request asks: as the person, in her seat.
        using (Callers.Begin(Caller.User(DemoPeople.Rhea.Id)))
        using (TenancyCallers.Begin(TenancyCaller<TenantId, SeatId>.InSeat(Harbor.Id, Harbor.SeatOf(DemoPeople.Rhea))))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var access = scope.ServiceProvider.GetRequiredService<ProjectAccess>();

            counter.WatchThisFlow();
            await using var reading = scope.ServiceProvider.GetRequiredService<IProjectReads>().Open();
            var held = await reading.KeysOnAsync(projects, access.KeysReachFor([ProjectKeys.View, ProjectKeys.Edit, ProjectKeys.ChangeOwner]), Cancellation);

            var statement = counter.Commands.Should().ContainSingle("the keys of every project asked about come in one statement").Which;
            statement.Should().MatchRegex(SeatRights, "the organization's keys are Tenancy's answer, inside it")
                .And.Contain($"\"{ProjectsContext.CrewRolesTable}\"", "the crew's keys are the roles held on each crew, inside it")
                .And.Contain("UNION ALL");
            held.Keys.Should().BeEquivalentTo([Harbor.ProjectNamed("Pier 7").Id, Harbor.ProjectNamed("Inland depot").Id]);
        }

        // System work in the tenant holds every key on every project of it, and asks Tenancy nothing.
        using (TenantsTenancy.BeginSystemIn(Harbor.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var access = scope.ServiceProvider.GetRequiredService<ProjectAccess>();

            counter.WatchThisFlow();
            await using var reading = scope.ServiceProvider.GetRequiredService<IProjectReads>().Open();
            var held = await reading.KeysOnAsync([.. projects, DemoData.Meadow.ProjectNamed("Garden shed").Id], access.KeysReachFor([ProjectKeys.View, ProjectKeys.ChangeOwner]), Cancellation);

            counter.Commands.Should().ContainSingle().Which.Should().NotMatchRegex(SeatRights);
            held.Keys.Should().BeEquivalentTo(projects, "a project of another tenant is not there");
            held.Values.Should().AllSatisfy(keys => keys.Should().BeEquivalentTo([ProjectKeys.View, ProjectKeys.ChangeOwner]));
        }
    }

    [Fact]
    public async Task More_than_two_hundred_ids_are_refused()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        var many = Enumerable.Range(0, KeysOnProjects.MostProjects + 1).Select(_ => Guid.NewGuid()).ToArray();

        using var response = await ada.PostAsJsonAsync("/access/projects/keys", new { keys = new[] { ProjectKeys.View }, projects = many }, Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.TooManyIds);
        refused.Argument("Max").Should().Be(KeysOnProjects.MostProjects.ToString());

        // The same id many times is one id.
        using var repeated = await ada.PostAsJsonAsync(
            "/access/projects/keys",
            new { keys = new[] { ProjectKeys.View }, projects = Enumerable.Repeat(PierSeven, KeysOnProjects.MostProjects + 1) },
            Cancellation);
        repeated.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_key_is_refused()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        using (var tenantWide = await rhea.GetAsync($"/access/keys?keys={ProjectKeys.View},projects.nothing", Cancellation))
        {
            var refused = await tenantWide.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.UnknownPermission);
            refused.Argument("Keys").Should().Be("projects.nothing");
        }

        using (var onProjects = await rhea.PostAsJsonAsync("/access/projects/keys", new { keys = new[] { "projects.nothing" }, projects = new[] { PierSeven } }, Cancellation))
        {
            await onProjects.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.UnknownPermission);
        }

        // A body without its lists is no question at all.
        using var bare = await rhea.PostAsJsonAsync("/access/projects/keys", new { keys = new[] { ProjectKeys.View } }, Cancellation);
        await bare.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
    }

    private static async Task<IReadOnlyList<string?>> HeldAtRootAsync(HttpClient client, string keys)
    {
        var answer = await client.GetFromJsonAsync<JsonElement>("/access/keys?keys=" + Uri.EscapeDataString(keys), Cancellation);
        return [.. answer.GetProperty("heldAtRoot").EnumerateArray().Select(key => key.GetString())];
    }

    /// <summary>The keys the client's caller holds on each project, from <c>POST /access/projects/keys</c>.</summary>
    private static async Task<IReadOnlyDictionary<Guid, string[]>> KeysOnAsync(HttpClient client, string[] keys, Guid[] projects)
    {
        using var response = await client.PostAsJsonAsync("/access/projects/keys", new { keys, projects }, Cancellation);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Cancellation));

        var answer = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        return answer.GetProperty("onProjects").EnumerateObject()
            .ToDictionary(held => Guid.Parse(held.Name), held => held.Value.EnumerateArray().Select(key => key.GetString()!).ToArray());
    }
}
