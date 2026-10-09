using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.HotChocolate.Tests.Infrastructure;
using DDDToolkit.Supporting.Tenancy.Access;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Inspections.Recording;

/// <summary>
/// A project's inspections are a field of the project: Projects owns the type, Inspections adds the field, and
/// the gateway puts the two together, so a client asks for projects with their inspections in one query. The
/// projects of an answer are gathered into a batch, and however many projects a batch holds, Inspections asks
/// Projects once whether the caller may see them and reads their inspections in one statement; and a project out
/// of the caller's reach gets nothing from it.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class InspectionsOfProjectsScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    /// <summary>
    /// What the list of projects calls its rows: a connection's nodes, as a project's inspections are. It is
    /// Projects' to name, and the one word of these documents that is not Inspections'.
    /// </summary>
    private const string Rows = "nodes";

    /// <summary>What both documented requests ask for: the text that tells them from every other in their files.</summary>
    private const string Documented = "inspections(first: 5)";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    private static DemoProject BayBridge => Harbor.ProjectNamed("Bay bridge");

    [Fact]
    public async Task A_page_of_projects_with_their_inspections_costs_one_question_and_one_statement()
    {
        var projects = new CommandCounter();
        var inspections = new CommandCounter();
        var questions = new ConcurrentQueue<(string Key, int Projects)>();

        // A batch leaves when it holds the four projects of Ada's page, however far apart their fields are
        // resolved: what is counted below is what one batch costs, and not how busy this machine is.
        var batches = new WholeBatches();
        batches.WholeAt(4);
        await using var host = await sample.StartAsync(services =>
        {
            batches.AddTo(services);
            services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(projects));
            services.ConfigureDbContext<InspectionsContext>(options => options.AddInterceptors(inspections));

            // The gate Inspections asks, with every question noted: the key, and how many projects it was about.
            services.AddScoped<IProjectGate>(provider => new NotedGate(provider.GetRequiredService<ProjectAccess>(), questions));
        });

        // The test server keeps this flow for the requests it runs, so the counters count what a request sends.
        // Set before any client is made, since each client's handler reads it when it is made.
        host.Server.PreserveExecutionContext = true;
        using var ada = await host.ClientAsync("ada", Harbor.Slug);

        await RecordAsync(ada, PierSeven, "Loose railing");
        await RecordAsync(ada, PierSeven, "Cracked step");
        await RecordAsync(ada, BayBridge, "Missing sign");

        void Watch()
        {
            projects.WatchThisFlow();
            inspections.WatchThisFlow();
            questions.Clear();
        }

        // What the list of projects costs by itself.
        Watch();
        (await ada.GraphQLDataAsync($$"""{ projects { {{Rows}} { number } } }""")).GetProperty("projects").GetProperty(Rows).GetArrayLength().Should().Be(4, "Ada sees every project of harbor");
        var aloneCommands = projects.Commands;
        var alone = aloneCommands.Count;
        inspections.Commands.Should().BeEmpty("nobody asked for an inspection");

        // What a count that is off by one names: the statements of both runs, so the one more or the one missing shows.
        string Sent(IReadOnlyList<string> commands) => string.Join("\n---\n", commands);

        // The same projects, each with its newest inspection: three modules' worth of one answer.
        Watch();
        var listed = (await ada.GraphQLDataAsync($$"""{ projects { {{Rows}} { number inspections(first: 1) { nodes { title } pageInfo { hasNextPage } } } } }"""))
            .GetProperty("projects").GetProperty(Rows).EnumerateArray()
            .ToDictionary(project => project.GetProperty("number").GetString()!, project => project.GetProperty("inspections"));

        Titles(listed[PierSeven.Number]).Should().Equal("Cracked step");
        listed[PierSeven.Number].GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue("another was recorded before it");
        Titles(listed[BayBridge.Number]).Should().Equal("Missing sign");
        Titles(listed[Harbor.ProjectNamed("HQ refit").Number]).Should().BeEmpty("a project she sees and nothing was recorded on has a page, an empty one");

        questions.Should().Equal([(InspectionsOfProjects.RequiredKey, 4)], "Projects is asked once, about the four projects together");
        inspections.Commands.Should().ContainSingle("the inspections of the four projects are one statement, and Inspections sent:\n{0}", Sent(inspections.Commands));
        projects.Commands.Should().HaveCount(
            alone + 1,
            "and that one question is one statement of Projects'; the list alone sent:\n{0}\nand with the inspections:\n{1}",
            Sent(aloneCommands),
            Sent(projects.Commands));

        // Whether she may record, and how many there are, are asked only now that they are selected: one question
        // and one statement more, again for the four projects together.
        Watch();
        var counted = (await ada.GraphQLDataAsync($$"""{ projects { {{Rows}} { number inspections(first: 1) { nodes { title } totalCount canRecord } } } }"""))
            .GetProperty("projects").GetProperty(Rows).EnumerateArray()
            .ToDictionary(project => project.GetProperty("number").GetString()!, project => project.GetProperty("inspections"));

        counted[PierSeven.Number].GetProperty("totalCount").GetInt32().Should().Be(2);
        counted.Values.Should().OnlyContain(connection => connection.GetProperty("canRecord").GetBoolean(), "the administrator may record on every open project");
        questions.Should().BeEquivalentTo([(InspectionsOfProjects.RequiredKey, 4), (ProjectsOpenToRecording.RequiredKey, 4)]);
        inspections.Commands.Should().HaveCount(2, "the pages, and the totals, and Inspections sent:\n{0}", Sent(inspections.Commands));
        projects.Commands.Should().HaveCount(
            alone + 2,
            "the list alone sent:\n{0}\nand with what may be recorded:\n{1}",
            Sent(aloneCommands),
            Sent(projects.Commands));
    }

    [Fact]
    public async Task A_project_out_of_reach_contributes_no_inspections()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", Meadow.Slug);

        await RecordAsync(ada, PierSeven, "Loose railing");
        await RecordAsync(ada, BayBridge, "Missing sign");
        var variables = new Dictionary<string, object?>
        {
            ["pier"] = await ada.ProjectNodeIdAsync(PierSeven),
            ["bridge"] = await ada.ProjectNodeIdAsync(BayBridge),
            ["shed"] = await tove.ProjectNodeIdAsync(Meadow.ProjectNamed("Garden shed")),
        };

        // Leo leads Pier 7's crew and sees no other project. Through the gateway he is given no project but his
        // own, so he is asked for here the way the gateway asks once Projects answered a project: of Inspections'
        // own schema, by the project's id, as the request's caller. Whatever id reaches that schema, what it
        // answers is decided by its own access check.
        JsonElement data;
        using (Callers.Begin(Caller.User(DemoPeople.Leo.Id)))
        using (TenancyCallers.Begin(TenancyCaller<TenantId, SeatId>.InSeat(Harbor.Id, Harbor.SeatOf(DemoPeople.Leo))))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var schema = await host.Services.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync("inspections", Cancellation);

            var answer = await schema.ExecuteAsync(
                OperationRequestBuilder.New()
                    .SetDocument(
                        """
                        query($pier: ID!, $bridge: ID!, $shed: ID!) {
                          mine: projectById(id: $pier) { inspections { nodes { title } canRecord } }
                          elsewhere: projectById(id: $bridge) { id inspections { nodes { title } canRecord } }
                          otherTenant: projectById(id: $shed) { id inspections { nodes { title } canRecord } }
                        }
                        """)
                    .SetVariableValues(variables)
                    .SetServices(scope.ServiceProvider)
                    .Build(),
                Cancellation);

            var json = JsonDocument.Parse(answer.ToJson()).RootElement;
            json.TryGetProperty("errors", out _).Should().BeFalse("out of reach is nothing, not an error; the schema answered {0}", json.GetRawText());
            data = json.GetProperty("data");
        }

        Titles(data.GetProperty("mine").GetProperty("inspections")).Should().Equal("Loose railing");
        data.GetProperty("mine").GetProperty("inspections").GetProperty("canRecord").GetBoolean().Should().BeTrue("the crew's lead may record");

        // The lookup answers the key it was given and nothing else; the field says nothing at all, though an
        // inspection was recorded on Bay bridge. A project of his own tenant he has no part in and one of another
        // tenant read the same.
        foreach (var (name, id) in new[] { ("elsewhere", variables["bridge"]), ("otherTenant", variables["shed"]) })
        {
            data.GetProperty(name).GetProperty("id").GetString().Should().Be((string?)id);
            data.GetProperty(name).GetProperty("inspections").ValueKind.Should().Be(JsonValueKind.Null, "{0} is out of Leo's reach", name);
        }
    }

    [Fact]
    public async Task An_inspection_says_who_wrote_its_row_in_a_projects_page_and_in_the_answer_of_its_mutation()
    {
        await using var host = await sample.StartAsync();
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        var seat = Harbor.SeatOf(DemoPeople.Juno).Value;

        // The mutation answers the one inspection it recorded, read by its id.
        var recorded = (await juno.GraphQLDataAsync(
            $$"""
            mutation($project: ID!) {
              inspectionRecord(input: { project: $project, title: "Loose railing" }) {
                inspection { changedBy { kind seat } }
                {{SampleGraphQLCalls.Errors}}
              }
            }
            """,
            new { project = await juno.ProjectNodeIdAsync(PierSeven) })).GetProperty("inspectionRecord");

        recorded.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        WhoWrote(recorded.GetProperty("inspection")).Should().Be(("seat", seat));

        // The field reads a page of every project of the answer in one statement, and each row of a page says
        // the same of itself: the save ran as the seat that recorded it.
        var listed = (await juno.GraphQLDataAsync($$"""{ projects { {{Rows}} { number inspections { nodes { title changedBy { kind seat } recordedBy { id } } } } } }"""))
            .GetProperty("projects").GetProperty(Rows).EnumerateArray()
            .Single(project => project.GetProperty("number").GetString() == PierSeven.Number)
            .GetProperty("inspections").GetProperty("nodes").EnumerateArray().Should().ContainSingle().Subject;

        listed.GetProperty("title").GetString().Should().Be("Loose railing");
        WhoWrote(listed).Should().Be(("seat", seat));

        static (string? Kind, Guid? Seat) WhoWrote(JsonElement inspection)
        {
            var by = inspection.GetProperty("changedBy");
            return (by.GetProperty("kind").GetString(), by.GetProperty("seat") is { ValueKind: JsonValueKind.String } named ? named.GetGuid() : null);
        }
    }

    /// <summary>
    /// What a reader is shown runs as it is written: the query of the documentation's GraphQL section, and the
    /// request of the host's <c>.http</c> file, each sent exactly as its file has it.
    /// </summary>
    [Fact]
    public async Task The_documented_query_and_the_request_of_the_http_file_run_as_written()
    {
        await using var host = await sample.StartAsync();
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        await RecordAsync(juno, PierSeven, "Handrails on the north pier checked");

        // The documentation: the one fenced query that asks for a project's inspections.
        var docs = await File.ReadAllTextAsync(System.IO.Path.Combine(SampleLayout.RepositoryRoot(), "docs", "tenancy.md"), Cancellation);
        var query = docs.Split("```graphql")[1..].Select(fenced => fenced.Split("```")[0]).Should().ContainSingle(fenced => fenced.Contains(Documented, StringComparison.Ordinal)).Subject;
        string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Be("{ projects { nodes { name inspections(first: 5) { nodes { title } } } } }", "this is the query the documentation shows");

        var shown = (await juno.GraphQLDataAsync(query)).GetProperty("projects").GetProperty("nodes").EnumerateArray()
            .ToDictionary(project => project.GetProperty("name").GetString()!, project => project.GetProperty("inspections"));
        shown.Keys.Should().Equal([PierSeven.Name], "Juno sees the one project she surveys");
        Titles(shown[PierSeven.Name]).Should().Equal("Handrails on the north pier checked");

        // The .http file: the body of its request, posted as the file has it, by the person the file sends it as.
        var requests = await File.ReadAllLinesAsync(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SampleLayout.HostProjectFile())!, "Examples.Tenancy.Host.http"), Cancellation);
        var body = requests.Should().ContainSingle(line => line.Contains(Documented, StringComparison.Ordinal)).Subject;
        requests[Array.IndexOf(requests, body) - 4].Should().Contain("{{juno.", "the file sends the request as Juno");

        using var content = new StringContent(body, System.Text.Encoding.UTF8, MediaTypeNames.Application.Json);
        using var response = await juno.PostAsync("/graphql", content, Cancellation);
        var answer = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        answer.TryGetProperty("errors", out _).Should().BeFalse("the gateway answered {0}", answer.GetRawText());

        var project = answer.GetProperty("data").GetProperty("projects").GetProperty("nodes").EnumerateArray().Should().ContainSingle().Subject;
        var inspections = project.GetProperty("inspections");
        project.GetProperty("name").GetString().Should().Be(PierSeven.Name);
        inspections.GetProperty("nodes").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("recordedBy").GetProperty("displayName").GetString().Should().Be(DemoPeople.Juno.Name, "Tenancy names the seat that recorded it");
        inspections.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeFalse();
        inspections.GetProperty("canRecord").GetBoolean().Should().BeTrue("a surveyor records");
    }

    private static async Task RecordAsync(HttpClient client, DemoProject project, string title)
    {
        using var recorded = await client.PostAsJsonAsync($"/projects/{project.Id.Value}/inspections", new { title }, Cancellation);
        recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Cancellation));
    }

    private static IEnumerable<string?> Titles(JsonElement connection)
        => connection.GetProperty("nodes").EnumerateArray().Select(inspection => inspection.GetProperty("title").GetString());

    /// <summary>Projects' gate, with every question it is asked noted before it is passed on.</summary>
    private sealed class NotedGate(IProjectGate gate, ConcurrentQueue<(string Key, int Projects)> questions) : IProjectGate
    {
        public Task<ProjectAnswer> AskAsync(ProjectId project, string key, CancellationToken cancellationToken)
        {
            questions.Enqueue((key, 1));
            return gate.AskAsync(project, key, cancellationToken);
        }

        public Task<IReadOnlyDictionary<ProjectId, ProjectAnswer>> AskAsync(IReadOnlyCollection<ProjectId> projects, string key, CancellationToken cancellationToken)
        {
            questions.Enqueue((key, projects.Count));
            return gate.AskAsync(projects, key, cancellationToken);
        }
    }
}
