using System.Net;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Crew;

/// <summary>
/// A seat deactivated in the Tenants module leaves the crews it is on. Tenancy raises <c>SeatDeactivated</c>, the Tenants
/// module's outbox hands it to the toolkit's mediator in the background, and Projects' handler takes the seat off every
/// open crew it does not own.
/// </summary>
/// <remarks>
/// The test changes a seat, so it makes a host of its own. Its host runs on Supabase's own Postgres image, with the
/// exported policies and privileges under the application's own checks, so the handler's work passes the database's
/// checks as the module's system work in the tenant.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class DeactivatedSeatScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task A_deactivated_seat_leaves_the_crew_of_a_project_it_does_not_own()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        await Outboxes.DrainedAsync(host, Cancellation);

        var juno = Harbor.SeatOf(DemoPeople.Juno).Value;
        (await CrewOfPierSevenAsync(leo)).Should().Contain(juno, "Juno is on Pier 7's crew");

        using (var deactivated = await rhea.PostAsync($"/tenancy/seats/{juno}/deactivate", content: null, Cancellation))
        {
            deactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // The deactivation commits first. The Tenants module's poller then delivers the event, and Projects' handler
        // saves the crew as its own work, which leaves an event in Projects' outbox for that poller to deliver.
        await Outboxes.DrainedAsync(host, Cancellation);

        (await CrewOfPierSevenAsync(leo)).Should().Equal(
            [Harbor.SeatOf(DemoPeople.Leo).Value, Harbor.SeatOf(DemoPeople.Vic).Value],
            "Juno left the crew, and its owner and Vic stay on it");
    }

    private static async Task<IReadOnlyList<Guid>> CrewOfPierSevenAsync(HttpClient client)
        => [.. (await client.CrewAsync(PierSeven)).EnumerateArray().Select(member => member.GetProperty("seatId").GetGuid())];
}
