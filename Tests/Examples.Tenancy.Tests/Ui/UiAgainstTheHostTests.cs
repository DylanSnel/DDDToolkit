using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Examples.Tenancy.Ui.Api;
using Examples.Tenancy.Ui.Api.TryIt;
using Examples.Tenancy.Ui.Api.Wire;
using Examples.Tenancy.Ui.Auth;
using Examples.Tenancy.Ui.Session;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Ui;

/// <summary>
/// The UI's own client against the real host: its wire records read what the API writes, its sign-in picks the
/// tenant the login page promises, and the try-it page's presets and actions reach the routes they name and are
/// answered as they expect.
/// </summary>
/// <remarks>
/// Nothing here changes data but the one test that plans and records, which has a host of its own: rhea only
/// reads, and every preset and every free-form action runs as someone the API refuses. So the rest share one host.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class UiAgainstTheHostTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    public static TheoryData<string> Presets => new(DevAttempts.All.Select(attempt => attempt.Id));

    public static TheoryData<string> Actions => new(TryItActions.All.Select(action => action.Text));

    [Fact]
    public async Task Every_screen_reads_what_the_api_answers()
    {
        var (api, session) = await SignInAsync("rhea");
        session.Tenant.Should().Be(Harbor.Slug);
        var pierSeven = Harbor.ProjectNamed("Pier 7").Id.Value.ToString();

        var me = (await api.WhoAmIAsync(Cancellation)).Value!;
        me.Tenant.Should().Be(new TenantInfo(Harbor.Id.Value, "harbor", "Harbor Works", "hierarchical", "active"));
        me.Seat.DisplayName.Should().Be("Rhea");
        me.Placements.Should().ContainSingle().Which.Unit.Path.Should().Be("Harbor Works / North");
        me.Placements[0].Grants.Should().ContainSingle().Which.Should().Match<GrantInfo>(grant => grant.Role == "Area manager" && grant.AppliesNow && grant.EndsAt == null);
        me.Roles.Should().ContainSingle().Which.FromPack.Should().Be(SampleCatalogue.AreaManager);
        me.Keys.Single(key => key.Key == ProjectKeys.View).Reaches.Select(unit => unit.Path)
            .Should().BeEquivalentTo(new[] { "Harbor Works / North", "Harbor Works / North / North Coast", "Harbor Works / North / North Inland" });

        var projects = (await api.VisibleProjectsAsync(cancellationToken: Cancellation)).Value!.Items;
        projects.Select(project => project.Name).Should().BeEquivalentTo(new[] { "Pier 7", "Inland depot" });
        var listed = projects.Single(project => project.Name == "Pier 7");
        listed.Via.Should().Be("organization");
        listed.Can.Should().Be(new ProjectAbilitiesInfo(Rename: true, Plan: true, Move: true, Close: true, Reopen: false, ManageCrew: true, ChangeOwner: true), "a row of the list says what may be done to it, as the project's own answer does");
        (await api.VisibleProjectsAsync(state: "closed", cancellationToken: Cancellation)).Value!.Items.Should().BeEmpty("the filter the page sends is one the API reads");
        (await api.VisibleProjectsAsync(text: "pier", cancellationToken: Cancellation)).Value!.Items.Select(project => project.Name).Should().Equal("Pier 7");

        // The list names its units, seats and project roles by id; the page asks the directory and the project
        // roles what they are called, and the answer on screen stays the list's.
        var names = new DirectoryNames(api);
        await names.EnsureAsync(
            seats: projects.SelectMany(project => project.Crew.Select(member => member.SeatId)),
            units: projects.Select(project => project.UnitId),
            projectRoles: projects.SelectMany(project => project.RoleIds),
            cancellationToken: Cancellation);
        session.LastAnswer!.Path.Should().StartWith("/projects", "the directory's answers are not the page's last answer");
        names.CrewByName(listed.Crew).Select(member => (names.OfSeat(member.SeatId), names.OfProjectRoles(member.RolesHeld.Select(held => held.RoleId)), member.IsOwner))
            .Should().Equal(("Leo", "Crew lead", true), ("Juno", "Surveyor", false), ("Vic", "Observer", false));
        listed.Crew.SelectMany(member => member.RolesHeld).Should().OnlyContain(held => held.AppliesNow && held.EndsAt == null, "every seeded crew role is held for good");
        names.OfUnit(listed.UnitId).Should().Be("Harbor Works / North / North Coast");
        names.OfProjectRoles(listed.MyRoles).Should().BeNull("she is not on its crew");
        names.OfProjectRoles(projects.Single(project => project.Name == "Inland depot").MyRoles).Should().Be("Crew lead");

        var detail = (await api.ProjectDetailAsync(pierSeven, Cancellation)).Value!;
        detail.UnitId.Should().Be(Harbor.UnitNamed("North Coast").Value);
        names.OfUnit(detail.UnitId).Should().Be("Harbor Works / North / North Coast");
        detail.Can.Should().Be(new ProjectAbilitiesInfo(Rename: true, Plan: true, Move: true, Close: true, Reopen: false, ManageCrew: true, ChangeOwner: true));

        var inspections = (await api.InspectionsAsync(pierSeven, cancellationToken: Cancellation)).Value!;
        inspections.CanRecord.Should().BeTrue();

        (await api.TenantSeatsAsync(Cancellation)).Value!.Should().Contain(seat => seat.DisplayName == "Seth" && !seat.IsActive);
        (await api.TenantUnitsAsync(Cancellation)).Value!.Select(unit => unit.Path).Should().Contain("Harbor Works / North / North Inland");
        var roles = (await api.TenantRolesAsync(Cancellation)).Value!;
        roles.Should().Contain(role => role.Name == "Crew lead" && role.IsActive);
        var projectRoles = (await api.ProjectRolesAsync(Cancellation)).Value!;
        projectRoles.Where(role => role.IsActive).Select(role => role.MadeFrom)
            .Should().BeEquivalentTo(new[] { SampleCatalogue.CrewLead, SampleCatalogue.Surveyor, SampleCatalogue.Observer }, "the crew's role pickers offer the tenant's project roles in use");
        projectRoles.Should().OnlyContain(role => role.Keys == null, "she manages no roles, so what each gives is not hers to read");

        // The directory by id reads into the same records the lists do.
        (await api.SeatsByIdAsync([Harbor.SeatOf(DemoPeople.Seth).Value], Cancellation)).Value!.Should().Equal(new SeatInfo(Harbor.SeatOf(DemoPeople.Seth).Value, "Seth", "suspended"));
        (await api.UnitsByIdAsync([Harbor.UnitNamed("South Bay").Value], Cancellation)).Value!.Should().ContainSingle()
            .Which.Should().Match<UnitInfo>(unit => unit.Path == "Harbor Works / South / South Bay" && unit.Name == "South Bay" && unit.Depth == 3, "she is not placed under South, and reads what it is called");
        (await api.RolesByIdAsync([Harbor.Roles[SampleCatalogue.Observer].Value], Cancellation)).Value!.Should().ContainSingle()
            .Which.Should().Match<RoleInfo>(role => role.Name == "Observer" && role.IsActive && !role.ManagesAccess);
        (await api.KeyCatalogueAsync(Cancellation)).Value!.Permissions.Should().Contain(permission => permission.Key == ProjectKeys.Close);

        session.IsSignedIn.Should().BeTrue();
    }

    [Fact]
    public async Task The_project_page_plans_a_project_and_records_an_inspection_for_its_days()
    {
        // It changes what it plans and records, so it has a host of its own.
        await using var host = await sample.StartAsync();
        var (api, _) = await SignInAsync(host, "leo");
        var pierSeven = Harbor.ProjectNamed("Pier 7").Id.Value.ToString();
        var (first, last) = (new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 30));

        (await api.ProjectDetailAsync(pierSeven, Cancellation)).Value!.Should().Match<ProjectInfo>(project => project.PlannedFrom == null && project.PlannedUntil == null && project.Can!.Plan);
        (await api.PlanAsync(pierSeven, first, last, Cancellation)).Succeeded.Should().BeTrue();
        (await api.ProjectDetailAsync(pierSeven, Cancellation)).Value!.Should().Match<ProjectInfo>(project => project.PlannedFrom == first && project.PlannedUntil == last);

        // Within the plan it is recorded and read back with its days; past it, the page shows Inspections' refusal.
        (await api.RecordInspectionAsync(pierSeven, "Handrails checked", first, first.AddDays(1), Cancellation)).Succeeded.Should().BeTrue();
        (await api.InspectionsAsync(pierSeven, cancellationToken: Cancellation)).Value!.Items.Should().ContainSingle()
            .Which.Should().Match<InspectionInfo>(inspection => inspection.From == first && inspection.Until == first.AddDays(1));
        var refused = await api.RecordInspectionAsync(pierSeven, "Handrails checked again", last, last.AddDays(1), Cancellation);
        refused.Problem!.Code.Should().Be("inspections.outside-planned-range");

        // Both pickers blank takes the plan away.
        (await api.PlanAsync(pierSeven, null, null, Cancellation)).Succeeded.Should().BeTrue();
        (await api.ProjectDetailAsync(pierSeven, Cancellation)).Value!.PlannedFrom.Should().BeNull();
    }

    [Fact]
    public async Task A_role_given_until_today_still_counts_today()
    {
        await using var host = await sample.StartAsync();
        var (api, _) = await SignInAsync(host, "leo");
        var pierSeven = Harbor.ProjectNamed("Pier 7").Id.Value.ToString();
        var surveyor = Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // The day picked on the project page, as the page sends it. Sent as the start of that day it would be an
        // end that has passed, and refused.
        var given = await api.GiveCrewRoleAsync(pierSeven, Harbor.SeatOf(DemoPeople.Vic).Value.ToString(), surveyor.ToString(), UntilDay.Instant(today), Cancellation);

        given.Succeeded.Should().BeTrue("the API answered {0}", given.RawBody);
        var vic = (await api.ProjectDetailAsync(pierSeven, Cancellation)).Value!.Crew.Single(member => member.SeatId == Harbor.SeatOf(DemoPeople.Vic).Value);
        vic.RolesHeld.Single(held => held.RoleId == surveyor).Should().Match<CrewRoleInfo>(held => held.AppliesNow && held.EndsAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task The_roles_say_which_manage_access()
    {
        var (api, _) = await SignInAsync("hana");

        var roles = (await api.TenantRolesAsync(Cancellation)).Value!;
        roles.Where(role => role.ManagesAccess).Select(role => role.FromPack)
            .Should().BeEquivalentTo(new[] { SampleCatalogue.AccessAdmin, SampleCatalogue.AreaManager, SampleCatalogue.CrewLead, SampleCatalogue.PeopleOffice });
        var me = (await api.WhoAmIAsync(Cancellation)).Value!;
        me.Roles.Should().ContainSingle().Which.Should().Match<RoleInfo>(role => role.FromPack == SampleCatalogue.PeopleOffice && role.ManagesAccess);
        var keys = (await api.KeyCatalogueAsync(Cancellation)).Value!.Permissions;
        keys.Single(permission => permission.Key == ProjectKeys.ChangeOwner).ManagesAccess.Should().BeTrue();
        keys.Single(permission => permission.Key == ProjectKeys.ManageCrew).ManagesAccess.Should().BeTrue();
        keys.Single(permission => permission.Key == ProjectKeys.Close).ManagesAccess.Should().BeFalse();
    }

    [Fact]
    public async Task The_history_page_reads_who_changed_whose_access_and_the_project_page_who_changed_the_project()
    {
        var (api, _) = await SignInAsync("maud");

        // An access admin reads the history: rows with who acted, here the seeding, and the event as an object.
        var history = (await api.AccessHistoryAsync(size: 20, cancellationToken: Cancellation)).Value!;
        history.Items.Should().HaveCount(20).And.OnlyContain(row => row.By == new ActorInfo("system", null) && row.Details.ValueKind == JsonValueKind.Object && row.Event.StartsWith("tenancy."));
        history.Next.Should().NotBeNull("harbor's history is longer than the page the screen asks for");
        var older = (await api.AccessHistoryAsync(history.Next, 20, Cancellation)).Value!;
        older.Items.Select(row => row.Id).Should().NotBeEmpty().And.NotIntersectWith(history.Items.Select(row => row.Id), "the marker the page gave asks the page after it");

        // And a project says who changed it last: the seeding opened Pier 7.
        (await api.ProjectDetailAsync(Harbor.ProjectNamed("Pier 7").Id.Value.ToString(), Cancellation)).Value!.ChangedBy.Should().Be(new ActorInfo("system", null));

        // Without the key the page shows the API's refusal.
        var (asRhea, _) = await SignInAsync("rhea");
        var refused = await asRhea.AccessHistoryAsync(cancellationToken: Cancellation);
        refused.Status.Should().Be(403);
        refused.Problem!.Code.Should().Be(TenancyRefusals.NotPermitted);
    }

    [Fact]
    public void The_ui_names_the_tenant_in_the_header_the_host_reads()
        => SampleApi.TenantHeader.Should().Be(TenantHeader.Name, "the UI knows the API by its wire only, so the two names are kept equal here");

    [Fact]
    public async Task Tove_starts_in_harbor_and_switching_to_meadow_shows_its_projects()
    {
        var (api, session) = await SignInAsync("tove");

        session.Tenant.Should().Be(Harbor.Slug, "harbor comes before meadow by slug, and both of her seats are active");
        (await api.VisibleProjectsAsync(cancellationToken: Cancellation)).Value!.Items.Select(project => project.Name).Should().Equal("Bay bridge");

        session.SelectTenant(DemoData.Meadow.Slug);

        (await api.VisibleProjectsAsync(cancellationToken: Cancellation)).Value!.Items.Select(project => project.Name).Should().Equal("Garden shed");
    }

    [Fact]
    public async Task The_invitations_page_invites_and_keeps_the_token_off_the_screen_and_the_accept_page_gets_the_seat()
    {
        // It invites and accepts, so it has a host of its own.
        await using var host = await sample.StartAsync();
        var meadow = DemoData.Meadow;
        var (tove, tovesSession) = await SignInAsync(host, "tove");
        tovesSession.SelectTenant(meadow.Slug);

        var invited = await tove.InvitePersonAsync(DemoPeople.Juno.Email, meadow.Root.Value.ToString(), meadow.Roles[SampleCatalogue.Surveyor].Value.ToString(), until: null, Cancellation);

        // The page that invited holds the token, once. What any page shows of the answer, and what the session
        // keeps as its last answer, has it blanked.
        var token = invited.Value!.Token;
        token.Should().HaveLength(43);
        invited.RawBody.Should().NotContain(token);
        tovesSession.LastAnswer!.Path.Should().Be("/tenancy/invitations");
        tovesSession.LastAnswer.RawBody.Should().NotContain(token);
        invited.Value.ToString().Should().NotContain(token);

        var listed = (await tove.OpenInvitationsAsync(Cancellation)).Value!.Should().ContainSingle().Subject;
        listed.Should().Match<InvitationInfo>(invitation => invitation.Id == invited.Value.InvitationId && invitation.Address == DemoPeople.Juno.Email
            && invitation.UnitId == meadow.Root.Value && invitation.RoleEndsAt == null && invitation.IssuedBy == meadow.Administrator.Id.Value);

        // Juno accepts with the token and the name she is shown by in meadow, naming no tenant, and the session then
        // works where her new seat is.
        var (juno, junoSession) = await SignInAsync(host, "juno");
        var accepted = await juno.AcceptInvitationAsync(token, displayName: "Juno", Cancellation);
        accepted.Tenant.Should().BeNull("accepting names no tenant");
        accepted.RawBody.Should().NotContain(token);
        var seats = (await juno.SeatsOfMineAsync(Cancellation)).Value!;
        var hers = seats.Single(mine => mine.Seat.Id == accepted.Value!.SeatId);
        junoSession.SeatAdded(hers.Tenant.Slug);
        (junoSession.Tenant, junoSession.SeatsAdded).Should().Be((meadow.Slug, 1));
        var overview = (await juno.WhoAmIAsync(Cancellation)).Value!;
        overview.Seat.DisplayName.Should().Be("Juno");
        overview.Placements.Should().ContainSingle().Which.Grants.Should().ContainSingle().Which.Role.Should().Be("Surveyor");

        // A second one is revoked from the list, and the refusal of a token nobody was given reads as a problem.
        var second = await tove.InvitePersonAsync("wren@example.test", meadow.Root.Value.ToString(), meadow.Roles[SampleCatalogue.Observer].Value.ToString(), until: null, Cancellation);
        (await tove.CancelInvitationAsync(second.Value!.InvitationId, Cancellation)).Succeeded.Should().BeTrue();
        (await tove.OpenInvitationsAsync(Cancellation)).Value!.Should().BeEmpty();
        (await juno.AcceptInvitationAsync("not-a-token", displayName: null, Cancellation)).Problem!.Code.Should().Be(TenancyRefusals.InvitationNotFound);
    }

    [Fact]
    public async Task Seth_starts_in_his_suspended_seat_and_sees_its_refusal()
    {
        var (api, session) = await SignInAsync("seth");

        session.Tenant.Should().Be(Harbor.Slug);
        var me = await api.WhoAmIAsync(Cancellation);

        me.Status.Should().Be(403);
        me.Problem!.Code.Should().Be("tenancy.seat-suspended");
        session.IsSignedIn.Should().BeTrue("a refused seat is not a refused token");

        // A project's page is refused him the same way, and not as a project that is not found: the page shows
        // the refusal alone, without the words it has for a 404.
        var project = await api.ProjectDetailAsync(Harbor.ProjectNamed("Pier 7").Id.Value.ToString(), Cancellation);
        (project.Status, project.Problem!.Code, project.NotFound).Should().Be((403, "tenancy.seat-suspended", false));
    }

    [Fact]
    public async Task Rhea_who_manages_seats_at_north_reads_that_inviting_takes_the_key_for_the_whole_tenant()
    {
        // Every call here is refused or only reads, so it runs on the shared host.
        var (api, _) = await SignInAsync("rhea");

        // Who am I lists the key for her: held at North, and not for the whole tenant.
        var held = (await api.WhoAmIAsync(Cancellation)).Value!.Keys.Single(key => key.Key == TenancyKeys.SeatsManage);
        held.WholeTenant.Should().BeFalse();
        held.GrantedAt.Select(unit => unit.Path).Should().Equal("Harbor Works / North");

        var refused = await api.InvitePersonAsync(
            "wren@example.test",
            Harbor.UnitNamed("North Coast").Value.ToString(),
            Harbor.Roles[SampleCatalogue.Observer].Value.ToString(),
            until: null,
            Cancellation);

        // The refusal's text names the key she holds and not where it was needed; its arguments name no unit,
        // and from that the page says the key is needed for the whole tenant.
        (refused.Status, refused.Problem!.Code).Should().Be((403, TenancyRefusals.NotPermitted));
        refused.Problem.Title.Should().Be("You lack the permission tenancy.seats.manage.");
        refused.Problem.KeyNeededForTheWholeTenant.Should().BeTrue("the API answered {0}", refused.RawBody);

        // So does the history page, whose key the sample's own check asks for.
        var history = await api.AccessHistoryAsync(size: 5, cancellationToken: Cancellation);
        (history.Problem!.Code, history.Problem.KeyNeededForTheWholeTenant).Should().Be((TenancyRefusals.NotPermitted, true), "the API answered {0}", history.RawBody);

        // A key needed at a unit is refused with that unit, and the page says nothing of the whole tenant.
        var outside = await api.SendAsync<JsonElement?>(
            HttpMethod.Post,
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Tove).Value}/grants",
            new { unitId = Harbor.UnitNamed("South Bay").Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value },
            CallAs.Session,
            TenantChoice.Session,
            Cancellation);
        (outside.Problem!.Code, outside.Problem.KeyNeededForTheWholeTenant).Should().Be((TenancyRefusals.NotPermitted, false), "the API answered {0}", outside.RawBody);
    }

    [Fact]
    public async Task The_language_switch_asks_the_api_in_that_language_and_survives_a_reload()
    {
        var (api, session) = await SignInAsync("seth");
        var english = await api.WhoAmIAsync(Cancellation);

        var changes = 0;
        session.Changed += () => changes++;
        session.SelectLanguage("nl");
        var dutch = await api.WhoAmIAsync(Cancellation);

        // The same refusal, by the same code; only its text follows the switch.
        english.Problem!.Code.Should().Be("tenancy.seat-suspended");
        dutch.Problem!.Code.Should().Be("tenancy.seat-suspended");
        english.Problem.Title.Should().Be("Your seat in this tenant has been suspended.");
        dutch.Problem.Title.Should().Be("Deze plaats in de tenant is geschorst.");
        changes.Should().Be(1, "the pages load again in the new language");

        // A language the UI does not offer changes nothing, and what a reload keeps names the language.
        session.SelectLanguage("fr");
        session.Language.Should().Be("nl");
        changes.Should().Be(1);

        var reloaded = new UiSession();
        reloaded.Restore(session.Snapshot()!, DateTimeOffset.UtcNow).Should().BeTrue();
        reloaded.Language.Should().Be("nl");
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public async Task Every_preset_run_by_the_ui_answers_as_expected(string id)
    {
        var (api, session) = await SignInAsync("leo");
        var runner = new AttemptRunner(api, new DevLoginClient(api));
        var preset = (await api.AttemptsAsync(Cancellation)).Value!.Single(candidate => candidate.Id == id);

        var run = await runner.RunAsync(preset, Cancellation);

        run.AsExpected.Should().BeTrue("'{0}' should answer {1} {2}, and answered {3}", preset.Title, preset.ExpectedStatus, preset.ExpectedCode, run.Answer.RawBody);
        session.Person.Should().Be("leo", "a preset runs as its own person and leaves the session alone");
        session.IsSignedIn.Should().BeTrue();
        session.Tenant.Should().Be(Harbor.Slug);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Every_free_form_action_reaches_its_route_with_a_body_the_api_reads(string name)
    {
        // Vic may read Pier 7, being on its crew, and change nothing, so every command reaches its rules and is
        // refused by one, with a code; a route that did not exist would answer without one.
        var (api, _) = await SignInAsync("vic");
        var action = TryItActions.ByText(name);
        var input = new TryItInput
        {
            Tenant = Harbor.Slug,
            Project = Harbor.ProjectNamed("Pier 7").Id.Value.ToString(),
            Key = ProjectKeys.View,
            Name = "Pier Seven",
            Unit = Harbor.UnitNamed("North Inland").Value.ToString(),
            Title = "Railing loose",
            Seat = Harbor.SeatOf(DemoPeople.Vic).Value.ToString(),
            Role = Harbor.Roles[SampleCatalogue.Observer].Value.ToString(),
            ProjectRole = Harbor.ProjectRoles[SampleCatalogue.Observer].Value.ToString(),
            Until = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Reason = "trying it",
        };

        var outcome = await api.SendAsync<JsonElement?>(action.Method, action.PathFor(input), action.BodyFor(input), CallAs.Session, TenantChoice.Of(input.Tenant), Cancellation);

        if (action.Reads)
        {
            outcome.Succeeded.Should().BeTrue("vic may read what '{0}' asks; the API answered {1}", name, outcome.RawBody);
        }
        else
        {
            outcome.Succeeded.Should().BeFalse("vic may change nothing");
            outcome.Problem!.Code.Should().NotBeNull("'{0}' should reach a route and meet a rule that says no; the API answered {1} {2}", name, outcome.Status, outcome.RawBody)
                .And.NotBe("invalid-request", "the form's body and ids should bind");
        }
    }

    /// <summary>Signs in as the login page does: a token from the dev login, then the tenant from the person's seats.</summary>
    private async Task<(SampleApi Api, UiSession Session)> SignInAsync(string person) => await SignInAsync(await sample.SharedAsync(), person);

    private static async Task<(SampleApi Api, UiSession Session)> SignInAsync(SampleFactory host, string person)
    {
        var session = new UiSession();
        var api = new SampleApi(new HttpClient(host.Server.CreateHandler()) { BaseAddress = host.Server.BaseAddress }, session);
        var login = new DevLoginClient(api);

        var token = (await login.SignInAsync(person, Cancellation)).Value!;
        var seats = (await api.SeatsOfMineAsync(CallAs.Person(person, token.AccessToken), Cancellation)).Value!;
        session.SignIn(person, DemoPeople.Find(person)!.Name, token.AccessToken, token.Expires, UiSession.TenantToStartIn(seats));

        return (api, session);
    }
}
