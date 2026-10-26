using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Inspections.Recording;

/// <summary>
/// The rule that crosses two modules: a project may be planned for a range of days, which is Projects' to know,
/// and the days an inspection covers lie within it, which Inspections holds. Inspections learns the plan from
/// Projects' gate and refuses under a code of its own. Inland depot is the demonstration's one planned project;
/// Rhea owns it, Maud sees it and may not record on it, and Pier 7 has no plan.
/// </summary>
/// <remarks>
/// A test that is refused and changes nothing shares the class's host; a test that changes something makes its own.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class PlannedRangeScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject InlandDepot => Harbor.ProjectNamed("Inland depot");

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task An_inspection_for_days_outside_the_projects_planned_range_is_refused_with_inspections_own_code()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);
        var planned = await PlannedAsync(rhea, InlandDepot);

        // One day past the plan's last is enough: every day an inspection covers lies within the plan.
        var body = new { title = "Roof trusses checked", from = planned.Until, until = planned.Until.AddDays(1) };
        using var past = await rhea.PostAsJsonAsync($"/projects/{InlandDepot.Id.Value}/inspections", body, Cancellation);

        var refused = await past.ShouldBeRefusedAsync(HttpStatusCode.Conflict, InspectionRefusals.OutsidePlannedRange);
        refused.Code.Should().StartWith("inspections.", "the plan is Projects' answer, and the refusal is Inspections' own");
        (refused.Argument("From"), refused.Argument("Until")).Should().Be((Iso(planned.From), Iso(planned.Until)));

        // Maud sees the project and may not record on it: she is refused for the key, and learns nothing from the days.
        using var notHers = await maud.PostAsJsonAsync($"/projects/{InlandDepot.Id.Value}/inspections", body, Cancellation);
        await notHers.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);

        var list = await rhea.GetFromJsonAsync<JsonElement>($"/projects/{InlandDepot.Id.Value}/inspections", Cancellation);
        list.GetProperty("items").EnumerateArray().Should().BeEmpty("a refusal records nothing");
    }

    [Fact]
    public async Task An_inspection_for_days_within_the_planned_range_is_recorded_with_its_days()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var planned = await PlannedAsync(rhea, InlandDepot);

        // The plan's own first and last day are within it; so is today, which is what leaving the days out means.
        using (var whole = await rhea.PostAsJsonAsync($"/projects/{InlandDepot.Id.Value}/inspections", new { title = "Foundation trench checked", from = planned.From, until = planned.Until }, Cancellation))
        {
            whole.StatusCode.Should().Be(HttpStatusCode.Created, await whole.Content.ReadAsStringAsync(Cancellation));
        }

        using (var today = await rhea.PostAsJsonAsync($"/projects/{InlandDepot.Id.Value}/inspections", new { title = "Fence walked" }, Cancellation))
        {
            today.StatusCode.Should().Be(HttpStatusCode.Created, await today.Content.ReadAsStringAsync(Cancellation));
        }

        var list = await rhea.GetFromJsonAsync<JsonElement>($"/projects/{InlandDepot.Id.Value}/inspections", Cancellation);
        var items = list.GetProperty("items").EnumerateArray().ToList();
        items.Select(item => (item.Text("title"), item.Text("from"), item.Text("until"))).Should().HaveCount(2)
            .And.Contain(("Foundation trench checked", Iso(planned.From), Iso(planned.Until)));
        var walked = items.Single(item => item.Text("title") == "Fence walked");
        walked.Text("from").Should().Be(walked.Text("until"), "left out, the days are the one day it was recorded");
        planned.Contains(Day(walked.Text("from")!)).Should().BeTrue();
    }

    [Fact]
    public async Task A_project_without_a_planned_range_takes_any_days()
    {
        await using var host = await sample.StartAsync();
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        using var recorded = await juno.PostAsJsonAsync(
            $"/projects/{PierSeven.Id.Value}/inspections",
            new { title = "Old survey, entered late", from = "2000-01-03", until = "2000-01-07" },
            Cancellation);

        recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Cancellation));
    }

    [Fact]
    public async Task Days_that_are_no_range_are_refused_whatever_the_plan()
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);

        using var backwards = await juno.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Railing", from = "2026-10-05", until = "2026-10-04" }, Cancellation);
        using var half = await juno.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Railing", until = "2026-10-04" }, Cancellation);

        await backwards.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, InspectionRefusals.DaysInvalid);
        await half.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, InspectionRefusals.DaysInvalid);
    }

    /// <summary>
    /// Left out, an inspection's day is the clock's day in UTC, whatever day it is where the person stands: east
    /// of UTC the first planned day has begun while the clock still says the day before. The refusal says so, and
    /// a client that sends its user's day records on it.
    /// </summary>
    [Fact]
    public async Task Without_days_an_inspection_takes_the_day_in_UTC_and_a_client_that_sends_its_own_day_records_on_it()
    {
        // A clock that stands still, so "today" is one day from the first request to the last. The host seeds
        // itself: the demonstration's periods are measured from the clock of the host that seeds it.
        var clock = new StoppedClock();
        await using var host = await sample.StartAsync(services => services.AddSingleton<TimeProvider>(clock), seeded: false);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        // Planned from tomorrow by the clock: the day that has begun already for somebody east of UTC.
        var theirToday = DateOnly.FromDateTime(clock.Now.UtcDateTime).AddDays(1);
        using (var planned = await leo.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/planned-range", new { from = theirToday, until = theirToday.AddDays(4) }, Cancellation))
        {
            planned.StatusCode.Should().Be(HttpStatusCode.NoContent, await planned.Content.ReadAsStringAsync(Cancellation));
        }

        using var withoutDays = await juno.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Gangway checked" }, Cancellation);
        var refused = await withoutDays.ShouldBeRefusedAsync(HttpStatusCode.Conflict, InspectionRefusals.OutsidePlannedRange);
        refused.Body.GetProperty("title").GetString().Should().Contain("UTC", "the refusal says which day leaving the days out means");

        using var withItsDay = await juno.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Gangway checked", from = theirToday, until = theirToday }, Cancellation);
        withItsDay.StatusCode.Should().Be(HttpStatusCode.Created, await withItsDay.Content.ReadAsStringAsync(Cancellation));

        var list = await juno.GetFromJsonAsync<JsonElement>($"/projects/{PierSeven.Id.Value}/inspections", Cancellation);
        list.GetProperty("items").EnumerateArray().Single(item => item.Text("title") == "Gangway checked").Text("from").Should().Be(Iso(theirToday));
    }

    /// <summary>The project's planned range, read the way a client reads it: two ISO dates in the project's answer.</summary>
    private static async Task<DateRange> PlannedAsync(HttpClient client, DemoProject project)
    {
        var detail = await client.ProjectDetailAsync(project);
        return new DateRange(Day(detail.Text("plannedFrom")!), Day(detail.Text("plannedUntil")!));
    }

    private static DateOnly Day(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
