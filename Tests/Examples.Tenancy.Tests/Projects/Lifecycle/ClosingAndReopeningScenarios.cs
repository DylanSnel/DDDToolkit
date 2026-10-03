using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Exceptions;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Lifecycle;

/// <summary>
/// Closing a project and reopening it, both with one key, <c>projects.close</c>, held through the crew or the
/// organization. Leo owns Pier 7 and leads its crew, Vic observes it, and Rhea manages North, where it is.
/// </summary>
/// <remarks>
/// Every scenario closes a project, so each makes a host of its own.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class ClosingAndReopeningScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task Leo_closes_Pier_7_as_its_owner_and_reopens_it()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        using (var closed = await leo.PostAsync($"/projects/{PierSeven.Id.Value}/close", content: null, Cancellation))
        {
            closed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Closed, it refuses every change: reopening it is all that is left to do.
        var shut = await leo.ProjectDetailAsync(PierSeven);
        shut.Text("state").Should().Be("closed");
        Abilities(shut).Should().Be((false, false, false, true, false, false));

        using (var reopened = await leo.PostAsync($"/projects/{PierSeven.Id.Value}/reopen", content: null, Cancellation))
        {
            reopened.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var open = await leo.ProjectDetailAsync(PierSeven);
        open.Text("state").Should().Be("open");
        Abilities(open).Should().Be((true, false, true, false, true, false));
    }

    [Fact]
    public async Task Rhea_reopens_a_project_in_her_area_and_Vic_may_not()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);

        using (var closed = await leo.PostAsync($"/projects/{PierSeven.Id.Value}/close", content: null, Cancellation))
        {
            closed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Vic still sees the closed project through its crew; his observer's role does not hold the key.
        using (var refused = await vic.PostAsync($"/projects/{PierSeven.Id.Value}/reopen", content: null, Cancellation))
        {
            (await refused.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted)).Argument("Key").Should().Be(ProjectKeys.Close);
        }

        (await vic.ProjectDetailAsync(PierSeven)).GetProperty("can").GetProperty("reopen").GetBoolean().Should().BeFalse();

        // Rhea holds it at North, above the project, and reopens it through the organization.
        var access = await rhea.GetFromJsonAsync<JsonElement>($"/access/projects/{PierSeven.Id.Value}?key={ProjectKeys.Close}", Cancellation);
        access.GetProperty("allowed").GetBoolean().Should().BeTrue();
        access.Text("via").Should().Be("organization");
        (await rhea.ProjectDetailAsync(PierSeven)).GetProperty("can").GetProperty("reopen").GetBoolean().Should().BeTrue();

        using (var reopened = await rhea.PostAsync($"/projects/{PierSeven.Id.Value}/reopen", content: null, Cancellation))
        {
            reopened.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await leo.ProjectDetailAsync(PierSeven)).Text("state").Should().Be("open");
    }

    [Fact]
    public async Task Rhea_closes_Pier_7_in_her_area_and_reopens_it()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        // She is not on its crew: her role at North, above the project, holds the key.
        using (var closed = await rhea.PostAsync($"/projects/{PierSeven.Id.Value}/close", content: null, Cancellation))
        {
            closed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await leo.ProjectDetailAsync(PierSeven)).Text("state").Should().Be("closed");

        using (var reopened = await rhea.PostAsync($"/projects/{PierSeven.Id.Value}/reopen", content: null, Cancellation))
        {
            reopened.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await leo.ProjectDetailAsync(PierSeven)).Text("state").Should().Be("open");
    }

    [Fact]
    public void Only_a_closed_project_is_reopened_and_reopening_raises_its_event()
    {
        var project = new Project(
            ProjectId.CreateSequential(),
            Harbor.Id,
            "P-100",
            "Harbor wall",
            Harbor.Root,
            Harbor.Administrator.Id,
            Harbor.ProjectRoles[SampleCatalogue.CrewLead],
            DateTimeOffset.UtcNow);

        var reopenOpen = () => project.Reopen();
        reopenOpen.Should().Throw<RefusalException>().Which.Code.Should().Be(ProjectRefusals.NotClosed);

        project.Close();
        project.Reopen();

        project.State.Should().Be(ProjectState.Open);
        project.DomainEvents.Select(raised => raised.GetType())
            .Should().Equal(typeof(ProjectOpened), typeof(ProjectClosed), typeof(ProjectReopened));
        project.DomainEvents.OfType<ProjectReopened>().Single().ProjectId.Should().Be(project.Id);
    }

    /// <summary>The can flags of one project, as its detail answers them, in the order the API lists them.</summary>
    private static (bool Rename, bool Move, bool Close, bool Reopen, bool ManageCrew, bool ChangeOwner) Abilities(JsonElement project)
    {
        var can = project.GetProperty("can");
        return (
            can.GetProperty("rename").GetBoolean(),
            can.GetProperty("move").GetBoolean(),
            can.GetProperty("close").GetBoolean(),
            can.GetProperty("reopen").GetBoolean(),
            can.GetProperty("manageCrew").GetBoolean(),
            can.GetProperty("changeOwner").GetBoolean());
    }
}
