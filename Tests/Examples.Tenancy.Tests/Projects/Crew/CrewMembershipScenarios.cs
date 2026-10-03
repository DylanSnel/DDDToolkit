using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Crew;

/// <summary>
/// Being on a crew and holding powers on it are two things, each with its own dates. A seat on a project's crew
/// sees the project for as long as its membership runs. What it may do there comes from the roles it holds on the
/// crew, each for a period of its own, and only while the membership runs. The roles a crew holds are the tenant's
/// project roles, and no role of its organization.
/// </summary>
/// <remarks>
/// Every test changes a crew or a role, so each makes a host of its own. The ones in which time passes are in
/// <see cref="CrewMembershipOverTimeScenarios"/>: they give a period an end a few seconds ahead and wait for it.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class CrewMembershipScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    private static ProjectRoleId Surveyor => Harbor.ProjectRoles[SampleCatalogue.Surveyor];

    private static ProjectRoleId Observer => Harbor.ProjectRoles[SampleCatalogue.Observer];

    private static ProjectRoleId CrewLead => Harbor.ProjectRoles[SampleCatalogue.CrewLead];

    [Fact]
    public async Task A_member_without_a_role_sees_the_project()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", Harbor.Slug);

        // Tove's seat in harbor is in South Bay, and holds nothing at North Coast: the crew is her only way in.
        (await tove.VisibleProjectsAsync()).Names().Should().Equal("Bay bridge");
        using (var added = await leo.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/crew", new { seatId = Harbor.SeatOf(DemoPeople.Tove).Value }, Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // On the crew, with no role: she sees the project, through the crew, and may do nothing to it.
        var toves = await tove.VisibleProjectsAsync();
        toves.Names().Should().Equal("Pier 7", "Bay bridge");
        var pier = await tove.ProjectDetailAsync(PierSeven);
        pier.Text("via").Should().Be("crew");
        pier.GetProperty("myRoleIds").GetArrayLength().Should().Be(0);
        pier.GetProperty("can").EnumerateObject().Select(ability => ability.Value.GetBoolean()).Should().AllBeEquivalentTo(false);
        var member = (await tove.WithNamesAsync(pier)).Member("Tove");
        member.Roles.Should().BeEmpty();
        member.AppliesNow.Should().BeTrue();

        (await KeyHeldAsync(tove, ProjectKeys.View)).Should().Be((true, "crew"));
        foreach (var key in new[] { ProjectKeys.Edit, ProjectKeys.Close, ProjectKeys.ManageCrew, InspectionKeys.Record })
        {
            (await KeyHeldAsync(tove, key)).Should().Be((false, (string?)null), key);
        }

        // Inspections asks Projects' gate: she reads the project's inspections, and records none.
        var inspections = await tove.GetFromJsonAsync<JsonElement>($"/projects/{PierSeven.Id.Value}/inspections", Cancellation);
        inspections.GetProperty("canRecord").GetBoolean().Should().BeFalse();
        using var recorded = await tove.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Loose railing" }, Cancellation);
        (await recorded.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, InspectionRefusals.ProjectNotPermitted)).Argument("Key").Should().Be(InspectionKeys.Record);
    }

    [Fact]
    public async Task A_member_holds_the_keys_of_its_live_crew_roles_only()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        var vicSeat = Harbor.SeatOf(DemoPeople.Vic);

        // An observer looks. Given the surveyor's role next to it, Vic holds both, and records.
        (await KeyHeldAsync(vic, InspectionKeys.Record)).Allowed.Should().BeFalse();
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, vicSeat, Surveyor))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var pier = await vic.ProjectDetailAsync(PierSeven);
        pier.GetProperty("myRoleIds").EnumerateArray().Select(role => role.GetGuid()).Should().BeEquivalentTo([Observer.Value, Surveyor.Value]);
        (await KeyHeldAsync(vic, InspectionKeys.Record)).Should().Be((true, "crew"));
        (await KeyHeldAsync(vic, ProjectKeys.Close)).Allowed.Should().BeFalse("neither role closes a project");

        // Taken away again, the role's keys go with it; the observer's stay, and so does he.
        using (var taken = await leo.TakeCrewRoleAsync(PierSeven, vicSeat, Surveyor))
        {
            taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await KeyHeldAsync(vic, InspectionKeys.Record)).Allowed.Should().BeFalse();
        (await vic.WithNamesAsync(await vic.ProjectDetailAsync(PierSeven))).MyRole.Should().Be("Observer");

        // With his last role gone he is on the crew still, and still sees the project.
        using (var taken = await leo.TakeCrewRoleAsync(PierSeven, vicSeat, Observer))
        {
            taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await vic.VisibleProjectsAsync()).Names().Should().Equal("Pier 7");
        (await vic.WithNamesAsync(await vic.ProjectDetailAsync(PierSeven))).MyRole.Should().BeNull();

        // A role he does not hold cannot be taken, and somebody who is not on the crew holds none to take.
        using (var again = await leo.TakeCrewRoleAsync(PierSeven, vicSeat, Observer))
        {
            var refused = await again.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.CrewRoleNotFound);
            refused.Argument("Role").Should().Be(Observer.Value.ToString());
            refused.Argument("Seat").Should().Be(vicSeat.Value.ToString());
        }

        using var stranger = await leo.TakeCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Tove), Observer);
        await stranger.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.MemberNotFound);
    }

    [Fact]
    public async Task An_end_that_has_passed_is_refused_and_nothing_is_added()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);

        // A membership and a role each start now, so an end that is not after now is no period at all.
        using (var added = await leo.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/crew", new { seatId = Harbor.SeatOf(DemoPeople.Tove).Value, until = yesterday }, Cancellation))
        {
            await added.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.InvalidPeriod);
        }

        using (var given = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), Surveyor, until: yesterday))
        {
            await given.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.InvalidPeriod);
        }

        var crew = (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).Crew;
        crew.Select(member => member.Name).Should().Equal("Leo", "Juno", "Vic");
        crew.Single(member => member.Name == "Vic").Role.Should().Be("Observer");
    }

    [Fact]
    public async Task A_seat_holds_a_role_once_on_a_crew()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var juno = Harbor.SeatOf(DemoPeople.Juno);

        // Juno is the crew's surveyor. The same role again is refused, whatever end it is given.
        foreach (var until in new DateTimeOffset?[] { null, DateTimeOffset.UtcNow.AddDays(7) })
        {
            using var twice = await leo.GiveCrewRoleAsync(PierSeven, juno, Surveyor, until);
            var refused = await twice.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.CrewRoleHeld);
            refused.Argument("Seat").Should().Be(juno.Value.ToString());
            refused.Argument("Role").Should().Be(Surveyor.Value.ToString());
        }

        // Another role is given next to it, and the first can be given again once it was taken.
        using (var other = await leo.GiveCrewRoleAsync(PierSeven, juno, Observer))
        {
            other.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var taken = await leo.TakeCrewRoleAsync(PierSeven, juno, Surveyor))
        {
            taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var again = await leo.GiveCrewRoleAsync(PierSeven, juno, Surveyor))
        {
            again.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).Member("Juno").Role.Should().Be("Observer, Surveyor");

        // A role is given to someone on the crew: a seat that is not on it is put on it first.
        using var stranger = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Tove), Surveyor);
        (await stranger.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.MemberNotFound)).Argument("Seat").Should().Be(Harbor.SeatOf(DemoPeople.Tove).Value.ToString());
    }

    [Fact]
    public async Task Naming_another_owner_takes_only_the_old_owners_lead_role()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        var leoSeat = Harbor.SeatOf(DemoPeople.Leo);

        // Leo owns Pier 7 and leads its crew. He is its surveyor too, by his own hand.
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, leoSeat, Surveyor))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Rhea, who names owners in North, names Juno.
        using (var named = await rhea.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/owner", new { seatId = Harbor.SeatOf(DemoPeople.Juno).Value }, Cancellation))
        {
            named.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Juno leads now, next to the surveyor's role she had. Leo lost the lead role and nothing else: he is on
        // the crew still, as its surveyor, for as long as before.
        var pier = await rhea.WithNamesAsync(await rhea.ProjectDetailAsync(PierSeven));
        pier.Crew.Select(member => (member.Name, member.Role, member.IsOwner, member.AppliesNow))
            .Should().Equal(("Juno", "Crew lead, Surveyor", true, true), ("Leo", "Surveyor", false, true), ("Vic", "Observer", false, true));

        (await KeyHeldAsync(leo, InspectionKeys.Record)).Should().Be((true, "crew"));
        (await KeyHeldAsync(leo, ProjectKeys.ManageCrew)).Allowed.Should().BeFalse("managing the crew came with the lead role");
        (await KeyHeldAsync(juno, ProjectKeys.ManageCrew)).Should().Be((true, "crew"));

        // And now he is a member like any other: the new owner takes his role, or takes him off the crew.
        using (var taken = await juno.TakeCrewRoleAsync(PierSeven, leoSeat, Surveyor))
        {
            taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await leo.VisibleProjectsAsync()).Names().Should().Equal(["Pier 7"], "on the crew with no role, he still sees it");
    }

    [Fact]
    public async Task The_owners_lead_role_cannot_be_taken()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var leoSeat = Harbor.SeatOf(DemoPeople.Leo);

        // Not by himself, and not by the organization, which manages every crew in North.
        foreach (var asking in new[] { leo, rhea })
        {
            using var taken = await asking.TakeCrewRoleAsync(PierSeven, leoSeat, CrewLead);
            await taken.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.OwnerProtected);
        }

        // Nor when he holds another role next to it: the lead role is the owner's until another owner is named.
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, leoSeat, Surveyor))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var still = await leo.TakeCrewRoleAsync(PierSeven, leoSeat, CrewLead))
        {
            await still.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.OwnerProtected);
        }

        // The other role is his to give up, as anyone's is.
        using (var other = await leo.TakeCrewRoleAsync(PierSeven, leoSeat, Surveyor))
        {
            other.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var his = (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).Member("Leo");
        his.Role.Should().Be("Crew lead");
        his.IsOwner.Should().BeTrue();
    }

    [Theory]
    [InlineData(SampleCatalogue.AreaManager)]
    [InlineData(SampleCatalogue.AccessAdmin)]
    [InlineData(SampleCatalogue.PeopleOffice)]
    [InlineData(SampleCatalogue.CrewLead)]
    [InlineData(SampleCatalogue.Surveyor)]
    [InlineData(SampleCatalogue.Observer)]
    public async Task A_role_of_the_organization_cannot_go_on_a_crew(string pack)
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var role = Harbor.Roles[pack];

        // Not to somebody on the crew: a crew holds project roles, whatever pack a role of the organization was
        // copied from, its name included.
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), role.Value))
        {
            var refused = await given.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.RoleNotForMembers);
            refused.Argument("Role").Should().Be(role.Value.ToString());
            refused.Title.Should().Be("That role is not one of this tenant's project roles in use, so it cannot go on a crew.");
        }

        // Nor with somebody who is put on it: the seat is then not put on the crew either.
        using (var added = await leo.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/crew", new { seatId = Harbor.SeatOf(DemoPeople.Tove).Value, roleId = role.Value }, Cancellation))
        {
            await added.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.RoleNotForMembers);
        }

        (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).Crew.Select(member => (member.Name, member.Role))
            .Should().Equal(("Leo", "Crew lead"), ("Juno", "Surveyor"), ("Vic", "Observer"));
    }

    [Fact]
    public async Task A_role_that_is_not_the_tenants_or_is_archived_cannot_go_on_a_crew()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var vic = Harbor.SeatOf(DemoPeople.Vic);

        // Meadow's surveyor is a project role, and not one of harbor's; an id of nothing is no role at all.
        foreach (var role in new[] { DemoData.Meadow.ProjectRoles[SampleCatalogue.Surveyor], ProjectRoleId.CreateSequential() })
        {
            using var given = await leo.GiveCrewRoleAsync(PierSeven, vic, role);
            await given.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.RoleNotForMembers);
        }

        // Juno records on Pier 7 through the surveyor's role she holds on its crew, and through nothing else.
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        (await KeyHeldAsync(juno, InspectionKeys.Record)).Should().Be((true, "crew"), "the surveyor's role gives it");

        // An archived role is given nowhere. Whoever holds it on a crew keeps the grant, which now gives nothing,
        // and it can still be taken.
        using (var archived = await ada.PostAsync($"/project-roles/{Surveyor.Value}/archive", content: null, Cancellation))
        {
            archived.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var given = await leo.GiveCrewRoleAsync(PierSeven, vic, Surveyor))
        {
            await given.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.RoleNotForMembers);
        }

        (await KeyHeldAsync(juno, InspectionKeys.Record)).Allowed.Should().BeFalse("an archived role grants nothing");
        (await KeyHeldAsync(juno, ProjectKeys.View)).Should().Be((true, "crew"), "she is on the crew still");
        using var taken = await leo.TakeCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Juno), Surveyor);
        taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>Whether Pier 7 gives the client's caller <paramref name="key"/>, and through what, from <c>GET /access/projects/{id}?key=</c>.</summary>
    private static async Task<(bool Allowed, string? Via)> KeyHeldAsync(HttpClient client, string key)
    {
        var answer = await client.GetFromJsonAsync<JsonElement>($"/access/projects/{PierSeven.Id.Value}?key={key}", Cancellation);
        return (answer.GetProperty("allowed").GetBoolean(), answer.Text("via"));
    }
}
