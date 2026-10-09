using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Examples.Tenancy.Ui;
using Examples.Tenancy.Ui.Api;
using Examples.Tenancy.Ui.Api.TryIt;
using Examples.Tenancy.Ui.Api.Wire;
using Examples.Tenancy.Ui.Auth;
using Examples.Tenancy.Ui.Components.Shared;
using Examples.Tenancy.Ui.Session;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The UI's records of a row of GET /me/seats and its tenant, not the module's answers of the same names, which every
// file of this project imports.
using SeatOfMine = Examples.Tenancy.Ui.Api.Wire.SeatOfMine;
using TenantOfSeat = Examples.Tenancy.Ui.Api.Wire.TenantOfSeat;

namespace Examples.Tenancy.Tests.Ui;

/// <summary>
/// The UI's client of the API, as plain C# over a stub: how it reads a refusal, when a 401 ends the session, that it
/// never retries, and which headers it sends.
/// </summary>
public sealed class SampleApiTests
{
    private const string SessionToken = "session-token";
    private const string ProjectId = "f0000000-0000-4000-8000-000000000101";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Problem_json_maps_to_code_title_and_arguments()
    {
        const string body = """
            {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"You may not do this to the project.","status":403,
             "detail":"More words.","code":"projects.not-permitted","arguments":{"Key":"projects.close","Max":200},"traceId":"00-1"}
            """;
        using var stub = StubApi.Answering(HttpStatusCode.Forbidden, body, "application/problem+json");
        var session = SignedIn();
        var api = new SampleApi(stub.Client(), session);

        var outcome = await api.CloseAsync(ProjectId, Cancellation);

        outcome.Status.Should().Be(403);
        outcome.Succeeded.Should().BeFalse();
        outcome.Value.Should().BeNull();
        outcome.RawBody.Should().Be(body);
        outcome.Method.Should().Be("POST");
        outcome.Path.Should().Be($"/projects/{ProjectId}/close");
        outcome.Caller.Should().Be("rhea");
        outcome.Tenant.Should().Be("harbor");

        outcome.Problem.Should().NotBeNull();
        outcome.Problem!.Code.Should().Be("projects.not-permitted");
        outcome.Problem.Title.Should().Be("You may not do this to the project.");
        outcome.Problem.Detail.Should().Be("More words.");
        outcome.Problem.Arguments.Keys.Should().BeEquivalentTo(new[] { "Key", "Max" });
        outcome.Problem.Arguments["Key"].GetString().Should().Be("projects.close");
        outcome.Problem.Arguments["Max"].GetInt32().Should().Be(200);

        session.LastAnswer.Should().BeSameAs(outcome, "the layout's strip shows every answer");
    }

    [Fact]
    public async Task A_problem_without_a_code_or_a_body_reads_as_a_problem_with_no_code()
    {
        using var stub = new StubApi(request => request.PathAndQuery == "/empty"
            ? StubApi.Response(HttpStatusCode.NotFound)
            : StubApi.Response(HttpStatusCode.Unauthorized, """{"title":"Unauthorized","status":401}""", "application/problem+json"));
        var api = new SampleApi(stub.Client(), new UiSession());

        var unauthorized = await api.SendAsync<JsonElement?>(HttpMethod.Get, "/projects", null, CallAs.Anonymous, TenantChoice.None, Cancellation);
        var empty = await api.SendAsync<JsonElement?>(HttpMethod.Get, "/empty", null, CallAs.Anonymous, TenantChoice.None, Cancellation);

        unauthorized.Problem!.Code.Should().BeNull();
        unauthorized.Problem.Title.Should().Be("Unauthorized");
        empty.Status.Should().Be(404);
        empty.Problem!.Code.Should().BeNull();
        empty.Problem.Arguments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_401_clears_the_session_only_when_the_session_token_was_sent()
    {
        var status = HttpStatusCode.Unauthorized;
        using var stub = new StubApi(_ => StubApi.Response(status, """{"title":"Unauthorized","status":401}""", "application/problem+json"));
        var session = SignedIn();
        var changes = 0;
        session.Changed += () => changes++;
        var api = new SampleApi(stub.Client(), session);

        // A preset's person being refused, and an anonymous call being refused, are the answers being demonstrated.
        await api.SendAsync<JsonElement?>(HttpMethod.Get, "/me", null, CallAs.Person("vic", "vics-token"), TenantChoice.From("harbor"), Cancellation);
        await api.SendAsync<JsonElement?>(HttpMethod.Get, "/me", null, CallAs.Anonymous, TenantChoice.Session, Cancellation);

        // And the session's own token being refused anything but a 401 says nothing about the token.
        status = HttpStatusCode.Forbidden;
        await api.WhoAmIAsync(Cancellation);

        session.IsSignedIn.Should().BeTrue();
        session.AccessToken.Should().Be(SessionToken);
        changes.Should().Be(0);

        status = HttpStatusCode.Unauthorized;
        var refused = await api.WhoAmIAsync(Cancellation);

        refused.Status.Should().Be(401);
        stub.Requests[^1].Authorization.Should().Be("Bearer " + SessionToken);
        session.IsSignedIn.Should().BeFalse();
        session.AccessToken.Should().BeNull();
        session.Tenant.Should().BeNull();
        session.SignedOutBecause.Should().Contain("401");
        session.LastAnswer.Should().BeSameAs(refused, "the answer that ended the session is the last one shown");
        changes.Should().Be(1);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_failed_post_is_never_retried(string method)
    {
        // Registered as the UI registers it, after the service defaults, which give every client a standard
        // resilience handler that retries a 503.
        using var stub = StubApi.Answering(HttpStatusCode.ServiceUnavailable);
        var builder = UiHost.CreateBuilder();
        builder.Configuration["Api:BaseUrl"] = StubApi.BaseAddress.ToString();
        builder.AddServiceDefaults();
        builder.Services.AddSampleUi();
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => stub));
        using var host = builder.Build();

        await using var circuit = host.Services.CreateAsyncScope();
        circuit.ServiceProvider.GetRequiredService<UiSession>().SignIn("rhea", "Rhea", SessionToken, DateTimeOffset.UtcNow.AddHours(1), "harbor");
        var api = circuit.ServiceProvider.GetRequiredService<SampleApi>();

        var outcome = await api.SendAsync<JsonElement?>(new HttpMethod(method), $"/projects/{ProjectId}/close", new { }, CallAs.Session, TenantChoice.Session, Cancellation);

        outcome.Status.Should().Be(503);
        stub.Requests.Should().ContainSingle("the first answer is the one shown, and a change is never sent twice");
    }

    [Fact]
    public async Task A_blank_tenant_sends_no_header_and_no_token_sends_no_bearer()
    {
        using var stub = StubApi.Answering(HttpStatusCode.OK, "[]");
        var api = new SampleApi(stub.Client(), SignedIn());

        await api.SendAsync<JsonElement?>(HttpMethod.Get, "/projects", null, CallAs.Anonymous, TenantChoice.From("   "), Cancellation);
        await api.VisibleProjectsAsync(cancellationToken: Cancellation);

        var bare = stub.Requests[0];
        bare.Authorization.Should().BeNull();
        bare.Tenant.Should().BeNull();

        // The same client, the session's way: its token and its tenant.
        var asSession = stub.Requests[1];
        asSession.Authorization.Should().Be("Bearer " + SessionToken);
        asSession.Tenant.Should().Be("harbor");
    }

    [Fact]
    public async Task A_role_reads_whether_it_manages_access()
    {
        using var stub = new StubApi(request => request.PathAndQuery == "/tenancy/roles"
            ? StubApi.Response(HttpStatusCode.OK, """
                [{"id":"e0000000-0000-4000-8000-000000000106","name":"People office","fromPack":"people-office","status":"active","keys":["tenancy.grants.manage"],"managesAccess":true},
                 {"id":"e0000000-0000-4000-8000-000000000105","name":"Observer","fromPack":"observer","status":"active","keys":["projects.view"]},
                 {"id":"e0000000-0000-4000-8000-000000000103","name":"Crew lead","fromPack":"crew-lead","status":"archived","keys":[]}]
                """)
            : StubApi.Response(HttpStatusCode.OK, """{"permissions":[{"key":"projects.owner.change","module":"Projects","description":"Name a project's owner","retired":false,"managesAccess":true}]}"""));
        var api = new SampleApi(stub.Client(), SignedIn());

        var roles = (await api.TenantRolesAsync(Cancellation)).Value!;
        var catalogue = (await api.KeyCatalogueAsync(Cancellation)).Value!;

        roles.Select(role => (role.Name, role.ManagesAccess, role.IsActive)).Should().Equal(
            [("People office", true, true), ("Observer", false, true), ("Crew lead", false, false)], "an answer that does not say reads as managing none");
        catalogue.Permissions.Single().ManagesAccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_project_role_reads_its_keys_only_where_they_were_answered()
    {
        using var stub = StubApi.Answering(HttpStatusCode.OK, """
            [{"id":"90000000-0000-4000-8000-000000000103","name":"Crew lead","description":"Leads a project crew","madeFrom":"crew-lead","status":"active","keys":["projects.view","projects.edit"]},
             {"id":"90000000-0000-4000-8000-000000000106","name":"Diver","description":"","madeFrom":null,"status":"archived","keys":null}]
            """);
        var api = new SampleApi(stub.Client(), SignedIn());

        var roles = (await api.ProjectRolesAsync(Cancellation)).Value!;

        stub.Requests.Should().ContainSingle().Which.Should().Match<StubRequest>(request => request.Method == "GET" && request.PathAndQuery == "/project-roles");
        roles.Select(role => (role.Name, role.MadeFrom, role.IsActive, role.Keys is null, role.KeysShown.Count)).Should().Equal(
            [("Crew lead", "crew-lead", true, false, 2), ("Diver", null, false, true, 0)],
            "keys the answer left out read as none to show, and not as a role that gives none");
    }

    [Fact]
    public async Task The_crew_commands_reach_their_routes_with_the_bodies_the_api_reads()
    {
        using var stub = StubApi.Answering(HttpStatusCode.NoContent);
        var api = new SampleApi(stub.Client(), SignedIn());
        const string seat = "c0000000-0000-4000-8000-000000000105";
        const string role = "90000000-0000-4000-8000-000000000104";
        var until = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await api.AddToCrewAsync(ProjectId, seat, roleId: " ", until: null, Cancellation);
        await api.GiveCrewRoleAsync(ProjectId, seat, role, until, Cancellation);
        await api.TakeCrewRoleAsync(ProjectId, seat, role, Cancellation);
        await api.RemoveFromCrewAsync(ProjectId, seat, Cancellation);

        stub.Requests.Select(request => (request.Method, request.PathAndQuery, request.Body)).Should().Equal(
            ("POST", $"/projects/{ProjectId}/crew", $$"""{"seatId":"{{seat}}","roleId":null,"until":null}"""),
            ("POST", $"/projects/{ProjectId}/crew/{seat}/roles", $$"""{"roleId":"{{role}}","until":"2027-01-01T00:00:00+00:00"}"""),
            ("DELETE", $"/projects/{ProjectId}/crew/{seat}/roles/{role}", null),
            ("DELETE", $"/projects/{ProjectId}/crew/{seat}", null));
    }

    [Fact]
    public async Task The_project_role_commands_reach_their_routes_with_the_bodies_the_api_reads()
    {
        using var stub = StubApi.Answering(HttpStatusCode.NoContent);
        var api = new SampleApi(stub.Client(), SignedIn());
        const string role = "90000000-0000-4000-8000-000000000104";

        await api.MakeProjectRoleAsync("Rigger", " ", ["projects.view"], Cancellation);
        await api.RenameProjectRoleAsync(role, "Rigger", "Rigs the hoists", Cancellation);
        await api.SetProjectRoleKeysAsync(role, [], Cancellation);
        await api.ArchiveProjectRoleAsync(role, Cancellation);

        stub.Requests.Select(request => (request.Method, request.PathAndQuery, request.Body)).Should().Equal(
            ("POST", "/project-roles", """{"name":"Rigger","description":null,"keys":["projects.view"]}"""),
            ("PUT", $"/project-roles/{role}", """{"name":"Rigger","description":"Rigs the hoists"}"""),
            ("PUT", $"/project-roles/{role}/keys", """{"keys":[]}"""),
            ("POST", $"/project-roles/{role}/archive", null));
    }

    [Fact]
    public async Task A_project_reads_its_crew_with_the_roles_each_member_holds()
    {
        using var stub = StubApi.Answering(HttpStatusCode.OK, """
            {"id":"f0000000-0000-4000-8000-000000000101","number":"P-001","name":"Pier 7","unitId":"b0000000-0000-4000-8000-000000000103","state":"open",
             "ownerSeat":"c0000000-0000-4000-8000-000000000103","myRoleIds":["90000000-0000-4000-8000-000000000105"],"via":"crew",
             "crew":[
               {"seatId":"c0000000-0000-4000-8000-000000000103","isOwner":true,"startsAt":"2026-09-01T08:00:00+00:00","endsAt":null,"appliesNow":true,
                "roles":[{"roleId":"90000000-0000-4000-8000-000000000103","startsAt":"2026-09-01T08:00:00+00:00","endsAt":null,"appliesNow":true}]},
               {"seatId":"c0000000-0000-4000-8000-000000000105","isOwner":false,"startsAt":"2026-09-02T08:00:00+00:00","endsAt":null,"appliesNow":true,
                "roles":[{"roleId":"90000000-0000-4000-8000-000000000105","startsAt":"2026-09-02T08:00:00+00:00","endsAt":null,"appliesNow":true},
                         {"roleId":"90000000-0000-4000-8000-000000000104","startsAt":"2026-09-03T08:00:00+00:00","endsAt":"2026-09-04T08:00:00+00:00","appliesNow":false}]},
               {"seatId":"c0000000-0000-4000-8000-000000000107","isOwner":false,"startsAt":"2026-09-05T08:00:00+00:00","endsAt":null,"appliesNow":true,"roles":[]}],
             "can":null}
            """);
        var api = new SampleApi(stub.Client(), SignedIn());

        var project = (await api.ProjectDetailAsync(ProjectId, Cancellation)).Value!;

        project.MyRoles.Should().Equal(Guid.Parse("90000000-0000-4000-8000-000000000105"));
        project.Crew.Select(member => member.RolesHeld.Count).Should().Equal(1, 2, 0);
        var ended = project.Crew[1].RolesHeld[1];
        ended.AppliesNow.Should().BeFalse();
        ended.EndsAt.Should().Be(new DateTimeOffset(2026, 9, 4, 8, 0, 0, TimeSpan.Zero));
        project.RoleIds.Distinct().Should().HaveCount(3, "the page names each project role once");
    }

    [Fact]
    public async Task A_command_answered_204_succeeds_with_no_body()
    {
        using var stub = StubApi.Answering(HttpStatusCode.NoContent);
        var api = new SampleApi(stub.Client(), SignedIn());

        var outcome = await api.RenameAsync(ProjectId, "Pier Seven", Cancellation);

        outcome.Succeeded.Should().BeTrue();
        outcome.Problem.Should().BeNull();
        outcome.Value.Should().BeNull();
        stub.Requests.Single().Body.Should().Be("""{"name":"Pier Seven"}""");
    }

    [Fact]
    public async Task An_api_that_does_not_answer_is_an_outcome_with_status_0()
    {
        using var stub = new StubApi(_ => throw new HttpRequestException("No connection could be made."));
        var api = new SampleApi(stub.Client(), SignedIn());

        var outcome = await api.WhoAmIAsync(Cancellation);

        outcome.Unanswered.Should().BeTrue();
        outcome.Status.Should().Be(0);
        outcome.Problem!.Detail.Should().Contain("No connection");
    }

    [Fact]
    public async Task The_project_page_explains_a_project_that_is_not_found_and_no_other_refusal()
    {
        const string unseen = "f0000000-0000-4000-8000-000000000102";
        using var stub = new StubApi(request => request.PathAndQuery == $"/projects/{unseen}"
            ? StubApi.Response(HttpStatusCode.NotFound, """{"title":"There is no such project.","status":404,"code":"projects.not-found","arguments":{}}""", "application/problem+json")
            : StubApi.Response(HttpStatusCode.Forbidden, """{"title":"Your seat in this tenant has been suspended.","status":403,"code":"tenancy.seat-suspended","arguments":{}}""", "application/problem+json"));
        var api = new SampleApi(stub.Client(), SignedIn());

        var notFound = await api.ProjectDetailAsync(unseen, Cancellation);
        var suspended = await api.ProjectDetailAsync(ProjectId, Cancellation);

        // "Not found, exactly as one that does not exist" is what the page says under a 404, which gives no
        // reason. A suspended seat's 403 gives its own, and the page shows it alone.
        (notFound.Value, notFound.Status, notFound.NotFound).Should().Be((null, 404, true));
        (suspended.Value, suspended.Status, suspended.NotFound).Should().Be((null, 403, false), "a refusal that says why is not explained as a project nobody may see");
        suspended.Problem!.Code.Should().Be("tenancy.seat-suspended");

        // Nor is an API that does not answer.
        using var silent = new StubApi(_ => throw new HttpRequestException("No connection could be made."));
        (await new SampleApi(silent.Client(), SignedIn()).ProjectDetailAsync(ProjectId, Cancellation)).NotFound.Should().BeFalse();

        // And the page asks exactly that before it says so: its one use of the text is under the question.
        var page = File.ReadAllText(Path.Combine(SampleLayout.RepositoryRoot(), "Examples", "Tenancy", "Examples.Tenancy.Ui", "Components", "Pages", "ProjectDetail.razor"));
        Regex.Matches(page, Regex.Escape("project.not-shown")).Should().ContainSingle();
        page.Should().MatchRegex("""@if \(_project\.NotFound\)\s*\{\s*<p>@T\["project\.not-shown"\]</p>\s*\}""");
    }

    [Fact]
    public async Task A_refusal_for_a_key_that_names_no_unit_reads_as_one_for_the_whole_tenant()
    {
        static HttpResponseMessage Refused(string code, string arguments)
            => StubApi.Response(HttpStatusCode.Forbidden, $$"""{"title":"You lack a permission.","status":403,"code":"{{code}}","arguments":{{arguments}}}""", "application/problem+json");

        using var stub = new StubApi(request => request.PathAndQuery switch
        {
            // As Tenancy's own check writes it for the whole tenant, and as the sample's check for a tenant-wide key does.
            "/whole-tenant" => Refused("tenancy.not-permitted", """{"Key":"tenancy.seats.manage","Unit":null}"""),
            "/names-no-unit" => Refused("tenancy.not-permitted", """{"Key":"tenancy.history.view"}"""),
            "/at-a-unit" => Refused("tenancy.not-permitted", """{"Key":"tenancy.grants.manage","Unit":"c0000000-0000-4000-8000-000000000004"}"""),
            "/another-rule" => Refused("tenancy.grant-exceeds-own", """{"Missing":"tenancy.seats.manage"}"""),
            _ => Refused("projects.not-permitted", """{"Key":"projects.close"}"""),
        });
        var api = new SampleApi(stub.Client(), SignedIn());

        async Task<bool> WholeTenantAsync(string path)
            => (await api.SendAsync<JsonElement?>(HttpMethod.Get, path, null, CallAs.Session, TenantChoice.Session, Cancellation)).Problem!.KeyNeededForTheWholeTenant;

        // The refusal's text names the key and not where it was needed, so a seat that holds the key at a unit
        // would read only that it lacks it. The arguments say where: no unit is the whole tenant.
        (await WholeTenantAsync("/whole-tenant")).Should().BeTrue();
        (await WholeTenantAsync("/names-no-unit")).Should().BeTrue();
        (await WholeTenantAsync("/at-a-unit")).Should().BeFalse("the key was needed at that unit, which the arguments show by name");
        (await WholeTenantAsync("/another-rule")).Should().BeFalse("only the refusal for a key that is not held says where the key was needed");
        (await WholeTenantAsync("/a-project")).Should().BeFalse("a project's refusal is about the project, and names no unit either");
        ApiProblem.WithoutArguments(null, "Unauthorized").KeyNeededForTheWholeTenant.Should().BeFalse();
    }

    [Fact]
    public async Task A_typed_id_is_escaped_into_the_path_so_the_api_refuses_it()
    {
        using var stub = StubApi.Answering(HttpStatusCode.BadRequest);
        var api = new SampleApi(stub.Client(), SignedIn());

        await api.ProjectDetailAsync("not an/id", Cancellation);

        stub.Requests.Single().PathAndQuery.Should().Be("/projects/not%20an%2Fid");
    }

    [Fact]
    public async Task A_preset_runs_as_its_person_and_leaves_the_session_alone()
    {
        using var stub = new StubApi(request => request.PathAndQuery == "/dev/auth/token"
            ? StubApi.Response(HttpStatusCode.OK, """{"access_token":"vics-token","token_type":"bearer","expires_in":3600,"expires_at":4102444800,"user":{"id":"d0000000-0000-4000-8000-000000000005"}}""")
            : StubApi.Response(HttpStatusCode.Unauthorized, """{"title":"Unauthorized","status":401}""", "application/problem+json"));
        var session = SignedIn();
        var api = new SampleApi(stub.Client(), session);
        var runner = new AttemptRunner(api, new DevLoginClient(api));

        using var body = JsonDocument.Parse("""{"roleId":"90000000-0000-4000-8000-000000000103"}""");
        var withToken = new AttemptPreset("p", "vic tries", "vic", "harbor", SendToken: true, SendTenant: true, "POST", "/projects/x/crew/y/roles", body.RootElement.Clone(), 401, null);
        var bare = withToken with { Id = "q", SendToken = false, SendTenant = false, Body = null };

        var first = await runner.RunAsync(withToken, Cancellation);
        var second = await runner.RunAsync(bare, Cancellation);

        first.AsExpected.Should().BeTrue();
        second.AsExpected.Should().BeTrue();

        var sent = stub.Requests.Where(request => request.PathAndQuery != "/dev/auth/token").ToList();
        sent[0].Should().Be(new StubRequest("POST", "/projects/x/crew/y/roles", "Bearer vics-token", "harbor", """{"roleId":"90000000-0000-4000-8000-000000000103"}"""));
        sent[1].Should().Be(new StubRequest("POST", "/projects/x/crew/y/roles", null, null, null));
        stub.Requests.Single(request => request.PathAndQuery == "/dev/auth/token").Body.Should().Be("""{"person":"vic"}""");

        session.IsSignedIn.Should().BeTrue("a preset's 401 is the answer being shown, not the end of the session");
        session.Person.Should().Be("rhea");
        session.Tenant.Should().Be("harbor");
    }

    [Fact]
    public async Task A_token_answer_keeps_its_token_out_of_what_a_page_shows()
    {
        using var stub = StubApi.Answering(
            HttpStatusCode.OK,
            """{"access_token":"vics-token","refresh_token":"vics-refresh","token_type":"bearer","expires_in":3600,"expires_at":4102444800,"user":{"id":"d0000000-0000-4000-8000-000000000005"}}""");
        var session = SignedIn();
        var api = new SampleApi(stub.Client(), session);
        var before = await api.WhoAmIAsync(Cancellation);

        var token = await api.TokenAsync("vic", Cancellation);

        token.Value!.AccessToken.Should().Be("vics-token", "the code that sends the token reads it from the value");
        token.RawBody.Should().NotContain("vics-token").And.NotContain("vics-refresh").And.Contain("bearer");
        session.LastAnswer.Should().BeSameAs(before, "a token request is not an answer the layout shows");
    }

    [Fact]
    public async Task Logging_out_forgets_the_last_answer_and_the_history()
    {
        using var stub = StubApi.Answering(HttpStatusCode.OK, "[]");
        var session = SignedIn();
        var api = new SampleApi(stub.Client(), session);
        var answered = 0;
        session.Answered += () => answered++;

        var projects = await api.VisibleProjectsAsync(cancellationToken: Cancellation);
        session.Remember(new TryItEntry(DateTimeOffset.UtcNow, "Open project", projects));
        answered = 0;

        session.SignOut();

        session.LastAnswer.Should().BeNull();
        session.History.Should().BeEmpty();
        answered.Should().Be(1, "the layout redraws its last-response strip");

        // Signing in as somebody else without logging out starts an empty history as well.
        session.SignIn("rhea", "Rhea", SessionToken, DateTimeOffset.UtcNow.AddHours(1), "harbor");
        session.Remember(new TryItEntry(DateTimeOffset.UtcNow, "Open project", projects));
        session.SignIn("tove", "Tove", "toves-token", DateTimeOffset.UtcNow.AddHours(1), "harbor");

        session.History.Should().BeEmpty();
    }

    [Fact]
    public void A_seat_that_is_not_active_says_so_wherever_a_picker_lists_it()
    {
        // The crew's picker and the owner's list the same seats, and say the same about one the API would refuse.
        new SeatInfo(Guid.NewGuid(), "Seth", "suspended").Label.Should().Be("Seth (suspended)");
        new SeatInfo(Guid.NewGuid(), "Leo", "active").Label.Should().Be("Leo");
    }

    [Fact]
    public void A_day_picked_as_until_is_sent_as_the_first_moment_after_it()
    {
        var friday = new DateOnly(2026, 10, 2);

        // The API's end is the first moment that no longer counts, so Friday itself still does.
        UntilDay.Instant(friday).Should().Be(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
        UntilDay.Instant(null).Should().BeNull("no day is no end");
        new TryItInput { Until = friday }.UntilInstant.Should().Be(UntilDay.Instant(friday), "the try-it form sends what the project page sends");
    }

    [Fact]
    public void An_end_is_shown_as_the_day_that_was_picked()
    {
        var friday = new DateOnly(2026, 10, 2);

        // What was sent for Friday reads Friday again, and not the moment after it.
        UntilDay.Shown(UntilDay.Instant(friday)!.Value).Should().Be("2026-10-02");
        UntilDay.Shown(new DateTimeOffset(2026, 10, 3, 2, 0, 0, TimeSpan.FromHours(2))).Should().Be("2026-10-02", "the same moment, said in another zone");

        // An end somebody set to the minute through the API is shown as the moment it is.
        UntilDay.Shown(new DateTimeOffset(2026, 10, 2, 16, 30, 0, TimeSpan.Zero)).Should().Be("2026-10-02 16:30 UTC");
    }

    [Fact]
    public void A_page_shows_the_answer_of_its_last_action_only()
    {
        var latest = new LatestAnswer();
        object invite = new(), page = new();

        // Inviting answered, then a revoke in the table, which leaves its answer to the page: the first goes.
        latest.IsFrom(invite);
        latest.IsOf(invite).Should().BeTrue();
        latest.IsFrom(page);
        (latest.IsOf(invite), latest.IsOf(page)).Should().Be((false, true), "an older answer is about the page as it was");
    }

    [Fact]
    public void A_fresh_sign_in_starts_in_the_first_tenant_by_slug_where_the_seat_is_active()
    {
        static SeatOfMine In(string slug, string status) => new(new TenantOfSeat(Guid.NewGuid(), slug, slug, "active"), new SeatInfo(Guid.NewGuid(), "Someone", status));

        UiSession.TenantToStartIn([In("meadow", "active"), In("harbor", "active")]).Should().Be("harbor");
        UiSession.TenantToStartIn([In("harbor", "suspended"), In("meadow", "active")]).Should().Be("meadow");
        UiSession.TenantToStartIn([In("harbor", "suspended")]).Should().Be("harbor", "someone with no active seat starts where the refusal is");
        UiSession.TenantToStartIn([]).Should().BeNull();
    }

    [Fact]
    public void A_kept_session_whose_token_has_expired_is_not_restored()
    {
        var session = new UiSession();
        var now = DateTimeOffset.UtcNow;

        session.Restore(new SessionSnapshot("rhea", "Rhea", SessionToken, now.AddSeconds(-1), "harbor"), now).Should().BeFalse();
        session.IsSignedIn.Should().BeFalse();

        // A copy kept before the UI had two languages names none, and is restored in the first.
        session.Restore(new SessionSnapshot("rhea", "Rhea", SessionToken, now.AddMinutes(5), "harbor"), now).Should().BeTrue();
        session.Snapshot().Should().Be(new SessionSnapshot("rhea", "Rhea", SessionToken, now.AddMinutes(5), "harbor", "en"));
    }

    private static UiSession SignedIn()
    {
        var session = new UiSession();
        session.SignIn("rhea", "Rhea", SessionToken, DateTimeOffset.UtcNow.AddHours(1), "harbor");
        return session;
    }
}
