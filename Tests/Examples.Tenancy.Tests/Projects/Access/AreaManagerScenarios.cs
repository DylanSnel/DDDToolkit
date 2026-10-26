using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Access;

/// <summary>
/// An area manager sees every project in her area and none outside it. Rhea is the area manager of North: a
/// role granted at North reaches North and every unit below it, and no unit beside or above.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class AreaManagerScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task Rhea_sees_the_projects_of_North_and_nothing_of_South_or_the_root()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        var projects = await rhea.VisibleProjectsAsync();

        // Pier 7 at North Coast and Inland depot at North Inland, both below North. Bay bridge at South Bay and
        // HQ refit at the root are outside her region.
        projects.Names().Should().Equal("Pier 7", "Inland depot");
        projects.Named("Pier 7").Text("via").Should().Be("organization");
        var pier = await rhea.WithNamesAsync(projects.Named("Pier 7"));
        pier.UnitPath.Should().Be("Harbor Works / North / North Coast");
        pier.MyRole.Should().BeNull("she is not on its crew");

        // She owns Inland depot, so she is on its crew as its lead, which is how she is shown to see it.
        projects.Named("Inland depot").Text("via").Should().Be("crew");
        (await rhea.WithNamesAsync(projects.Named("Inland depot"))).MyRole.Should().Be("Crew lead");
    }

    [Fact]
    public async Task Rhea_opening_Bay_bridge_is_not_found()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        using var outside = await rhea.GetAsync($"/projects/{Harbor.ProjectNamed("Bay bridge").Id.Value}", Cancellation);
        using var missing = await rhea.GetAsync($"/projects/{Guid.NewGuid()}", Cancellation);

        var refused = await outside.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        var none = await missing.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        refused.Title.Should().Be(none.Title, "a project out of reach must not be told apart from one that does not exist");
    }

    [Fact]
    public async Task Access_probe_shows_where_projects_view_reaches()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        var units = await rhea.GetFromJsonAsync<JsonElement>($"/access/units?key={ProjectKeys.View}", Cancellation);
        var onPier = await rhea.GetFromJsonAsync<JsonElement>($"/access/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}?key={ProjectKeys.Close}", Cancellation);
        using var onBridge = await rhea.GetAsync($"/access/projects/{Harbor.ProjectNamed("Bay bridge").Id.Value}?key={ProjectKeys.View}", Cancellation);

        // The answer is ids: North, where she holds the key, and the two units below it.
        units.GetProperty("unitsWhereIHold").EnumerateArray().Select(unit => unit.GetGuid())
            .Should().BeEquivalentTo([Harbor.UnitNamed("North").Value, Harbor.UnitNamed("North Coast").Value, Harbor.UnitNamed("North Inland").Value]);
        units.GetProperty("wholeTenant").GetBoolean().Should().BeFalse();

        onPier.GetProperty("allowed").GetBoolean().Should().BeTrue("an area manager may close a project in her area");
        onPier.Text("via").Should().Be("organization");
        await onBridge.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
    }
}
