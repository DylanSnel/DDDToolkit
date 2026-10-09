using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Auth.Supabase;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace Examples.Tenancy.Tests.Supabase;

/// <summary>
/// What the sample relies on from Supabase's own images, asked of the images themselves. Each test here is
/// something the sample is built on: if an image is moved to another version and answers differently, the test
/// that fails names what has to be looked at again.
/// </summary>
/// <remarks>
/// They need Docker, so they carry the samples' traits and stay out of the runs that have none. These are the
/// images as they come: nothing here uses the sample's own migrations but the last test, which hands a token
/// of the image's Auth server to the sample's host. That test gives a demonstration person a password at the
/// stack's Auth server, so the class takes turns with the others that do.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
[Collection(SampleSupabaseStack.AuthUsers)]
public sealed class SupabaseStackTests(SampleSupabaseStack stack)
{
    /// <summary>What Postgres says when a role may not do something.</summary>
    private const string InsufficientPrivilege = PostgresErrorCodes.InsufficientPrivilege;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ---- The database -------------------------------------------------------------------------------------

    /// <summary>
    /// Forcing row level security takes the owner's free pass away, so the sample's migrations and the functions
    /// that run as their owner keep working only if the role that owns them is let through for another reason.
    /// On this image it is: the migration role is no superuser, but it may bypass row level security.
    /// </summary>
    [Fact]
    public async Task The_migration_role_on_the_supabase_image_bypasses_row_level_security()
    {
        const string Owner = "stack_probe_owner";

        var database = await stack.CreateDatabaseAsync(Cancellation);
        await using var migration = await OpenAsync(database.AsMigrationRole);

        (await RowAsync(migration, "SELECT current_user::text, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user"))
            .Should().Equal([SampleSupabaseStack.MigrationRole, false, true], "migrations run as a role that is no superuser and that row level security lets through");

        // A table nobody's policy admits, forced, and a function that reads it as its owner.
        await RunAsync(migration, """
            CREATE SCHEMA probe;
            CREATE TABLE probe.notes (id int PRIMARY KEY, body text NOT NULL);
            ALTER TABLE probe.notes ENABLE ROW LEVEL SECURITY;
            ALTER TABLE probe.notes FORCE ROW LEVEL SECURITY;
            CREATE POLICY nobody ON probe.notes FOR ALL USING (false) WITH CHECK (false);
            CREATE FUNCTION probe.count_notes() RETURNS bigint LANGUAGE sql SECURITY DEFINER SET search_path = ''
                AS $$ SELECT count(*) FROM probe.notes $$;
            REVOKE ALL ON FUNCTION probe.count_notes() FROM PUBLIC;
            GRANT USAGE ON SCHEMA probe TO authenticated;
            GRANT SELECT, INSERT ON probe.notes TO authenticated;
            GRANT EXECUTE ON FUNCTION probe.count_notes() TO authenticated;
            """);

        (await RowAsync(migration, "SELECT pg_get_userbyid(relowner)::text, relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = 'probe.notes'::regclass"))
            .Should().Equal([SampleSupabaseStack.MigrationRole, true, true], "the migration role owns what it makes, and the table is forced");

        // The owner writes and reads through the forced policy.
        await RunAsync(migration, "INSERT INTO probe.notes VALUES (1, 'written by the migration role')");
        (await ScalarAsync<long>(migration, "SELECT count(*) FROM probe.notes")).Should().Be(1);

        // A signed-in user is held by it, and still gets an answer from the function that runs as the owner.
        await RunAsync(migration, "SET ROLE authenticated");
        (await ScalarAsync<long>(migration, "SELECT count(*) FROM probe.notes")).Should().Be(0);
        (await RefusedAsync(migration, "INSERT INTO probe.notes VALUES (2, 'written by a user')")).Should().Be(InsufficientPrivilege);
        (await ScalarAsync<long>(migration, "SELECT probe.count_notes()")).Should().Be(1, "a function that runs as the migration role reads through the forced policy");
        await RunAsync(migration, "RESET ROLE");

        // It is the role's attribute that lets it through, not owning the table: an owner without it is held by
        // its own forced policy, which is the difference forcing makes.
        try
        {
            await RunAsync(migration, $"""
                CREATE ROLE {Owner} NOLOGIN;
                GRANT {Owner} TO CURRENT_USER WITH SET TRUE, INHERIT TRUE;
                CREATE SCHEMA probe_owned AUTHORIZATION {Owner};
                SET ROLE {Owner};
                CREATE TABLE probe_owned.notes (id int PRIMARY KEY, body text NOT NULL);
                ALTER TABLE probe_owned.notes ENABLE ROW LEVEL SECURITY;
                CREATE POLICY nobody ON probe_owned.notes FOR ALL USING (false) WITH CHECK (false);
                INSERT INTO probe_owned.notes VALUES (1, 'written by an owner, before the policies were forced');
                ALTER TABLE probe_owned.notes FORCE ROW LEVEL SECURITY;
                """);

            (await ScalarAsync<bool>(migration, "SELECT rolbypassrls FROM pg_roles WHERE rolname = current_user")).Should().BeFalse();
            (await ScalarAsync<long>(migration, "SELECT count(*) FROM probe_owned.notes")).Should().Be(0, "an owner that may not bypass no longer sees its own row");
            (await RefusedAsync(migration, "INSERT INTO probe_owned.notes VALUES (2, 'written by an owner, after')")).Should().Be(InsufficientPrivilege);
        }
        finally
        {
            // Roles are the server's, not this database's: the next test finds the server as this one did. The
            // migration role may drop the schema because it was given the owner's role with what that role may do.
            await RunAsync(migration, $"RESET ROLE; DROP SCHEMA IF EXISTS probe_owned CASCADE; DROP ROLE IF EXISTS {Owner}");
        }
    }

    /// <summary>
    /// The sample's request login is made by a migration: a role that owns nothing and inherits nothing, and may
    /// switch to the callers' roles. Deployment, not a migration, lets it log in. The migration role can do all
    /// of that here without being a superuser.
    /// </summary>
    [Fact]
    public async Task The_migration_role_can_make_a_login_role_a_member_of_the_callers_roles()
    {
        const string Login = "stack_probe_login";
        const string OwnRole = "stack_probe_system";
        var password = Guid.NewGuid().ToString("N");

        var database = await stack.CreateDatabaseAsync(Cancellation);
        await using var migration = await OpenAsync(database.AsMigrationRole);
        try
        {
            // What a migration does: the image's two caller roles and a role the migrations made themselves,
            // given to a role that cannot log in yet. And a table a signed-in user may read.
            await RunAsync(migration, $"""
                CREATE ROLE {OwnRole} NOLOGIN NOINHERIT;
                CREATE ROLE {Login} NOLOGIN NOINHERIT;
                GRANT anon, authenticated, {OwnRole} TO {Login};
                CREATE SCHEMA probe;
                CREATE TABLE probe.notes (id int PRIMARY KEY);
                INSERT INTO probe.notes VALUES (1);
                GRANT USAGE ON SCHEMA probe TO authenticated;
                GRANT SELECT ON probe.notes TO authenticated;
                """);

            // What deployment does, once.
            await RunAsync(migration, $"ALTER ROLE {Login} WITH LOGIN PASSWORD '{password}'");

            (await RowAsync(migration, $"SELECT rolcanlogin, rolsuper, rolbypassrls, rolinherit, rolcreaterole, rolcreatedb FROM pg_roles WHERE rolname = '{Login}'"))
                .Should().Equal([true, false, false, false, false, false], "the login role is nothing but a way in");
            (await ScalarAsync<string[]>(migration, $"SELECT array_agg(roleid::regrole::text ORDER BY roleid::regrole::text) FROM pg_auth_members WHERE member = '{Login}'::regrole"))
                .Should().Equal("anon", "authenticated", OwnRole);

            await using (var login = await OpenAsync(database.As(Login, password)))
            {
                (await RowAsync(login, "SELECT session_user::text, current_user::text")).Should().Equal(Login, Login);

                // As itself it holds nothing: what a member role may do is not inherited.
                (await RefusedAsync(login, "SELECT count(*) FROM probe.notes")).Should().Be(InsufficientPrivilege);

                // It becomes each role it was given, and with it gets what that role may do.
                foreach (var role in new[] { "anon", "authenticated", OwnRole })
                {
                    await RunAsync(login, $"SET ROLE {role}");
                    (await ScalarAsync<string>(login, "SELECT current_user::text")).Should().Be(role);
                }

                await RunAsync(login, "SET ROLE authenticated");
                (await ScalarAsync<long>(login, "SELECT count(*) FROM probe.notes")).Should().Be(1);
                await RunAsync(login, "RESET ROLE");

                // And no role it was not given: not the migration role, the service role or the superuser.
                foreach (var role in new[] { SampleSupabaseStack.MigrationRole, "service_role", "supabase_admin" })
                {
                    (await RefusedAsync(login, $"SET ROLE {role}")).Should().Be(InsufficientPrivilege, "the login role was not given {0}", role);
                }
            }
        }
        finally
        {
            await RunAsync(migration, $"DROP ROLE IF EXISTS {Login}; DROP ROLE IF EXISTS {OwnRole}");
        }
    }

    /// <summary>
    /// A database of the stack is a project's: the callers' roles are there, and Auth's functions read the caller
    /// from <c>request.jwt.claims</c>, which is the one setting the toolkit puts on a connection. The image alone
    /// reads only the older settings, one per claim; it is Auth's own migrations that make it so.
    /// </summary>
    [Fact]
    public async Task A_database_of_the_stack_reads_the_caller_from_the_claims_as_a_project_does()
    {
        var caller = Guid.NewGuid();

        var database = await stack.CreateDatabaseAsync(Cancellation);
        await using var connection = await OpenAsync(database.AsMigrationRole);

        (await ScalarAsync<string[]>(connection, "SELECT array_agg(rolname::text ORDER BY rolname) FROM pg_roles WHERE rolname IN ('anon', 'authenticated', 'service_role')"))
            .Should().Equal("anon", "authenticated", "service_role");
        (await ScalarAsync<string>(connection, "SELECT pg_get_userbyid(datdba)::text FROM pg_database WHERE datname = current_database()"))
            .Should().Be(SampleSupabaseStack.MigrationRole, "the migration role owns a project's database, so it may make schemas there");

        await using var claims = new NpgsqlCommand("SELECT set_config('request.jwt.claims', $1, false)", connection);
        claims.Parameters.AddWithValue(JsonSerializer.Serialize(new { sub = caller, role = "authenticated", email = "caller@example.test" }));
        await claims.ExecuteNonQueryAsync(Cancellation);

        // Asked as the role a signed-in user's statements run as.
        await RunAsync(connection, "SET ROLE authenticated");
        (await RowAsync(connection, "SELECT auth.uid(), auth.role(), (auth.jwt() ->> 'sub')::uuid, auth.jwt() ->> 'email'"))
            .Should().Equal(caller, "authenticated", caller, "caller@example.test");
    }

    /// <summary>
    /// The server is Supabase's, and not a plain Postgres started from Supabase's image: it runs on the image's
    /// own settings, so the extension that guards Supabase's roles is loaded into every session, and the
    /// migration role may not change a role Supabase reserves. That is why the stack, standing in for the
    /// platform, gives the Auth server's role its password. The stack adds one setting of its own: functions
    /// are tracked, for tests that count how often one was called.
    /// </summary>
    [Fact]
    public async Task The_stack_runs_the_image_on_its_own_settings_and_tracks_functions()
    {
        var database = await stack.CreateDatabaseAsync(Cancellation);
        await using var connection = await OpenAsync(database.AsMigrationRole);

        (await RowAsync(connection, "SELECT current_setting('session_preload_libraries') LIKE '%supautils%', current_setting('track_functions')"))
            .Should().Equal([true, "all"], "the image's settings file loads Supabase's guard, and the stack's command asks for the tracking");

        // The guard at work, on a change that would change nothing: no limit is what the role has.
        (await RefusedAsync(connection, "ALTER ROLE supabase_auth_admin CONNECTION LIMIT -1"))
            .Should().Be(InsufficientPrivilege, "the Auth server's role is one Supabase reserves");
    }

    /// <summary>
    /// Supabase Queues is pgmq, which the image offers at the version the toolkit's own queue tests run against
    /// as Supabase's, and which the migration role can turn on. The sample uses no queue; this is here so that
    /// the version is known where it is decided.
    /// </summary>
    [Fact]
    public async Task The_supabase_image_offers_pgmq()
    {
        const string Version = "1.5.1";

        var database = await stack.CreateDatabaseAsync(Cancellation);
        await using var connection = await OpenAsync(database.AsMigrationRole);

        (await RowAsync(connection, "SELECT default_version, installed_version FROM pg_available_extensions WHERE name = 'pgmq'"))
            .Should().Equal([Version, null], "the image offers pgmq {0}, and a project turns it on itself", Version);

        await RunAsync(connection, "CREATE EXTENSION pgmq");
        (await ScalarAsync<string>(connection, "SELECT extversion FROM pg_extension WHERE extname = 'pgmq'")).Should().Be(Version);

        // And it works for the role that turned it on.
        await RunAsync(connection, "SELECT pgmq.create('stack_probe')");
        var sent = await ScalarAsync<long>(connection, """SELECT * FROM pgmq.send('stack_probe', '{"probe": true}'::jsonb)""");
        (await ScalarAsync<long>(connection, "SELECT msg_id FROM pgmq.read('stack_probe', 30, 1)")).Should().Be(sent);
    }

    /// <summary>The schema Supabase Storage keeps its tables in is there, and a signed-in user may use it.</summary>
    [Fact]
    public async Task The_supabase_image_has_the_storage_schema()
    {
        var database = await stack.CreateDatabaseAsync(Cancellation);
        await using var connection = await OpenAsync(database.AsMigrationRole);

        (await RowAsync(connection, """
            SELECT pg_get_userbyid(nspowner)::text,
                   has_schema_privilege('authenticated', 'storage', 'USAGE'),
                   has_schema_privilege(current_user, 'storage', 'USAGE'),
                   has_schema_privilege(current_user, 'storage', 'CREATE')
            FROM pg_namespace WHERE nspname = 'storage'
            """))
            .Should().Equal(["supabase_admin", true, true, false], "the schema is Supabase's: callers and the migration role may use it, and nobody but Supabase adds to it");
    }

    /// <summary>
    /// The image has the Storage and Realtime schemas and none of their tables: each server makes its own when
    /// it migrates, as the Auth server does. So a policy on a file or on a broadcast cannot be applied to the
    /// image alone, and what such a policy does is proven where those servers have run.
    /// </summary>
    [Fact]
    public async Task The_supabase_image_leaves_the_storage_and_realtime_tables_to_their_servers()
    {
        var database = await stack.CreateDatabaseAsync(Cancellation);
        await using var connection = await OpenAsync(database.AsMigrationRole);

        (await ScalarAsync<string[]>(connection, "SELECT array_agg(nspname::text ORDER BY nspname) FROM pg_namespace WHERE nspname IN ('storage', 'realtime')"))
            .Should().Equal("realtime", "storage");
        (await RowAsync(connection, "SELECT to_regclass('storage.objects')::text, to_regclass('storage.buckets')::text, to_regclass('realtime.messages')::text"))
            .Should().Equal([null, null, null], "the tables come with the Storage and Realtime servers' own migrations, and neither server is part of this stack");
    }

    // ---- The Auth server ----------------------------------------------------------------------------------

    /// <summary>
    /// An address that belongs to a user who has proven it is not invited again, and the admin client says so
    /// with an answer of its own instead of an error. What Auth answers is recorded here, in both shapes its
    /// errors come in, because the client decides on exactly this.
    /// </summary>
    [Fact]
    public async Task Inviting_an_address_with_an_account_is_already_registered()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var address = NewAddress("registered");
        await admin.CreateUserAsync(new SupabaseNewUser(address, EmailConfirmed: true), Cancellation);

        (await admin.InviteByEmailAsync(address, options: null, Cancellation))
            .Should().BeOfType<SupabaseInvitationResult.AlreadyRegistered>();

        using var http = AsServiceRole(auth);
        using (var answer = await http.PostAsJsonAsync("invite", new { email = address }, Cancellation))
        {
            answer.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await answer.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("error_code").GetString().Should().Be("email_exists");
        }

        using var newer = new HttpRequestMessage(HttpMethod.Post, "invite") { Content = JsonContent.Create(new { email = address }) };
        newer.Headers.Add("X-Supabase-Api-Version", "2024-01-01");
        using (var answer = await http.SendAsync(newer, Cancellation))
        {
            answer.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await answer.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("code").GetString().Should().Be("email_exists");
        }

        // Auth mails before it answers, so a mail would be there by now.
        (await auth.Mail.SentToAsync(address, atLeast: 0, Cancellation)).Should().BeEmpty("nobody is mailed about an account they already have");
    }

    /// <summary>
    /// The other side of the same answer: an address whose user never proved it is mailed again and keeps its
    /// id, so inviting twice is harmless. Making a user for that address is refused all the same, proven or
    /// not, which is what lets an application make the user first and invite after.
    /// </summary>
    [Fact]
    public async Task Inviting_an_address_that_was_never_proven_mails_it_again_under_the_same_id()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var address = NewAddress("waiting");

        var first = (await admin.InviteByEmailAsync(address, options: null, Cancellation)).Should().BeOfType<SupabaseInvitationResult.Sent>().Subject;
        var second = (await admin.InviteByEmailAsync(address, options: null, Cancellation)).Should().BeOfType<SupabaseInvitationResult.Sent>().Subject;

        second.UserId.Should().Be(first.UserId);
        (await auth.Mail.SentToAsync(address, atLeast: 2, Cancellation)).Should().HaveCount(2);

        var refused = await FluentActions.Awaiting(() => admin.CreateUserAsync(new SupabaseNewUser(address), Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();
        refused.Which.AddressAlreadyRegistered.Should().BeTrue();
    }

    /// <summary>
    /// With sign-ups off nobody makes an account for themselves, and the application still invites: that is how
    /// a project that only admits invited people is set up, and how this stack is.
    /// </summary>
    [Fact]
    public async Task Inviting_works_with_sign_ups_off()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();

        using var anyone = new HttpClient { BaseAddress = auth.Url };
        using (var signUp = await anyone.PostAsJsonAsync("signup", new { email = NewAddress("stranger"), password = NewPassword() }, Cancellation))
        {
            signUp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await signUp.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("error_code").GetString().Should().Be("signup_disabled");
        }

        var invited = (await admin.InviteByEmailAsync(NewAddress("invited"), options: null, Cancellation))
            .Should().BeOfType<SupabaseInvitationResult.Sent>().Subject;

        var user = await admin.FindUserAsync(invited.UserId, Cancellation);
        user.Should().NotBeNull("the user is there at once, under the id the invitation answered");
        user!.InvitedAt.Should().NotBeNull();
        user.EmailConfirmedAt.Should().BeNull("the address is proven by following the link, not by being invited");
        user.HasSignedIn.Should().BeFalse();
    }

    /// <summary>The stack's Auth server mails through the catcher, with a link that leads where a project's leads.</summary>
    [Fact]
    public async Task An_invited_address_gets_its_mail_in_the_catcher()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var address = NewAddress("invited");

        (await admin.InviteByEmailAsync(address, options: null, Cancellation)).Should().BeOfType<SupabaseInvitationResult.Sent>();

        var mail = (await auth.Mail.SentToAsync(address, atLeast: 1, Cancellation)).Should().ContainSingle().Subject;
        mail.From.Should().Be("tenancy@example.test");
        mail.Subject.Should().NotBeNullOrWhiteSpace();
        mail.Text.Should().Contain(SupabaseTokens.IssuerOf(SampleSupabaseStack.ProjectUrl) + "/verify?token=").And.Contain("type=invite");
    }

    /// <summary>
    /// The demonstration's people have fixed ids, and their seats are found by them. The admin API makes a user
    /// under the id it is given, so a person who signs in for real is the person the seat was made for.
    /// </summary>
    [Fact]
    public async Task The_admin_api_keeps_a_given_user_id()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var given = Guid.NewGuid();

        var made = await admin.CreateUserAsync(new SupabaseNewUser(NewAddress("given"), Id: given), Cancellation);

        made.Id.Should().Be(given);
        (await admin.FindUserAsync(given, Cancellation)).Should().NotBeNull();

        // An id is one user's: a second user under it is refused, with the database's code for a duplicate key.
        var taken = await FluentActions.Awaiting(() => admin.CreateUserAsync(new SupabaseNewUser(NewAddress("second"), Id: given), Cancellation))
            .Should().ThrowAsync<SupabaseAuthAdminException>();
        taken.Which.Status.Should().Be(500);
        taken.Which.AuthErrorCode.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    /// <summary>
    /// A person who signs in at the Auth server with a password gets a token the sample's host takes beside the
    /// dev login's: the same issuer and the same audience, and a signature of another kind. So both logins work
    /// side by side on a developer's machine, and the subject finds the same seat either way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What is pinned.</b> The stack gives Auth a signing key, as the Auth server of Supabase's own local
    /// stack is given one, so Auth signs a person's token with that key (ES256), names the key in the token's
    /// header, and publishes its public half, and nothing else, where a project publishes its keys. That is
    /// also how a hosted project with signing keys signs. The dev login signs with the local secret (HS256).
    /// </para>
    /// <para>
    /// <b>Why.</b> While this stack signed with the secret, every scenario on Supabase's images checked Auth's
    /// tokens with the secret the dev login uses, and the path a real project takes, a key fetched from Auth,
    /// was proven by a stand-in alone. Now a token of this Auth server passes only if the host fetched Auth's
    /// key and checked the token with it. If another version of the Auth image signs or publishes differently,
    /// this is the test that says so.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_password_grant_token_from_the_local_auth_server_validates_with_the_dev_login_on()
    {
        var auth = await stack.AuthAsync(Cancellation);
        using var admin = auth.Admin();
        var person = DemoPeople.Rhea;
        var password = NewPassword();

        // Rhea at Auth, under the id her seat is found by. Another test of this run may have made her already.
        if (await admin.FindUserAsync(person.Id, Cancellation) is null)
        {
            await admin.CreateUserAsync(new SupabaseNewUser(person.Email, person.Id, password, EmailConfirmed: true), Cancellation);
        }
        else
        {
            await admin.UpdateUserAsync(person.Id, new SupabaseUserChange(Password: password), Cancellation);
        }

        using var anyone = new HttpClient { BaseAddress = auth.Url };
        using var signIn = await anyone.PostAsJsonAsync("token?grant_type=password", new { email = person.Email, password }, Cancellation);
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await signIn.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("access_token").GetString()!;

        var jwt = new JsonWebToken(token);
        jwt.Alg.Should().Be("ES256", "Auth was given a signing key, and signs a person's token with it and not with the secret");
        jwt.Issuer.Should().Be(SupabaseTokens.IssuerOf(SampleSupabaseStack.ProjectUrl));
        jwt.Audiences.Should().Equal(SupabaseTokens.Audience);
        jwt.Subject.Should().Be(person.Id.ToString());
        jwt.GetClaim("role").Value.Should().Be("authenticated");

        // The key the token names is the one Auth publishes: its public half, and no secret beside it.
        var published = await anyone.GetFromJsonAsync<JsonElement>(".well-known/jwks.json", Cancellation);
        var key = published.GetProperty("keys").EnumerateArray().Should().ContainSingle("Auth publishes its signing key and never its secret").Subject;
        key.GetProperty("kid").GetString().Should().Be(jwt.Kid).And.NotBeNullOrEmpty();
        (key.GetProperty("kty").GetString(), key.GetProperty("crv").GetString(), key.GetProperty("alg").GetString()).Should().Be(("EC", "P-256", "ES256"));
        key.TryGetProperty("d", out _).Should().BeFalse("the private half stays with Auth");

        // The host as a developer runs it: Development, with the dev login on, and told where Auth answers,
        // which is where it fetches that key.
        await using var sample = await SampleOnPostgres.CreateAsync(
            stack, Cancellation, settings: new Dictionary<string, string> { [SampleAuthentication.AuthUrlSetting] = auth.Url.ToString() });
        using (var real = sample.Host.Client(token, tenant: null))
        {
            (await TenantsSeatedInAsync(real)).Should().Equal("harbor");
        }

        using var dev = await sample.Host.ClientAsync(person.Key, tenant: null);
        (await TenantsSeatedInAsync(dev)).Should().Equal("harbor");

        // A host that is not told where Auth answers asks at the project's URL, where this Auth server is
        // not, and refuses the token: the secret it checks the dev login's with does not check this one.
        await using var elsewhere = await SampleOnPostgres.CreateAsync(stack, Cancellation);
        using (var unknown = elsewhere.Host.Client(token, tenant: null))
        using (var refused = await unknown.GetAsync("/me/seats", Cancellation))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        static async Task<IEnumerable<string?>> TenantsSeatedInAsync(HttpClient client)
        {
            var seats = await client.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation);
            return [.. seats.EnumerateArray().Select(seat => seat.GetProperty("tenant").GetProperty("slug").GetString())];
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------------

    /// <summary>An address nobody else in this run uses, on a domain that can never receive mail.</summary>
    private static string NewAddress(string who) => $"{who}-{Guid.NewGuid():N}@example.test";

    private static string NewPassword() => "Pw-" + Guid.NewGuid().ToString("N");

    /// <summary>A client that calls the Auth server with the service role's token, as the admin client does.</summary>
    private static HttpClient AsServiceRole(SupabaseAuthServer auth)
    {
        var http = new HttpClient { BaseAddress = auth.Url };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.ServiceRoleKey);
        return http;
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

    /// <summary>The one row <paramref name="sql"/> answers, a <see langword="null"/> for each column that is null.</summary>
    private static async Task<object?[]> RowAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        (await reader.ReadAsync(Cancellation)).Should().BeTrue("'{0}' answers a row", sql);

        var row = new object?[reader.FieldCount];
        for (var column = 0; column < row.Length; column++)
        {
            row[column] = await reader.IsDBNullAsync(column, Cancellation) ? null : reader.GetValue(column);
        }

        return row;
    }

    /// <summary>The code Postgres refuses <paramref name="sql"/> with; fails when it does not refuse it.</summary>
    private static async Task<string> RefusedAsync(NpgsqlConnection connection, string sql)
    {
        var refused = await FluentActions.Awaiting(() => RunAsync(connection, sql)).Should().ThrowAsync<PostgresException>("'{0}' must be refused", sql);
        return refused.Which.SqlState;
    }
}
