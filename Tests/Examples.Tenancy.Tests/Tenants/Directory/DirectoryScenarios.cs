using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.Directory;

/// <summary>
/// The directory by id, over HTTP: what seats, units and roles are called, asked with the ids another answer
/// carried. Anyone seated in the tenant may ask, about anything of that tenant; another tenant's id is left out of
/// the answer, which never says so.
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
public sealed class DirectoryScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task A_seat_asks_the_names_of_seats_by_their_ids()
    {
        // Juno is a surveyor on one crew and holds no role in the organization: a name needs no key.
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        Guid[] ids = [Harbor.SeatOf(DemoPeople.Vic).Value, Harbor.SeatOf(DemoPeople.Seth).Value, Harbor.SeatOf(DemoPeople.Leo).Value, Harbor.SeatOf(DemoPeople.Vic).Value];

        var seats = await AskAsync(juno, "/tenancy/directory/seats", ids);

        seats.EnumerateArray().Select(seat => (seat.GetProperty("id").GetGuid(), seat.Text("displayName"), seat.Text("status"))).Should().Equal(
            (Harbor.SeatOf(DemoPeople.Leo).Value, "Leo", "active"),
            (Harbor.SeatOf(DemoPeople.Seth).Value, "Seth", "suspended"),
            (Harbor.SeatOf(DemoPeople.Vic).Value, "Vic", "active"));
    }

    [Fact]
    public async Task A_unit_is_answered_with_its_path_from_the_root()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        var units = await AskAsync(rhea, "/tenancy/directory/units", [Harbor.UnitNamed("North Coast").Value, Harbor.Root.Value]);

        units.EnumerateArray()
            .Select(unit => (unit.GetProperty("id").GetGuid(), unit.Text("name"), unit.Text("kind"), unit.Text("status"), unit.Text("path"), unit.GetProperty("depth").GetInt32()))
            .Should().Equal(
                (Harbor.Root.Value, "Harbor Works", "company", "active", "Harbor Works", 1),
                (Harbor.UnitNamed("North Coast").Value, "North Coast", "area", "active", "Harbor Works / North / North Coast", 3));
        units[1].GetProperty("parentId").GetGuid().Should().Be(Harbor.UnitNamed("North").Value);
        units[0].GetProperty("parentId").ValueKind.Should().Be(JsonValueKind.Null);
        units[0].EnumerateObject().Select(property => property.Name).Should().Equal("id", "parentId", "name", "kind", "status", "path", "depth");
    }

    [Fact]
    public async Task A_crew_member_reads_the_path_of_a_unit_it_is_not_placed_under()
    {
        // Vic is placed in North Inland, and observes Pier 7, which hangs at North Coast.
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);

        var listed = await vic.GetFromJsonAsync<JsonElement>("/tenancy/units", Cancellation);
        var pier = (await vic.VisibleProjectsAsync()).Named("Pier 7");
        var unit = pier.GetProperty("unitId").GetGuid();

        listed.EnumerateArray().Select(row => row.Text("path")).Should().Equal(["Harbor Works / North / North Inland"], "the list is the units he is placed under");
        unit.Should().Be(Harbor.UnitNamed("North Coast").Value);

        var named = await AskAsync(vic, "/tenancy/directory/units", [unit]);
        named.EnumerateArray().Should().ContainSingle().Which.Text("path").Should().Be("Harbor Works / North / North Coast");
        (await vic.WithNamesAsync(pier)).UnitPath.Should().Be("Harbor Works / North / North Coast");
    }

    [Fact]
    public async Task A_role_is_answered_with_its_name_and_whether_it_manages_access()
    {
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);

        var roles = await AskAsync(
            vic,
            "/tenancy/directory/roles",
            [Harbor.Roles[SampleCatalogue.Observer].Value, Harbor.Roles[SampleCatalogue.CrewLead].Value, Harbor.Roles[SampleCatalogue.AreaManager].Value]);

        roles.EnumerateArray()
            .Select(role => (role.GetProperty("id").GetGuid(), role.Text("name"), role.Text("fromPack"), role.Text("status"), role.GetProperty("managesAccess").GetBoolean()))
            .Should().Equal(
                (Harbor.Roles[SampleCatalogue.AreaManager].Value, "Area manager", SampleCatalogue.AreaManager, "active", true),
                (Harbor.Roles[SampleCatalogue.CrewLead].Value, "Crew lead", SampleCatalogue.CrewLead, "active", true),
                (Harbor.Roles[SampleCatalogue.Observer].Value, "Observer", SampleCatalogue.Observer, "active", false));
        roles[0].EnumerateObject().Select(property => property.Name).Should().Equal("id", "name", "fromPack", "status", "keys", "managesAccess");

        // What a role lets its holder do is for whoever manages the tenant's roles. Vic does not, and is answered
        // no keys at all, rather than an empty list, which would say the role brings none; Ada does.
        roles.EnumerateArray().Should().OnlyContain(role => role.GetProperty("keys").ValueKind == JsonValueKind.Null);

        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        var asAda = await AskAsync(ada, "/tenancy/directory/roles", [Harbor.Roles[SampleCatalogue.Observer].Value]);
        asAda[0].GetProperty("keys").EnumerateArray().Select(key => key.GetString()).Should().Equal(ProjectKeys.View);
    }

    [Fact]
    public async Task An_id_of_another_tenant_is_left_out_without_a_word()
    {
        // Tove has a seat in both tenants. Asked in harbor, meadow's ids are no ids of the tenant she works in, her
        // own seat there included, and an id of nothing is answered the same way: with nothing.
        using var inHarbor = await sample.ClientAsync("tove", Harbor.Slug);
        using var inMeadow = await sample.ClientAsync("tove", Meadow.Slug);
        var nothing = Guid.NewGuid();

        (await AskAsync(inHarbor, "/tenancy/directory/seats", [Meadow.SeatOf(DemoPeople.Tove).Value, nothing])).GetArrayLength().Should().Be(0);
        (await AskAsync(inHarbor, "/tenancy/directory/units", [Meadow.Root.Value, nothing])).GetArrayLength().Should().Be(0);
        (await AskAsync(inHarbor, "/tenancy/directory/roles", [Meadow.Roles[SampleCatalogue.TenantAdmin].Value, nothing])).GetArrayLength().Should().Be(0);

        // Among ids of her tenant they are simply not there.
        (await AskAsync(inHarbor, "/tenancy/directory/seats", [Meadow.SeatOf(DemoPeople.Tove).Value, Harbor.SeatOf(DemoPeople.Tove).Value]))
            .EnumerateArray().Select(seat => seat.GetProperty("id").GetGuid()).Should().Equal(Harbor.SeatOf(DemoPeople.Tove).Value);

        // The same ids, asked in meadow, are meadow's to answer, and harbor's are left out there.
        (await AskAsync(inMeadow, "/tenancy/directory/units", [Meadow.Root.Value, Harbor.Root.Value]))
            .EnumerateArray().Select(unit => unit.Text("path")).Should().Equal(Meadow.Name);
    }

    [Fact]
    public async Task More_ids_than_a_question_takes_are_refused()
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        var most = TenancyUseCases.TenancyDirectory.MostIds;
        var tooMany = Enumerable.Range(0, most + 1).Select(_ => Guid.NewGuid()).ToArray();

        foreach (var route in new[] { "/tenancy/directory/seats", "/tenancy/directory/units", "/tenancy/directory/roles" })
        {
            using var response = await juno.PostAsJsonAsync(route, new { ids = tooMany }, Cancellation);

            var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.TooManyIds);
            refused.Argument("Max").Should().Be(most.ToString(System.Globalization.CultureInfo.InvariantCulture));
            refused.Title.Should().Be("Ask for at most 200 ids at a time.");
        }

        // As many as a question takes are answered, and it is the different ids that count.
        Guid[] atTheLimit = [.. tooMany[..(most - 1)], Harbor.SeatOf(DemoPeople.Leo).Value, Harbor.SeatOf(DemoPeople.Leo).Value];
        (await AskAsync(juno, "/tenancy/directory/seats", atTheLimit)).EnumerateArray().Should().ContainSingle().Which.Text("displayName").Should().Be("Leo");
    }

    [Fact]
    public async Task The_directory_never_answers_an_identity()
    {
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        var everyone = Harbor.Seats.Select(seat => seat.Id.Value).Append(Harbor.Administrator.Id.Value).ToArray();

        using var response = await ada.PostAsJsonAsync("/tenancy/directory/seats", new { ids = everyone }, Cancellation);
        var raw = await response.Content.ReadAsStringAsync(Cancellation);
        var seats = JsonDocument.Parse(raw).RootElement;

        // Even the administrator is answered a name and a status, and nothing a person is found by.
        seats.GetArrayLength().Should().Be(everyone.Length);
        seats.EnumerateArray().Should().AllSatisfy(seat => seat.EnumerateObject().Select(property => property.Name).Should().Equal("id", "displayName", "status"));
        foreach (var person in DemoPeople.All)
        {
            raw.Should().NotContain(person.Id.ToString(), "{0}'s identity stays with Tenancy", person.Name)
                .And.NotContain(person.Email);
        }
    }

    /// <summary>What the directory answers <paramref name="client"/> about <paramref name="ids"/>: a 200, and a list.</summary>
    private static async Task<JsonElement> AskAsync(HttpClient client, string route, Guid[] ids)
    {
        using var response = await client.PostAsJsonAsync(route, new { ids }, Cancellation);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "{0} answers anyone seated in the tenant", route);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
    }
}
