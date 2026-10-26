using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Operators;

/// <summary>
/// What an operator reads of the projects through the GraphQL schema: every project of the tenant it names, as
/// the route answers them. An operator has no seat, so the field asks for an operator where every other asks for
/// a seat, and refuses anybody else with the code the route gives.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks. An operator's query runs there as the operators' own database role.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class TenantProjectsFieldScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Projects =
        "query($tenant: UUID!) { tenantProjects(tenant: $tenant) { id number name unitId state ownerSeat planned { from until } changedBy { kind seat } } }";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task An_operator_reads_the_projects_of_the_tenant_it_names_as_the_route_answers_them()
    {
        // No tenant is selected: an operator works in none, and names the one it reads.
        using var orla = await sample.ClientAsync("orla", tenant: null);

        foreach (var tenant in new[] { Harbor, Meadow })
        {
            var projects = (await orla.GraphQLDataAsync(Projects, new { tenant = tenant.Id.Value })).GetProperty("tenantProjects").EnumerateArray().ToList();
            var byRoute = (await orla.GetFromJsonAsync<JsonElement>($"/operations/tenants/{tenant.Id.Value}/projects", Cancellation)).EnumerateArray().ToList();

            // The route's projects in the route's order, each with what the route says of it. The id alone is
            // spelled differently: a node id here, as in every schema that names a project.
            projects.Select(Told).Should().Equal(byRoute.Select(project => (
                project.Text("number"),
                project.Text("name"),
                project.GetProperty("unitId").GetGuid(),
                project.Text("state"),
                project.GetProperty("ownerSeat").GetGuid(),
                project.Text("plannedFrom"),
                project.Text("plannedUntil"),
                project.GetProperty("changedBy").Text("kind"),
                project.GetProperty("changedBy").Text("seatId"))));
            projects.Select(project => project.Text("number")).Should().Equal(tenant.Projects.Select(project => project.Number).Order(StringComparer.Ordinal));
        }

        // The id is the one a seat of the tenant is given for the same project.
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);
        var pier = Harbor.ProjectNamed("Pier 7");
        (await orla.GraphQLDataAsync(Projects, new { tenant = Harbor.Id.Value })).GetProperty("tenantProjects").EnumerateArray().Named(pier.Name).Text("id")
            .Should().Be(await ada.ProjectNodeIdAsync(pier));

        // A tenant that does not exist has no projects.
        (await orla.GraphQLDataAsync(Projects, new { tenant = Guid.NewGuid() })).GetProperty("tenantProjects").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_seat_is_no_operator_whatever_it_holds()
    {
        // Harbor's first administrator, who holds every key there is in her tenant, asking about her own tenant.
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        var refused = (await ada.GraphQLAsync(Projects, new { tenant = Harbor.Id.Value })).SingleError();

        refused.Code().Should().Be(TenancyRefusals.OperatorsOnly);
        refused.Kind().Should().Be("not_permitted");
    }

    /// <summary>What the field says of a project, in the order the route's answer is read above: its plan is one range here, and two dates there.</summary>
    private static (string? Number, string? Name, Guid Unit, string? State, Guid Owner, string? From, string? Until, string? ChangedByKind, string? ChangedBySeat) Told(JsonElement project) => (
        project.Text("number"),
        project.Text("name"),
        project.GetProperty("unitId").GetGuid(),
        project.Text("state"),
        project.GetProperty("ownerSeat").GetGuid(),
        project.GetProperty("planned") is { ValueKind: JsonValueKind.Object } planned ? planned.Text("from") : null,
        project.GetProperty("planned") is { ValueKind: JsonValueKind.Object } range ? range.Text("until") : null,
        project.GetProperty("changedBy").Text("kind"),
        project.GetProperty("changedBy").Text("seat"));
}
