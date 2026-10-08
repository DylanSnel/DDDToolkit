using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.GraphQL;

/// <summary>
/// One schema over the modules: a module names another module's entity by its id, and the gateway asks the owner
/// for the rest. A project's unit, owner and crew get their names from Tenancy, an inspection its project from
/// Projects, and what the owner has nothing to say about arrives as nothing, never as an error.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class GraphQLLookupScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task A_projects_unit_owner_and_crew_are_named_by_tenancy()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        // Projects answers the project with the ids of its unit, its owner and its crew, and names the crew's project
        // roles itself; the other names come from Tenancy, through its lookups, in the same answer.
        var data = await rhea.GraphQLDataAsync(
            """
            {
              projects(text: "Pier 7") {
                nodes {
                  number name state via
                  unit { id path kind }
                  owner { id displayName status }
                  crew { isOwner seat { displayName } roles { role { name } } }
                  can { rename manageCrew }
                }
                pageInfo { hasNextPage }
              }
            }
            """);

        var pier = data.GetProperty("projects").GetProperty("nodes").EnumerateArray().Should().ContainSingle().Subject;
        data.GetProperty("projects").GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeFalse();
        pier.GetProperty("number").GetString().Should().Be("P-001");
        pier.GetProperty("state").GetString().Should().Be("open", "enum values are spelled as the REST API spells them");
        pier.GetProperty("via").GetString().Should().Be("organization");
        pier.GetProperty("can").GetProperty("rename").GetBoolean().Should().BeTrue("Rhea manages North");

        var unit = pier.GetProperty("unit");
        unit.GetProperty("id").GetGuid().Should().Be(Harbor.UnitNamed("North Coast").Value, "Projects gives the id");
        unit.GetProperty("path").GetString().Should().Be("Harbor Works / North / North Coast", "and Tenancy the path");
        unit.GetProperty("kind").GetString().Should().Be("area");

        var owner = pier.GetProperty("owner");
        owner.GetProperty("id").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Leo).Value);
        owner.GetProperty("displayName").GetString().Should().Be(DemoPeople.Leo.Name);
        owner.GetProperty("status").GetString().Should().Be("active");

        var crew = pier.GetProperty("crew").EnumerateArray()
            .Select(member => (
                Name: member.GetProperty("seat").GetProperty("displayName").GetString(),
                IsOwner: member.GetProperty("isOwner").GetBoolean(),
                Roles: string.Join(", ", member.GetProperty("roles").EnumerateArray().Select(held => held.GetProperty("role").GetProperty("name").GetString()))))
            .ToList();
        crew.Should().BeEquivalentTo(
        [
            (DemoPeople.Leo.Name, true, "Crew lead"),
            (DemoPeople.Juno.Name, false, "Surveyor"),
            (DemoPeople.Vic.Name, false, "Observer"),
        ]);
    }

    [Fact]
    public async Task A_seat_of_another_tenant_is_its_id_with_nothing_else_and_no_error()
    {
        // A project of harbor whose owner is a seat of meadow: no use case makes one, so it is stored directly, as
        // system work in harbor. Tenancy's directory answers nothing for that seat to anybody of harbor.
        await using var host = await sample.StartAsync();
        var stray = Meadow.Administrator.Id;

        using (TenancyUseCases.BeginSystemIn(Harbor.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var projects = scope.ServiceProvider.GetRequiredService<ProjectsContext>();
            projects.Projects.Add(new Project(ProjectId.CreateSequential(), Harbor.Id, "P-950", "Outer breakwater", Harbor.Root, stray, Harbor.ProjectRoles[SampleCatalogue.CrewLead], DateTimeOffset.UtcNow));
            await projects.SaveChangesAsync(Cancellation);
        }

        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var answer = await ada.GraphQLAsync("""{ projects(text: "Outer breakwater") { nodes { name unit { path } owner { id displayName status } } } }""");

        // The reference is there, because Projects named it; Tenancy filled in nothing, and said nothing. Not there
        // and not yours to see read the same.
        answer.TryGetProperty("errors", out _).Should().BeFalse("nothing to read is no error, got {0}", answer.GetRawText());
        var project = answer.GetProperty("data").GetProperty("projects").GetProperty("nodes").EnumerateArray().Should().ContainSingle().Subject;
        var owner = project.GetProperty("owner");
        owner.GetProperty("id").GetGuid().Should().Be(stray.Value);
        owner.GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
        owner.GetProperty("status").ValueKind.Should().Be(JsonValueKind.Null);
        project.GetProperty("unit").GetProperty("path").GetString().Should().Be("Harbor Works", "the rest of the answer stands");
    }

    [Fact]
    public async Task A_project_out_of_reach_is_nothing_by_its_id_and_as_a_node_whatever_the_reason()
    {
        // The node ids, as those who may see each project are given them.
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        using var tove = await sample.ClientAsync("tove", Meadow.Slug);
        var pier = await ada.ProjectNodeIdAsync(Harbor.ProjectNamed("Pier 7"));
        var depot = await ada.ProjectNodeIdAsync(Harbor.ProjectNamed("Inland depot"));
        var shed = await tove.ProjectNodeIdAsync(Meadow.ProjectNamed("Garden shed"));

        // Leo sees Pier 7, whose crew he leads, and nothing else: not a project of his own tenant he has no part
        // in, and not one of another tenant. Both are nothing, by the lookup and by the node field alike.
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);
        var data = await leo.GraphQLDataAsync(
            """
            query($pier: ID!, $depot: ID!, $shed: ID!) {
              mine: project(id: $pier) { name }
              mineAsNode: node(id: $pier) { ... on Project { name } }
              elsewhere: project(id: $depot) { name }
              elsewhereAsNode: node(id: $depot) { id }
              otherTenant: project(id: $shed) { name }
              otherTenantAsNode: node(id: $shed) { id }
            }
            """,
            new { pier, depot, shed });

        data.GetProperty("mine").GetProperty("name").GetString().Should().Be("Pier 7");
        data.GetProperty("mineAsNode").GetProperty("name").GetString().Should().Be("Pier 7");
        foreach (var nothing in new[] { "elsewhere", "elsewhereAsNode", "otherTenant", "otherTenantAsNode" })
        {
            data.GetProperty(nothing).ValueKind.Should().Be(JsonValueKind.Null, "{0} is out of Leo's reach", nothing);
        }
    }
}
