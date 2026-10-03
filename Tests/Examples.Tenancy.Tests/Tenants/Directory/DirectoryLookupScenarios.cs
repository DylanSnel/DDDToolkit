using System.Text.Json;
using DDDToolkit.HotChocolate.Tests.Infrastructure;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.Directory;

/// <summary>
/// What the names in one GraphQL answer cost. Another module names a seat or a unit by its id, and the gateway asks
/// Tenancy for each of them; behind Tenancy's lookups a data loader gathers the ids into a batch, so a field that
/// names seats is one question for a batch, however many seats, and however many projects, it names. A crew's
/// roles are the Projects module's own, and its loader gathers them the same way.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class DirectoryLookupScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task The_seats_units_and_roles_a_field_names_are_one_question_however_many()
    {
        // A host of its own, which notes every command and query it is sent. A batch of the directory's loaders
        // leaves when it holds every id the answer names, however far apart the gateway's lookups get to it, and
        // not when HotChocolate's own dispatcher finds it quiet: what is counted below is what a batch costs the
        // directory, and not how busy this machine is.
        var sent = new SentRequests();
        var batches = new WholeBatches();
        await using var host = await sample.StartAsync(services =>
        {
            sent.AddTo(services);
            batches.AddTo(services);
        });
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var pier = await ada.ProjectNodeIdAsync(Harbor.ProjectNamed("Pier 7"));
        var bridge = await ada.ProjectNodeIdAsync(Harbor.ProjectNamed("Bay bridge"));

        Task<JsonElement> BothAsync(string named)
            => ada.GraphQLDataAsync(
                $$"""query($pier: ID!, $bridge: ID!) { pier: project(id: $pier) { {{named}} } bridge: project(id: $bridge) { {{named}} } }""",
                new { pier, bridge });

        // Two projects at two units, with five people and three roles between them: Leo, Juno and Vic on Pier 7,
        // Ada and Tove on the bridge.
        batches.Of<OrganizationUnitId>(2);
        batches.Of<SeatId>(5);
        batches.Of<ProjectRoleId>(3);
        sent.Clear();
        var data = await BothAsync("unit { path } crew { seat { displayName status } roles { role { name } } }");

        // The names arrived: the gateway resolved every reference, and Projects named its own roles.
        data.GetProperty("pier").GetProperty("unit").GetProperty("path").GetString().Should().Be("Harbor Works / North / North Coast");
        data.GetProperty("pier").GetProperty("crew").EnumerateArray()
            .Select(member => member.GetProperty("seat").GetProperty("displayName").GetString())
            .Should().BeEquivalentTo([DemoPeople.Leo.Name, DemoPeople.Juno.Name, DemoPeople.Vic.Name]);
        data.GetProperty("bridge").GetProperty("crew").EnumerateArray()
            .SelectMany(member => member.GetProperty("roles").EnumerateArray())
            .Select(held => held.GetProperty("role").GetProperty("name").GetString())
            .Should().BeEquivalentTo(["Crew lead", "Surveyor"]);

        // And each field that names something was one question, with every id of both projects in it once.
        sent.Of<SeatsById>().Should().ContainSingle("the seats of both crews are asked together")
            .Which.Ids.Should().BeEquivalentTo(Seats(DemoPeople.Leo, DemoPeople.Juno, DemoPeople.Vic, DemoPeople.Ada, DemoPeople.Tove));
        sent.Of<OrganizationUnitsById>().Should().ContainSingle("the units of both projects are asked together")
            .Which.Ids.Should().BeEquivalentTo([Harbor.UnitNamed("North Coast"), Harbor.UnitNamed("South Bay")]);
        sent.Of<ProjectRolesById>().Should().ContainSingle("the roles of both crews are asked together")
            .Which.Ids.Should().BeEquivalentTo([Harbor.ProjectRoles[SampleCatalogue.CrewLead], Harbor.ProjectRoles[SampleCatalogue.Surveyor], Harbor.ProjectRoles[SampleCatalogue.Observer]]);

        // Each of them left as one batch, whole: the host's requests take their dispatcher from the test, so
        // nothing above was counted by the clock.
        batches.SentOf<SeatId>().Should().Equal([5], "the seats of both crews waited on one batch");
        batches.SentOf<OrganizationUnitId>().Should().Equal([2], "the units of both projects waited on one batch");
        batches.SentOf<ProjectRoleId>().Should().Equal([3], "the roles of both crews waited on one batch");

        // The gateway asks Tenancy once for each field of an answer that is a reference, so the owners are a
        // question of their own although each of them is on a crew as well: one question for the two, never one
        // each. Here they are asked in a request of their own, because a batch says nothing of the field it is
        // for: two seats make the owners' batch whole, where five make the crews'.
        batches.Of<SeatId>(2);
        sent.Clear();
        var owned = await BothAsync("owner { displayName }");

        owned.GetProperty("pier").GetProperty("owner").GetProperty("displayName").GetString().Should().Be(DemoPeople.Leo.Name);
        owned.GetProperty("bridge").GetProperty("owner").GetProperty("displayName").GetString().Should().Be(DemoPeople.Ada.Name);
        sent.Of<SeatsById>().Should().ContainSingle("the owners of both projects are asked together")
            .Which.Ids.Should().BeEquivalentTo(Seats(DemoPeople.Leo, DemoPeople.Ada));
        batches.SentOf<SeatId>().Should().Equal([5, 2], "the owners waited on one batch of their own");

        static List<SeatId> Seats(params DemoPerson[] people) => [.. people.Select(Harbor.SeatOf)];
    }
}
