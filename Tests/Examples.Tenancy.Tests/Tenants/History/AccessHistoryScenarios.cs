using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.History;

/// <summary>
/// The access history as a list: every change to who may do what in a tenant, newest first and a page at a time,
/// each with who made it. Whoever holds <c>tenancy.history.view</c> for the whole tenant reads it, and so does an
/// operator, for the tenant it names; nobody else, and never another tenant's.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks. There the history's own policies keep a tenant's rows to that tenant's readers, and the
/// operators' role reads every row.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class AccessHistoryScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Granted = "tenancy.organization-role-granted";

    private const string Revoked = "tenancy.organization-role-revoked";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task Who_holds_the_key_reads_who_changed_whose_access_newest_first()
    {
        await using var host = await sample.StartAsync();
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        using var maud = await host.ClientAsync("maud", Harbor.Slug);
        var leo = Harbor.SeatOf(DemoPeople.Leo).Value;
        var grant = $"/tenancy/seats/{leo}/grants";
        var observer = Harbor.Roles[SampleCatalogue.Observer].Value;
        var coast = Harbor.UnitNamed("North Coast").Value;

        // Hana gives leo a role, and takes it away again.
        using (var given = await hana.PostAsJsonAsync(grant, new { unitId = coast, roleId = observer, reason = "Follows the projects on the coast" }, Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var taken = await hana.DeleteAsync($"{grant}/{coast}/{observer}", Cancellation))
        {
            taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Maud, an access admin, reads the two newest rows: what happened last comes first, and each says hana's
        // seat made the change and whose access it was, by ids.
        var newest = await maud.GetFromJsonAsync<JsonElement>("/tenancy/history?size=2", Cancellation);
        var rows = newest.GetProperty("items").EnumerateArray().ToList();

        rows.Select(row => row.Text("event")).Should().Equal(Revoked, Granted);
        foreach (var row in rows)
        {
            row.GetProperty("by").Text("kind").Should().Be("seat");
            row.GetProperty("by").GetProperty("seatId").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Hana).Value);
            row.GetProperty("details").GetProperty("SeatId").GetGuid().Should().Be(leo);
            row.GetProperty("details").GetProperty("RoleId").GetGuid().Should().Be(observer);
            row.GetProperty("details").TryGetProperty("By", out _).Should().BeFalse("who acted is said once, by the row, and without an operator's identity");
            row.GetRawText().Should().NotContain("Follows the projects", "why a role was given stays with the grant").And.NotContain("Leo", "and a row names nobody");
        }

        newest.Text("next").Should().NotBeNull("what the seeding did follows");

        // Page by page to the end: every row once, never one before a newer one, and what the seeding did is the
        // application's own work.
        var all = new List<JsonElement>();
        string? after = null;
        do
        {
            var page = await maud.GetFromJsonAsync<JsonElement>("/tenancy/history?size=25" + (after is null ? null : "&after=" + Uri.EscapeDataString(after)), Cancellation);
            all.AddRange(page.GetProperty("items").EnumerateArray());
            after = page.Text("next");
        }
        while (after is not null);

        all.Take(2).Select(row => row.GetProperty("id").GetGuid()).Should().Equal(rows.Select(row => row.GetProperty("id").GetGuid()));
        all.Select(row => row.GetProperty("id").GetGuid()).Should().OnlyHaveUniqueItems().And.HaveCountGreaterThan(25, "the walk took more than one page")
            .And.Equal(await IdsAsync(maud, "/tenancy/history?size=200"), "the pages are the one list, cut where each marker says");
        all.Select(row => row.Text("event")).Should().Contain("tenancy.tenant-provisioned", "the history goes back to the tenant being made");
        all[^1].GetProperty("occurredAt").GetDateTimeOffset().Should().BeBefore(all[0].GetProperty("occurredAt").GetDateTimeOffset());
        all.Skip(2).Should().OnlyContain(row => row.GetProperty("by").GetProperty("kind").GetString() == "system");
        all.Should().OnlyContain(row => !row.GetRawText().Contains(Meadow.Id.Value.ToString()), "it is harbor's history, and nothing of another tenant's");
    }

    [Theory]
    [InlineData("rhea")]
    [InlineData("hana")]
    public async Task Without_the_key_the_history_is_refused(string person)
    {
        // An area manager runs a region, and the people office gives roles: neither holds the key that reads the
        // history, which counts only when held for the whole tenant.
        using var client = await sample.ClientAsync(person, Harbor.Slug);

        using var response = await client.GetAsync("/tenancy/history", Cancellation);

        (await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted)).Argument("Key").Should().Be(TenancyKeys.HistoryView);
    }

    [Fact]
    public async Task A_page_size_out_of_range_and_a_marker_of_no_list_are_refused()
    {
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);

        foreach (var size in new[] { 0, HistoryRefusals.LargestPage + 1 })
        {
            using var response = await maud.GetAsync($"/tenancy/history?size={size}", Cancellation);
            (await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, HistoryRefusals.PageSizeInvalid)).Argument("Max").Should().Be("200");
        }

        using (var largest = await maud.GetAsync($"/tenancy/history?size={HistoryRefusals.LargestPage}", Cancellation))
        {
            largest.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // A text that is no marker, a head with no value, a marker of another list (the projects', ordered by a
        // number alone), one cut short, and two of this list's own with a head somebody wrote. The history is a
        // route and no field, and the operators' route reads it through the same read.
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        using var orla = await sample.ClientAsync("orla", tenant: null);
        var next = (await maud.GetFromJsonAsync<JsonElement>("/tenancy/history?size=1", Cancellation)).Text("next")!;
        var ofProjects = (await ada.ProjectPageAsync("?size=1")).Text("next")!;
        ofProjects.Should().Be(Markers.From("{}P-001"), "a cursor is the head and what the list is ordered by");

        foreach (var marker in new[] { Markers.PlainText, Markers.HeadAlone, ofProjects, next[..^4], Markers.UnreadableHead, Markers.WithHead(next, "3|0|99") })
        {
            using var unreadable = await maud.GetAsync("/tenancy/history?after=" + Uri.EscapeDataString(marker), Cancellation);
            await unreadable.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.CursorInvalid);

            using var asOperator = await orla.GetAsync($"/operations/tenants/{Harbor.Id.Value}/history?after=" + Uri.EscapeDataString(marker), Cancellation);
            await asOperator.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.CursorInvalid);
        }

        // And its own marker is still read.
        using var onward = await maud.GetAsync("/tenancy/history?size=1&after=" + Uri.EscapeDataString(next), Cancellation);
        onward.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_operator_reads_the_history_of_the_tenant_it_names_and_a_seat_only_its_own()
    {
        using var orla = await sample.ClientAsync("orla", tenant: null);
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);
        using var tove = await sample.ClientAsync("tove", Meadow.Slug);

        var harborAsOperator = await IdsAsync(orla, $"/operations/tenants/{Harbor.Id.Value}/history?size=200");
        var meadowAsOperator = await IdsAsync(orla, $"/operations/tenants/{Meadow.Id.Value}/history?size=200");

        // The operator reads, per tenant, exactly what that tenant's own reader reads; and the two share no row.
        harborAsOperator.Should().NotBeEmpty().And.Equal(await IdsAsync(maud, "/tenancy/history?size=200"));
        meadowAsOperator.Should().NotBeEmpty().And.Equal(await IdsAsync(tove, "/tenancy/history?size=200"));
        harborAsOperator.Should().NotIntersectWith(meadowAsOperator);

        // A tenant that does not exist has no history.
        (await IdsAsync(orla, $"/operations/tenants/{Guid.NewGuid()}/history")).Should().BeEmpty();
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string path)
        => [.. (await client.GetFromJsonAsync<JsonElement>(path, Cancellation)).GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid())];
}
