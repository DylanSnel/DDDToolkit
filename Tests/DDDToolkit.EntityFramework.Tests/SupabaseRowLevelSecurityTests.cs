using System.Data;
using System.Transactions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Row level security applied to a context's own queries, against a real Postgres set up with Supabase's
/// roles and auth functions. The policy under test is the usual one: a note is its owner's.
/// </summary>
public sealed class SupabaseRowLevelSecurityTests(SupabaseRowLevelSecurityDatabase database)
    : IClassFixture<SupabaseRowLevelSecurityDatabase>, IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly CallerOfTheTest _caller = new();

    public async ValueTask InitializeAsync()
    {
        if (database.Available)
        {
            await database.ClearAsync();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_user_sees_only_their_own_rows_and_the_database_fills_in_whose_they_are()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        await WriteAsAsync(Bob, "Bob's");

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = database.CreateContext(_caller);
        var notes = await context.Notes.ToListAsync(Cancellation);

        notes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Text = "Alice's", Owner = (Guid?)Alice });
    }

    [Fact]
    public async Task A_user_cannot_write_a_row_for_somebody_else()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = database.CreateContext(_caller);
        context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Bob's, says Alice", Owner = Bob });

        var save = () => context.SaveChangesAsync(Cancellation);

        (await save.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "the policy's WITH CHECK refuses the row");
    }

    [Fact]
    public async Task A_caller_without_a_user_sees_nothing_and_postgres_knows_them_as_anon()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");

        _caller.Current = Caller.Anonymous;
        await using var context = database.CreateContext(_caller);

        (await context.Notes.CountAsync(Cancellation)).Should().Be(0, "the policy only lets authenticated users in");
        (await ScalarAsync(context, "SELECT current_user")).Should().Be("anon");
        (await ScalarAsync(context, "SELECT auth.role()")).Should().Be("anon", "PostgREST gives a request without a user the claims {\"role\":\"anon\"}");
        (await ScalarAsync(context, "SELECT coalesce(auth.uid()::text, 'none')")).Should().Be("none");
    }

    [Fact]
    public async Task The_claims_reach_postgres_exactly_as_they_were_signed()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice, email: "alice@example.com"));
        await using var context = database.CreateContext(_caller);

        (await ScalarAsync(context, "SELECT auth.uid()::text")).Should().Be(Alice.ToString());
        (await ScalarAsync(context, "SELECT auth.jwt() ->> 'email'")).Should().Be("alice@example.com");
        (await ScalarAsync(context, "SELECT auth.jwt() -> 'app_metadata' -> 'teams' ->> 0")).Should().Be("north", "nested claims stay nested");
        (await ScalarAsync(context, "SELECT current_user")).Should().Be("authenticated");
    }

    [Fact]
    public async Task Background_work_runs_as_the_system_role_when_one_is_set()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        await WriteAsAsync(Bob, "Bob's");

        _caller.Current = Caller.System;
        await using var context = database.CreateContext(_caller, new PostgresRowLevelSecurityOptions { SystemRole = SupabaseRowLevelSecurity.ServiceRole });

        (await context.Notes.CountAsync(Cancellation)).Should().Be(2, "service_role bypasses row level security, as a secret key does");
        (await ScalarAsync(context, "SELECT current_user")).Should().Be("service_role");
    }

    [Fact]
    public async Task Background_work_without_a_system_role_runs_as_the_login_role()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        _caller.Current = Caller.System;

        // Logged in as a role that may only switch roles, that fails closed.
        await using (var application = database.CreateContext(_caller))
        {
            (await ScalarAsync(application, "SELECT current_user")).Should().Be(LoginRole);

            var read = () => application.Notes.CountAsync(Cancellation);
            (await read.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        // Logged in as the owner, as an application on Supabase that logs in as postgres is, it sees everything.
        await using (var owner = database.CreateContext(_caller, connectionString: database.OwnerConnectionString))
        {
            (await owner.Notes.CountAsync(Cancellation)).Should().Be(1);
        }
    }

    [Fact]
    public async Task A_pooled_connection_forgets_the_last_caller_before_anyone_else_uses_it()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        await WriteAsAsync(Bob, "Bob's");

        // One connection in the pool, so every open below gets the same one.
        await using var pool = new NpgsqlDataSourceBuilder(
            new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString) { MaxPoolSize = 1 }.ConnectionString).Build();

        await using var context = new NotesContext(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(pool)
            .AddInterceptors(new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions()))
            .Options);

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        (await context.Notes.Select(note => note.Text).ToListAsync(Cancellation)).Should().Equal("Alice's");
        var backend = await ScalarAsync(context, "SELECT pg_backend_pid()::text");

        _caller.Current = Callers.FromClaims(ClaimsOf(Bob));
        (await context.Notes.Select(note => note.Text).ToListAsync(Cancellation)).Should().Equal("Bob's");

        // Code that does not go through the interceptor gets the very same server connection, and none of it.
        await using var plain = await pool.OpenConnectionAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT pg_backend_pid()::text || '|' || current_user || '|' || coalesce(current_setting('request.jwt.claims', true), '')", plain);
        (await command.ExecuteScalarAsync(Cancellation)).Should().Be($"{backend}|{LoginRole}|");
    }

    [Theory]
    [InlineData("Host=db.example.com;Database=postgres;No Reset On Close=true", "No Reset On Close")]
    [InlineData("Host=db.example.com;Database=postgres;Multiplexing=true", "Multiplexing")]
    [InlineData("Host=aws-0-eu-west-1.pooler.supabase.com;Port=6543;Database=postgres", "6543")]
    [InlineData("Host=aws-0-eu-west-1.pooler.supabase.com:6543;Database=postgres", "6543")]
    public async Task Connection_strings_that_would_hand_a_callers_role_to_someone_else_are_refused_before_connecting(string connectionString, string named)
    {
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = new NotesContext(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions()))
            .Options);

        var open = () => context.Database.OpenConnectionAsync(Cancellation);

        (await open.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{named}*");
    }

    [Fact]
    public async Task SystemIn_runs_as_the_system_in_role_with_its_claims_and_scope()
    {
        database.Require();

        _caller.Current = Caller.SystemIn("tenancy");
        await using var context = database.CreateContext(_caller);

        (await ScalarAsync(context, "SELECT current_user")).Should().Be(PostgresRowLevelSecurityOptions.DefaultSystemInRole);
        (await ScalarAsync(context, "SELECT current_setting('request.jwt.claims', true)")).Should().Be("""{"role":"ddd_system_in","scope":"tenancy"}""");
        (await ScalarAsync(context, "SELECT auth.role()")).Should().Be(PostgresRowLevelSecurityOptions.DefaultSystemInRole);
        (await ScalarAsync(context, "SELECT auth.jwt() ->> 'scope'")).Should().Be("tenancy", "a policy can ask whose work it is");
        (await ScalarAsync(context, "SELECT coalesce(auth.uid()::text, 'none')")).Should().Be("none");
    }

    [Fact]
    public async Task SystemIn_without_a_system_in_role_throws_before_anything_runs()
    {
        database.Require();

        _caller.Current = Caller.SystemIn("tenancy");
        await using var context = database.CreateContext(_caller, new PostgresRowLevelSecurityOptions { SystemInRole = null });

        var read = () => context.Notes.CountAsync(Cancellation);

        (await read.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*system in tenancy*SystemInRole is null*");
        context.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed, "it failed before connecting, so nothing ran as another role");
    }

    public static TheoryData<CallerKind> EveryKind() => new(Enum.GetValues<CallerKind>());

    [Theory]
    [MemberData(nameof(EveryKind))]
    public async Task No_kind_is_ever_given_the_system_role_by_default(CallerKind kind)
    {
        database.Require();

        _caller.Current = kind switch
        {
            CallerKind.System => Caller.System,
            CallerKind.Anonymous => Caller.Anonymous,
            CallerKind.User => Callers.FromClaims(ClaimsOf(Alice)),
            CallerKind.SystemIn => Caller.SystemIn("tenancy"),
            _ => throw new InvalidOperationException(
                $"This test has no caller of kind {kind}. Add one here, and give the kind a role of its own in PostgresRowLevelSecurityOptions.RoleOf and claims of its own in the interceptor."),
        };

        await using var context = database.CreateContext(_caller, new PostgresRowLevelSecurityOptions { SystemRole = SupabaseRowLevelSecurity.ServiceRole });
        var role = await ScalarAsync(context, "SELECT current_user");

        if (kind == CallerKind.System)
        {
            role.Should().Be(SupabaseRowLevelSecurity.ServiceRole);
        }
        else
        {
            role.Should().NotBe(SupabaseRowLevelSecurity.ServiceRole, "only the system itself runs as the system's role")
                .And.NotBe(LoginRole, "nor as the role the application logged in as");
        }
    }

    [Fact]
    public async Task The_system_in_role_has_no_bypass()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");

        _caller.Current = Caller.SystemIn("tenancy");
        await using var context = database.CreateContext(_caller);

        (await context.Notes.CountAsync(Cancellation)).Should().Be(0, "it may read the table, but no policy lets it see a row, and it cannot pass by the one there is");
        (await ScalarAsync(context, "SELECT (SELECT rolbypassrls::text FROM pg_catalog.pg_roles WHERE rolname = current_user)")).Should().Be("false");
    }

    [Fact]
    public async Task A_caller_begun_on_an_open_connection_is_applied_before_the_next_command()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        await WriteAsAsync(Bob, "Bob's");
        await using var context = database.CreateContext(new AmbientCallerAccessor());

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Alice))))
        {
            await context.Database.OpenConnectionAsync(Cancellation);
            (await TextsAsync(context)).Should().Equal("Alice's");
            var backend = await ScalarAsync(context, "SELECT pg_backend_pid()::text");

            using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
            {
                (await TextsAsync(context)).Should().Equal(["Bob's"], "the connection stayed open, and the caller that changed was set before the next command");
                (await ScalarAsync(context, "SELECT pg_backend_pid()::text")).Should().Be(backend);
            }

            (await TextsAsync(context)).Should().Equal(["Alice's"], "and back again when that caller ended");
        }
    }

    [Fact]
    public async Task A_caller_begun_before_a_transaction_on_an_open_connection_is_applied_when_it_begins()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        await WriteAsAsync(Bob, "Bob's");
        await using var context = database.CreateContext(Strict());

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Alice))))
        {
            await context.Database.OpenConnectionAsync(Cancellation);
            (await TextsAsync(context)).Should().Equal("Alice's");
        }

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
            (await TextsAsync(context)).Should().Equal(["Bob's"], "the caller changed before the transaction began, which is where it belongs");
        }
    }

    [Fact]
    public async Task A_caller_begun_inside_a_transaction_is_refused_not_ignored()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        await WriteAsAsync(Bob, "Bob's");
        await using var context = database.CreateContext(Strict());

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Alice))))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
            (await TextsAsync(context)).Should().Equal("Alice's");

            using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
            {
                var read = () => TextsAsync(context);

                (await read.Should().ThrowAsync<InvalidOperationException>())
                    .WithMessage($"The caller changed from authenticated {Alice} to authenticated {Bob} while a transaction was open on this connection.*begin the caller before the transaction*");
            }

            (await TextsAsync(context)).Should().Equal(["Alice's"], "the transaction runs as the caller it began with");
        }
    }

    [Fact]
    public async Task A_caller_begun_inside_a_transaction_is_logged_without_explicit_callers()
    {
        database.Require();

        await WriteAsAsync(Alice, "Alice's");
        await WriteAsAsync(Bob, "Bob's");
        var logger = new KeptLogger<PostgresRowLevelSecurityInterceptor>();
        await using var context = database.CreateContext(new PostgresRowLevelSecurityInterceptor(new AmbientCallerAccessor(), new PostgresRowLevelSecurityOptions(), [], callerOptions: null, logger));

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Alice))))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
            (await TextsAsync(context)).Should().Equal("Alice's");

            using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
            {
                (await TextsAsync(context)).Should().Equal(["Alice's"], "as before, a transaction goes on as the caller it began with");
                (await TextsAsync(context)).Should().Equal("Alice's");
            }

            await transaction.CommitAsync(Cancellation);
        }

        logger.Entries.Where(entry => entry.Level == LogLevel.Warning).Should().ContainSingle("it is logged once per connection")
            .Which.Message.Should().Contain($"from authenticated {Alice} to authenticated {Bob} while a transaction was open");

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
        {
            (await TextsAsync(context)).Should().Equal(["Bob's"], "outside the transaction the new caller is set");
        }
    }

    [Fact]
    public async Task Every_registered_setting_is_set_on_every_open_and_empty_when_absent()
    {
        database.Require();

        var tenant = new TenantSetting { Current = "north" };
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = database.CreateContext(new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions(), [tenant]));

        (await ScalarAsync(context, $"SELECT current_setting('{TenantSetting.Tenant}', true)")).Should().Be("north");
        (await ScalarAsync(context, $"SELECT current_setting('{TenantSetting.Unit}', true)")).Should().Be("", "a setting the provider owns and left out is emptied");

        tenant.Current = "south";
        (await ScalarAsync(context, $"SELECT current_setting('{TenantSetting.Tenant}', true)")).Should().Be("south", "every open asks again");

        tenant.Current = null;
        (await ScalarAsync(context, $"SELECT current_setting('{TenantSetting.Tenant}', true)")).Should().Be("");

        await context.Database.OpenConnectionAsync(Cancellation);
        tenant.Current = "east";
        (await ScalarAsync(context, $"SELECT current_setting('{TenantSetting.Tenant}', true)")).Should().Be("east", "a setting that changed on an open connection is set before the next command, as a caller is");
    }

    [Fact]
    public async Task A_pooled_connection_comes_back_with_empty_settings()
    {
        database.Require();

        await using var pool = new NpgsqlDataSourceBuilder(
            new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString) { MaxPoolSize = 1 }.ConnectionString).Build();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = new NotesContext(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(pool)
            .AddInterceptors(new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions(), [new TenantSetting { Current = "north" }]))
            .Options);

        (await ScalarAsync(context, $"SELECT current_setting('{TenantSetting.Tenant}', true)")).Should().Be("north");
        var backend = await ScalarAsync(context, "SELECT pg_backend_pid()::text");

        await using var plain = await pool.OpenConnectionAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            $"SELECT pg_backend_pid()::text || '|' || current_user || '|' || coalesce(current_setting('{TenantSetting.Tenant}', true), '')", plain);

        (await command.ExecuteScalarAsync(Cancellation)).Should().Be($"{backend}|{LoginRole}|", "the pool's reset takes the settings off with the role");
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("Test.tenant")]
    [InlineData("test.tenant.unit")]
    [InlineData("test.ten-ant")]
    [InlineData("1test.tenant")]
    [InlineData("test.")]
    [InlineData("test.tenant\n")]
    [InlineData("request.tenant")]
    public void Setting_names_are_checked_when_the_interceptor_is_built(string name)
    {
        var build = () => new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions(), [new DeclaredSettings([name])]);

        build.Should().Throw<ArgumentException>().WithParameterName("settings").WithMessage($"{nameof(DeclaredSettings)} declares the setting*");
    }

    [Fact]
    public void Setting_names_are_checked_when_the_interceptor_is_built_and_a_name_has_one_owner()
    {
        var build = () => new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions(), [new TenantSetting(), new DeclaredSettings([TenantSetting.Tenant])]);

        build.Should().Throw<ArgumentException>().WithParameterName("settings")
            .WithMessage($"The setting '{TenantSetting.Tenant}' is declared by {nameof(TenantSetting)} and by {nameof(DeclaredSettings)}*");
    }

    [Fact]
    public async Task A_value_for_an_undeclared_name_throws()
    {
        var provider = new DeclaredSettings([TenantSetting.Tenant], new KeyValuePair<string, string>(TenantSetting.Unit, "north"));
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = new NotesContext(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql("Host=db.example.com;Database=postgres")
            .AddInterceptors(new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions(), [provider]))
            .Options);

        var open = () => context.Database.OpenConnectionAsync(Cancellation);

        (await open.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"{nameof(DeclaredSettings)} gave a value for '{TenantSetting.Unit}', which is not one of its Names*");
    }

    [Fact]
    public async Task Settings_travel_in_the_same_statement_as_role_and_claims()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        var unpooled = new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString) { Pooling = false }.ConnectionString;
        await using var context = database.CreateContext(
            new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions(), [new TenantSetting { Current = "north" }]),
            unpooled);

        await context.Database.OpenConnectionAsync(Cancellation);
        var backend = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;

        // What the server last ran on that connection, asked from another: the statement the interceptor sent on open.
        await using var owner = new NpgsqlConnection(database.OwnerConnectionString);
        await owner.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT query FROM pg_catalog.pg_stat_activity WHERE pid = $1", owner);
        command.Parameters.Add(new NpgsqlParameter { Value = backend });
        var statement = (string?)await command.ExecuteScalarAsync(Cancellation);

        statement.Should().Be(
            "SELECT set_config('role', $1, false), set_config('request.jwt.claims', $2, false), " +
            "set_config('request.jwt.claim.sub', '', false), set_config('request.jwt.claim.role', '', false), " +
            "set_config('request.jwt.claim.email', '', false), set_config('request.jwt.claim', '', false), " +
            "set_config($3, $4, false), set_config($5, $6, false)",
            "the role, the claims, the emptied claim settings and both settings of the provider are one statement, one round trip");
        (await ScalarAsync(context, $"SELECT current_user || '|' || current_setting('{TenantSetting.Tenant}', true)")).Should().Be("authenticated|north");
    }

    [Fact]
    public async Task A_legacy_claim_left_on_the_backend_does_not_decide_who_runs()
    {
        database.Require();

        // Every session of the application's login role in this database starts with Bob's id and e-mail where
        // Supabase's auth.uid() and auth.email() look first, as a server connection a pooler did not reset would.
        var owner = await database.CreateDatabaseAsync("legacy_claims");
        await using (var connection = new NpgsqlConnection(owner))
        {
            await connection.OpenAsync(Cancellation);
            await using var dirty = new NpgsqlCommand(
                $"ALTER ROLE {LoginRole} IN DATABASE legacy_claims SET request.jwt.claim.sub = '{Bob}'; " +
                $"ALTER ROLE {LoginRole} IN DATABASE legacy_claims SET request.jwt.claim.email = 'bob@example.com'",
                connection);
            await dirty.ExecuteNonQueryAsync(Cancellation);
        }

        var application = AsApplication(owner);
        await using (var plain = new NpgsqlConnection(application))
        {
            await plain.OpenAsync(Cancellation);
            await using var read = new NpgsqlCommand("SELECT current_setting('request.jwt.claim.sub', true)", plain);
            (await read.ExecuteScalarAsync(Cancellation)).Should().Be(Bob.ToString(), "without the interceptor, a session there starts as Bob");
        }

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice, email: "alice@example.com"));
        await using var context = database.CreateContext(_caller, connectionString: application);

        (await ScalarAsync(context, "SELECT auth.uid()::text")).Should().Be(Alice.ToString(), "the interceptor empties the setting auth.uid() reads before the claims");
        (await ScalarAsync(context, "SELECT coalesce(auth.jwt() ->> 'sub', '')")).Should().Be(Alice.ToString());
        (await ScalarAsync(context, "SELECT auth.email()")).Should().Be("alice@example.com", "and the one auth.email() reads first");

        // Without a user the e-mail is nobody's, not the one left on the server connection.
        _caller.Current = Caller.Anonymous;
        await using var anonymous = database.CreateContext(_caller, connectionString: application);
        (await ScalarAsync(anonymous, "SELECT coalesce(auth.email(), 'none')")).Should().Be("none");
    }

    [Fact]
    public async Task A_connection_whose_caller_could_not_be_set_is_closed_rather_than_used_as_the_login_role()
    {
        database.Require();

        // A scoped system role that exists, but that the application's login role may not switch to.
        await using (var owner = new NpgsqlConnection(database.OwnerConnectionString))
        {
            await owner.OpenAsync(Cancellation);
            await using var create = new NpgsqlCommand(
                "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'outside_the_grants') THEN CREATE ROLE outside_the_grants NOLOGIN NOINHERIT; END IF; END $$",
                owner);
            await create.ExecuteNonQueryAsync(Cancellation);
        }

        _caller.Current = Caller.SystemIn("tenancy");
        await using var context = database.CreateContext(_caller, new PostgresRowLevelSecurityOptions { SystemInRole = "outside_the_grants" });

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var read = () => ScalarAsync(context, "SELECT current_user");

            (await read.Should().ThrowAsync<PostgresException>($"attempt {attempt} sets the caller again, and fails again"))
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            context.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed, "a connection the caller could not be set on is not kept open to run as the login role");
        }

        var readSynchronously = () => context.Database.SqlQueryRaw<string>("SELECT current_user AS \"Value\"").Single();
        readSynchronously.Should().Throw<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        context.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task A_rollback_that_undoes_the_callers_settings_is_followed_by_setting_them_again()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = database.CreateContext(_caller);

        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            // Opened inside the scope, so the statement that sets the caller runs inside its transaction.
            await context.Database.OpenConnectionAsync(Cancellation);
            (await ScalarAsync(context, "SELECT current_user")).Should().Be("authenticated");

            // Not completed: the transaction rolls back, and the settings made inside it with it.
        }

        (await ScalarAsync(context, "SELECT current_user || '|' || auth.uid()::text")).Should().Be(
            $"authenticated|{Alice}", "the connection stayed open, and the caller the rollback undid is set again before the next command");
    }

    [Fact]
    public async Task Under_explicit_callers_the_system_is_the_caller_only_where_it_was_begun()
    {
        database.Require();

        // An accessor of the host's own that falls back to the system by itself.
        var required = new CallerOptions { RequireExplicitCallers = true };
        _caller.Current = Caller.System;
        await using var context = database.CreateContext(new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions(), [], required));

        var read = () => ScalarAsync(context, "SELECT current_user");

        (await read.Should().ThrowAsync<NoCallerException>()).WithMessage($"{nameof(CallerOfTheTest)} answered the system, but nothing began Caller.System*RequireExplicitCallers*");
        context.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed, "it failed before connecting");

        using (Callers.Begin(Caller.System))
        {
            (await ScalarAsync(context, "SELECT current_user")).Should().Be(LoginRole, "begun on purpose, the system runs as the login role");
        }
    }

    [Fact]
    public async Task The_interceptor_the_services_build_takes_their_settings_and_caller_options()
    {
        using var services = new ServiceCollection()
            .AddPostgresRowLevelSecurity<CallerOfTheTest>()
            .RequireExplicitCallers()
            .AddSingleton<IRowLevelSecuritySettings>(new DeclaredSettings([TenantSetting.Tenant], new KeyValuePair<string, string>(TenantSetting.Unit, "north")))
            .BuildServiceProvider();
        var callers = (CallerOfTheTest)services.GetRequiredService<ICallerAccessor>();
        var options = new DbContextOptionsBuilder<NotesContext>().UseNpgsql("Host=db.example.com;Database=postgres");
        options.UsePostgresRowLevelSecurity(services);
        await using var context = new NotesContext(options.Options);

        // The caller options: the system, begun by nothing, is refused.
        callers.Current = Caller.System;
        var open = () => context.Database.OpenConnectionAsync(Cancellation);
        (await open.Should().ThrowAsync<NoCallerException>()).WithMessage("*answered the system, but nothing began Caller.System*");

        // The settings: a value for a name the provider did not declare fails, before anything connects.
        callers.Current = Callers.FromClaims(ClaimsOf(Alice));
        (await open.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"{nameof(DeclaredSettings)} gave a value for '{TenantSetting.Unit}', which is not one of its Names*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[\"a\"]")]
    public void Claims_are_a_json_object(string claims)
    {
        var act = () => Callers.FromClaims(claims);

        act.Should().Throw<ArgumentException>();
    }

    private async Task WriteAsAsync(Guid user, string text)
    {
        var caller = new CallerOfTheTest { Current = Callers.FromClaims(ClaimsOf(user)) };
        await using var context = database.CreateContext(caller);
        context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = text });
        await context.SaveChangesAsync(Cancellation);
    }

    /// <summary>An interceptor on the ambient caller, in a host that requires explicit callers.</summary>
    private static PostgresRowLevelSecurityInterceptor Strict()
    {
        var required = new CallerOptions { RequireExplicitCallers = true };
        return new PostgresRowLevelSecurityInterceptor(new AmbientCallerAccessor(required), new PostgresRowLevelSecurityOptions(), [], required);
    }

    private static Task<List<string>> TextsAsync(NotesContext context)
        => context.Notes.OrderBy(note => note.Text).Select(note => note.Text).ToListAsync(Cancellation);

    /// <summary>One value, from SQL the tests themselves write; nothing here comes from outside.</summary>
    private static async Task<string?> ScalarAsync(DbContext context, string sql)
    {
#pragma warning disable EF1003
        return await context.Database.SqlQueryRaw<string>(sql + " AS \"Value\"").SingleAsync(Cancellation);
#pragma warning restore EF1003
    }
}
