using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects;

/// <summary>
/// Projects' refusals that no scenario elsewhere runs into, each with the status its kind maps to, its code and
/// the argument it carries: a number taken or blank, a blank name, a planned range that is none, a seat that is
/// on the crew already, is not on it, is suspended or owns the project already, and a project reopened while it is
/// open.
/// </summary>
/// <remarks>
/// Every call is refused and changes nothing, so the class shares one host.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class ProjectRefusalScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static readonly DemoTenant Harbor = DemoData.Harbor;
    private static readonly Guid PierSeven = Harbor.ProjectNamed("Pier 7").Id.Value;

    private static readonly Dictionary<string, Attempted> Attempts = new()
    {
        ["ada opens a project with Pier 7's number"] = new(
            "ada", HttpMethod.Post, "/projects",
            new { number = "P-001", name = "Pier 7 again", unitId = Harbor.UnitNamed("North Coast").Value },
            HttpStatusCode.Conflict, ProjectRefusals.NumberTaken, ("Number", "P-001")),
        ["ada opens a project with a blank number"] = new(
            "ada", HttpMethod.Post, "/projects",
            new { number = "  ", name = "Harbor wall", unitId = Harbor.UnitNamed("North").Value },
            HttpStatusCode.BadRequest, ProjectRefusals.NumberInvalid, ("Max", "40")),
        ["leo renames Pier 7 to nothing"] = new(
            "leo", HttpMethod.Put, $"/projects/{PierSeven}/name",
            new { name = "" },
            HttpStatusCode.BadRequest, ProjectRefusals.NameInvalid, ("Max", "200")),
        ["leo plans Pier 7 until the day before it starts"] = new(
            "leo", HttpMethod.Put, $"/projects/{PierSeven}/planned-range",
            new { from = "2026-10-05", until = "2026-10-04" },
            HttpStatusCode.BadRequest, ProjectRefusals.PlannedRangeInvalid, null),
        ["leo plans Pier 7 from a day, until none"] = new(
            "leo", HttpMethod.Put, $"/projects/{PierSeven}/planned-range",
            new { from = "2026-10-05" },
            HttpStatusCode.BadRequest, ProjectRefusals.PlannedRangeInvalid, null),
        ["leo puts juno on Pier 7's crew again"] = new(
            "leo", HttpMethod.Post, $"/projects/{PierSeven}/crew",
            new { seatId = Harbor.SeatOf(DemoPeople.Juno).Value },
            HttpStatusCode.Conflict, ProjectRefusals.AlreadyOnCrew, ("Seat", Harbor.SeatOf(DemoPeople.Juno).Value.ToString())),
        ["leo takes rhea, who is not on it, off Pier 7's crew"] = new(
            "leo", HttpMethod.Delete, $"/projects/{PierSeven}/crew/{Harbor.SeatOf(DemoPeople.Rhea).Value}",
            null,
            HttpStatusCode.NotFound, ProjectRefusals.MemberNotFound, ("Seat", Harbor.SeatOf(DemoPeople.Rhea).Value.ToString())),
        ["leo puts seth, whose seat is suspended, on Pier 7's crew"] = new(
            "leo", HttpMethod.Post, $"/projects/{PierSeven}/crew",
            new { seatId = Harbor.SeatOf(DemoPeople.Seth).Value },
            HttpStatusCode.Conflict, ProjectRefusals.SeatNotActive, ("Seat", Harbor.SeatOf(DemoPeople.Seth).Value.ToString())),
        ["rhea names leo, the owner, owner of Pier 7"] = new(
            "rhea", HttpMethod.Put, $"/projects/{PierSeven}/owner",
            new { seatId = Harbor.SeatOf(DemoPeople.Leo).Value },
            HttpStatusCode.Conflict, ProjectRefusals.AlreadyOwner, null),
        ["leo reopens Pier 7, which is open"] = new(
            "leo", HttpMethod.Post, $"/projects/{PierSeven}/reopen",
            null,
            HttpStatusCode.Conflict, ProjectRefusals.NotClosed, null),
    };

    public static TheoryData<string> Names => new(Attempts.Keys);

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Each_project_refusal_has_its_status_and_code(string name)
    {
        var attempt = Attempts[name];
        using var client = await sample.ClientAsync(attempt.Person, Harbor.Slug);
        using var request = new HttpRequestMessage(attempt.Method, attempt.Path)
        {
            Content = attempt.Body is null ? null : JsonContent.Create(attempt.Body),
        };

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var refused = await response.ShouldBeRefusedAsync(attempt.Status, attempt.Code);
        if (attempt.Argument is { } expected)
        {
            refused.Argument(expected.Name).Should().Be(expected.Value);
        }
    }

    /// <summary>One call and the refusal it earns, with one of the refusal's arguments when it carries any.</summary>
    private sealed record Attempted(
        string Person,
        HttpMethod Method,
        string Path,
        object? Body,
        HttpStatusCode Status,
        string Code,
        (string Name, string Value)? Argument);
}
