using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.HotChocolate.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.GraphQL;

/// <summary>
/// Projects in the schema, the way HotChocolate offers it: the list is a connection that pages forward and
/// backward by the cursors REST gives too; a project's crew and what the caller may do to it are read only when
/// asked for, once for the whole page; and a field may ask for a permission key, which refuses the field and
/// leaves the object.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class GraphQLProjectScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Page =
        """
        query($first: Int, $after: String, $last: Int, $before: String) {
          projects(first: $first, after: $after, last: $last, before: $before) {
            nodes { number }
            pageInfo { hasNextPage hasPreviousPage startCursor endCursor }
            totalCount
          }
        }
        """;

    private const string WithRoles = "query($first: Int) { projects(first: $first) { nodes { number crew { isOwner seat { id } roles { appliesNow role { id } } } } } }";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task The_list_pages_forward_and_backward_and_by_the_cursor_the_route_gives()
    {
        // Ada sees all four of harbor's projects. Two to a page, forward from the start.
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        var first = (await ada.GraphQLDataAsync(Page, new { first = 2 })).GetProperty("projects");
        Numbers(first).Should().Equal("P-001", "P-002");
        first.GetProperty("totalCount").GetInt32().Should().Be(4, "the whole list, not the page");
        Info(first, "hasNextPage").GetBoolean().Should().BeTrue();
        Info(first, "hasPreviousPage").GetBoolean().Should().BeFalse();

        var second = (await ada.GraphQLDataAsync(Page, new { first = 2, after = Info(first, "endCursor").GetString() })).GetProperty("projects");
        Numbers(second).Should().Equal("P-003", "P-004");
        Info(second, "hasNextPage").GetBoolean().Should().BeFalse("nothing follows the last page");
        Info(second, "hasPreviousPage").GetBoolean().Should().BeTrue();

        // Backward from the end, and from the start of the page that came back.
        var last = (await ada.GraphQLDataAsync(Page, new { last = 2 })).GetProperty("projects");
        Numbers(last).Should().Equal("P-003", "P-004");
        Info(last, "hasPreviousPage").GetBoolean().Should().BeTrue();

        var before = (await ada.GraphQLDataAsync(Page, new { last = 2, before = Info(last, "startCursor").GetString() })).GetProperty("projects");
        Numbers(before).Should().Equal("P-001", "P-002");
        Info(before, "hasPreviousPage").GetBoolean().Should().BeFalse("nothing comes before the first page");

        // One mechanism under both: the route's "next" is the cursor of the same place, and either takes the other's.
        var route = await ada.ProjectPageAsync("?size=2");
        route.GetProperty("next").GetString().Should().Be(Info(first, "endCursor").GetString());
        Numbers((await ada.GraphQLDataAsync(Page, new { first = 2, after = route.GetProperty("next").GetString() })).GetProperty("projects")).Should().Equal("P-003", "P-004");

        // A cursor is a place in the list, not a right: Juno, on one crew, is given her own list after it.
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        Numbers((await juno.GraphQLDataAsync(Page, new { first = 2 })).GetProperty("projects")).Should().Equal("P-001");
        Numbers((await juno.GraphQLDataAsync(Page, new { first = 2, after = Info(first, "endCursor").GetString() })).GetProperty("projects")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_list_costs_one_statement_and_with_crews_and_abilities_three()
    {
        var counter = new CommandCounter();

        // A batch leaves when it holds every project the request is about, however far apart their fields are
        // resolved: what is counted is what a batch costs, and not how busy this machine is.
        var batches = new WholeBatches();
        await using var host = await sample.StartAsync(services =>
        {
            services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter));
            batches.AddTo(services);
        });

        // The test server normally runs a request apart from the test's flow; here it keeps it, so what the
        // request sends to the database is counted as the test's own. Set before any client is made.
        host.Server.PreserveExecutionContext = true;
        using var ada = await host.ClientAsync("ada", Harbor.Slug);

        // The page alone: the projects, with how each is reached. No crew is read, and nothing a caller may do.
        counter.WatchThisFlow();
        var plain = await ada.GraphQLDataAsync("{ projects { nodes { number name via } } }");

        Numbers(plain.GetProperty("projects")).Should().HaveCount(4);
        counter.Commands.Should().ContainSingle("a list that shows no crew reads none");
        counter.Commands[0].Should().NotContain($"\"{ProjectsContext.CrewRolesTable}\"");

        // With the crew, the caller's own roles and what it may do: one statement for the crews of the whole page
        // and one for the abilities, whether the page holds four projects or one.
        foreach (var size in new[] { 4, 1 })
        {
            batches.Of(size);
            counter.WatchThisFlow();
            var full = await ada.GraphQLDataAsync("query($first: Int) { projects(first: $first) { nodes { number crew { isOwner } myRoles { id } can { rename move } } } }", new { first = size });

            var nodes = full.GetProperty("projects").GetProperty("nodes").EnumerateArray().ToList();
            nodes.Should().HaveCount(size).And.OnlyContain(project => project.GetProperty("crew").GetArrayLength() > 0 && project.GetProperty("can").GetProperty("rename").GetBoolean());
            counter.Commands.Should().HaveCount(3, "a page of {0}: the page, its crews, its abilities", size);
        }

        // The lookup hands its id to a loader: several projects by id, and the same one twice, are one statement.
        var pier = await ada.ProjectNodeIdAsync(Harbor.ProjectNamed("Pier 7"));
        var depot = await ada.ProjectNodeIdAsync(Harbor.ProjectNamed("Inland depot"));
        batches.Of(2);
        counter.WatchThisFlow();
        var found = await ada.GraphQLDataAsync(
            "query($pier: ID!, $depot: ID!) { one: project(id: $pier) { name } other: project(id: $depot) { name } again: project(id: $pier) { number } }",
            new { pier, depot });

        found.GetProperty("one").GetProperty("name").GetString().Should().Be("Pier 7");
        found.GetProperty("other").GetProperty("name").GetString().Should().Be("Inland depot");
        found.GetProperty("again").GetProperty("number").GetString().Should().Be("P-001");
        counter.Commands.Should().ContainSingle("every id the fields of a request ask about is asked in one question");
    }

    /// <summary>
    /// What a request may cost is estimated before it runs, in each module's schema, from the largest page each
    /// list may hold. A page of projects with everything a client can ask of one, its crew with names and roles,
    /// what the caller may do to it and its inspections by their nodes and by their edges, is within that: for the
    /// page a client gets when it names no size, and for the largest it may name.
    /// </summary>
    [Fact]
    public async Task A_page_of_projects_with_crews_abilities_and_inspections_is_within_what_a_request_may_cost()
    {
        const string Everything =
            """
            query($projects: Int, $inspections: Int) {
              projects(first: $projects) {
                nodes {
                  number name via
                  planned { from until }
                  changedBy { kind seat }
                  unit { name }
                  owner { displayName }
                  crew { isOwner seat { displayName } roles { appliesNow role { name } } }
                  myRoles { name }
                  can { rename plan move close reopen manageCrew changeOwner }
                  inspections(first: $inspections) {
                    nodes { title days { from until } recordedBy { displayName } changedBy { kind seat } }
                    edges { cursor node { title days { from until } recordedBy { displayName } changedBy { kind seat } } }
                    pageInfo { hasNextPage endCursor }
                    totalCount
                    canRecord
                  }
                }
                edges { cursor node { number } }
                pageInfo { hasNextPage endCursor }
                totalCount
              }
            }
            """;

        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using (var recorded = await ada.PostAsJsonAsync($"/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}/inspections", new { title = "Loose railing" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // No size named, which is ten of each; and fifty of each, the most a client may name.
        foreach (var sizes in new object?[] { null, new { projects = 50, inspections = 50 } })
        {
            var projects = (await ada.GraphQLDataAsync(Everything, sizes)).GetProperty("projects");

            projects.GetProperty("totalCount").GetInt32().Should().Be(4);
            var pier = projects.GetProperty("nodes").EnumerateArray().Should().HaveCount(4).And.Subject.First();
            pier.GetProperty("owner").GetProperty("displayName").GetString().Should().Be("Leo");
            pier.GetProperty("crew").EnumerateArray().Select(member => member.GetProperty("seat").GetProperty("displayName").GetString()).Should().Equal("Leo", "Juno", "Vic");
            pier.GetProperty("can").GetProperty("rename").GetBoolean().Should().BeTrue();
            pier.GetProperty("inspections").GetProperty("nodes").EnumerateArray().Should().ContainSingle().Which.GetProperty("recordedBy").GetProperty("displayName").GetString().Should().Be("Ada");
            pier.GetProperty("inspections").GetProperty("canRecord").GetBoolean().Should().BeTrue();
        }

        // One more than the most is no page of either list: HotChocolate refuses the size before anything is read.
        foreach (var sizes in new object[] { new { projects = 51, inspections = 10 }, new { projects = 10, inspections = 51 } })
        {
            (await ada.GraphQLAsync(Everything, sizes)).GetProperty("errors").EnumerateArray().Should().NotBeEmpty("{0} asks for a page larger than the schema offers", sizes);
        }
    }

    /// <summary>
    /// What a request may cost is estimated in each module's schema, for each operation the gateway sends it. A
    /// request that is wide rather than deep, the same list many times over under names of its own and, in each
    /// row of each, another list many times over, can stay under every schema's limit and still ask for a great
    /// many rows. So the gateway bounds the request itself: how many fields a document may have, and how deep it
    /// may go. It is refused before a single statement is sent.
    /// </summary>
    [Fact]
    public async Task A_request_that_asks_the_same_lists_many_times_over_is_refused_before_anything_is_read()
    {
        var counter = new CommandCounter();
        await using var host = await sample.StartAsync(services =>
        {
            services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter));
            services.ConfigureDbContext<InspectionsContext>(options => options.AddInterceptors(counter));
        });
        host.Server.PreserveExecutionContext = true;
        using var ada = await host.ClientAsync("ada", Harbor.Slug);

        // Nineteen times the largest page of projects, and of each project nineteen times the largest page of
        // its inspections: 361 lists of fifty in fifty rows each, if it were answered.
        var inspections = string.Join(" ", Enumerable.Range(1, 19).Select(n => $"b{n}: inspections(first: 50) {{ nodes {{ id }} }}"));
        var wide = "{ " + string.Join(" ", Enumerable.Range(1, 19).Select(n => $"a{n}: projects(first: 50) {{ nodes {{ {inspections} }} }}")) + " }";

        counter.WatchThisFlow();
        var answer = await ada.GraphQLAsync(wide);

        answer.TryGetProperty("errors", out var errors).Should().BeTrue("the gateway answered {0}", answer.GetRawText()[..Math.Min(400, answer.GetRawText().Length)]);
        errors.EnumerateArray().Should().NotBeEmpty();
        (answer.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object).Should().BeFalse("nothing of the request is answered");
        counter.Commands.Should().BeEmpty("the request was refused as a document, before any list was read");

        // And one that goes deeper than any screen asks: a project of an inspection of a project, and on. Round
        // three times it is twelve levels, and refused like the wide one: nothing answered, nothing read.
        static string Round(int times)
            => "{ projects(first: 1) { nodes { " + string.Concat(Enumerable.Repeat("inspections(first: 1) { nodes { project { ", times)) + "number" + new string('}', (times * 3) + 2).Replace("}", " }", StringComparison.Ordinal) + " }";

        counter.WatchThisFlow();
        var refused = await ada.GraphQLAsync(Round(3));
        refused.TryGetProperty("errors", out var tooDeep).Should().BeTrue("the gateway answered {0}", refused.GetRawText()[..Math.Min(400, refused.GetRawText().Length)]);
        tooDeep.EnumerateArray().Should().NotBeEmpty();
        (refused.TryGetProperty("data", out var ofTheDeep) && ofTheDeep.ValueKind == JsonValueKind.Object).Should().BeFalse("nothing of the request is answered");
        counter.Commands.Should().BeEmpty("the request was refused for its depth, before any list was read");

        // Round twice the same request is nine levels, within the bound, and answered: so what refused the one
        // above is its depth, and no field it names.
        (await ada.GraphQLDataAsync(Round(2))).GetProperty("projects").GetProperty("nodes").GetArrayLength().Should().Be(1);

        // A page with everything a screen shows of it is neither: the scenario above asks it of this same gateway.
        (await ada.GraphQLDataAsync("{ projects(first: 50) { nodes { number inspections(first: 50) { nodes { id } } } } }")).GetProperty("projects").GetProperty("nodes").GetArrayLength().Should().Be(4);
    }

    [Fact]
    public async Task Who_manages_a_crew_reads_its_roles_and_another_member_is_refused_them_and_keeps_the_member()
    {
        // Leo leads Pier 7's crew, so he manages it: the roles each member holds are his to read.
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);
        var managed = (await leo.GraphQLDataAsync(WithRoles)).GetProperty("projects").GetProperty("nodes").EnumerateArray().Should().ContainSingle().Subject;

        managed.GetProperty("crew").EnumerateArray().Select(member => member.GetProperty("roles").EnumerateArray().Single().GetProperty("role").GetProperty("id").GetGuid())
            .Should().Equal(Harbor.ProjectRoles[SampleCatalogue.CrewLead].Value, Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value, Harbor.ProjectRoles[SampleCatalogue.Observer].Value);

        // Juno surveys on that crew. She sees the project and who is on its crew; the roles are a field under the
        // key that manages a crew, which she does not hold on it.
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        var answer = await juno.GraphQLAsync(WithRoles);

        var project = answer.GetProperty("data").GetProperty("projects").GetProperty("nodes").EnumerateArray().Should().ContainSingle().Subject;
        project.GetProperty("number").GetString().Should().Be("P-001", "the project stays");
        var crew = project.GetProperty("crew").EnumerateArray().ToList();
        crew.Select(member => member.GetProperty("seat").GetProperty("id").GetGuid())
            .Should().Equal([Harbor.SeatOf(DemoPeople.Leo).Value, Harbor.SeatOf(DemoPeople.Juno).Value, Harbor.SeatOf(DemoPeople.Vic).Value], "and so does every member");
        crew.Should().OnlyContain(member => member.GetProperty("roles").ValueKind == JsonValueKind.Null, "the field under the rule is nothing");

        // Refused out loud: one error for each member's field, with the code and the key a command would be
        // refused with.
        var errors = answer.GetProperty("errors").EnumerateArray().ToList();
        errors.Should().HaveCount(3);
        errors.Should().OnlyContain(error => error.Code() == ProjectRefusals.NotPermitted && error.Kind() == "not_permitted");
        errors.Should().OnlyContain(error => error.GetProperty("path").EnumerateArray().Last().GetString() == "roles");
        errors.Should().OnlyContain(error => error.GetProperty("extensions").GetProperty("arguments").GetProperty("Key").GetString() == ProjectKeys.ManageCrew);

        // What she holds herself is not under the rule: her own roles on the crew.
        var own = (await juno.GraphQLDataAsync("{ projects { nodes { myRoles { id } } } }")).GetProperty("projects").GetProperty("nodes")[0];
        own.GetProperty("myRoles").EnumerateArray().Select(role => role.GetProperty("id").GetGuid()).Should().Equal(Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value);
    }

    [Fact]
    public async Task A_page_asks_the_rule_once_however_many_crews_it_shows()
    {
        var counter = new CommandCounter();
        var batches = new WholeBatches();
        await using var host = await sample.StartAsync(services =>
        {
            services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter));
            batches.AddTo(services);
        });
        host.Server.PreserveExecutionContext = true;
        using var ada = await host.ClientAsync("ada", Harbor.Slug);

        // The rule is asked for every member of every crew of the page, and they all wait on one question: which
        // of the ruled keys the caller holds on these projects. One project or four, the same statements, once the
        // projects of the page are one batch: a batch leaves here when it holds them all.
        var costs = new List<int>();
        foreach (var size in new[] { 1, 4 })
        {
            batches.Of(size);
            counter.WatchThisFlow();
            var nodes = (await ada.GraphQLDataAsync(WithRoles, new { first = size })).GetProperty("projects").GetProperty("nodes").EnumerateArray().ToList();

            nodes.Should().HaveCount(size);
            nodes.SelectMany(project => project.GetProperty("crew").EnumerateArray()).Should().OnlyContain(member => member.GetProperty("roles").GetArrayLength() > 0, "she administers the tenant");
            costs.Add(counter.Commands.Count);
        }

        costs.Should().Equal([4, 4], "the page, its crews, and the keys held: at the root and on the projects");
    }

    [Fact]
    public async Task Two_changes_in_one_request_each_answer_the_project_as_that_change_left_it()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var pier = await leo.ProjectNodeIdAsync(Harbor.ProjectNamed("Pier 7"));
        var tove = Harbor.SeatOf(DemoPeople.Tove).Value;

        // Tove is put on the crew and taken off it again, in one request. A loader remembers what it loaded for
        // the request, so the second answer must not be the crew the first one read.
        var data = await leo.GraphQLDataAsync(
            """
            mutation($id: ID!, $seat: UUID!) {
              added: crewMemberAdd(input: { id: $id, seatId: $seat }) { project { version crew { seat { id } } can { manageCrew } } errors { __typename } }
              removed: crewMemberRemove(input: { id: $id, seatId: $seat }) { project { version crew { seat { id } } can { manageCrew } } errors { __typename } }
            }
            """,
            new { id = pier, seat = tove });

        var added = data.GetProperty("added").GetProperty("project");
        var removed = data.GetProperty("removed").GetProperty("project");
        Seats(added).Should().HaveCount(4).And.Contain(tove);
        Seats(removed).Should().HaveCount(3).And.NotContain(tove, "the second change is answered with the crew as it left it");
        removed.GetProperty("version").GetInt64().Should().BeGreaterThan(added.GetProperty("version").GetInt64());

        static IEnumerable<Guid> Seats(JsonElement project) => project.GetProperty("crew").EnumerateArray().Select(member => member.GetProperty("seat").GetProperty("id").GetGuid());
    }

    private static IEnumerable<string?> Numbers(JsonElement projects)
        => projects.GetProperty("nodes").EnumerateArray().Select(project => project.GetProperty("number").GetString());

    private static JsonElement Info(JsonElement projects, string field) => projects.GetProperty("pageInfo").GetProperty(field);
}
