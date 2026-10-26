using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.Operators;

/// <summary>
/// What an operator reads of Tenancy through the GraphQL schema: every tenant, and the access history of the
/// one it names, as the routes answer them. An operator has no seat, so these fields ask for an operator where
/// every other asks for a seat, and refuse anybody else with the code the routes give.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks. An operator's query runs there as the operators' own database role.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class OperatorFieldScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Tenants = "query($size: Int, $after: String) { tenants(size: $size, after: $after) { items { id slug name status activeSeats } next } }";

    private const string History =
        """
        query($tenant: UUID!, $first: Int, $after: String) {
          tenantAccessHistory(tenant: $tenant, first: $first, after: $after) {
            nodes { id event occurredAt byKind bySeatId details }
            pageInfo { hasNextPage endCursor }
            totalCount
          }
        }
        """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task An_operator_lists_every_tenant_as_the_route_answers_them()
    {
        // No tenant is named: an operator works in none.
        using var orla = await sample.ClientAsync("orla", tenant: null);

        var page = (await orla.GraphQLDataAsync(Tenants)).GetProperty("tenants");
        var byRoute = await orla.GetFromJsonAsync<JsonElement>("/operations/tenants", Cancellation);

        page.GetProperty("items").EnumerateArray().Select(Told).Should().Equal(byRoute.GetProperty("items").EnumerateArray().Select(Told));
        page.GetProperty("items").EnumerateArray().Select(tenant => tenant.Text("slug")).Should().Equal(Harbor.Slug, Meadow.Slug);
        page.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null);

        // A page at a time, by the route's own marker.
        var first = (await orla.GraphQLDataAsync(Tenants, new { size = 1 })).GetProperty("tenants");
        var firstByRoute = await orla.GetFromJsonAsync<JsonElement>("/operations/tenants?size=1", Cancellation);
        first.Text("next").Should().NotBeNull().And.Be(firstByRoute.Text("next"));

        var second = (await orla.GraphQLDataAsync(Tenants, new { size = 1, after = first.Text("next") })).GetProperty("tenants");
        second.GetProperty("items").EnumerateArray().Select(tenant => tenant.Text("slug")).Should().Equal(Meadow.Slug);
    }

    [Fact]
    public async Task An_operator_reads_the_access_history_of_the_tenant_it_names_as_the_route_answers_it()
    {
        await using var host = await sample.StartAsync();
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        using var orla = await host.ClientAsync("orla", tenant: null);
        var grant = $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants";

        // Hana gives leo a role: the newest row of harbor's history, made by her seat, above what the seeding did.
        using (var given = await hana.PostAsJsonAsync(grant, new { unitId = Harbor.UnitNamed("North Coast").Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value }, Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var route = $"/operations/tenants/{Harbor.Id.Value}/history";
        var byRoute = await orla.GetFromJsonAsync<JsonElement>(route + "?size=2", Cancellation);
        var first = (await orla.GraphQLDataAsync(History, new { tenant = Harbor.Id.Value, first = 2 })).GetProperty("tenantAccessHistory");
        var rows = first.GetProperty("nodes").EnumerateArray().ToList();

        // The rows of the route, in its order, each with what the route says of it: who acted is a kind and,
        // for a seat, its id, since no tenant is selected for the directory to say who the seat is.
        rows.Select(Said).Should().Equal(byRoute.GetProperty("items").EnumerateArray().Select(row => (
            row.GetProperty("id").GetGuid(),
            row.Text("event"),
            row.GetProperty("occurredAt").GetDateTimeOffset(),
            row.GetProperty("by").Text("kind"),
            row.GetProperty("by").Text("seatId"))));
        rows.Select(row => (row.Text("byKind"), row.Text("bySeatId"))).Should().Equal(("seat", Harbor.SeatOf(DemoPeople.Hana).Value.ToString()), ("system", null));
        JsonDocument.Parse(rows[0].Text("details")!).RootElement.GetProperty("SeatId").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Leo).Value);

        // The page's end is the route's marker, and the page after it the route's next page.
        first.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
        first.GetProperty("pageInfo").Text("endCursor").Should().Be(byRoute.Text("next"));
        var next = (await orla.GraphQLDataAsync(History, new { tenant = Harbor.Id.Value, first = 2, after = byRoute.Text("next") })).GetProperty("tenantAccessHistory");
        var nextByRoute = await orla.GetFromJsonAsync<JsonElement>(route + "?size=2&after=" + Uri.EscapeDataString(byRoute.Text("next")!), Cancellation);
        next.GetProperty("nodes").EnumerateArray().Select(row => row.GetProperty("id").GetGuid())
            .Should().Equal(nextByRoute.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()))
            .And.NotIntersectWith(rows.Select(row => row.GetProperty("id").GetGuid()));

        // Every row of that tenant is counted, and none of another: the two tenants share no row.
        var all = await orla.GetFromJsonAsync<JsonElement>($"{route}?size={HistoryRefusals.LargestPage}", Cancellation);
        first.GetProperty("totalCount").GetInt32().Should().Be(all.GetProperty("items").GetArrayLength());
        var ofMeadow = (await orla.GraphQLDataAsync(History, new { tenant = Meadow.Id.Value, first = 50 })).GetProperty("tenantAccessHistory").GetProperty("nodes").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToList();
        ofMeadow.Should().NotBeEmpty().And.NotIntersectWith(all.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));

        // A tenant that does not exist has no history, and a marker of no list is refused as the route refuses it.
        (await orla.GraphQLDataAsync(History, new { tenant = Guid.NewGuid() })).GetProperty("tenantAccessHistory").GetProperty("nodes").GetArrayLength().Should().Be(0);
        (await orla.GraphQLAsync(History, new { tenant = Harbor.Id.Value, after = "not-a-marker" })).SingleError().Code().Should().Be(TenancyRefusals.CursorInvalid);
    }

    /// <summary>
    /// The operators' field sends the query through the check the tenant's own <c>accessHistory</c> comes through,
    /// so it refuses the pages that field refuses, with the same codes: none reaches the paging library.
    /// </summary>
    [Fact]
    public async Task A_page_of_a_tenants_history_asked_for_from_both_ends_is_refused_as_the_tenants_own_field_refuses_it()
    {
        using var orla = await sample.ClientAsync("orla", tenant: null);
        const string Field = "query($tenant: UUID!, $first: Int, $last: Int) { tenantAccessHistory(tenant: $tenant, first: $first, last: $last) { nodes { id } } }";

        // The first two and the last two at once is no page of the list: refused with a code that says so.
        var both = (await orla.GraphQLAsync(Field, new { tenant = Harbor.Id.Value, first = 2, last = 2 })).SingleError();
        both.Code().Should().Be(HistoryRefusals.PageFromBothEnds);
        both.Kind().Should().Be("invalid");

        // Either end alone is a page, and a size of nothing is none, from whichever end.
        (await orla.GraphQLDataAsync(Field, new { tenant = Harbor.Id.Value, last = 2 })).GetProperty("tenantAccessHistory").GetProperty("nodes").GetArrayLength().Should().Be(2);
        (await orla.GraphQLAsync(Field, new { tenant = Harbor.Id.Value, last = 0 })).SingleError().Code().Should().Be(HistoryRefusals.PageSizeInvalid);
    }

    [Fact]
    public async Task The_seat_that_acted_is_the_directorys_to_name_and_an_operator_is_refused_it()
    {
        await using var host = await sample.StartAsync();
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        using var orla = await host.ClientAsync("orla", tenant: null);

        using (var given = await hana.PostAsJsonAsync($"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants", new { unitId = Harbor.UnitNamed("North Coast").Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value }, Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // The row is answered with the seat's id. Who that seat is, the directory says to a seat of the tenant,
        // and an operator has none: the one field is refused, with the code the directory's routes give it.
        var answer = await orla.GraphQLAsync(
            "query($tenant: UUID!) { tenantAccessHistory(tenant: $tenant, first: 1) { nodes { byKind bySeatId bySeat { displayName } } } }",
            new { tenant = Harbor.Id.Value });

        answer.SingleError().Code().Should().Be(TenancyRefusals.NotSeated);
        var row = answer.GetProperty("data").GetProperty("tenantAccessHistory").GetProperty("nodes").EnumerateArray().Should().ContainSingle().Subject;
        row.Text("bySeatId").Should().Be(Harbor.SeatOf(DemoPeople.Hana).Value.ToString());
        row.GetProperty("bySeat").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_seat_is_no_operator_and_an_operator_has_no_seat()
    {
        // Harbor's first administrator, who holds every key there is in her tenant.
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        var refused = (await ada.GraphQLAsync(Tenants)).SingleError();
        refused.Code().Should().Be(TenancyRefusals.OperatorsOnly);
        refused.Kind().Should().Be("not_permitted");

        // Her own tenant's history is hers to read as a seat, through accessHistory; the operators' field is not.
        (await ada.GraphQLAsync(History, new { tenant = Harbor.Id.Value })).SingleError().Code().Should().Be(TenancyRefusals.OperatorsOnly);

        // And the other way round: whatever tenant an operator names, a field inside a tenant refuses it.
        using var orla = await sample.ClientAsync("orla", Harbor.Slug);
        (await orla.GraphQLAsync("{ seats { id } }")).SingleError().Code().Should().Be(TenancyRefusals.NotSeated);
        (await orla.GraphQLAsync("{ accessHistory { nodes { id } } }")).SingleError().Code().Should().Be(TenancyRefusals.NotSeated);
    }

    /// <summary>What a row of the history says that the route says too: its id, its event, when, and who acted.</summary>
    private static (Guid Id, string? Event, DateTimeOffset OccurredAt, string? ByKind, string? BySeat) Said(JsonElement row) => (
        row.GetProperty("id").GetGuid(),
        row.Text("event"),
        row.GetProperty("occurredAt").GetDateTimeOffset(),
        row.Text("byKind"),
        row.Text("bySeatId"));

    /// <summary>What an answer says of a tenant, the route's and the field's alike.</summary>
    private static (Guid Id, string? Slug, string? Name, string? Status, int ActiveSeats) Told(JsonElement tenant) => (
        tenant.GetProperty("id").GetGuid(),
        tenant.Text("slug"),
        tenant.Text("name"),
        tenant.Text("status"),
        tenant.GetProperty("activeSeats").GetInt32());
}
