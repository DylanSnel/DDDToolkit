using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Inspections.Recording;

/// <summary>
/// A project's inspections come newest first and a page at a time, by HotChocolate's own paging: the route and the
/// GraphQL field read the same pages and give the same cursors. A cursor holds what the list is ordered by, the
/// moment and the id, so inspections recorded at one moment are each listed once, whichever page they fall on.
/// </summary>
/// <remarks>
/// The first scenario stops the application's clock, which stamps when an inspection was recorded. No rule of the
/// database compares that moment with its own clock, so the clock may stand still; what the database does decide
/// is how exactly it keeps the moment a cursor is made from. Its host runs on that clock from the start, so it
/// seeds the demonstration itself.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class InspectionListScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    private static string Inspections => $"/projects/{PierSeven.Id.Value}/inspections";

    [Fact]
    public async Task Inspections_page_newest_first_through_the_route_and_forward_and_backward_through_GraphQL()
    {
        // A clock that stands still, so several inspections are recorded at one moment. The host seeds itself: the
        // demonstration's periods are measured from the clock of the host that seeds it, and this host has its own.
        var clock = new StoppedClock();
        await using var host = await sample.StartAsync(services => services.AddSingleton<TimeProvider>(clock), seeded: false);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        var older = new List<Guid>();
        foreach (var title in new[] { "Loose railing", "Cracked step", "Missing sign" })
        {
            older.Add(await RecordAsync(juno, title));
        }

        clock.Now += TimeSpan.FromMinutes(5);
        var newest = await RecordAsync(juno, "Blocked drain");

        // The route, two to a page. The first holds the newest and one of the three of the earlier moment; the
        // second the other two, though they were recorded at the very moment the first page ended at.
        var first = await PageAsync(juno, "?size=2");
        var second = await PageAsync(juno, "?size=2&after=" + Uri.EscapeDataString(first.GetProperty("next").GetString()!));

        Ids(first).Should().HaveCount(2).And.StartWith(newest);
        Ids(second).Should().HaveCount(2);
        second.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null, "nothing follows the last page");
        Ids(first).Concat(Ids(second)).Should().BeEquivalentTo([newest, .. older], "every inspection is listed once");
        first.GetProperty("canRecord").GetBoolean().Should().BeTrue("every page says whether another may be recorded");

        // One to a page walks the same list, in the same order.
        var walked = new List<Guid>();
        string? after = null;
        do
        {
            var page = await PageAsync(juno, "?size=1" + (after is null ? null : "&after=" + Uri.EscapeDataString(after)));
            walked.AddRange(Ids(page));
            after = page.GetProperty("next").GetString();
        }
        while (after is not null);

        walked.Should().Equal(Ids(first).Concat(Ids(second)));

        // The field of the project, forward: the same pages, and the cursor that ends a page is the route's "next".
        var pier = await juno.ProjectNodeIdAsync(PierSeven);
        var forward = await FieldAsync(juno, pier, "first: 2");
        Nodes(forward).Should().Equal(Ids(first));
        forward.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
        forward.GetProperty("pageInfo").GetProperty("endCursor").GetString().Should().Be(first.GetProperty("next").GetString(), "the route and the field page by one mechanism");
        forward.GetProperty("totalCount").GetInt32().Should().Be(4);
        forward.GetProperty("canRecord").GetBoolean().Should().BeTrue("Juno is a surveyor on the crew");

        var onward = await FieldAsync(juno, pier, $"first: 2, after: \"{forward.GetProperty("pageInfo").GetProperty("endCursor").GetString()}\"");
        Nodes(onward).Should().Equal(Ids(second));
        onward.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeFalse();

        // And backward: the last two are the second page, in the list's own order, and before them comes the first.
        var backward = await FieldAsync(juno, pier, "last: 2");
        Nodes(backward).Should().Equal(Ids(second));
        backward.GetProperty("pageInfo").GetProperty("hasPreviousPage").GetBoolean().Should().BeTrue();

        var before = await FieldAsync(juno, pier, $"last: 2, before: \"{backward.GetProperty("pageInfo").GetProperty("startCursor").GetString()}\"");
        Nodes(before).Should().Equal(Ids(first));
        before.GetProperty("pageInfo").GetProperty("hasPreviousPage").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("?size=0")]
    [InlineData("?size=201")]
    public async Task A_page_size_out_of_range_is_refused(string query)
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);

        using var response = await juno.GetAsync(Inspections + query, Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, InspectionRefusals.PageSizeInvalid);
    }

    [Fact]
    public async Task A_page_asked_for_from_both_ends_is_refused_and_each_end_is_held_to_the_sizes_on_its_own()
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        var pier = await juno.ProjectNodeIdAsync(PierSeven);

        async Task<JsonElement> AskedAsync(string paging)
            => await juno.GraphQLAsync($$"""query($id: ID!) { project(id: $id) { inspections({{paging}}) { nodes { id } } } }""", new { id = pier });

        // The first two and the last two at once is no page of the list: refused with a code that says so.
        var both = (await AskedAsync("first: 2, last: 2")).SingleError();
        both.Code().Should().Be(InspectionRefusals.PageFromBothEnds);
        both.Kind().Should().Be("invalid");

        // A size of nothing is no page, from whichever end.
        (await AskedAsync("first: 0")).SingleError().Code().Should().Be(InspectionRefusals.PageSizeInvalid);
        (await AskedAsync("last: 0")).SingleError().Code().Should().Be(InspectionRefusals.PageSizeInvalid);
    }

    /// <summary>
    /// A marker that is not a cursor of this list is refused, by the route and by the project's field alike: a
    /// text that is no cursor, a cursor of another list, one cut short, and a cursor of this list with a head
    /// somebody wrote. None is answered an empty page, and none fails inside the paging.
    /// </summary>
    [Fact]
    public async Task A_marker_that_is_no_cursor_of_the_list_is_refused_by_the_route_and_by_the_field()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        foreach (var title in new[] { "Loose railing", "Cracked step" })
        {
            await RecordAsync(ada, title);
        }

        // A cursor of this list, and one of another: the list of projects, ordered by a number alone.
        var own = (await PageAsync(ada, "?size=1")).GetProperty("next").GetString()!;
        var ofProjects = (await ada.ProjectPageAsync("?size=1")).GetProperty("next").GetString()!;
        var pier = await ada.ProjectNodeIdAsync(PierSeven);

        const string Field = "query($id: ID!, $after: String) { project(id: $id) { number inspections(after: $after) { nodes { id } } } }";
        const string OfThePage = "query($after: String) { projects { nodes { number inspections(after: $after) { nodes { id } } } } }";
        foreach (var marker in new[] { Markers.PlainText, Markers.HeadAlone, ofProjects, own[..^4], Markers.UnreadableHead, Markers.WithHead(own, "3|0|99") })
        {
            using var route = await ada.GetAsync(Inspections + "?size=2&after=" + Uri.EscapeDataString(marker), Cancellation);
            await route.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, InspectionRefusals.CursorInvalid);

            // The field is refused and the project it is a field of stays: the gateway hands on the refusal's code.
            var answer = await ada.GraphQLAsync(Field, new { id = pier, after = marker });
            var refused = answer.SingleError();
            refused.Code().Should().Be(InspectionRefusals.CursorInvalid, "after: {0}", marker);
            refused.Kind().Should().Be("invalid");
            answer.GetProperty("data").GetProperty("project").GetProperty("number").GetString().Should().Be(PierSeven.Number);
            answer.GetProperty("data").GetProperty("project").GetProperty("inspections").ValueKind.Should().Be(JsonValueKind.Null);

            // And for every project of a page, which are read together: each is told, and none is given a page.
            var page = await ada.GraphQLAsync(OfThePage, new { after = marker });
            page.GetProperty("errors").EnumerateArray().Should().NotBeEmpty().And.OnlyContain(error => error.Code() == InspectionRefusals.CursorInvalid, "after: {0}", marker);
            page.GetProperty("data").GetProperty("projects").GetProperty("nodes").EnumerateArray()
                .Should().HaveCount(4).And.OnlyContain(project => project.GetProperty("inspections").ValueKind == JsonValueKind.Null);
        }

        // And its own cursor is still read, by both.
        Ids(await PageAsync(ada, "?size=2&after=" + Uri.EscapeDataString(own))).Should().ContainSingle();
        Nodes(await FieldAsync(ada, pier, $"first: 2, after: \"{own}\"")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_cursor_is_no_way_into_a_project_out_of_sight()
    {
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        // Hana sees no project. Whatever cursor or size she sends, the project is not there for her.
        foreach (var query in new[] { string.Empty, "?after=not-a-cursor", "?size=0" })
        {
            using var response = await hana.GetAsync(Inspections + query, Cancellation);
            await response.ShouldBeRefusedAsync(HttpStatusCode.NotFound, InspectionRefusals.ProjectNotFound);
        }
    }

    private static async Task<Guid> RecordAsync(HttpClient client, string title)
    {
        using var recorded = await client.PostAsJsonAsync(Inspections, new { title }, Cancellation);
        recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Cancellation));
        return (await recorded.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();
    }

    /// <summary>A page of Pier 7's inspections, as the route answers it.</summary>
    private static Task<JsonElement> PageAsync(HttpClient client, string query) => client.GetFromJsonAsync<JsonElement>(Inspections + query, Cancellation);

    /// <summary>A page of a project's inspections, as the project's field answers it through the gateway.</summary>
    private static async Task<JsonElement> FieldAsync(HttpClient client, string project, string arguments)
        => (await client.GraphQLDataAsync(
            $$"""
            query($id: ID!) {
              project(id: $id) {
                inspections({{arguments}}) { nodes { id } pageInfo { hasNextPage hasPreviousPage startCursor endCursor } totalCount canRecord }
              }
            }
            """,
            new { id = project })).GetProperty("project").GetProperty("inspections");

    private static IReadOnlyList<Guid> Ids(JsonElement page)
        => [.. page.GetProperty("items").EnumerateArray().Select(inspection => inspection.GetProperty("id").GetGuid())];

    private static IReadOnlyList<Guid> Nodes(JsonElement connection)
        => [.. connection.GetProperty("nodes").EnumerateArray().Select(inspection => inspection.GetProperty("id").GetGuid())];
}
