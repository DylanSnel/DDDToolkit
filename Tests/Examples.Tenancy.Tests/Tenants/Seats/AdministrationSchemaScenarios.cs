using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Tenants.Seats;

/// <summary>
/// The tenant's administration schema at <c>/admin/graphql</c>: what a seat is offered at <c>/graphql</c> of
/// Tenancy, and another person's roles besides, which <c>/graphql</c> does not offer anybody. Which schema offers
/// the field decides nothing about who may read it: that is the request's, <c>tenancy.seats.manage</c> for the whole
/// tenant, as on its route.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks: the roles are read as the caller.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class AdministrationSchemaScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string Grants = "query($seat: UUID!) { seatGrants(seatId: $seat) { unitId unitPath roleId role appliesNow } }";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static Guid Rhea => Harbor.SeatOf(DemoPeople.Rhea).Value;

    [Fact]
    public async Task Maud_reads_rheas_roles_at_the_administrations_endpoint_and_her_route_answers_the_same()
    {
        // Maud is harbor's second access admin, who holds every key of Tenancy at the root.
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);

        var grant = (await maud.AdministrationDataAsync(Grants, new { seat = Rhea })).GetProperty("seatGrants")
            .EnumerateArray().Should().ContainSingle("Rhea is an area manager of North, and that is all").Subject;

        grant.GetProperty("unitId").GetGuid().Should().Be(Harbor.UnitNamed("North").Value);
        grant.GetProperty("unitPath").GetString().Should().EndWith("North");
        grant.GetProperty("roleId").GetGuid().Should().Be(Harbor.Roles[SampleCatalogue.AreaManager].Value);
        grant.GetProperty("appliesNow").GetBoolean().Should().BeTrue();

        var byRoute = (await maud.GetFromJsonAsync<JsonElement>($"/tenancy/seats/{Rhea}/grants", Cancellation)).EnumerateArray().Should().ContainSingle().Subject;
        byRoute.GetProperty("roleId").GetGuid().Should().Be(grant.GetProperty("roleId").GetGuid());
        byRoute.GetProperty("role").GetString().Should().Be(grant.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Leo_whose_seat_holds_no_key_gets_nothing_from_the_administrations_field()
    {
        // Leo has a seat, so the endpoint lets him ask; the request refuses him before anything is read.
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);

        var answer = await leo.GraphQLAsync(SampleGraphQLCalls.Administration, Grants, new { seat = Rhea });

        answer.SingleError().Code().Should().Be(TenancyRefusals.NotPermitted);
        answer.TryGetProperty("data", out var data).Should().BeTrue();
        data.ValueKind.Should().Be(JsonValueKind.Null, "nothing of Rhea's roles reaches him");
        answer.GetRawText().Should().NotContain(Harbor.Roles[SampleCatalogue.AreaManager].Value.ToString());
    }

    [Fact]
    public async Task Rhea_who_manages_seats_in_her_region_only_is_refused_as_well()
    {
        // She holds tenancy.seats.manage at North: another person's roles in every unit ask for it at the root.
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        (await rhea.GraphQLAsync(SampleGraphQLCalls.Administration, Grants, new { seat = Harbor.SeatOf(DemoPeople.Leo).Value }))
            .SingleError().Code().Should().Be(TenancyRefusals.NotPermitted);
    }

    [Fact]
    public async Task A_seat_of_another_tenant_has_no_roles_here_at_the_field_or_on_the_route()
    {
        // Tove is meadow's administrator, with the key for the whole of meadow: her own seat there has her role.
        var toveInMeadow = DemoData.Meadow.SeatOf(DemoPeople.Tove).Value;
        using var tove = await sample.ClientAsync("tove", DemoData.Meadow.Slug);
        (await tove.AdministrationDataAsync(Grants, new { seat = toveInMeadow })).GetProperty("seatGrants")
            .EnumerateArray().Should().ContainSingle("she reads the seats of her own tenant");

        // A seat of harbor, asked in meadow, has none: the read is kept to the caller's tenant, which the answer
        // does not tell apart from a seat that does not exist.
        (await tove.AdministrationDataAsync(Grants, new { seat = Rhea })).GetProperty("seatGrants").EnumerateArray().Should().BeEmpty();
        (await tove.GetFromJsonAsync<JsonElement>($"/tenancy/seats/{Rhea}/grants", Cancellation)).EnumerateArray().Should().BeEmpty();

        // And the other way: Maud holds every key of harbor, and reads nothing of Tove's seat in meadow.
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);
        (await maud.AdministrationDataAsync(Grants, new { seat = toveInMeadow })).GetProperty("seatGrants").EnumerateArray().Should().BeEmpty();
        (await maud.GetFromJsonAsync<JsonElement>($"/tenancy/seats/{toveInMeadow}/grants", Cancellation)).EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Past_the_mediator_the_storage_asks_again_as_the_caller_and_leo_reads_nothing()
    {
        // The request's access check is the mediator's step. Called without it, as Leo, the read and its handler
        // still run as his seat under the policies, which give him nobody's roles but his own.
        var host = await sample.SharedAsync();
        using (SampleCallers.BeginSeatOf(DemoPeople.Leo, Harbor))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var reads = scope.ServiceProvider.GetRequiredService<ITenancyReads>();

            (await reads.GrantsOfAsync(Harbor.SeatOf(DemoPeople.Rhea), Cancellation)).Should().BeEmpty();
            (await new SeatGrantsHandler(reads).Handle(new SeatGrants(Harbor.SeatOf(DemoPeople.Maud)), Cancellation)).Should().BeEmpty();
        }

        // The same read as Maud answers: what Leo was given is nothing, not a read that does not work.
        using (SampleCallers.BeginSeatOf(DemoPeople.Maud, Harbor))
        {
            await using var scope = host.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<ITenancyReads>().GrantsOfAsync(Harbor.SeatOf(DemoPeople.Rhea), Cancellation))
                .Should().ContainSingle().Which.RoleId.Should().Be(Harbor.Roles[SampleCatalogue.AreaManager]);
        }
    }

    [Fact]
    public async Task Past_the_mediator_rhea_reads_the_roles_in_her_region_and_none_above_it()
    {
        // Rhea manages seats, grants and units at North, and the request refuses her (above), since it asks the seats key
        // for the whole tenant. Called without it, the storage reads as her seat, and the policy on the grants draws a
        // line of its own, wider than the request's, a key reading only where it applies: Vic's role at North Inland,
        // below North, and nothing of Maud's at the root, or Seth's at South Bay.
        var host = await sample.SharedAsync();
        using (SampleCallers.BeginSeatOf(DemoPeople.Rhea, Harbor))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var reads = scope.ServiceProvider.GetRequiredService<ITenancyReads>();

            (await reads.GrantsOfAsync(Harbor.SeatOf(DemoPeople.Vic), Cancellation)).Should().ContainSingle()
                .Which.UnitId.Should().Be(Harbor.UnitNamed("North Inland"));
            (await reads.GrantsOfAsync(Harbor.SeatOf(DemoPeople.Maud), Cancellation)).Should().BeEmpty("Maud's role is held at the root, above North");
            (await reads.GrantsOfAsync(Harbor.SeatOf(DemoPeople.Seth), Cancellation)).Should().BeEmpty("Seth's is at South Bay, beside it");
        }
    }

    [Fact]
    public async Task At_graphql_the_field_is_offered_to_nobody_and_a_seat_reads_its_own_roles_there()
    {
        // Not to Maud either: the gateway does not have the field, so the document is refused before anything runs.
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);
        var refused = await maud.GraphQLAsync(Grants, new { seat = Rhea });

        refused.TryGetProperty("data", out _).Should().BeFalse("a document that names a field the schema has not is not run");
        refused.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("seatGrants");

        // What a seat reads of its own roles is at /graphql: its overview.
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);
        var own = (await rhea.GraphQLDataAsync("{ overviewOfMine { placements { unit { id } grants { roleId appliesNow } } } }"))
            .GetProperty("overviewOfMine").GetProperty("placements").EnumerateArray().Should().ContainSingle().Subject;
        own.GetProperty("unit").GetProperty("id").GetGuid().Should().Be(Harbor.UnitNamed("North").Value);
        own.GetProperty("grants").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("roleId").GetGuid().Should().Be(Harbor.Roles[SampleCatalogue.AreaManager].Value);
    }

    [Fact]
    public async Task The_administrations_endpoint_bounds_a_request_as_the_gateway_bounds_one_at_graphql()
    {
        // The gateway's bounds are the gateway's: the schema served apart has the same, from the host.
        using var maud = await sample.ClientAsync("maud", Harbor.Slug);

        static string Seats(int times) => "{ " + string.Join(" ", Enumerable.Range(1, times).Select(n => $"s{n}: seats {{ id }}")) + " }";

        // A hundred and one lists of seats are 202 fields, more than a document may have.
        var wide = await maud.GraphQLAsync(SampleGraphQLCalls.Administration, Seats(101), variables: null);
        wide.TryGetProperty("data", out _).Should().BeFalse("a document of too many fields is not run: {0}", wide.GetRawText());
        wide.GetProperty("errors").EnumerateArray().Should().NotBeEmpty();

        // Eleven levels deep, one more than a request may go.
        var deep = await maud.GraphQLAsync(
            SampleGraphQLCalls.Administration,
            "{ __schema { types { fields { type { ofType { ofType { ofType { ofType { ofType { ofType { name } } } } } } } } } } }",
            variables: null);
        deep.TryGetProperty("data", out _).Should().BeFalse("a document that goes too deep is not run: {0}", deep.GetRawText());
        deep.GetProperty("errors").EnumerateArray().Should().ContainSingle().Which.GetProperty("message").GetString().Should().Contain("depth");

        // Within both, the same request is answered.
        (await maud.AdministrationDataAsync(Seats(50))).EnumerateObject().Should().HaveCount(50);
    }

    [Fact]
    public async Task The_administrations_endpoint_wants_a_token_and_a_seat_as_the_routes_do()
    {
        using var anonymous = (await sample.SharedAsync()).Client(token: null, tenant: Harbor.Slug);
        using (var response = await anonymous.PostAsJsonAsync(SampleGraphQLCalls.Administration, new { query = "{ seats { id } }" }, Cancellation))
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // Rhea has no seat in meadow: the endpoint answers as a route inside a tenant does.
        using var elsewhere = await sample.ClientAsync("rhea", DemoData.Meadow.Slug);
        using (var response = await elsewhere.PostAsJsonAsync(SampleGraphQLCalls.Administration, new { query = "{ seats { id } }" }, Cancellation))
        {
            await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);
        }
    }
}
