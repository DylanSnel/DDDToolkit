using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Exceptions;
using Examples.Tenancy.Ui.Auth;
using Examples.Tenancy.Ui.Session;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace Examples.Tenancy.Tests.Supabase;

/// <summary>
/// The sample on the stack the Supabase CLI starts, <c>supabase start</c> in <c>Examples/Tenancy</c>: the steps the
/// sample's README gives for it, taken one by one, and what a person then does with it. The CLI applied the
/// files under <c>supabase/migrations</c> itself, its gateway is in front of its Auth server, and that server
/// signs a person's token with a key it publishes; none of that is so on the images the other classes of this
/// folder start themselves.
/// </summary>
/// <remarks>
/// These tests start no container: the stack is the one a developer, or the workflow, started with the CLI.
/// They run where <see cref="SupabaseCliStack.RequiredVariable"/> is set, and fail there without the stack; where
/// it is not set they are skipped and touch nothing. They work in the stack's own database and leave it as a host
/// that ran there leaves it: seeded, the demonstration people users of Auth, and the login role with a password.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.SupabaseCli")]
public sealed class SampleOnTheCliStackTests(SupabaseCliStack stack) : IClassFixture<SupabaseCliStack>
{
    /// <summary>The tables that are not a tenant's: each module's outbox, and Entity Framework's own record of what ran.</summary>
    private static readonly string[] TablesWithoutPolicies =
    [
        "inspections.InspectionsOutboxMessages", "inspections.__EFMigrationsHistory",
        "projects.ProjectsOutboxMessages", "projects.__EFMigrationsHistory",
        "tenancy.TenancyOutboxMessages", "tenancy.__EFMigrationsHistory",
    ];

    private const string ProjectNames = "{ projects { nodes { name } } }";

    private static readonly DemoTenant Harbor = DemoData.Harbor;

    private static readonly DemoTenant Meadow = DemoData.Meadow;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// The CLI applies every file of the directory, in the order of their names, as <c>postgres</c>, which on
    /// Supabase's image is no superuser: the tables are that role's, every table of a tenant's forces its
    /// policies, and the roles the policies name are there.
    /// </summary>
    [Fact]
    public async Task The_cli_applied_every_file_in_order_as_a_role_that_is_no_superuser()
    {
        stack.EnforceOrSkip();
        await using var owner = await OpenAsync(SupabaseCliStack.AsOwner);

        // What the CLI recorded is what the directory holds: a file's version is its name up to the first underscore.
        var files = SampleOnPostgres.MigrationFiles().Select(Path.GetFileNameWithoutExtension).ToList();
        (await ListAsync(owner, "SELECT version || '_' || name FROM supabase_migrations.schema_migrations ORDER BY version"))
            .Should().Equal(files);

        (await RowAsync(owner, "SELECT current_user::text, rolsuper FROM pg_roles WHERE rolname = current_user"))
            .Should().Equal("postgres", false);

        var tables = await RowsAsync(owner, """
            SELECT n.nspname || '.' || c.relname, c.relrowsecurity AND c.relforcerowsecurity, pg_get_userbyid(c.relowner)::text
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r' AND n.nspname IN ('tenancy', 'projects', 'inspections') ORDER BY 1
            """);
        tables.Should().OnlyContain(table => (string)table[2]! == "postgres");
        tables.Where(table => !(bool)table[1]!).Select(table => (string)table[0]!)
            .Should().BeEquivalentTo(TablesWithoutPolicies, "every other table has row level security, forced on its owner too");

        // The image's own two, the three the access files make, and the one the hand-written file makes.
        (await ListAsync(owner, "SELECT rolname::text FROM pg_roles WHERE rolname IN ('anon', 'authenticated', 'ddd_system_in', 'ddd_system', 'tenancy_operator', 'tenancy_api') ORDER BY 1"))
            .Should().Equal("anon", "authenticated", "ddd_system", "ddd_system_in", "tenancy_api", "tenancy_operator");

        // Supabase's own roles hold nothing in the modules' schemas: the Data API beside the host reaches none of them.
        (await ListAsync(owner, """
            SELECT r || ' in ' || s FROM unnest(ARRAY['anon', 'service_role', 'authenticator']) r, unnest(ARRAY['tenancy', 'projects', 'inspections']) s
            WHERE has_schema_privilege(r, s, 'USAGE')
            """)).Should().BeEmpty();
    }

    /// <summary>
    /// The README's steps lead to a running host: the login role is given a password, the host is given that
    /// role's connection string and nothing else of the database, and every start-up check passes.
    /// </summary>
    [Fact]
    public async Task The_host_starts_as_the_login_role_and_passes_every_startup_check()
    {
        var host = await stack.StartedAsync();

        // Started, so: every migration was found applied, the login role owns and holds nothing, the functions
        // that run as their owner are let through the forced policies, and Tenancy's second lock is in place.
        using var anyone = host.CreateClient();
        using var alive = await anyone.GetAsync("/health", Cancellation);
        alive.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var owner = await OpenAsync(SupabaseCliStack.AsOwner);
        (await RowAsync(owner, $"SELECT rolcanlogin, rolsuper, rolbypassrls, rolinherit, rolcreaterole FROM pg_roles WHERE rolname = '{SampleOnPostgres.LoginRole}'"))
            .Should().Equal(true, false, false, false, false);

        // What the host is logged in as: a role that owns nothing and reads no table by itself.
        await using var login = await OpenAsync(stack.AsLoginRole);
        (await ListAsync(login, "SELECT relname::text FROM pg_class WHERE relowner = (SELECT oid FROM pg_roles WHERE rolname = current_user)")).Should().BeEmpty();
        (await RefusedAsync(login, "SELECT count(*) FROM projects.\"Projects\"")).Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await RefusedAsync(login, "SELECT count(*) FROM tenancy.\"Seats\"")).Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    /// <summary>
    /// The real login: the host made the demonstration people users of the CLI's Auth, a person signs in there
    /// with her address and the password, and the host takes the token, which Auth signed with a key it
    /// publishes, as it takes the dev login's. She reads her projects through the routes and through GraphQL,
    /// and a person of the other tenant reads none of them.
    /// </summary>
    [Fact]
    public async Task A_person_signs_in_at_the_clis_auth_and_reads_her_projects_and_nothing_of_another_tenant()
    {
        var host = await stack.StartedAsync();
        using var http = new HttpClient { BaseAddress = SupabaseCliStack.Auth };
        var login = new SupabaseLoginClient(http, new UiSession());

        (await login.SignInAsync(DemoPeople.Rhea.Email, "not-the-password-of-this-run", Cancellation)).Answer.Should().BeNull();

        var signedIn = await login.SignInAsync(DemoPeople.Rhea.Email, stack.DemoPassword, Cancellation);
        signedIn.Refusal.Should().BeNull();
        var token = signedIn.Answer!.AccessToken;

        // The stack the CLI starts signs with a published key, as the images the other tests start are set up
        // to: the host checks this one against Auth's keys, and the dev login's against the secret.
        var jwt = new JsonWebToken(token);
        jwt.Alg.Should().Be("ES256");
        jwt.Subject.Should().Be(DemoPeople.Rhea.Id.ToString(), "Auth made her under the id her seat is found by");

        using var rhea = host.Client(token, Harbor.Slug);
        var me = await rhea.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("seat").GetProperty("id").GetGuid().Should().Be(Harbor.SeatOf(DemoPeople.Rhea).Value);
        (await rhea.VisibleProjectsAsync()).Names().Should().BeEquivalentTo("Pier 7", "Inland depot");
        NamesOf(await rhea.GraphQLDataAsync(ProjectNames)).Should().BeEquivalentTo("Pier 7", "Inland depot");

        // She has no seat in meadow, so the same token reads nothing there, by either way in.
        using var rheaInMeadow = host.Client(token, Meadow.Slug);
        using (var noSeat = await rheaInMeadow.GetAsync("/projects", Cancellation))
        {
            await noSeat.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, "tenancy.not-seated");
        }

        (await rheaInMeadow.GraphQLAsync(ProjectNames)).SingleError().Code().Should().Be("tenancy.not-seated");

        // Tove signs in the same way, and works in meadow: its one project, and none of harbor's, by its id either.
        var tove = await login.SignInAsync(DemoPeople.Tove.Email, stack.DemoPassword, Cancellation);
        using var toveInMeadow = host.Client(tove.Answer!.AccessToken, Meadow.Slug);
        (await toveInMeadow.VisibleProjectsAsync()).Names().Should().Equal("Garden shed");
        NamesOf(await toveInMeadow.GraphQLDataAsync(ProjectNames)).Should().Equal("Garden shed");
        using var acrossTenants = await toveInMeadow.GetAsync($"/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}", Cancellation);
        await acrossTenants.ShouldBeRefusedAsync(HttpStatusCode.NotFound, ProjectRefusals.NotFound);

        // And the dev login still works beside it, on the same host, for the same person.
        using var throughDevLogin = await host.ClientAsync(DemoPeople.Rhea.Key, Harbor.Slug);
        (await throughDevLogin.VisibleProjectsAsync()).Names().Should().BeEquivalentTo("Pier 7", "Inland depot");
    }

    /// <summary>
    /// Nobody signs up: <c>config.toml</c> turns the e-mail provider on, which signing in with a password needs
    /// on this stack, and keeps signing up closed, so Auth makes no account for an address the application made
    /// none for.
    /// </summary>
    [Fact]
    public async Task A_stranger_who_signs_up_at_the_clis_auth_is_refused_and_gets_no_account()
    {
        stack.EnforceOrSkip();
        const string Stranger = "stranger@example.test";
        using var http = new HttpClient { BaseAddress = SupabaseCliStack.Auth };

        using var answer = await http.PostAsJsonAsync("signup", new { email = Stranger, password = "A-password-nobody-will-use-1" }, Cancellation);
        answer.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await answer.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).Text("error_code").Should().Be("signup_disabled");

        await using var owner = await OpenAsync(SupabaseCliStack.AsOwner);
        (await RowAsync(owner, $"SELECT count(*) FROM auth.users WHERE email = '{Stranger}'")).Should().Equal(0L);
    }

    /// <summary>
    /// The database refuses what its row rules do not allow, with its own code, whatever the application thinks:
    /// a row a seat may not write is refused where the exported policy checks it, and a change the application
    /// allows and a policy denies is answered as a refusal with a code, not as a failure.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_a_change_its_row_rules_do_not_allow()
    {
        var host = await stack.StartedAsync();
        var rheasSeat = Harbor.SeatOf(DemoPeople.Rhea).Value;

        // As Rhea's seat in harbor, the way the host's connections say it. She runs the north: a project there is
        // hers to open, and one in the south is not.
        await using var asRhea = await OpenAsync(stack.AsLoginRole);
        await RunAsync(asRhea, $$"""
            SET ROLE authenticated;
            SELECT set_config('request.jwt.claims', '{"sub":"{{DemoPeople.Rhea.Id}}","role":"authenticated"}', false);
            SELECT set_config('tenancy.caller_tenant', '{{Harbor.Id.Value}}', false);
            """);

        string Opening(string number, string unit) => $"""
            INSERT INTO projects."Projects" ("Id", "TenantId", "Number", "Name", "UnitId", "State", "OwnerSeatId", "CreatedByKind", "CreatedBySeat", "ChangedByKind", "ChangedBySeat", "Version")
            VALUES (gen_random_uuid(), '{Harbor.Id.Value}', '{number}', 'Written past the application', '{Harbor.UnitNamed(unit).Value}', 'Open', '{rheasSeat}', 'seat', '{rheasSeat}', 'seat', '{rheasSeat}', 1)
            """;

        await RunAsync(asRhea, "BEGIN");
        await RunAsync(asRhea, Opening("P-901", "North Coast"));
        await RunAsync(asRhea, "ROLLBACK");

        var refused = await FluentActions.Awaiting(() => RunAsync(asRhea, Opening("P-902", "South Bay"))).Should().ThrowAsync<PostgresException>();
        refused.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        refused.Which.MessageText.Should().Contain("row-level security policy");

        // Through the host: a policy of the test's own that the application knows nothing of. The application lets
        // the change through, the database denies the row, and the caller is told so with the toolkit's code.
        const string Policy = "\"Nobody renames (the test's)\"";
        await using var owner = await OpenAsync(SupabaseCliStack.AsOwner);
        await RunAsync(owner, $"DROP POLICY IF EXISTS {Policy} ON projects.\"{ProjectsContext.ProjectsTable}\"");
        await RunAsync(owner, $"CREATE POLICY {Policy} ON projects.\"{ProjectsContext.ProjectsTable}\" AS RESTRICTIVE FOR UPDATE TO authenticated USING (true) WITH CHECK (false)");
        try
        {
            using var rhea = await host.ClientAsync(DemoPeople.Rhea.Key, Harbor.Slug);
            using var renamed = await rhea.PutAsJsonAsync($"/projects/{Harbor.ProjectNamed("Pier 7").Id.Value}/name", new { name = "Pier 7 east" }, Cancellation);
            await renamed.ShouldBeRefusedAsync(HttpStatusCode.Forbidden, ToolkitRefusals.Refused);
        }
        finally
        {
            await RunAsync(owner, $"DROP POLICY IF EXISTS {Policy} ON projects.\"{ProjectsContext.ProjectsTable}\"");
        }

        (await ListAsync(owner, $"SELECT \"Name\" FROM projects.\"{ProjectsContext.ProjectsTable}\" WHERE \"Id\" = '{Harbor.ProjectNamed("Pier 7").Id.Value}'"))
            .Should().Equal("Pier 7");
    }

    private static IEnumerable<string?> NamesOf(JsonElement data)
        => data.GetProperty("projects").GetProperty("nodes").EnumerateArray().Select(project => project.Text("name"));

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

    private static async Task<object?[]> RowAsync(NpgsqlConnection connection, string sql)
        => (await RowsAsync(connection, sql)).Should().ContainSingle("the statement answers one row: {0}", sql).Subject;

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
        => [.. (await RowsAsync(connection, sql)).Select(row => (string)row[0]!)];

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
