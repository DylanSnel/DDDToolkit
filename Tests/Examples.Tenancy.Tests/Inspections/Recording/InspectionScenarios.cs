using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Access;
using FluentAssertions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Inspections.Recording;

/// <summary>
/// Crew roles, seen from a second module. Inspections owns no rule about who may record: it asks Projects' gate,
/// which answers from the crew and from the organization alike. Juno is Pier 7's surveyor, Leo its lead and Vic
/// its observer; Rhea manages North, where Pier 7 is.
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
public sealed class InspectionScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    public static TheoryData<string, bool> MayRecordOnPierSeven => new()
    {
        // Through the crew: the lead's and the surveyor's roles grant inspections.record, the observer's does not.
        { "leo", true },
        { "juno", true },
        { "vic", false },

        // Through the organization: the area manager at North, and the administrator, who holds every key.
        { "rhea", true },
        { "ada", true },
    };

    [Fact]
    public async Task Inspections_recorded_at_one_instant_are_listed_in_one_order()
    {
        var counter = new CommandCounter();
        await using var host = await sample.StartAsync(services => services.ConfigureDbContext<InspectionsContext>(options => options.AddInterceptors(counter)));

        // The list is ordered where it is read, newest first by when each inspection was recorded and then by its id:
        // two recorded at the same instant would otherwise come back in whatever order the database had them. Asked
        // as a request asks: as the person, in her seat. A provider quotes an alias or leaves it bare.
        using (Callers.Begin(Caller.User(DemoPeople.Juno.Id)))
        using (TenancyCallers.Begin(TenancyCaller<TenantId, SeatId>.InSeat(Harbor.Id, Harbor.SeatOf(DemoPeople.Juno))))
        {
            await using var scope = host.Services.CreateAsyncScope();
            counter.WatchThisFlow();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ProjectInspections(PierSeven.Id), Cancellation);

            counter.Commands.Should().ContainSingle().Which.Should().MatchRegex("ORDER BY \"?[a-z]\"?\\.\"RecordedAt\" DESC, \"?[a-z]\"?\\.\"Id\"\\s");
        }
    }

    [Fact]
    public async Task Juno_records_an_inspection()
    {
        await using var host = await sample.StartAsync();
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        using var recorded = await juno.PostAsJsonAsync(
            $"/projects/{PierSeven.Id.Value}/inspections",
            new { title = "  Loose railing on the east side  " },
            Cancellation);

        recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Cancellation));
        var id = (await recorded.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();

        var list = await juno.GetFromJsonAsync<JsonElement>($"/projects/{PierSeven.Id.Value}/inspections", Cancellation);
        var inspection = list.GetProperty("items").EnumerateArray().Should().ContainSingle().Which;
        inspection.GetProperty("id").GetGuid().Should().Be(id);
        inspection.GetProperty("title").GetString().Should().Be("Loose railing on the east side");
        inspection.GetProperty("recordedBy").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Juno).Value, "the recorder is the caller's seat");
        inspection.GetProperty("recordedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Vic_recording_is_not_permitted()
    {
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);

        using var response = await vic.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Looks fine" }, Cancellation);

        // He sees Pier 7 through the crew, so the project is found; his observer's role does not let him record.
        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
        refused.Argument("Key").Should().Be(InspectionKeys.Record);
    }

    [Theory]
    [MemberData(nameof(MayRecordOnPierSeven))]
    public async Task The_inspection_list_says_whether_I_may_record(string person, bool canRecord)
    {
        using var client = await sample.ClientAsync(person, Harbor.Slug);

        var list = await client.GetFromJsonAsync<JsonElement>($"/projects/{PierSeven.Id.Value}/inspections", Cancellation);

        list.GetProperty("canRecord").GetBoolean().Should().Be(canRecord);
    }

    [Fact]
    public async Task Recording_on_a_closed_project_is_refused()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        using (var closed = await rhea.PostAsync($"/projects/{PierSeven.Id.Value}/close", content: null, Cancellation))
        {
            closed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var response = await juno.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "After the end" }, Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.Closed);

        // The crew still sees the project and its inspections; the button is empty for everyone.
        foreach (var client in new[] { juno, rhea })
        {
            var list = await client.GetFromJsonAsync<JsonElement>($"/projects/{PierSeven.Id.Value}/inspections", Cancellation);
            list.GetProperty("canRecord").GetBoolean().Should().BeFalse();
            list.GetProperty("items").EnumerateArray().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Recording_resumes_when_the_project_is_reopened()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        using (var closed = await leo.PostAsync($"/projects/{PierSeven.Id.Value}/close", content: null, Cancellation))
        {
            closed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var refused = await juno.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "While it was closed" }, Cancellation))
        {
            await refused.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.Closed);
        }

        using (var reopened = await leo.PostAsync($"/projects/{PierSeven.Id.Value}/reopen", content: null, Cancellation))
        {
            reopened.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Inspections keeps no copy of the project's state: it asks Projects' gate, which answers from the project
        // as it is now.
        using (var recorded = await juno.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Back at work" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Cancellation));
        }

        var list = await juno.GetFromJsonAsync<JsonElement>($"/projects/{PierSeven.Id.Value}/inspections", Cancellation);
        list.GetProperty("canRecord").GetBoolean().Should().BeTrue();
        list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()).Should().Equal("Back at work");
    }

    [Fact]
    public async Task A_project_out_of_sight_is_not_found_for_recording_and_listing()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);
        using var toveInHarbor = await sample.ClientAsync("tove", Harbor.Slug);
        var bayBridge = Harbor.ProjectNamed("Bay bridge").Id.Value;
        var gardenShed = DemoData.Meadow.ProjectNamed("Garden shed").Id.Value;

        // Outside Rhea's region, and in Tove's other tenant: the same answer as a project that does not exist.
        using var outside = await rhea.PostAsJsonAsync($"/projects/{bayBridge}/inspections", new { title = "Cracked pillar" }, Cancellation);
        using var listed = await rhea.GetAsync($"/projects/{bayBridge}/inspections", Cancellation);
        using var elsewhere = await toveInHarbor.PostAsJsonAsync($"/projects/{gardenShed}/inspections", new { title = "Gate latch loose" }, Cancellation);
        using var missing = await rhea.GetAsync($"/projects/{Guid.NewGuid()}/inspections", Cancellation);

        await outside.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        await listed.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        await elsewhere.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        await missing.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
    }

    [Fact]
    public async Task The_project_is_asked_before_the_title_is_checked()
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);

        using var blank = await juno.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "   " }, Cancellation);
        using var blankFromVic = await vic.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "   " }, Cancellation);

        var invalid = await blank.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, InspectionRefusals.TitleInvalid);
        invalid.Argument("Max").Should().Be(Inspection.LongestTitle.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // A caller who may not record learns nothing from the title.
        await blankFromVic.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
    }

    [Fact]
    public async Task System_work_records_for_the_seat_it_acts_for()
    {
        await using var host = await sample.StartAsync();
        var leo = Harbor.SeatOf(DemoPeople.Leo);

        // With no seat to act for there is nobody to record it: that is a mistake in the calling code.
        using (TenantsTenancy.BeginSystemIn(Harbor.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            var record = async () => await sender.Send(new RecordInspection(PierSeven.Id, "Recorded for the lead"), Cancellation);

            (await record.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*the seat it acts for*");
        }

        InspectionId recorded;
        using (TenantsTenancy.BeginSystemIn(Harbor.Id, leo))
        {
            await using var scope = host.Services.CreateAsyncScope();
            recorded = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordInspection(PierSeven.Id, "Recorded for the lead"), Cancellation);
        }

        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        var list = await juno.GetFromJsonAsync<JsonElement>($"/projects/{PierSeven.Id.Value}/inspections", Cancellation);
        var inspection = list.GetProperty("items").EnumerateArray().Should().ContainSingle().Which;
        inspection.GetProperty("id").GetGuid().Should().Be(recorded.Value);
        inspection.GetProperty("recordedBy").GetGuid().Should().Be(leo.Value);
    }

    [Fact]
    public void Inspections_refuses_a_project_with_the_codes_projects_uses()
    {
        // Written out in Inspections, which may not name Projects' own classes; held to Projects' strings here.
        InspectionRefusals.ProjectNotFound.Should().Be(ProjectRefusals.NotFound);
        InspectionRefusals.ProjectNotPermitted.Should().Be(ProjectRefusals.NotPermitted);
        InspectionRefusals.ProjectClosed.Should().Be(ProjectRefusals.Closed);
    }
}
