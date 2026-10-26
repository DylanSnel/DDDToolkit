using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Ownership;

/// <summary>
/// Opening a project for an owner the command names: the permission that opens a project at the unit, and of the owner, the
/// caller's own seat or the organization's permission to name somebody else, all of it at the door. A seat that may open
/// projects and names nobody else opens one; naming somebody else without the second permission is one <c>access.refused</c>
/// with both reasons, over REST and in the GraphQL mutation alike.
/// </summary>
/// <remarks>
/// Nobody of the demonstration holds the permission that opens a project without the permission that names an owner, so Ada makes a
/// role of the organization's that opens projects alone and gives it to Juno at North Coast. The class's hosts run on
/// Supabase's own Postgres image, with the exported policies and privileges under the application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class OpeningForAnOwnerScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Open =
        $$"""mutation($unit: UUID!, $owner: UUID) { projectOpenAtUnit(input: { number: "P-902", name: "Quay steps", unitId: $unit, ownerSeat: $owner }) { project { id owner { displayName } } {{SampleGraphQLCalls.Errors}} } }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task A_seat_that_may_open_projects_names_itself_the_owner_and_nobody_else_without_the_permission_to_name_one()
    {
        await using var host = await sample.StartAsync();
        var northCoast = Harbor.UnitNamed("North Coast").Value;
        var juno = Harbor.SeatOf(DemoPeople.Juno).Value;
        var leo = Harbor.SeatOf(DemoPeople.Leo).Value;

        using (var ada = await host.ClientAsync("ada", Harbor.Slug))
        {
            using var made = await ada.PostAsJsonAsync("/tenancy/roles", new { name = "Opener", permissions = new[] { ProjectPermissionNames.View, ProjectPermissionNames.Open } }, Cancellation);
            made.StatusCode.Should().Be(HttpStatusCode.Created, await made.Content.ReadAsStringAsync(Cancellation));
            var opener = (await made.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();
            using var given = await ada.PostAsJsonAsync($"/tenancy/seats/{juno}/grants", new { unitId = northCoast, roleId = opener }, Cancellation);
            given.StatusCode.Should().Be(HttpStatusCode.NoContent, await given.Content.ReadAsStringAsync(Cancellation));
        }

        using var asJuno = await host.ClientAsync("juno", Harbor.Slug);

        // Leo as the owner: his seat is not hers, and she holds no permission to name somebody else. One refusal, both reasons,
        // the caller's own first, then the permission's, each with its own code.
        using (var refused = await asJuno.PostAsJsonAsync("/projects", new { number = "P-901", name = "Quay wall", unitId = northCoast, ownerSeat = leo }, Cancellation))
        {
            (await refused.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ToolkitRefusals.Refused)).Reasons
                .Should().Equal([ToolkitRefusals.NotOwn, Examples.Tenancy.Projects.Domain.Aggregates.Projects.ProjectRefusals.NotPermitted], "the owner is asked about at the door, not by the handler");
        }

        var payload = (await asJuno.GraphQLDataAsync(Open, new { unit = northCoast, owner = leo })).GetProperty("projectOpenAtUnit");
        payload.GetProperty("project").ValueKind.Should().Be(JsonValueKind.Null);
        payload.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which.Text("code").Should().Be(ToolkitRefusals.Refused);

        // Herself as the owner, by her own seat, and nobody named at all, which is herself too.
        using (var opened = await asJuno.PostAsJsonAsync("/projects", new { number = "P-901", name = "Quay wall", unitId = northCoast, ownerSeat = juno }, Cancellation))
        {
            opened.StatusCode.Should().Be(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Cancellation));
        }

        var mine = (await asJuno.GraphQLDataAsync(Open, new { unit = northCoast, owner = (Guid?)null })).GetProperty("projectOpenAtUnit");
        mine.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        mine.GetProperty("project").GetProperty("owner").Text("displayName").Should().Be(DemoPeople.Juno.Name, "nobody named is the caller");
    }
}
