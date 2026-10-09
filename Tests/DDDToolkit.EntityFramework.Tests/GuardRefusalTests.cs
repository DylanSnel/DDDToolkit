using System.Data.Common;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What the toolkit reads as Postgres refusing a caller, from the fields of the error Npgsql hands over, without a
/// database: a guard that raises <c>42501</c> with the toolkit's hint, and a policy that refuses a new row, which
/// Postgres raises from one routine of its own. Neither is read from the words of the message, which a server may
/// translate, and a <c>42501</c> that is neither is the application's own set-up and no refusal, whatever it says.
/// </summary>
/// <remarks>
/// The errors are made as Npgsql makes them, field by field, and what Postgres really sends in each field is
/// pinned against a real Postgres in <see cref="DatabaseRefusalProbeTests"/>.
/// </remarks>
public sealed class GuardRefusalTests : IDisposable
{
    private const string Pallets = "Pallets";

    private static readonly DepotId North = new(1);

    private readonly SqliteDatabase _db = new();

    public GuardRefusalTests() => _db.EnsureCreated(() => new PalletContext(_db.Options<PalletContext>()));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public void A_42501_with_the_toolkits_hint_is_a_guards_refusal_named_by_its_constraint()
    {
        var refused = DatabaseRefusal.From(Postgres(
            "42501",
            "A pallet is moved by a person who may open pallets where it goes.",
            hint: "ddd:access.refused",
            constraint: "pallets_depot_is_held",
            routine: "exec_stmt_raise"))!;

        refused.Kind.Should().Be(DatabaseRefusalKind.GuardRefused);
        refused.Constraint.Should().Be("pallets_depot_is_held", "the guard's name is what the warning names");
        refused.Table.Should().BeNull("the guard named no table");
        refused.Schema.Should().BeNull();
        refused.Columns.Should().BeEmpty();
        DatabaseRefusal.GuardHint.Should().Be("ddd:access.refused");

        // A guard that names its table and schema as well, which RAISE lets it.
        var named = DatabaseRefusal.From(Postgres("42501", "Refused.", hint: DatabaseRefusal.GuardHint, constraint: "pallets_depot_is_held", table: Pallets, schema: "depot"))!;
        (named.Kind, named.Table, named.Schema).Should().Be((DatabaseRefusalKind.GuardRefused, Pallets, "depot"));

        // A guard that gives no name is a guard's refusal all the same.
        DatabaseRefusal.From(Postgres("42501", "Refused.", hint: DatabaseRefusal.GuardHint))!.Kind.Should().Be(DatabaseRefusalKind.GuardRefused);
    }

    [Fact]
    public void A_policys_refusal_is_known_by_the_routine_that_raised_it_in_any_language()
    {
        // As a server whose lc_messages is not English says it: the words are its own, the routine is Postgres's.
        var translated = DatabaseRefusal.From(Postgres(
            "42501",
            "nieuwe rij schendt het beleid voor beveiliging op rijniveau van tabel »Pallets«",
            routine: "ExecWithCheckOptions"))!;
        translated.Kind.Should().Be(DatabaseRefusalKind.PolicyDenied);
        translated.Table.Should().BeNull("the table is read from straight quotes, and this message quotes it otherwise");
        translated.Constraint.Should().BeNull();

        // In English the message quotes the table.
        var english = DatabaseRefusal.From(Postgres("42501", "new row violates row-level security policy for table \"Pallets\"", routine: "ExecWithCheckOptions"))!;
        (english.Kind, english.Table).Should().Be((DatabaseRefusalKind.PolicyDenied, Pallets));
    }

    [Fact]
    public void A_42501_without_the_hint_that_is_no_policys_refusal_is_not_mistaken_for_one()
    {
        // A missing privilege: the application's own set-up.
        DatabaseRefusal.From(Postgres("42501", "permission denied for table Pallets", routine: "aclcheck_error")).Should().BeNull();

        // A read with row_security off by a role the policies hold: set-up as well, though it names row level
        // security in so many words, which is why the words decide nothing.
        DatabaseRefusal.From(Postgres("42501", "query would be affected by row-level security policy for table \"Pallets\"", routine: "check_enable_rls"))
            .Should().BeNull();

        // The same, to a role that owns the table: Postgres gives its own 42501 a hint of its own, in the server's
        // language. A hint marks a guard only when it is exactly the toolkit's, which Postgres never writes.
        DatabaseRefusal.From(Postgres(
                "42501",
                "query would be affected by row-level security policy for table \"Pallets\"",
                hint: "To disable the policy for the table's owner, use ALTER TABLE NO FORCE ROW LEVEL SECURITY.",
                routine: "check_enable_rls"))
            .Should().BeNull();

        // A trigger of somebody's that raises 42501 and leaves out the hint: no guard that asked to be answered as one.
        DatabaseRefusal.From(Postgres("42501", "Not on a Sunday.", constraint: "pallets_not_on_sunday", routine: "exec_stmt_raise")).Should().BeNull();

        // Nor one with a hint of its own, or the toolkit's with something after it.
        DatabaseRefusal.From(Postgres("42501", "Not on a Sunday.", hint: "Ask the depot's manager.", routine: "exec_stmt_raise")).Should().BeNull();
        DatabaseRefusal.From(Postgres("42501", "Not on a Sunday.", hint: DatabaseRefusal.GuardHint + " on Sundays", routine: "exec_stmt_raise")).Should().BeNull();
    }

    [Fact]
    public void The_hint_marks_a_refusal_only_with_42501()
    {
        // What may never be is not a refusal of the caller: a check that fails for whoever writes.
        DatabaseRefusal.From(Postgres("23514", "A depot keeps a pallet.", hint: DatabaseRefusal.GuardHint, constraint: "depot_keeps_a_pallet")).Should().BeNull();

        // A RAISE that names no code is P0001, which no tool reads as "not allowed".
        DatabaseRefusal.From(Postgres("P0001", "Refused.", hint: DatabaseRefusal.GuardHint, constraint: "pallets_guard")).Should().BeNull();
    }

    [Fact]
    public void A_guards_refusal_is_read_through_the_exceptions_a_save_wraps_it_in()
    {
        var guard = Postgres("42501", "Refused.", hint: DatabaseRefusal.GuardHint, constraint: "pallets_depot_is_held");

        DatabaseRefusal.From(new DbUpdateException("An error occurred while saving the entity changes.", guard))!.Constraint.Should().Be("pallets_depot_is_held");
        DatabaseRefusal.From(new InvalidOperationException("Outer", new DbUpdateException("Inner", guard)))!.Kind.Should().Be(DatabaseRefusalKind.GuardRefused);
    }

    [Fact]
    public void RowAccessModel_writes_the_statement_a_guard_refuses_with()
    {
        RowAccessModel.Refusal("projects_unit_is_held", "A project is moved by a seat that may open projects where it goes.").Should().Be(
            "RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'projects_unit_is_held', HINT = 'ddd:access.refused', "
            + "MESSAGE = 'A project is moved by a seat that may open projects where it goes.';");

        // A quote in the name or the message stays inside its literal.
        RowAccessModel.Refusal("o'neill", "It's not yours.").Should().Contain("CONSTRAINT = 'o''neill'").And.Contain("MESSAGE = 'It''s not yours.'");

        // A guard says who it is and what it holds.
        FluentActions.Invoking(() => RowAccessModel.Refusal(" ", "Refused.")).Should().Throw<ArgumentException>().Which.ParamName.Should().Be("guard");
        FluentActions.Invoking(() => RowAccessModel.Refusal("pallets_guard", "")).Should().Throw<ArgumentException>().Which.ParamName.Should().Be("message");
    }

    [Fact]
    public async Task A_save_a_guard_refuses_is_access_refused_and_the_warning_names_the_guard()
    {
        using var logs = new KeptWarnings();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Warning));

        // The database's answer to the save's statement, as Npgsql would hand it over.
        var database = new Answering(Postgres("42501", "A pallet is moved by a person who may open pallets where it goes.", hint: DatabaseRefusal.GuardHint, constraint: "pallets_depot_is_held"));
        await using var context = new PalletContext(_db.Options<PalletContext>(options => options
            .UseLoggerFactory(factory)
            .AddInterceptors(new AggregateVersionInterceptor(), new DatabaseRefusalInterceptor(), database)));
        context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 1, "Fragile", owner: null));

        var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(ToolkitRefusals.Refused);
        refusal.Kind.Should().Be(RefusalKind.NotPermitted);
        refusal.Message.Should().Be("The database refused this change.", "the caller is told it may not, and nothing of the guard");
        refusal.Arguments.Should().BeEmpty();
        refusal.InnerException.Should().BeOfType<DbUpdateException>("the refusal keeps the failure it stands for")
            .Which.InnerException.Should().BeOfType<PostgresException>().Which.ConstraintName.Should().Be("pallets_depot_is_held");

        // The save without await answers the same.
        FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<RefusalException>().Which.Code.Should().Be(ToolkitRefusals.Refused);

        var warned = logs.Kept.Should().HaveCount(2).And.AllSatisfy(warning => warning.Level.Should().Be(LogLevel.Warning)).And.Subject.First();
        warned.Message.Should().Be("The database refused a save the application allowed: the guard pallets_depot_is_held on depot.Pallets. C# and the guards disagree.");
        warned.Exception.Should().BeOfType<DbUpdateException>();
    }

    [Fact]
    public async Task A_save_a_trigger_refuses_without_the_hint_fails_as_the_database_failed_it()
    {
        using var logs = new KeptWarnings();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Warning));
        var database = new Answering(Postgres("42501", "Not on a Sunday.", constraint: "pallets_not_on_sunday", routine: "exec_stmt_raise"));
        await using var context = new PalletContext(_db.Options<PalletContext>(options => options
            .UseLoggerFactory(factory)
            .AddInterceptors(new AggregateVersionInterceptor(), new DatabaseRefusalInterceptor(), database)));
        context.Pallets.Add(new Pallet(PalletId.CreateSequential(), North, 1, "Fragile", owner: null));

        (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        logs.Kept.Should().BeEmpty("nothing was refused that the application could have been told about");
    }

    /// <summary>A Postgres error, field by field, as Npgsql makes one of what the server sent.</summary>
    private static PostgresException Postgres(
        string sqlState,
        string message,
        string? hint = null,
        string? constraint = null,
        string? table = null,
        string? schema = null,
        string? routine = null)
        => new(message, "ERROR", "ERROR", sqlState, hint: hint, constraintName: constraint, tableName: table, schemaName: schema, routine: routine);

    /// <summary>Answers every statement that writes with <paramref name="failure"/>, as a database that refused it.</summary>
    private sealed class Answering(DbException failure) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            => IsAWrite(command) ? throw failure : result;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            => IsAWrite(command) ? throw failure : ValueTask.FromResult(result);

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            => IsAWrite(command) ? throw failure : result;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => IsAWrite(command) ? throw failure : ValueTask.FromResult(result);

        private static bool IsAWrite(DbCommand command)
            => command.CommandText.StartsWith("INSERT", StringComparison.Ordinal) || command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal);
    }

    /// <summary>Keeps what the toolkit's interceptor logs at warning level and above.</summary>
    private sealed class KeptWarnings : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _kept = [];

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Kept => _kept;

        public ILogger CreateLogger(string categoryName) => new Keeper(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Keeper(KeptWarnings logs, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (category == typeof(DatabaseRefusalInterceptor).FullName && IsEnabled(logLevel))
                {
                    logs._kept.Add((logLevel, formatter(state, exception), exception));
                }
            }
        }
    }
}
