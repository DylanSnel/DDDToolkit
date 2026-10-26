using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Projects.Ownership;

/// <summary>
/// Walks the owner's scenarios, one request each: what Leo, who owns Pier 7 and leads its crew and holds no role at
/// any unit, may do to Pier 7 and may not, and what Vic may once Leo has given him a heavy role on its crew: Site
/// manager, a project role the tenant made with every key a crew role can give. The owner may do everything that
/// acts on his project; opening projects, moving one to another unit, naming an owner and managing the
/// organization are the organization's, and no crew role reaches them.
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
public sealed class OwnerScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task Leo_renames_Pier_7()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        using (var renamed = await leo.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/name", new { name = "Pier 7 east" }, Cancellation))
        {
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // The crew sees the new name too: it is the project's, not Leo's view of it.
        (await leo.ProjectDetailAsync(PierSeven)).Text("name").Should().Be("Pier 7 east");
        (await juno.VisibleProjectsAsync()).Names().Should().Equal("Pier 7 east");
    }

    [Fact]
    public async Task Leo_puts_Tove_on_Pier_7s_crew_as_surveyor()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", Harbor.Slug);

        // Tove's seat in harbor is in South Bay, outside North: a crew takes any active seat of the tenant.
        using (var added = await leo.PostAsJsonAsync(
                   $"/projects/{PierSeven.Id.Value}/crew",
                   new { seatId = Harbor.SeatOf(DemoPeople.Tove).Value, roleId = Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value },
                   Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).Crew
            .Select(member => (member.Name, member.Role))
            .Should().Equal(("Leo", "Crew lead"), ("Juno", "Surveyor"), ("Tove", "Surveyor"), ("Vic", "Observer"));

        var toves = await tove.VisibleProjectsAsync();
        toves.Names().Should().Equal("Pier 7", "Bay bridge");
        (await tove.WithNamesAsync(toves.Named("Pier 7"))).MyRole.Should().Be("Surveyor");
        toves.Named("Pier 7").Text("via").Should().Be("crew");
    }

    [Fact]
    public async Task Leo_takes_Juno_off_Pier_7s_crew()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        using (var removed = await leo.DeleteAsync($"/projects/{PierSeven.Id.Value}/crew/{Harbor.SeatOf(DemoPeople.Juno).Value}", Cancellation))
        {
            removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).Crew.Select(member => member.Name)
            .Should().Equal("Leo", "Vic");

        // The crew was her only way in.
        (await juno.VisibleProjectsAsync()).Should().BeEmpty();
        using var opened = await juno.GetAsync($"/projects/{PierSeven.Id.Value}", Cancellation);
        await opened.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
    }

    [Fact]
    public async Task Leo_makes_Vic_a_co_lead_of_Pier_7()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);

        using (var made = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), Harbor.ProjectRoles[SampleCatalogue.CrewLead]))
        {
            made.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Two leads, one owner: Vic leads the crew as Leo does, next to the observer's role he had, and Leo stays
        // its owner.
        var pier = await vic.ProjectDetailAsync(PierSeven);
        var crew = (await vic.WithNamesAsync(pier)).Crew.Select(member => (member.Name, member.Role, member.IsOwner));
        crew.Should().Equal(("Leo", "Crew lead", true), ("Juno", "Surveyor", false), ("Vic", "Crew lead, Observer", false));
        Abilities(pier).Should().Be((true, false, true, false, true, false));
    }

    [Fact]
    public async Task Leo_gives_Vic_a_heavy_role_on_the_crew_and_Vic_holds_what_Leo_holds()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        var host = onPostgres.Host;
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await VicWithAHeavyCrewRoleAsync(onPostgres);

        // Positional: Leo gives a role the tenant made, not he, and on the crew it gives exactly the lead's, though
        // its row holds more.
        var his = await vic.ProjectDetailAsync(PierSeven);
        var leos = await leo.ProjectDetailAsync(PierSeven);
        (await vic.WithNamesAsync(his)).MyRole.Should().Be("Observer, Site manager");
        his.Text("via").Should().Be("crew");
        Abilities(his).Should().Be(Abilities(leos)).And.Be((true, false, true, false, true, false));

        var hisInspections = await vic.GetFromJsonAsync<JsonElement>($"/projects/{PierSeven.Id.Value}/inspections", Cancellation);
        hisInspections.GetProperty("canRecord").GetBoolean().Should().BeTrue("the lead may record, so the area manager on the crew may");

        // Key by key, over every key of the application's catalogue, of which the Site manager's role holds every
        // one a crew gives: Pier 7 answers Vic as it answers Leo, and both hold exactly the keys a crew gives.
        var catalogue = host.Services.GetRequiredService<TenancyCatalogue>();
        var held = new List<string>();
        foreach (var key in catalogue.LiveKeys)
        {
            var answer = await HoldsOnPierSevenAsync(vic, key);
            answer.Should().Be(await HoldsOnPierSevenAsync(leo, key), key);
            if (answer.Allowed)
            {
                held.Add(key);
            }
        }

        held.Should().Equal(catalogue.LiveKeys.Where(ProjectCatalogue.CrewGives));
    }

    [Fact]
    public async Task Leo_records_an_inspection_on_Pier_7()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        using (var recorded = await leo.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Loose railing on the east side" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Cancellation));
        }

        var list = await leo.GetFromJsonAsync<JsonElement>($"/projects/{PierSeven.Id.Value}/inspections", Cancellation);
        var inspection = list.GetProperty("items").EnumerateArray().Should().ContainSingle().Which;
        inspection.GetProperty("title").GetString().Should().Be("Loose railing on the east side");
        inspection.GetProperty("recordedBy").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Leo).Value);
    }

    [Fact]
    public async Task Leo_moving_Pier_7_to_South_Bay_is_not_permitted()
    {
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);

        using var moved = await leo.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/unit", new { unitId = Harbor.UnitNamed("South Bay").Value }, Cancellation);

        // He may edit Pier 7, but moving it asks for projects.open at the unit it would go to, through the organization.
        var refused = await moved.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
        refused.Argument("Key").Should().Be(ProjectKeys.Open);
        (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).UnitPath.Should().Be("Harbor Works / North / North Coast");
    }

    [Fact]
    public async Task Leo_opening_a_project_is_not_permitted()
    {
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);

        // Not even in North Coast, where his seat is placed: he holds no role at any unit.
        using var opened = await leo.PostAsJsonAsync(
            "/projects",
            new { number = "P-100", name = "Harbor wall", unitId = Harbor.UnitNamed("North Coast").Value },
            Cancellation);

        var refused = await opened.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
        refused.Argument("Key").Should().Be(ProjectKeys.Open);
        (await leo.VisibleProjectsAsync()).Names().Should().Equal("Pier 7");
    }

    [Fact]
    public async Task Leo_finds_neither_Inland_depot_nor_Bay_bridge()
    {
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);

        using var depot = await leo.GetAsync($"/projects/{Harbor.ProjectNamed("Inland depot").Id.Value}", Cancellation);
        using var bridge = await leo.GetAsync($"/projects/{Harbor.ProjectNamed("Bay bridge").Id.Value}", Cancellation);

        // Owning one project reaches that project and no other: not Inland depot, in North as Pier 7 is, nor Bay bridge.
        await depot.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        await bridge.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        (await leo.VisibleProjectsAsync()).Names().Should().Equal("Pier 7");
    }

    [Fact]
    public async Task Leo_making_Vic_area_manager_of_North_is_not_permitted()
    {
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);

        // A crew role he gives; an organization role, never: he holds tenancy.grants.manage at no unit.
        using var granted = await leo.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Vic).Value}/grants",
            new { unitId = Harbor.UnitNamed("North").Value, roleId = Harbor.Roles[SampleCatalogue.AreaManager].Value },
            Cancellation);

        var refused = await granted.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted);
        refused.Argument("Key").Should().Be(TenancyKeys.GrantsManage);
    }

    [Fact]
    public async Task Vic_with_a_heavy_crew_role_renames_Pier_7_changes_its_crew_and_closes_it()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        using var vic = await VicWithAHeavyCrewRoleAsync(onPostgres);
        var pier = PierSeven.Id.Value;

        using (var renamed = await vic.PutAsJsonAsync($"/projects/{pier}/name", new { name = "Pier 7 east" }, Cancellation))
        {
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var given = await vic.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Juno), Harbor.ProjectRoles[SampleCatalogue.Observer]))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var taken = await vic.TakeCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Juno), Harbor.ProjectRoles[SampleCatalogue.Surveyor]))
        {
            taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var added = await vic.PostAsJsonAsync(
                   $"/projects/{pier}/crew",
                   new { seatId = Harbor.SeatOf(DemoPeople.Tove).Value, roleId = Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value },
                   Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var closed = await vic.PostAsync($"/projects/{pier}/close", content: null, Cancellation))
        {
            closed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var project = await vic.ProjectDetailAsync(PierSeven);
        project.Text("name").Should().Be("Pier 7 east");
        project.Text("state").Should().Be("closed");
        (await vic.WithNamesAsync(project)).Crew
            .Select(member => (member.Name, member.Role))
            .Should().Equal(("Leo", "Crew lead"), ("Juno", "Observer"), ("Tove", "Surveyor"), ("Vic", "Observer, Site manager"));
    }

    [Fact]
    public async Task Vic_with_a_heavy_crew_role_neither_removes_the_owner_nor_takes_his_lead_role()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        using var vic = await VicWithAHeavyCrewRoleAsync(onPostgres);
        var leoSeat = Harbor.SeatOf(DemoPeople.Leo);

        using (var taken = await vic.TakeCrewRoleAsync(PierSeven, leoSeat, Harbor.ProjectRoles[SampleCatalogue.CrewLead]))
        {
            await taken.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.OwnerProtected);
        }

        using (var removed = await vic.DeleteAsync($"/projects/{PierSeven.Id.Value}/crew/{leoSeat.Value}", Cancellation))
        {
            await removed.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.OwnerProtected);
        }

        var leo = (await vic.WithNamesAsync(await vic.ProjectDetailAsync(PierSeven))).Member("Leo");
        leo.Role.Should().Be("Crew lead");
        leo.IsOwner.Should().BeTrue();
    }

    [Fact]
    public async Task Vic_with_a_heavy_crew_role_manages_no_seats_and_no_grants()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        using var vic = await VicWithAHeavyCrewRoleAsync(onPostgres);
        var northCoast = Harbor.UnitNamed("North Coast").Value;

        // A crew role gives none of Tenancy's keys, at Pier 7's unit or anywhere else.
        using (var placed = await vic.PostAsJsonAsync($"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Tove).Value}/placements", new { unitId = northCoast, primary = false }, Cancellation))
        {
            (await placed.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted)).Argument("Key").Should().Be(TenancyKeys.SeatsManage);
        }

        using (var granted = await vic.PostAsJsonAsync(
                   $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Juno).Value}/grants",
                   new { unitId = northCoast, roleId = Harbor.Roles[SampleCatalogue.Observer].Value },
                   Cancellation))
        {
            (await granted.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted)).Argument("Key").Should().Be(TenancyKeys.GrantsManage);
        }

        // Nor on Pier 7 itself, the one place his crew role counts: asked there, not one of Tenancy's keys is held.
        foreach (var key in TenancyKeys.Permissions.Select(permission => permission.Key))
        {
            (await HoldsOnPierSevenAsync(vic, key)).Should().Be((false, (string?)null), key);
        }
    }

    [Fact]
    public async Task Vic_with_a_heavy_crew_role_finds_neither_Bay_bridge_nor_Inland_depot()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        using var vic = await VicWithAHeavyCrewRoleAsync(onPostgres);

        using var bridge = await vic.GetAsync($"/projects/{Harbor.ProjectNamed("Bay bridge").Id.Value}", Cancellation);
        using var depot = await vic.GetAsync($"/projects/{Harbor.ProjectNamed("Inland depot").Id.Value}", Cancellation);

        // A role on a crew reaches only the crew's project.
        await bridge.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        await depot.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        (await vic.VisibleProjectsAsync()).Names().Should().Equal("Pier 7");
    }

    /// <summary>
    /// Ada makes the Site manager's project role, with every key a crew role can give, and Leo gives it to Vic on
    /// Pier 7's crew, next to the observer's role Vic has there; answers a client that calls as Vic.
    /// <para>
    /// The role's row is then given keys no crew gives as well, past the application, as a role manager's own
    /// statement could write them: naming an owner, opening projects, managing seats and grants. So whatever Vic is
    /// answered was cut to what a crew gives, by the questions and the functions, and is not merely all the row has.
    /// </para>
    /// </summary>
    private static async Task<HttpClient> VicWithAHeavyCrewRoleAsync(SampleOnPostgres onPostgres)
    {
        var host = onPostgres.Host;
        var everyCrewKey = host.Services.GetRequiredService<TenancyCatalogue>().LiveKeys.Where(ProjectCatalogue.CrewGives).ToArray();

        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var made = await ada.PostAsJsonAsync("/project-roles", new { name = "Site manager", keys = everyCrewKey }, Cancellation);
        made.StatusCode.Should().Be(HttpStatusCode.Created);
        var siteManager = new ProjectRoleId((await made.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid());

        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), siteManager))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        await onPostgres.WidenProjectRoleAsync(siteManager, Cancellation);
        return await host.ClientAsync("vic", Harbor.Slug);
    }

    /// <summary>Whether the client's caller holds <paramref name="key"/> on Pier 7, and through what, from <c>GET /access/projects/{id}?key=</c>.</summary>
    private static async Task<(bool Allowed, string? Via)> HoldsOnPierSevenAsync(HttpClient client, string key)
    {
        var answer = await client.GetFromJsonAsync<JsonElement>($"/access/projects/{PierSeven.Id.Value}?key={key}", Cancellation);
        return (answer.GetProperty("allowed").GetBoolean(), answer.Text("via"));
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
