using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Lifecycle;

/// <summary>
/// Moving a unit or a project changes who sees it, while nobody's grant changes: what a role held at a unit
/// reaches follows the tree, and a project follows the unit it hangs at. So moving a project asks, besides
/// editing it, for opening one at the unit it goes to.
/// </summary>
/// <remarks>
/// Every test here moves something, so each makes a host of its own.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class MovingScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task Moving_North_Coast_under_South_hides_Pier_7_from_rhea()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var pier = Harbor.ProjectNamed("Pier 7");

        (await rhea.VisibleProjectsAsync()).Names().Should().Contain("Pier 7");

        using (var moved = await ada.PutAsJsonAsync(
                   $"/tenancy/units/{Harbor.UnitNamed("North Coast").Value}/parent",
                   new { parentId = Harbor.UnitNamed("South").Value },
                   Cancellation))
        {
            moved.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await rhea.VisibleProjectsAsync()).Names().Should().Equal("Inland depot");
        using var opened = await rhea.GetAsync($"/projects/{pier.Id.Value}", Cancellation);
        await opened.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
    }

    [Fact]
    public async Task Moving_Bay_bridge_to_North_Coast_shows_it_to_rhea()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var bridge = Harbor.ProjectNamed("Bay bridge");

        (await rhea.VisibleProjectsAsync()).Names().Should().NotContain("Bay bridge");

        using (var moved = await ada.PutAsJsonAsync($"/projects/{bridge.Id.Value}/unit", new { unitId = Harbor.UnitNamed("North Coast").Value }, Cancellation))
        {
            moved.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var projects = await rhea.VisibleProjectsAsync();
        projects.Names().Should().Contain("Bay bridge");
        projects.Named("Bay bridge").Text("via").Should().Be("organization");
        (await rhea.WithNamesAsync(projects.Named("Bay bridge"))).UnitPath.Should().Be("Harbor Works / North / North Coast");
    }

    [Fact]
    public async Task Rhea_moves_Pier_7_within_North_and_not_to_South_Bay()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var pier = Harbor.ProjectNamed("Pier 7");

        // Moving it asks for projects.open at the unit it would go to: she holds it at North and below, not in South.
        using (var south = await rhea.PutAsJsonAsync($"/projects/{pier.Id.Value}/unit", new { unitId = Harbor.UnitNamed("South Bay").Value }, Cancellation))
        {
            var refused = await south.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
            refused.Argument("Key").Should().Be(ProjectKeys.Open);
        }

        using (var inland = await rhea.PutAsJsonAsync($"/projects/{pier.Id.Value}/unit", new { unitId = Harbor.UnitNamed("North Inland").Value }, Cancellation))
        {
            inland.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await rhea.WithNamesAsync(await rhea.ProjectDetailAsync(pier))).UnitPath.Should().Be("Harbor Works / North / North Inland");
    }

    [Fact]
    public async Task Moving_a_project_to_an_archived_unit_is_refused()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);

        using var added = await ada.PostAsJsonAsync("/tenancy/units", new { parentId = Harbor.UnitNamed("North").Value, name = "North Harbor", kind = "area" }, Cancellation);
        added.StatusCode.Should().Be(HttpStatusCode.Created);
        var harborArea = (await added.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();
        using (var archived = await ada.PostAsync($"/tenancy/units/{harborArea}/archive", content: null, Cancellation))
        {
            archived.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var moved = await ada.PutAsJsonAsync($"/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}/unit", new { unitId = harborArea }, Cancellation);

        var refused = await moved.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.UnitNotActive);
        refused.Argument("Unit").Should().Be(harborArea.ToString());
    }
}
