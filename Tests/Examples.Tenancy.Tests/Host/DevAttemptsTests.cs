using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// The try-it presets, served by <c>GET /dev/attempts</c>, each run exactly as the UI runs it: read from the API,
/// signed in as its person through the dev login, sent with or without the token and the <c>Tenant</c> header as
/// it says, and judged by its status and its problem's code.
/// </summary>
/// <remarks>
/// Every preset is a refusal or only reads, so they share one host: whatever order they run in, none changes what
/// another sees.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class DevAttemptsTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Presets => new(DevAttempts.All.Select(attempt => attempt.Id));

    [Theory]
    [MemberData(nameof(Presets))]
    public async Task Every_preset_answers_its_expected_status_and_code(string id)
    {
        var host = await sample.SharedAsync();
        var attempt = (await ServedAsync(host)).Single(candidate => candidate.GetProperty("id").GetString() == id);

        using var response = await RunAsync(host, attempt);

        var expectedStatus = (HttpStatusCode)attempt.GetProperty("expectedStatus").GetInt32();
        var problem = await response.ProblemAsync();
        response.StatusCode.Should().Be(expectedStatus, "'{0}' was answered {1}", attempt.GetProperty("title").GetString(), problem.Body.GetRawText());
        problem.Code.Should().Be(attempt.GetProperty("expectedCode").GetString());
    }

    [Fact]
    public async Task The_presets_are_served_with_every_id_filled_in()
    {
        var served = await ServedAsync(await sample.SharedAsync());

        served.Select(attempt => attempt.GetProperty("id").GetString()).Should().Equal(DevAttempts.All.Select(attempt => attempt.Id));
        foreach (var attempt in served)
        {
            var path = attempt.GetProperty("path").GetString();
            path.Should().StartWith("/").And.NotContain("{", "a client sends the path as it is");
            DemoPeople.Find(attempt.GetProperty("person").GetString()).Should().NotBeNull("a preset runs as one of the demonstration people");
        }
    }

    [Fact]
    public async Task Running_every_preset_changes_nothing()
    {
        await using var host = await sample.StartAsync();
        var before = await SnapshotAsync(host);

        foreach (var attempt in await ServedAsync(host))
        {
            using var response = await RunAsync(host, attempt);
            (attempt.GetProperty("method").GetString() == "GET" || (int)response.StatusCode >= 400)
                .Should().BeTrue("'{0}' is a refusal, or only reads", attempt.GetProperty("id").GetString());
        }

        (await SnapshotAsync(host)).Should().Equal(before);
    }

    /// <summary>The presets as the API serves them, as the UI reads them.</summary>
    private static async Task<IReadOnlyList<JsonElement>> ServedAsync(SampleFactory host)
    {
        using var anonymous = host.CreateClient();
        var attempts = await anonymous.GetFromJsonAsync<JsonElement>("/dev/attempts", Cancellation);
        return [.. attempts.EnumerateArray()];
    }

    /// <summary>
    /// Sends one preset the way the UI does: the person's token from the dev login when it says so, the tenant
    /// header when it says so, the body as it is.
    /// </summary>
    private static async Task<HttpResponseMessage> RunAsync(SampleFactory host, JsonElement attempt)
    {
        using var request = new HttpRequestMessage(new HttpMethod(attempt.GetProperty("method").GetString()!), attempt.GetProperty("path").GetString());

        if (attempt.GetProperty("sendToken").GetBoolean())
        {
            var token = await host.SignInAsync(attempt.GetProperty("person").GetString()!);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (attempt.GetProperty("sendTenant").GetBoolean())
        {
            request.Headers.Add(TenantHeader.Name, attempt.GetProperty("tenant").GetString());
        }

        if (attempt.GetProperty("body") is { ValueKind: JsonValueKind.Object } body)
        {
            request.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        }

        using var client = host.CreateClient();
        return await client.SendAsync(request, Cancellation);
    }

    /// <summary>What the presets would change if one of them got through: people, grants, crews, projects.</summary>
    private static async Task<IReadOnlyList<string>> SnapshotAsync(SampleFactory host)
    {
        using var ada = await host.ClientAsync("ada", DemoData.Harbor.Slug);
        using var leo = await host.ClientAsync("leo", DemoData.Harbor.Slug);
        using var toveInHarbor = await host.ClientAsync("tove", DemoData.Harbor.Slug);
        using var toveInMeadow = await host.ClientAsync("tove", DemoData.Meadow.Slug);
        using var hana = await host.ClientAsync("hana", DemoData.Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", DemoData.Harbor.Slug);
        using var maud = await host.ClientAsync("maud", DemoData.Harbor.Slug);

        return
        [
            await ada.GetStringAsync("/tenancy/seats", Cancellation),
            await ada.GetStringAsync("/tenancy/roles", Cancellation),
            await maud.GetStringAsync("/me", Cancellation),
            await ada.GetStringAsync("/me", Cancellation),
            await ada.GetStringAsync("/projects", Cancellation),
            await leo.GetStringAsync("/me", Cancellation),
            await hana.GetStringAsync("/me", Cancellation),
            await rhea.GetStringAsync("/me", Cancellation),
            await toveInHarbor.GetStringAsync("/me", Cancellation),
            await toveInMeadow.GetStringAsync("/me", Cancellation),
            await toveInMeadow.GetStringAsync("/projects", Cancellation),
        ];
    }
}
