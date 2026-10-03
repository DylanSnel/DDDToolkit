using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Inspections.Operators;

/// <summary>
/// What an operator reads of the inspections through the GraphQL schema: the inspections of one project of the
/// tenant it names, as the route answers them. An operator has no seat, so the field asks for an operator where
/// every other asks for a seat, and refuses anybody else with the code the route gives.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks. An operator's query runs there as the operators' own database role.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class TenantProjectInspectionsFieldScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Projects = "query($tenant: UUID!) { tenantProjects(tenant: $tenant) { id name } }";

    private const string Inspections =
        """
        query($tenant: UUID!, $project: ID!) {
          tenantProjectInspections(tenant: $tenant, project: $project) { id title days { from until } recordedAt recordedBy { id } changedBy { kind seat } }
        }
        """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task An_operator_reads_a_projects_inspections_under_the_tenant_it_names_as_the_route_answers_them()
    {
        await using var host = await sample.StartAsync();
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        using var orla = await host.ClientAsync("orla", tenant: null);
        var pier = Harbor.ProjectNamed("Pier 7");

        foreach (var title in new[] { "Loose railing", "Cracked plank" })
        {
            using var recorded = await juno.PostAsJsonAsync($"/projects/{pier.Id.Value}/inspections", new { title }, Cancellation);
            recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // The project's id, as the operators' list of the tenant's projects gives it.
        var project = (await orla.GraphQLDataAsync(Projects, new { tenant = Harbor.Id.Value })).GetProperty("tenantProjects").EnumerateArray().Named(pier.Name).Text("id");

        var inspections = (await orla.GraphQLDataAsync(Inspections, new { tenant = Harbor.Id.Value, project })).GetProperty("tenantProjectInspections").EnumerateArray().ToList();
        var byRoute = (await orla.GetFromJsonAsync<JsonElement>($"/operations/tenants/{Harbor.Id.Value}/projects/{pier.Id.Value}/inspections", Cancellation)).EnumerateArray().ToList();

        // The route's inspections in the route's order, newest first, each with what the route says of it. The
        // seat that recorded it is its id: its name is Tenancy's to say, inside a tenant.
        inspections.Select(inspection => (
            inspection.GetProperty("id").GetGuid(),
            inspection.Text("title"),
            inspection.GetProperty("days").Text("from"),
            inspection.GetProperty("days").Text("until"),
            inspection.GetProperty("recordedAt").GetDateTimeOffset(),
            inspection.GetProperty("recordedBy").GetProperty("id").GetGuid(),
            inspection.GetProperty("changedBy").Text("kind"),
            inspection.GetProperty("changedBy").Text("seat")))
            .Should().Equal(byRoute.Select(inspection => (
                inspection.GetProperty("id").GetGuid(),
                inspection.Text("title"),
                inspection.Text("from"),
                inspection.Text("until"),
                inspection.GetProperty("recordedAt").GetDateTimeOffset(),
                inspection.GetProperty("recordedBy").GetGuid(),
                inspection.GetProperty("changedBy").Text("kind"),
                inspection.GetProperty("changedBy").Text("seatId"))));
        inspections.Select(inspection => inspection.Text("title")).Should().Equal("Cracked plank", "Loose railing");
        inspections.Should().OnlyContain(inspection => inspection.GetProperty("recordedBy").GetProperty("id").GetGuid() == Harbor.SeatOf(DemoPeople.Juno).Value);

        // Under another tenant the project has none.
        (await orla.GraphQLDataAsync(Inspections, new { tenant = Meadow.Id.Value, project })).GetProperty("tenantProjectInspections").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_seat_is_no_operator_whatever_it_holds()
    {
        // Harbor's first administrator, who sees the project and its inspections as a seat, through the project.
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        var project = await ada.ProjectNodeIdAsync(Harbor.ProjectNamed("Pier 7"));

        var refused = (await ada.GraphQLAsync(Inspections, new { tenant = Harbor.Id.Value, project })).SingleError();

        refused.Code().Should().Be(TenancyRefusals.OperatorsOnly);
        refused.Kind().Should().Be("not_permitted");
    }
}
