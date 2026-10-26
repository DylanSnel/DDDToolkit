using System.Net;
using System.Text;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// The seat a route inside a tenant requires is an authorization policy of the host, <see cref="SamplePolicies.SeatRequired"/>:
/// a request without a seat is answered before the route's arguments are read, with the refusal its caller
/// resolved to, as problem+json with its code.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class SeatPolicyScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task The_seat_policy_answers_tenant_required_and_not_seated_with_their_codes()
    {
        // No tenant named: which tenant is the first thing a request inside one says.
        using var nowhere = await sample.ClientAsync("rhea", tenant: null);
        using (var response = await nowhere.GetAsync("/projects", Cancellation))
        {
            await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.TenantRequired);
        }

        // Rhea has no seat in meadow, and Seth's seat in harbor is suspended: each is told which.
        using var elsewhere = await sample.ClientAsync("rhea", DemoData.Meadow.Slug);
        using (var response = await elsewhere.GetAsync("/projects", Cancellation))
        {
            await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);
        }

        using var seth = await sample.ClientAsync("seth", Harbor.Slug);
        using (var response = await seth.GetAsync("/projects", Cancellation))
        {
            await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.SeatSuspended);
        }
    }

    [Fact]
    public async Task A_malformed_request_without_a_seat_is_403_not_seated_and_with_one_400_invalid_request()
    {
        var pier = Harbor.ProjectNamed("Pier 7").Id.Value;
        using var outsider = await sample.ClientAsync("rhea", DemoData.Meadow.Slug);
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        // A body without the unit it needs, and an id that is no id. Authorization runs before a route's arguments
        // are read, so whoever has no seat is told that, and learns nothing about what the route would have read.
        foreach (var (method, path, body) in new[]
                 {
                     (HttpMethod.Post, "/projects", """{"number":"P-100","name":"Harbor wall"}"""),
                     (HttpMethod.Put, $"/projects/{pier}/owner", "{}"),
                     (HttpMethod.Get, "/projects/not-a-project", null),
                 })
        {
            using var unseated = await outsider.SendAsync(Request(method, path, body), Cancellation);
            await unseated.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);

            using var seated = await rhea.SendAsync(Request(method, path, body), Cancellation);
            await seated.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
        }
    }

    [Fact]
    public async Task Seats_of_mine_needs_a_token_but_no_seat()
    {
        using var anonymous = (await sample.SharedAsync()).Client(token: null, tenant: null);
        using (var response = await anonymous.GetAsync("/me/seats", Cancellation))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // Picking a tenant comes before being in one: with a token and no tenant, the list is answered.
        using var rhea = await sample.ClientAsync("rhea", tenant: null);
        using var mine = await rhea.GetAsync("/me/seats", Cancellation);
        mine.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_request_without_a_token_is_401_whatever_tenant_it_names()
    {
        // No token is not "no seat": the caller is challenged, and is not told about seats at all.
        using var anonymous = (await sample.SharedAsync()).Client(token: null, tenant: Harbor.Slug);
        using var response = await anonymous.GetAsync("/projects", Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_route_inside_a_tenant_asks_for_the_seat_policy()
    {
        var host = await sample.SharedAsync();
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        // The routes of SeatedRoutes.InTenant are held to what the host maps elsewhere. Here: each of them carries the
        // policy, and the one route that needs only a token does not.
        var policies = endpoints.ToLookup(
            endpoint => endpoint.RoutePattern.RawText,
            endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ToList());

        foreach (var route in SeatedRoutes.InTenant)
        {
            var template = route.Template.Split(' ')[1].Split('?')[0];
            policies[template].Should().NotBeEmpty("{0} is mapped", route.Template)
                .And.OnlyContain(asked => asked.Contains(SamplePolicies.SeatRequired), "{0} works inside a tenant", route.Template);
        }

        policies["/me/seats"].Should().ContainSingle().Which.Should().NotContain(SamplePolicies.SeatRequired);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? body)
        => new(method, path) { Content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json") };
}
