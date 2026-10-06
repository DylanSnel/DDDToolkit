using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DDDToolkit.EntityFramework.Postgres;

/// <summary>
/// Makes Postgres apply row level security to a context's queries. Every time the context opens a
/// connection, this sets the role and the token's claims for the caller <see cref="ICallerAccessor"/>
/// names, the way PostgREST does for each request, together with the settings every registered
/// <see cref="IRowLevelSecuritySettings"/> gives, in one statement:
/// <code>
/// SELECT set_config('role', 'authenticated', false), set_config('request.jwt.claims', '{"sub":"…"}', false), …
/// </code>
/// after which the caller functions answer for that user, <c>ddd.caller_id()</c> on a Postgres of your
/// own and <c>auth.uid()</c> on Supabase, and every policy on every table the context touches applies.
/// </summary>
/// <remarks>
/// Register it with <c>services.AddPostgresRowLevelSecurity()</c>: <c>options.UseDDDToolkit(provider)</c> then adds it
/// to every context on Postgres, and <c>options.UsePostgresRowLevelSecurity(provider)</c> to one configured without it.
/// A context on another database that has it all the same, because its provider was configured after
/// <c>UseDDDToolkit</c>, is passed over at every use: nothing is set, and nobody is asked who is calling.
/// <para>
/// <b>In <see cref="RowLevelSecurityScope.Connection"/> scope, the default, the settings last as long as the
/// connection is open, not as long as a transaction.</b> Entity
/// Framework opens a connection for each query outside a transaction and closes it straight after, so
/// they have to be on the connection rather than in a transaction, or a plain query would run without
/// them. That costs one round trip each time a connection is opened. Npgsql clears them with
/// <c>DISCARD ALL</c> before the pooled connection is used again, which is also why this refuses three
/// ways of connecting that would let them reach somebody else: <c>No Reset On Close</c>, which skips that
/// reset; <c>Multiplexing</c>, which shares one connection between callers at once; and Supabase's
/// transaction pooler on port 6543, which hands each transaction whichever server connection is free.
/// Use the session pooler on port 5432, connect directly, or use the other scope.
/// </para>
/// <para>
/// <b>In <see cref="RowLevelSecurityScope.Transaction"/> scope nothing is ever set on a session</b>, so a
/// pooler that hands each transaction any free server connection carries nothing from one client to the
/// next. A command outside a transaction gets <c>RESET ROLE; CALL ddd.use_caller(…);</c> in front of its
/// text, which sets the same role, claims and settings for the transaction that one command is; the provider
/// sends them together, so it costs no round trip. The reset takes off a role some other client left on the
/// server connection, so the call is always made by the role the application logged in as, the only one that
/// may make it. A transaction a context begins, or is handed with <c>UseTransaction</c>, gets the settings
/// once, as its first statement, and so does a <c>TransactionScope</c> the context's connection is enlisted
/// in; from there on the transaction runs as that one caller. A save always runs in a transaction there,
/// because a save's own commands cannot carry the call, Entity Framework counting the rows of each statement:
/// the interceptor sets the context's <c>AutoTransactionBehavior</c> to <c>Always</c> when it saves, and
/// leaves it there. A command that would end the transaction itself, or
/// change the role, is refused: <c>COMMIT</c> or <c>SET ROLE</c> in SQL of your own would leave what follows
/// it to the role the application logged in as. <c>No Reset On Close</c> and the transaction pooler are
/// allowed in this scope; <c>Multiplexing</c> is not.
/// </para>
/// <para>
/// The same statement empties <c>request.jwt.claim.sub</c>, <c>request.jwt.claim.role</c>,
/// <c>request.jwt.claim.email</c> and <c>request.jwt.claim</c>, which Supabase's <c>auth.uid()</c>,
/// <c>auth.role()</c>, <c>auth.email()</c> and <c>auth.jwt()</c> read before the claims, so a value
/// something else left on the server connection cannot decide who a statement runs as.
/// </para>
/// <para>
/// A statement that fails to set the caller closes the connection it was opened on, so the next command
/// opens it again and sets the caller again, rather than running on it as the role the application
/// logged in as.
/// </para>
/// <para>
/// <b>A caller that changes while the connection is open.</b> Before every command, and before a
/// transaction begins, the interceptor works the role, the claims and the settings out again and compares
/// them with what it set. Outside a transaction a change is set first, with the same statement. Inside
/// one it is not, because a transaction runs as one caller and a setting made inside it is undone by its
/// rollback: with <see cref="CallerOptions.RequireExplicitCallers"/> the command is refused with an
/// <see cref="InvalidOperationException"/>, and without it a warning is logged, once per connection, and
/// the transaction goes on as the caller it began with. A connection opened inside a
/// <c>TransactionScope</c> sets the caller inside that transaction; when the transaction is rolled back,
/// and the setting with it, the caller is set again before the connection's next command. A transaction
/// begun with SQL of your own, <c>BEGIN</c> in a command, or begun on the connection itself and never handed
/// to the context, is not seen.
/// </para>
/// <para>
/// <b>A token's role picks the database role only through a list.</b> A signed-in user runs as
/// <see cref="PostgresRowLevelSecurityOptions.UserRole"/> when the token says <c>authenticated</c> or names no
/// role, and as the role <see cref="PostgresRowLevelSecurityOptions.TokenRoles"/> maps the token's role to. A
/// role on no list is refused with a <see cref="RefusalException"/>, <c>access.role-not-allowed</c>, before the
/// context connects, and before the next command where the caller changed on an open connection, so nothing
/// runs for it as any role; see <see cref="PostgresRowLevelSecurityOptions.UnknownTokenRole"/>. The roles are
/// read once, when the interceptor is built.
/// </para>
/// <para>
/// With <see cref="CallerOptions.RequireExplicitCallers"/>, the system is the caller only where something
/// began it, <c>Callers.Begin(Caller.System)</c>: an <see cref="ICallerAccessor"/> that answers
/// <see cref="Caller.System"/> for work that began nothing is refused with <see cref="NoCallerException"/>,
/// whichever accessor the host registered.
/// </para>
/// <para>
/// <b>What a connection carries is remembered for the whole process</b>, not per interceptor: a connection one
/// context opened is known to every other context it is handed to, whichever instance of this class each has,
/// and so is one that <see cref="CallerConnections"/> opened. What was set for one transaction is marked on
/// the server, and a context that uses the connection without having been handed the transaction asks the
/// server for the mark, at the cost of a round trip per command, and then runs as the transaction's caller:
/// hand the transaction over with <c>UseTransaction</c> and it asks once. A connection nobody set a caller on, one opened
/// outside Entity Framework and passed to <c>UseNpgsql(connection)</c> already open, gets the caller before its
/// first command, at the cost of that one round trip. Where a transaction is already open on it the caller
/// cannot be set safely: with <see cref="CallerOptions.RequireExplicitCallers"/> the command is refused, and
/// without it a warning is logged, once per connection, and the command runs as whatever the connection
/// carries, which is the role the application logged in as.
/// </para>
/// <para>
/// <b>A statement timeout per kind of caller</b>, <see cref="PostgresRowLevelSecurityOptions.StatementTimeouts"/>,
/// travels in the same statement, and a kind without one gets the login role's own back.
/// </para>
/// <para>
/// <b>A context from a pool</b> (<c>AddPooledDbContextFactory</c>, <c>AddDbContextPool</c>) keeps its
/// connection object from one rental to the next, closed in between: Entity Framework closes a connection
/// that was left open, and rolls back a transaction that was left open, when the context goes back. Nothing
/// here is kept per context, and what is remembered per connection is forgotten when it closes, set again on
/// every open and compared with the current caller before every command, so the next renter's first command
/// runs as the next renter. One instance of this interceptor serves every context of the pool, and every
/// caller at once.
/// </para>
/// </remarks>
public sealed class PostgresRowLevelSecurityInterceptor : DbConnectionInterceptor, IDbCommandInterceptor, IDbTransactionInterceptor, ISaveChangesInterceptor
{
    /// <summary>
    /// The name of the part row level security brings to a context's options (<c>ContextPart</c>):
    /// <c>AddPostgresRowLevelSecurity</c> and <c>AddSupabaseRowLevelSecurity</c> register it, and <c>UseDDDToolkit</c>
    /// adds this interceptor to every context that may be on Postgres. The information line of <c>UseDDDToolkit</c>
    /// names it.
    /// </summary>
    public const string PartName = "postgres.row-level-security";

    /// <summary>
    /// Where the part goes among the parts of a context's options: after the toolkit's own interceptors, which come
    /// before every part, and before Tenancy's save check, which comes last.
    /// </summary>
    public const int PartPosition = 100;

    /// <summary>
    /// The setting a transaction's own statement marks the transaction with. A context handed a transaction
    /// asks the server for it, rather than trust what was remembered about a transaction object: a provider may
    /// hand the same object out again for the next transaction, which carries nothing.
    /// </summary>
    internal const string MarkSetting = "ddd.caller_mark";

    private const string RoleParameter = "__ddd_role";

    private const string ClaimsParameter = "__ddd_claims";

    private const string NamesParameter = "__ddd_names";

    private const string ValuesParameter = "__ddd_values";

    /// <summary>
    /// What a command outside a transaction starts with where the settings last one transaction: the call that
    /// sets them for the transaction that command is. Named parameters, as Entity Framework's own are, so the
    /// provider can tell them from the command's.
    /// </summary>
    /// <remarks>
    /// The reset comes first because the call is the login role's alone to make. A server connection a pooler
    /// hands over may still carry a role some other client set for its session, and the call would then be made
    /// as that role, and refused. Nothing of a caller lives on a session in this scope, so taking a role off it
    /// loses nothing.
    /// </remarks>
    private const string Call = "RESET ROLE;\nCALL ddd.use_caller(@" + RoleParameter + ", @" + ClaimsParameter + ", @" + NamesParameter + ", @" + ValuesParameter + ");\n";

    private const string TimeoutSetting = "statement_timeout";

    private const string ReadMark = "SELECT pg_catalog.current_setting('" + MarkSetting + "', true)";

    private static readonly ConcurrentDictionary<(RowLevelSecurityScope Scope, string ConnectionString), string?> Refusals = new();

    /// <summary>A setting a module may own: <c>prefix.name</c>, lower case, as Postgres folds an unquoted one.</summary>
    private static readonly Regex SettingName = new(@"^[a-z_][a-z0-9_]*\.[a-z_][a-z0-9_]*\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// What each open connection carries, for the whole process: a connection one context opened is known to
    /// another it is handed to, whichever interceptor each has. An entry is emptied when its connection changes
    /// state, so a connection closed and opened again behind Entity Framework's back carries nothing.
    /// </summary>
    private static readonly ConditionalWeakTable<DbConnection, Carried> Connections = new();

    private readonly ICallerAccessor _callers;
    private readonly PostgresRowLevelSecurityOptions _options;
    private readonly RowLevelSecurityScope _scope;
    private readonly bool _requireExplicitCallers;
    private readonly ILogger _logger;
    private readonly string _anonymousClaims;
    private readonly string _systemClaims;
    private readonly (IRowLevelSecuritySettings Provider, HashSet<string> Names)[] _providers;
    private readonly string[] _names;
    private readonly Dictionary<string, int> _positions;

    /// <summary>The timeout of each kind of caller that has one, in milliseconds as Postgres reads it; empty where the host configured none.</summary>
    private readonly Dictionary<CallerKind, string> _timeouts;

    /// <summary>The statements that set a caller, by how many settings they carry, how long they last and whether a timeout follows.</summary>
    private readonly ConcurrentDictionary<(int Settings, bool Local, bool Timed), string> _statements = new();

    /// <summary>
    /// The roles this interceptor gives each kind of caller and how long the settings last: what
    /// <see cref="PostgresRowAccessChecks.EnsureLoginRoleMaySwitchToCallersAsync"/> holds the database to. Public so a
    /// start-up check of another package, such as Supabase's check that these are the roles the access files were
    /// written for, asks about the roles a context really runs with, found among its interceptors, and a host that
    /// runs such a check by hand hands over the context alone. Read them; a change made here after the first
    /// connection is not one the interceptor promises to follow.
    /// </summary>
    public PostgresRowLevelSecurityOptions Options => _options;

    /// <summary>An interceptor that asks <paramref name="callers"/> who is calling, and gives each kind of caller the role <paramref name="options"/> names.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="callers"/> or <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">A role in <paramref name="options"/> is empty.</exception>
    public PostgresRowLevelSecurityInterceptor(ICallerAccessor callers, PostgresRowLevelSecurityOptions options)
        : this(callers, options, [])
    {
    }

    /// <summary>
    /// An interceptor that asks <paramref name="callers"/> who is calling, gives each kind of caller the role
    /// <paramref name="options"/> names, and sets the settings of <paramref name="settings"/> in the same
    /// statement.
    /// </summary>
    /// <param name="callers">Says who is calling.</param>
    /// <param name="options">The roles, how long the settings last, and the statement timeouts.</param>
    /// <param name="settings">The modules' settings; every name each one declares is checked here, once.</param>
    /// <param name="callerOptions">Whether a caller that changes inside a transaction is refused rather than logged.</param>
    /// <param name="logger">Where that warning goes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="callers"/>, <paramref name="options"/> or <paramref name="settings"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// A role in <paramref name="options"/> is empty, or a setting name is not <c>prefix.name</c> in lower
    /// case, is one of PostgREST's <c>request.*</c> or the toolkit's own, or is declared twice.
    /// </exception>
    public PostgresRowLevelSecurityInterceptor(
        ICallerAccessor callers,
        PostgresRowLevelSecurityOptions options,
        IEnumerable<IRowLevelSecuritySettings> settings,
        CallerOptions? callerOptions = null,
        ILogger<PostgresRowLevelSecurityInterceptor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(callers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);

        // The options as they are now, checked: a role changed on the host's options afterwards would otherwise
        // reach a connection without the claims that go with it, and without the check.
        _options = options.Snapshot();
        _scope = _options.Scope;
        _callers = callers;
        _requireExplicitCallers = callerOptions?.RequireExplicitCallers == true;
        _logger = logger ?? (ILogger)NullLogger.Instance;

        // PostgREST gives a request without a user the claims {"role":"anon"}, so the role and claims
        // functions answer the same here. Background work on the login role has no claims at all.
        _anonymousClaims = RoleClaims(_options.AnonymousRole);
        _systemClaims = _options.SystemRole is null ? string.Empty : RoleClaims(_options.SystemRole);

        // Whole milliseconds, which is what Postgres counts a timeout in.
        _timeouts = _options.StatementTimeouts.ToDictionary(
            timeout => timeout.Key,
            timeout => ((long)Math.Ceiling(timeout.Value.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture));

        (_providers, _names, _positions) = Declared(settings);
    }

    /// <summary>
    /// Whether this interceptor was built requiring explicit callers: it refuses a caller that changes inside a
    /// transaction, and a system answer nothing began. It reads <see cref="CallerOptions"/> once, when it is
    /// built, so a start-up check that needs explicit callers asks the interceptor the contexts use, not only
    /// the options: a later registration of the options, or an interceptor built by hand without them, would
    /// leave the two apart.
    /// </summary>
    public bool RequireExplicitCallers => _requireExplicitCallers;

    /// <summary>
    /// How long what this interceptor sets lasts, as it read it when it was built: what a start-up check, or
    /// code that sends SQL of its own, asks instead of the options, for the same reason as
    /// <see cref="RequireExplicitCallers"/>.
    /// </summary>
    public RowLevelSecurityScope Scope => _scope;

    /// <inheritdoc />
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (!NotOnPostgres(eventData?.Context))
        {
            EnsureConnects(connection);
        }

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (!NotOnPostgres(eventData?.Context))
        {
            EnsureConnects(connection);
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    /// <remarks>
    /// When the statement fails, the connection is closed before the failure goes on. Entity Framework counts
    /// a connection whose opening failed as never opened, so it would neither close it nor open it again: the
    /// next command would run on it with nothing set, as the role the application logged in as. Where the
    /// settings last one transaction, nothing is set here.
    /// </remarks>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (_scope == RowLevelSecurityScope.Transaction || NotOnPostgres(eventData?.Context))
        {
            return;
        }

        try
        {
            Send(connection, new Pending(Desired(applied: null), Local: false, Keep: true));
        }
        catch
        {
            connection.Close();
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>As <see cref="ConnectionOpened"/>: a statement that fails, or is cancelled, closes the connection.</remarks>
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (_scope == RowLevelSecurityScope.Transaction || NotOnPostgres(eventData?.Context))
        {
            return;
        }

        try
        {
            await SendAsync(connection, new Pending(Desired(applied: null), Local: false, Keep: true), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
        => Forget(connection);

    /// <inheritdoc />
    public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        Forget(connection);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    InterceptionResult<DbDataReader> IDbCommandInterceptor.ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        EnsureCurrent(command, eventData);
        return result;
    }

    /// <inheritdoc />
    async ValueTask<InterceptionResult<DbDataReader>> IDbCommandInterceptor.ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken)
    {
        await EnsureCurrentAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    InterceptionResult<object> IDbCommandInterceptor.ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        EnsureCurrent(command, eventData);
        return result;
    }

    /// <inheritdoc />
    async ValueTask<InterceptionResult<object>> IDbCommandInterceptor.ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken)
    {
        await EnsureCurrentAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    InterceptionResult<int> IDbCommandInterceptor.NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        EnsureCurrent(command, eventData);
        return result;
    }

    /// <inheritdoc />
    async ValueTask<InterceptionResult<int>> IDbCommandInterceptor.NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken)
    {
        await EnsureCurrentAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A caller begun after the connection opened and before the transaction is set here, so the advice to
    /// begin the caller before the transaction holds for a connection opened earlier as well. Where the
    /// settings last one transaction, the caller is only worked out here, so one that cannot run fails before
    /// the transaction begins rather than after.
    /// </remarks>
    InterceptionResult<DbTransaction> IDbTransactionInterceptor.TransactionStarting(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
    {
        if (!NotOnPostgres(eventData?.Context) && BeforeTransaction(connection, eventData?.Context) is { } pending)
        {
            Send(connection, pending);
        }

        return result;
    }

    /// <inheritdoc />
    async ValueTask<InterceptionResult<DbTransaction>> IDbTransactionInterceptor.TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken)
    {
        if (!NotOnPostgres(eventData?.Context) && BeforeTransaction(connection, eventData?.Context) is { } pending)
        {
            await SendAsync(connection, pending, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Where the settings last one transaction, they are set here, as the transaction's first statement, at its
    /// top level and before any savepoint. A statement that fails ends the transaction before the failure goes
    /// on: Entity Framework would otherwise leave it open, with nothing set, on a connection it goes on using.
    /// </remarks>
    DbTransaction IDbTransactionInterceptor.TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        if (_scope == RowLevelSecurityScope.Transaction && !NotOnPostgres(eventData?.Context))
        {
            try
            {
                Send(connection, new Pending(Desired(applied: null).In(result), Local: true, Keep: true));
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        return result;
    }

    /// <inheritdoc />
    async ValueTask<DbTransaction> IDbTransactionInterceptor.TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken)
    {
        if (_scope == RowLevelSecurityScope.Transaction && !NotOnPostgres(eventData?.Context))
        {
            try
            {
                await SendAsync(connection, new Pending(Desired(applied: null).In(result), Local: true, Keep: true), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await result.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A transaction handed to a context with <c>UseTransaction</c>. Where it is remembered as carrying a
    /// caller, the server is asked whether it still does: it does when another context began it, or
    /// <see cref="CallerConnections"/> did, and then it goes on as that caller, a different one being refused
    /// or logged as any change inside a transaction is. Otherwise, where the settings last one transaction, they
    /// are set here.
    /// </remarks>
    DbTransaction IDbTransactionInterceptor.TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result)
    {
        if (NotOnPostgres(eventData?.Context))
        {
            return result;
        }

        if (Remembered(connection, result) is { } remembered)
        {
            using var read = MarkCommand(connection, result);
            if (StillThere(connection, remembered, read.ExecuteScalar()) is { } joined)
            {
                GoOnAs(joined);
                return result;
            }
        }

        if (_scope == RowLevelSecurityScope.Transaction)
        {
            Send(connection, new Pending(Desired(applied: null).In(result, afterItBegan: true), Local: true, Keep: true));
        }

        return result;
    }

    /// <inheritdoc />
    async ValueTask<DbTransaction> IDbTransactionInterceptor.TransactionUsedAsync(DbConnection connection, TransactionEventData eventData, DbTransaction result, CancellationToken cancellationToken)
    {
        if (NotOnPostgres(eventData?.Context))
        {
            return result;
        }

        if (Remembered(connection, result) is { } remembered)
        {
            var read = MarkCommand(connection, result);
            await using (read.ConfigureAwait(false))
            {
                if (StillThere(connection, remembered, await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) is { } joined)
                {
                    GoOnAs(joined);
                    return result;
                }
            }
        }

        if (_scope == RowLevelSecurityScope.Transaction)
        {
            await SendAsync(connection, new Pending(Desired(applied: null).In(result, afterItBegan: true), Local: true, Keep: true), cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    void IDbTransactionInterceptor.TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => Forget(transaction, eventData.Context);

    /// <inheritdoc />
    Task IDbTransactionInterceptor.TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        Forget(transaction, eventData.Context);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    void IDbTransactionInterceptor.TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => Forget(transaction, eventData.Context);

    /// <inheritdoc />
    Task IDbTransactionInterceptor.TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        Forget(transaction, eventData.Context);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>A transaction whose commit or rollback failed is in no state anything can be said about, so nothing is remembered of it.</remarks>
    void IDbTransactionInterceptor.TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
        => Forget(transaction, eventData.Context);

    /// <inheritdoc />
    Task IDbTransactionInterceptor.TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken)
    {
        Forget(transaction, eventData.Context);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Going back to a savepoint undoes a setting made after it. The settings of a transaction a context began
    /// are made at its top level, before any savepoint, and nothing undoes them. Those of a transaction a
    /// context was handed are made when it is handed over, which may be after a savepoint: for such a
    /// transaction they are set again here, unchanged where they were never undone.
    /// </remarks>
    void IDbTransactionInterceptor.RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData)
    {
        if (Connection(transaction, eventData.Context) is { } connection && Remembered(connection, transaction) is { SetAfterItBegan: true } remembered)
        {
            Send(connection, new Pending(remembered, Local: true, Keep: false));
        }
    }

    /// <inheritdoc />
    async Task IDbTransactionInterceptor.RolledBackToSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken)
    {
        if (Connection(transaction, eventData.Context) is { } connection && Remembered(connection, transaction) is { SetAfterItBegan: true } remembered)
        {
            await SendAsync(connection, new Pending(remembered, Local: true, Keep: false), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Where the settings last one transaction, a save runs in one: the transaction is where the settings are
    /// set, and the commands of a save cannot carry them in front, since Entity Framework counts the rows of
    /// every statement it sent. So the context's <c>AutoTransactionBehavior</c> is set to <c>Always</c> here,
    /// and stays that. It is not put back when the save ends, because not every save tells an interceptor
    /// that it ended: one that another interceptor refuses before it begins, as the check of the invariants
    /// does, tells nobody. Nothing but a save reads the behavior, and every save of this context passes here.
    /// </remarks>
    InterceptionResult<int> ISaveChangesInterceptor.SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        SaveInATransaction(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    ValueTask<InterceptionResult<int>> ISaveChangesInterceptor.SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken)
    {
        SaveInATransaction(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Why settings made on a connection with <paramref name="connectionString"/> could reach another
    /// caller in <paramref name="scope"/>, or <see langword="null"/> when they cannot. Worked out once per
    /// scope and connection string.
    /// </summary>
    internal static string? RefusalFor(RowLevelSecurityScope scope, string connectionString)
    {
        DbConnectionStringBuilder builder;
        try
        {
            builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException)
        {
            // Not a connection string this can read; the provider will say what is wrong with it.
            return null;
        }

        if (scope == RowLevelSecurityScope.Transaction)
        {
            // Nothing lives on a session, so neither a skipped reset nor a transaction pooler can hand anything on.
            return IsOn(builder, "Multiplexing")
                ? "Row level security sets the caller's role and claims in front of each command, and at the start of each transaction. " +
                  "Multiplexing sends many callers' commands down one connection at the same time, and row level security has not been proven on it. Turn Multiplexing off."
                : null;
        }

        const string Preamble = "Row level security sets the caller's role and claims on the connection when a context opens it. ";
        const string OtherScope = $"set {nameof(PostgresRowLevelSecurityOptions)}.{nameof(PostgresRowLevelSecurityOptions.Scope)} to {nameof(RowLevelSecurityScope.Transaction)}";

        if (IsOn(builder, "No Reset On Close") || IsOn(builder, "NoResetOnClose"))
        {
            return Preamble + $"'No Reset On Close' hands the connection back to the pool with them still set, to whoever opens it next. Remove it from the connection string, or {OtherScope}, which leaves nothing on a connection.";
        }

        if (IsOn(builder, "Multiplexing"))
        {
            return Preamble + "Multiplexing sends many callers' commands down one connection at the same time, so one caller's role would be every caller's. Turn Multiplexing off.";
        }

        if (IsTransactionPooler(builder))
        {
            return Preamble + $"Supabase's transaction pooler, on port 6543, gives every transaction whichever server connection is free, so a query may run without them, or with somebody else's. Connect through the session pooler, on port 5432, or directly, or {OtherScope}.";
        }

        return null;
    }

    /// <summary>
    /// The claims a user made without a token gets: their id as <c>sub</c> and their role, which is all
    /// the caller functions read. A user of a token gets the token's claims as they were signed.
    /// </summary>
    internal static string ClaimsOf(Caller user)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (user.UserId is { } id)
            {
                writer.WriteString("sub", id.ToString("D", CultureInfo.InvariantCulture));
            }

            if (user.Role is { } role)
            {
                writer.WriteString("role", role);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The claims of a scoped system caller: its role and its scope, <c>{"role":"ddd_system_in","scope":"projects"}</c>,
    /// so the role function answers the role, and a policy may ask whose work it is.
    /// </summary>
    internal static string ScopedClaims(string role, string scope)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("role", role);
            writer.WriteString("scope", scope);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Refuses a connection whose settings could reach somebody else, and a caller that cannot run, before
    /// anything connects: what <see cref="CallerConnections"/> asks before it opens a connection, as a context
    /// does.
    /// </summary>
    /// <exception cref="InvalidOperationException">The connection string would hand the settings on, or the caller has no role to run as.</exception>
    /// <exception cref="NoCallerException">Nobody is calling, in a host that requires explicit callers.</exception>
    /// <exception cref="RefusalException">The caller's token carries a role that is on no list.</exception>
    internal void EnsureConnects(DbConnection connection)
    {
        EnsureSettingsStayWithTheCaller(connection);

        // Worked out before connecting, so a caller that cannot run fails before anything reaches the server.
        _ = Desired(applied: null);
    }

    /// <summary>
    /// Sets the current caller on <paramref name="connection"/>, which is open: for its session, or, with
    /// <paramref name="transaction"/>, for that transaction alone. What it set is remembered, so a context
    /// handed the connection, or the transaction, finds the caller there.
    /// </summary>
    internal ValueTask ApplyAsync(DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        var desired = Desired(applied: null);
        return SendAsync(
            connection,
            transaction is null ? new Pending(desired, Local: false, Keep: true) : new Pending(desired.In(transaction), Local: true, Keep: true),
            cancellationToken);
    }

    private void EnsureSettingsStayWithTheCaller(DbConnection connection)
    {
        var scope = _scope;
        if (Refusals.GetOrAdd((scope, connection.ConnectionString ?? string.Empty), static key => RefusalFor(key.Scope, key.ConnectionString)) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }
    }

    /// <summary>The providers with the names each declares, every name, and where each one's value goes.</summary>
    private static ((IRowLevelSecuritySettings, HashSet<string>)[] Providers, string[] Names, Dictionary<string, int> Positions) Declared(IEnumerable<IRowLevelSecuritySettings> settings)
    {
        var providers = new List<(IRowLevelSecuritySettings, HashSet<string>)>();
        var names = new List<string>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var provider in settings)
        {
            ArgumentNullException.ThrowIfNull(provider, nameof(settings));
            var owner = provider.GetType().Name;
            var declared = new HashSet<string>(StringComparer.Ordinal);

            foreach (var name in provider.Names ?? throw new ArgumentException($"{owner} declares no Names: an empty list, not null, is a provider without settings.", nameof(settings)))
            {
                if (name is null || !SettingName.IsMatch(name))
                {
                    throw new ArgumentException(
                        $"{owner} declares the setting '{name}', which row level security cannot set. A setting is 'prefix.name', in lower case letters, digits and '_', such as 'teams.current'.",
                        nameof(settings));
                }

                if (name.StartsWith("request.", StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"{owner} declares the setting '{name}', but request.* is PostgREST's, where the caller's claims are. Give the setting a prefix of the module's own.",
                        nameof(settings));
                }

                if (string.Equals(name, MarkSetting, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"{owner} declares the setting '{name}', which is the toolkit's own: it marks a transaction the caller was set for. Give the setting a prefix of the module's own.",
                        nameof(settings));
                }

                if (!owners.TryAdd(name, owner))
                {
                    throw new ArgumentException(
                        $"The setting '{name}' is declared by {owners[name]} and by {owner}. A setting has one owner, or the two would overwrite each other on every connection.",
                        nameof(settings));
                }

                declared.Add(name);
                positions[name] = names.Count;
                names.Add(name);
            }

            providers.Add((provider, declared));
        }

        return ([.. providers], [.. names], positions);
    }

    /// <summary>
    /// The statement that sets a caller: the role, the claims, and the four settings Supabase's caller functions
    /// read before the claims, emptied, then one <c>set_config</c> pair of parameters per module setting.
    /// Positional parameters, which Npgsql binds with or without its SQL rewriting.
    /// </summary>
    /// <param name="settings">How many module settings follow the role and the claims.</param>
    /// <param name="local">Whether the settings last the transaction, and the statement marks it, or the session.</param>
    /// <param name="timed">Whether a statement timeout follows.</param>
    /// <remarks>
    /// For a session the timeout is the caller's kind's, or the login role's own where the kind has none: its
    /// reset value, read in the same statement, so a timeout set for one caller never outlives it. For a
    /// transaction a timeout is only ever set, since it ends with the transaction.
    /// </remarks>
    private static string StatementFor(int settings, bool local, bool timed)
    {
        var scope = local ? "true" : "false";
        var statement = new StringBuilder()
            .Append("SELECT set_config('role', $1, ").Append(scope).Append("), set_config('request.jwt.claims', $2, ").Append(scope).Append("), ")
            .Append("set_config('request.jwt.claim.sub', '', ").Append(scope).Append("), set_config('request.jwt.claim.role', '', ").Append(scope).Append("), ")
            .Append("set_config('request.jwt.claim.email', '', ").Append(scope).Append("), set_config('request.jwt.claim', '', ").Append(scope).Append(')');

        var parameter = 3;
        for (var i = 0; i < settings; i++, parameter += 2)
        {
            statement.Append(CultureInfo.InvariantCulture, $", set_config(${parameter}, ${parameter + 1}, {scope})");
        }

        if (local)
        {
            statement.Append(CultureInfo.InvariantCulture, $", set_config('{MarkSetting}', ${parameter++}, true)");
        }

        if (timed)
        {
            statement.Append(local
                ? string.Create(CultureInfo.InvariantCulture, $", set_config('{TimeoutSetting}', ${parameter}, true)")
                : string.Create(CultureInfo.InvariantCulture, $", set_config('{TimeoutSetting}', coalesce(nullif(${parameter}, ''), (SELECT reset_val FROM pg_catalog.pg_settings WHERE name = '{TimeoutSetting}')), false)"));
        }

        return statement.ToString();
    }

    /// <summary>
    /// What the connection should carry now: the current caller's role and claims, every setting's value, and
    /// the statement timeout of the caller's kind. The role and the claims of the caller
    /// <paramref name="applied"/> was made for are taken as they were, since they follow from the caller alone,
    /// where this interceptor worked them out.
    /// </summary>
    private Applied Desired(Applied? applied)
    {
        var caller = _callers.Current
            ?? throw new InvalidOperationException($"{_callers.GetType().Name} said nobody is calling. An {nameof(ICallerAccessor)} answers {nameof(Caller)}.{nameof(Caller.System)} for background work, or throws {nameof(NoCallerException)}, not null.");

        // Where every flow of work has to say who it runs as, the system is only ever begun on purpose: an
        // accessor that falls back to it by itself would give work nobody said anything about the login role.
        if (_requireExplicitCallers && caller.IsSystem && Callers.Ambient?.IsSystem != true)
        {
            throw new NoCallerException(
                $"{_callers.GetType().Name} answered the system, but nothing began Caller.System, and this host requires every flow of work to say who it runs as (RequireExplicitCallers). " +
                "Begin Callers.Begin(Caller.System) around the application's own work, or a scoped caller a module gives you.");
        }

        string role;
        string claims;
        bool asAnonymous;
        if (applied is not null && !applied.IsUndone && ReferenceEquals(applied.Owner, this) && ReferenceEquals(applied.Caller, caller))
        {
            role = applied.Role;
            claims = applied.Claims;
            asAnonymous = applied.AsAnonymous;
        }
        else
        {
            role = _options.RoleOf(caller, out asAnonymous);
            claims = caller.Kind switch
            {
                // A user whose token's role is on no list, where the host has such a caller run as an anonymous
                // one: the token's claims stay behind, so the database sees no user.
                CallerKind.User when asAnonymous => _anonymousClaims,
                CallerKind.User => caller.Claims ?? ClaimsOf(caller),
                CallerKind.Anonymous => _anonymousClaims,
                CallerKind.System => _systemClaims,
                CallerKind.SystemIn => ScopedClaims(role, caller.Scope!),
                _ => throw new InvalidOperationException($"No claims are defined for a caller of kind {caller.Kind}."),
            };
        }

        // A user who runs as an anonymous caller is one to the modules' settings and to the timeouts too: a
        // setting made from the user's id or claims would show the database the user its role and claims no
        // longer name.
        var kind = asAnonymous ? CallerKind.Anonymous : caller.Kind;
        var timeout = _timeouts.Count == 0 ? null : _timeouts.GetValueOrDefault(kind, string.Empty);

        return new Applied(this, caller, role, claims, _names, ValuesFor(asAnonymous ? Caller.Anonymous : caller), timeout, asAnonymous);
    }

    /// <summary>Every setting's value for <paramref name="caller"/>, in the order of the statement's parameters; <c>''</c> where a provider gave none.</summary>
    private string[] ValuesFor(Caller caller)
    {
        if (_names.Length == 0)
        {
            return [];
        }

        var values = new string[_names.Length];
        Array.Fill(values, string.Empty);

        foreach (var (provider, declared) in _providers)
        {
            foreach (var (name, value) in provider.For(caller) ?? [])
            {
                if (name is null || !declared.Contains(name))
                {
                    throw new InvalidOperationException(
                        $"{provider.GetType().Name} gave a value for '{name}', which is not one of its Names. A provider sets only the settings it declares, so each is set, or emptied, on every connection.");
                }

                values[_positions[name]] = value ?? string.Empty;
            }
        }

        return values;
    }

    /// <summary>What <paramref name="connection"/> is remembered to carry, or <see langword="null"/> when nothing is.</summary>
    private static Applied? CarriedBy(DbConnection connection)
        => Connections.TryGetValue(connection, out var carried) ? carried.Applied : null;

    /// <summary>
    /// What <paramref name="connection"/> is remembered to carry for <paramref name="transaction"/> alone, or
    /// <see langword="null"/>: what was set for another transaction, or for the session, is not it.
    /// </summary>
    private static Applied? Remembered(DbConnection connection, DbTransaction transaction)
        => CarriedBy(connection) is { Transaction: { } carried } applied && ReferenceEquals(carried, transaction) ? applied : null;

    /// <summary>The entry of <paramref name="connection"/>, made the first time, when it starts to follow the connection's state.</summary>
    private static Carried EntryOf(DbConnection connection)
    {
        if (Connections.TryGetValue(connection, out var carried))
        {
            return carried;
        }

        var created = new Carried();
        if (Connections.TryAdd(connection, created))
        {
            // A connection that closes, breaks or opens carries nothing of what was set before, whoever closed
            // or opened it: a context, or code that holds the connection itself.
            connection.StateChange += created.Forget;
            return created;
        }

        return Connections.TryGetValue(connection, out carried) ? carried : created;
    }

    private static void Forget(DbConnection connection)
    {
        if (Connections.TryGetValue(connection, out var carried))
        {
            carried.Applied = null;
        }
    }

    /// <summary>Forgets what was set for <paramref name="transaction"/>, which has ended.</summary>
    private static void Forget(DbTransaction transaction, DbContext? context)
    {
        if (Connection(transaction, context) is { } connection && Remembered(connection, transaction) is not null)
        {
            Forget(connection);
        }
    }

    /// <summary>The connection of <paramref name="transaction"/>, which a provider may no longer say once the transaction has ended; then the context's.</summary>
    private static DbConnection? Connection(DbTransaction transaction, DbContext? context)
    {
        try
        {
            if (transaction.Connection is { } connection)
            {
                return connection;
            }
        }
        catch (InvalidOperationException)
        {
            // Ended or disposed, as some providers say it.
        }

        return context?.Database.GetDbConnection();
    }

    /// <summary>
    /// Remembers what <paramref name="connection"/> carries. Set for the session inside an ambient transaction,
    /// it lasts only if that transaction commits: a rollback undoes a setting made inside it, so then the
    /// connection is marked as carrying nothing known, and its next command sets the caller again. Set for an
    /// ambient transaction alone, it is forgotten when that transaction ends.
    /// </summary>
    private static void Record(DbConnection connection, Applied desired)
    {
        var entry = EntryOf(connection);
        entry.Applied = desired;

        if (desired.Ambient is { } scope)
        {
            // Set for that transaction alone: whatever way it ends, the settings end with it.
            scope.TransactionCompleted += (_, _) =>
            {
                if (ReferenceEquals(entry.Applied, desired))
                {
                    entry.Applied = null;
                }
            };
        }
        else if (desired.Transaction is null && System.Transactions.Transaction.Current is { } ambient)
        {
            ambient.TransactionCompleted += (_, completed) =>
            {
                if (completed.Transaction?.TransactionInformation.Status != System.Transactions.TransactionStatus.Committed
                    && ReferenceEquals(entry.Applied, desired))
                {
                    entry.Applied = desired.Undone();
                }
            };
        }
    }

    /// <summary>
    /// What to set on <paramref name="connection"/>, for its session, before its next statement, or
    /// <see langword="null"/> when what it carries is current. Inside a transaction nothing is set: a change of
    /// caller, and a connection that carries no caller at all, are refused with
    /// <see cref="CallerOptions.RequireExplicitCallers"/>, and logged once otherwise.
    /// </summary>
    /// <param name="connection">The connection the statement is for.</param>
    /// <param name="transaction">The transaction the statement runs in, as far as Entity Framework knows of one.</param>
    /// <param name="inTransaction">Whether the statement runs inside a transaction: that one, or an ambient one.</param>
    /// <param name="joined">What a transaction the context was not handed carries, when the server said it is still open.</param>
    private Applied? Changed(DbConnection? connection, DbTransaction? transaction, bool inTransaction, Applied? joined = null)
    {
        if (connection is null)
        {
            return null;
        }

        // What was set for one transaction counts inside that transaction alone; anywhere else the session
        // carries nothing of it.
        var applied = joined ?? CarriedBy(connection);
        if (joined is null && applied is not null && !applied.HoldsIn(transaction, System.Transactions.Transaction.Current))
        {
            applied = null;
        }

        inTransaction |= joined is not null;

        if (applied is null)
        {
            // A connection opened outside Entity Framework, so nothing set a caller on it.
            EnsureSettingsStayWithTheCaller(connection);
            var caller = Desired(applied: null);
            if (!inTransaction)
            {
                return caller;
            }

            const string NoCaller =
                "This connection was opened outside Entity Framework and carries no caller, and a transaction is open on it, so the caller cannot be set safely.";
            if (_requireExplicitCallers)
            {
                throw new InvalidOperationException(
                    NoCaller + $" Open the connection through a context, begin the transaction after a context opened it, or use {nameof(CallerConnections)}.");
            }

            var entry = EntryOf(connection);
            if (!entry.WarnedNoCaller)
            {
                entry.WarnedNoCaller = true;
                _logger.LogWarning(
                    NoCaller + " The command runs as the role the connection carries, not as {Caller}. " +
                    "Open the connection through a context, begin the transaction after a context opened it, or use CallerConnections; with RequireExplicitCallers this is refused.",
                    caller.Caller);
            }

            return null;
        }

        var desired = Desired(applied);
        if (applied.SameAs(desired))
        {
            return null;
        }

        if (!inTransaction)
        {
            // The same connection still: a warning it gave stays given.
            desired.Warned = applied.Warned;
            return desired;
        }

        RefuseOrLog(applied, desired);
        return null;
    }

    /// <summary>
    /// A caller that changed inside a transaction: refused with <see cref="CallerOptions.RequireExplicitCallers"/>,
    /// and otherwise logged, once per connection, after which the transaction goes on as the caller it began with.
    /// </summary>
    private void RefuseOrLog(Applied applied, Applied desired)
    {
        var (from, to) = Applied.Describe(applied, desired);
        if (_requireExplicitCallers)
        {
            throw new InvalidOperationException(
                $"The caller changed from {from} to {to} while a transaction was open on this connection. " +
                "A transaction runs as one caller: begin the caller before the transaction, or run the work on a context of its own.");
        }

        if (!applied.Warned)
        {
            applied.Warned = true;
            _logger.LogWarning(
                "The caller changed from {From} to {To} while a transaction was open on this connection, so the transaction goes on as {From}. " +
                "Begin the caller before the transaction, or run the work on a context of its own; with RequireExplicitCallers this is refused.",
                from,
                to,
                from);
        }
    }

    /// <summary>What to send before a transaction begins on <paramref name="connection"/>, if anything.</summary>
    private Pending? BeforeTransaction(DbConnection connection, DbContext? context)
    {
        if (_scope == RowLevelSecurityScope.Transaction)
        {
            // Only worked out, so a caller that cannot run fails before the transaction begins.
            _ = Desired(applied: null);
            return null;
        }

        return Changed(connection, transaction: null, InAmbientTransaction(context)) is { } desired
            ? new Pending(desired, Local: false, Keep: true)
            : null;
    }

    /// <summary>
    /// What to send before <paramref name="command"/>, if anything. Where the settings last one transaction and
    /// the command runs outside one, nothing is sent first: the call that sets them is written in front of the
    /// command's own text, here.
    /// </summary>
    /// <param name="command">The command about to run.</param>
    /// <param name="eventData">Where it comes from.</param>
    /// <param name="joined">What a transaction the context was not handed carries, when the server said it is still open: the command runs in it.</param>
    private Pending? Before(DbCommand command, CommandEventData eventData, Applied? joined)
    {
        var context = eventData.Context;
        var transaction = VisibleTransaction(command, context);

        if (_scope == RowLevelSecurityScope.Connection)
        {
            return Changed(command.Connection, transaction, transaction is not null || InAmbientTransaction(context), joined) is { } changed
                ? new Pending(changed, Local: false, Keep: true)
                : null;
        }

        // The text as the application wrote it: a command sent a second time carries the call already.
        var text = command.CommandText;
        if (TransactionControl.Refused(text.StartsWith(Call, StringComparison.Ordinal) ? text[Call.Length..] : text) is { } refused)
        {
            throw new InvalidOperationException(
                $"A command begins a statement with {refused}, which row level security refuses where the caller's settings last one transaction ({nameof(RowLevelSecurityScope)}.{nameof(RowLevelSecurityScope.Transaction)}): " +
                "it would end the transaction they are set for, or change the role itself, and every statement after it would run as the role the application logged in as. " +
                "Begin, commit and roll back through the context, and leave the role to the caller.");
        }

        if (joined is not null)
        {
            // Inside a transaction another context began and this one was not handed: it carries its caller.
            GoOnAs(joined);
            return null;
        }

        if (transaction is not null)
        {
            if (command.Connection is { } connection && Remembered(connection, transaction) is { } applied)
            {
                GoOnAs(applied);
                return null;
            }

            // A transaction nothing set a caller for. Every transaction a context begins or is handed gets one,
            // so this is a transaction whose settings went with something that happened behind the context's
            // back. Nothing can be set for it safely now, and a command in it would run as the login role.
            throw new InvalidOperationException(
                "This command runs in a transaction that no caller was set for: it was neither begun through a context nor handed to one with UseTransaction, or it has ended since. " +
                $"Begin the transaction through the context, hand it over with Database.UseTransaction, or take it from {nameof(CallerConnections)}.{nameof(CallerConnections.BeginTransactionAsync)}.");
        }

        if (AmbientOf(context) is { } ambient)
        {
            // A System.Transactions transaction, which Entity Framework enlists the connection in before it sends
            // a command. The settings are sent once, for that transaction, and remembered while the connection
            // stays open, as for any transaction: the commands of a save cannot carry the call, and a
            // transaction runs as one caller.
            if (command.Connection is { } enlisted && CarriedBy(enlisted) is { Ambient: { } carried } known && carried.Equals(ambient))
            {
                GoOnAs(known);
                return null;
            }

            return new Pending(Desired(applied: null).In(ambient), Local: true, Keep: true);
        }

        if (eventData.CommandSource == CommandSource.SaveChanges)
        {
            // The commands of a save cannot carry the call: Entity Framework reads the rows each statement
            // changed, in order.
            throw new InvalidOperationException(
                $"A save reached the database outside a transaction, where the caller's settings last one transaction ({nameof(RowLevelSecurityScope)}.{nameof(RowLevelSecurityScope.Transaction)}), so nothing would be set for it. " +
                "The interceptor sets the context's AutoTransactionBehavior to Always before every save; something set it to another value after that. Leave it to the interceptor.");
        }

        Prefix(command, Desired(applied: null));
        return null;
    }

    /// <summary>
    /// Writes the call that sets <paramref name="desired"/> for one transaction in front of
    /// <paramref name="command"/>'s text, with its four parameters. A command that carries the call already,
    /// one executed a second time, keeps its text and gets the parameters of the caller there is now.
    /// </summary>
    /// <exception cref="InvalidOperationException">The command is not text, or has a parameter without a name.</exception>
    private static void Prefix(DbCommand command, Applied desired)
    {
        var names = desired.Names;
        var values = desired.Values;
        if (desired.Timeout is { Length: > 0 } timeout)
        {
            names = [.. names, TimeoutSetting];
            values = [.. values, timeout];
        }

        if (command.CommandText.StartsWith(Call, StringComparison.Ordinal) && command.Parameters.Contains(RoleParameter))
        {
            command.Parameters[RoleParameter].Value = desired.Role;
            command.Parameters[ClaimsParameter].Value = desired.Claims;
            command.Parameters[NamesParameter].Value = names;
            command.Parameters[ValuesParameter].Value = values;
            return;
        }

        if (command.CommandType != CommandType.Text)
        {
            throw new InvalidOperationException(
                $"In transaction scope the caller's settings are written in front of a command's text, which a command of type {command.CommandType} has none of. Send the statement as text.");
        }

        foreach (DbParameter parameter in command.Parameters)
        {
            if (string.IsNullOrEmpty(parameter.ParameterName))
            {
                throw new InvalidOperationException(
                    "In transaction scope a command's parameters must be named, as Entity Framework's are; positional parameters cannot be mixed with the caller's.");
            }
        }

        command.CommandText = Call + command.CommandText;
        command.Parameters.Add(Named(command, RoleParameter, desired.Role));
        command.Parameters.Add(Named(command, ClaimsParameter, desired.Claims));

        // Arrays of text, which the provider reads from the value itself.
        command.Parameters.Add(Named(command, NamesParameter, names));
        command.Parameters.Add(Named(command, ValuesParameter, values));
    }

    private void EnsureCurrent(DbCommand command, CommandEventData eventData)
    {
        if (NotOnPostgres(eventData.Context))
        {
            return;
        }

        Applied? joined = null;
        if (Elsewhere(command, eventData.Context) is { } elsewhere)
        {
            using var read = MarkCommand(command.Connection!, command.Transaction);
            joined = StillThere(command.Connection!, elsewhere, read.ExecuteScalar());
        }

        if (Before(command, eventData, joined) is { } pending)
        {
            Send(command.Connection!, pending);
        }
    }

    private async ValueTask EnsureCurrentAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
    {
        if (NotOnPostgres(eventData.Context))
        {
            return;
        }

        Applied? joined = null;
        if (Elsewhere(command, eventData.Context) is { } elsewhere)
        {
            var read = MarkCommand(command.Connection!, command.Transaction);
            await using (read.ConfigureAwait(false))
            {
                joined = StillThere(command.Connection!, elsewhere, await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            }
        }

        if (Before(command, eventData, joined) is { } pending)
        {
            await SendAsync(command.Connection!, pending, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The transaction <paramref name="command"/> runs in, as far as Entity Framework knows of one: the command's own, or the context's current one.</summary>
    private static DbTransaction? VisibleTransaction(DbCommand command, DbContext? context)
        => command.Transaction ?? context?.Database.CurrentTransaction?.GetDbTransaction();

    /// <summary>
    /// What the command's connection is remembered to carry for a transaction the command does not run in, as far
    /// as Entity Framework knows, or <see langword="null"/>. Such a transaction has either ended without a word,
    /// on the connection itself, or it is still open and the context was not handed it; only the server knows
    /// which.
    /// </summary>
    private static Applied? Elsewhere(DbCommand command, DbContext? context)
        => command.Connection is { } connection
           && CarriedBy(connection) is { IsUndone: false } applied
           && (applied.Transaction is not null || applied.Ambient is not null)
           && !applied.HoldsIn(VisibleTransaction(command, context), AmbientOf(context))
            ? applied
            : null;

    private static void Send(DbConnection connection, Pending pending)
    {
        using (var command = CreateCommand(connection, pending))
        {
            command.ExecuteNonQuery();
        }

        if (pending.Keep)
        {
            Record(connection, pending.Apply);
        }
    }

    private static async ValueTask SendAsync(DbConnection connection, Pending pending, CancellationToken cancellationToken)
    {
        var command = CreateCommand(connection, pending);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (pending.Keep)
        {
            Record(connection, pending.Apply);
        }
    }

    /// <summary>
    /// Whether the context's commands run inside a System.Transactions transaction: an ambient
    /// <c>TransactionScope</c>, or one the context was enlisted in by hand.
    /// </summary>
    private static bool InAmbientTransaction(DbContext? context) => AmbientOf(context) is not null;

    /// <summary>The System.Transactions transaction the context's commands run in, if any: the ambient one, or one it was enlisted in by hand.</summary>
    private static System.Transactions.Transaction? AmbientOf(DbContext? context)
        => System.Transactions.Transaction.Current
            ?? (context is null ? null : System.Transactions.TransactionsDatabaseFacadeExtensions.GetEnlistedTransaction(context.Database));

    /// <summary>The statement that sets <paramref name="pending"/>'s caller, with its parameters in the statement's order.</summary>
    private static DbCommand CreateCommand(DbConnection connection, Pending pending)
    {
        var desired = pending.Apply;

        // For a session a timeout is always sent where the host configured any: a kind without one gets the
        // login role's own back. For a transaction only a kind that has one sends it.
        var timed = pending.Local ? desired.Timeout is { Length: > 0 } : desired.Timeout is not null;

        var command = connection.CreateCommand();
        command.Transaction = desired.Transaction;
        command.CommandText = desired.Owner.Statement(desired.Names.Length, pending.Local, timed);
        command.Parameters.Add(Parameter(command, desired.Role));
        command.Parameters.Add(Parameter(command, desired.Claims));

        for (var i = 0; i < desired.Names.Length; i++)
        {
            command.Parameters.Add(Parameter(command, desired.Names[i]));
            command.Parameters.Add(Parameter(command, desired.Values[i]));
        }

        if (pending.Local)
        {
            command.Parameters.Add(Parameter(command, desired.Mark ?? string.Empty));
        }

        if (timed)
        {
            command.Parameters.Add(Parameter(command, desired.Timeout!));
        }

        return command;
    }

    /// <summary>The text of the statement that sets a caller, built once per shape.</summary>
    private string Statement(int settings, bool local, bool timed)
        => _statements.GetOrAdd((settings, local, timed), static shape => StatementFor(shape.Settings, shape.Local, shape.Timed));

    /// <summary>The command that asks the server what the transaction that is open on <paramref name="connection"/>, if any, is marked with.</summary>
    private static DbCommand MarkCommand(DbConnection connection, DbTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ReadMark;
        return command;
    }

    /// <summary>
    /// <paramref name="remembered"/> when the server still has the mark it was set with, read as
    /// <paramref name="mark"/>: the transaction it was set for is the one that is open. Without the mark that
    /// transaction has ended, whatever the provider's object for it says, and what was remembered is forgotten.
    /// </summary>
    private static Applied? StillThere(DbConnection connection, Applied remembered, object? mark)
    {
        if (mark is string marked && string.Equals(marked, remembered.Mark, StringComparison.Ordinal))
        {
            return remembered;
        }

        Forget(connection);
        return null;
    }

    /// <summary>
    /// A command inside a transaction that carries <paramref name="applied"/>: it runs as that caller. Another
    /// caller by now is refused, or logged, as any change inside a transaction is.
    /// </summary>
    private void GoOnAs(Applied applied)
    {
        var desired = Desired(applied);
        if (!applied.SameAs(desired))
        {
            RefuseOrLog(applied, desired);
        }
    }

    private void SaveInATransaction(DbContext? context)
    {
        if (_scope == RowLevelSecurityScope.Transaction && context is not null && !NotOnPostgres(context))
        {
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
        }
    }

    /// <summary>
    /// Whether <paramref name="context"/> is on another database than Postgres, so nothing here is for it.
    /// <c>UseDDDToolkit</c> adds this interceptor to a context whose provider it could not see yet, one configured
    /// after the call or in <c>OnConfiguring</c>; such a context on SQLite, say, is passed over at every use, before
    /// anything asks who is calling. One on Postgres, whenever its provider was configured, is run as its caller. An
    /// event that names no context is taken as Postgres's, so what cannot be told fails closed.
    /// </summary>
    private static bool NotOnPostgres(DbContext? context)
        => context is not null && !string.Equals(context.Database.ProviderName, PostgresRowAccessChecks.NpgsqlProvider, StringComparison.Ordinal);

    private static DbParameter Parameter(DbCommand command, string value)
    {
        var parameter = command.CreateParameter();
        parameter.DbType = DbType.String;
        parameter.Value = value;
        return parameter;
    }

    private static DbParameter Named(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        if (value is string)
        {
            parameter.DbType = DbType.String;
        }

        parameter.Value = value;
        return parameter;
    }

    private static string RoleClaims(string role) => "{\"role\":" + JsonSerializer.Serialize(role) + "}";

    private static bool IsOn(DbConnectionStringBuilder builder, string key)
        => builder.TryGetValue(key, out var value)
            && Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() is { } text
            && (bool.TryParse(text, out var on) ? on : text is "1" || text.Equals("yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>Supabase's transaction pooler: a <c>*.pooler.supabase.com</c> host on port 6543.</summary>
    private static bool IsTransactionPooler(DbConnectionStringBuilder builder)
    {
        var hosts = (builder.TryGetValue("Host", out var host) || builder.TryGetValue("Server", out host))
            ? Convert.ToString(host, CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        var port = builder.TryGetValue("Port", out var configured) ? Convert.ToString(configured, CultureInfo.InvariantCulture)?.Trim() : null;

        foreach (var entry in hosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.LastIndexOf(':');
            var name = separator > 0 ? entry[..separator] : entry;
            var entryPort = separator > 0 ? entry[(separator + 1)..] : port;

            if (name.EndsWith(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase) && entryPort == "6543")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What to send for a caller before something else runs: the statement of <paramref name="Apply"/>, for the
    /// session or, with <paramref name="Local"/>, for the running transaction, and whether the connection is
    /// remembered to carry it afterwards.
    /// </summary>
    private readonly record struct Pending(Applied Apply, bool Local, bool Keep);

    /// <summary>What one connection carries, while it stays open.</summary>
    private sealed class Carried
    {
        /// <summary>What was last set on the connection, or <see langword="null"/> when nothing is known to be.</summary>
        public Applied? Applied { get; set; }

        /// <summary>Whether a command on it inside a transaction nobody set a caller for was logged already, so it is logged once while the connection is open.</summary>
        public bool WarnedNoCaller { get; set; }

        /// <summary>The connection closed, broke or opened: whatever was set is gone, or was never there.</summary>
        public void Forget(object sender, StateChangeEventArgs change)
        {
            Applied = null;
            WarnedNoCaller = false;
        }
    }

    /// <summary>What one statement set on a connection, and for whom.</summary>
    private sealed class Applied(
        PostgresRowLevelSecurityInterceptor owner,
        Caller caller,
        string role,
        string claims,
        string[] names,
        string[] values,
        string? timeout,
        bool asAnonymous,
        bool undone = false)
    {
        /// <summary>The interceptor that worked it out, with its own roles and settings.</summary>
        public PostgresRowLevelSecurityInterceptor Owner { get; } = owner;

        public Caller Caller { get; } = caller;

        public string Role { get; } = role;

        public string Claims { get; } = claims;

        /// <summary>The names of the module settings, which two interceptors of one process need not share.</summary>
        public string[] Names { get; } = names;

        public string[] Values { get; } = values;

        /// <summary>
        /// The statement timeout: milliseconds, <c>''</c> for the login role's own, and <see langword="null"/>
        /// where the host configured no timeouts, so none was set.
        /// </summary>
        public string? Timeout { get; } = timeout;

        /// <summary>Whether the caller is a signed-in user who runs as an anonymous caller, its token's role being on no list.</summary>
        public bool AsAnonymous { get; } = asAnonymous;

        /// <summary>Whether a rollback undid it, so the connection carries the role it logged in as, whatever this says.</summary>
        public bool IsUndone { get; } = undone;

        /// <summary>The transaction it was set for, and ends with; <see langword="null"/> when it was set for the session, or for a System.Transactions transaction.</summary>
        public DbTransaction? Transaction { get; private init; }

        /// <summary>The System.Transactions transaction it was set for, and ends with, where the settings last one transaction.</summary>
        public System.Transactions.Transaction? Ambient { get; private init; }

        /// <summary>What the transaction was marked with when it was set there.</summary>
        public string? Mark { get; private init; }

        /// <summary>
        /// Whether it was set for a transaction that was already running, one handed to a context, so that a
        /// savepoint made before it was set can undo it.
        /// </summary>
        public bool SetAfterItBegan { get; private init; }

        /// <summary>Whether a change inside a transaction was logged already, so it is logged once while the connection is open.</summary>
        public bool Warned { get; set; }

        /// <summary>The same, undone by the rollback of the transaction it was set in.</summary>
        public Applied Undone() => new(Owner, Caller, Role, Claims, Names, Values, Timeout, AsAnonymous, undone: true) { Warned = Warned };

        /// <summary>The same, set for one transaction and with a mark of its own.</summary>
        public Applied In(DbTransaction transaction, bool afterItBegan = false)
            => new(Owner, Caller, Role, Claims, Names, Values, Timeout, AsAnonymous) { Transaction = transaction, Mark = Guid.NewGuid().ToString("N"), SetAfterItBegan = afterItBegan };

        /// <summary>The same, set for one System.Transactions transaction.</summary>
        public Applied In(System.Transactions.Transaction ambient)
            => new(Owner, Caller, Role, Claims, Names, Values, Timeout, AsAnonymous) { Ambient = ambient, Mark = Guid.NewGuid().ToString("N") };

        /// <summary>
        /// Whether what was set is there for a statement that runs in <paramref name="transaction"/> and
        /// <paramref name="ambient"/>: always when it was set for the session, and otherwise only inside the
        /// transaction it was set for.
        /// </summary>
        public bool HoldsIn(DbTransaction? transaction, System.Transactions.Transaction? ambient)
            => Transaction is not null ? ReferenceEquals(Transaction, transaction)
                : Ambient is null || Ambient.Equals(ambient);

        public bool SameAs(Applied other)
            => !IsUndone
               && !other.IsUndone
               && string.Equals(Role, other.Role, StringComparison.Ordinal)
               && string.Equals(Claims, other.Claims, StringComparison.Ordinal)
               && string.Equals(Timeout, other.Timeout, StringComparison.Ordinal)
               && Names.AsSpan().SequenceEqual(other.Names)
               && Values.AsSpan().SequenceEqual(other.Values);

        /// <summary>The two callers for a message: by who they are, and by their settings when that is all that differs.</summary>
        public static (string From, string To) Describe(Applied from, Applied to)
        {
            if (from.IsUndone)
            {
                return ($"{from.Caller} (undone by the rollback of the transaction it was set in)", to.Caller.ToString());
            }

            var (a, b) = (from.Caller.ToString(), to.Caller.ToString());
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                return (a, b);
            }

            if (!from.Values.AsSpan().SequenceEqual(to.Values) || !from.Names.AsSpan().SequenceEqual(to.Names))
            {
                return ($"{a} (settings {string.Join(", ", from.Values.Select(Quoted))})", $"{b} (settings {string.Join(", ", to.Values.Select(Quoted))})");
            }

            return string.Equals(from.Timeout, to.Timeout, StringComparison.Ordinal)
                ? (a, b)
                : ($"{a} (statement timeout {Quoted(from.Timeout ?? "unset")})", $"{b} (statement timeout {Quoted(to.Timeout ?? "unset")})");

            static string Quoted(string value) => "'" + value + "'";
        }
    }
}
