using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.History;

/// <summary>
/// The access history through the GraphQL schema: a connection over the query the route sends, so a page of the
/// field is the page of the route, cut at the same markers, and it is refused under the same key. Who acted is a
/// kind and, for a seat, the seat, read once for a whole page.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class AccessHistoryFieldScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Page =
        """
        query($first: Int, $after: String) {
          accessHistory(first: $first, after: $after) {
            nodes { id event occurredAt byKind bySeat { id displayName } details }
            pageInfo { hasNextPage endCursor }
          }
        }
        """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task A_page_of_the_field_is_the_page_of_the_route_with_the_seat_that_acted_by_name()
    {
        var sent = new SentRequests();
        await using var host = await sample.StartAsync(sent.AddTo);
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        using var maud = await host.ClientAsync("maud", Harbor.Slug);
        var leo = Harbor.SeatOf(DemoPeople.Leo).Value;
        var grant = $"/tenancy/seats/{leo}/grants";
        var observer = Harbor.Roles[SampleCatalogue.Observer].Value;
        var coast = Harbor.UnitNamed("North Coast").Value;

        // Hana gives leo a role, and takes it away again: the two newest rows, both made by her seat.
        using (var given = await hana.PostAsJsonAsync(grant, new { unitId = coast, roleId = observer }, Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var taken = await hana.DeleteAsync($"{grant}/{coast}/{observer}", Cancellation))
        {
            taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Three rows, so the page holds what a seat did and what the seeding did.
        var byRoute = await maud.GetFromJsonAsync<JsonElement>("/tenancy/history?size=3", Cancellation);
        sent.Clear();
        var first = (await maud.GraphQLDataAsync(Page, new { first = 3 })).GetProperty("accessHistory");
        var rows = first.GetProperty("nodes").EnumerateArray().ToList();

        // The rows of the route, in its order, each with what the route says of it.
        rows.Select(Told).Should().Equal(byRoute.GetProperty("items").EnumerateArray().Select(row => (
            row.GetProperty("id").GetGuid(),
            row.Text("event"),
            row.GetProperty("occurredAt").GetDateTimeOffset(),
            row.GetProperty("by").Text("kind"),
            row.GetProperty("by").GetProperty("seatId").ValueKind == JsonValueKind.Null ? (Guid?)null : row.GetProperty("by").GetProperty("seatId").GetGuid())));
        rows.Select(row => row.Text("event")).Take(2).Should().Equal("tenancy.organization-role-revoked", "tenancy.organization-role-granted");

        // Hana's seat made the two, and the schema says who that is; the event is the stored one, as JSON.
        foreach (var row in rows.Take(2))
        {
            row.Text("byKind").Should().Be("seat");
            row.GetProperty("bySeat").GetProperty("id").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Hana).Value);
            row.GetProperty("bySeat").Text("displayName").Should().Be(DemoPeople.Hana.Name);
            JsonDocument.Parse(row.Text("details")!).RootElement.GetProperty("SeatId").GetGuid().Should().Be(leo);
        }

        // The application's own work is a kind, and no seat.
        rows[2].Text("byKind").Should().Be("system");
        rows[2].GetProperty("bySeat").ValueKind.Should().Be(JsonValueKind.Null);

        // One query for the page, and one question to the directory for the seats its rows name.
        sent.Of<AccessHistory>().Should().ContainSingle();
        sent.Of<SeatsById>().Should().ContainSingle().Which.Ids.Should().Equal(Harbor.SeatOf(DemoPeople.Hana));

        // The page's end is the route's marker, and the page after it the route's next page.
        var info = first.GetProperty("pageInfo");
        info.GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
        info.Text("endCursor").Should().Be(byRoute.Text("next"));

        var next = (await maud.GraphQLDataAsync(Page, new { first = 3, after = info.Text("endCursor") })).GetProperty("accessHistory");
        var nextByRoute = await maud.GetFromJsonAsync<JsonElement>("/tenancy/history?size=3&after=" + Uri.EscapeDataString(byRoute.Text("next")!), Cancellation);
        next.GetProperty("nodes").EnumerateArray().Select(row => row.GetProperty("id").GetGuid())
            .Should().Equal(nextByRoute.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()))
            .And.NotIntersectWith(rows.Select(row => row.GetProperty("id").GetGuid()));

        // The rows are counted only for a client that asks: every row of the tenant, whatever the page.
        var counted = (await maud.GraphQLDataAsync("{ accessHistory(first: 1) { totalCount nodes { id } } }")).GetProperty("accessHistory");
        var all = await maud.GetFromJsonAsync<JsonElement>($"/tenancy/history?size={HistoryRefusals.LargestPage}", Cancellation);
        all.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null, "the largest page holds harbor's whole history");
        counted.GetProperty("totalCount").GetInt32().Should().Be(all.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("rhea")]
    [InlineData("hana")]
    public async Task Without_the_key_the_field_is_refused_with_the_code_the_route_gives(string person)
    {
        using var client = await sample.ClientAsync(person, Harbor.Slug);

        var error = (await client.GraphQLAsync(Page)).SingleError();

        error.Code().Should().Be(TenancyRefusals.NotPermitted);
        error.Kind().Should().Be("not_permitted");
        error.GetProperty("extensions").GetProperty("arguments").Text("Key").Should().Be(TenancyKeys.HistoryView);
    }

    [Fact]
    public async Task A_marker_of_no_list_and_a_page_of_no_rows_are_refused_with_the_codes_the_route_gives()
    {
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);

        (await maud.GraphQLAsync(Page, new { after = "not-a-marker" })).SingleError().Code().Should().Be(TenancyRefusals.CursorInvalid);
        (await maud.GraphQLAsync(Page, new { first = 0 })).SingleError().Code().Should().Be(HistoryRefusals.PageSizeInvalid);
    }

    [Fact]
    public async Task A_page_asked_for_from_both_ends_is_refused_and_each_end_is_held_to_the_sizes_on_its_own()
    {
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);
        const string Field = "query($first: Int, $last: Int) { accessHistory(first: $first, last: $last) { nodes { id } } }";

        // The first two and the last two at once is no page of the list: refused with a code that says so.
        var both = (await maud.GraphQLAsync(Field, new { first = 2, last = 2 })).SingleError();
        both.Code().Should().Be(HistoryRefusals.PageFromBothEnds);
        both.Kind().Should().Be("invalid");

        // Either end alone is a page, and a size of nothing is none, from whichever end.
        (await maud.GraphQLDataAsync(Field, new { last = 2 })).GetProperty("accessHistory").GetProperty("nodes").GetArrayLength().Should().Be(2);
        (await maud.GraphQLAsync(Field, new { last = 0 })).SingleError().Code().Should().Be(HistoryRefusals.PageSizeInvalid);
    }

    /// <summary>What a row of the field says that the route says too: its id, its event, when, and who acted.</summary>
    private static (Guid Id, string? Event, DateTimeOffset OccurredAt, string? ByKind, Guid? BySeat) Told(JsonElement row) => (
        row.GetProperty("id").GetGuid(),
        row.Text("event"),
        row.GetProperty("occurredAt").GetDateTimeOffset(),
        row.Text("byKind"),
        row.GetProperty("bySeat").ValueKind == JsonValueKind.Null ? null : row.GetProperty("bySeat").GetProperty("id").GetGuid());
}
