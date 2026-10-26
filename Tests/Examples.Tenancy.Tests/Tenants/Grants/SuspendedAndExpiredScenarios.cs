using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.Grants;

/// <summary>
/// Neither a suspended seat nor an expired grant gives anything. Seth's seat in harbor was placed and granted
/// Observer at South Bay like any other, and then suspended. Vic's Observer grant at North Inland ran
/// for four weeks and ended yesterday; he is still an observer on Pier 7's crew.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class SuspendedAndExpiredScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    public static TheoryData<string> SeatedRoutesOfEveryModule => SeatedRoutes.Templates(SeatedRoutes.InTenant);

    [Theory]
    [MemberData(nameof(SeatedRoutesOfEveryModule))]
    public async Task Seth_is_refused_as_suspended_everywhere(string template)
    {
        using var seth = await sample.ClientAsync("seth", Harbor.Slug);

        using var response = await seth.SendAsync(SeatedRoutes.Named(template).Request(), Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.SeatSuspended);
    }

    [Fact]
    public async Task Seths_seat_still_shows_in_his_seat_list()
    {
        using var seth = await sample.ClientAsync("seth", tenant: null);

        var seats = await seth.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation);

        // The tenant picker shows it, so its refusal can be seen, rather than a tenant that seems not to exist.
        var harbor = seats.EnumerateArray().Should().ContainSingle().Which;
        harbor.GetProperty("tenant").GetProperty("slug").GetString().Should().Be(Harbor.Slug);
        harbor.GetProperty("seat").GetProperty("id").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Seth).Value);
        harbor.GetProperty("seat").GetProperty("status").GetString().Should().Be("suspended");
    }

    [Fact]
    public async Task Vic_does_not_see_Inland_depot_because_his_grant_expired()
    {
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);

        using var depot = await vic.GetAsync($"/projects/{Harbor.ProjectNamed("Inland depot").Id.Value}", Cancellation);
        var units = await vic.GetFromJsonAsync<JsonElement>($"/access/units?key={ProjectKeys.View}", Cancellation);

        await depot.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        units.GetProperty("unitsWhereIHold").EnumerateArray().Should().BeEmpty("the grant at North Inland has ended, and nothing was written when it did");
    }

    [Fact]
    public async Task Vic_still_sees_Pier_7_through_the_crew()
    {
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);

        var projects = await vic.VisibleProjectsAsync();

        var pier = projects.Should().ContainSingle().Which;
        pier.Text("name").Should().Be("Pier 7");
        pier.Text("via").Should().Be("crew");
        (await vic.WithNamesAsync(pier)).MyRole.Should().Be("Observer");
    }

    [Fact]
    public async Task WhoAmI_shows_the_expired_grant_as_not_applying()
    {
        using var vic = await sample.ClientAsync("vic", Harbor.Slug);

        var me = await vic.GetFromJsonAsync<JsonElement>("/me", Cancellation);

        var placement = me.GetProperty("placements").EnumerateArray().Should().ContainSingle().Which;
        placement.GetProperty("unit").GetProperty("path").GetString().Should().Be("Harbor Works / North / North Inland");
        var grant = placement.GetProperty("grants").EnumerateArray().Should().ContainSingle().Which;
        grant.GetProperty("role").GetString().Should().Be("Observer");
        grant.GetProperty("appliesNow").GetBoolean().Should().BeFalse();
        grant.GetProperty("endsAt").GetDateTimeOffset().Should().BeBefore(DateTimeOffset.UtcNow);
        me.GetProperty("keys").EnumerateArray().Should().BeEmpty("a grant that does not apply holds no key");
    }
}
