using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Projects.Crew;

/// <summary>
/// A crew membership and a crew role each run for a period of their own, and nothing is written when one runs
/// out: the period is compared when the question is asked. So here time passes, and the same questions are
/// asked again.
/// </summary>
/// <remarks>
/// Time passes for real. The application compares a period with its own clock and the database's policies
/// compare the same period with the database's, which no test moves. So a role or a membership is given an end a
/// few seconds ahead (<see cref="Soon"/>), through the route anyone gives one by, and the scenario waits until
/// that moment is past for the database and for the host before it asks again. Nobody changes a row to end a
/// period: what ends it is the clock, as it will be for a real crew.
/// <para>
/// What has to be answered while the period runs is asked first and judged after <see cref="InTime"/> has
/// said it was still running, so a machine too slow for the scenario says that, and not that a rule broke.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class CrewMembershipOverTimeScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    /// <summary>
    /// How far ahead a period ends that a scenario waits out: time for the two or three requests that have to
    /// be answered while it runs, on a machine that runs every other class beside this one, and no longer than
    /// a test can wait.
    /// </summary>
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    private static ProjectRoleId Surveyor => Harbor.ProjectRoles[SampleCatalogue.Surveyor];

    private static ProjectRoleId Observer => Harbor.ProjectRoles[SampleCatalogue.Observer];

    private static ProjectRoleId CrewLead => Harbor.ProjectRoles[SampleCatalogue.CrewLead];

    [Fact]
    public async Task A_crew_role_ends_on_its_own_date_while_the_membership_stays()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        var host = onPostgres.Host;
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        var soon = Now(host) + Soon;

        // Vic is the crew's observer for good, and its surveyor for a few seconds.
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), Surveyor, until: soon))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var whileItRan = await KeyHeldAsync(vic, InspectionKeys.Record);
        InTime(host, soon);
        whileItRan.Should().Be((true, "crew"));

        await onPostgres.WaitUntilItIsPastAsync(soon, Cancellation);

        // Nothing was written when the role ran out: its period is compared when the question is asked. The role is
        // still listed, and counts no longer; the observer's does, and so does the membership.
        (await KeyHeldAsync(vic, InspectionKeys.Record)).Allowed.Should().BeFalse();

        // What the question answers, the command does: he sees the project, and records on it no longer.
        using (var recorded = await vic.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Loose railing" }, Cancellation))
        {
            await recorded.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);
        }

        // Which roles a member holds, and until when, is read by whoever manages the crew: Leo.
        (await KeyHeldAsync(vic, ProjectKeys.View)).Should().Be((true, "crew"));
        var his = (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).Member("Vic");
        his.AppliesNow.Should().BeTrue();
        his.Holding("Surveyor").AppliesNow.Should().BeFalse();
        his.Holding("Surveyor").EndsAt.Should().BeCloseTo(soon, TimeSpan.FromSeconds(1));
        his.Holding("Observer").AppliesNow.Should().BeTrue();
        (await vic.ProjectDetailAsync(PierSeven)).GetProperty("myRoleIds").EnumerateArray().Select(role => role.GetGuid()).Should().Equal(Observer.Value);

        // A role that has run out is given again, and then replaces the one that ran out: a seat holds a role once.
        using (var again = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Vic), Surveyor))
        {
            again.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var held = (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).Member("Vic").Roles;
        held.Select(role => (role.Name, role.AppliesNow, HasAnEnd: role.EndsAt is not null)).Should().Equal(("Observer", true, false), ("Surveyor", true, false));
        (await KeyHeldAsync(vic, InspectionKeys.Record)).Should().Be((true, "crew"));
    }

    [Fact]
    public async Task An_ended_membership_holds_no_role()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        var host = onPostgres.Host;
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", Harbor.Slug);
        var toveSeat = Harbor.SeatOf(DemoPeople.Tove);
        var soon = Now(host) + Soon;

        // Tove is on the crew for a few seconds, and its surveyor for good: the role outlasts the membership.
        using (var added = await leo.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/crew", new { seatId = toveSeat.Value, until = soon }, Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var given = await leo.GiveCrewRoleAsync(PierSeven, toveSeat, Surveyor);
        var whileItRan = await KeyHeldAsync(tove, InspectionKeys.Record);
        InTime(host, soon);
        given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        whileItRan.Should().Be((true, "crew"));

        await onPostgres.WaitUntilItIsPastAsync(soon, Cancellation);

        // The membership has ended, so the project is not there for her, whatever the role's own dates say.
        (await tove.VisibleProjectsAsync()).Names().Should().Equal("Bay bridge");
        using (var opened = await tove.GetAsync($"/projects/{PierSeven.Id.Value}", Cancellation))
        {
            await opened.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        }

        using (var recorded = await tove.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/inspections", new { title = "Loose railing" }, Cancellation))
        {
            await recorded.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        }

        // The lead still sees her on the crew, as someone whose membership, and so whose role, counts no longer.
        var hers = (await leo.WithNamesAsync(await leo.ProjectDetailAsync(PierSeven))).Member("Tove");
        hers.AppliesNow.Should().BeFalse();
        hers.Holding("Surveyor").Should().Match<NamedCrewRole>(role => !role.AppliesNow && role.EndsAt == null);
    }

    [Fact]
    public async Task A_seat_whose_membership_ended_is_put_on_the_crew_again()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        var host = onPostgres.Host;
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", Harbor.Slug);
        var toveSeat = Harbor.SeatOf(DemoPeople.Tove);
        var soon = Now(host) + Soon;

        // Tove is on the crew for a few seconds, and its surveyor for good.
        using (var added = await leo.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/crew", new { seatId = toveSeat.Value, until = soon }, Cancellation))
        {
            added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var given = await leo.GiveCrewRoleAsync(PierSeven, toveSeat, Surveyor))
        {
            InTime(host, soon);
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        await onPostgres.WaitUntilItIsPastAsync(soon, Cancellation);

        // Her time on the crew has ended. A role would not bring her back, since a role counts only while the
        // membership does: giving one is refused, and says she is not on the crew.
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, toveSeat, Observer))
        {
            (await given.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.MemberNotFound)).Argument("Seat").Should().Be(toveSeat.Value.ToString());
        }

        (await tove.VisibleProjectsAsync()).Names().Should().Equal("Bay bridge");

        // She is put on the crew again, and sees the project from then on.
        using (var again = await leo.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/crew", new { seatId = toveSeat.Value }, Cancellation))
        {
            again.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await tove.VisibleProjectsAsync()).Names().Should().Equal("Pier 7", "Bay bridge");
        (await KeyHeldAsync(tove, ProjectKeys.View)).Should().Be((true, "crew"));

        // It is a new membership: the surveyor's role of the one that ended did not come back with it.
        (await KeyHeldAsync(tove, InspectionKeys.Record)).Allowed.Should().BeFalse();
        var hers = (await tove.WithNamesAsync(await tove.ProjectDetailAsync(PierSeven))).Member("Tove");
        hers.AppliesNow.Should().BeTrue();
        hers.Roles.Should().BeEmpty();

        // And on the crew, she is not put on it a second time.
        using var twice = await leo.PostAsJsonAsync($"/projects/{PierSeven.Id.Value}/crew", new { seatId = toveSeat.Value }, Cancellation);
        await twice.ShouldBeRefusedAsync(HttpStatusCode.Conflict, ProjectRefusals.AlreadyOnCrew);
    }

    [Fact]
    public async Task A_lead_role_that_ran_out_no_longer_renames()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        var host = onPostgres.Host;
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        var soon = Now(host) + Soon;

        // Juno leads the crew next to Leo for a few seconds, and renames the project while she does.
        using (var given = await leo.GiveCrewRoleAsync(PierSeven, Harbor.SeatOf(DemoPeople.Juno), CrewLead, until: soon))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var renamed = await juno.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/name", new { name = "Pier 7 east" }, Cancellation))
        {
            InTime(host, soon);
            renamed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        await onPostgres.WaitUntilItIsPastAsync(soon, Cancellation);

        // The role ran out; she is on the crew still, as its surveyor, so the project is there and the key is not.
        using var later = await juno.PutAsJsonAsync($"/projects/{PierSeven.Id.Value}/name", new { name = "Pier 7 west" }, Cancellation);
        (await later.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted)).Argument("Key").Should().Be(ProjectKeys.Edit);
        (await juno.ProjectDetailAsync(PierSeven)).GetProperty("name").GetString().Should().Be("Pier 7 east");
    }

    /// <summary>The moment by the host's own clock, which is the one it compares a period with and stamps a start with.</summary>
    private static DateTimeOffset Now(SampleFactory host) => host.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    /// <summary>
    /// Holds that the period that ends at <paramref name="end"/> is still running for the host: whatever was
    /// asked just before was answered inside it. Called before those answers are judged.
    /// </summary>
    private static void InTime(SampleFactory host, DateTimeOffset end)
        => Now(host).Should().BeBefore(end, "what was asked above has to be answered while the period runs, which gives it {0} seconds", Soon.TotalSeconds);

    /// <summary>Whether Pier 7 gives the client's caller <paramref name="key"/>, and through what, from <c>GET /access/projects/{id}?key=</c>.</summary>
    private static async Task<(bool Allowed, string? Via)> KeyHeldAsync(HttpClient client, string key)
    {
        var answer = await client.GetFromJsonAsync<JsonElement>($"/access/projects/{PierSeven.Id.Value}?key={key}", Cancellation);
        return (answer.GetProperty("allowed").GetBoolean(), answer.Text("via"));
    }
}
