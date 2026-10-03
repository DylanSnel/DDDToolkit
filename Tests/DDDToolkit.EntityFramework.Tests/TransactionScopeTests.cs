using System.Data;
using System.Data.Common;
using System.Transactions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Row level security where the caller's settings last one transaction, against a real Postgres, on a
/// connection pool of one that is never reset: whatever a test leaves on the session, the plain client that
/// opens the pool's connection next reads. "Clean" below means that client finds the same backend, running as
/// the login role, with no claims and no tenant.
/// </summary>
[Collection(SettingsPerTransaction.Name)]
public sealed class TransactionScopeTests(PoolerFixture database) : IAsyncLifetime
{
    private readonly CallerOfTheTest _caller = new();
    private readonly TenantSetting _tenant = new() { Current = "north" };
    private readonly string _pool = "scope-" + Guid.NewGuid().ToString("N")[..12];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>One connection, never reset, of this test alone.</summary>
    private string Connection => database.Direct(_pool);

    public async ValueTask InitializeAsync()
    {
        if (database.Available)
        {
            await database.ClearAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (database.Available)
        {
            // The pool this test made is nobody else's, and its one connection would stay open until the run ends.
            using var connection = new NpgsqlConnection(Connection);
            NpgsqlConnection.ClearPool(connection);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_query_runs_as_the_caller_with_its_settings()
    {
        database.Require();
        await database.WriteAsync(Alice, "Alice's");
        await database.WriteAsync(Bob, "Bob's");

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();

        (await TextsAsync(context)).Should().Equal(["Alice's"], "the policy holds the query to the caller's rows");

        var state = await BackendState.OfAsync(context, Cancellation);
        state.Should().Be(new BackendState(state.Backend, "authenticated", LoginRole, ClaimsOf(Alice), "", "north"));
        (await ScalarAsync(context, "SELECT auth.uid()::text")).Should().Be(Alice.ToString());

        // And for another caller on the same context, the next command.
        _caller.Current = Caller.Anonymous;
        _tenant.Current = null;
        (await BackendState.OfAsync(context, Cancellation)).Should().Be(new BackendState(state.Backend, "anon", LoginRole, """{"role":"anon"}""", "", ""));
    }

    [Fact]
    public async Task After_a_query_the_backend_is_clean()
    {
        database.Require();
        await database.WriteAsync(Alice, "Alice's");

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        (await TextsAsync(context)).Should().Equal("Alice's");
        var backend = await BackendAsync(context);

        await ShouldBeCleanAsync(backend);
    }

    [Fact]
    public async Task After_a_save_the_backend_is_clean()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        var backend = await BackendAsync(context);

        var note = new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's" };
        context.Notes.Add(note);
        (await context.SaveChangesAsync(Cancellation)).Should().Be(1);

        note.Owner.Should().Be(Alice, "the save ran as the caller, whose id the database filled in");
        await ShouldBeCleanAsync(backend);
    }

    [Fact]
    public async Task After_a_user_transaction_the_backend_is_clean()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        int backend;

        await using (var transaction = await context.Database.BeginTransactionAsync(Cancellation))
        {
            var state = await BackendState.OfAsync(context, Cancellation);
            state.Should().Be(new BackendState(state.Backend, "authenticated", LoginRole, ClaimsOf(Alice), "", "north"), "the transaction's first statement set the caller");
            backend = state.Backend;

            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's" });
            (await context.SaveChangesAsync(Cancellation)).Should().Be(1);
            (await TextsAsync(context)).Should().Equal("Alice's");

            await transaction.CommitAsync(Cancellation);
        }

        await ShouldBeCleanAsync(backend);
        (await database.AsOwnerAsync("""SELECT count(*) FROM notes."Notes" """)).Should().Be("1");
    }

    [Fact]
    public async Task After_a_failed_statement_the_backend_is_clean()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        var backend = await BackendAsync(context);

        var divide = () => ScalarAsync(context, "SELECT (1 / (SELECT 0))::text");
        (await divide.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero);
        await ShouldBeCleanAsync(backend);

        // A save the policy refuses fails inside its transaction, which ends with it.
        context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Bob's, says Alice", Owner = Bob });
        var save = () => context.SaveChangesAsync(Cancellation);
        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeCleanAsync(backend);
    }

    [Fact]
    public async Task After_a_cancelled_command_the_backend_is_clean()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        var backend = await BackendAsync(context);

        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        cancelled.CancelAfter(TimeSpan.FromMilliseconds(300));
        var sleep = () => context.Database.SqlQueryRaw<string>("""SELECT pg_sleep(30)::text AS "Value" """).SingleAsync(cancelled.Token);

        await sleep.Should().ThrowAsync<OperationCanceledException>();
        await ShouldBeCleanAsync(backend);
    }

    [Fact]
    public async Task After_a_rolled_back_transaction_the_backend_is_clean()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        int backend;

        await using (var transaction = await context.Database.BeginTransactionAsync(Cancellation))
        {
            backend = await BackendAsync(context);
            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Never kept" });
            await context.SaveChangesAsync(Cancellation);
            await transaction.RollbackAsync(Cancellation);
        }

        await ShouldBeCleanAsync(backend);

        // And one that is given up without a word, which Entity Framework rolls back as it disposes it.
        await using (await context.Database.BeginTransactionAsync(Cancellation))
        {
            (await BackendAsync(context)).Should().Be(backend);
        }

        await ShouldBeCleanAsync(backend);
        (await database.AsOwnerAsync("""SELECT count(*) FROM notes."Notes" """)).Should().Be("0");
    }

    [Fact]
    public async Task A_transaction_given_up_on_an_open_connection_leaves_the_next_command_its_own_caller()
    {
        database.Require();
        await database.WriteAsync(Alice, "Alice's");
        await database.WriteAsync(Bob, "Bob's");

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();

        // The connection stays open over the end of the transaction, so nothing but the server can say that
        // the transaction is over: Entity Framework tells no interceptor of a transaction it only disposes.
        await context.Database.OpenConnectionAsync(Cancellation);
        int backend;
        await using (await context.Database.BeginTransactionAsync(Cancellation))
        {
            backend = await BackendAsync(context);
        }

        _caller.Current = Callers.FromClaims(ClaimsOf(Bob));
        (await BackendState.OfAsync(context, Cancellation)).Should().Be(
            new BackendState(backend, "authenticated", LoginRole, ClaimsOf(Bob), "", "north"),
            "the settings of the transaction went with it, so the next command carries its own call, for the caller there is now");
        (await TextsAsync(context)).Should().Equal("Bob's");

        await context.Database.CloseConnectionAsync();
        await ShouldBeCleanAsync(backend);
    }

    [Fact]
    public async Task After_ExecuteUpdate_and_raw_sql_the_backend_is_clean()
    {
        database.Require();
        await database.WriteAsync(Alice, "Alice's");
        await database.WriteAsync(Bob, "Bob's");

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        var backend = await BackendAsync(context);

        (await context.Notes.ExecuteUpdateAsync(notes => notes.SetProperty(note => note.Text, "Changed"), Cancellation))
            .Should().Be(1, "the call in front changes no rows, and the policy holds the update to the caller's one");
        await ShouldBeCleanAsync(backend);

        (await context.Database.ExecuteSqlAsync($"""UPDATE notes."Notes" SET "Text" = {"Raw"}""", Cancellation)).Should().Be(1, "a parameter of the command's own travels next to the caller's");
        await ShouldBeCleanAsync(backend);

        (await context.Notes.ExecuteDeleteAsync(Cancellation)).Should().Be(1);
        await ShouldBeCleanAsync(backend);

        (await database.AsOwnerAsync("""SELECT string_agg("Text", ',') FROM notes."Notes" """)).Should().Be("Bob's", "nothing of this reached a row of somebody else");
    }

    [Fact]
    public async Task A_scalar_command_gets_its_value_back_past_the_call_in_front()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();

        // Entity Framework asks this with ExecuteScalar: the first column of the first row, which the call has none of.
        (await context.GetService<IRelationalDatabaseCreator>().HasTablesAsync(Cancellation)).Should().BeTrue();
        context.GetService<IRelationalDatabaseCreator>().HasTables().Should().BeTrue("and the same without async");

        await ShouldBeCleanAsync(await BackendAsync(context));
    }

    [Fact]
    public async Task A_save_keeps_its_row_counts_and_its_concurrency_check()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var first = Guarded();
        await using var second = Guarded();

        var id = Guid.CreateVersion7();
        first.Notes.Add(new PrivateNote { Id = id, Text = "One" });
        (await first.SaveChangesAsync(Cancellation)).Should().Be(1, "a save of one insert runs in a transaction here, so the call is not in front of it and its row count is its own");

        first.Notes.AddRange(Enumerable.Range(0, 3).Select(index => new PrivateNote { Id = Guid.CreateVersion7(), Text = $"More {index}" }));
        (await first.SaveChangesAsync(Cancellation)).Should().Be(3);

        // Two contexts read the same note; the second to save has a stale text, which is the concurrency token.
        var mine = await first.Notes.SingleAsync(note => note.Id == id, Cancellation);
        var theirs = await second.Notes.SingleAsync(note => note.Id == id, Cancellation);
        mine.Text = "Mine";
        (await first.SaveChangesAsync(Cancellation)).Should().Be(1);

        theirs.Text = "Theirs";
        var stale = () => second.SaveChangesAsync(Cancellation);
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();

        (await database.AsOwnerAsync($"""SELECT "Text" FROM notes."Notes" WHERE "Id" = '{id}'""")).Should().Be("Mine", "the stale save changed nothing");
        first.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Always, "which is how every save of the context runs in this scope, and the context says so");
        await ShouldBeCleanAsync(await BackendAsync(first));
    }

    [Fact]
    public async Task A_save_runs_in_a_transaction_whatever_the_context_says()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;

        var note = new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's" };
        context.Notes.Add(note);
        (await context.SaveChangesAsync(Cancellation)).Should().Be(1);
        note.Owner.Should().Be(Alice, "the save ran in a transaction, whose first statement set the caller");
        context.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Always, "the interceptor set it, and leaves it: no save of this context can run without a transaction");

        // Set back by the application, it is set again by the next save, one that fails included.
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Bob's, says Alice", Owner = Bob });
        var refused = () => context.SaveChangesAsync(Cancellation);
        (await refused.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege, "the policy refused it, as the caller's");

        // And without async.
        context.ChangeTracker.Clear();
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Another" });
        context.SaveChanges().Should().Be(1);
        (await TextsAsync(context)).Should().Equal("Alice's", "Another");
        await ShouldBeCleanAsync(await BackendAsync(context));
    }

    [Fact]
    public async Task A_save_another_interceptor_refused_before_it_began_leaves_nothing_that_the_next_save_trips_over()
    {
        database.Require();

        // An interceptor that refuses a save after this one had its say, as the toolkit's own check of the
        // invariants does. Entity Framework tells no interceptor that such a save ended: it never began. So
        // nothing here may wait for the end of a save.
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        var refuses = new RefusesSaves();
        await using var context = new NotesContext(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(Connection)
            .AddInterceptors(PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Transaction, [_tenant]), refuses)
            .Options);

        context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's" });
        var refused = () => context.SaveChangesAsync(Cancellation);
        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage("Not this save.");
        var refusedToo = () => context.SaveChanges();
        refusedToo.Should().Throw<InvalidOperationException>().WithMessage("Not this save.");

        // The application says how the context saves, after two saves that never ended; the next save is in a
        // transaction all the same, as the caller.
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        refuses.Refusing = false;
        (await context.SaveChangesAsync(Cancellation)).Should().Be(1);
        context.Database.AutoTransactionBehavior.Should().Be(AutoTransactionBehavior.Always);
        (await TextsAsync(context)).Should().Equal("Alice's");
        await ShouldBeCleanAsync(await BackendAsync(context));
    }

    [Fact]
    public async Task A_savepoint_in_a_user_transaction_keeps_the_callers_settings()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        int backend;

        await using (var transaction = await context.Database.BeginTransactionAsync(Cancellation))
        {
            var began = await BackendState.OfAsync(context, Cancellation);
            backend = began.Backend;

            // A save inside a transaction makes a savepoint, and a save that fails goes back to it.
            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Kept" });
            (await context.SaveChangesAsync(Cancellation)).Should().Be(1);

            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Bob's, says Alice", Owner = Bob });
            var refused = () => context.SaveChangesAsync(Cancellation);
            await refused.Should().ThrowAsync<DbUpdateException>();
            context.ChangeTracker.Clear();

            (await BackendState.OfAsync(context, Cancellation)).Should().Be(began, "the settings were made at the transaction's top level, before any savepoint");

            // And a savepoint of the application's own.
            await transaction.CreateSavepointAsync("mine", Cancellation);
            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Undone" });
            await context.SaveChangesAsync(Cancellation);
            await transaction.RollbackToSavepointAsync("mine", Cancellation);
            context.ChangeTracker.Clear();

            (await BackendState.OfAsync(context, Cancellation)).Should().Be(began);
            (await TextsAsync(context)).Should().Equal("Kept");
            await transaction.CommitAsync(Cancellation);
        }

        await ShouldBeCleanAsync(backend);
    }

    [Fact]
    public async Task An_ambient_transaction_scope_gets_the_settings_on_every_command()
    {
        database.Require();
        await database.WriteAsync(Bob, "Bob's");

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        int backend;

        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            var state = await BackendState.OfAsync(context, Cancellation);
            state.Should().Be(new BackendState(state.Backend, "authenticated", LoginRole, ClaimsOf(Alice), "", "north"));
            backend = state.Backend;

            // The settings were sent once, before the first command, and last as long as the scope's
            // transaction: a save, whose commands cannot carry a call in front, finds them there.
            var note = new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's" };
            context.Notes.Add(note);
            (await context.SaveChangesAsync(Cancellation)).Should().Be(1);
            note.Owner.Should().Be(Alice);

            (await TextsAsync(context)).Should().Equal(["Alice's"], "the next query is the caller's as well");
            (await BackendAsync(context)).Should().Be(backend, "the scope keeps its connection");
            scope.Complete();
        }

        await ShouldBeCleanAsync(backend);
        (await database.AsOwnerAsync("""SELECT count(*) FROM notes."Notes" """)).Should().Be("2", "the scope committed");
    }

    [Fact]
    public async Task Positional_parameters_are_refused()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        var backend = await BackendAsync(context);

        // Entity Framework names every parameter it is given, so a positional one reaches the database only from a
        // command built with the pieces a provider builds its own from.
        var command = context.GetService<IRelationalCommandBuilderFactory>().Create()
            .Append("SELECT $1::int")
            .AddRawParameter("first", new NpgsqlParameter { Value = 41 })
            .Build();
        var positional = () => command.ExecuteScalarAsync(
            new RelationalCommandParameterObject(
                context.GetService<IRelationalConnection>(),
                new Dictionary<string, object?> { ["first"] = null },
                readerColumns: null,
                context,
                context.GetService<IRelationalCommandDiagnosticsLogger>(),
                CommandSource.Unknown),
            Cancellation);

        (await positional.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("In transaction scope a command's parameters must be named, as Entity Framework's are; positional parameters cannot be mixed with the caller's.");
        await ShouldBeCleanAsync(backend);

        // Inside a transaction nothing is written in front of a command, so there is nothing to mix with.
        await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
        (await positional()).Should().Be(41);
    }

    [Theory]
    [InlineData("COMMIT", "COMMIT")]
    [InlineData("commit;", "COMMIT")]
    [InlineData("  COMMIT AND CHAIN", "COMMIT")]
    [InlineData("COMMIT PREPARED 'later'", "COMMIT")]
    [InlineData("BEGIN", "BEGIN")]
    [InlineData("BEGIN ISOLATION LEVEL SERIALIZABLE", "BEGIN")]
    [InlineData("START TRANSACTION", "START TRANSACTION")]
    [InlineData("END", "END")]
    [InlineData("ROLLBACK", "ROLLBACK")]
    [InlineData("ROLLBACK WORK", "ROLLBACK")]
    [InlineData("ROLLBACK PREPARED 'later'", "ROLLBACK")]
    [InlineData("ABORT", "ABORT")]
    [InlineData("PREPARE TRANSACTION 'later'", "PREPARE TRANSACTION")]
    [InlineData("SET ROLE authenticated", "SET ROLE")]
    [InlineData("SET LOCAL ROLE authenticated", "SET ROLE")]
    [InlineData("SET SESSION ROLE none", "SET ROLE")]
    [InlineData("set role = 'anon'", "SET ROLE")]
    [InlineData("RESET ROLE", "RESET ROLE")]
    [InlineData("SET SESSION AUTHORIZATION DEFAULT", "SET SESSION AUTHORIZATION")]
    [InlineData("SET LOCAL SESSION AUTHORIZATION DEFAULT", "SET SESSION AUTHORIZATION")]
    [InlineData("SET session_authorization = 'shop_app'", "SET SESSION AUTHORIZATION")]
    [InlineData("RESET SESSION AUTHORIZATION", "RESET SESSION AUTHORIZATION")]
    [InlineData("RESET ALL", "RESET ALL")]
    [InlineData("DISCARD ALL", "DISCARD")]
    [InlineData("SELECT 1; COMMIT; SELECT 2", "COMMIT")]
    [InlineData("SELECT ';'; ROLLBACK", "ROLLBACK")]
    [InlineData("/* tidy up */ COMMIT", "COMMIT")]
    [InlineData("-- tidy up\nCOMMIT", "COMMIT")]
    [InlineData("SELECT $body$;$body$;\n\tEND", "END")]
    public async Task A_command_with_its_own_commit_is_refused_in_transaction_scope(string sql, string named)
    {
        database.Require();
        await database.WriteAsync(Alice, "Alice's");

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        var backend = await BackendAsync(context);

        var send = () => context.Database.ExecuteSqlRawAsync(sql, Cancellation);

        (await send.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"A command begins a statement with {named}, which row level security refuses where the caller's settings last one transaction*");
        await ShouldBeCleanAsync(backend);

        // Inside a transaction of the context's as well: what follows it there would run with nothing set.
        await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
        (await send.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"A command begins a statement with {named},*");
        (await TextsAsync(context)).Should().Equal(["Alice's"], "nothing of the command was sent, so the transaction goes on as the caller");
    }

    [Theory]
    [InlineData("SELECT CASE WHEN true THEN 1 ELSE 2 END")]
    [InlineData("SELECT 'COMMIT; BEGIN'")]
    [InlineData("SELECT $$ROLLBACK;$$")]
    [InlineData("SELECT $quoted$ it's; COMMIT $quoted$")]
    [InlineData("SELECT 1 /* COMMIT; /* nested; ABORT */ END; */")]
    [InlineData("SELECT 1 -- ; COMMIT")]
    [InlineData("SELECT E'a\\'; COMMIT; '")]
    [InlineData("SELECT \"end\" FROM (SELECT 1 AS \"end\") commit")]
    [InlineData("DO $do$ BEGIN PERFORM 1; END $do$")]
    [InlineData("SET LOCAL lock_timeout = '5s'")]
    [InlineData("RESET lock_timeout")]
    [InlineData("SAVEPOINT here; SELECT 1; ROLLBACK TO SAVEPOINT here; ROLLBACK TO here; ROLLBACK WORK TO SAVEPOINT here; RELEASE SAVEPOINT here")]
    public async Task A_case_expression_ending_in_end_is_not_refused(string sql)
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();

        // In a transaction, which the savepoints need, given up afterwards.
        await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
        var send = () => context.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal), Cancellation);

        await send.Should().NotThrowAsync("only the first words of a statement count, and none of these begins with a word that ends the transaction or changes the role");
    }

    [Fact]
    public async Task A_caller_begun_inside_a_transaction_is_refused_in_transaction_scope()
    {
        database.Require();
        await database.WriteAsync(Alice, "Alice's");
        await database.WriteAsync(Bob, "Bob's");

        var required = new CallerOptions { RequireExplicitCallers = true };
        await using var context = PoolerFixture.Context(
            Connection,
            PoolerFixture.Interceptor(new AmbientCallerAccessor(required), RowLevelSecurityScope.Transaction, callerOptions: required));

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
    public async Task A_caller_begun_inside_a_transaction_is_logged_once_without_explicit_callers_in_transaction_scope()
    {
        database.Require();
        await database.WriteAsync(Alice, "Alice's");
        await database.WriteAsync(Bob, "Bob's");

        var logger = new KeptLogger<PostgresRowLevelSecurityInterceptor>();
        await using var context = PoolerFixture.Context(Connection, PoolerFixture.Interceptor(new AmbientCallerAccessor(), RowLevelSecurityScope.Transaction, logger: logger));

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Alice))))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);

            using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
            {
                (await TextsAsync(context)).Should().Equal(["Alice's"], "a transaction goes on as the caller it began with");
                (await TextsAsync(context)).Should().Equal("Alice's");
            }

            await transaction.CommitAsync(Cancellation);
        }

        logger.Entries.Where(entry => entry.Level == LogLevel.Warning).Should().ContainSingle("it is logged once per connection")
            .Which.Message.Should().Contain($"from authenticated {Alice} to authenticated {Bob} while a transaction was open");

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
        {
            (await TextsAsync(context)).Should().Equal(["Bob's"], "outside the transaction every command is its caller's");
        }
    }

    [Fact]
    public async Task A_caller_that_cannot_run_fails_before_the_transaction_begins()
    {
        database.Require();

        _caller.Current = Caller.SystemIn("tenancy");
        await using var context = PoolerFixture.Context(
            Connection,
            PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Transaction, configure: options => options.SystemInRole = null));

        var begin = () => context.Database.BeginTransactionAsync(Cancellation);

        (await begin.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*system in tenancy*SystemInRole is null*");
        context.Database.CurrentTransaction.Should().BeNull();
    }

    [Fact]
    public async Task A_transaction_whose_caller_could_not_be_set_is_ended_rather_than_used_as_the_login_role()
    {
        database.Require();

        // A role that exists, and that the application's login role may not switch to.
        await database.AsOwnerAsync(
            "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'beyond_the_grants') THEN CREATE ROLE beyond_the_grants NOLOGIN NOINHERIT; END IF; END $$; SELECT 1");

        _caller.Current = Caller.SystemIn("tenancy");
        await using (var context = PoolerFixture.Context(
            Connection,
            PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Transaction, [_tenant], configure: options => options.SystemInRole = "beyond_the_grants")))
        {
            var begin = () => context.Database.BeginTransactionAsync(Cancellation);
            (await begin.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
            context.Database.CurrentTransaction.Should().BeNull("the transaction nothing could be set for was rolled back, not handed to the context");

            var query = () => BackendState.OfAsync(context, Cancellation);
            (await query.Should().ThrowAsync<PostgresException>("a command outside a transaction fails with its call, in the same round trip"))
                .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        // The connection is as it was for whoever comes next.
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var next = Context();
        await ShouldBeCleanAsync(await BackendAsync(next));
    }

    [Fact]
    public async Task Sql_on_GetDbConnection_outside_a_transaction_runs_as_the_login_role()
    {
        database.Require();

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Context();
        await context.Database.OpenConnectionAsync(Cancellation);
        var connection = context.Database.GetDbConnection();

        (await RawAsync(connection, transaction: null)).Should().Be(
            LoginRole, "nothing lives on the session in this scope, and a command sent past Entity Framework has no call in front of it");
        (await BackendState.OfAsync(context, Cancellation)).CurrentUser.Should().Be("authenticated", "the same connection, through Entity Framework");

        await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
        (await RawAsync(connection, transaction.GetDbTransaction())).Should().Be("authenticated", "inside a transaction the context began, everything on the connection is the caller's");

        static async Task<string?> RawAsync(DbConnection connection, DbTransaction? transaction)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT current_user::text";
            command.Transaction = transaction;
            return (string?)await command.ExecuteScalarAsync(Cancellation);
        }
    }

    [Fact]
    public async Task Session_settings_would_leak_under_No_Reset_On_Close()
    {
        database.Require();

        // The control: what the scope is for. A setting made for the session outlives the client that made it.
        int backend;
        await using (var first = new NpgsqlConnection(Connection))
        {
            await first.OpenAsync(Cancellation);
            await using var set = new NpgsqlCommand($"SELECT pg_catalog.pg_backend_pid() FROM pg_catalog.set_config('{TenantSetting.Tenant}', 'left behind', false)", first);
            backend = (int)(await set.ExecuteScalarAsync(Cancellation))!;
        }

        (await BackendState.OfAPlainClientAsync(Connection, Cancellation)).Should().Be(
            BackendState.Clean(backend) with { Tenant = "left behind" },
            "the same backend, handed to the next client with the last one's setting still on it");
    }

    public static TheoryData<string, string> HandedOnInConnectionScope() => new()
    {
        { "Host=db.example.test;Database=postgres;No Reset On Close=true", "No Reset On Close" },
        { "Host=aws-0-eu-west-1.pooler.supabase.com;Port=6543;Database=postgres", "6543" },
        { "Host=aws-0-eu-west-1.pooler.supabase.com:6543;Database=postgres", "6543" },
    };

    [Theory]
    [MemberData(nameof(HandedOnInConnectionScope))]
    public async Task The_transaction_pooler_is_allowed_in_transaction_scope_and_refused_in_connection_scope(string connectionString, string named)
    {
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        await using var perConnection = NeverConnecting(connectionString, RowLevelSecurityScope.Connection);
        var open = () => perConnection.Database.OpenConnectionAsync(Cancellation);
        (await open.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"Row level security sets the caller's role and claims on the connection when a context opens it. *{named}*set PostgresRowLevelSecurityOptions.Scope to Transaction*");

        await using var perTransaction = NeverConnecting(connectionString, RowLevelSecurityScope.Transaction);
        var openPerTransaction = () => perTransaction.Database.OpenConnectionAsync(Cancellation);
        await openPerTransaction.Should().NotThrowAsync("nothing is left on a session in this scope, so there is nothing for a pooler, or a skipped reset, to hand on");
    }

    [Theory]
    [InlineData(RowLevelSecurityScope.Connection, "so one caller's role would be every caller's")]
    [InlineData(RowLevelSecurityScope.Transaction, "row level security has not been proven on it")]
    public async Task Multiplexing_is_refused_in_both_scopes(RowLevelSecurityScope scope, string why)
    {
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = NeverConnecting("Host=db.example.test;Database=postgres;Multiplexing=true", scope);

        var open = () => context.Database.OpenConnectionAsync(Cancellation);

        (await open.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"Row level security sets the caller's role and claims*Multiplexing*{why}*Turn Multiplexing off.");
    }

    [Theory]
    [InlineData(RowLevelSecurityScope.Connection)]
    [InlineData(RowLevelSecurityScope.Transaction)]
    public async Task Over_a_data_source_the_settings_and_refusals_hold_in_both_scopes(RowLevelSecurityScope scope)
    {
        database.Require();
        await database.WriteAsync(Alice, "Alice's");
        await database.WriteAsync(Bob, "Bob's");

        // Reset on close where the settings last a session, which needs it; never reset where they last a transaction.
        var perTransaction = scope == RowLevelSecurityScope.Transaction;
        var connectionString = new NpgsqlConnectionStringBuilder(Connection) { NoResetOnClose = perTransaction }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var context = OnDataSource(dataSource, scope);

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        (await TextsAsync(context)).Should().Equal("Alice's");
        var state = await BackendState.OfAsync(context, Cancellation);
        state.Should().Be(new BackendState(state.Backend, "authenticated", LoginRole, ClaimsOf(Alice), "", "north"));

        context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's too" });
        (await context.SaveChangesAsync(Cancellation)).Should().Be(1);

        _caller.Current = Callers.FromClaims(ClaimsOf(Bob));
        (await TextsAsync(context)).Should().Equal(["Bob's"], "the pool's one connection, and the next caller");

        // Code that takes a connection from the data source itself gets the same backend, and nothing of any caller.
        await using (var plain = await dataSource.OpenConnectionAsync(Cancellation))
        {
            await using var read = new NpgsqlCommand(BackendState.Sql, plain);
            BackendState.Parse((string)(await read.ExecuteScalarAsync(Cancellation))!).Should().Be(BackendState.Clean(state.Backend));
        }

        // The refusals read the data source's connection string, as they read a context's own.
        await using var multiplexing = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString) { Multiplexing = true }.ConnectionString);
        await using var refused = OnDataSource(multiplexing, scope);
        var read2 = () => TextsAsync(refused);
        (await read2.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Turn Multiplexing off.");

        await using var unreset = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString) { NoResetOnClose = true, ApplicationName = _pool + "-unreset" }.ConnectionString);
        await using var onUnreset = OnDataSource(unreset, scope);
        var read3 = () => TextsAsync(onUnreset);
        if (perTransaction)
        {
            (await read3()).Should().Equal("Bob's");
        }
        else
        {
            (await read3.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*No Reset On Close*");
        }
    }

    [Fact]
    public void The_scope_is_said_in_the_options_and_read_once_when_the_interceptor_is_built()
    {
        new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions()).Scope.Should().Be(RowLevelSecurityScope.Connection, "the default, as it always was");

        var options = new PostgresRowLevelSecurityOptions { Scope = RowLevelSecurityScope.Transaction };
        var interceptor = new PostgresRowLevelSecurityInterceptor(_caller, options);
        options.Scope = RowLevelSecurityScope.Connection;
        interceptor.Scope.Should().Be(RowLevelSecurityScope.Transaction, "an option changed afterwards does not reach an interceptor half way");

        using var services = new ServiceCollection().AddPostgresRowLevelSecurity<CallerOfTheTest>(configured => configured.Scope = RowLevelSecurityScope.Transaction).BuildServiceProvider();
        services.GetRequiredService<PostgresRowLevelSecurityInterceptor>().Scope.Should().Be(RowLevelSecurityScope.Transaction);

        using var supabase = new ServiceCollection().AddSupabaseRowLevelSecurity(configured => configured.Scope = RowLevelSecurityScope.Transaction).BuildServiceProvider();
        supabase.GetRequiredService<PostgresRowLevelSecurityInterceptor>().Scope.Should().Be(RowLevelSecurityScope.Transaction, "Supabase's registration takes the same options");

        var undefined = () => new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions { Scope = (RowLevelSecurityScope)7 });
        undefined.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(PostgresRowLevelSecurityOptions.Scope));
    }

    [Fact]
    public void The_setting_that_marks_a_transaction_is_the_toolkits_own()
    {
        var build = () => new PostgresRowLevelSecurityInterceptor(_caller, new PostgresRowLevelSecurityOptions(), [new DeclaredSettings(["ddd.caller_mark"])]);

        build.Should().Throw<ArgumentException>().WithParameterName("settings")
            .WithMessage($"{nameof(DeclaredSettings)} declares the setting 'ddd.caller_mark', which is the toolkit's own*");
    }

    private NotesContext Context()
        => PoolerFixture.Context(Connection, PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Transaction, [_tenant]));

    private NotesContext OnDataSource(NpgsqlDataSource dataSource, RowLevelSecurityScope scope)
        => new(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(dataSource)
            .AddInterceptors(PoolerFixture.Interceptor(_caller, scope, [_tenant]))
            .Options);

    /// <summary>The same table, with a note's text as its concurrency token.</summary>
    private GuardedNotesContext Guarded()
        => new(new DbContextOptionsBuilder<GuardedNotesContext>()
            .UseNpgsql(Connection)
            .AddInterceptors(PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Transaction, [_tenant]))
            .Options);

    /// <summary>A context whose connection is never really opened: the interceptor decides, and then nothing connects.</summary>
    private NotesContext NeverConnecting(string connectionString, RowLevelSecurityScope scope)
        => new(new DbContextOptionsBuilder<NotesContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(PoolerFixture.Interceptor(_caller, scope), new NeverOpens())
            .Options);

    private static async Task<int> BackendAsync(DbContext context) => (await BackendState.OfAsync(context, Cancellation)).Backend;

    /// <summary>What the pool's one connection carries for a client that sets nothing: the backend the caller ran on, as the login role, with nothing set.</summary>
    private async Task ShouldBeCleanAsync(int backend)
        => (await BackendState.OfAPlainClientAsync(Connection, Cancellation)).Should().Be(
            BackendState.Clean(backend),
            "nothing of a caller outlives its transaction, so the next client on the same backend finds the login role and no settings");

    private static Task<List<string>> TextsAsync(NotesContext context)
        => context.Notes.OrderBy(note => note.Text).Select(note => note.Text).ToListAsync(Cancellation);

    /// <summary>One value, from SQL the tests themselves write; nothing here comes from outside.</summary>
    private static async Task<string?> ScalarAsync(DbContext context, string sql)
    {
#pragma warning disable EF1003
        return await context.Database.SqlQueryRaw<string>(sql + " AS \"Value\"").SingleAsync(Cancellation);
#pragma warning restore EF1003
    }

    /// <summary>Refuses every save before it begins, after the interceptors before it had their say, for as long as it is told to.</summary>
    private sealed class RefusesSaves : SaveChangesInterceptor
    {
        public bool Refusing { get; set; } = true;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => Refusing ? throw new InvalidOperationException("Not this save.") : result;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => Refusing ? throw new InvalidOperationException("Not this save.") : ValueTask.FromResult(result);
    }

    /// <summary>Stops a context from connecting, after the interceptors before it had their say.</summary>
    private sealed class NeverOpens : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }
}
