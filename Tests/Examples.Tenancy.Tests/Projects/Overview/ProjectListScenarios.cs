using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using FluentAssertions;
using GreenDonut.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Projects.Overview;

/// <summary>
/// The project list comes a page at a time, by number, with a cursor for the page after it; it is narrowed by
/// state, by a unit and everything below it, and by text; it counts only when asked; and every row says what its
/// caller may do, as the project's own answer does.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class ProjectListScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task Paging_through_the_list_returns_every_visible_project_once()
    {
        // Ada sees all four of harbor's projects. Two to a page: two pages, in the order of their numbers.
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        var first = await ada.ProjectPageAsync("?size=2");
        Numbers(first).Should().Equal("P-001", "P-002");
        var next = first.GetProperty("next").GetString();
        next.Should().NotBeNullOrEmpty("two more follow");

        var second = await ada.ProjectPageAsync($"?size=2&after={next}");
        Numbers(second).Should().Equal("P-003", "P-004");
        second.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null, "nothing follows the last page");

        // The cursor is a place in the list, not a right: Juno, on one crew, is given the page after it of her own
        // list, which is empty.
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        Numbers(await juno.ProjectPageAsync()).Should().Equal("P-001");
        Numbers(await juno.ProjectPageAsync($"?after={next}")).Should().BeEmpty();
    }

    /// <summary>
    /// A marker that is not a cursor of this list is refused, by the route and by the field alike: a text that is
    /// no cursor, a cursor of another list, and a cursor of this list with a head somebody wrote. None is read as
    /// a place in the list, and none fails inside the paging.
    /// </summary>
    [Fact]
    public async Task A_marker_that_is_no_cursor_of_the_list_is_refused_by_the_route_and_by_the_field()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);

        // A cursor of this list, and one of another: the list of a project's inspections, ordered by other keys.
        var pier = Harbor.ProjectNamed("Pier 7").Id.Value;
        foreach (var title in new[] { "Loose railing", "Cracked step" })
        {
            using var recorded = await ada.PostAsJsonAsync($"/projects/{pier}/inspections", new { title }, Cancellation);
            recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var own = (await ada.ProjectPageAsync("?size=1")).GetProperty("next").GetString()!;
        var ofInspections = (await ada.GetFromJsonAsync<JsonElement>($"/projects/{pier}/inspections?size=1", Cancellation)).GetProperty("next").GetString()!;

        const string Field = "query($after: String, $before: String, $last: Int) { projects(after: $after, before: $before, last: $last) { nodes { number } } }";
        foreach (var marker in new[] { Markers.PlainText, Markers.HeadAlone, ofInspections, Markers.UnreadableHead, Markers.WithHead(own, "3|0|99") })
        {
            using var route = await ada.GetAsync("/projects?size=2&after=" + Uri.EscapeDataString(marker), Cancellation);
            await route.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.CursorInvalid);

            var forward = (await ada.GraphQLAsync(Field, new { after = marker })).SingleError();
            forward.Code().Should().Be(ProjectRefusals.CursorInvalid, "after: {0}", marker);
            forward.Kind().Should().Be("invalid");

            (await ada.GraphQLAsync(Field, new { before = marker, last = 2 })).SingleError().Code().Should().Be(ProjectRefusals.CursorInvalid, "before: {0}", marker);
        }

        // And its own cursor is still read, by both.
        Numbers(await ada.ProjectPageAsync("?size=2&after=" + Uri.EscapeDataString(own))).Should().Equal("P-002", "P-003");
        (await ada.GraphQLDataAsync(Field, new { after = own })).GetProperty("projects").GetProperty("nodes").GetArrayLength().Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(VisibleProjects.LargestPage + 1)]
    public async Task A_page_size_out_of_range_is_refused(int size)
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        using var response = await ada.GetAsync($"/projects?size={size}", Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.PageSizeInvalid);
        refused.Argument("Max").Should().Be(VisibleProjects.LargestPage.ToString());
    }

    [Fact]
    public async Task A_page_asked_for_from_both_ends_is_refused_and_each_end_is_held_to_the_sizes_on_its_own()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        const string Field = "query($first: Int, $last: Int) { projects(first: $first, last: $last) { nodes { number } } }";

        // The first two and the last two at once is no page of the list. It is refused with a code that says so,
        // not passed to the paging, which would throw.
        var both = (await ada.GraphQLAsync(Field, new { first = 2, last = 2 })).SingleError();
        both.Code().Should().Be(ProjectRefusals.PageFromBothEnds);
        both.Kind().Should().Be("invalid");

        // Either end alone is a page, and a size of nothing is none, from whichever end.
        (await ada.GraphQLDataAsync(Field, new { first = 2 })).GetProperty("projects").GetProperty("nodes").EnumerateArray().Select(project => project.GetProperty("number").GetString())
            .Should().Equal("P-001", "P-002");
        (await ada.GraphQLDataAsync(Field, new { last = 2 })).GetProperty("projects").GetProperty("nodes").EnumerateArray().Select(project => project.GetProperty("number").GetString())
            .Should().Equal("P-003", "P-004");
        (await ada.GraphQLAsync(Field, new { first = 0 })).SingleError().Code().Should().Be(ProjectRefusals.PageSizeInvalid);
        (await ada.GraphQLAsync(Field, new { last = 0 })).SingleError().Code().Should().Be(ProjectRefusals.PageSizeInvalid);
    }

    [Fact]
    public async Task The_state_filter_keeps_closed_projects_apart()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using (var closed = await ada.PostAsync($"/projects/{Harbor.ProjectNamed("HQ refit").Id.Value}/close", null, Cancellation))
        {
            closed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        Numbers(await ada.ProjectPageAsync("?state=closed")).Should().Equal("P-004");
        Numbers(await ada.ProjectPageAsync("?state=open")).Should().Equal("P-001", "P-002", "P-003");
        Numbers(await ada.ProjectPageAsync()).Should().HaveCount(4, "with no state named, both are listed");

        using var unknown = await ada.GetAsync("/projects?state=sunk", Cancellation);
        await unknown.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
    }

    [Fact]
    public async Task The_unit_filter_takes_the_subtree_and_never_widens_what_is_seen()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        using var tove = await sample.ClientAsync("tove", Harbor.Slug);
        var north = Harbor.UnitNamed("North").Value;
        var south = Harbor.UnitNamed("South").Value;

        // North's two areas each have a project; none hangs at North itself.
        Numbers(await ada.ProjectPageAsync($"?unit={north}")).Should().Equal("P-001", "P-002");
        Numbers(await ada.ProjectPageAsync($"?unit={Harbor.UnitNamed("North Coast").Value}")).Should().Equal("P-001");
        Numbers(await ada.ProjectPageAsync($"?unit={Harbor.Root.Value}")).Should().HaveCount(4, "the root's subtree is the tenant");

        // Tove sees Bay bridge by its crew and nothing else. Asking about a unit shows her what she sees there,
        // which under North is nothing, and a unit of another tenant holds nothing for anybody.
        Numbers(await tove.ProjectPageAsync($"?unit={south}")).Should().Equal("P-003");
        Numbers(await tove.ProjectPageAsync($"?unit={north}")).Should().BeEmpty();
        Numbers(await ada.ProjectPageAsync($"?unit={DemoData.Meadow.Root.Value}")).Should().BeEmpty();
    }

    [Fact]
    public async Task The_text_filter_matches_numbers_and_names_whatever_the_case()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        Numbers(await ada.ProjectPageAsync("?text=p-00")).Should().HaveCount(4, "a number that starts with the text");
        Numbers(await ada.ProjectPageAsync("?text=002")).Should().BeEmpty("a number is matched from its start");
        Numbers(await ada.ProjectPageAsync("?text=BRIDGE")).Should().Equal("P-003");
        Numbers(await ada.ProjectPageAsync("?text=%20depot%20")).Should().Equal("P-002");

        // The text is text: what a pattern would read as "anything" finds only itself.
        Numbers(await ada.ProjectPageAsync("?text=%25")).Should().BeEmpty();
        Numbers(await ada.ProjectPageAsync("?text=_")).Should().BeEmpty();
    }

    /// <summary>
    /// Which letters have a case is the database's to say, and Postgres folds every letter, the ones outside A to Z
    /// too. Pinned here, so a change of it shows.
    /// </summary>
    [Fact]
    public async Task The_text_filter_folds_every_letter()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using (var opened = await ada.PostAsJsonAsync("/projects", new { number = "P-900", name = "Écluse nord", unitId = Harbor.Root.Value }, Cancellation))
        {
            opened.StatusCode.Should().Be(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Cancellation));
        }

        Numbers(await ada.ProjectPageAsync("?text=NORD")).Should().Equal(["P-900"], "A to Z fold");
        Numbers(await ada.ProjectPageAsync("?text=" + Uri.EscapeDataString("écluse")))
            .Should().Equal(["P-900"], "the capital of the name is outside A to Z, and Postgres folds it");
    }

    [Fact]
    public async Task No_total_unless_asked()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);

        (await ada.ProjectPageAsync()).TryGetProperty("total", out _).Should().BeFalse();

        // The total is of the whole list as it is filtered, not of the page, and of what the caller sees.
        var counted = await ada.ProjectPageAsync("?size=1&count=true");
        Numbers(counted).Should().Equal("P-001");
        counted.GetProperty("total").GetInt32().Should().Be(4);
        (await ada.ProjectPageAsync("?text=pier&count=true")).GetProperty("total").GetInt32().Should().Be(1);
        (await juno.ProjectPageAsync("?count=true")).GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_page_with_its_count_is_read_on_one_context_whatever_its_size()
    {
        var counter = new CommandCounter();
        await using var host = await sample.StartAsync(services => services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter)));

        // Asked as a request asks: as the person, in her seat.
        using (Callers.Begin(Caller.User(Harbor.Administrator.Person.Id)))
        using (TenancyCallers.Begin(TenancyCaller<TenantId, SeatId>.InSeat(Harbor.Id, Harbor.Administrator.Id)))
        {
            string? after = null;
            foreach (var size in new[] { 1, 3 })
            {
                await using var scope = host.Services.CreateAsyncScope();

                // The page and the count of the whole list: one project or three, the same statements, on the
                // query's one context.
                counter.WatchThisFlow();
                var page = await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new VisibleProjects(new PagingArguments(first: size, after: after, includeTotalCount: true)), Cancellation);

                page.Items.Should().HaveCount(size);
                page.TotalCount.Should().Be(4);
                counter.Commands.Count.Should().BeLessThanOrEqualTo(2, "a page of {0}: the page, and at most one statement more for the count", size);
                counter.Rentals.Should().ContainSingle("the whole page is read on the query's one context");
                after = page.CreateCursor(page.Last!.Value);
            }
        }
    }

    [Theory]
    [InlineData("ada")]
    [InlineData("rhea")]
    [InlineData("leo")]
    [InlineData("vic")]
    [InlineData("maud")]
    public async Task Every_item_carries_what_the_caller_may_do(string person)
    {
        using var client = await sample.ClientAsync(person, Harbor.Slug);

        var listed = await client.VisibleProjectsAsync();

        // A row of the list and the project's own answer are decided in one place, so they say the same: Leo leads
        // Pier 7, Vic only looks at it, Maud runs access and none of the work.
        listed.Should().NotBeEmpty();
        foreach (var item in listed)
        {
            var detail = await client.GetFromJsonAsync<JsonElement>($"/projects/{item.GetProperty("id").GetGuid()}", Cancellation);
            item.GetProperty("can").GetRawText().Should().Be(detail.GetProperty("can").GetRawText(), "{0} is told the same about {1} either way", person, item.GetProperty("name").GetString());
            item.GetProperty("version").GetInt64().Should().Be(detail.GetProperty("version").GetInt64());
        }
    }

    private static IEnumerable<string?> Numbers(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(project => project.GetProperty("number").GetString());
}
