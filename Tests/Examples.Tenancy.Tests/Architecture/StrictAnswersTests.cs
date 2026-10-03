using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Architecture;

/// <summary>
/// What Projects and Inspections answer carries ids of Tenancy's seats, units and roles, and none of their names:
/// those modules have no name of Tenancy's to give. A client that shows one asks Tenancy's directory, by id.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class StrictAnswersTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task Answers_of_projects_and_inspections_carry_no_name_of_tenancys()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var pier = Harbor.ProjectNamed("Pier 7").Id.Value;

        // An inspection, so that list has a seat in it: Juno records one on Pier 7.
        using (var recorded = await juno.PostAsJsonAsync($"/projects/{pier}/inspections", new { title = "Railing loose" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Every name Tenancy has of the tenant, as its own API answers the administrator: what its seats are shown
        // by, what its units are called and their paths, and what its roles are called.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        names.UnionWith((await ada.GetFromJsonAsync<JsonElement>("/tenancy/seats", Cancellation)).EnumerateArray().Select(seat => seat.GetProperty("displayName").GetString()!));
        names.UnionWith((await ada.GetFromJsonAsync<JsonElement>("/tenancy/roles", Cancellation)).EnumerateArray().Select(role => role.GetProperty("name").GetString()!));
        foreach (var unit in (await ada.GetFromJsonAsync<JsonElement>("/tenancy/units", Cancellation)).EnumerateArray())
        {
            names.Add(unit.GetProperty("name").GetString()!);
            names.Add(unit.GetProperty("path").GetString()!);
        }

        names.Should().Contain(["Leo", "Seth", "Crew lead", "Observer", "North Coast", "Harbor Works", "Harbor Works / North / North Coast"], "these are the names an answer must not carry")
            .And.HaveCountGreaterThan(Harbor.Seats.Count + Harbor.Units.Count + Harbor.Roles.Count);

        // Rhea sees Pier 7 through her area and Inland depot through its crew, so both ways in are answered.
        (string Route, JsonElement Answer)[] answers =
        [
            ("GET /projects", await rhea.ProjectPageAsync()),
            ("GET /projects/{id}", await rhea.GetFromJsonAsync<JsonElement>($"/projects/{pier}", Cancellation)),
            ("GET /projects/{id}/inspections", await rhea.GetFromJsonAsync<JsonElement>($"/projects/{pier}/inspections", Cancellation)),
            ("GET /access/projects/{id}?key=", await rhea.GetFromJsonAsync<JsonElement>($"/access/projects/{pier}?key={ProjectKeys.View}", Cancellation)),
        ];

        foreach (var (route, answer) in answers)
        {
            var texts = TextsOf(answer).ToList();
            texts.Where(names.Contains).Should().BeEmpty("{0} answers ids, and no name of Tenancy's", route);
        }

        // The answers do carry what the names are asked by: the ids, and the project's own texts.
        var detail = answers[1].Answer;
        TextsOf(detail).Should().Contain(["Pier 7", "P-001", Harbor.UnitNamed("North Coast").Value.ToString(), Harbor.SeatOf(DemoPeople.Leo).Value.ToString(), Harbor.ProjectRoles[SampleCatalogue.CrewLead].Value.ToString()]);
        detail.EnumerateObject().Select(property => property.Name).Should().Equal(
            ["id", "version", "number", "name", "unitId", "state", "ownerSeat", "plannedFrom", "plannedUntil", "myRoleIds", "via", "crew", "can", "changedBy"], "a project answers exactly these");
        detail.GetProperty("changedBy").EnumerateObject().Select(property => property.Name).Should().Equal(["kind", "seatId"], "and who changed it last a kind and a seat's id, never a name");
        detail.GetProperty("myRoleIds").ValueKind.Should().Be(JsonValueKind.Array, "the caller's crew roles are a list of ids, empty for a caller who is not on the crew");
        detail.GetProperty("crew")[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            ["seatId", "isOwner", "startsAt", "endsAt", "appliesNow", "roles"], "a crew member exactly these");
        detail.GetProperty("crew")[0].GetProperty("roles")[0].EnumerateObject().Select(property => property.Name).Should().Equal(
            ["roleId", "startsAt", "endsAt", "appliesNow"], "and a role it holds exactly these");
        TextsOf(answers[2].Answer).Should().Contain(Harbor.SeatOf(DemoPeople.Juno).Value.ToString(), "an inspection names who recorded it by the seat's id");
    }

    [Fact]
    public async Task A_crew_its_roles_and_the_callers_own_come_in_one_order()
    {
        // A clock that stands still: everything here starts at one moment, so only the tie-break orders it, and the
        // order the database happens to return rows in would show. The host seeds the demonstration itself, so
        // the crew it starts with came on at that moment too.
        await using var host = await sample.StartAsync(services => services.AddSingleton<TimeProvider>(new StoppedClock()), seeded: false);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        var pier = Harbor.ProjectNamed("Pier 7");
        Guid Seat(DemoPerson person) => Harbor.SeatOf(person).Value;
        Guid Role(string pack) => Harbor.ProjectRoles[pack].Value;

        // Put on the crew in the reverse of the order of their ids, and given roles in the reverse of theirs.
        foreach (var person in new[] { DemoPeople.Tove, DemoPeople.Rhea })
        {
            using var added = await leo.PostAsJsonAsync($"/projects/{pier.Id.Value}/crew", new { seatId = Seat(person) }, Cancellation);
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        foreach (var pack in new[] { SampleCatalogue.Surveyor, SampleCatalogue.CrewLead })
        {
            using var given = await leo.GiveCrewRoleAsync(pier, Harbor.SeatOf(DemoPeople.Vic), Harbor.ProjectRoles[pack]);
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Read straight from the answer, in the order it has there: the owner first, then by when each came on the
        // crew, then by seat; a member's roles by when each was given, then by role; the caller's own roles by role.
        var detail = await vic.ProjectDetailAsync(pier);
        var crew = detail.GetProperty("crew").EnumerateArray().ToList();
        crew.Select(member => member.GetProperty("seatId").GetGuid()).Should().Equal(
            Seat(DemoPeople.Leo), Seat(DemoPeople.Rhea), Seat(DemoPeople.Juno), Seat(DemoPeople.Vic), Seat(DemoPeople.Tove));
        crew.Select(member => member.GetProperty("startsAt").GetDateTimeOffset()).Distinct().Should().ContainSingle("they all came on the crew at one moment");
        crew.Single(member => member.GetProperty("seatId").GetGuid() == Seat(DemoPeople.Vic))
            .GetProperty("roles").EnumerateArray().Select(held => held.GetProperty("roleId").GetGuid())
            .Should().Equal(Role(SampleCatalogue.CrewLead), Role(SampleCatalogue.Surveyor), Role(SampleCatalogue.Observer));
        detail.GetProperty("myRoleIds").EnumerateArray().Select(role => role.GetGuid())
            .Should().Equal(Role(SampleCatalogue.CrewLead), Role(SampleCatalogue.Surveyor), Role(SampleCatalogue.Observer));

        // The crew on its own, and a row of the list, read the same.
        (await vic.CrewAsync(pier)).GetRawText().Should().Be(detail.GetProperty("crew").GetRawText());
        (await vic.VisibleProjectsAsync()).Named("Pier 7").GetProperty("crew").GetRawText().Should().Be(detail.GetProperty("crew").GetRawText());
    }

    /// <summary>Every string value anywhere in <paramref name="element"/>.</summary>
    private static IEnumerable<string> TextsOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Array => element.EnumerateArray().SelectMany(TextsOf),
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => TextsOf(property.Value)),
        _ => [],
    };
}
