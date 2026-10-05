using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// The demonstration data, as the people in it see it: every scenario starts from here, so it has to be what
/// <see cref="DemoData"/> says, and seeding it again must change nothing.
/// </summary>
/// <remarks>
/// The tests that read the demonstration read it where every scenario finds it: on a copy of the database the
/// run seeded once. The two about a second start run a host that seeds its own database, and then another host
/// on that same database, which is what starting the application again is.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class DemoSeederTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Harbor_has_every_unit_and_every_seat_of_the_demonstration()
    {
        var harbor = DemoData.Harbor;
        using var ada = await sample.ClientAsync("ada", harbor.Slug);

        var units = await ada.GetFromJsonAsync<JsonElement>("/tenancy/units", Cancellation);
        var seats = await ada.GetFromJsonAsync<JsonElement>("/tenancy/seats", Cancellation);

        units.EnumerateArray().Select(unit => unit.GetProperty("path").GetString()).Should().BeEquivalentTo(
            "Harbor Works",
            "Harbor Works / North",
            "Harbor Works / North / North Coast",
            "Harbor Works / North / North Inland",
            "Harbor Works / South",
            "Harbor Works / South / South Bay");
        seats.EnumerateArray()
            .Select(seat => (Name: seat.GetProperty("displayName").GetString(), Status: seat.GetProperty("status").GetString()))
            .Should().BeEquivalentTo(
            [
                ("Ada", "active"), ("Rhea", "active"), ("Leo", "active"), ("Juno", "active"),
                ("Vic", "active"), ("Seth", "suspended"), ("Tove", "active"), ("Hana", "active"), ("Maud", "active"),
            ]);
    }

    [Fact]
    public async Task Every_active_seat_is_placed_where_the_demonstration_says()
    {
        var harbor = DemoData.Harbor;

        foreach (var seat in harbor.Seats.Prepend(harbor.Administrator).Where(seat => !harbor.Suspended.Contains(seat.Person)))
        {
            using var person = await sample.ClientAsync(seat.Person.Key, harbor.Slug);

            var me = await person.GetFromJsonAsync<JsonElement>("/me", Cancellation);

            me.GetProperty("seat").GetProperty("id").GetGuid().Should().Be(seat.Id.Value);
            me.GetProperty("placements").EnumerateArray()
                .Select(placement => (Unit: placement.GetProperty("unit").GetProperty("id").GetGuid(), Primary: placement.GetProperty("isPrimary").GetBoolean()))
                .Should().ContainSingle("{0} is placed in one unit", seat.Person.Name)
                .Which.Should().Be((seat.PlacedIn.Value, true));
        }
    }

    [Fact]
    public async Task Each_tenants_first_administrator_holds_the_administrators_role_of_its_shape_at_the_root_for_good()
    {
        // A flat tenant's administrator holds every key through the one role. A hierarchical tenant's holds the
        // role that runs access, and in the demonstration the area manager's next to it, for the work.
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [DemoData.Harbor.Slug] = [SampleCatalogue.AccessAdmin, SampleCatalogue.AreaManager],
            [DemoData.Meadow.Slug] = [SampleCatalogue.TenantAdmin],
        };

        foreach (var tenant in DemoData.Tenants)
        {
            using var administrator = await sample.ClientAsync(tenant.Administrator.Person.Key, tenant.Slug);

            var me = await administrator.GetFromJsonAsync<JsonElement>("/me", Cancellation);

            me.GetProperty("tenant").GetProperty("shape").GetString().Should().Be(tenant.Shape.OnTheWire());
            var grants = me.GetProperty("placements").EnumerateArray().Should().ContainSingle().Which.GetProperty("grants").EnumerateArray().ToList();
            grants.Select(grant => grant.GetProperty("roleId").GetGuid())
                .Should().BeEquivalentTo(expected[tenant.Slug].Select(pack => tenant.Roles[pack].Value));
            grants.Should().Contain(grant => grant.GetProperty("roleId").GetGuid() == tenant.AdministratorsRole.Value);
            foreach (var grant in grants)
            {
                grant.GetProperty("endsAt").ValueKind.Should().Be(JsonValueKind.Null);
                grant.GetProperty("appliesNow").GetBoolean().Should().BeTrue();
            }

            // Either way every live key of the catalogue is hers, for the whole tenant.
            me.GetProperty("keys").EnumerateArray().Where(key => key.GetProperty("wholeTenant").GetBoolean()).Select(key => key.GetProperty("key").GetString())
                .Should().BeEquivalentTo((await sample.SharedAsync()).Services.GetRequiredService<TenancyCatalogue>().LiveKeys);
        }
    }

    [Fact]
    public async Task Every_tenant_is_set_up_with_the_starter_project_roles()
    {
        foreach (var tenant in DemoData.Tenants)
        {
            using var administrator = await sample.ClientAsync(tenant.Administrator.Person.Key, tenant.Slug);

            // The project roles a tenant's crews begin with, made once when it was set up, with the ids the
            // demonstration gives them, the names and keys of the starter roles, and in use.
            (await administrator.ProjectRolesAsync())
                .Select(role => (
                    Id: role.GetProperty("id").GetGuid(),
                    MadeFrom: role.Text("madeFrom")!,
                    Name: role.Text("name")!,
                    Status: role.Text("status"),
                    Keys: string.Join(" ", role.GetProperty("keys").EnumerateArray().Select(key => key.GetString()))))
                .Should().BeEquivalentTo(SampleCatalogue.ProjectRoles.Select(starter => (
                    tenant.ProjectRoles[starter.Key].Value,
                    starter.Key,
                    starter.Name,
                    (string?)"active",
                    string.Join(" ", starter.Keys.Order(StringComparer.Ordinal)))));
        }
    }

    [Fact]
    public async Task Every_project_is_opened_with_its_owner_as_lead_and_its_crew()
    {
        foreach (var tenant in DemoData.Tenants)
        {
            using var administrator = await sample.ClientAsync(tenant.Administrator.Person.Key, tenant.Slug);

            // The first administrator sees every project of the tenant, from the root.
            var projects = await administrator.VisibleProjectsAsync();

            projects.Select(project => project.Text("number")).Should().Equal(tenant.Projects.Select(project => project.Number));
            foreach (var expected in tenant.Projects)
            {
                var project = projects.Named(expected.Name);
                project.GetProperty("id").GetGuid().Should().Be(expected.Id.Value);
                project.GetProperty("unitId").GetGuid().Should().Be(expected.Unit.Value);
                project.GetProperty("ownerSeat").GetGuid().Should().Be(tenant.SeatOf(expected.Owner).Value);
                project.GetProperty("crew").EnumerateArray()
                    .Select(member =>
                    {
                        // Each holds one role, for good, like the membership it is held in.
                        var held = member.GetProperty("roles").EnumerateArray().Should().ContainSingle().Which;
                        held.GetProperty("endsAt").ValueKind.Should().Be(JsonValueKind.Null);
                        held.GetProperty("appliesNow").GetBoolean().Should().BeTrue();
                        member.GetProperty("endsAt").ValueKind.Should().Be(JsonValueKind.Null);
                        return (Seat: member.GetProperty("seatId").GetGuid(), Role: held.GetProperty("roleId").GetGuid(), Applies: member.GetProperty("appliesNow").GetBoolean());
                    })
                    .Should().BeEquivalentTo(
                        expected.Crew.Select(member => (tenant.SeatOf(member.Person).Value, tenant.ProjectRoles[member.Role].Value, true))
                            .Prepend((tenant.SeatOf(expected.Owner).Value, tenant.ProjectRoles[SampleCatalogue.CrewLead].Value, true)));
            }
        }
    }

    [Fact]
    public async Task Seeding_again_changes_nothing()
    {
        // A database with nothing in it, which the first host seeds as it starts.
        await using var onPostgres = await sample.StartOnPostgresAsync(seeded: false);
        var first = await EverythingAdaSeesAsync(onPostgres.Host);
        await onPostgres.Host.DisposeAsync();

        // The second host, on the same database, finds harbor's slug taken, and stops seeding there.
        var logs = new RecordingLoggerProvider();
        await using var again = await onPostgres.StartAnotherHostAsync(Cancellation, services => services.AddSingleton<ILoggerProvider>(logs));
        var second = await EverythingAdaSeesAsync(again);

        second.Should().Be(first);
        logs.Entries.Should().Contain(entry => entry.Category == typeof(DemoSeeder).FullName, "the second host ran the seeding, which found the demonstration there");
        logs.Entries.Should().NotContain(entry => entry.Category == typeof(DemoSeeder).FullName && entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task A_seeding_that_stopped_part_way_is_reported_on_the_next_start()
    {
        var harbor = DemoData.Harbor;

        // What a seeding leaves behind when it stops after its first command: harbor provisioned, nothing more.
        await using var onPostgres = await sample.StartOnPostgresAsync(
            settings: new Dictionary<string, string> { [DemoSeeder.Setting] = "false" },
            seeded: false);
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            await using var scope = onPostgres.Host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SampleTenancy.TenantCommands>().ProvisionAsync(
                new SampleTenancy.TenantToProvision(
                    harbor.Slug,
                    harbor.Name,
                    harbor.Shape,
                    harbor.Name,
                    harbor.Administrator.Person.Id,
                    harbor.Administrator.Person.Name,
                    TenantId: harbor.Id,
                    RootId: harbor.Root,
                    AdminSeatId: harbor.Administrator.Id,
                    RoleIds: harbor.Roles,
                    ConfigureRoot: root => root.SetKind(DemoTenant.RootKind)),
                Cancellation);
        }

        await onPostgres.Host.DisposeAsync();

        // The next start finds harbor's slug taken, as it would for a complete demonstration, and says it is not.
        var logs = new RecordingLoggerProvider();
        await using var next = await onPostgres.StartAnotherHostAsync(Cancellation, services => services.AddSingleton<ILoggerProvider>(logs));

        // The warning names what is missing, down to the last project of the last tenant, harbor's project roles
        // among it, since nothing set harbor up for projects, and the two ways to a database that is seeded from
        // scratch: the AppHost's, new with every run, and the CLI stack's, reset.
        var last = DemoData.Tenants[^1].Projects[^1];
        logs.Entries.Should().ContainSingle(entry => entry.Category == typeof(DemoSeeder).FullName && entry.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("Missing:").And.Contain(harbor.Slug + ":").And.Contain(last.Number)
            .And.Contain("project role " + SampleCatalogue.CrewLead)
            .And.Contain("restart the AppHost").And.Contain("supabase db reset");
    }

    [Fact]
    public void An_older_demonstration_is_named_by_what_it_lacks()
    {
        // A file seeded before harbor had an access admin: Maud's seat and the access admin's role are not in it.
        var harbor = DemoData.Harbor;
        var maud = harbor.SeatOf(DemoPeople.Maud);
        var accessAdmin = harbor.Roles[SampleCatalogue.AccessAdmin];
        var seats = harbor.Seats.Select(seat => seat.Id).Append(harbor.Administrator.Id).Where(seat => seat != maud).ToHashSet();
        var roles = harbor.Roles.Values.Where(role => role != accessAdmin).ToHashSet();
        var projectRoles = harbor.ProjectRoles.Values.ToHashSet();
        var projects = harbor.Projects.Select(project => project.Id).ToHashSet();

        harbor.MissingFrom(seats, roles, projectRoles, projects).Should().Equal("seat Maud", "role access-admin");
        harbor.MissingFrom(seats.Append(maud).ToHashSet(), roles.Append(accessAdmin).ToHashSet(), projectRoles, projects).Should().BeEmpty();
    }

    [Fact]
    public void A_demonstration_seeded_before_a_crew_held_project_roles_is_named_by_them()
    {
        // A file seeded before a crew held project roles: every seat, role of the organization and project is in
        // it, and its tenants were never set up for projects, so none of them can open one. That is what it lacks.
        var harbor = DemoData.Harbor;
        var seats = harbor.Seats.Select(seat => seat.Id).Append(harbor.Administrator.Id).ToHashSet();
        var roles = harbor.Roles.Values.ToHashSet();
        var projects = harbor.Projects.Select(project => project.Id).ToHashSet();

        harbor.MissingFrom(seats, roles, new HashSet<ProjectRoleId>(), projects)
            .Should().Equal(SampleCatalogue.ProjectRoles.Select(starter => "project role " + starter.Key));
    }

    private static async Task<string> EverythingAdaSeesAsync(SampleFactory host)
    {
        using var ada = await host.ClientAsync("ada", DemoData.Harbor.Slug);

        var answers = new List<string>();
        foreach (var path in new[] { "/tenancy/units", "/tenancy/seats", "/tenancy/roles", "/projects" })
        {
            answers.Add(await ada.GetStringAsync(path, Cancellation));
        }

        return string.Join(Environment.NewLine, answers);
    }
}
