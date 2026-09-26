using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A plain Postgres with the help desk on it, set up the way an application off Supabase sets itself up:
/// <see cref="PostgresRowAccess.SetupScript"/> for the roles and the caller functions, the schema from the
/// model, owned by the application's login role, and the desk's rules as policies from
/// <see cref="PostgresRowAccess.Script"/>. Nothing of Supabase's is there.
/// </summary>
public sealed class PostgresRowAccessDatabase : IAsyncLifetime
{
    public const string LoginRole = "desk_app";

    public const int TicketCount = 5;

    public static readonly Guid Alice = Guid.Parse("a11ce000-0000-4000-8000-000000000001");

    public static readonly Guid Bob = Guid.Parse("b0b00000-0000-4000-8000-000000000002");

    public static readonly Guid Carol = Guid.Parse("ca401000-0000-4000-8000-000000000003");

    private PgmqDatabase? _database;

    /// <summary>Whether the container started; a test skips or fails through <see cref="Require"/> when it did not.</summary>
    public bool Available => _database is not null;

    private PgmqDatabase Database => _database ?? throw new InvalidOperationException("The database did not start.");

    private string ApplicationConnectionString => new NpgsqlConnectionStringBuilder(Database.ConnectionString)
    {
        Username = LoginRole,
        Password = LoginRole,
    }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        var cancellation = TestContext.Current.CancellationToken;
        _database = await PgmqDatabase.StartAsync(cancellation);
        if (_database is null)
        {
            return;
        }

        await RunAsOwnerAsync($"CREATE ROLE {LoginRole} LOGIN NOINHERIT PASSWORD '{LoginRole}';", cancellation);
        await RunAsOwnerAsync(PostgresRowAccess.SetupScript(loginRole: LoginRole), cancellation);

        await using (var model = DeskContext.Create(Database.ConnectionString))
        {
            await RunAsOwnerAsync(model.Database.GenerateCreateScript(), cancellation);
            await RunAsOwnerAsync(
                $"""
                DO $$
                DECLARE
                    t record;
                BEGIN
                    FOR t IN SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname = 'desk' LOOP
                        EXECUTE format('ALTER TABLE desk.%I OWNER TO {LoginRole}', t.tablename);
                    END LOOP;
                END
                $$;
                ALTER SCHEMA desk OWNER TO {LoginRole};
                GRANT USAGE ON SCHEMA desk TO anon, authenticated;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA desk TO anon, authenticated;
                """,
                cancellation);
            await RunAsOwnerAsync(PostgresRowAccess.Script(model, [DeskRules.Owners, DeskRules.Teammates, DeskRules.Public]), cancellation);
        }

        // As the application itself, which owns the tables: the rules are for callers.
        await using var seed = CreateContext(Caller.System);
        seed.Tickets.AddRange(
            Ticket("Alice's, open for the north", Alice, "north", TicketStatus.Open, isPublic: false, "On Alice's"),
            Ticket("Bob's own, closed", Bob, "north", TicketStatus.Closed, isPublic: false, "On Bob's"),
            Ticket("Carol's own", Carol, "south", TicketStatus.Open, isPublic: false, "On Carol's own"),
            Ticket("Everyone's", owner: null, team: null, TicketStatus.Open, isPublic: true, "On everyone's"),
            Ticket("Nobody's", owner: null, team: null, TicketStatus.Closed, isPublic: false, "On nobody's"));
        await seed.SaveChangesAsync(cancellation);
    }

    /// <summary>Skips the calling test without Docker, or fails it where containers are required.</summary>
    public void Require()
        => RequiredContainers.EnforceOrSkip(
            Available,
            RequiredContainers.Required,
            "PostgreSQL",
            $"No Docker here, so '{PgmqDatabase.Image}' could not be started. Row level security on Postgres is not covered on this machine.");

    /// <summary>A context as the application logs in, running as <paramref name="caller"/>.</summary>
    public DeskContext CreateContext(Caller caller)
        => DeskContext.Create(ApplicationConnectionString, new PostgresRowLevelSecurityInterceptor(new Fixed(caller), new PostgresRowLevelSecurityOptions()));

    /// <summary>Every ticket <paramref name="caller"/> may read.</summary>
    public async Task<List<Ticket>> TicketsAsync(Caller caller, CancellationToken cancellationToken)
    {
        await using var context = CreateContext(caller);
        return await context.Tickets.AsNoTracking().ToListAsync(cancellationToken);
    }

    /// <summary>Every comment <paramref name="caller"/> may read, asked of the comments' own table.</summary>
    public async Task<List<string>> CommentsAsync(Caller caller, CancellationToken cancellationToken)
    {
        await using var context = CreateContext(caller);
        return await context.Database.SqlQueryRaw<string>("""SELECT "Text" AS "Value" FROM desk."TicketComment" """).ToListAsync(cancellationToken);
    }

    /// <summary>The names of the policies on <c>desk.<paramref name="table"/></c>, in order.</summary>
    public async Task<List<string>> PolicyNamesAsync(string table, CancellationToken cancellationToken)
    {
        await using var connection = await Database.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT policyname FROM pg_catalog.pg_policies WHERE schemaname = 'desk' AND tablename = $1 ORDER BY policyname", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = table });

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>Runs <paramref name="sql"/> as the superuser.</summary>
    public async Task RunAsOwnerAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await Database.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The claims an identity provider would sign for <paramref name="user"/> of <paramref name="team"/>.</summary>
    public static string ClaimsOf(Guid user, string team)
        => $$$"""{"sub":"{{{user}}}","role":"authenticated","app_metadata":{"team":"{{{team}}}"}}""";

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private static Ticket Ticket(string title, Guid? owner, string? team, TicketStatus status, bool isPublic, string comment)
    {
        var ticket = new Ticket(TicketId.CreateSequential(), title, owner, team, status, isPublic);
        ticket.Comment(comment);
        return ticket;
    }

    private sealed class Fixed(Caller caller) : ICallerAccessor
    {
        public Caller Current => caller;
    }
}
