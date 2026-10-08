using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Examples.Tenancy.Ui.Auth;
using Examples.Tenancy.Ui.Languages;
using Examples.Tenancy.Ui.Session;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace Examples.Tenancy.Tests.Supabase;

/// <summary>
/// The sample on Supabase's own Postgres and Auth images together: the database as the role that owns it makes
/// it from the exported files, a demonstration person signing in at Auth with a password, and the policies
/// holding everyone who is not that role: the login the host uses, a caller without a seat, and the
/// application's own work in one tenant.
/// </summary>
/// <remarks>
/// They need Docker and Supabase's images, so they carry the samples' traits and stay out of the runs that have
/// neither. One of them makes the demonstration people users of the stack's Auth server, so the class takes
/// turns with the others that do.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
[Collection(SampleSupabaseStack.AuthUsers)]
public sealed class SampleOnSupabaseTests(SampleSupabaseStack stack)
{
    private const string ModuleTables = """
        SELECT n.nspname || '.' || c.relname AS name, c.relrowsecurity AS enabled, c.relforcerowsecurity AS forced, pg_get_userbyid(c.relowner)::text AS owner
        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'r' AND n.nspname IN ('tenancy', 'projects', 'inspections')
        """;

    private static readonly DemoTenant Harbor = DemoData.Harbor;

    private static readonly DemoTenant Meadow = DemoData.Meadow;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// A project's migrations run as a role that is no superuser. Every file the sample keeps for Supabase, the
    /// tables, the policies with their functions and privileges, and the login role, is applied by that role
    /// alone, and what it made is its own.
    /// </summary>
    [Fact]
    public async Task The_supabase_image_starts_and_postgres_applies_the_migrations_without_superuser()
    {
        await using var sample = await SampleOnPostgres.CreateAsync(stack, Cancellation);
        await using var owner = await OpenAsync(sample.Database.AsMigrationRole);

        (await RowAsync(owner, "SELECT current_user::text, rolsuper FROM pg_roles WHERE rolname = current_user"))
            .Should().Equal(SampleSupabaseStack.MigrationRole, false);

        // Every module's tables are there, and theirs.
        var tables = await RowsAsync(owner, ModuleTables + " ORDER BY 1");
        tables.Select(table => (string)table[0]!).Should().Contain(["tenancy.Seats", "projects.Projects", "inspections.Inspections"]);
        tables.Should().OnlyContain(table => (string)table[3]! == SampleSupabaseStack.MigrationRole);

        // The login role as the hand-written file leaves it and the deployment turned it on: it logs in, and is
        // nothing more than that.
        (await RowAsync(owner, $"SELECT rolcanlogin, rolsuper, rolbypassrls, rolinherit, rolcreaterole FROM pg_roles WHERE rolname = '{SampleOnPostgres.LoginRole}'"))
            .Should().Equal(true, false, false, false, false);
    }

    /// <summary>
    /// The real sign-in, end to end: the host made the demonstration people users of Auth under their fixed ids,
    /// the UI's own client signs one in with her address and the password, and the host takes Auth's token,
    /// checked with the key Auth publishes, beside the dev login's, checked with the secret, finding her seat
    /// by the token's subject either way.
    /// </summary>
    [Fact]
    public async Task A_demo_person_signs_in_with_a_password_and_reads_her_projects_and_nothing_of_another_tenant()
    {
        await using var sample = await StartedAsync(withAuth: true);
        using var http = new HttpClient { BaseAddress = sample.Auth!.Url };
        var login = new SupabaseLoginClient(http, new UiSession());
        var refused = PasswordSignIn.RefusedBecause(UiTexts.For("login.password.refused", UiSession.Languages[0]));
        var rhea = DemoPeople.Rhea;

        // A wrong password and an address nobody has are told the same, and neither gets a token.
        (await login.SignInAsync(rhea.Email, "not-the-password-of-this-run", Cancellation))
            .Should().Be(refused);
        (await login.SignInAsync("nobody@example.test", SampleOnPostgres.DemoPassword, Cancellation))
            .Should().Be(refused);

        var signedIn = await login.SignInAsync(rhea.Email, SampleOnPostgres.DemoPassword, Cancellation);
        signedIn.Refusal.Should().BeNull();
        var token = signedIn.Answer!.AccessToken;
        signedIn.Answer.User!.Id.Should().Be(rhea.Id, "Auth made her under the id her seat is found by");
        new JsonWebToken(token).Subject.Should().Be(rhea.Id.ToString());
        new JsonWebToken(token).Alg.Should().Be("ES256", "this Auth server signs with a key it publishes, so a host that takes the token fetched that key from it");

        // With Auth's token: her seat in harbor, and the projects of the north she manages.
        using var inHarbor = sample.Host.Client(token, Harbor.Slug);
        var me = await inHarbor.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("seat").GetProperty("id").GetGuid().Should().Be(Harbor.SeatOf(rhea).Value);
        (await inHarbor.VisibleProjectsAsync()).Names().Should().BeEquivalentTo("Pier 7", "Inland depot");

        // She has no seat in meadow, so the same token reads nothing there.
        using var inMeadow = sample.Host.Client(token, Meadow.Slug);
        using (var noSeat = await inMeadow.GetAsync("/projects", Cancellation))
        {
            noSeat.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // And the dev login still works beside it, on the same host, for the same person.
        using var throughDevLogin = await sample.Host.ClientAsync(rhea.Key, Harbor.Slug);
        (await throughDevLogin.VisibleProjectsAsync()).Names().Should().BeEquivalentTo("Pier 7", "Inland depot");
    }

    /// <summary>
    /// The policies are forced on every table of a tenant's, so nothing but the role that owns the database is
    /// let past them. The role the host logs in as is not: by itself it may not touch a table, and as a
    /// signed-in user it reads the rows of the seat its claims and its tenant name, and no other. Asked of every
    /// table the modules' models map, so a table added to a module is held to it the day it is added: every
    /// route and field is answered by the application's own check first, and only a statement past the
    /// application shows what the database would let through.
    /// </summary>
    [Fact]
    public async Task Every_table_forces_its_policies_and_the_login_role_reads_no_row_without_a_seat()
    {
        await using var sample = await StartedAsync();

        // An inspection in each tenant: the demonstration seeds none, and a table without rows proves nothing.
        using (var ada = await sample.Host.ClientAsync("ada", Harbor.Slug))
        using (var recorded = await ada.PostAsJsonAsync($"/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}/inspections", new { title = "Loose railing" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Cancellation));
        }

        using (var tove = await sample.Host.ClientAsync("tove", Meadow.Slug))
        using (var recorded = await tove.PostAsJsonAsync($"/projects/{Meadow.ProjectNamed("Garden shed").Id.Value}/inspections", new { title = "Door sticks" }, Cancellation))
        {
            recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync(Cancellation));
        }

        // The tables, from the modules' models. The database holds those and Entity Framework's record of its
        // migrations, and every one that holds a tenant's rows has row level security, forced on its owner too:
        // all but each module's outbox and that record.
        var model = await SampleTables.OfTheModelsAsync(sample.Host.Services, Cancellation);
        var ofATenant = model.Where(table => table.OfATenant).Select(table => table.Name).ToList();
        ofATenant.Should().HaveCountGreaterThan(10);

        await using var owner = await OpenAsync(sample.Database.AsMigrationRole);
        var tables = await RowsAsync(owner, ModuleTables + " ORDER BY 1");
        tables.Select(table => (string)table[0]!)
            .Should().BeEquivalentTo([.. model.Select(table => table.Name), .. SampleTables.MigrationHistories], "the exported files make the tables of the models, and nothing the models do not know");
        tables.Where(table => (bool)table[1]! && (bool)table[2]!).Select(table => (string)table[0]!)
            .Should().BeEquivalentTo(ofATenant, "a table that holds a tenant's rows has row level security, forced on its owner too, and no other table has");

        // The owner is let through, on this image: it sees both tenants' projects, and rows in the tables the two
        // tenants are known to fill, so that nobody else reads one of them below is the policies' doing.
        var everyProject = Harbor.Projects.Concat(Meadow.Projects).Select(project => project.Name).ToList();
        (await NamesAsync(owner)).Should().BeEquivalentTo(everyProject);

        var filled = new List<string>();
        foreach (var table in ofATenant)
        {
            if (await ScalarAsync<long>(owner, $"SELECT count(*) FROM {Quoted(table)}") > 0)
            {
                filled.Add(table);
            }
        }

        filled.Should().Contain(
        [
            $"{TenantsContext.Schema}.Seats",
            $"{TenantsContext.Schema}.{TenantsContext.HistoryTable}",
            $"{ProjectsContext.Schema}.{ProjectsContext.ProjectsTable}",
            $"{ProjectsContext.Schema}.{ProjectsContext.CrewTable}",
            $"{ProjectsContext.Schema}.{ProjectsContext.CrewRolesTable}",
            $"{InspectionsContext.Schema}.{InspectionsContext.InspectionsTable}",
        ]);

        // The login role, as itself: it owns nothing, and no table of a tenant's is open to it.
        await using var login = await OpenAsync(sample.AsLoginRole);
        (await ScalarAsync<long>(login, "SELECT count(*) FROM pg_class WHERE relowner = (SELECT oid FROM pg_roles WHERE rolname = current_user)")).Should().Be(0);
        foreach (var table in ofATenant)
        {
            (await RefusedAsync(login, $"SELECT count(*) FROM {Quoted(table)}")).Should().Be(PostgresErrorCodes.InsufficientPrivilege, "{0} is closed to the login role", table);
        }

        // As a signed-in user who says nothing about who they are: the tables are open, and no row of any is theirs.
        await RunAsync(login, "SET ROLE authenticated");
        await NoRowIsReadAsync("a signed-in user who names nobody", own: []);

        // As Rhea in harbor, the way the host's connections say it: the north's projects, which her seat reaches.
        await RunAsync(login, $$"""
            SELECT set_config('request.jwt.claims', '{"sub":"{{DemoPeople.Rhea.Id}}","role":"authenticated"}', false);
            SELECT set_config('tenancy.caller_tenant', '{{Harbor.Id.Value}}', false);
            """);
        (await NamesAsync(login)).Should().BeEquivalentTo("Pier 7", "Inland depot");

        // The same person naming the other tenant has no seat there, so nothing of meadow, of any table: the
        // database finds the seat itself, from the identity and the tenant, and takes nobody's word for it. What
        // she still reads is her own, whatever tenant she names, which is how a person picks a tenant before
        // being in one: her seat, and the tenant and the organization it is in. All three are harbor's.
        await RunAsync(login, $"SELECT set_config('tenancy.caller_tenant', '{Meadow.Id.Value}', false)");
        await NoRowIsReadAsync("Rhea, who has no seat in meadow", own: new()
        {
            [$"{TenantsContext.Schema}.Seats"] = Harbor.SeatOf(DemoPeople.Rhea).Value,
            [$"{TenantsContext.Schema}.Tenants"] = Harbor.Id.Value,
            [$"{TenantsContext.Schema}.Organizations"] = Harbor.Id.Value,
        });

        // No row of any table, but the one row of each table in own, named by its id.
        async Task NoRowIsReadAsync(string who, Dictionary<string, Guid> own)
        {
            foreach (var table in ofATenant)
            {
                if (own.TryGetValue(table, out var id))
                {
                    (await RowsAsync(login, $"SELECT \"Id\" FROM {Quoted(table)}")).Select(row => (Guid)row[0]!)
                        .Should().Equal([id], "{0} reads her own row of {1} and no other", who, table);
                }
                else
                {
                    (await RowsReadAsync(login, table)).Should().Be(0, "{0} reads no row of {1}", who, table);
                }
            }
        }
    }

    /// <summary>
    /// The application's own work in a tenant is a caller like any other to the database: it runs as a role of its
    /// own, and the policies hold it to the tenant it was begun in, whatever the statement asks for.
    /// </summary>
    [Fact]
    public async Task System_work_in_a_tenant_reads_that_tenant_and_no_other()
    {
        await using var sample = await StartedAsync();

        // Raw SQL through the module's own context, so nothing but the database decides what comes back.
        Task<List<string>> ProjectsSeenInAsync(DemoTenant tenant) => sample.AsSystemInAsync(tenant, async services =>
        {
            await using var context = await services.GetRequiredService<IDbContextFactory<ProjectsContext>>().CreateDbContextAsync(Cancellation);
            return await context.Database
                .SqlQueryRaw<string>($"SELECT \"Name\" AS \"Value\" FROM {ProjectsContext.Schema}.\"{ProjectsContext.ProjectsTable}\"")
                .ToListAsync(Cancellation);
        });

        (await ProjectsSeenInAsync(Harbor)).Should().BeEquivalentTo(Harbor.Projects.Select(project => project.Name));
        (await ProjectsSeenInAsync(Meadow)).Should().BeEquivalentTo(Meadow.Projects.Select(project => project.Name));

        // The owner sees them all, which is what the two answers above were cut from.
        await using var owner = await OpenAsync(sample.Database.AsMigrationRole);
        (await NamesAsync(owner)).Should().HaveCount(Harbor.Projects.Count + Meadow.Projects.Count);
    }

    /// <summary>The host, started on the demonstration the run seeded once, with the database's clock past the moment it started.</summary>
    private async Task<SampleOnPostgres> StartedAsync(bool withAuth = false)
    {
        var sample = await SampleOnPostgres.CreateAsync(stack, Cancellation, withAuth: withAuth);
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

    /// <summary>The names of the projects the connection's caller reads, straight from the table.</summary>
    private static async Task<List<string>> NamesAsync(NpgsqlConnection connection)
        => [.. (await RowsAsync(connection, $"SELECT \"Name\" FROM {ProjectsContext.Schema}.\"{ProjectsContext.ProjectsTable}\"")).Select(row => (string)row[0]!)];

    /// <summary>A table's name as a statement writes it: the schema, and the name as Entity Framework spelled it.</summary>
    private static string Quoted(string table) => $"{table[..table.IndexOf('.')]}.\"{table[(table.IndexOf('.') + 1)..]}\"";

    /// <summary>
    /// How many rows of <paramref name="table"/> the connection's caller reads: none, too, of a table its role may
    /// not read at all, which is how a table only a function answers from is kept.
    /// </summary>
    private static async Task<long> RowsReadAsync(NpgsqlConnection connection, string table)
    {
        try
        {
            return await ScalarAsync<long>(connection, $"SELECT count(*) FROM {Quoted(table)}");
        }
        catch (PostgresException closed) when (closed.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return 0;
        }
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Cancellation);
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
        => (await RowsAsync(connection, sql)).Should().ContainSingle("the statement answers one row: {0}", sql).Subject;

    private static async Task<List<object?[]>> RowsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(Cancellation))
        {
            var row = new object?[reader.FieldCount];
            for (var column = 0; column < row.Length; column++)
            {
                row[column] = reader.IsDBNull(column) ? null : reader.GetValue(column);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The SQLSTATE Postgres refuses <paramref name="sql"/> with.</summary>
    private static async Task<string> RefusedAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var run = async () => await command.ExecuteScalarAsync(Cancellation);
        return (await run.Should().ThrowAsync<PostgresException>("'{0}' must be refused", sql)).Which.SqlState;
    }
}
