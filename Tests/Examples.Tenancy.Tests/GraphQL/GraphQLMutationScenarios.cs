using System.Text.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.GraphQL;

/// <summary>
/// A mutation sends the command its route sends and answers what it changed, read after the save. What the use
/// case refuses is no top-level error: it is a typed error in the mutation's payload, with its code.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class GraphQLMutationScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Rename =
        $$"""
        mutation($id: ID!, $name: String!, $expectedVersion: Long) {
          projectRename(input: { id: $id, name: $name, expectedVersion: $expectedVersion }) {
            project { name version }
            {{SampleGraphQLCalls.Errors}}
          }
        }
        """;

    private const string Plan =
        $$"""
        mutation($id: ID!, $planned: DateRangeInput) {
          projectPlan(input: { id: $id, planned: $planned }) {
            project { planned { from until } can { plan } }
            {{SampleGraphQLCalls.Errors}}
          }
        }
        """;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task A_refused_mutation_answers_a_refusal_error_with_its_code_and_changes_nothing()
    {
        await using var host = await sample.StartAsync();
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        var pier = await vic.ProjectNodeIdAsync(PierSeven);

        // Vic is an observer on the crew: he sees the project, and may not rename it.
        var data = await vic.GraphQLDataAsync(Rename, new { id = pier, name = "Pier 7 east" });

        var payload = data.GetProperty("projectRename");
        payload.GetProperty("project").ValueKind.Should().Be(JsonValueKind.Null, "a refused command answers no project");
        var error = payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
        error.GetProperty("__typename").GetString().Should().Be("RefusalError");
        error.GetProperty("code").GetString().Should().Be(ProjectRefusals.NotPermitted);
        error.GetProperty("kind").GetString().Should().Be("not_permitted");
        error.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();

        (await vic.ProjectDetailAsync(PierSeven)).GetProperty("name").GetString().Should().Be("Pier 7");
    }

    [Fact]
    public async Task A_rename_answers_the_project_with_its_new_version_and_a_stale_version_is_a_conflict()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var pier = await leo.ProjectNodeIdAsync(PierSeven);
        var read = (await leo.ProjectDetailAsync(PierSeven)).GetProperty("version").GetInt64();

        // With the version he read: the answer is the project as it is now, so no second request is needed for
        // the version the next change takes.
        var renamed = (await leo.GraphQLDataAsync(Rename, new { id = pier, name = "Pier 7 east", expectedVersion = read })).GetProperty("projectRename");
        renamed.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        renamed.GetProperty("project").GetProperty("name").GetString().Should().Be("Pier 7 east");
        renamed.GetProperty("project").GetProperty("version").GetInt64().Should().BeGreaterThan(read);

        // With the version he read before that change: decided on a reading that is no longer what is there.
        var stale = (await leo.GraphQLDataAsync(Rename, new { id = pier, name = "Pier 7 west", expectedVersion = read })).GetProperty("projectRename");
        stale.GetProperty("project").ValueKind.Should().Be(JsonValueKind.Null);
        var conflict = stale.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
        conflict.GetProperty("__typename").GetString().Should().Be("ConcurrencyConflictError");
        conflict.GetProperty("code").GetString().Should().Be(RefusalProblems.ConcurrencyConflict, "the code the route answers its 409 with");

        (await leo.ProjectDetailAsync(PierSeven)).GetProperty("name").GetString().Should().Be("Pier 7 east");
    }

    [Fact]
    public async Task Planning_a_range_that_does_not_hold_answers_a_refusal_error_and_a_range_that_does_the_plan()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var pier = await leo.ProjectNodeIdAsync(PierSeven);

        // The last day before the first: the project refuses it under the code its route answers 400 with.
        var refused = (await leo.GraphQLDataAsync(Plan, new { id = pier, planned = new { from = "2026-10-30", until = "2026-10-05" } })).GetProperty("projectPlan");
        refused.GetProperty("project").ValueKind.Should().Be(JsonValueKind.Null);
        var error = refused.GetProperty("errors").EnumerateArray().Should().ContainSingle().Subject;
        error.GetProperty("__typename").GetString().Should().Be("RefusalError");
        error.GetProperty("code").GetString().Should().Be(ProjectRefusals.PlannedRangeInvalid);
        error.GetProperty("kind").GetString().Should().Be("invalid");
        (await leo.ProjectDetailAsync(PierSeven)).GetProperty("plannedFrom").ValueKind.Should().Be(JsonValueKind.Null, "nothing was planned");

        // The same days the right way round: the answer is the project with its plan, the range of days the
        // modules share, which the gateway composes from the two schemas that show it as one type.
        var planned = (await leo.GraphQLDataAsync(Plan, new { id = pier, planned = new { from = "2026-10-05", until = "2026-10-30" } })).GetProperty("projectPlan");
        planned.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        planned.GetProperty("project").GetProperty("planned").Text("from").Should().Be("2026-10-05");
        planned.GetProperty("project").GetProperty("planned").Text("until").Should().Be("2026-10-30");

        // Left out, the plan is taken away, and the project says nothing where its range was.
        var unplanned = (await leo.GraphQLDataAsync(Plan, new { id = pier })).GetProperty("projectPlan");
        unplanned.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        unplanned.GetProperty("project").GetProperty("planned").ValueKind.Should().Be(JsonValueKind.Null);

        (await leo.GraphQLDataAsync(Plan, new { id = pier, planned = new { from = "2026-10-05", until = "2026-10-30" } })).GetProperty("projectPlan").GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        planned.GetProperty("project").GetProperty("can").GetProperty("plan").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_recorded_inspection_names_its_project_and_its_recorder_through_the_other_modules()
    {
        await using var host = await sample.StartAsync();
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        var pier = await juno.ProjectNodeIdAsync(PierSeven);

        // Inspections answers the inspection with the ids of its project and of the seat that recorded it; the
        // project's number comes from Projects and the names from Tenancy, three modules in one answer.
        const string Inspection = "title project { number unit { path } } recordedBy { displayName }";
        var recorded = (await juno.GraphQLDataAsync(
            $$"""
            mutation($project: ID!) {
              inspectionRecord(input: { project: $project, title: "Loose railing" }) {
                inspection { id {{Inspection}} }
                {{SampleGraphQLCalls.Errors}}
              }
            }
            """,
            new { project = pier })).GetProperty("inspectionRecord");

        recorded.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        Named(recorded.GetProperty("inspection"));

        var listed = (await juno.GraphQLDataAsync(
            $$"""query($project: ID!) { project(id: $project) { inspections { nodes { id {{Inspection}} } canRecord } } }""",
            new { project = pier })).GetProperty("project").GetProperty("inspections");

        listed.GetProperty("canRecord").GetBoolean().Should().BeTrue("Juno is a surveyor on the crew");
        var item = listed.GetProperty("nodes").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("id").GetGuid().Should().Be(recorded.GetProperty("inspection").GetProperty("id").GetGuid(), "the mutation answered the inspection it recorded");
        Named(item);

        static void Named(JsonElement inspection)
        {
            inspection.GetProperty("title").GetString().Should().Be("Loose railing");
            inspection.GetProperty("project").GetProperty("number").GetString().Should().Be("P-001");
            inspection.GetProperty("project").GetProperty("unit").GetProperty("path").GetString().Should().Be("Harbor Works / North / North Coast");
            inspection.GetProperty("recordedBy").GetProperty("displayName").GetString().Should().Be(DemoPeople.Juno.Name);
        }
    }

    [Fact]
    public async Task A_unit_added_is_answered_as_the_organization_lists_it()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);

        var added = (await ada.GraphQLDataAsync(
            $$"""
            mutation($parent: UUID!) {
              organizationUnitAdd(input: { parentId: $parent, name: "North Harbor", kind: area }) {
                organizationUnit { id parentId name kind path depth status }
                {{SampleGraphQLCalls.Errors}}
              }
            }
            """,
            new { parent = Harbor.UnitNamed("North").Value })).GetProperty("organizationUnitAdd");

        added.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        var unit = added.GetProperty("organizationUnit");
        unit.GetProperty("path").GetString().Should().Be("Harbor Works / North / North Harbor");
        unit.GetProperty("depth").GetInt32().Should().Be(3);
        unit.GetProperty("status").GetString().Should().Be("active");
        unit.GetProperty("kind").GetString().Should().Be("area", "the kind is the application's own field, set in the save that added the unit");

        // The kind is the application's to require or not: this one leaves it out, and the unit has none.
        var plain = (await ada.GraphQLDataAsync(
            """
            mutation($parent: UUID!) {
              organizationUnitAdd(input: { parentId: $parent, name: "North Shed" }) { organizationUnit { id kind } }
            }
            """,
            new { parent = Harbor.UnitNamed("North").Value })).GetProperty("organizationUnitAdd").GetProperty("organizationUnit");
        plain.GetProperty("kind").ValueKind.Should().Be(JsonValueKind.Null);

        var listed = (await ada.GraphQLDataAsync("{ organizationUnits { id name kind } }")).GetProperty("organizationUnits").EnumerateArray().ToList();
        listed.Should().Contain(row => row.GetProperty("id").GetGuid() == unit.GetProperty("id").GetGuid() && row.GetProperty("name").GetString() == "North Harbor"
                                       && row.GetProperty("kind").GetString() == "area");
        listed.Should().Contain(row => row.GetProperty("id").GetGuid() == Harbor.Root.Value && row.GetProperty("kind").GetString() == "company",
            "the root's kind was set when the tenant was provisioned");
    }
}
