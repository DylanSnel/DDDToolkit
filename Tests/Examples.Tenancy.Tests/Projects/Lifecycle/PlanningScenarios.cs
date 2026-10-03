using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Exceptions;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Lifecycle;

/// <summary>
/// A project's planned range: the days the work is planned for, given when the project is opened or by a command
/// of its own later, under the key that edits a project. What Inspections does with it is in its own scenarios.
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
public sealed class PlanningScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task A_project_is_planned_when_it_is_opened_or_later_and_its_plan_can_be_taken_away()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);

        // Opened with its plan: two ISO dates in the body, and the same two in every answer about it.
        using var opened = await ada.PostAsJsonAsync(
            "/projects",
            new { number = "P-100", name = "Harbor wall", unitId = Harbor.UnitNamed("North Coast").Value, plannedFrom = "2026-11-02", plannedUntil = "2026-11-27" },
            Cancellation);
        opened.StatusCode.Should().Be(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Cancellation));
        var wall = (await ada.VisibleProjectsAsync()).Named("Harbor wall");
        (wall.Text("plannedFrom"), wall.Text("plannedUntil")).Should().Be(("2026-11-02", "2026-11-27"));

        // Planned later: Pier 7 has no plan until its lead, who holds projects.edit through the crew, gives it one.
        var before = await leo.ProjectDetailAsync(PierSeven);
        (before.Text("plannedFrom"), before.Text("plannedUntil")).Should().Be((null, null));
        before.GetProperty("can").GetProperty("plan").GetBoolean().Should().BeTrue();
        (await vic.ProjectDetailAsync(PierSeven)).GetProperty("can").GetProperty("plan").GetBoolean().Should().BeFalse("an observer edits nothing");

        using (var planned = await leo.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/planned-range", new { from = "2026-10-05", until = "2026-10-30" }, Cancellation))
        {
            planned.StatusCode.Should().Be(HttpStatusCode.NoContent, await planned.Content.ReadAsStringAsync(Cancellation));
        }

        // Whoever sees the project sees its plan.
        var after = await vic.ProjectDetailAsync(PierSeven);
        (after.Text("plannedFrom"), after.Text("plannedUntil")).Should().Be(("2026-10-05", "2026-10-30"));

        // Both dates left out takes the plan away.
        using (var cleared = await leo.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/planned-range", new { }, Cancellation))
        {
            cleared.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await leo.ProjectDetailAsync(PierSeven)).GetProperty("plannedFrom").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Planning_a_project_takes_the_key_that_edits_it()
    {
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        var body = new { from = "2026-10-05", until = "2026-10-30" };

        using var observed = await vic.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/planned-range", body, Cancellation);
        using var outOfSight = await juno.PutAsJsonAsync($"/projects/{Harbor.ProjectNamed("Bay bridge").Id.Value}/planned-range", body, Cancellation);

        (await observed.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted)).Argument("Key").Should().Be(ProjectKeys.Edit);
        await outOfSight.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
    }

    [Fact]
    public void Planning_raises_its_event_when_the_plan_changes_and_refuses_what_is_no_range_or_a_closed_project()
    {
        var week = new DateRange(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11));
        var project = new Project(
            ProjectId.CreateSequential(),
            Harbor.Id,
            "P-100",
            "Harbor wall",
            Harbor.Root,
            Harbor.Administrator.Id,
            Harbor.ProjectRoles[SampleCatalogue.CrewLead],
            DateTimeOffset.UtcNow);
        project.Planned.Should().BeNull("a project is not planned until somebody plans it");

        project.Plan(week);
        project.Plan(new DateRange(week.From, week.Until));

        project.Planned.Should().Be(week);
        project.DomainEvents.Select(raised => raised.GetType())
            .Should().Equal([typeof(ProjectOpened), typeof(ProjectPlanned)], "the same days again change nothing");

        var backwards = () => project.Plan(new DateRange(week.Until, week.From));
        backwards.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.PlannedRangeInvalid);
        project.Planned.Should().Be(week, "a refusal changes nothing");
        new Project.PlannedRangeIsValid().Check(project).Should().BeNull();

        project.Close();
        var closed = () => project.Plan(null);
        closed.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.Closed);
    }
}
