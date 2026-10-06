using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.Seats;

/// <summary>
/// The name a seat is shown by is this application's, not Tenancy's: a field of the module's own seat class, which
/// every answer about a seat carries, and which the module's own command renames, under the module's own rule. A
/// seat renames itself; another seat takes <c>tenancy.seats.manage</c> for the whole tenant.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks: the database lets the same callers change the row, and the command's rule is the
/// narrower of the two.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class SeatNameScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Rename =
        $$"""mutation($seat: UUID!, $name: String!) { seatRename(input: { seatId: $seat, displayName: $name }) { seat { id displayName status } {{SampleGraphQLCalls.Errors}} } }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static Guid Leo => Harbor.SeatOf(DemoPeople.Leo).Value;

    [Fact]
    public async Task Leo_renames_his_own_seat_and_every_answer_about_it_shows_the_new_name()
    {
        // It renames, so it has a host of its own. Leo holds no key of the organization's at all.
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        using (var renamed = await leo.PutAsJsonAsync($"/tenancy/seats/{Leo}/name", new { displayName = "  Leo Marsh  " }, Cancellation))
        {
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Who he is, the tenant's seats and the directory by id: each from the seat the directory read, with no read more.
        (await leo.GetFromJsonAsync<JsonElement>("/me", Cancellation)).GetProperty("seat").Text("displayName").Should().Be("Leo Marsh", "the seat's rule drops the space around a name");
        (await leo.GetFromJsonAsync<JsonElement>("/tenancy/seats", Cancellation)).EnumerateArray()
            .Should().Contain(seat => seat.GetProperty("id").GetGuid() == Leo && seat.Text("displayName") == "Leo Marsh");
        using (var byId = await leo.PostAsJsonAsync("/tenancy/directory/seats", new { ids = new[] { Leo } }, Cancellation))
        {
            (await byId.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).EnumerateArray().Should().ContainSingle().Which.Text("displayName").Should().Be("Leo Marsh");
        }

        // The mutation sends the same command, and answers the seat as the directory answers it now.
        var payload = (await leo.GraphQLDataAsync(Rename, new { seat = Leo, name = "Leo" })).GetProperty("seatRename");
        payload.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        payload.GetProperty("seat").Text("displayName").Should().Be("Leo");
        (await leo.GraphQLDataAsync("{ overviewOfMine { seat { displayName } } }")).GetProperty("overviewOfMine").GetProperty("seat").Text("displayName").Should().Be("Leo");
    }

    [Fact]
    public async Task Another_seat_is_renamed_with_seats_manage_for_the_whole_tenant_and_by_the_seats_own_rule()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var ada = await host.ClientAsync("ada", Harbor.Slug);

        // Rhea manages seats at North, where leo is: not for the whole tenant, which renaming another seat takes.
        using (var refused = await rhea.PutAsJsonAsync($"/tenancy/seats/{Leo}/name", new { displayName = "Leopold" }, Cancellation))
        {
            (await refused.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted)).Argument("Key").Should().Be(RenameSeat.RequiredKey);
        }

        // Ada administers harbor and holds it. A name the seat's rule refuses is refused with the rule's own code.
        using (var blank = await ada.PutAsJsonAsync($"/tenancy/seats/{Leo}/name", new { displayName = " " }, Cancellation))
        {
            var problem = await blank.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, Seat.DisplayNameIsValid.ViolationCode);
            (problem.Argument("Max"), problem.Argument("Field")).Should().Be(("200", "displayName"));
        }

        using (var renamed = await ada.PutAsJsonAsync($"/tenancy/seats/{Leo}/name", new { displayName = "Leopold" }, Cancellation))
        {
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await ada.GetFromJsonAsync<JsonElement>("/tenancy/seats", Cancellation)).EnumerateArray()
            .Should().Contain(seat => seat.GetProperty("id").GetGuid() == Leo && seat.Text("displayName") == "Leopold");

        // Tove's seat in meadow is no seat of harbor's: not found, as a seat of nobody is.
        var tovesMeadowSeat = DemoData.Meadow.SeatOf(DemoPeople.Tove).Value;
        using (var elsewhere = await ada.PutAsJsonAsync($"/tenancy/seats/{tovesMeadowSeat}/name", new { displayName = "Tove" }, Cancellation))
        {
            await elsewhere.ShouldBeRefusedAsync(HttpStatusCode.NotFound, TenancyRefusals.SeatNotFound);
        }

        // In GraphQL a refusal arrives in the payload, with the seat left out.
        var payload = (await rhea.GraphQLDataAsync(Rename, new { seat = Leo, name = "Leo" })).GetProperty("seatRename");
        payload.GetProperty("seat").ValueKind.Should().Be(JsonValueKind.Null);
        payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which.Text("code").Should().Be(TenancyRefusals.NotPermitted);
    }

    [Fact]
    public async Task The_tenant_picker_shows_each_of_ones_own_seats_by_the_name_its_tenant_keeps()
    {
        // It renames, so it has a host of its own. Tove gives her seat in meadow another name than harbor knows her by:
        // one person, called differently in each tenant.
        await using var host = await sample.StartAsync();
        var meadow = DemoData.Meadow;
        var tovesMeadowSeat = meadow.SeatOf(DemoPeople.Tove).Value;
        using (var inMeadow = await host.ClientAsync("tove", meadow.Slug))
        using (var renamed = await inMeadow.PutAsJsonAsync($"/tenancy/seats/{tovesMeadowSeat}/name", new { displayName = "Tove of the meadow" }, Cancellation))
        {
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Her own seats are read across tenants, before one is picked, each with the name its tenant keeps, from the
        // seats the package read anyway; and never with her identity.
        using var tove = await host.ClientAsync("tove", tenant: null);
        var mine = (await tove.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation)).EnumerateArray()
            .ToDictionary(row => row.GetProperty("tenant").Text("slug")!, row => row.GetProperty("seat"));

        mine.Keys.Should().BeEquivalentTo([Harbor.Slug, meadow.Slug]);
        mine[Harbor.Slug].Text("displayName").Should().Be(DemoPeople.Tove.Name);
        mine[meadow.Slug].Text("displayName").Should().Be("Tove of the meadow");
        mine[meadow.Slug].GetProperty("id").GetGuid().Should().Be(tovesMeadowSeat);
        mine.Values.SelectMany(seat => seat.EnumerateObject().Select(property => property.Name)).Distinct()
            .Should().BeEquivalentTo(["id", "displayName", "status"], "a seat is answered as every answer writes one, never with an identity");

        // GraphQL's picker answers the Seat every other field answers, with the same name.
        var picked = (await tove.GraphQLDataAsync("{ seatsOfMine { tenant { slug } seat { id displayName status } } }")).GetProperty("seatsOfMine").EnumerateArray()
            .ToDictionary(row => row.GetProperty("tenant").Text("slug")!, row => row.GetProperty("seat").Text("displayName"));
        picked.Should().BeEquivalentTo(new Dictionary<string, string?> { [Harbor.Slug] = DemoPeople.Tove.Name, [meadow.Slug] = "Tove of the meadow" });
    }
}
