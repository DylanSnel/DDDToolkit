using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// Orla works for the application, not for a tenant. Signed in with an operator's token she lists every tenant
/// and reads one tenant's projects and their inspections; she has no seat anywhere, so every route inside a
/// tenant refuses her, the ones that change something among them. And the other way round: a seat is no
/// operator, whatever it holds.
/// </summary>
/// <remarks>
/// Who is an operator is what the token says, and the dev login writes an operator's token for the people the
/// host's development settings list.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks. There her queries run as the operators' own database role, which the exported
/// policies let read and nothing else.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class OperatorScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static DemoTenant Meadow => DemoData.Meadow;

    public static TheoryData<string> RoutesInsideATenant => SeatedRoutes.Templates(SeatedRoutes.InTenant);

    public static TheoryData<string> RoutesOfTheOperators => SeatedRoutes.Templates(SeatedRoutes.Operations);

    [Fact]
    public async Task An_operator_lists_every_tenant_with_its_active_seats()
    {
        using var orla = await sample.ClientAsync("orla", tenant: null);

        var tenants = await orla.GetFromJsonAsync<JsonElement>("/operations/tenants", Cancellation);

        // By slug, each with its id, its status and how many of its seats are in use: a suspended seat is not.
        tenants.GetProperty("items").EnumerateArray()
            .Select(tenant => (tenant.GetProperty("id").GetGuid(), tenant.Text("slug"), tenant.Text("name"), tenant.Text("status"), tenant.GetProperty("activeSeats").GetInt32()))
            .Should().Equal(
                (Harbor.Id.Value, Harbor.Slug, Harbor.Name, "active", ActiveSeatsOf(Harbor)),
                (Meadow.Id.Value, Meadow.Slug, Meadow.Name, "active", ActiveSeatsOf(Meadow)));
        tenants.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null);

        // A page at a time: the marker of one page asks for the next.
        var first = await orla.GetFromJsonAsync<JsonElement>("/operations/tenants?size=1", Cancellation);
        var second = await orla.GetFromJsonAsync<JsonElement>($"/operations/tenants?size=1&after={Uri.EscapeDataString(first.Text("next")!)}", Cancellation);
        first.GetProperty("items").EnumerateArray().Select(tenant => tenant.Text("slug")).Should().Equal(Harbor.Slug);
        second.GetProperty("items").EnumerateArray().Select(tenant => tenant.Text("slug")).Should().Equal(Meadow.Slug);
    }

    [Fact]
    public async Task An_operator_reads_one_tenants_projects_and_their_inspections()
    {
        await using var host = await sample.StartAsync();
        using var juno = await host.ClientAsync("juno", Harbor.Slug);
        using var orla = await host.ClientAsync("orla", tenant: null);
        var pier = Harbor.ProjectNamed("Pier 7");

        using (var recorded = await juno.PostAsJsonAsync($"/projects/{pier.Id.Value}/inspections", new { title = "Loose railing" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Every project of the tenant the path names, by number, whoever in it would see them; and none of another's.
        var inHarbor = await orla.GetFromJsonAsync<JsonElement>($"/operations/tenants/{Harbor.Id.Value}/projects", Cancellation);
        var inMeadow = await orla.GetFromJsonAsync<JsonElement>($"/operations/tenants/{Meadow.Id.Value}/projects", Cancellation);
        inHarbor.EnumerateArray().Select(project => project.Text("number")).Should().Equal(Harbor.Projects.Select(project => project.Number).Order(StringComparer.Ordinal));
        inHarbor.EnumerateArray().Names().Should().BeEquivalentTo(Harbor.Projects.Select(project => project.Name));
        inMeadow.EnumerateArray().Names().Should().Equal(Meadow.Projects.Select(project => project.Name));

        // Ids and the project's own texts, as every answer of the module.
        inHarbor.EnumerateArray().Named("Pier 7").EnumerateObject().Select(property => property.Name).Should().Equal(
            "id", "number", "name", "unitId", "state", "ownerSeat", "plannedFrom", "plannedUntil", "changedBy");
        inHarbor.EnumerateArray().Named("Pier 7").GetProperty("ownerSeat").GetGuid().Should().Be(Harbor.SeatOf(pier.Owner).Value);

        // A project's inspections, under the tenant it is in; under another tenant the project has none.
        var inspections = await orla.GetFromJsonAsync<JsonElement>($"/operations/tenants/{Harbor.Id.Value}/projects/{pier.Id.Value}/inspections", Cancellation);
        var inspection = inspections.EnumerateArray().Should().ContainSingle().Subject;
        inspection.Text("title").Should().Be("Loose railing");
        inspection.GetProperty("recordedBy").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Juno).Value);
        inspection.GetProperty("changedBy").Text("kind").Should().Be("seat", "the save that wrote the row ran as the seat that recorded it");
        inspection.GetProperty("changedBy").GetProperty("seatId").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Juno).Value);

        var elsewhere = await orla.GetFromJsonAsync<JsonElement>($"/operations/tenants/{Meadow.Id.Value}/projects/{pier.Id.Value}/inspections", Cancellation);
        elsewhere.EnumerateArray().Should().BeEmpty("the project is not one of that tenant's");
    }

    [Theory]
    [MemberData(nameof(RoutesInsideATenant))]
    public async Task An_operator_has_no_seat_so_every_route_inside_a_tenant_refuses_it(string template)
    {
        // Whatever tenant the request names: every read and every change of harbor, asked as an operator.
        using var orla = await sample.ClientAsync("orla", Harbor.Slug);

        using var response = await orla.SendAsync(SeatedRoutes.Named(template).Request(), Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotSeated);
    }

    [Fact]
    public async Task What_an_operator_tried_to_change_is_as_it_was()
    {
        await using var host = await sample.StartAsync();
        using var orla = await host.ClientAsync("orla", Harbor.Slug);
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var before = (await ada.GetStringAsync("/projects", Cancellation), await ada.GetStringAsync("/tenancy/seats", Cancellation), await ada.GetStringAsync("/tenancy/roles", Cancellation));

        foreach (var route in SeatedRoutes.InTenant.Where(route => route.Method != HttpMethod.Get))
        {
            using var response = await orla.SendAsync(route.Request(), Cancellation);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "{0} changes something", route.Template);
        }

        (await ada.GetStringAsync("/projects", Cancellation), await ada.GetStringAsync("/tenancy/seats", Cancellation), await ada.GetStringAsync("/tenancy/roles", Cancellation))
            .Should().Be(before);
        (await orla.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation)).EnumerateArray().Should().BeEmpty("an operator has a seat in no tenant");
    }

    [Theory]
    [MemberData(nameof(RoutesOfTheOperators))]
    public async Task A_seat_is_never_an_operator(string template)
    {
        // Harbor's first administrator, who holds every key there is in her tenant.
        using var ada = await sample.ClientAsync("ada", Harbor.Slug);

        using var response = await ada.SendAsync(SeatedRoutes.Named(template).Request(), Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.OperatorsOnly);
    }

    [Fact]
    public async Task The_use_cases_themselves_refuse_whoever_is_no_operator()
    {
        // Past the routes and their policy: each query sent as the application's own work in harbor, which holds
        // every key there and is no operator. What refuses is each module's own check, on the way to the handler.
        var host = await sample.SharedAsync();
        IMessage[] queries =
        [
            new AllTenants(),
            new TenantAccessHistory(Harbor.Id, new GreenDonut.Data.PagingArguments(first: 5)),
            new TenantProjects(Harbor.Id),
            new TenantProjectInspections(Harbor.Id, Harbor.ProjectNamed("Pier 7").Id),
        ];

        using (TenantsTenancy.BeginSystemIn(Harbor.Id, Harbor.Administrator.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            foreach (var query in queries)
            {
                var send = async () => await sender.Send(query, Cancellation);
                (await send.Should().ThrowAsync<RefusalException>("{0} is for operators only", query.GetType().Name))
                    .Which.Code.Should().Be(TenancyRefusals.OperatorsOnly);
            }
        }
    }

    /// <summary>The seats of <paramref name="tenant"/> that are in use: its administrator's and the others', but the suspended.</summary>
    private static int ActiveSeatsOf(DemoTenant tenant) => 1 + tenant.Seats.Count - tenant.Suspended.Count;
}
