using System.Data.Common;
using System.Transactions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Connections and transactions a context did not open or begin itself: what a connection carries is known to
/// every context it is handed to, a connection nobody set a caller on gets one before its first command, and
/// where that cannot be done safely the command is refused, or logged, rather than run as nobody in particular.
/// </summary>
[Collection(SettingsPerTransaction.Name)]
public sealed class PassedInConnectionTests(PoolerFixture database) : IAsyncLifetime
{
    private const string SetsTheRole = "set_config('role'";
    private const string Calls = "CALL ddd.use_caller";
    private const string AsksForTheMark = "current_setting('ddd.caller_mark'";

    private readonly CallerOfTheTest _caller = new();
    private readonly TenantSetting _tenant = new() { Current = "north" };
    private readonly SentStatements _sent = new();
    private readonly string _pool = "passed-" + Guid.NewGuid().ToString("N")[..12];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (database.Available)
        {
            await database.ClearAsync();
            await database.WriteAsync(Alice, "Alice's");
            await database.WriteAsync(Bob, "Bob's");
        }
    }

    public ValueTask DisposeAsync()
    {
        _sent.Dispose();
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(RowLevelSecurityScope.Connection)]
    [InlineData(RowLevelSecurityScope.Transaction)]
    public async Task A_connection_opened_outside_any_context_gets_the_caller_before_its_first_command(RowLevelSecurityScope scope)
    {
        database.Require();
        await using var dataSource = DataSource(scope);
        await using var connection = await dataSource.OpenConnectionAsync(Cancellation);
        (await RawAsync(connection)).Should().Be(LoginRole, "nothing set a caller on a connection opened by hand");

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Over(connection, Interceptor(scope));

        var state = await BackendState.OfAsync(context, Cancellation);
        state.Should().Be(new BackendState(connection.ProcessID, "authenticated", LoginRole, ClaimsOf(Alice), "", "north"), "the context's first command runs as the caller");
        context.Notes.Select(note => note.Text).ToList().Should().Equal(["Alice's"], "and so does the next one, without async");

        if (scope == RowLevelSecurityScope.Connection)
        {
            _sent.Count(SetsTheRole).Should().Be(1, "the caller is set once, before the first command, as for a caller that changed");
            (await RawAsync(connection)).Should().Be("authenticated", "it was set for the session, so a command sent past Entity Framework runs as the caller too");
        }
        else
        {
            _sent.Count(SetsTheRole).Should().Be(0, "nothing is set for a session in this scope");
            _sent.Count(Calls).Should().Be(2, "every command outside a transaction carries the call, whoever opened the connection");
            (await RawAsync(connection)).Should().Be(LoginRole, "and a command sent past Entity Framework finds nothing on the session");
        }
    }

    [Fact]
    public async Task A_connection_one_context_opened_carries_its_caller_into_another_context()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Connection);

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var first = new NotesContext(new DbContextOptionsBuilder<NotesContext>().UseNpgsql(dataSource).AddInterceptors(Interceptor(RowLevelSecurityScope.Connection)).Options);
        await first.Database.OpenConnectionAsync(Cancellation);
        (await TextsAsync(first)).Should().Equal("Alice's");
        _sent.Count(SetsTheRole).Should().Be(1);

        // A second context on the same connection, with an interceptor of its own: another instance, the same answers.
        await using var second = Over(first.Database.GetDbConnection(), Interceptor(RowLevelSecurityScope.Connection));
        (await TextsAsync(second)).Should().Equal("Alice's");
        _sent.Count(SetsTheRole).Should().Be(1, "what the first context's interceptor set is known to the second's, which sends nothing");

        _caller.Current = Callers.FromClaims(ClaimsOf(Bob));
        (await TextsAsync(second)).Should().Equal(["Bob's"], "a caller that changed is set by whichever context runs the next command");
        _sent.Count(SetsTheRole).Should().Be(2);

        (await TextsAsync(first)).Should().Equal("Bob's");
        _sent.Count(SetsTheRole).Should().Be(2, "and the first context finds it set");
    }

    [Theory]
    [InlineData(RowLevelSecurityScope.Connection)]
    [InlineData(RowLevelSecurityScope.Transaction)]
    public async Task Two_contexts_on_one_connection_and_one_transaction_run_as_the_caller_who_began_it(RowLevelSecurityScope scope)
    {
        database.Require();
        await using var dataSource = DataSource(scope);
        var required = new CallerOptions { RequireExplicitCallers = true };
        PostgresRowLevelSecurityInterceptor Strict() => PoolerFixture.Interceptor(new AmbientCallerAccessor(required), scope, [_tenant], required);

        await using var first = new NotesContext(new DbContextOptionsBuilder<NotesContext>().UseNpgsql(dataSource).AddInterceptors(Strict()).Options);

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Alice))))
        {
            await using var transaction = await first.Database.BeginTransactionAsync(Cancellation);
            (await TextsAsync(first)).Should().Equal("Alice's");
            var sentByTheFirst = _sent.Count(SetsTheRole);
            sentByTheFirst.Should().Be(1);

            // The second context joins the first one's transaction, with an interceptor of its own.
            await using var second = Over(first.Database.GetDbConnection(), Strict());
            await second.Database.UseTransactionAsync(transaction.GetDbTransaction(), Cancellation);
            (await TextsAsync(second)).Should().Equal(["Alice's"], "it runs as the caller the transaction began with");
            _sent.Count(SetsTheRole).Should().Be(sentByTheFirst, "and sets nothing of its own");

            using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
            {
                var asBob = () => TextsAsync(second);
                (await asBob.Should().ThrowAsync<InvalidOperationException>("a transaction runs as one caller, whichever context the command comes from"))
                    .WithMessage($"The caller changed from authenticated {Alice} to authenticated {Bob} while a transaction was open on this connection.*");

                // And a third, joining as somebody else: refused when it joins where the settings are the
                // transaction's own, and at its first command where they are the session's.
                await using var third = Over(first.Database.GetDbConnection(), Strict());
                var join = async () =>
                {
                    await third.Database.UseTransactionAsync(transaction.GetDbTransaction(), Cancellation);
                    return await TextsAsync(third);
                };
                (await join.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"The caller changed from authenticated {Alice} to authenticated {Bob}*");
            }

            (await TextsAsync(first)).Should().Equal(["Alice's"], "the transaction goes on as Alice");
            (await TextsAsync(second)).Should().Equal("Alice's");
            await transaction.CommitAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_raw_connection_inside_a_transaction_is_refused_with_explicit_callers()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Connection);
        var required = new CallerOptions { RequireExplicitCallers = true };
        var interceptor = PoolerFixture.Interceptor(new AmbientCallerAccessor(required), RowLevelSecurityScope.Connection, [_tenant], required);
        const string refusal =
            "This connection was opened outside Entity Framework and carries no caller, and a transaction is open on it, so the caller cannot be set safely. " +
            "Open the connection through a context, begin the transaction after a context opened it, or use CallerConnections.";

        using var alice = Callers.Begin(Callers.FromClaims(ClaimsOf(Alice)));

        // A transaction begun on the connection itself, and handed to the context.
        await using (var connection = await dataSource.OpenConnectionAsync(Cancellation))
        await using (var transaction = await connection.BeginTransactionAsync(Cancellation))
        await using (var context = Over(connection, interceptor))
        {
            await context.Database.UseTransactionAsync(transaction, Cancellation);

            var read = () => TextsAsync(context);
            (await read.Should().ThrowAsync<InvalidOperationException>()).WithMessage(refusal);
            (await RawAsync(connection, transaction)).Should().Be(LoginRole, "nothing was set, and nothing ran");
        }

        // A connection opened by hand inside a transaction scope, which is a transaction as well.
        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await using var connection = await dataSource.OpenConnectionAsync(Cancellation);
            await using var context = Over(connection, interceptor);

            var read = () => TextsAsync(context);
            (await read.Should().ThrowAsync<InvalidOperationException>()).WithMessage(refusal);
        }

        _sent.Count(SetsTheRole).Should().Be(0);

        // The same connection outside any transaction is given the caller, and a transaction begun after that is the caller's.
        await using (var connection = await dataSource.OpenConnectionAsync(Cancellation))
        await using (var context = Over(connection, interceptor))
        {
            (await TextsAsync(context)).Should().Equal("Alice's");
            await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
            (await TextsAsync(context)).Should().Equal("Alice's");
        }
    }

    [Fact]
    public async Task A_raw_connection_inside_a_transaction_is_logged_once_without_explicit_callers()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Connection);
        var logger = new KeptLogger<PostgresRowLevelSecurityInterceptor>();
        var interceptor = PoolerFixture.Interceptor(_caller, RowLevelSecurityScope.Connection, [_tenant], logger: logger);
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        await using var connection = await dataSource.OpenConnectionAsync(Cancellation);
        await using var context = Over(connection, interceptor);

        await using (var transaction = await connection.BeginTransactionAsync(Cancellation))
        {
            await context.Database.UseTransactionAsync(transaction, Cancellation);

            // As before this was looked at: the commands run as the role the connection carries, which may read nothing here.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                (await context.Database.SqlQueryRaw<string>("""SELECT current_user::text AS "Value" """).SingleAsync(Cancellation)).Should().Be(LoginRole);
            }

            await context.Database.UseTransactionAsync(null, Cancellation);
            await transaction.RollbackAsync(Cancellation);
        }

        logger.Entries.Where(entry => entry.Level == LogLevel.Warning).Should().ContainSingle("it is logged once per connection")
            .Which.Message.Should().StartWith("This connection was opened outside Entity Framework and carries no caller, and a transaction is open on it, so the caller cannot be set safely.")
            .And.Contain($"not as authenticated {Alice}");
        _sent.Count(SetsTheRole).Should().Be(0);

        (await TextsAsync(context)).Should().Equal(["Alice's"], "outside the transaction the caller is set, as for any connection passed in");
    }

    [Fact]
    public async Task In_transaction_scope_a_transaction_passed_with_UseTransaction_gets_the_settings()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Transaction);
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        int backend;
        await using (var connection = await dataSource.OpenConnectionAsync(Cancellation))
        await using (var transaction = await connection.BeginTransactionAsync(Cancellation))
        await using (var context = Over(connection, Interceptor(RowLevelSecurityScope.Transaction)))
        {
            backend = connection.ProcessID;
            (await RawAsync(connection, transaction)).Should().Be(LoginRole, "before a context is handed it, the transaction is the login role's");

            await context.Database.UseTransactionAsync(transaction, Cancellation);

            (await BackendState.OfAsync(context, Cancellation)).Should().Be(new BackendState(backend, "authenticated", LoginRole, ClaimsOf(Alice), "", "north"));
            (await TextsAsync(context)).Should().Equal("Alice's");
            _sent.Count(SetsTheRole).Should().Be(1, "the settings are sent once, when the transaction is handed over");
            _sent.Count(Calls).Should().Be(0);
            (await RawAsync(connection, transaction)).Should().Be("authenticated", "from there on the whole transaction is the caller's");

            await transaction.CommitAsync(Cancellation);
        }

        await using var plain = await dataSource.OpenConnectionAsync(Cancellation);
        (await BackendState.OnAsync(plain, Cancellation)).Should().Be(BackendState.Clean(backend), "and nothing of it outlives the transaction");
    }

    [Fact]
    public async Task In_transaction_scope_a_transaction_the_interceptor_did_not_see_is_refused()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Transaction);
        var required = new CallerOptions { RequireExplicitCallers = true };
        PostgresRowLevelSecurityInterceptor Strict() => PoolerFixture.Interceptor(new AmbientCallerAccessor(required), RowLevelSecurityScope.Transaction, [_tenant], required);

        using var alice = Callers.Begin(Callers.FromClaims(ClaimsOf(Alice)));
        await using var first = new NotesContext(new DbContextOptionsBuilder<NotesContext>().UseNpgsql(dataSource).AddInterceptors(Strict()).Options);
        var transaction = await first.Database.BeginTransactionAsync(Cancellation);
        (await TextsAsync(first)).Should().Equal("Alice's");

        // A second context joins the transaction and commits it. The first context still holds it as its
        // current one, though what was set for it went with it, and nothing told the first context: its next
        // command comes with a transaction no caller is set for, and would run on its own, as the login role.
        await using (var second = Over(first.Database.GetDbConnection(), Strict()))
        {
            await second.Database.UseTransactionAsync(transaction.GetDbTransaction(), Cancellation);
            await second.Database.CommitTransactionAsync(Cancellation);
        }

        first.Database.CurrentTransaction.Should().NotBeNull("the first context was told nothing");
        var sent = _sent.Sent.Count;
        var read = () => TextsAsync(first);

        // Npgsql refuses a command given a transaction that has ended before the interceptor is asked; where a
        // provider does not, the interceptor refuses it. Either way nothing is sent. A transaction begun on the
        // connection itself and never handed to a context is another matter: nothing tells a context, or an
        // interceptor, that it exists.
        await read.Should().ThrowAsync<InvalidOperationException>("a command in a transaction no caller is set for must not run as the role the application logged in as");
        _sent.Sent.Should().HaveCount(sent, "nothing reached the server");
    }

    [Fact]
    public async Task In_transaction_scope_a_transaction_never_handed_to_a_context_runs_each_of_its_commands_as_the_caller_there_is()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Transaction);
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        int backend;
        await using (var connection = await dataSource.OpenConnectionAsync(Cancellation))
        {
            backend = connection.ProcessID;

            // Begun on the connection itself, and handed to nobody: neither Entity Framework nor the interceptor
            // can see that it is open, so it cannot be refused. What it must not do is run a command as nobody.
            await using var transaction = await connection.BeginTransactionAsync(Cancellation);
            (await RawAsync(connection, transaction)).Should().Be(LoginRole, "until a context sends something, the transaction is the login role's");

            await using var context = Over(connection, Interceptor(RowLevelSecurityScope.Transaction));
            context.Database.CurrentTransaction.Should().BeNull("nobody told the context");

            (await TextsAsync(context)).Should().Equal(["Alice's"], "the context's command carries its call, as any command outside a transaction it knows of");
            _sent.Count(Calls).Should().Be(1);
            (await RawAsync(connection, transaction)).Should().Be("authenticated", "the call was made inside the open transaction, so the rest of it runs as that caller");

            // The next caller's command carries its own call, which takes the place of the first.
            _caller.Current = Callers.FromClaims(ClaimsOf(Bob));
            (await TextsAsync(context)).Should().Equal("Bob's");
            _sent.Count(Calls).Should().Be(2);
            _sent.Count(SetsTheRole).Should().Be(0);

            await transaction.CommitAsync(Cancellation);
            (await RawAsync(connection)).Should().Be(LoginRole, "what the calls set ended with the transaction");
        }

        await using var plain = await dataSource.OpenConnectionAsync(Cancellation);
        (await BackendState.OnAsync(plain, Cancellation)).Should().Be(BackendState.Clean(backend), "and nothing of either caller is on the session");
    }

    [Fact]
    public async Task In_transaction_scope_a_context_not_handed_the_transaction_on_its_connection_runs_as_the_caller_who_began_it()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Transaction);
        var required = new CallerOptions { RequireExplicitCallers = true };
        PostgresRowLevelSecurityInterceptor Strict() => PoolerFixture.Interceptor(new AmbientCallerAccessor(required), RowLevelSecurityScope.Transaction, [_tenant], required);

        await using var first = new NotesContext(new DbContextOptionsBuilder<NotesContext>().UseNpgsql(dataSource).AddInterceptors(Strict()).Options);

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Alice))))
        {
            await using (var transaction = await first.Database.BeginTransactionAsync(Cancellation))
            {
                first.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's, not committed" });
                await first.SaveChangesAsync(Cancellation);

                // A second context on the same connection that nobody handed the transaction: Entity Framework
                // tells it of none, and its commands run in the first one's all the same.
                await using var second = Over(first.Database.GetDbConnection(), Strict());
                second.Database.CurrentTransaction.Should().BeNull();
                (await TextsAsync(second)).Should().Equal(["Alice's", "Alice's, not committed"], "the server said the transaction the first context began is still open, so the command runs in it, as its caller");
                _sent.Count(Calls).Should().Be(0, "no call is written in front of a command inside a transaction");
                _sent.Count(AsksForTheMark).Should().Be(1, "it cost one question to the server, which handing the transaction over would have saved on every command but the first");

                using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
                {
                    var asBob = () => TextsAsync(second);
                    (await asBob.Should().ThrowAsync<InvalidOperationException>("a transaction runs as one caller, also for a context that was not told of it"))
                        .WithMessage($"The caller changed from authenticated {Alice} to authenticated {Bob} while a transaction was open on this connection.*");
                }

                // The transaction is given up without a word to either context.
            }

            // And once it is over, the second context's commands are on their own again, each with its call.
            await using var afterwards = Over(first.Database.GetDbConnection(), Strict());
            (await TextsAsync(afterwards)).Should().Equal("Alice's");
            _sent.Count(Calls).Should().Be(1);
        }
    }

    [Fact]
    public async Task In_transaction_scope_a_transaction_handed_over_after_a_savepoint_keeps_its_caller_when_the_savepoint_is_undone()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Transaction);
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        await using var connection = await dataSource.OpenConnectionAsync(Cancellation);
        await using var transaction = await connection.BeginTransactionAsync(Cancellation);
        await transaction.SaveAsync("before", Cancellation);

        // The settings are made when the transaction is handed over, which here is after its savepoint.
        await using var context = Over(connection, Interceptor(RowLevelSecurityScope.Transaction));
        await context.Database.UseTransactionAsync(transaction, Cancellation);
        (await TextsAsync(context)).Should().Equal("Alice's");
        _sent.Count(SetsTheRole).Should().Be(1);

        // Going back to the savepoint undoes them with everything else made after it.
        await context.Database.CurrentTransaction!.RollbackToSavepointAsync("before", Cancellation);

        (await RawAsync(connection, transaction)).Should().Be("authenticated", "the interceptor set them again, or the rest of the transaction would run as the login role");
        (await TextsAsync(context)).Should().Equal("Alice's");
        _sent.Count(SetsTheRole).Should().Be(2);

        // A transaction the context began sets them before any savepoint, and sends nothing when one is undone.
        await context.Database.UseTransactionAsync(null, Cancellation);
        await transaction.CommitAsync(Cancellation);
        await using var own = await context.Database.BeginTransactionAsync(Cancellation);
        await own.CreateSavepointAsync("mine", Cancellation);
        await own.RollbackToSavepointAsync("mine", Cancellation);
        (await TextsAsync(context)).Should().Equal("Alice's");
        _sent.Count(SetsTheRole).Should().Be(3, "one for the transaction, none for its savepoint");
    }

    [Fact]
    public void Transactions_handed_over_and_joined_behave_the_same_without_async()
    {
        database.Require();
        using var dataSource = DataSource(RowLevelSecurityScope.Transaction);
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        using var connection = dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        transaction.Save("before");

        using var context = Over(connection, Interceptor(RowLevelSecurityScope.Transaction));
        context.Database.UseTransaction(transaction);
        Texts(context).Should().Equal("Alice's");

        context.Database.CurrentTransaction!.RollbackToSavepoint("before");
        Texts(context).Should().Equal(["Alice's"], "set again after the savepoint was undone");
        _sent.Count(SetsTheRole).Should().Be(2);

        // A context that is not handed the transaction asks the server before its command, and one that is asks once.
        using var notHanded = Over(connection, Interceptor(RowLevelSecurityScope.Transaction));
        Texts(notHanded).Should().Equal("Alice's");

        using var handed = Over(connection, Interceptor(RowLevelSecurityScope.Transaction));
        handed.Database.UseTransaction(transaction);
        Texts(handed).Should().Equal("Alice's");
        Texts(handed).Should().Equal("Alice's");

        _sent.Count(SetsTheRole).Should().Be(2, "neither set anything: the transaction carries its caller");
        _sent.Count(AsksForTheMark).Should().Be(2, "one question for the command of the first, one for the handing over to the second");
        _sent.Count(Calls).Should().Be(0);
        transaction.Commit();

        static List<string> Texts(NotesContext context) => [.. context.Notes.OrderBy(note => note.Text).Select(note => note.Text)];
    }

    [Fact]
    public async Task A_transaction_object_the_provider_hands_out_again_is_not_taken_for_the_one_before()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Transaction);
        var required = new CallerOptions { RequireExplicitCallers = true };
        var interceptor = PoolerFixture.Interceptor(new AmbientCallerAccessor(required), RowLevelSecurityScope.Transaction, [_tenant], required);

        await using var connection = await dataSource.OpenConnectionAsync(Cancellation);
        await using var context = Over(connection, interceptor);
        DbTransaction earlier;

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Alice))))
        {
            earlier = await connection.BeginTransactionAsync(Cancellation);
            await context.Database.UseTransactionAsync(earlier, Cancellation);
            (await TextsAsync(context)).Should().Equal("Alice's");

            // Ended on the connection itself, which no interceptor is told about.
            await context.Database.UseTransactionAsync(null, Cancellation);
            await earlier.CommitAsync(Cancellation);
            await earlier.DisposeAsync();
        }

        using (Callers.Begin(Callers.FromClaims(ClaimsOf(Bob))))
        {
            await using var later = await connection.BeginTransactionAsync(Cancellation);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"The second transaction on the connection is {(ReferenceEquals(earlier, later) ? "the same object as" : "another object than")} the first.");

            // Whether the provider handed out the same object again or not, this is another transaction: it
            // carries nothing, the server says so, and Bob is not refused as a caller who changed inside Alice's.
            await context.Database.UseTransactionAsync(later, Cancellation);
            (await TextsAsync(context)).Should().Equal("Bob's");
            (await RawAsync(connection, later)).Should().Be("authenticated");
            await later.CommitAsync(Cancellation);
        }
    }

    [Fact]
    public async Task A_connection_closed_and_opened_again_behind_the_context_is_given_the_caller_again()
    {
        database.Require();
        await using var dataSource = DataSource(RowLevelSecurityScope.Connection);
        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));

        await using var connection = await dataSource.OpenConnectionAsync(Cancellation);
        await using var context = Over(connection, Interceptor(RowLevelSecurityScope.Connection));
        (await TextsAsync(context)).Should().Equal("Alice's");
        _sent.Count(SetsTheRole).Should().Be(1);

        // The code that owns the connection closes it and opens it again: the pool's reset took the caller off.
        await connection.CloseAsync();
        await connection.OpenAsync(Cancellation);
        (await RawAsync(connection)).Should().Be(LoginRole, "the session is as new");

        (await TextsAsync(context)).Should().Equal(["Alice's"], "what was remembered of the connection went when it closed, so the caller is set again");
        _sent.Count(SetsTheRole).Should().Be(2);
    }

    [Fact]
    public async Task A_connection_passed_in_that_is_never_reset_is_refused_in_connection_scope()
    {
        database.Require();
        await using var dataSource = _sent.DataSource(database.Direct(_pool));
        await using var connection = await dataSource.OpenConnectionAsync(Cancellation);

        _caller.Current = Callers.FromClaims(ClaimsOf(Alice));
        await using var context = Over(connection, Interceptor(RowLevelSecurityScope.Connection));
        var read = () => TextsAsync(context);

        (await read.Should().ThrowAsync<InvalidOperationException>("what is set for its session would still be there for whoever is handed the connection next"))
            .WithMessage("*'No Reset On Close'*");
        _sent.Count(SetsTheRole).Should().Be(0);
    }

    /// <summary>A data source of one connection, whose statements are kept: reset on close where the settings last a session, never reset where they last a transaction.</summary>
    private NpgsqlDataSource DataSource(RowLevelSecurityScope scope)
        => _sent.DataSource(new NpgsqlConnectionStringBuilder(database.Direct(_pool)) { NoResetOnClose = scope == RowLevelSecurityScope.Transaction }.ConnectionString);

    private PostgresRowLevelSecurityInterceptor Interceptor(RowLevelSecurityScope scope) => PoolerFixture.Interceptor(_caller, scope, [_tenant]);

    /// <summary>A context on a connection somebody else opened.</summary>
    private static NotesContext Over(DbConnection connection, PostgresRowLevelSecurityInterceptor interceptor)
        => new(new DbContextOptionsBuilder<NotesContext>().UseNpgsql(connection).AddInterceptors(interceptor).Options);

    private static Task<List<string>> TextsAsync(NotesContext context)
        => context.Notes.OrderBy(note => note.Text).Select(note => note.Text).ToListAsync(Cancellation);

    /// <summary>Who a command sent past Entity Framework runs as.</summary>
    private static async Task<string?> RawAsync(DbConnection connection, DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_user::text";
        command.Transaction = transaction;
        return (string?)await command.ExecuteScalarAsync(Cancellation);
    }
}
