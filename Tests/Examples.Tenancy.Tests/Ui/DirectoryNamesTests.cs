using System.Net;
using System.Text.Json;
using Examples.Tenancy.Ui.Api;
using Examples.Tenancy.Ui.Api.Wire;
using Examples.Tenancy.Ui.Session;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Ui;

/// <summary>
/// The names a page shows for the ids the API answered with, as plain C# over a stub: an id is asked about once per
/// load, only the kinds with something unknown are asked, the tenant's project roles are read once for all of them,
/// an id the directory does not answer is shown as the start of the id and left alone, more ids than a question
/// takes go in parts, what a page listed anyway is not asked about, and none of it is the session's last answer.
/// </summary>
public sealed class DirectoryNamesTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_id_is_asked_once_per_load()
    {
        using var stub = Directory();
        var names = new DirectoryNames(new SampleApi(stub.Client(), SignedIn()));
        var (leo, juno) = (Guid.NewGuid(), Guid.NewGuid());

        await names.EnsureAsync(seats: [leo, juno, leo], cancellationToken: Cancellation);
        await names.EnsureAsync(seats: [juno, leo], cancellationToken: Cancellation);

        var asked = stub.Requests.Should().ContainSingle("the second time nothing is unknown").Which;
        asked.Method.Should().Be("POST");
        asked.PathAndQuery.Should().Be("/tenancy/directory/seats");
        asked.Authorization.Should().Be("Bearer session-token");
        asked.Tenant.Should().Be("harbor");
        IdsOf(asked).Should().Equal([leo, juno], "each id once");
        names.OfSeat(leo).Should().Be("seat " + leo);
        names.OfSeat(juno).Should().Be("seat " + juno);

        // Another load is another instance: it knows nothing, and asks again, so a renamed seat shows.
        var next = new DirectoryNames(new SampleApi(stub.Client(), SignedIn()));
        await next.EnsureAsync(seats: [leo], cancellationToken: Cancellation);
        stub.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Only_the_kinds_with_unknown_ids_are_asked()
    {
        using var stub = Directory();
        var names = new DirectoryNames(new SampleApi(stub.Client(), SignedIn()));
        var (unit, role, seat) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await names.EnsureAsync(units: [unit], cancellationToken: Cancellation);
        stub.Requests.Select(request => request.PathAndQuery).Should().Equal("/tenancy/directory/units");

        // A crew whose members hold no role asks about no role, and no ids at all ask about nothing.
        await names.EnsureAsync(seats: [], units: [unit], roles: [], cancellationToken: Cancellation);
        await names.EnsureAsync(cancellationToken: Cancellation);
        stub.Requests.Should().HaveCount(1);

        await names.EnsureAsync(seats: [seat], units: [unit], roles: [role, role], cancellationToken: Cancellation);
        stub.Requests.Select(request => request.PathAndQuery).Should().BeEquivalentTo(
            ["/tenancy/directory/units", "/tenancy/directory/seats", "/tenancy/directory/roles"], "the unit is known by now");
        IdsOf(stub.Requests.Single(request => request.PathAndQuery == "/tenancy/directory/roles")).Should().Equal([role], "a role two members hold is asked about once");

        names.OfUnit(unit).Should().Be("path of " + unit);
        names.OfRole(role).Should().Be("role " + role);
    }

    [Fact]
    public async Task The_project_roles_are_read_once_for_every_one_a_page_shows()
    {
        var (lead, surveyor, gone) = (Guid.NewGuid(), Guid.NewGuid(), Guid.Parse("90000000-0000-4000-8000-000000000109"));
        using var stub = Directory(lead, surveyor);
        var names = new DirectoryNames(new SampleApi(stub.Client(), SignedIn()));

        // A tenant has few project roles, so they are read all at once: one question, however many are shown.
        await names.EnsureAsync(projectRoles: [lead, surveyor, lead, gone], cancellationToken: Cancellation);
        var asked = stub.Requests.Should().ContainSingle().Which;
        (asked.Method, asked.PathAndQuery, asked.Body).Should().Be(("GET", "/project-roles", null));

        names.OfProjectRole(lead).Should().Be("crew role " + lead);
        names.OfProjectRole(gone).Should().Be("90000000…", "a role the answer does not hold is shown by the start of its id");
        names.OfProjectRoles([surveyor, lead]).Should().Be(string.Join(", ", new[] { "crew role " + lead, "crew role " + surveyor }.Order(StringComparer.OrdinalIgnoreCase)));
        names.OfProjectRoles([]).Should().BeNull("no role has no name, and the page says so in its own words");

        // Asked again, nothing is unknown, the one the answer left out included.
        await names.EnsureAsync(projectRoles: [surveyor, gone], cancellationToken: Cancellation);
        stub.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task An_id_the_directory_does_not_answer_is_shown_as_its_id_and_not_asked_again()
    {
        var (known, leftOut, refused) = (Guid.NewGuid(), Guid.Parse("c0000000-0000-4000-8000-000000000207"), Guid.Parse("b0000000-0000-4000-8000-000000000205"));
        using var stub = new StubApi(request => request.PathAndQuery switch
        {
            // Another tenant's seat is left out of the answer, and the units' question is refused altogether.
            "/tenancy/directory/seats" => StubApi.Response(HttpStatusCode.OK, JsonSerializer.Serialize(new[] { new { id = known, displayName = "Leo", status = "active" } })),
            _ => StubApi.Response(HttpStatusCode.Forbidden, """{"title":"Not seated","status":403,"code":"tenancy.not-seated"}""", "application/problem+json"),
        });
        var names = new DirectoryNames(new SampleApi(stub.Client(), SignedIn()));

        await names.EnsureAsync(seats: [known, leftOut], units: [refused], cancellationToken: Cancellation);

        names.OfSeat(known).Should().Be("Leo");
        names.OfSeat(leftOut).Should().Be("c0000000…", "the page still shows the row, by the start of the id");
        names.OfUnit(refused).Should().Be("b0000000…", "a refused question is no reason not to show the page");
        names.OfRole(refused).Should().Be("b0000000…", "an id that was never asked about is shown the same way");

        await names.EnsureAsync(seats: [known, leftOut], units: [refused], cancellationToken: Cancellation);
        stub.Requests.Should().HaveCount(2, "what the directory did not answer is not asked about again in this load");
    }

    [Fact]
    public async Task More_ids_than_one_question_takes_are_asked_in_parts()
    {
        using var stub = Directory();
        var names = new DirectoryNames(new SampleApi(stub.Client(), SignedIn()));
        var seats = Enumerable.Range(0, (2 * DirectoryNames.MostIdsPerQuestion) + 50).Select(_ => Guid.NewGuid()).ToArray();

        await names.EnsureAsync(seats: seats, cancellationToken: Cancellation);

        DirectoryNames.MostIdsPerQuestion.Should().Be(SampleTenancy.TenancyDirectory.MostIds, "the UI asks for as many at a time as the API takes");
        stub.Requests.Should().HaveCount(3).And.OnlyContain(request => request.PathAndQuery == "/tenancy/directory/seats");
        stub.Requests.Select(request => IdsOf(request).Count).Should().BeEquivalentTo([200, 200, 50]);
        stub.Requests.SelectMany(IdsOf).Should().BeEquivalentTo(seats, "every id is asked about, each in one part");
        seats.Should().OnlyContain(seat => names.OfSeat(seat) == "seat " + seat);
    }

    [Fact]
    public async Task What_a_page_already_listed_is_not_asked_again()
    {
        using var stub = Directory();
        var names = new DirectoryNames(new SampleApi(stub.Client(), SignedIn()));
        var (leo, coast, manager, lead, elsewhere) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // What the pickers of a page listed: its seats, the units the person is placed under, the roles of the
        // organization and the project roles.
        names.Include([new SeatInfo(leo, "Leo", "active")]);
        names.Include([new UnitInfo(coast, null, "North Coast", "area", "active", "Harbor Works / North / North Coast", 3)]);
        names.Include([new RoleInfo(manager, "Area manager", "area-manager", "active", [])]);
        names.Include([new ProjectRoleInfo(lead, "Crew lead", "Leads a project crew", "crew-lead", "active", [])]);

        await names.EnsureAsync(seats: [leo], units: [coast], roles: [manager], projectRoles: [lead], cancellationToken: Cancellation);
        stub.Requests.Should().BeEmpty("everything the page shows was listed already");
        names.OfSeat(leo).Should().Be("Leo");
        names.OfUnit(coast).Should().Be("Harbor Works / North / North Coast");
        names.OfRole(manager).Should().Be("Area manager");
        names.OfProjectRole(lead).Should().Be("Crew lead");

        // An id whose kind is not known, such as an argument of a refusal, is looked up among all four.
        (names.NameOf(leo), names.NameOf(coast), names.NameOf(manager), names.NameOf(lead))
            .Should().Be(("Leo", "Harbor Works / North / North Coast", "Area manager", "Crew lead"));
        names.NameOf(elsewhere).Should().BeNull("an id the page has no name for stays an id");

        // Only what the lists lack is asked for: a unit the person is not placed under.
        await names.EnsureAsync(seats: [leo], units: [coast, elsewhere], roles: [manager], projectRoles: [lead], cancellationToken: Cancellation);
        var asked = stub.Requests.Should().ContainSingle().Which;
        asked.PathAndQuery.Should().Be("/tenancy/directory/units");
        IdsOf(asked).Should().Equal(elsewhere);

        // A crew is shown the owner first, then by the names its seats are shown by; a member's roles by their
        // names, and several of them as one text.
        var (juno, vic, surveyor) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        names.Include([new SeatInfo(juno, "Juno", "active"), new SeatInfo(vic, "Vic", "active")]);
        names.Include([new ProjectRoleInfo(surveyor, "Surveyor", "Records inspections", "surveyor", "active", [])]);
        var start = DateTimeOffset.UtcNow;
        CrewRoleInfo Held(Guid role) => new(role, start, null, true);
        var both = new CrewInfo(juno, false, start, null, true, [Held(surveyor), Held(lead)]);
        names.CrewByName([new CrewInfo(vic, false, start, null, true, []), both, new CrewInfo(leo, true, start, null, true, [Held(lead)])])
            .Select(member => names.OfSeat(member.SeatId)).Should().Equal("Leo", "Juno", "Vic");
        names.RolesByName(both).Select(held => names.OfProjectRole(held.RoleId)).Should().Equal("Crew lead", "Surveyor");

        // Two seats shown by one name come in the order of their ids, whichever way the API listed them.
        var (first, second) = (new Guid("c0000000-0000-4000-8000-000000000201"), new Guid("c0000000-0000-4000-8000-000000000202"));
        names.Include([new SeatInfo(first, "Wren", "active"), new SeatInfo(second, "Wren", "active")]);
        names.CrewByName([new CrewInfo(second, false, start, null, true, []), new CrewInfo(first, false, start, null, true, [])])
            .Select(member => member.SeatId).Should().Equal(first, second);
        names.OfProjectRoles(both.RolesHeld.Select(held => held.RoleId)).Should().Be("Crew lead, Surveyor");

        // A member the API answered without a list of roles reads as one with none.
        new CrewInfo(vic, false, start, null, true, Roles: null).RolesHeld.Should().BeEmpty();
        new ProjectInfo(Guid.NewGuid(), "P-1", "Pier", coast, "open", leo, MyRoleIds: null, "crew", [both], null).RoleIds.Should().BeEquivalentTo([surveyor, lead]);
    }

    [Fact]
    public async Task A_directory_question_is_not_the_sessions_last_answer()
    {
        using var stub = StubApi.Answering(HttpStatusCode.OK, "[]");
        var session = SignedIn();
        var api = new SampleApi(stub.Client(), session);

        var projects = await api.VisibleProjectsAsync(cancellationToken: Cancellation);
        session.LastAnswer.Should().BeSameAs(projects);

        await new DirectoryNames(api).EnsureAsync(seats: [Guid.NewGuid()], units: [Guid.NewGuid()], roles: [Guid.NewGuid()], projectRoles: [Guid.NewGuid()], cancellationToken: Cancellation);

        stub.Requests.Should().HaveCount(5, "the list, a question for each kind, and the project roles");
        session.LastAnswer.Should().BeSameAs(projects, "the page is about its projects; the names only fill them in");
    }

    /// <summary>
    /// A directory that knows every id it is asked about, and names each after the id; and a tenant whose project
    /// roles are <paramref name="projectRoles"/>, each named after its id.
    /// </summary>
    private static StubApi Directory(params Guid[] projectRoles) => new(request =>
    {
        object[] rows = request.PathAndQuery switch
        {
            "/tenancy/directory/seats" => [.. IdsOf(request).Select(id => new { id, displayName = "seat " + id, status = "active" })],
            "/tenancy/directory/units" => [.. IdsOf(request).Select(id => new { id, parentId = (Guid?)null, name = "unit", kind = "area", status = "active", path = "path of " + id, depth = 1 })],
            "/tenancy/directory/roles" => [.. IdsOf(request).Select(id => new { id, name = "role " + id, fromPack = (string?)null, status = "active", keys = Array.Empty<string>(), managesAccess = false })],
            "/project-roles" => [.. projectRoles.Select(id => new { id, name = "crew role " + id, description = string.Empty, madeFrom = (string?)null, status = "active", keys = (string[]?)null })],
            _ => throw new InvalidOperationException("The stub has no route " + request.PathAndQuery + "."),
        };
        return StubApi.Response(HttpStatusCode.OK, JsonSerializer.Serialize(rows));
    });

    /// <summary>The ids a request asked about, in the order its body has them.</summary>
    private static IReadOnlyList<Guid> IdsOf(StubRequest request)
        => [.. JsonDocument.Parse(request.Body!).RootElement.GetProperty("ids").EnumerateArray().Select(id => id.GetGuid())];

    private static UiSession SignedIn()
    {
        var session = new UiSession();
        session.SignIn("rhea", "Rhea", "session-token", DateTimeOffset.UtcNow.AddHours(1), "harbor");
        return session;
    }
}
