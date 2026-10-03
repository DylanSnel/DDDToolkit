using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy;
using Examples.Tenancy.Projects.Infrastructure.Access;
using Examples.Tenancy.Tenants.Contracts.TokenRoles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Examples.Tenancy.Tests.Supabase;

/// <summary>
/// The host on Postgres, started the way a deployment starts it: against a database the exported files made, as
/// the login role that owns nothing. What is held here is that it starts at all, with every start-up check
/// passed, and that the exported policies and privileges let a seat do its work and nothing of another tenant's.
/// The scenario classes ask what a request is answered; these ask the database beside it, and the host's start.
/// </summary>
/// <remarks>
/// They need Docker and Supabase's Postgres image, so they carry the samples' traits and stay out of the runs
/// that have neither.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class SampleOnPostgresTests(SampleSupabaseStack stack)
{
    private static readonly DemoTenant Harbor = DemoData.Harbor;

    private static readonly DemoTenant Meadow = DemoData.Meadow;

    /// <summary>The projects a caller sees, each with its unit and its owner, which the gateway asks Tenancy's schema for.</summary>
    private const string ProjectsWithNames = "{ projects { nodes { name unit { name } owner { displayName } } } }";

    /// <summary>
    /// What the database says to a statement that names an owner without the key that does: the Membership
    /// package's lock on the owner column, written from the projects' rules.
    /// </summary>
    private const string OwnerNamedWithoutTheKey = "The owner of a row of \"projects\".\"Projects\" is changed by a caller that holds projects.owner.change on it.";

    /// <summary>A role of the test's own that the policies hold: it bypasses nothing, and the login role cannot switch to it.</summary>
    private const string HeldToThePolicies = "tenancy_tests_held_to_the_policies";

    /// <summary>
    /// A database made wrong, one way for each check the host makes of it before it takes a request, with what the
    /// check says. Each is something a deployment can do by hand, and each would leave a hole no request shows.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Sql, string Says)> MadeWrong = new Dictionary<string, (string, string)>
    {
        // Every migration is applied: the host applies none itself.
        ["a migration that was never applied"] = (
            $"""
            DELETE FROM {ProjectsContext.Schema}."{HistoryRepository.DefaultTableName}"
            WHERE "MigrationId" = (SELECT max("MigrationId") FROM {ProjectsContext.Schema}."{HistoryRepository.DefaultTableName}")
            """,
            "The database is missing migrations:"),

        // Every function that runs as its owner is owned by a role the forced policies let through: owned by any
        // other, it would read no row and answer that nobody may do anything.
        ["a function that runs as an owner the policies hold"] = (
            $"""
            DO $do$ BEGIN CREATE ROLE {HeldToThePolicies} NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $do$;
            GRANT {HeldToThePolicies} TO {SampleSupabaseStack.MigrationRole};
            GRANT USAGE, CREATE ON SCHEMA {InspectionsContext.Schema} TO {HeldToThePolicies};
            CREATE FUNCTION {InspectionsContext.Schema}.made_wrong() RETURNS integer LANGUAGE sql SECURITY DEFINER SET search_path = '' AS 'SELECT 1';
            ALTER FUNCTION {InspectionsContext.Schema}.made_wrong() OWNER TO {HeldToThePolicies};
            """,
            "their owner is held to row level security"),

        // System work in a tenant cannot leave it, and nobody without a seat asks what Tenancy answers about one.
        ["a function of Tenancy's the anonymous role may ask"] = (
            $"GRANT EXECUTE ON FUNCTION {TenantsContext.Schema}.caller_seat() TO anon",
            $"anon may execute {TenantsContext.Schema}.caller_seat()"),

        // The few reads across tenants are the application's own, and no signed-in user's.
        ["a read across tenants a signed-in user may ask"] = (
            $"GRANT EXECUTE ON FUNCTION {TenantsContext.Schema}.role_keys_in_use() TO authenticated",
            $"authenticated may execute {TenantsContext.Schema}.role_keys_in_use()"),

        // The second lock on a tenant's single root: the policies hold a seat, and the index holds everyone.
        ["no unique index on a tenant's root"] = (
            $"""DROP INDEX {TenantsContext.Schema}."IX_OrganizationUnits_TenantId_WhereRoot" """,
            "no unique index that keeps a tenant's organization to a single root"),

        // The Membership package's lock on a crew, written from the projects' rules: a role given on a crew is one
        // of the tenant's, whoever writes the row.
        ["a crew's roles not held to the roles the caller sees"] = (
            $"""DROP POLICY "Members hold roles the caller sees (insert) for authenticated" ON {ProjectsContext.Schema}."{ProjectsContext.CrewRolesTable}" """,
            $"{ProjectsContext.Schema}.\"{ProjectsContext.CrewRolesTable}\" (authenticated is not held to the roles it sees)"),

        // The crew lead's project role stays in use, whatever statement archives it.
        ["the trigger that keeps the crew lead's role in use disabled"] = (
            $"""ALTER TABLE {ProjectsContext.Schema}."{ProjectsContext.ProjectRolesTable}" DISABLE TRIGGER projects_owner_role_stays""",
            "projects_owner_role_stays, is missing, disabled, or written from other rules"),

        // A project's owner changes only for a seat that holds the key that names one.
        ["no lock on a project's owner"] = (
            $"""DROP TRIGGER projects_owner_stays ON {ProjectsContext.Schema}."{ProjectsContext.ProjectsTable}" """,
            "(its owner column is not held to the key that changes the owner)"),
    };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> DatabasesMadeWrong => [.. MadeWrong.Keys];

    [Fact]
    public async Task The_host_starts_as_a_login_that_owns_nothing_and_a_seat_reads_its_own_tenants_projects()
    {
        await using var sample = await StartedAsync();

        // Started: every migration was found applied, the login role owns and holds nothing, the functions that
        // run as their owner are let through the forced policies, and Tenancy's second lock is in place. The
        // demonstration data was seeded as system work in each tenant, under the policies.
        using var inHarbor = await sample.Host.ClientAsync("tove", Harbor.Slug);
        using var inMeadow = await sample.Host.ClientAsync("tove", Meadow.Slug);

        // One person with a seat in each tenant: each seat reads its own tenant's projects, the ones it reaches.
        (await inHarbor.VisibleProjectsAsync()).Names().Should().Equal("Bay bridge");
        (await inMeadow.VisibleProjectsAsync()).Names().Should().Equal("Garden shed");

        // And nothing of the other tenant: her own project there does not exist here.
        using var acrossTenants = await inMeadow.GetAsync($"/projects/{Harbor.ProjectNamed("Bay bridge").Id.Value}", Cancellation);
        await acrossTenants.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);

        // A seat in harbor alone has none in meadow, and so reads nothing there.
        using var rheaInMeadow = await sample.Host.ClientAsync("rhea", Meadow.Slug);
        using var noSeat = await rheaInMeadow.GetAsync("/projects", Cancellation);
        noSeat.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // GraphQL answers the same, from the same use cases under the same policies: a seat reads its tenant's
        // projects, with the names Tenancy's schema adds to them through the gateway.
        ProjectsOf(await inHarbor.GraphQLDataAsync(ProjectsWithNames)).Should().Equal(("Bay bridge", "South Bay", "Ada"));
        ProjectsOf(await inMeadow.GraphQLDataAsync(ProjectsWithNames)).Should().Equal(("Garden shed", "Meadow Gardens", "Tove"));

        // A seat of another tenant: the project of harbor is not there for her seat in meadow, by its id either,
        // and a caller without a seat in meadow is refused in front of every field.
        var bayBridge = await inHarbor.ProjectNodeIdAsync(Harbor.ProjectNamed("Bay bridge"));
        (await inMeadow.GraphQLDataAsync("query($id: ID!) { project(id: $id) { name } }", new { id = bayBridge }))
            .GetProperty("project").ValueKind.Should().Be(JsonValueKind.Null);
        (await rheaInMeadow.GraphQLAsync(ProjectsWithNames)).SingleError().Code().Should().Be(TenancyRefusals.NotSeated);

        // What the host is logged in as: a role that reads no table by itself.
        await using var login = new NpgsqlConnection(sample.AsLoginRole);
        await login.OpenAsync(Cancellation);
        (await RefusedAsync(login, "SELECT count(*) FROM projects.\"Projects\"")).Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await RefusedAsync(login, "SELECT count(*) FROM tenancy.\"Seats\"")).Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_seat_opens_a_project_and_records_an_inspection_under_the_exported_policies()
    {
        await using var sample = await StartedAsync();
        var northCoast = Harbor.UnitNamed("North Coast");
        var pier = Harbor.ProjectNamed("Pier 7").Id.Value;

        // Rhea runs the north: she holds the key to open a project there, which is what the insert policy asks,
        // and the privilege to insert was written from that policy.
        using var rhea = await sample.Host.ClientAsync("rhea", Harbor.Slug);
        using (var opened = await rhea.PostAsJsonAsync("/projects", new { number = "P-900", name = "Lighthouse stairs", unitId = northCoast.Value }, Cancellation))
        {
            opened.StatusCode.Should().Be(HttpStatusCode.Created, await opened.Content.ReadAsStringAsync(Cancellation));
        }

        var lighthouse = (await rhea.VisibleProjectsAsync()).Named("Lighthouse stairs");
        var lighthouseId = lighthouse.GetProperty("id").GetGuid();

        // A change, under the update policy: the row is found and written back, with its version.
        using (var renamed = await rhea.PutAsJsonAsync($"/projects/{lighthouseId}/name", new { name = "Lighthouse steps" }, Cancellation))
        {
            renamed.IsSuccessStatusCode.Should().BeTrue(await renamed.Content.ReadAsStringAsync(Cancellation));
        }

        // Juno surveys on Pier 7's crew: the key to record reaches her through the crew, which Projects answers
        // to the policy on Inspections' table without that module reading a table of Projects'.
        using var juno = await sample.Host.ClientAsync("juno", Harbor.Slug);
        using (var recorded = await juno.PostAsJsonAsync($"/projects/{pier}/inspections", new { title = "Railing loose" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Cancellation));
        }

        var inspections = await juno.GetFromJsonAsync<JsonElement>($"/projects/{pier}/inspections", Cancellation);
        inspections.GetRawText().Should().Contain("Railing loose");

        // Tove sees neither project, so she records on neither: the application refuses first.
        using var tove = await sample.Host.ClientAsync("tove", Harbor.Slug);
        using (var refused = await tove.PostAsJsonAsync($"/projects/{pier}/inspections", new { title = "Not hers to record" }, Cancellation))
        {
            await refused.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);
        }

        // The rows say who wrote them, and the tenant's access history was kept from the first seeded event on.
        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        (await RowAsync(owner, $"SELECT \"CreatedByKind\", \"CreatedBySeat\"::text, \"ChangedByKind\", \"ChangedBySeat\"::text, \"Name\" FROM projects.\"Projects\" WHERE \"Id\" = '{lighthouseId}'"))
            .Should().Equal(["seat", Harbor.SeatOf(DemoPeople.Rhea).Value.ToString(), "seat", Harbor.SeatOf(DemoPeople.Rhea).Value.ToString(), "Lighthouse steps"]);
        (await RowAsync(owner, $"SELECT \"CreatedByKind\", \"CreatedBySeat\"::text FROM inspections.\"Inspections\" WHERE \"Title\" = 'Railing loose'"))
            .Should().Equal(["seat", Harbor.SeatOf(DemoPeople.Juno).Value.ToString()]);
        (await ScalarAsync<long>(owner, $"SELECT count(*) FROM tenancy.\"{TenantsContext.HistoryTable}\" WHERE \"TenantId\" = '{Harbor.Id.Value}'"))
            .Should().BePositive("every event of Tenancy's that changes access is kept in the history, and seeding a tenant raises many");
    }

    [Fact]
    public async Task A_role_a_seat_gives_is_kept_in_the_access_history_with_that_seat()
    {
        await using var sample = await StartedAsync();
        var hana = Harbor.SeatOf(DemoPeople.Hana).Value;
        var leo = Harbor.SeatOf(DemoPeople.Leo).Value;
        var observer = Harbor.Roles[SampleCatalogue.Observer].Value;

        // Hana, of the people office, gives Leo a role: the grant and its row in the history are one save, and the
        // history's policy takes a row from a seat only when the row names that seat.
        using var asHana = await sample.Host.ClientAsync("hana", Harbor.Slug);
        using (var given = await asHana.PostAsJsonAsync($"/tenancy/seats/{leo}/grants", new { unitId = Harbor.UnitNamed("North Coast").Value, roleId = observer }, Cancellation))
        {
            given.StatusCode.Should().Be(HttpStatusCode.NoContent, await given.Content.ReadAsStringAsync(Cancellation));
        }

        // The seeding gave every role as system work, so the one grant a seat made is hers: the row's own columns
        // name her seat, and so does the event it holds.
        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        var kept = await RowAsync(owner, $"""
            SELECT "TenantId"::text, "ActedById", "Payload"::text
            FROM tenancy."{TenantsContext.HistoryTable}"
            WHERE "EventName" = 'tenancy.organization-role-granted' AND "ActedByKind" = 'seat'
            """);
        kept[..2].Should().Equal(Harbor.Id.Value.ToString(), hana.ToString());

        using var payload = JsonDocument.Parse((string)kept[2]!);
        payload.RootElement.GetProperty("SeatId").GetGuid().Should().Be(leo);
        payload.RootElement.GetProperty("RoleId").GetGuid().Should().Be(observer);
        payload.RootElement.GetProperty("By").GetProperty("Kind").GetString().Should().Be("seat");
        payload.RootElement.GetProperty("By").GetProperty("Seat").GetGuid().Should().Be(hana);
    }

    [Fact]
    public async Task An_operator_reads_every_tenant_as_its_own_role_and_the_database_refuses_its_writes()
    {
        await using var sample = await StartedAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id.Value;
        var everyProject = Harbor.Projects.Count + Meadow.Projects.Count;

        // What the host's connections are for an operator's request: the operators' own database role, the
        // operator's identity in the claims, and no tenant. A statement of its own, past every check of the
        // application.
        await using var orla = new NpgsqlConnection(sample.AsLoginRole);
        await orla.OpenAsync(Cancellation);
        await RunAsync(orla, $$"""
            SET ROLE {{SampleTokenRoles.Operator}};
            SELECT set_config('request.jwt.claims', '{"sub":"{{DemoPeople.Orla.Id}}","role":"{{SampleTokenRoles.Operator}}"}', false);
            """);

        // It reads every tenant's rows, of Tenancy's tables and of the modules'.
        (await ScalarAsync<long>(orla, "SELECT count(*) FROM tenancy.\"Tenants\"")).Should().Be(DemoData.Tenants.Count);
        (await ScalarAsync<long>(orla, "SELECT count(*) FROM projects.\"Projects\"")).Should().Be(everyProject);
        (await ScalarAsync<long>(orla, "SELECT count(*) FROM projects.\"ProjectCrewMembers\"")).Should().BePositive();
        (await ScalarAsync<long>(orla, "SELECT count(*) FROM tenancy.\"TenancyEventLog\"")).Should().BePositive();

        // And writes nothing: the role holds no privilege to add, change or remove a row anywhere.
        string[] writes =
        [
            $"UPDATE projects.\"Projects\" SET \"Name\" = 'Renamed by staff' WHERE \"Id\" = '{pier}'",
            "DELETE FROM projects.\"Projects\"",
            "INSERT INTO projects.\"Projects\" SELECT * FROM projects.\"Projects\" LIMIT 1",
            "DELETE FROM projects.\"ProjectCrewMembers\"",
            "INSERT INTO inspections.\"Inspections\" SELECT * FROM inspections.\"Inspections\" LIMIT 1",
            "DELETE FROM inspections.\"Inspections\"",
            "UPDATE tenancy.\"Seats\" SET \"Status\" = 'Suspended'",
            "DELETE FROM tenancy.\"TenancyEventLog\"",
        ];
        foreach (var write in writes)
        {
            (await RefusedAsync(orla, write)).Should().Be(PostgresErrorCodes.InsufficientPrivilege, "an operator's role may not run: {0}", write);
        }

        // Those are a few statements; the privileges themselves say it of every table. No table of the three
        // schemas, the modules' own and whatever else is there, lets the role add, change, remove or empty.
        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        const string Tables = $"""
            SELECT n.nspname || '.' || c.relname
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p') AND n.nspname IN ('{TenantsContext.Schema}', '{ProjectsContext.Schema}', '{InspectionsContext.Schema}')
            """;
        (await ListAsync(owner, Tables + " ORDER BY 1"))
            .Should().Contain((await SampleTables.OfTheModelsAsync(sample.Host.Services, Cancellation)).Select(table => table.Name), "the tables asked about are the ones the modules' models map");
        (await ListAsync(owner, $"{Tables} AND has_table_privilege('{SampleTokenRoles.Operator}', c.oid, 'INSERT, UPDATE, DELETE, TRUNCATE') ORDER BY 1"))
            .Should().BeEmpty("the operators' role reads, and holds no privilege to write on any table");

        // Through the host it is the same role that reads: every tenant, and one tenant's projects.
        using var asOperator = await sample.Host.ClientAsync("orla", tenant: null);
        var tenants = await asOperator.GetFromJsonAsync<JsonElement>("/operations/tenants", Cancellation);
        tenants.GetProperty("items").EnumerateArray().Select(tenant => tenant.Text("slug")).Should().Equal(Harbor.Slug, Meadow.Slug);
        (await asOperator.GetFromJsonAsync<JsonElement>($"/operations/tenants/{Meadow.Id.Value}/projects", Cancellation))
            .EnumerateArray().Names().Should().Equal("Garden shed");

        // And nothing changed.
        (await ScalarAsync<string>(owner, $"SELECT \"Name\" FROM projects.\"Projects\" WHERE \"Id\" = '{pier}'")).Should().Be("Pier 7");
    }

    [Fact]
    public async Task An_ended_crew_role_and_an_ended_membership_give_nothing_through_the_crew()
    {
        await using var sample = await StartedAsync();
        var bridge = Harbor.ProjectNamed("Bay bridge").Id.Value;
        var seat = Harbor.SeatOf(DemoPeople.Tove).Value;

        // The two functions the policies ask about a crew, asked as Tove's seat in harbor, the way the host's
        // connections ask them: the role of a signed-in user, her identity in the claims, and the tenant.
        await using var tove = new NpgsqlConnection(sample.AsLoginRole);
        await tove.OpenAsync(Cancellation);
        await RunAsync(tove, $$"""
            SET ROLE authenticated;
            SELECT set_config('request.jwt.claims', '{"sub":"{{DemoPeople.Tove.Id}}","role":"authenticated"}', false);
            SELECT set_config('tenancy.caller_tenant', '{{Harbor.Id.Value}}', false);
            """);

        async Task<(bool OnTheCrew, bool SeesThroughTheCrew)> AskAsync()
            => (await ScalarAsync<bool>(tove, $"SELECT '{bridge}' = ANY (ARRAY(SELECT projects.crew_member_project_ids()))"),
                await ScalarAsync<bool>(tove, $"SELECT '{bridge}' = ANY (ARRAY(SELECT projects.crew_project_ids('{ProjectKeys.View}')))"));

        // A surveyor on the crew of Bay bridge: on it, and holding the key to see it through her role there.
        (await AskAsync()).Should().Be((true, true));

        // Her role on the crew ended a minute ago: still on the crew, and no key through it.
        await sample.AsOwnerAsync($"""
            UPDATE projects."{ProjectsContext.CrewRolesTable}" held SET "EndsAt" = now() - interval '1 minute'
            FROM projects."{ProjectsContext.CrewTable}" member
            WHERE member."ProjectId" = held."ProjectId" AND member."Id" = held."CrewMemberId"
              AND member."ProjectId" = '{bridge}' AND member."SeatId" = '{seat}'
            """, Cancellation);
        (await AskAsync()).Should().Be((true, false));

        // Her membership ended too: not on the crew, whatever role is still written under it.
        await sample.AsOwnerAsync($"""
            UPDATE projects."{ProjectsContext.CrewRolesTable}" held SET "EndsAt" = NULL
            FROM projects."{ProjectsContext.CrewTable}" member
            WHERE member."ProjectId" = held."ProjectId" AND member."Id" = held."CrewMemberId"
              AND member."ProjectId" = '{bridge}' AND member."SeatId" = '{seat}';
            UPDATE projects."{ProjectsContext.CrewTable}" SET "EndsAt" = now() - interval '1 minute'
            WHERE "ProjectId" = '{bridge}' AND "SeatId" = '{seat}'
            """, Cancellation);
        (await AskAsync()).Should().Be((false, false), "a role that still runs gives nothing inside a membership that ended");

        // And the host answers the same: the project she reached only through its crew is gone from her list.
        using var inHarbor = await sample.Host.ClientAsync("tove", Harbor.Slug);
        (await inHarbor.VisibleProjectsAsync()).Names().Should().BeEmpty();
    }

    /// <summary>
    /// The policy for changing a project is coarser than the application: it lets a seat that holds any key that
    /// changes one write the row. Two columns decide who reaches the project at all, so the database holds those
    /// itself, each with a trigger: the unit a project is at, the module's own, and the seat that owns it, the
    /// Membership package's, written from the projects' rules. Each statement here goes round the application, as
    /// a seat, with everything else about it in order.
    /// </summary>
    [Fact]
    public async Task A_statement_that_goes_round_the_application_moves_no_project_and_names_no_owner_without_the_keys_for_it()
    {
        await using var sample = await StartedAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id.Value;
        var (northInland, southBay) = (Harbor.UnitNamed("North Inland").Value, Harbor.UnitNamed("South Bay").Value);
        var juno = Harbor.SeatOf(DemoPeople.Juno).Value;

        // What the host's connections are for a seat's request: the role of a signed-in user, the person's identity
        // in the claims, and the tenant.
        async Task<NpgsqlConnection> AsSeatAsync(DemoPerson person)
        {
            var connection = new NpgsqlConnection(sample.AsLoginRole);
            await connection.OpenAsync(Cancellation);
            await RunAsync(connection, $$"""
                SET ROLE authenticated;
                SELECT set_config('request.jwt.claims', '{"sub":"{{person.Id}}","role":"authenticated"}', false);
                SELECT set_config('tenancy.caller_tenant', '{{Harbor.Id.Value}}', false);
                """);
            return connection;
        }

        // A change to Pier 7 as a statement of the seat's own, recorded as changed by that seat, which another
        // trigger holds it to: so what refuses below is the rule about the unit and the owner, and nothing else.
        string Changing(DemoPerson by, string set)
            => $"""UPDATE projects."Projects" SET {set}, "ChangedByKind" = 'seat', "ChangedBySeat" = '{Harbor.SeatOf(by).Value}', "ChangedByIdentity" = NULL WHERE "Id" = '{pier}'""";

        async Task ShouldBeRefusedAsync(NpgsqlConnection connection, DemoPerson by, string set, string because)
        {
            await using var command = new NpgsqlCommand(Changing(by, set), connection);
            var refused = (await FluentActions.Awaiting(() => command.ExecuteNonQueryAsync(Cancellation)).Should().ThrowAsync<PostgresException>(set)).Which;
            (refused.SqlState, refused.MessageText).Should().Be((PostgresErrorCodes.InsufficientPrivilege, because), set);
        }

        async Task ShouldChangeAsync(NpgsqlConnection connection, DemoPerson by, string set)
        {
            await using var command = new NpgsqlCommand(Changing(by, set), connection);
            (await command.ExecuteNonQueryAsync(Cancellation)).Should().Be(1, "{0} may: {1}", by.Name, set);
        }

        // Leo leads Pier 7's crew, which lets him edit and close the project and manage its crew, and he holds
        // nothing at a unit. The policy lets him write the row, and he renames the project with a statement.
        await using (var leo = await AsSeatAsync(DemoPeople.Leo))
        {
            await ShouldChangeAsync(leo, DemoPeople.Leo, "\"Name\" = 'Pier 7, north side'");

            // The unit: to no unit at all, to a unit of another tenant, and to one of his own tenant where he may
            // open no project.
            await ShouldBeRefusedAsync(leo, DemoPeople.Leo, $"\"UnitId\" = '{Guid.NewGuid()}'", UnitChangesWithItsKeys.UnitOfAnotherTenant);
            await ShouldBeRefusedAsync(leo, DemoPeople.Leo, $"\"UnitId\" = '{Meadow.Root.Value}'", UnitChangesWithItsKeys.UnitOfAnotherTenant);
            await ShouldBeRefusedAsync(leo, DemoPeople.Leo, $"\"UnitId\" = '{southBay}'", UnitChangesWithItsKeys.MovedWithoutTheKeys);

            // The owner: no crew role names one.
            await ShouldBeRefusedAsync(leo, DemoPeople.Leo, $"\"OwnerSeatId\" = '{juno}'", OwnerNamedWithoutTheKey);
        }

        // Maud administers access: she names owners and manages crews everywhere in harbor, and edits and opens
        // nothing. The policy would let her move the project, since she holds a key at every unit; the trigger
        // asks for the two keys a move takes.
        await using (var maud = await AsSeatAsync(DemoPeople.Maud))
        {
            await ShouldBeRefusedAsync(maud, DemoPeople.Maud, $"\"UnitId\" = '{northInland}'", UnitChangesWithItsKeys.MovedWithoutTheKeys);
            await ShouldChangeAsync(maud, DemoPeople.Maud, $"\"OwnerSeatId\" = '{juno}'");
        }

        // Rhea manages North: she edits the project where it is and opens projects at North Inland, so she moves
        // it there, and no further than her area.
        await using (var rhea = await AsSeatAsync(DemoPeople.Rhea))
        {
            await ShouldChangeAsync(rhea, DemoPeople.Rhea, $"\"UnitId\" = '{northInland}'");
            await ShouldBeRefusedAsync(rhea, DemoPeople.Rhea, $"\"UnitId\" = '{southBay}'", UnitChangesWithItsKeys.MovedWithoutTheKeys);
        }

        // What was let through is all that changed.
        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        (await RowAsync(owner, $"SELECT \"Name\", \"UnitId\", \"OwnerSeatId\" FROM projects.\"Projects\" WHERE \"Id\" = '{pier}'"))
            .Should().Equal("Pier 7, north side", northInland, juno);
    }

    /// <summary>
    /// Who may change a crew, and name an owner, is what the commands require; a crew's rows and the owner column
    /// are held to the same keys by the database, with the lock the Membership package writes from the projects'
    /// rules. A crew member whose role lets them edit the project writes its row, and gives themself no role and
    /// names no owner, whatever statement they send.
    /// </summary>
    [Fact]
    public async Task A_crew_member_who_may_edit_gives_themself_no_role_and_names_no_owner_by_writing_to_the_database()
    {
        await using var sample = await StartedAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id.Value;
        var vic = Harbor.SeatOf(DemoPeople.Vic).Value;
        var lead = Harbor.ProjectRoles[SampleCatalogue.CrewLead].Value;

        // Vic is given a scribe's project role on Pier 7: he edits the project, and manages neither its crew nor
        // its owner.
        using (var ada = await sample.Host.ClientAsync("ada", Harbor.Slug))
        using (var leo = await sample.Host.ClientAsync("leo", Harbor.Slug))
        {
            using var made = await ada.PostAsJsonAsync("/project-roles", new { name = "Scribe", keys = new[] { ProjectKeys.View, ProjectKeys.Edit } }, Cancellation);
            made.StatusCode.Should().Be(HttpStatusCode.Created);
            var scribe = (await made.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();
            using var given = await leo.GiveCrewRoleAsync(Harbor.ProjectNamed("Pier 7"), Harbor.SeatOf(DemoPeople.Vic), scribe);
            given.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // His own statements, as the host's connections are for his requests.
        await using var connection = new NpgsqlConnection(sample.AsLoginRole);
        await connection.OpenAsync(Cancellation);
        await RunAsync(connection, $$"""
            SET ROLE authenticated;
            SELECT set_config('request.jwt.claims', '{"sub":"{{DemoPeople.Vic.Id}}","role":"authenticated"}', false);
            SELECT set_config('tenancy.caller_tenant', '{{Harbor.Id.Value}}', false);
            """);

        // He renames the project: editing is his.
        await RunAsync(connection, $"""UPDATE projects."Projects" SET "Name" = 'Pier 7, by Vic', "ChangedByKind" = 'seat', "ChangedBySeat" = '{vic}', "ChangedByIdentity" = NULL WHERE "Id" = '{pier}'""");

        // He gives himself the crew lead's role, as a row of his own membership.
        (await RefusedAsync(connection, $"""
            INSERT INTO projects."{ProjectsContext.CrewRolesTable}" ("ProjectId", "CrewMemberId", "RoleId", "StartsAt", "EndsAt", "GivenBy")
            SELECT member."ProjectId", member."Id", '{lead}', now(), NULL, '{vic}'
            FROM projects."{ProjectsContext.CrewTable}" member WHERE member."ProjectId" = '{pier}' AND member."SeatId" = '{vic}'
            """)).Should().Be(PostgresErrorCodes.InsufficientPrivilege, "giving a role on a crew takes managing it, in the database as in the command");

        // Nor does he end a role of the crew's, or take one away: he reads them, and writes none.
        foreach (var sql in new[]
                 {
                     $"""UPDATE projects."{ProjectsContext.CrewRolesTable}" SET "EndsAt" = now() WHERE "ProjectId" = '{pier}'""",
                     $"""DELETE FROM projects."{ProjectsContext.CrewRolesTable}" WHERE "ProjectId" = '{pier}'""",
                 })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            (await command.ExecuteNonQueryAsync(Cancellation)).Should().Be(0, "a crew's roles are written by whoever manages the crew: {0}", sql);
        }

        // He names himself owner.
        await using (var named = new NpgsqlCommand(
                         $"""UPDATE projects."Projects" SET "OwnerSeatId" = '{vic}', "ChangedByKind" = 'seat', "ChangedBySeat" = '{vic}', "ChangedByIdentity" = NULL WHERE "Id" = '{pier}'""",
                         connection))
        {
            var refused = (await FluentActions.Awaiting(() => named.ExecuteNonQueryAsync(Cancellation)).Should().ThrowAsync<PostgresException>()).Which;
            (refused.SqlState, refused.MessageText).Should().Be((PostgresErrorCodes.InsufficientPrivilege, OwnerNamedWithoutTheKey));
        }

        // What was let through is all that changed: the name, and not the owner or any role of the crew.
        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        (await RowAsync(owner, $"SELECT \"Name\", \"OwnerSeatId\" FROM projects.\"Projects\" WHERE \"Id\" = '{pier}'"))
            .Should().Equal("Pier 7, by Vic", Harbor.SeatOf(DemoPeople.Leo).Value);
        (await ScalarAsync<long>(owner, $"""SELECT count(*) FROM projects."{ProjectsContext.CrewRolesTable}" WHERE "ProjectId" = '{pier}' AND "EndsAt" IS NULL"""))
            .Should().Be(4, "the lead's, the surveyor's, the observer's and his scribe's role, each as it was");
    }

    /// <summary>
    /// A crew role is one of the tenant's project roles, and the database holds a crew to that too: whoever
    /// writes a crew's roles gives one the database shows them, and Tenancy's policies show a seat its own
    /// tenant's. The Membership package's functions find a role by its id, so a role of another tenant written
    /// onto a crew would give its keys there; it is never written.
    /// </summary>
    [Fact]
    public async Task A_seat_that_manages_a_crew_gives_no_project_role_of_another_tenant_by_writing_to_the_database()
    {
        await using var sample = await StartedAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id.Value;
        var vic = Harbor.SeatOf(DemoPeople.Vic).Value;

        // Leo owns Pier 7, and so manages its crew. Vic is on it as an observer.
        string RoleGivenToVic(Guid role) => $"""
            INSERT INTO projects."{ProjectsContext.CrewRolesTable}" ("ProjectId", "CrewMemberId", "RoleId", "StartsAt", "EndsAt", "GivenBy")
            SELECT member."ProjectId", member."Id", '{role}', now() - interval '1 hour', NULL, '{Harbor.SeatOf(DemoPeople.Leo).Value}'
            FROM projects."{ProjectsContext.CrewTable}" member WHERE member."ProjectId" = '{pier}' AND member."SeatId" = '{vic}'
            """;

        // Meadow's crew lead, which closes a project and manages its crew, given to Vic on Pier 7.
        await using (var leo = await AsSeatAsync(sample, DemoPeople.Leo, Harbor))
        {
            await using var command = new NpgsqlCommand(RoleGivenToVic(Meadow.ProjectRoles[SampleCatalogue.CrewLead].Value), leo);
            var refused = (await FluentActions.Awaiting(() => command.ExecuteNonQueryAsync(Cancellation)).Should().ThrowAsync<PostgresException>()).Which;
            refused.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            refused.MessageText.Should().Contain("Members hold roles the caller sees (insert) for authenticated", "the role is not one harbor's seats see");
        }

        // A surveyor's role of harbor's own he gives as before: it is the tenant of the role, and not the giving, that is held.
        await using (var leo = await AsSeatAsync(sample, DemoPeople.Leo, Harbor))
        {
            await RunAsync(leo, RoleGivenToVic(Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value));
        }

        // Meadow's keys are held on none of harbor's projects: Vic holds what harbor's roles give him on Pier 7, and no more.
        await using var asVic = await AsSeatAsync(sample, DemoPeople.Vic, Harbor);
        (await ListAsync(asVic, $"SELECT id::text FROM projects.crew_project_ids('{ProjectKeys.Close}') AS id")).Should().BeEmpty();
        (await ListAsync(asVic, $"SELECT id::text FROM projects.project_ids_where_i_hold('{ProjectKeys.ManageCrew}') AS id")).Should().BeEmpty();
        (await ListAsync(asVic, $"SELECT id::text FROM projects.crew_project_ids('{InspectionKeys.Record}') AS id")).Should().Equal(pier.ToString());

        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        (await ScalarAsync<long>(owner, $"""SELECT count(*) FROM projects."{ProjectsContext.CrewRolesTable}" WHERE "RoleId" = '{Meadow.ProjectRoles[SampleCatalogue.CrewLead].Value}' AND "ProjectId" = '{pier}'"""))
            .Should().Be(0);
    }

    /// <summary>
    /// A crew is seats of the project's tenant, and the database holds a crew to that too: a seat that manages a
    /// crew puts none of another tenant's on it, and moves no place on the crew to another seat, whatever statement
    /// it sends. A seat of another tenant on a crew would be answered the project, acting in its own tenant, by
    /// the projects' functions.
    /// </summary>
    [Fact]
    public async Task A_seat_that_manages_a_crew_puts_no_seat_of_another_tenant_on_it_by_writing_to_the_database()
    {
        await using var sample = await StartedAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id.Value;
        var leo = Harbor.SeatOf(DemoPeople.Leo).Value;

        string Adding(Guid seat) => $"""
            INSERT INTO projects."{ProjectsContext.CrewTable}" ("Id", "ProjectId", "SeatId", "StartsAt", "EndsAt", "AddedBy")
            VALUES ('{Guid.NewGuid()}', '{pier}', '{seat}', now() - interval '1 hour', NULL, '{leo}')
            """;

        // Leo owns Pier 7, and so manages its crew: Tove's seat in meadow he puts on it by no statement.
        await using (var asLeo = await AsSeatAsync(sample, DemoPeople.Leo, Harbor))
        {
            await using var command = new NpgsqlCommand(Adding(Meadow.SeatOf(DemoPeople.Tove).Value), asLeo);
            var refused = (await FluentActions.Awaiting(() => command.ExecuteNonQueryAsync(Cancellation)).Should().ThrowAsync<PostgresException>()).Which;
            refused.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            refused.MessageText.Should().Contain($"{CrewSeatsOfTheProjectsTenant.Policy} (insert) for authenticated", "the seat is not one of harbor's");
        }

        // A seat of harbor's own he puts on it as before.
        await using (var asLeo = await AsSeatAsync(sample, DemoPeople.Leo, Harbor))
        {
            await RunAsync(asLeo, Adding(Harbor.SeatOf(DemoPeople.Hana).Value));
        }

        // Nor does he move Juno's place on the crew, with her surveyor's role, to another seat: who a place is of is
        // written once, and the privileges leave the column out.
        await using (var asLeo = await AsSeatAsync(sample, DemoPeople.Leo, Harbor))
        {
            (await RefusedAsync(asLeo, $"""UPDATE projects."{ProjectsContext.CrewTable}" SET "SeatId" = '{Meadow.SeatOf(DemoPeople.Tove).Value}' WHERE "ProjectId" = '{pier}' AND "SeatId" = '{Harbor.SeatOf(DemoPeople.Juno).Value}'"""))
                .Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        // Meadow's seats are on none of harbor's crews, and Tove in meadow is answered nothing of Pier 7.
        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        (await ScalarAsync<long>(owner, $"""SELECT count(*) FROM projects."{ProjectsContext.CrewTable}" WHERE "ProjectId" = '{pier}' AND "SeatId" = '{Meadow.SeatOf(DemoPeople.Tove).Value}'"""))
            .Should().Be(0);
        await using var toveInMeadow = await AsSeatAsync(sample, DemoPeople.Tove, Meadow);
        (await ListAsync(toveInMeadow, "SELECT id::text FROM projects.project_ids_i_see() AS id")).Should().NotContain(pier.ToString());
    }

    /// <summary>
    /// Who owns a new project is held by the rule that adds it: the Membership package's lock holds an owner that
    /// changes, and a new row had none. A seat that may open projects opens one for itself, and for somebody else
    /// only where it may name owners, as <c>OpenProject</c> asks, whatever statement it sends.
    /// </summary>
    [Fact]
    public async Task A_seat_that_may_open_projects_names_nobody_else_their_owner_by_writing_to_the_database()
    {
        await using var sample = await StartedAsync();
        var northCoast = Harbor.UnitNamed("North Coast").Value;
        var (leo, juno) = (Harbor.SeatOf(DemoPeople.Leo).Value, Harbor.SeatOf(DemoPeople.Juno).Value);

        // Ada makes a role of the organization that opens projects and names no owner, and gives it to Leo where his
        // seat is placed.
        using (var ada = await sample.Host.ClientAsync("ada", Harbor.Slug))
        {
            using var made = await ada.PostAsJsonAsync("/tenancy/roles", new { name = "Opener", keys = new[] { ProjectKeys.View, ProjectKeys.Open } }, Cancellation);
            made.StatusCode.Should().Be(HttpStatusCode.Created, await made.Content.ReadAsStringAsync(Cancellation));
            var opener = (await made.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();
            using var given = await ada.PostAsJsonAsync($"/tenancy/seats/{leo}/grants", new { unitId = northCoast, roleId = opener }, Cancellation);
            given.StatusCode.Should().Be(HttpStatusCode.NoContent, await given.Content.ReadAsStringAsync(Cancellation));
        }

        string Opening(string number, Guid owner) => $"""
            INSERT INTO projects."{ProjectsContext.ProjectsTable}" ("Id", "TenantId", "Version", "Number", "Name", "UnitId", "State", "OwnerSeatId", "CreatedByKind", "CreatedBySeat", "ChangedByKind", "ChangedBySeat")
            VALUES ('{Guid.NewGuid()}', '{Harbor.Id.Value}', 1, '{number}', 'Opened by a statement', '{northCoast}', 'Open', '{owner}', 'seat', '{leo}', 'seat', '{leo}')
            """;

        await using var asLeo = await AsSeatAsync(sample, DemoPeople.Leo, Harbor);

        // For Juno: refused, by the rule that adds a project.
        (await RefusedAsync(asLeo, Opening("P-901", juno))).Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        // For himself: added.
        await RunAsync(asLeo, Opening("P-902", leo));

        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        (await ListAsync(owner, $"""SELECT "Number" || ' ' || "OwnerSeatId"::text FROM projects."{ProjectsContext.ProjectsTable}" WHERE "Number" IN ('P-901', 'P-902')"""))
            .Should().Equal($"P-902 {leo}");
    }

    /// <summary>
    /// A project role's row says what everybody who holds it may do, and the database holds whoever may change it
    /// to what the commands hold them to: where a role came from is written once, and the crew lead's role, which
    /// every owner holds, is not archived, whatever statement reaches the database.
    /// </summary>
    [Fact]
    public async Task A_role_manager_moves_no_project_roles_origin_and_archives_no_crew_leads_role_by_writing_to_the_database()
    {
        await using var sample = await StartedAsync();
        var lead = Harbor.ProjectRoles[SampleCatalogue.CrewLead].Value;
        var observer = Harbor.ProjectRoles[SampleCatalogue.Observer].Value;

        // Ada manages harbor's roles for the whole tenant, which is what the commands on project roles ask.
        await using var ada = await AsSeatAsync(sample, DemoPeople.Ada, Harbor);

        // Where the crew lead's role came from: the privileges the export wrote leave the column out.
        (await RefusedAsync(ada, $"""UPDATE projects."{ProjectsContext.ProjectRolesTable}" SET "MadeFrom" = NULL WHERE "Id" = '{lead}'"""))
            .Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        // Archiving it: the trigger the Membership package writes from the projects' rules.
        await using (var archive = new NpgsqlCommand($"""UPDATE projects."{ProjectsContext.ProjectRolesTable}" SET "Status" = 'Archived' WHERE "Id" = '{lead}'""", ada))
        {
            var refused = (await FluentActions.Awaiting(() => archive.ExecuteNonQueryAsync(Cancellation)).Should().ThrowAsync<PostgresException>()).Which;
            (refused.SqlState, refused.MessageText).Should().Be((
                PostgresErrorCodes.CheckViolation,
                $"A row of \"projects\".\"{ProjectsContext.ProjectRolesTable}\" made from the starter role '{SampleCatalogue.CrewLead}' is the role every owner of a Project holds, and is not archived."));
        }

        // What she may do she does: she renames the crew lead's role and archives the observer's, under the rule
        // that asks the one question of whether she manages the tenant's roles.
        await using (var renamed = new NpgsqlCommand($"""UPDATE projects."{ProjectsContext.ProjectRolesTable}" SET "Name" = 'Lead hand' WHERE "Id" = '{lead}'""", ada))
        {
            (await renamed.ExecuteNonQueryAsync(Cancellation)).Should().Be(1);
        }

        await using (var archived = new NpgsqlCommand($"""UPDATE projects."{ProjectsContext.ProjectRolesTable}" SET "Status" = 'Archived' WHERE "Id" = '{observer}'""", ada))
        {
            (await archived.ExecuteNonQueryAsync(Cancellation)).Should().Be(1);
        }

        // And nothing of another tenant's. Meadow's crew lead's role is no row she changes: a statement that finds
        // its rows finds them as she reads them, and the module's rule reads her only harbor's.
        await using (var meadows = new NpgsqlCommand($"""UPDATE projects."{ProjectsContext.ProjectRolesTable}" SET "Name" = 'Taken' WHERE "Id" = '{Meadow.ProjectRoles[SampleCatalogue.CrewLead].Value}'""", ada))
        {
            (await meadows.ExecuteNonQueryAsync(Cancellation)).Should().Be(0);
        }

        // Nor does she add a project role to meadow. The rule that lets her add one asks only whether she manages the
        // tenant's roles, and leaves the tenant to Tenancy's restrictive policy, which is what refuses this row.
        await using (var planted = new NpgsqlCommand(
                         $"""
                         INSERT INTO projects."{ProjectsContext.ProjectRolesTable}" ("Id", "TenantId", "Version", "Name", "Description", "Keys", "Status", "MadeFrom")
                         VALUES ('{Guid.NewGuid()}', '{Meadow.Id.Value}', 1, 'Planted', '', ARRAY['{ProjectKeys.View}', '{ProjectKeys.Close}']::text[], 'Active', NULL)
                         """,
                         ada))
        {
            var refused = (await FluentActions.Awaiting(() => planted.ExecuteNonQueryAsync(Cancellation)).Should().ThrowAsync<PostgresException>()).Which;
            refused.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            refused.MessageText.Should().Contain("Kept to its tenant (all) for authenticated", "a row of meadow's is not harbor's to add");
        }

        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        (await RowAsync(owner, $"""SELECT "Name", "Status", "MadeFrom" FROM projects."{ProjectsContext.ProjectRolesTable}" WHERE "Id" = '{lead}'"""))
            .Should().Equal("Lead hand", "Active", SampleCatalogue.CrewLead);
        (await ScalarAsync<long>(owner, $"""SELECT count(*) FROM projects."{ProjectsContext.ProjectRolesTable}" WHERE "Name" = 'Planted'""")).Should().Be(0);
    }

    /// <summary>
    /// A role manager writes a project role's keys, and the privileges let a statement of its own write any key
    /// there. What a crew role gives is cut where it is read, by the questions and by the projects' functions
    /// alike: whoever holds the role is given none of the keys no crew gives, whatever its row says.
    /// </summary>
    [Fact]
    public async Task A_role_manager_writes_keys_no_crew_gives_into_a_project_role_and_its_holders_are_given_none_of_them()
    {
        await using var sample = await StartedAsync();
        var pier = Harbor.ProjectNamed("Pier 7").Id.Value;
        var surveyor = Harbor.ProjectRoles[SampleCatalogue.Surveyor].Value;
        var noCrewGives = string.Join(", ", SampleOnPostgres.KeysNoCrewGives.Select(key => $"'{key}'"));

        // Ada writes them into the surveyor's role, which Juno holds on Pier 7's crew: the database lets her.
        await using (var ada = await AsSeatAsync(sample, DemoPeople.Ada, Harbor))
        await using (var widened = new NpgsqlCommand($"""UPDATE projects."{ProjectsContext.ProjectRolesTable}" SET "Keys" = "Keys" || ARRAY[{noCrewGives}]::text[] WHERE "Id" = '{surveyor}'""", ada))
        {
            (await widened.ExecuteNonQueryAsync(Cancellation)).Should().Be(1);
        }

        // The application answers Juno none of them on Pier 7, and the key the role gave her before it still.
        using var juno = await sample.Host.ClientAsync("juno", Harbor.Slug);
        foreach (var key in SampleOnPostgres.KeysNoCrewGives)
        {
            var answer = await juno.GetFromJsonAsync<JsonElement>($"/access/projects/{pier}?key={key}", Cancellation);
            answer.GetProperty("allowed").GetBoolean().Should().BeFalse(key);
        }

        (await juno.GetFromJsonAsync<JsonElement>($"/access/projects/{pier}?key={InspectionKeys.Record}", Cancellation)).GetProperty("allowed").GetBoolean().Should().BeTrue();

        // And neither do the functions the policies ask, asked as her seat.
        await using var asJuno = await AsSeatAsync(sample, DemoPeople.Juno, Harbor);
        foreach (var key in SampleOnPostgres.KeysNoCrewGives)
        {
            (await ListAsync(asJuno, $"SELECT id::text FROM projects.project_ids_where_i_hold('{key}') AS id")).Should().BeEmpty(key);
        }

        (await ListAsync(asJuno, $"SELECT id::text FROM projects.project_ids_where_i_hold('{InspectionKeys.Record}') AS id")).Should().Equal(pier.ToString());
    }

    [Fact]
    public async Task A_rental_while_the_system_is_the_caller_draws_on_the_background_pool()
    {
        await using var sample = await StartedAsync();
        var contexts = sample.Host.Services.GetRequiredService<IDbContextFactory<ProjectsContext>>();

        async Task<string> ApplicationNameAsync()
        {
            await using var context = await contexts.CreateDbContextAsync(Cancellation);
            return await context.Database.SqlQueryRaw<string>("SELECT current_setting('application_name') AS \"Value\"").SingleAsync(Cancellation);
        }

        // The toolkit's own bookkeeping, which is what the system caller is: the connections kept for the background.
        using (Callers.Begin(Caller.System))
        {
            (await ApplicationNameAsync()).Should().Be("tenancy-background");
        }

        // Anyone else: the connections requests are answered on. Read at each rental, so the same factory serves both.
        using (Callers.Begin(Caller.User(DemoPeople.Tove.Id)))
        {
            (await ApplicationNameAsync()).Should().Be("tenancy-requests");
        }

        // The pollers run as the system caller, so they are on the background's connections, and a request is not.
        using var tove = await sample.Host.ClientAsync("tove", Harbor.Slug);
        (await tove.VisibleProjectsAsync()).Should().NotBeEmpty();
        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        (await ListAsync(owner, $"SELECT DISTINCT application_name FROM pg_stat_activity WHERE datname = current_database() AND usename = '{SampleOnPostgres.LoginRole}' ORDER BY 1"))
            .Should().Equal("tenancy-background", "tenancy-requests");
    }

    [Fact]
    public async Task A_graphql_query_runs_as_its_caller_on_the_connections_requests_are_answered_on()
    {
        await using var sample = await StartedAsync();
        using var ada = await sample.Host.ClientAsync("ada", Harbor.Slug);
        using var tove = await sample.Host.ClientAsync("tove", Harbor.Slug);

        // Ada manages all of harbor, so the application shows her every project of it; Tove sees one, through its crew.
        ProjectsOf(await ada.GraphQLDataAsync(ProjectsWithNames)).Select(project => project.Name)
            .Should().BeEquivalentTo("Pier 7", "Inland depot", "Bay bridge", "HQ refit");
        ProjectsOf(await tove.GraphQLDataAsync(ProjectsWithNames)).Select(project => project.Name).Should().Equal("Bay bridge");

        // One more policy, the test's own, that the application knows nothing of: a signed-in user reads a project
        // only when the token is Tove's. Whoever is withheld a row by it is withheld by the database, which can
        // only tell the two apart when a resolver's commands reach it as the caller, with the caller's claims.
        await sample.AsOwnerAsync($"""
            CREATE POLICY "Only one person reads (the test's)" ON projects."{ProjectsContext.ProjectsTable}" AS RESTRICTIVE FOR SELECT TO authenticated
                USING ((SELECT auth.uid()) = '{DemoPeople.Tove.Id}')
            """, Cancellation);

        ProjectsOf(await tove.GraphQLDataAsync(ProjectsWithNames)).Select(project => project.Name).Should().Equal("Bay bridge");
        ProjectsOf(await ada.GraphQLDataAsync(ProjectsWithNames)).Should().BeEmpty("the database withholds from her what the application would show");
        (await ada.VisibleProjectsAsync()).Should().BeEmpty("a route reads through the same contexts");

        // The one factory of a context is the host's, over its two pools: what a resolver that asked for a context
        // would rent from, as the reads behind every field do.
        sample.Host.Services.GetServices<IDbContextFactory<ProjectsContext>>().Should().ContainSingle()
            .Which.GetType().Name.Should().StartWith("ContextsByPurpose");

        // And no resolver opened connections of its own: the login role holds the two sets the host budgeted.
        await using var owner = new NpgsqlConnection(sample.Database.AsMigrationRole);
        await owner.OpenAsync(Cancellation);
        (await ListAsync(owner, $"SELECT DISTINCT application_name FROM pg_stat_activity WHERE datname = current_database() AND usename = '{SampleOnPostgres.LoginRole}' ORDER BY 1"))
            .Should().Equal("tenancy-background", "tenancy-requests");
    }

    /// <summary>
    /// A statement that runs for a signed-in user is stopped when it takes longer than the host allows a request,
    /// and the application's own work is not held to that: the host says so once, where it registers row level
    /// security, and every connection carries it with the caller.
    /// </summary>
    [Fact]
    public async Task A_users_statements_run_under_the_hosts_statement_timeout_and_the_applications_own_work_does_not()
    {
        await using var sample = await StartedAsync();
        using var ada = await sample.Host.ClientAsync("ada", Harbor.Slug);
        var allowed = $"{(int)SampleStorage.UserStatementTimeout.TotalSeconds}s";

        // One more policy, the test's own: a signed-in user reads a project only on a connection whose statement
        // timeout is the host's. The database answers what the connection carries, whatever the application meant.
        await sample.AsOwnerAsync($"""
            CREATE POLICY "Only under the timeout (the test's)" ON projects."{ProjectsContext.ProjectsTable}" AS RESTRICTIVE FOR SELECT TO authenticated
                USING (current_setting('statement_timeout') = '{allowed}')
            """, Cancellation);
        (await ada.VisibleProjectsAsync()).Should().HaveCount(Harbor.Projects.Count, "her statements run under a timeout of {0}", allowed);

        // The check checks: under any other timeout she would read none.
        await sample.AsOwnerAsync($"""
            ALTER POLICY "Only under the timeout (the test's)" ON projects."{ProjectsContext.ProjectsTable}"
                USING (current_setting('statement_timeout') <> '{allowed}')
            """, Cancellation);
        (await ada.VisibleProjectsAsync()).Should().BeEmpty();

        // The application's own work in a tenant keeps the timeout of the role the host logs in as.
        var ofSystemWork = await sample.AsSystemInAsync(Harbor, async services =>
        {
            var database = services.GetRequiredService<ProjectsContext>().Database;
            return await database.SqlQueryRaw<string>("SELECT current_setting('statement_timeout') AS \"Value\"").SingleAsync(Cancellation);
        });
        ofSystemWork.Should().NotBe(allowed);
    }

    [Fact]
    public async Task A_host_given_the_owners_connection_does_not_start()
    {
        // The same database, and the host handed the role that owns its tables: the mistake a deployment makes
        // when it reuses the migrations' connection string. It would work, and every policy would be for show.
        await using var sample = await SampleOnPostgres.CreateAsync(stack, Cancellation, connectAs: database => database.AsMigrationRole);

        var refused = sample.Host.RefusedStart();

        refused.OfType<InvalidOperationException>().Select(failure => failure.Message)
            .Should().Contain(message => message.Contains("is meant to hold nothing", StringComparison.Ordinal), "the host gave up with {0}", string.Join(" / ", refused.Select(failure => failure.Message)));
    }

    /// <summary>
    /// The other checks of the start, each proven the way a deployment would meet it: a database the exported
    /// files made, then changed by its owner in one respect, and a host that does not start on it and says why.
    /// A host that stopped calling one of them would start here.
    /// </summary>
    [Theory]
    [MemberData(nameof(DatabasesMadeWrong))]
    public async Task A_host_does_not_start_on_a_database_with(string what)
    {
        var (sql, says) = MadeWrong[what];
        await using var sample = await SampleOnPostgres.CreateAsync(stack, Cancellation);
        await sample.AsOwnerAsync(sql, Cancellation);

        var refused = sample.Host.RefusedStart();

        refused.Select(failure => failure.Message)
            .Should().Contain(message => message.Contains(says, StringComparison.Ordinal), "the host gave up with {0}", string.Join(" / ", refused.Select(failure => failure.Message)));
    }

    /// <summary>The host, started on the demonstration the run seeded once, with the database's clock past the moment it started.</summary>
    private async Task<SampleOnPostgres> StartedAsync()
    {
        var sample = await SampleOnPostgres.CreateAsync(stack, Cancellation);
        try
        {
            _ = sample.Host.Server;
            await sample.WaitUntilTheDatabaseIsPastAsync(DateTimeOffset.UtcNow, Cancellation);
            return sample;
        }
        catch
        {
            await sample.DisposeAsync();
            throw;
        }
    }

    /// <summary>What <see cref="ProjectsWithNames"/> answered: each project's name, its unit's and its owner's.</summary>
    private static List<(string? Name, string? Unit, string? Owner)> ProjectsOf(JsonElement data)
        => [.. data.GetProperty("projects").GetProperty("nodes").EnumerateArray().Select(project => (
            project.Text("name"),
            project.GetProperty("unit") is { ValueKind: JsonValueKind.Object } unit ? unit.Text("name") : null,
            project.GetProperty("owner") is { ValueKind: JsonValueKind.Object } owner ? owner.Text("displayName") : null))];

    /// <summary>
    /// A connection as the host's connections are for <paramref name="person"/>'s request in <paramref name="tenant"/>:
    /// the role of a signed-in user, the person's identity in the claims, and the tenant.
    /// </summary>
    private static async Task<NpgsqlConnection> AsSeatAsync(SampleOnPostgres sample, DemoPerson person, DemoTenant tenant)
    {
        var connection = new NpgsqlConnection(sample.AsLoginRole);
        await connection.OpenAsync(Cancellation);
        await RunAsync(connection, $$"""
            SET ROLE authenticated;
            SELECT set_config('request.jwt.claims', '{"sub":"{{person.Id}}","role":"authenticated"}', false);
            SELECT set_config('tenancy.caller_tenant', '{{tenant.Id.Value}}', false);
            """);
        return connection;
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Cancellation))!;
    }

    private static async Task<object?[]> RowAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        (await reader.ReadAsync(Cancellation)).Should().BeTrue("the statement answers a row: {0}", sql);

        var row = new object?[reader.FieldCount];
        for (var column = 0; column < row.Length; column++)
        {
            row[column] = reader.IsDBNull(column) ? null : reader.GetValue(column);
        }

        return row;
    }

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);

        var rows = new List<string>();
        while (await reader.ReadAsync(Cancellation))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    /// <summary>The SQLSTATE Postgres refuses <paramref name="sql"/> with.</summary>
    private static async Task<string> RefusedAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var run = async () => await command.ExecuteScalarAsync(Cancellation);
        return (await run.Should().ThrowAsync<PostgresException>()).Which.SqlState;
    }
}
