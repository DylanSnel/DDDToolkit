using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.Access;

/// <summary>
/// A role that manages access goes only from someone who holds its keys that do, and never to themselves; other
/// roles go from whoever manages grants where the seat is placed. A seat that holds such a role is suspended,
/// deactivated or reactivated by the same rule, and a unit is moved away from it, or under it, by the same rule
/// too. And a tenant always keeps an administrator: meadow has one, so every way of her stepping down is refused;
/// harbor has two, so either may step down, and not the one who is then the last.
/// </summary>
/// <remarks>
/// The tests here are refused and change nothing, so they share one host. A test that changes something
/// makes a host of its own.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class ContainmentAndLastAdminScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tove_revoking_her_own_admin_grant_is_refused()
    {
        var meadow = DemoData.Meadow;
        using var tove = await sample.ClientAsync("tove", meadow.Slug);

        using var response = await tove.DeleteAsync(
            $"/tenancy/seats/{meadow.Administrator.Id.Value}/grants/{meadow.Root.Value}/{meadow.AdministratorsRole.Value}",
            Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.LastAdmin);
    }

    [Fact]
    public async Task Tove_suspending_herself_is_refused()
    {
        var meadow = DemoData.Meadow;
        using var tove = await sample.ClientAsync("tove", meadow.Slug);

        // A seat may suspend itself, and her own grant is her hold of its keys; the last-administrator rule is what
        // refuses this.
        using var response = await tove.PostAsync($"/tenancy/seats/{meadow.Administrator.Id.Value}/suspend", content: null, Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.LastAdmin);
    }

    [Fact]
    public async Task Tove_withdrawing_her_meadow_root_placement_is_refused()
    {
        var meadow = DemoData.Meadow;
        using var tove = await sample.ClientAsync("tove", meadow.Slug);

        // Withdrawing the placement would take the administrators' grant made with it.
        using var response = await tove.DeleteAsync($"/tenancy/seats/{meadow.Administrator.Id.Value}/placements/{meadow.Root.Value}", Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.LastAdmin);
    }

    [Fact]
    public async Task Rhea_grants_observer_at_North_Coast()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);

        // Observer manages no access, so managing grants at North, and so at North Coast below it, is all she needs.
        using var granted = await rhea.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants",
            new { unitId = NorthCoast.Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value },
            Cancellation);

        granted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var me = await leo.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("placements").EnumerateArray().Single().GetProperty("grants").EnumerateArray()
            .Select(grant => (Role: grant.GetProperty("role").GetString(), Applies: grant.GetProperty("appliesNow").GetBoolean()))
            .Should().Equal(("Observer", true));
    }

    [Fact]
    public async Task Crew_lead_at_a_unit_is_given_by_rhea_who_holds_crew_management_and_not_by_hana()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var hana = await host.ClientAsync("hana", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var crewLead = Harbor.Roles[SampleCatalogue.CrewLead].Value;

        // Crew lead manages access through projects.crew.manage, which Rhea holds at North for good.
        using (var granted = await rhea.PostAsJsonAsync(
                   $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants",
                   new { unitId = NorthCoast.Value, roleId = crewLead },
                   Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var me = await leo.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("key").GetString())
            .Should().Contain(ProjectKeys.ManageCrew, "he now manages every crew at North Coast");

        // Hana manages grants there too, and holds no crew management.
        using var given = await hana.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Juno).Value}/grants",
            new { unitId = NorthCoast.Value, roleId = crewLead },
            Cancellation);
        var refused = await given.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.GrantExceedsOwn);
        refused.Argument("Missing").Should().Be(ProjectKeys.ManageCrew);
    }

    [Fact]
    public async Task Rhea_granting_access_admin_is_refused_with_the_missing_keys()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        using var response = await rhea.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants",
            new { unitId = NorthCoast.Value, roleId = Harbor.Roles[SampleCatalogue.AccessAdmin].Value },
            Cancellation);

        // Access admin manages access. Of its keys that do, she holds at North Coast all but two; the keys that
        // manage no access, such as projects.view, are not counted.
        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.GrantExceedsOwn);
        refused.Argument("Missing").Should().Be(TenancyKeys.RolesManage + ", " + TenancyKeys.SettingsManage);
    }

    [Fact]
    public async Task Rhea_gives_leo_the_people_office_at_North_Coast()
    {
        await using var host = await sample.StartAsync();
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        using var juno = await host.ClientAsync("juno", Harbor.Slug);

        // The People office manages access through its one key, which Rhea holds at North for good.
        using (var granted = await rhea.PostAsJsonAsync(
                   $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Leo).Value}/grants",
                   new { unitId = NorthCoast.Value, roleId = Harbor.Roles[SampleCatalogue.PeopleOffice].Value },
                   Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Now Leo gives roles at North Coast: Observer, whose key he need not hold.
        using (var given = await leo.PostAsJsonAsync(
                   $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Juno).Value}/grants",
                   new { unitId = NorthCoast.Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value },
                   Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var me = await juno.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("placements").EnumerateArray().Single().GetProperty("grants").EnumerateArray()
            .Select(grant => grant.GetProperty("role").GetString())
            .Should().Equal("Observer");
    }

    [Fact]
    public async Task Rhea_cannot_give_herself_the_people_office_though_she_holds_its_key()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        using var response = await rhea.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Rhea).Value}/grants",
            new { unitId = Harbor.UnitNamed("North").Value, roleId = Harbor.Roles[SampleCatalogue.PeopleOffice].Value },
            Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.SelfAppointment);
        refused.Argument("Role").Should().Be(Harbor.Roles[SampleCatalogue.PeopleOffice].Value.ToString());
    }

    [Fact]
    public async Task Rhea_granting_at_South_Bay_is_not_permitted()
    {
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        using var response = await rhea.PostAsJsonAsync(
            $"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Tove).Value}/grants",
            new { unitId = Harbor.UnitNamed("South Bay").Value, roleId = Harbor.Roles[SampleCatalogue.Observer].Value },
            Cancellation);

        var refused = await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.NotPermitted);
        refused.Argument("Key").Should().Be(TenancyKeys.GrantsManage);
    }

    [Fact]
    public async Task Rhea_withdrawing_a_placement_whose_grants_exceed_hers_is_refused()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var leo = Harbor.SeatOf(DemoPeople.Leo).Value;

        using (var granted = await ada.PostAsJsonAsync(
                   $"/tenancy/seats/{leo}/grants",
                   new { unitId = NorthCoast.Value, roleId = Harbor.Roles[SampleCatalogue.AccessAdmin].Value },
                   Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Rhea may withdraw a seat in her area, but withdrawing Leo would revoke a role she could not have granted.
        using var response = await rhea.DeleteAsync($"/tenancy/seats/{leo}/placements/{NorthCoast.Value}", Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.GrantExceedsOwn);
    }

    [Fact]
    public async Task Ada_appoints_an_area_manager_for_South()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var tove = await host.ClientAsync("tove", Harbor.Slug);
        var toveSeat = Harbor.SeatOf(DemoPeople.Tove).Value;
        var south = Harbor.UnitNamed("South").Value;

        // The access admin's role holds every key that manages access, so Ada can grant every other pack.
        using (var placed = await ada.PostAsJsonAsync($"/tenancy/seats/{toveSeat}/placements", new { unitId = south, primary = false }, Cancellation))
        {
            placed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var granted = await ada.PostAsJsonAsync(
                   $"/tenancy/seats/{toveSeat}/grants",
                   new { unitId = south, roleId = Harbor.Roles[SampleCatalogue.AreaManager].Value },
                   Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Tove now closes Bay bridge as South's manager, where she was only its surveyor.
        var bridge = await tove.GetFromJsonAsync<JsonElement>(
            $"/access/projects/{Harbor.ProjectNamed("Bay bridge").Id.Value}?key={ProjectKeys.Close}",
            Cancellation);
        bridge.GetProperty("allowed").GetBoolean().Should().BeTrue();
        bridge.GetProperty("via").GetString().Should().Be("organization");
    }

    [Fact]
    public async Task With_two_administrators_either_may_step_down_and_the_one_left_may_not()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var maud = await host.ClientAsync("maud", Harbor.Slug);
        var accessAdmin = Harbor.AdministratorsRole.Value;
        var maudSeat = Harbor.SeatOf(DemoPeople.Maud).Value;

        // Harbor has two: Ada, its first administrator, and Maud. Ada gives up the administrators' role; she keeps
        // the area manager's, which makes nobody an administrator.
        using (var steppedDown = await ada.DeleteAsync($"/tenancy/seats/{Harbor.Administrator.Id.Value}/grants/{Harbor.Root.Value}/{accessAdmin}", Cancellation))
        {
            steppedDown.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var adas = await ada.GetFromJsonAsync<JsonElement>($"/access/units?key={TenancyKeys.RolesManage}", Cancellation);
        var mauds = await maud.GetFromJsonAsync<JsonElement>($"/access/units?key={TenancyKeys.RolesManage}", Cancellation);
        adas.GetProperty("wholeTenant").GetBoolean().Should().BeFalse();
        mauds.GetProperty("wholeTenant").GetBoolean().Should().BeTrue();

        // Now Maud is the last, and stays: neither giving up the role nor suspending her own seat gets through.
        using (var revoked = await maud.DeleteAsync($"/tenancy/seats/{maudSeat}/grants/{Harbor.Root.Value}/{accessAdmin}", Cancellation))
        {
            await revoked.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.LastAdmin);
        }

        using (var suspended = await maud.PostAsync($"/tenancy/seats/{maudSeat}/suspend", content: null, Cancellation))
        {
            await suspended.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.LastAdmin);
        }
    }

    [Fact]
    public async Task A_temporary_administrator_cannot_suspend_hana_but_may_suspend_juno()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var rhea = await host.ClientAsync("rhea", Harbor.Slug);
        var rheaSeat = Harbor.SeatOf(DemoPeople.Rhea).Value;
        var until = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(7), TimeSpan.Zero);

        // Rhea runs the tenant's access for a week: Access admin at the root, until then.
        using (var placed = await ada.PostAsJsonAsync($"/tenancy/seats/{rheaSeat}/placements", new { unitId = Harbor.Root.Value, primary = false }, Cancellation))
        {
            placed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var granted = await ada.PostAsJsonAsync(
                   $"/tenancy/seats/{rheaSeat}/grants",
                   new { unitId = Harbor.Root.Value, roleId = Harbor.Roles[SampleCatalogue.AccessAdmin].Value, until },
                   Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Suspending Hana would take away her People office, given for good at the root, which a week does not cover.
        using (var suspended = await rhea.PostAsync($"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Hana).Value}/suspend", content: null, Cancellation))
        {
            var refused = await suspended.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.GrantExceedsOwn);
            refused.Argument("Missing").Should().Be(TenancyKeys.GrantsManage);
        }

        // Juno holds no role at a unit, so suspending her takes nothing that manages access.
        using (var suspended = await rhea.PostAsync($"/tenancy/seats/{Harbor.SeatOf(DemoPeople.Juno).Value}/suspend", content: null, Cancellation))
        {
            suspended.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var seats = await ada.GetFromJsonAsync<JsonElement>("/tenancy/seats", Cancellation);
        seats.EnumerateArray().Select(seat => (Name: seat.GetProperty("displayName").GetString(), Status: seat.GetProperty("status").GetString()))
            .Should().Contain([("Hana", "active"), ("Juno", "suspended")]);
    }

    [Fact]
    public async Task A_temporary_administrator_cannot_move_north_coast_away_from_rhea_but_ada_may()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        using var leo = await host.ClientAsync("leo", Harbor.Slug);
        var leoSeat = Harbor.SeatOf(DemoPeople.Leo).Value;
        var until = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(7), TimeSpan.Zero);
        var south = new { parentId = Harbor.UnitNamed("South").Value };

        // Leo runs the tenant's access for a week: Access admin at the root, until then.
        using (var placed = await ada.PostAsJsonAsync($"/tenancy/seats/{leoSeat}/placements", new { unitId = Harbor.Root.Value, primary = false }, Cancellation))
        {
            placed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var granted = await ada.PostAsJsonAsync(
                   $"/tenancy/seats/{leoSeat}/grants",
                   new { unitId = Harbor.Root.Value, roleId = Harbor.Roles[SampleCatalogue.AccessAdmin].Value, until },
                   Cancellation))
        {
            granted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Under South, North Coast would no longer be Rhea's to manage, which it is for good: the move would take
        // her Area manager's keys that manage access there, which a week does not cover.
        using (var moved = await leo.PutAsJsonAsync($"/tenancy/units/{NorthCoast.Value}/parent", south, Cancellation))
        {
            var refused = await moved.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, TenancyRefusals.GrantExceedsOwn);
            refused.Argument("Missing").Should().Be(string.Join(", ",
                ProjectKeys.ManageCrew, ProjectKeys.ChangeOwner, TenancyKeys.GrantsManage, TenancyKeys.SeatsManage, TenancyKeys.UnitsManage));
        }

        using (var moved = await ada.PutAsJsonAsync($"/tenancy/units/{NorthCoast.Value}/parent", south, Cancellation))
        {
            moved.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var units = await ada.GetFromJsonAsync<JsonElement>("/tenancy/units", Cancellation);
        units.EnumerateArray().Select(unit => unit.GetProperty("path").GetString())
            .Should().Contain("Harbor Works / South / North Coast", "Ada holds every key that manages access at the root for good");
    }

    private static DemoTenant Harbor => DemoData.Harbor;

    private static OrganizationUnitId NorthCoast => Harbor.UnitNamed("North Coast");
}
