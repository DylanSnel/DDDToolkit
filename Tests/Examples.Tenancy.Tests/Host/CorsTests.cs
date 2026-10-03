using System.Net;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// A browser application on another origin may call the API only when <c>Sample:Cors:Origins</c> lists it, and then
/// with the headers a client of this API sends.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class CorsTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Browser = "https://app.example.test";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_preflight_from_a_configured_origin_allows_the_tenant_header()
    {
        await using var host = await WithOriginsAsync(Browser);
        using var client = host.CreateClient();

        using var answer = await client.SendAsync(Preflight(Browser, "PUT", $"authorization,{TenantHeader.Name},if-match,content-type"), Cancellation);

        answer.StatusCode.Should().Be(HttpStatusCode.NoContent, "the browser asks before it sends, and is answered without a token");
        answer.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(Browser);
        string.Join(",", answer.Headers.GetValues("Access-Control-Allow-Headers")).Split(',')
            .Should().Contain(header => string.Equals(header.Trim(), TenantHeader.Name, StringComparison.OrdinalIgnoreCase))
            .And.Contain(header => string.Equals(header.Trim(), "If-Match", StringComparison.OrdinalIgnoreCase));

        // And an answer says which of its headers the browser may hand to the page: the version, and where a new thing is.
        using var rhea = await host.ClientAsync("rhea", DemoData.Harbor.Slug);
        using var asked = new HttpRequestMessage(HttpMethod.Get, $"/projects/{DemoData.Harbor.ProjectNamed("Pier 7").Id.Value}");
        asked.Headers.Add("Origin", Browser);
        using var project = await rhea.SendAsync(asked, Cancellation);

        project.StatusCode.Should().Be(HttpStatusCode.OK);
        string.Join(",", project.Headers.GetValues("Access-Control-Expose-Headers")).Should().Contain("ETag").And.Contain("Location");
    }

    [Fact]
    public async Task A_preflight_from_another_origin_is_not_allowed()
    {
        await using var host = await WithOriginsAsync(Browser);
        using var client = host.CreateClient();

        using var answer = await client.SendAsync(Preflight("https://elsewhere.example.test", "GET", "authorization"), Cancellation);

        answer.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Without_origins_there_is_no_cors()
    {
        // The host the class shares is given no origin, as a host is unless its settings list one.
        using var client = (await sample.SharedAsync()).CreateClient();

        using var answer = await client.SendAsync(Preflight(Browser, "GET", "authorization"), Cancellation);

        answer.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        answer.StatusCode.Should().NotBe(HttpStatusCode.NoContent, "nothing answers a preflight when no origin is configured");
    }

    private Task<SampleFactory> WithOriginsAsync(params string[] origins)
        => sample.StartAsync(
            settings: origins.Select((origin, index) => (Key: $"{BrowserCors.OriginsSetting}:{index}", origin)).ToDictionary(pair => pair.Key, pair => pair.origin));

    private static HttpRequestMessage Preflight(string origin, string method, string headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/projects");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", headers);
        return request;
    }
}
