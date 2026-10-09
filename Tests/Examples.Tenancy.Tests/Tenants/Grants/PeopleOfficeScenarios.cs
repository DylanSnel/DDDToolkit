using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Tenants.Grants;

/// <summary>
/// Hana is in the people office: she holds grant management at Harbor Works' root and nothing else. Whoever
/// gives roles need not hold them, so she gives any role that manages no access to anyone in harbor, herself
/// included, and takes it away again: Surveyor and Observer. A role that manages access she gives only with its
/// keys that do, which she lacks but for her own, and never to herself: Crew lead among them, since the host
/// marks managing a crew.
/// </summary>
/// <remarks>
/// The refusals here change nothing, so they share the class's host. A test that changes something makes a
/// host of its own, as <see cref="ContainmentAndLastAdminScenarios"/> does.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class PeopleOfficeScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    private static Guid HanasSeat => Harbor.SeatOf(DemoPeople.Hana).Value;

    [Fact]
    public async Task Hana_holds_grant_management_for_the_whole_tenant_and_nothing_else()
    {
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        var me = await hana.GetFromJsonAsync<JsonElement>("/me", Cancellation);

        me.GetProperty("placements").EnumerateArray().Should().ContainSingle().Which
            .GetProperty("unit").GetProperty("path").GetString().Should().Be("Harbor Works");
        var key = me.GetProperty("keys").EnumerateArray().Should().ContainSingle().Which;
        key.GetProperty("key").GetString().Should().Be(TenancyKeys.GrantsManage);
        key.GetProperty("wholeTenant").GetBoolean().Should().BeTrue();
        var role = me.GetProperty("roles").EnumerateArray().Should().ContainSingle().Which;
        role.GetProperty("fromPack").GetString().Should().Be(SampleCatalogue.PeopleOffice);
        role.GetProperty("managesAccess").GetBoolean().Should().BeTrue("its one key gives power over other people's access");
        (await hana.VisibleProjectsAsync()).Should().BeEmpty("giving roles lets her see nothing");
    }

    [Fact]
    public async Task Hana_gives_leo_observer_at_North_Coast_without_holding_it()
    {
        await using var host = await sample.StartAsync();
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        using (var given = await hana.PostAsJsonAsync(
                   $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants",
                   new { unitId = NorthCoast.Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value, reason = "Follows the projects on the coast" },
                   Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var me = await leo.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("placements").EnumerateArray().Single().GetProperty("grants").EnumerateArray()
            .Select(grant => grant.GetProperty("role").GetString()).Should().Equal("Observer");
        var hanas = await hana.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        hanas.GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("key").GetString())
            .Should().Equal([TenancyKeys.GrantsManage], "giving a role gives the giver nothing");
    }

    [Fact]
    public async Task A_role_hana_gives_is_kept_in_the_access_history_as_given_by_her_seat()
    {
        await using var host = await sample.StartAsync();
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        var leo = Harbor.SeatOf(DemoPeople.Leo).Value;

        using (var given = await hana.PostAsJsonAsync(
                   $"/tenancy/seats/{leo}/grants",
                   new { unitId = NorthCoast.Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value, reason = "Follows the projects on the coast" },
                   Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // The history, read as the application's own work in harbor. The seeding gave every role as system work,
        // so the one grant a seat made is hers.
        List<(TenantId Tenant, string? By, string Payload)> grantsBySeats;
        using (TenancyUseCases.BeginSystemIn(Harbor.Id))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<TenantsContext>().Set<EventLogEntry>()
                .Where(entry => entry.EventName == "tenancy.organization-role-granted" && entry.ActedByKind == "seat")
                .Select(entry => new { Tenant = EF.Property<TenantId>(entry, TenancyEventLogTable.TenantId), entry.ActedById, entry.Payload })
                .ToListAsync(Cancellation);
            grantsBySeats = [.. rows.Select(row => (row.Tenant, row.ActedById, row.Payload))];
        }

        // The row's own columns say who acted and in which tenant, and the event says the same: both name her seat.
        var kept = grantsBySeats.Should().ContainSingle().Subject;
        kept.Tenant.Should().Be(Harbor.Id);
        kept.By.Should().Be(HanasSeat.ToString("D"));

        using var payload = JsonDocument.Parse(kept.Payload);
        var grant = payload.RootElement;
        grant.GetProperty("SeatId").GetGuid().Should().Be(leo);
        grant.GetProperty("UnitId").GetGuid().Should().Be(NorthCoast.Value);
        grant.GetProperty("RoleId").GetGuid().Should().Be(Harbor.Roles[SampleCatalogue.Observer].Value);
        grant.GetProperty("By").GetProperty("Kind").GetString().Should().Be("seat");
        grant.GetProperty("By").GetProperty("Seat").GetGuid().Should().Be(HanasSeat);
        kept.Payload.Should().NotContain("Follows the projects", "why a role was given stays with the grant");
    }

    [Fact]
    public async Task Hana_gives_vic_surveyor_at_North_Inland_and_he_sees_Inland_depot_again()
    {
        await using var host = await sample.StartAsync();
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        var depot = Harbor.ProjectNamed("Inland depot");

        using (var before = await vic.GetAsync($"/projects/{depot.Id.Value}", Cancellation))
        {
            await before.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        }

        using (var given = await hana.PostAsJsonAsync(
                   $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Vic).Value}/grants",
                   new { unitId = Harbor.UnitNamed("North Inland").Value, roleId = Harbor.Roles[SampleCatalogue.Surveyor].Value },
                   Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var after = await vic.GetAsync($"/projects/{depot.Id.Value}", Cancellation);
        after.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Hana_takes_away_vics_expired_observer_grant()
    {
        await using var host = await sample.StartAsync();
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        using var vic = await host.ClientAsync("vic", Harbor.Slug);
        var northInland = Harbor.UnitNamed("North Inland").Value;

        using (var taken = await hana.DeleteAsync(
                   $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Vic).Value}/grants/{northInland}/{Harbor.Roles[SampleCatalogue.Observer].Value}",
                   Cancellation))
        {
            taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var me = await vic.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("placements").EnumerateArray().Single().GetProperty("grants").EnumerateArray().Should().BeEmpty();
    }

    [Theory]
    [InlineData(SampleCatalogue.CrewLead, "projects.crew.manage")]
    [InlineData(SampleCatalogue.AreaManager, "projects.crew.manage, projects.owner.change, tenancy.seats.manage, tenancy.units.manage")]
    [InlineData(SampleCatalogue.AccessAdmin, "projects.crew.manage, projects.owner.change, tenancy.roles.manage, tenancy.seats.manage, tenancy.settings.manage, tenancy.units.manage")]
    public async Task Hana_cannot_give_a_role_that_manages_access(string pack, string missing)
    {
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        using var response = await hana.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants",
            new { unitId = NorthCoast.Value, roleId = Harbor.Roles[pack].Value },
            Cancellation);

        // Only the role's keys that manage access count, and of those she holds tenancy.grants.manage alone.
        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.GrantExceedsOwn);
        refused.Argument("Missing").Should().Be(missing);
    }

    [Fact]
    public async Task Hana_cannot_take_away_rheas_area_manager()
    {
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        using var response = await hana.DeleteAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Rhea).Value}/grants/{Harbor.UnitNamed("North").Value}/{Harbor.Roles[SampleCatalogue.AreaManager].Value}",
            Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.GrantExceedsOwn);
        refused.Argument("Missing").Should().Be(ProjectKeys.ManageCrew + ", " + ProjectKeys.ChangeOwner + ", " + TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage);
    }

    [Fact]
    public async Task Hana_gives_herself_surveyor_and_sees_every_project_of_harbor()
    {
        await using var host = await sample.StartAsync();
        using var hana = await host.ClientAsync("hana", Harbor.Slug);

        using (var given = await hana.PostAsJsonAsync(
                   $"/tenancy/seats/{HanasSeat}/grants",
                   new { unitId = Harbor.Root.Value, roleId = Harbor.Roles[SampleCatalogue.Surveyor].Value },
                   Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await hana.VisibleProjectsAsync()).Select(project => project.Text("number"))
            .Should().BeEquivalentTo(Harbor.Projects.Select(project => project.Number), "a surveyor at the root sees every project below it");
    }

    [Theory]
    [InlineData(SampleCatalogue.CrewLead)]
    [InlineData(SampleCatalogue.AreaManager)]
    [InlineData(SampleCatalogue.PeopleOffice)]
    public async Task Hana_cannot_give_herself_a_role_that_manages_access(string pack)
    {
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        using var response = await hana.PostAsJsonAsync(
            $"/tenancy/seats/{HanasSeat}/grants",
            new { unitId = Harbor.Root.Value, roleId = Harbor.Roles[pack].Value },
            Cancellation);

        // Refused before her keys are counted, and before anything else about the grant.
        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.SelfAppointment);
        refused.Argument("Role").Should().Be(Harbor.Roles[pack].Value.ToString());
    }

    [Fact]
    public async Task Hana_places_nobody()
    {
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        using var response = await hana.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Tove).Value}/placements",
            new { unitId = Harbor.UnitNamed("North").Value, primary = false },
            Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted);
        refused.Argument("Key").Should().Be(TenancyKeys.SeatsManage);
    }

    [Fact]
    public async Task The_catalogue_and_the_roles_say_which_manage_access()
    {
        using var hana = await sample.ClientAsync("hana", Harbor.Slug);

        var catalogue = await hana.GetFromJsonAsync<JsonElement>("/tenancy/catalogue", Cancellation);
        var roles = await hana.GetFromJsonAsync<JsonElement>("/tenancy/roles", Cancellation);

        catalogue.GetProperty("permissions").EnumerateArray()
            .Where(permission => permission.GetProperty("managesAccess").GetBoolean())
            .Select(permission => permission.GetProperty("key").GetString())
            .Should().BeEquivalentTo(
            [
                ProjectKeys.ChangeOwner, ProjectKeys.ManageCrew,
                TenancyKeys.SettingsManage, TenancyKeys.UnitsManage, TenancyKeys.SeatsManage, TenancyKeys.GrantsManage, TenancyKeys.RolesManage,
            ],
            "the host marks naming an owner and managing a crew, keys Projects declares, and Tenancy's own keys all manage access");
        roles.EnumerateArray()
            .Select(role => (Pack: role.GetProperty("fromPack").GetString(), ManagesAccess: role.GetProperty("managesAccess").GetBoolean()))
            .Should().BeEquivalentTo(
            [
                (SampleCatalogue.AccessAdmin, true), (SampleCatalogue.AreaManager, true), (SampleCatalogue.CrewLead, true), (SampleCatalogue.PeopleOffice, true),
                (SampleCatalogue.Surveyor, false), (SampleCatalogue.Observer, false),
            ]);
    }

    [Fact]
    public async Task Of_the_keys_that_manage_access_a_crew_gives_crew_management_alone()
    {
        // A crew gives only keys that act on its project, and whoever manages the crew gives them, holding them or
        // not. Managing the crew is the one marked key among them: held at a unit it reaches every crew below it,
        // on a crew only that crew, which whoever gives it there manages already.
        var catalogue = (await sample.SharedAsync()).Services.GetRequiredService<TenancyCatalogue>();

        catalogue.AccessManagingKeys.Where(ProjectCatalogue.CrewGives).Should().Equal(ProjectKeys.ManageCrew);
    }

    private static OrganizationUnitId NorthCoast => Harbor.UnitNamed("North Coast");
}
