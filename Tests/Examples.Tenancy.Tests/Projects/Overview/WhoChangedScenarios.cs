using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using FluentAssertions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Projects.Overview;

/// <summary>
/// A project and an inspection each say who changed their row last: the kind of actor and, for a seat, its id.
/// The save that wrote the row filled it in from the caller it ran as, so a seat's change names that seat, the
/// application's own work names the system, and work carried out for an operator names the operator, by its kind
/// alone. What an operator tries itself is refused, and leaves the row saying what it said.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks, and the trigger that holds a caller to itself in what a row says of who changed it.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class WhoChangedScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject Pier => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task A_change_by_a_seat_is_recorded_as_that_seat()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        // Rhea renames the project: the row says her seat.
        await RenameAsync(rhea, "Pier 7 east");
        ChangedBy(await rhea.ProjectDetailAsync(Pier)).Should().Be(("seat", Harbor.SeatOf(DemoPeople.Rhea).Value));

        // Leo renames it after her: the row says who changed it last, and a row of the list reads the same.
        await RenameAsync(leo, "Pier 7 west");
        ChangedBy(await rhea.ProjectDetailAsync(Pier)).Should().Be(("seat", Harbor.SeatOf(DemoPeople.Leo).Value));
        ChangedBy((await rhea.VisibleProjectsAsync()).Named("Pier 7 west")).Should().Be(("seat", Harbor.SeatOf(DemoPeople.Leo).Value));

        // An inspection never changes, so who changed its row last is who recorded it.
        using (var recorded = await juno.PostAsJsonAsync($"/projects/{Pier.Id.Value}/inspections", new { title = "Loose railing" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var inspection = (await juno.GetFromJsonAsync<JsonElement>($"/projects/{Pier.Id.Value}/inspections", Cancellation)).GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        ChangedBy(inspection).Should().Be(("seat", Harbor.SeatOf(DemoPeople.Juno).Value));
        inspection.GetProperty("recordedBy").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Juno).Value);
        inspection.TryGetProperty("recordedAt", out _).Should().BeTrue("and when is the moment it was recorded at");
    }

    [Fact]
    public async Task The_applications_own_work_is_recorded_as_the_system()
    {
        // The demonstration's projects were opened by the seeding, which is system work in each tenant, done for
        // the tenant's administrator: the row names the system, and no seat.
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        ChangedBy(await rhea.ProjectDetailAsync(Pier)).Should().Be(("system", null));
        (await rhea.VisibleProjectsAsync()).Should().NotBeEmpty().And.OnlyContain(project => project.GetProperty("changedBy").GetProperty("kind").GetString() == "system");
    }

    [Fact]
    public async Task What_an_operator_tries_is_refused_and_work_done_for_an_operator_is_recorded_as_an_operators()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var orla = await host.ClientAsync("orla", Harbor.Slug);

        // The operator herself changes nothing: she has no seat, and the row keeps saying what it said.
        using (var tried = await orla.PutAsJsonAsync($"/projects/{Pier.Id.Value}/name", new { name = "Renamed by staff" }, Cancellation))
        {
            await tried.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);
        }

        var untouched = await rhea.ProjectDetailAsync(Pier);
        untouched.Text("name").Should().Be("Pier 7");
        ChangedBy(untouched).Should().Be(("system", null));

        // What an operator asked for is carried out by the application's own work in the tenant, which names the
        // operator: the row then says an operator changed it, and no seat.
        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(Harbor.Id, DemoPeople.Orla.Id, scope: "projects"))
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ChangeProjectName(Pier.Id, "Pier 7, as support renamed it"), Cancellation);
        }

        var renamed = await rhea.ProjectDetailAsync(Pier);
        renamed.Text("name").Should().Be("Pier 7, as support renamed it");
        ChangedBy(renamed).Should().Be(("operator", null));
        renamed.GetRawText().Should().NotContain(DemoPeople.Orla.Id.ToString(), "the identity a member of staff signs in with is not a tenant's to read");

        // And the operator reads the same of it.
        var asOperator = (await orla.GetFromJsonAsync<JsonElement>($"/operations/tenants/{Harbor.Id.Value}/projects", Cancellation)).EnumerateArray().Named("Pier 7, as support renamed it");
        ChangedBy(asOperator).Should().Be(("operator", null));
    }

    /// <summary>
    /// The schema says it as the routes do. A project's own field is Projects' and an inspection's is
    /// Inspections', each declared over its module's record, and the gateway offers them as one type: one
    /// fragment reads both, in one request.
    /// </summary>
    [Fact]
    public async Task A_project_and_its_inspections_each_say_who_changed_them_in_one_query()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        var pier = await leo.ProjectNodeIdAsync(Pier);

        // Seeded, so the system's; then Leo renames it, and the mutation's own answer says his seat.
        const string Who = "fragment who on ChangedBy { kind seat }";
        var seeded = (await leo.GraphQLDataAsync("query($id: ID!) { project(id: $id) { changedBy { ...who } } } " + Who, new { id = pier })).GetProperty("project");
        InTheSchema(seeded).Should().Be(("system", null));

        var renamed = (await leo.GraphQLDataAsync(
            "mutation($id: ID!) { projectRename(input: { id: $id, name: \"Pier 7 east\" }) { project { changedBy { ...who } } errors { __typename } } } " + Who,
            new { id = pier })).GetProperty("projectRename");
        renamed.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        InTheSchema(renamed.GetProperty("project")).Should().Be(("seat", Harbor.SeatOf(DemoPeople.Leo).Value));

        using (var recorded = await juno.PostAsJsonAsync($"/projects/{Pier.Id.Value}/inspections", new { title = "Loose railing" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // One request through the gateway: the project's from Projects, its inspection's from Inspections.
        var project = (await juno.GraphQLDataAsync("{ projects { nodes { name changedBy { ...who } inspections { nodes { changedBy { ...who } } } } } } " + Who))
            .GetProperty("projects").GetProperty("nodes").EnumerateArray().Should().ContainSingle().Subject;

        project.GetProperty("name").GetString().Should().Be("Pier 7 east");
        InTheSchema(project).Should().Be(("seat", Harbor.SeatOf(DemoPeople.Leo).Value));
        InTheSchema(project.GetProperty("inspections").GetProperty("nodes").EnumerateArray().Should().ContainSingle().Subject)
            .Should().Be(("seat", Harbor.SeatOf(DemoPeople.Juno).Value));

        // And the route still says it, under its own spelling.
        ChangedBy(await juno.ProjectDetailAsync(Pier)).Should().Be(("seat", Harbor.SeatOf(DemoPeople.Leo).Value));

        static (string? Kind, Guid? Seat) InTheSchema(JsonElement answer)
        {
            var by = answer.GetProperty("changedBy");
            return (by.Text("kind"), by.GetProperty("seat") is { ValueKind: JsonValueKind.String } seat ? seat.GetGuid() : null);
        }
    }

    private static async Task RenameAsync(HttpClient client, string name)
    {
        using var renamed = await client.PutAsJsonAsync($"/projects/{Pier.Id.Value}/name", new { name }, Cancellation);
        renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>Who changed a project or an inspection last, as its answer says it: the kind and, for a seat, its id.</summary>
    private static (string? Kind, Guid? Seat) ChangedBy(JsonElement answer)
    {
        var by = answer.GetProperty("changedBy");
        return (by.Text("kind"), by.GetProperty("seatId") is { ValueKind: JsonValueKind.String } seat ? seat.GetGuid() : null);
    }
}
