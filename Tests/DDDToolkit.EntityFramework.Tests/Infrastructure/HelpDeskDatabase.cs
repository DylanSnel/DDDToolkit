using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A plain Postgres with the help desk in <c>"Help_Desk"</c>, a schema Postgres keeps only in double quotes, set up
/// as <see cref="PostgresRowAccessDatabase"/> sets up the desk: <see cref="PostgresRowAccess.SetupScript"/>, the
/// tables from the model, owned by the application's login role, and the desk's rules as policies, with the
/// privileges the script writes from them, a set-shaped access function named relative to the desk and a column rule.
/// The script runs twice, as a second access file does over a database that has the first.
/// </summary>
public sealed class HelpDeskDatabase : IAsyncLifetime
{
    public const string LoginRole = "help_desk_app";

    /// <summary>The help desk's rules, as the database has them.</summary>
    public static readonly RowAccessRule[] Rules =
        [DeskRules.Owners, DeskRules.Teammates, DeskRules.Public, DeskRules.WatchersBySet, DeskRules.TeamWork, DeskRules.OwnersClose];

    private PgmqDatabase? _database;

    /// <summary>Whether the container started; a test skips or fails through <see cref="Require"/> when it did not.</summary>
    public bool Available => _database is not null;

    private PgmqDatabase Database => _database ?? throw new InvalidOperationException("The database did not start.");

    private string ApplicationConnectionString => new NpgsqlConnectionStringBuilder(Database.ConnectionString)
    {
        Username = LoginRole,
        Password = LoginRole,
    }.ConnectionString;

    /// <summary>
    /// An access function named relative to the desk that no rule asks: nothing in the database depends on it, so
    /// only its name, as the next script finds it, keeps it where it is.
    /// </summary>
    public static readonly RowAccessFunction AnyTicket = RowAccessFunction.For<Ticket>("desk/any_ticket", "TRUE");

    /// <summary>
    /// The script of the help desk's rules, with the privileges it writes from them, and <see cref="AnyTicket"/> unless
    /// left out; with row level security forced on the desk's tables where <paramref name="force"/> says so.
    /// </summary>
    public static string Script(bool withAnyTicket = true, bool force = false)
    {
        using var model = HelpDeskContext.Create();
        return PostgresRowAccess.Script(model, Rules, withAnyTicket ? [DeskRules.WatchedSet, AnyTicket] : [DeskRules.WatchedSet], new RowAccessExport { WriteGrants = true, ForceRowLevelSecurity = force });
    }

    public async ValueTask InitializeAsync()
    {
        var cancellation = TestContext.Current.CancellationToken;
        _database = await PgmqDatabase.StartAsync(cancellation);
        if (_database is null)
        {
            return;
        }

        await RunAsOwnerAsync(ServerRole.Create(LoginRole, $"LOGIN NOINHERIT PASSWORD '{LoginRole}'"), cancellation);
        await RunAsOwnerAsync(PostgresRowAccess.SetupScript(loginRole: LoginRole), cancellation);

        await using (var model = HelpDeskContext.Create(Database.ConnectionString))
        {
            await RunAsOwnerAsync(model.Database.GenerateCreateScript(), cancellation);
        }

        await RunAsOwnerAsync(
            $"""
            DO $$
            DECLARE
                t record;
            BEGIN
                FOR t IN SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname = '{HelpDeskContext.Schema}' LOOP
                    EXECUTE format('ALTER TABLE %I.%I OWNER TO {LoginRole}', '{HelpDeskContext.Schema}', t.tablename);
                END LOOP;
            END
            $$;
            ALTER SCHEMA "{HelpDeskContext.Schema}" OWNER TO {LoginRole};
            """,
            cancellation);

        // Twice: the second finds what the first made by the names the catalog keeps, and makes it again.
        await RunAsOwnerAsync(Script(), cancellation);
        await RunAsOwnerAsync(Script(), cancellation);

        // As the application itself, which owns the tables: the rules are for callers.
        await using var seed = CreateContext(Caller.System);
        seed.Tickets.AddRange(
            Ticket("Alice's, open for the north", PostgresRowAccessDatabase.Alice, "north", TicketStatus.Open, isPublic: false),
            Ticket("Bob's own, closed", PostgresRowAccessDatabase.Bob, "north", TicketStatus.Closed, isPublic: false),
            Ticket("Carol's own", PostgresRowAccessDatabase.Carol, "south", TicketStatus.Open, isPublic: false),
            Ticket("Everyone's", owner: null, team: null, TicketStatus.Open, isPublic: true),
            Ticket("Nobody's", owner: null, team: null, TicketStatus.Closed, isPublic: false, watcher: PostgresRowAccessDatabase.Carol));
        await seed.SaveChangesAsync(cancellation);
    }

    /// <summary>Skips the calling test without Docker, or fails it where containers are required.</summary>
    public void Require()
        => RequiredContainers.EnforceOrSkip(
            Available,
            RequiredContainers.Required,
            "PostgreSQL",
            $"No Docker here, so '{PgmqDatabase.Image}' could not be started. Row level security in a schema with capitals is not covered on this machine.");

    /// <summary>A context as the application logs in, running as <paramref name="caller"/>.</summary>
    public HelpDeskContext CreateContext(Caller caller)
        => HelpDeskContext.Create(ApplicationConnectionString, new PostgresRowLevelSecurityInterceptor(new Fixed(caller), new PostgresRowLevelSecurityOptions()));

    /// <summary>Every ticket <paramref name="caller"/> may read.</summary>
    public async Task<List<Ticket>> TicketsAsync(Caller caller, CancellationToken cancellationToken)
    {
        await using var context = CreateContext(caller);
        return await context.Tickets.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <summary>Runs <paramref name="sql"/> as <paramref name="caller"/>, with <c>{0}</c>, <c>{1}</c> for <paramref name="parameters"/>, and returns the rows it affected.</summary>
    public async Task<int> RunAsAsync(Caller caller, string sql, CancellationToken cancellationToken, params object[] parameters)
    {
        await using var context = CreateContext(caller);
        return await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }

    /// <summary>The first column of every row <paramref name="sql"/> answers, asked as the superuser.</summary>
    public async Task<List<string>> AskAsOwnerAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await Database.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return rows;
    }

    /// <summary>Runs <paramref name="sql"/> as the superuser.</summary>
    public async Task RunAsOwnerAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await Database.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private static Ticket Ticket(string title, Guid? owner, string? team, TicketStatus status, bool isPublic, Guid? watcher = null)
    {
        var ticket = new Ticket(TicketId.CreateSequential(), title, owner, team, status, isPublic);
        if (watcher is { } user)
        {
            ticket.Watch(user);
        }

        return ticket;
    }

    private sealed class Fixed(Caller caller) : ICallerAccessor
    {
        public Caller Current => caller;
    }
}
