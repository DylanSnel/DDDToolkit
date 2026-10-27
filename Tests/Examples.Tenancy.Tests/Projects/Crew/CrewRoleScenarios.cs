using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Projects.Crew;

/// <summary>
/// Whoever manages a project's crew gives and takes the roles its members hold there. A crew role is one of the
/// tenant's project roles, held on one project: Leo owns Pier 7 and holds no role on its crew, Juno is its surveyor
/// and Vic its observer, and none of them holds anything at a unit. The owner holds the permissions Projects declares
/// for an owner; naming another owner, and opening projects, are the organization's.
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
public sealed class CrewRoleScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    public static TheoryData<string, bool, bool, bool, bool, bool> CanFlags => new()
    {
        // The owner, through his crew lead's role: rename, close and manage the crew. Nowhere to move it to,
        // since he may open no project anywhere, and no owner changes, which only the organization makes.
        { "leo", true, false, true, true, false },

        // A surveyor and an observer on the crew may look, and nothing more.
        { "juno", false, false, false, false, false },
        { "vic", false, false, false, false, false },

        // The area manager, through her role at North: everything.
        { "rhea", true, true, true, true, true },
    };

    [Fact]
    public async Task Juno_sees_Pier_7_as_surveyor_via_crew()
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);

        var projects = await juno.VisibleProjectsAsync();

        // Placed in North Coast with no role there: the crew is her only way in.
        var pier = projects.Should().ContainSingle().Which;
        pier.Text("name").Should().Be("Pier 7");
        pier.Text("via").Should().Be("crew");
        // Her own role on the crew is hers to read; which roles the others hold is for whoever manages the crew.
        var named = await juno.WithNamesAsync(pier);
        named.MyRole.Should().Be("Surveyor");
        named.Crew.Select(member => (member.Name, member.RolesShown, member.IsOwner))
            .Should().Equal(("Leo", false, true), ("Juno", false, false), ("Vic", false, false));
    }

    /// <summary>
    /// One rule, held where a crew is read: who is on a crew is for whoever sees the project, and which roles each
    /// member holds there, and until when, for whoever holds <c>projects.crew.manage</c> on it. The routes and the
    /// GraphQL field answer alike, because neither decides it.
    /// </summary>
    [Theory]
    [InlineData("vic", false, "an observer on the crew sees who is on it")]
    [InlineData("juno", false, "and so does its surveyor")]
    [InlineData("leo", true, "its owner manages the crew")]
    [InlineData("rhea", true, "and so does the area manager, through her role at North")]
    public async Task A_crews_roles_are_answered_to_who_manages_the_crew_by_every_route_and_by_the_field(string person, bool manages, string because)
    {
        using var client = await sample.ClientAsync(person, Harbor.Slug);
        Guid[] everyone = [Harbor.SeatOf(DemoPeople.Leo).Value, Harbor.SeatOf(DemoPeople.Juno).Value, Harbor.SeatOf(DemoPeople.Vic).Value];
        Guid[] heldByEach = [Guid.Empty, Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value, Harbor.ProjectRoles[SampleCatalogue.Observer].Value];

        // The crew's own route, the project's, and the list of projects: three answers, one crew.
        JsonElement[] crews =
        [
            await client.CrewAsync(PierSeven),
            (await client.ProjectDetailAsync(PierSeven)).GetProperty("crew"),
            (await client.VisibleProjectsAsync()).Named("Pier 7").GetProperty("crew"),
        ];

        foreach (var crew in crews)
        {
            var members = crew.EnumerateArray().ToList();
            members.Select(member => member.GetProperty("seatId").GetGuid()).Should().Equal(everyone, "whoever sees the project reads who is on its crew");

            if (manages)
            {
                members.Select(member => member.GetProperty("roles").EnumerateArray().Select(held => held.GetProperty("roleId").GetGuid()).SingleOrDefault()).Should().Equal(heldByEach, because);
            }
            else
            {
                members.Should().OnlyContain(member => member.GetProperty("roles").ValueKind == JsonValueKind.Null, because);
                crew.GetRawText().Should().NotContain(Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value.ToString(), "no role of anybody's is in the answer");
            }
        }

        // The field says the same, and says why: the refusal a command would give for the permission.
        var answer = await client.GraphQLAsync("query($text: String) { projects(text: $text) { nodes { crew { seat { id } roles { role { id } } } } } }", new { text = "Pier 7" });
        var asked = answer.GetProperty("data").GetProperty("projects").GetProperty("nodes")[0].GetProperty("crew").EnumerateArray().ToList();
        asked.Select(member => member.GetProperty("seat").GetProperty("id").GetGuid()).Should().Equal(everyone);

        if (manages)
        {
            answer.TryGetProperty("errors", out _).Should().BeFalse("the gateway answered {0}", answer.GetRawText());
            asked.Select(member => member.GetProperty("roles").EnumerateArray().Select(held => held.GetProperty("role").GetProperty("id").GetGuid()).SingleOrDefault()).Should().Equal(heldByEach);
        }
        else
        {
            asked.Should().OnlyContain(member => member.GetProperty("roles").ValueKind == JsonValueKind.Null);
            answer.GetProperty("errors").EnumerateArray().Should().HaveCount(everyone.Length)
                .And.OnlyContain(error => error.Code() == ProjectRefusals.NotPermitted
                    && error.GetProperty("extensions").GetProperty("arguments").GetProperty("Permission").GetString() == ProjectPermissionNames.ManageCrew);
        }
    }

    [Fact]
    public async Task Juno_renaming_Pier_7_is_not_permitted()
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);

        using var response = await juno.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/name", new { name = "Pier 8" }, Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
        refused.Argument("Permission").Should().Be(ProjectPermissionNames.Edit);
    }

    [Fact]
    public async Task Juno_closing_Pier_7_is_not_permitted()
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);

        using var response = await juno.PostAsync($"/projects/{PierSeven.Id.Value}/close", content: null, Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
        refused.Argument("Permission").Should().Be(ProjectPermissionNames.Close);
    }

    [Fact]
    public async Task Juno_making_herself_crew_lead_is_not_permitted()
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);
        var junoSeat = Harbor.SeatOf(DemoPeople.Juno).Value;

        // Giving oneself a crew role is allowed to whoever manages the crew, and to nobody else.
        using var response = await juno.PostAsJsonAsync(
            $"/projects/{PierSeven.Id.Value}/crew/{junoSeat}/roles",
            new { roleId = Harbor.ProjectRoles[SampleCatalogue.CrewLead].Value },
            Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
        refused.Argument("Permission").Should().Be(ProjectPermissionNames.ManageCrew);
        (await juno.WithNamesAsync(await juno.ProjectDetailAsync(PierSeven))).MyRole.Should().Be("Surveyor");
    }

    [Fact]
    public async Task Vic_closing_Pier_7_is_not_permitted()
    {
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);

        using var response = await vic.PostAsync($"/projects/{PierSeven.Id.Value}/close", content: null, Cancellation);

        // He sees Pier 7 through the crew; his observer's role does not hold the permission.
        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
        refused.Argument("Permission").Should().Be(ProjectPermissionNames.Close);
    }

    [Fact]
    public async Task Leo_makes_vic_surveyor_on_Pier_7()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);

        // Positional: Leo manages the crew, so he gives any crew role, whether or not he holds its permissions. Vic holds
        // it next to the observer's role he had.
        using var response = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), Harbor.ProjectRoles[SampleCatalogue.Surveyor]);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await vic.WithNamesAsync((await vic.VisibleProjectsAsync()).Named("Pier 7"))).MyRole.Should().Be("Observer, Surveyor");
    }

    [Fact]
    public async Task Juno_giving_a_crew_role_is_not_permitted()
    {
        using var juno = await sample.ClientAsync("juno", Harbor.Slug);

        using var response = await juno.PostAsJsonAsync(
            $"/projects/{PierSeven.Id.Value}/crew",
            new { seatId = Harbor.SeatOf(DemoPeople.Vic).Value, roleId = Harbor.ProjectRoles[SampleCatalogue.CrewLead].Value },
            Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
        refused.Argument("Permission").Should().Be(ProjectPermissionNames.ManageCrew);
    }

    [Fact]
    public async Task The_crew_lead_holds_every_permission_a_crew_can_give()
    {
        var lead = SampleCatalogue.ProjectRoles.Single(role => role.Key == SampleCatalogue.CrewLead);
        var rules = SampleCatalogue.Projects.Rules;

        // Every permission of the application's catalogue, whichever module declared it, that a crew role gives.
        var crewPermissions = (await sample.SharedAsync()).Services.GetRequiredService<TenancyCatalogue>().LivePermissions.Where(rules.RolePermissions.Allows).ToList();
        crewPermissions.Should().NotContain([ProjectPermissionNames.Open, ProjectPermissionNames.ChangeOwner]).And.NotContain(TenancyPermissions.GrantsManage);

        // So the lead may do everything a crew role can give on the project, and no crew role gives more; an owner
        // holds part of it by owning the project.
        lead.Permissions.Should().BeEquivalentTo(crewPermissions);
        rules.OwnerPermissions.Should().BeSubsetOf(lead.Permissions, "an owner holds what a lead does on the project, but for what other modules ask");
    }

    [Fact]
    public async Task Only_the_organization_names_an_owner()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var juno = Harbor.SeatOf(DemoPeople.Juno).Value;

        // Leo owns Pier 7, and still names nobody: the permission is held at a unit or not at all.
        using (var fromTheCrew = await leo.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/owner", new { seatId = juno }, Cancellation))
        {
            (await fromTheCrew.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted)).Argument("Permission").Should().Be(ProjectPermissionNames.ChangeOwner);
        }

        // Rhea holds it at North, above the project.
        using (var fromTheUnit = await rhea.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/owner", new { seatId = juno }, Cancellation))
        {
            fromTheUnit.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await rhea.ProjectDetailAsync(PierSeven)).GetProperty("ownerSeat").GetGuid().Should().Be(juno);
    }

    [Fact]
    public async Task A_co_lead_cannot_unseat_the_owner()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        var leoSeat = Harbor.SeatOf(DemoPeople.Leo);
        var crewLead = Harbor.ProjectRoles[SampleCatalogue.CrewLead];

        using (var made = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), crewLead))
        {
            made.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Vic now holds everything Leo holds on the crew, and more, but is not its owner: the owner is given no role,
        // and stays on the crew until the organization names another.
        using (var given = await vic.GiveCrewRoleAsync(PierSeven, leoSeat, Harbor.ProjectRoles[SampleCatalogue.Observer]))
        {
            await given.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.OwnerProtected);
        }

        using (var removed = await vic.DeleteAsync($"/projects/{PierSeven.Id.Value}/crew/{leoSeat.Value}", Cancellation))
        {
            await removed.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.OwnerProtected);
        }

        var pier = await vic.WithNamesAsync(await vic.ProjectDetailAsync(PierSeven));
        pier.Member("Leo").Role.Should().BeNull();
        pier.Member("Leo").IsOwner.Should().BeTrue();
    }

    [Fact]
    public async Task A_co_lead_gives_and_takes_their_own_crew_roles()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        var vicSeat = Harbor.SeatOf(DemoPeople.Vic);

        using (var made = await leo.GiveCrewRoleAsync(PierSeven, vicSeat, Harbor.ProjectRoles[SampleCatalogue.CrewLead]))
        {
            made.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Giving a crew role is positional, and that includes one's own: Vic, who is not the owner, gives himself
        // the surveyor's role and steps down as a lead.
        using (var own = await vic.GiveCrewRoleAsync(PierSeven, vicSeat, Harbor.ProjectRoles[SampleCatalogue.Surveyor]))
        {
            own.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var down = await vic.TakeCrewRoleAsync(PierSeven, vicSeat, Harbor.ProjectRoles[SampleCatalogue.CrewLead]))
        {
            down.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var pier = await vic.ProjectDetailAsync(PierSeven);
        (await vic.WithNamesAsync(pier)).MyRole.Should().Be("Observer, Surveyor");
        pier.GetProperty("can").GetProperty("manageCrew").GetBoolean().Should().BeFalse("neither a surveyor nor an observer manages the crew");
    }

    [Fact]
    public async Task A_co_lead_takes_themself_off_the_crew()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        var vicSeat = Harbor.SeatOf(DemoPeople.Vic);

        using (var made = await leo.GiveCrewRoleAsync(PierSeven, vicSeat, Harbor.ProjectRoles[SampleCatalogue.CrewLead]))
        {
            made.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Whoever manages a crew takes anyone off it, themself included. With Vic go the roles he held there, the
        // one that let him do this among them: the change is his to make, and what it leaves him is nothing.
        using (var left = await vic.DeleteAsync($"/projects/{PierSeven.Id.Value}/crew/{vicSeat.Value}", Cancellation))
        {
            left.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await vic.VisibleProjectsAsync()).Should().BeEmpty("the crew was his only way to Pier 7");
        (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).Crew.Select(member => member.Name).Should().Equal("Leo", "Juno");
    }

    /// <summary>
    /// A seat that gives up a role of its own is saved as the application's own work, which no row rule judges,
    /// so that save writes the one project it changed. With a change to another aggregate in the same unit of
    /// work, left by an earlier command of the scope, it refuses, and writes neither.
    /// </summary>
    [Fact]
    public async Task The_save_of_a_seats_own_place_refuses_when_the_unit_of_work_holds_another_change()
    {
        var bridge = Harbor.ProjectNamed("Bay bridge");
        var (juno, lead, surveyor) = (Harbor.SeatOf(DemoPeople.Juno), Harbor.ProjectRoles[SampleCatalogue.CrewLead], Harbor.ProjectRoles[SampleCatalogue.Surveyor]);

        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        // Juno manages Pier 7's crew from here on, so she may take the surveyor's role she holds there herself.
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, juno, lead))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProjectsContext>();

            // What an earlier command of this scope would have left behind: a project she may not even see, renamed
            // and never saved. The policies show it to no statement of hers, so it is loaded as harbor's own work,
            // which ends before she is the caller.
            Project leftover;
            using (SampleAccess.Manager.BeginSystemIn(Harbor.Id, Harbor.Administrator.Id))
            {
                leftover = await db.Projects.AsTracking().SingleAsync(project => project.Id == bridge.Id, Cancellation);
            }

            leftover.Rename("Not hers to rename");

            using (SampleCallers.BeginSeatOf(DemoPeople.Juno, Harbor))
            {
                var taking = async () => await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TakeCrewRole(PierSeven.Id, juno, surveyor), Cancellation);

                (await taking.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(nameof(Project));
            }
        }

        // Nothing was written: not the leftover, and not her own change either.
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        (await ada.ProjectDetailAsync(bridge)).GetProperty("name").GetString().Should().Be("Bay bridge");
        (await ada.CrewAsync(PierSeven)).EnumerateArray().Single(member => member.GetProperty("seatId").GetGuid() == juno.Value)
            .GetProperty("roles").EnumerateArray().Select(held => held.GetProperty("roleId").GetGuid())
            .Should().BeEquivalentTo([lead.Value, surveyor.Value]);

        // In a scope of her own the same command goes through, as it always did.
        using var hers = await host.ClientAsync("juno", Harbor.Slug);
        using var taken = await hers.TakeCrewRoleAsync(PierSeven, juno, surveyor);
        taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Whoever_manages_the_crew_may_put_themself_on_it()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);

        // Rhea manages Pier 7's crew through her role at North, and puts herself on it.
        using (var added = await rhea.PostAsJsonAsync(
                   $"/projects/{PierSeven.Id.Value}/crew",
                   new { seatId = Harbor.SeatOf(DemoPeople.Rhea).Value, roleId = Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value },
                   Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var pier = (await rhea.VisibleProjectsAsync()).Named("Pier 7");
        (await rhea.WithNamesAsync(pier)).MyRole.Should().Be("Surveyor");
        pier.Text("via").Should().Be("crew", "the crew comes first when both reach the project");
    }

    [Fact]
    public async Task A_crew_place_one_gives_oneself_outlasts_the_grant_that_allowed_it()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        var junoSeat = Harbor.SeatOf(DemoPeople.Juno).Value;
        var depot = Harbor.ProjectNamed("Inland depot");
        var north = Harbor.UnitNamed("North").Value;
        var until = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(7), TimeSpan.Zero);

        // Juno manages North's crews for a week, through an area manager's grant that ends then.
        using (var placed = await ada.PostAsJsonAsync($"/tenancy/seats/{junoSeat}/placements", new { unitId = north, primary = false }, Cancellation))
        {
            placed.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var granted = await ada.PostAsJsonAsync(
                   $"/tenancy/seats/{junoSeat}/grants",
                   new { unitId = north, roleId = Harbor.Roles[SampleCatalogue.AreaManager].Value, until },
                   Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Giving a crew role is positional, and a membership ends when whoever gives it says, not when their own
        // grant does: she makes herself Inland depot's lead with no end, and leads it after the week is over.
        using (var added = await juno.PostAsJsonAsync(
                   $"/projects/{depot.Id.Value}/crew",
                   new { seatId = junoSeat, roleId = Harbor.ProjectRoles[SampleCatalogue.CrewLead].Value },
                   Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var herself = (await juno.ProjectDetailAsync(depot)).GetProperty("crew").EnumerateArray()
            .Single(member => member.GetProperty("seatId").GetGuid() == junoSeat);
        herself.GetProperty("endsAt").ValueKind.Should().Be(JsonValueKind.Null);
        var held = herself.GetProperty("roles").EnumerateArray().Should().ContainSingle().Which;
        held.GetProperty("roleId").GetGuid().Should().Be(Harbor.ProjectRoles[SampleCatalogue.CrewLead].Value);
        held.GetProperty("endsAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_owner_holds_what_an_owner_does_and_no_project_role_gives_more()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        var host = onPostgres.Host;
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);

        // A project role holds the permissions that act on a project and nothing more, so none gives more than the crew
        // lead's: one with the organization's permissions, naming owners, opening projects or managing seats, is not made.
        using (var beyond = await ada.PostAsJsonAsync(
                   "/project-roles",
                   new { name = "Area lead", permissions = new[] { ProjectPermissionNames.Edit, ProjectPermissionNames.ChangeOwner, ProjectPermissionNames.Open, TenancyPermissions.GrantsManage } },
                   Cancellation))
        {
            var refused = await beyond.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, ProjectRefusals.PermissionNotForMembers);
            refused.Argument("Permissions").Should().Contain(ProjectPermissionNames.ChangeOwner).And.Contain(ProjectPermissionNames.Open).And.Contain(TenancyPermissions.GrantsManage).And.NotContain(ProjectPermissionNames.Edit);
        }

        // Nor does a row that holds them give them: the crew lead's row is given those permissions past the application,
        // as a role manager's own statement could, and Leo gives it to Vic. What follows is cut to what a crew gives
        // all the same.
        await onPostgres.WidenProjectRoleAsync(Harbor.ProjectRoles[SampleCatalogue.CrewLead], Cancellation);
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), Harbor.ProjectRoles[SampleCatalogue.CrewLead]))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Leo, the owner, holding no role, and Vic, the lead, do everything on the project, and no owner changes and
        // no moving.
        foreach (var (client, role) in new[] { (leo, (string?)null), (vic, "Crew lead, Observer") })
        {
            var pier = await client.ProjectDetailAsync(PierSeven);
            (await client.WithNamesAsync(pier)).MyRole.Should().Be(role);
            var can = pier.GetProperty("can");
            (
                Rename: can.GetProperty("rename").GetBoolean(),
                Move: can.GetProperty("move").GetBoolean(),
                Close: can.GetProperty("close").GetBoolean(),
                ManageCrew: can.GetProperty("manageCrew").GetBoolean(),
                ChangeOwner: can.GetProperty("changeOwner").GetBoolean())
                .Should().Be((true, false, true, true, false));

            using (var named = await client.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/owner", new { seatId = Harbor.SeatOf(DemoPeople.Juno).Value }, Cancellation))
            {
                (await named.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted)).Argument("Permission").Should().Be(ProjectPermissionNames.ChangeOwner);
            }

            // Nor any of Tenancy's permissions, which no crew gives.
            var grants = await client.GetFromJsonAsync<JsonElement>($"/access/projects/{PierSeven.Id.Value}?permission={TenancyPermissions.GrantsManage}", Cancellation);
            grants.GetProperty("allowed").GetBoolean().Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_crew_role_reaches_no_further_than_the_lead()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        var host = onPostgres.Host;
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        var pier = PierSeven.Id.Value;
        var vicSeat = Harbor.SeatOf(DemoPeople.Vic);

        // The heaviest role a crew holds is the crew lead's. Positional: Leo manages the crew, so he gives it.
        using (var made = await leo.GiveCrewRoleAsync(PierSeven, vicSeat, Harbor.ProjectRoles[SampleCatalogue.CrewLead]))
        {
            made.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // On the crew the role gives the permissions that act on the project.
        var held = await vic.GetFromJsonAsync<JsonElement>($"/access/projects/{pier}?permission={ProjectPermissionNames.Close}", Cancellation);
        held.GetProperty("allowed").GetBoolean().Should().BeTrue();
        held.Text("via").Should().Be("crew");

        // And nothing that is the organization's: naming an owner, somebody else or himself, or managing the
        // organization itself. Not even once the role's row holds those permissions, written past the application as a
        // role manager's own statement could: a crew role gives what a crew gives, whatever its row says.
        await onPostgres.WidenProjectRoleAsync(Harbor.ProjectRoles[SampleCatalogue.CrewLead], Cancellation);
        foreach (var permission in new[] { ProjectPermissionNames.ChangeOwner, ProjectPermissionNames.Open, TenancyPermissions.GrantsManage, TenancyPermissions.SeatsManage })
        {
            var organization = await vic.GetFromJsonAsync<JsonElement>($"/access/projects/{pier}?permission={permission}", Cancellation);
            organization.GetProperty("allowed").GetBoolean().Should().BeFalse(permission);
        }

        foreach (var seat in new[] { Harbor.SeatOf(DemoPeople.Juno).Value, vicSeat.Value })
        {
            using var named = await vic.PutAsJsonAsync($"/projects/{pier}/owner", new { seatId = seat }, Cancellation);
            var refused = await named.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
            refused.Argument("Permission").Should().Be(ProjectPermissionNames.ChangeOwner);
        }

        // Nor moving it: he edits the project, and moving it takes projects.open at the unit it would go to as
        // well, where he holds nothing.
        using var moved = await vic.PutAsJsonAsync($"/projects/{pier}/unit", new { unitId = Harbor.UnitNamed("North Inland").Value }, Cancellation);
        (await moved.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted)).Argument("Permission").Should().Be(ProjectPermissionNames.Open);
    }

    [Fact]
    public async Task A_project_role_made_by_hand_goes_on_a_crew_and_gives_its_permissions_there()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", Harbor.Slug);
        var pier = PierSeven.Id.Value;

        // A project role of the tenant's own, which records inspections and changes nothing of the project.
        using var created = await ada.PostAsJsonAsync(
            "/project-roles",
            new { name = "Site keeper", description = "Keeps the site tidy", permissions = new[] { ProjectPermissionNames.View, InspectionPermissionNames.Record } },
            Cancellation);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var keeper = (await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();

        using (var added = await leo.PostAsJsonAsync($"/projects/{pier}/crew", new { seatId = Harbor.SeatOf(DemoPeople.Tove).Value, roleId = keeper }, Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // On the crew she sees the project, as every member does, and records on it; nothing else.
        var named = await tove.WithNamesAsync(await tove.ProjectDetailAsync(PierSeven));
        named.MyRole.Should().Be("Site keeper");
        named.Via.Should().Be("crew");
        var recording = await tove.GetFromJsonAsync<JsonElement>($"/access/projects/{pier}?permission={InspectionPermissionNames.Record}", Cancellation);
        recording.Text("via").Should().Be("crew");
        foreach (var permission in new[] { ProjectPermissionNames.Edit, ProjectPermissionNames.Close, ProjectPermissionNames.ManageCrew })
        {
            var held = await tove.GetFromJsonAsync<JsonElement>($"/access/projects/{pier}?permission={permission}", Cancellation);
            held.GetProperty("allowed").GetBoolean().Should().BeFalse(permission);
        }
    }

    [Fact]
    public async Task The_owner_cannot_be_removed_until_another_is_named()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        var leoSeat = Harbor.SeatOf(DemoPeople.Leo).Value;

        using (var himself = await leo.DeleteAsync($"/projects/{PierSeven.Id.Value}/crew/{leoSeat}", Cancellation))
        {
            await himself.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.OwnerProtected);
        }

        // Rhea may name owners in her area. Juno becomes the owner, and the surveyor's role she had goes; Leo stays
        // on the crew as an observer, as Rhea says.
        using (var named = await rhea.PutAsJsonAsync(
                   $"/projects/{PierSeven.Id.Value}/owner",
                   new { seatId = Harbor.SeatOf(DemoPeople.Juno).Value, formerOwnerRoleId = Harbor.ProjectRoles[SampleCatalogue.Observer].Value },
                   Cancellation))
        {
            named.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var pier = await rhea.WithNamesAsync(await rhea.ProjectDetailAsync(PierSeven));
        pier.Member("Juno").IsOwner.Should().BeTrue();
        pier.Member("Juno").Role.Should().BeNull();
        pier.Member("Leo").Role.Should().Be("Observer");

        // Now the owner, Juno may take Leo off the crew.
        using (var removed = await juno.DeleteAsync($"/projects/{PierSeven.Id.Value}/crew/{leoSeat}", Cancellation))
        {
            removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await rhea.WithNamesAsync(await rhea.ProjectDetailAsync(PierSeven))).Crew.Select(member => member.Name)
            .Should().Equal("Juno", "Vic");
    }

    [Fact]
    public async Task A_project_opens_in_a_tenant_without_project_roles_with_its_owner_on_the_crew()
    {
        await using var host = await sample.StartAsync();

        // A tenant made while the application runs, and not set up for projects: it has no project role yet, and a
        // project needs none, since its owner holds what an owner does by owning it.
        var quarry = await SampleTenants.ProvisionAsync(host, "quarry", "Quarry");

        var opened = await SampleTenants.SendAsSystemAsync(host, quarry.Tenant, new OpenProject("P-100", "Harbor wall", quarry.RootUnit, quarry.AdminSeat));

        var crew = await SampleTenants.AskAsSystemAsync(host, quarry.Tenant, new AllCrewMembers(opened));
        crew.Should().ContainSingle().Which.Roles.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Crew_periods_are_compared_in_sql()
    {
        var counter = new CommandCounter();
        await using var host = await sample.StartAsync(services => services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(counter)));
        var depot = Harbor.ProjectNamed("Inland depot");
        var surveyor = Harbor.ProjectRoles[SampleCatalogue.Surveyor];
        var now = DateTimeOffset.UtcNow;

        // Written as the projects' system work in harbor, since no command puts a membership that ended already on
        // a crew: Juno's ended yesterday, Leo's ends tomorrow.
        using (SampleAccess.Manager.BeginSystemIn(Harbor.Id, Harbor.Administrator.Id, ProjectMembership.Scope))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var projects = scope.ServiceProvider.GetRequiredService<ProjectsContext>();
            var project = await projects.Projects.SingleAsync(candidate => candidate.Id == depot.Id, Cancellation);
            var then = now.AddDays(-30);
            project.AddToCrew(Harbor.SeatOf(DemoPeople.Juno), MemberPeriod.Between(then, now.AddDays(-1)), then, addedBy: null);
            project.GiveCrewRole(Harbor.SeatOf(DemoPeople.Juno), surveyor, MemberPeriod.Open(then), then, givenBy: null);
            project.AddToCrew(Harbor.SeatOf(DemoPeople.Leo), MemberPeriod.Between(then, now.AddDays(1)), then, addedBy: null);
            project.GiveCrewRole(Harbor.SeatOf(DemoPeople.Leo), surveyor, MemberPeriod.Open(then), then, givenBy: null);
            await projects.SaveChangesAsync(Cancellation);
        }

        // The period is part of the statement that lists what a seat may see, compared where the statement runs,
        // not a filter applied to rows already read. The statement reads both columns for the crew it answers
        // with as well, and compares the periods of the seat's rights in the organization the same way, so what is
        // looked for is the comparison of a crew member's own columns: the alias the crew's table has where the
        // seat's membership is looked up. A provider quotes an alias or leaves it bare, and names the table's
        // schema or has none, so the pattern takes both. Asked as a request asks: as the person, in her seat.
        using (TestUser.BeginAs(DemoPeople.Juno.Id))
        using (TenancyCallers.Begin(TenancyCaller<TenantId, SeatId>.InSeat(Harbor.Id, Harbor.SeatOf(DemoPeople.Juno))))
        {
            await using var scope = host.Services.CreateAsyncScope();
            counter.WatchThisFlow();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new VisibleProjects(), Cancellation);

            var sql = counter.Commands[0];
            sql.Should().MatchRegex(
                """FROM (?:\w+\.)?"ProjectCrewMembers" AS "?(?<crew>\w+)"?[\s\S]*?"?\k<crew>"?\."StartsAt" <= @\w+ AND \("?\k<crew>"?\."EndsAt" IS NULL OR "?\k<crew>"?\."EndsAt" > @\w+\)""");
        }

        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        (await juno.VisibleProjectsAsync()).Names().Should().Equal("Pier 7");
        using (var ended = await juno.GetAsync($"/projects/{depot.Id.Value}", Cancellation))
        {
            await ended.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        }

        var leos = await leo.VisibleProjectsAsync();
        leos.Names().Should().Equal("Pier 7", "Inland depot");
        (await leo.WithNamesAsync(leos.Named("Inland depot"))).MyRole.Should().Be("Surveyor");
    }

    [Theory]
    [MemberData(nameof(CanFlags))]
    public async Task Project_detail_can_flags_match_the_callers_rights(string person, bool rename, bool move, bool close, bool manageCrew, bool changeOwner)
    {
        using var client = await sample.ClientAsync(person, Harbor.Slug);

        var can = (await client.ProjectDetailAsync(PierSeven)).GetProperty("can");

        (
            Rename: can.GetProperty("rename").GetBoolean(),
            Move: can.GetProperty("move").GetBoolean(),
            Close: can.GetProperty("close").GetBoolean(),
            ManageCrew: can.GetProperty("manageCrew").GetBoolean(),
            ChangeOwner: can.GetProperty("changeOwner").GetBoolean())
            .Should().Be((rename, move, close, manageCrew, changeOwner));
        can.GetProperty("reopen").GetBoolean().Should().BeFalse("Pier 7 is open");
    }
}
