using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// Tenants stay apart, and one person moves between their seats in two tenants: the tenant a request names is
/// resolved against the caller's own seats, and nothing else lets anyone in.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class TenantSeparationScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> RoutesThatNeedAToken => SeatedRoutes.Templates(SeatedRoutes.All);

    [Fact]
    public async Task Tove_sees_Bay_bridge_in_harbor_and_Garden_shed_in_meadow()
    {
        using var inHarbor = await sample.ClientAsync("tove", DemoData.Harbor.Slug);
        using var inMeadow = await sample.ClientAsync("tove", DemoData.Meadow.Slug);

        // One person, one token, two seats: the header decides which one the request is made with.
        var harbors = await inHarbor.VisibleProjectsAsync();
        var meadows = await inMeadow.VisibleProjectsAsync();

        harbors.Names().Should().Equal("Bay bridge");
        (await inHarbor.WithNamesAsync(harbors.Named("Bay bridge"))).MyRole.Should().Be("Surveyor");
        meadows.Names().Should().Equal("Garden shed");
        (await inMeadow.WithNamesAsync(meadows.Named("Garden shed"))).MyRole.Should().Be("Crew lead");
    }

    [Fact]
    public async Task Tove_opening_Garden_shed_with_the_harbor_header_is_not_found()
    {
        using var inHarbor = await sample.ClientAsync("tove", DemoData.Harbor.Slug);

        using var response = await inHarbor.GetAsync($"/projects/{DemoData.Meadow.ProjectNamed("Garden shed").Id.Value}", Cancellation);

        // Her own project, in her other tenant: in harbor it does not exist.
        await response.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
    }

    [Fact]
    public async Task Rhea_with_the_meadow_header_has_no_seat()
    {
        using var rhea = await sample.ClientAsync("rhea", DemoData.Meadow.Slug);

        using var response = await rhea.GetAsync("/me", Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);
    }

    [Fact]
    public async Task An_unknown_slug_answers_like_someone_elses()
    {
        using var inMeadow = await sample.ClientAsync("rhea", DemoData.Meadow.Slug);
        using var nowhere = await sample.ClientAsync("rhea", "no-such-tenant");

        using var someoneElses = await inMeadow.GetAsync("/me", Cancellation);
        using var unknown = await nowhere.GetAsync("/me", Cancellation);

        var theirs = await someoneElses.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);
        var none = await unknown.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);
        none.Title.Should().Be(theirs.Title, "a slug must not tell anyone whether its tenant exists");
    }

    [Fact]
    public async Task No_header_is_tenant_required()
    {
        using var rhea = await sample.ClientAsync("rhea", tenant: null);

        using var response = await rhea.GetAsync("/me", Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, TenancyRefusals.TenantRequired);
    }

    [Theory]
    [MemberData(nameof(RoutesThatNeedAToken))]
    public async Task No_token_is_401_on_every_seated_route(string template)
    {
        using var anonymous = (await sample.SharedAsync()).Client(token: null, DemoData.Harbor.Slug);

        using var response = await anonymous.SendAsync(SeatedRoutes.Named(template).Request(), Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_route_that_needs_a_token_is_among_the_routes_asked_without_one()
    {
        // The theory above proves something for the routes it is given. So the list it is given is held to what
        // the host maps: every route the modules map into the groups that require a caller, and nothing that is
        // gone. A route added to a module fails here until it is listed, and is then asked without a token too.
        var host = await sample.SharedAsync();
        var mapped = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
            .ToList();

        // A template of the list may end in the query its route reads, which is no part of the route.
        mapped.Should().BeEquivalentTo(SeatedRoutes.All.Select(route => route.Template.Split('?')[0]));
    }

    [Fact]
    public async Task An_expired_token_is_401()
    {
        var host = await sample.SharedAsync();
        var issuer = host.Services.GetRequiredService<LocalTokenIssuer>();
        var expired = issuer.IssueAt(DemoPeople.Rhea, DateTimeOffset.UtcNow - LocalTokenIssuer.Lifetime - TimeSpan.FromMinutes(10));
        using var rhea = host.Client(expired.AccessToken, DemoData.Harbor.Slug);

        using var response = await rhea.GetAsync("/me", Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("expired");
    }

    [Fact]
    public async Task Me_seats_lists_both_of_toves_seats()
    {
        using var tove = await sample.ClientAsync("tove", tenant: null);

        var seats = await tove.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation);

        seats.EnumerateArray()
            .Select(seat => (
                Slug: seat.GetProperty("tenant").GetProperty("slug").GetString(),
                Seat: seat.GetProperty("seat").GetProperty("id").GetGuid(),
                Status: seat.GetProperty("seat").GetProperty("status").GetString()))
            .Should().BeEquivalentTo(
            [
                (DemoData.Harbor.Slug, DemoData.Harbor.SeatOf(DemoPeople.Tove).Value, "active"),
                (DemoData.Meadow.Slug, DemoData.Meadow.SeatOf(DemoPeople.Tove).Value, "active"),
            ]);
    }
}
