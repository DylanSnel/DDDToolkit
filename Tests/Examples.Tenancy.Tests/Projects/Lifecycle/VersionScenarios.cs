using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Projects.Lifecycle;

/// <summary>
/// A project answers with its version, and a change may name the version its caller read: a change decided on an
/// older reading is refused instead of made over what happened since. Without a version the last change wins.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class VersionScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoProject PierSeven => Harbor.ProjectNamed("Pier 7");

    [Fact]
    public async Task The_project_detail_sends_its_version_as_etag()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        using var response = await leo.GetAsync($"/projects/{PierSeven.Id.Value}", Cancellation);
        var version = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("version").GetInt64();

        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.IsWeak.Should().BeFalse("the version says exactly which project was read");
        response.Headers.ETag.Tag.Should().Be($"\"{version}\"");

        // Every change moves it on: the crew's as much as the project's own.
        (await RenameAsync(leo, "Pier 7 east")).Should().Be(HttpStatusCode.NoContent);
        (await VersionAsync(leo)).Should().BeGreaterThan(version);
    }

    [Fact]
    public async Task A_rename_with_a_stale_if_match_is_409_concurrency_conflict_and_changes_nothing()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var read = await VersionAsync(leo);

        // Leo and Rhea both read the project. Rhea renames it first, with the version she read.
        (await RenameAsync(rhea, "Pier 7 east", read)).Should().Be(HttpStatusCode.NoContent);

        // Leo's change was decided on what he read, which is no longer what is there.
        using (var stale = await SendRenameAsync(leo, "Pier 7 west", $"\"{read}\""))
        {
            await stale.ShouldBeRefusedAsync(HttpStatusCode.Conflict, RefusalProblems.ConcurrencyConflict);
        }

        (await leo.ProjectDetailAsync(PierSeven)).GetProperty("name").GetString().Should().Be("Pier 7 east");

        // He reads again, and decides again.
        (await RenameAsync(leo, "Pier 7 west", await VersionAsync(leo))).Should().Be(HttpStatusCode.NoContent);
        (await leo.ProjectDetailAsync(PierSeven)).GetProperty("name").GetString().Should().Be("Pier 7 west");
    }

    [Fact]
    public async Task A_caller_without_access_learns_nothing_from_a_version()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        var current = await VersionAsync(leo);

        // Vic sees Pier 7 and may not rename it; Hana does not see it. Whatever version either sends, the right one
        // or a wrong one, the answer is the one about access: a conflict would tell them which version it is at.
        foreach (var version in new[] { current, current + 5 })
        {
            using var seen = await SendRenameAsync(vic, "Pier 7 east", $"\"{version}\"");
            await seen.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ProjectRefusals.NotPermitted);

            using var unseen = await SendRenameAsync(hana, "Pier 7 east", $"\"{version}\"");
            await unseen.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        }
    }

    [Fact]
    public async Task Without_a_version_the_last_write_wins_as_before()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);

        (await RenameAsync(rhea, "Pier 7 east")).Should().Be(HttpStatusCode.NoContent);
        (await RenameAsync(leo, "Pier 7 west")).Should().Be(HttpStatusCode.NoContent);

        (await leo.ProjectDetailAsync(PierSeven)).GetProperty("name").GetString().Should().Be("Pier 7 west");
    }

    [Theory]
    [InlineData("W/\"1\"")]
    [InlineData("1")]
    [InlineData("*")]
    [InlineData("\"one\"")]
    [InlineData("\"1\", \"2\"")]
    public async Task A_malformed_if_match_is_400_invalid_request(string header)
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        using var response = await SendRenameAsync(leo, "Pier 7 east", header);

        await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
        (await leo.ProjectDetailAsync(PierSeven)).GetProperty("name").GetString().Should().Be("Pier 7");
    }

    [Fact]
    public async Task A_change_to_the_crew_takes_the_version_too()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var read = await VersionAsync(leo);
        (await RenameAsync(leo, "Pier 7 east")).Should().Be(HttpStatusCode.NoContent);

        // The crew is part of the project: a member added on an older reading of it is a lost race as well.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/projects/{PierSeven.Id.Value}/crew")
        {
            Content = JsonContent.Create(new { seatId = Harbor.SeatOf(DemoPeople.Tove).Value }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{read}\"");
        using var stale = await leo.SendAsync(request, Cancellation);

        await stale.ShouldBeRefusedAsync(HttpStatusCode.Conflict, RefusalProblems.ConcurrencyConflict);
        (await leo.CrewAsync(PierSeven)).GetArrayLength().Should().Be(3, "nobody was added");
    }

    [Fact]
    public async Task Planning_takes_the_version_too()
    {
        await using var host = await sample.StartAsync();
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var read = await VersionAsync(leo);
        (await RenameAsync(leo, "Pier 7 east")).Should().Be(HttpStatusCode.NoContent);

        // The days were picked on a reading of the project that is no longer what is there.
        using (var stale = await SendPlanAsync(leo, read))
        {
            await stale.ShouldBeRefusedAsync(HttpStatusCode.Conflict, RefusalProblems.ConcurrencyConflict);
        }

        (await leo.ProjectDetailAsync(PierSeven)).GetProperty("plannedFrom").ValueKind.Should().Be(JsonValueKind.Null, "nothing was planned");

        // With the version it is at now, it is planned, and that moves the version on.
        var current = await VersionAsync(leo);
        using (var planned = await SendPlanAsync(leo, current))
        {
            planned.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await leo.ProjectDetailAsync(PierSeven)).GetProperty("plannedFrom").GetString().Should().Be("2026-10-05");
        (await VersionAsync(leo)).Should().BeGreaterThan(current);
    }

    private static async Task<long> VersionAsync(HttpClient client)
        => (await client.ProjectDetailAsync(PierSeven)).GetProperty("version").GetInt64();

    private static async Task<HttpStatusCode> RenameAsync(HttpClient client, string name, long? expected = null)
    {
        using var response = await SendRenameAsync(client, name, expected is { } version ? $"\"{version}\"" : null);
        return response.StatusCode;
    }

    private static Task<HttpResponseMessage> SendPlanAsync(HttpClient client, long expected)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/projects/{PierSeven.Id.Value}/planned-range")
        {
            Content = JsonContent.Create(new { from = "2026-10-05", until = "2026-10-30" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expected}\"");

        return client.SendAsync(request, Cancellation);
    }

    private static Task<HttpResponseMessage> SendRenameAsync(HttpClient client, string name, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/projects/{PierSeven.Id.Value}/name") { Content = JsonContent.Create(new { name }) };
        if (ifMatch is not null)
        {
            // As a client would write it, valid or not: the API reads the header itself.
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, Cancellation);
    }
}
