using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Crew;

/// <summary>
/// A project's crew, asked for on its own: <c>GET /projects/{id}/crew</c>. Whoever sees the project sees its
/// crew, through the crew or through the organization, and is answered what the project's own answer carries as
/// its crew. Whoever does not see the project is told there is none.
/// </summary>
/// <remarks>
/// Nothing here changes data, so the class shares one host.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class AllCrewMembersScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task Juno_is_answered_the_crew_of_Pier_7_with_its_owner_first()
    {
        // Juno is the crew's surveyor and holds nothing at a unit: being on the crew is her way in.
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);

        var crew = await juno.CrewAsync(PierSeven);

        var members = crew.EnumerateArray().ToList();
        members.Select(member => member.GetProperty("seatId").GetGuid()).Should().Equal(
            [Harbor.SeatOf(DemoPeople.Leo).Value, Harbor.SeatOf(DemoPeople.Juno).Value, Harbor.SeatOf(DemoPeople.Vic).Value],
            "the owner first, then by when each was put on the crew");
        members.Select(member => member.GetProperty("isOwner").GetBoolean()).Should().Equal(true, false, false);
        members.Should().OnlyContain(member => member.GetProperty("appliesNow").GetBoolean(), "all three are on the crew now");

        // Which role each of them holds there is for whoever manages the crew, and she does not: no roles at all,
        // rather than an empty list, which would say a member holds none.
        members.Should().OnlyContain(member => member.GetProperty("roles").ValueKind == JsonValueKind.Null);

        // Leo leads the crew. His answer names seats and project roles by their ids: what a seat is called is asked
        // of Tenancy's directory, and what a project role is, of the tenant's project roles.
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);
        var managed = (await leo.CrewAsync(PierSeven)).EnumerateArray().ToList();
        managed.Select(member => member.GetProperty("seatId").GetGuid()).Should().Equal(members.Select(member => member.GetProperty("seatId").GetGuid()), "the same crew, in the same order");
        var roles = await leo.ProjectRoleNamesAsync();
        managed.Select(member => roles[member.GetProperty("roles").EnumerateArray().Single().GetProperty("roleId").GetGuid()])
            .Should().Equal("Crew lead", "Surveyor", "Observer");
    }

    [Fact]
    public async Task The_crew_on_its_own_is_the_crew_the_project_is_answered_with()
    {
        // Once for a seat that only sees the crew, and once for the one that manages it.
        foreach (var person in new[] { "juno", "leo" })
        {
            using var client = await sample.ClientAsync(person, Harbor.Slug);

            var crew = await client.CrewAsync(PierSeven);
            var project = await client.ProjectDetailAsync(PierSeven);

            // Member for member and field for field: one description of a crew member serves both answers.
            crew.GetRawText().Should().Be(project.GetProperty("crew").GetRawText(), "as {0}", person);
            crew[0].EnumerateObject().Select(field => field.Name).Should().Equal("seatId", "isOwner", "startsAt", "endsAt", "appliesNow", "roles");
            if (person == "leo")
            {
                crew[0].GetProperty("roles")[0].EnumerateObject().Select(field => field.Name).Should().Equal("roleId", "startsAt", "endsAt", "appliesNow");
            }
        }
    }

    [Fact]
    public async Task Rhea_reaches_Pier_7_through_her_area_and_is_answered_the_same_crew()
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        // Rhea is on no crew of Pier 7: she holds the key to see it at North, above its unit.
        var asRhea = await rhea.CrewAsync(PierSeven);

        asRhea.EnumerateArray().Select(member => member.GetProperty("seatId").GetGuid())
            .Should().Equal((await juno.CrewAsync(PierSeven)).EnumerateArray().Select(member => member.GetProperty("seatId").GetGuid()));
    }

    [Fact]
    public async Task Hana_reaches_no_project_and_is_told_there_is_none()
    {
        // The people office gives roles and sees no project: Pier 7 is not there for her, and neither is its crew.
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        using var response = await hana.GetAsync($"/projects/{PierSeven.Id.Value}/crew", Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
    }

    [Fact]
    public async Task A_project_of_another_tenant_and_one_that_does_not_exist_are_the_same_answer()
    {
        // Tove is seated in both tenants. In meadow, harbor's Pier 7 is not there, exactly as a project nobody opened.
        using var tove = await sample.ClientAsync("tove", DemoData.Meadow.Slug);

        using var ofHarbor = await tove.GetAsync($"/projects/{PierSeven.Id.Value}/crew", Cancellation);
        using var ofNobody = await tove.GetAsync($"/projects/{ProjectId.CreateSequential().Value}/crew", Cancellation);

        var first = await ofHarbor.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        var second = await ofNobody.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        first.Title.Should().Be(second.Title, "the answer never says which it was");
    }
}
