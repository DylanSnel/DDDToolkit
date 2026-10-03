using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Converters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// The help desk at a size where Postgres plans like it would in production: 5,000 tickets, two watchers on
/// each, in a schema of its own, <c>desk_scale</c>, so the small desk and every count its tests assert stay as
/// they are. Its one rule asks a set-shaped access function, the tickets the caller watches, so what the
/// tests read in <c>EXPLAIN</c> is how a policy asks a set.
/// </summary>
/// <remarks>
/// The rows are written by SQL as the owner, in bulk, and the tables analyzed, so the planner knows their
/// size. <see cref="Watcher"/> watches every 200th ticket, <see cref="WatchedByWatcher"/> in all, and a
/// thousand other people share the rest.
/// </remarks>
public sealed class PostgresRowAccessScaleDatabase : IAsyncLifetime
{
    public const string LoginRole = "desk_scale_app";

    public const int TicketCount = 5000;

    public const int WatchedByWatcher = 25;

    /// <summary>The person the tests read as: they watch <see cref="WatchedByWatcher"/> tickets.</summary>
    public static readonly Guid Watcher = Guid.Parse("5ca1e000-0000-4000-8000-000000000001");

    private PgmqDatabase? _database;

    /// <summary>Whether the container started; a test skips or fails through <see cref="Require"/> when it did not.</summary>
    public bool Available => _database is not null;

    private PgmqDatabase Database => _database ?? throw new InvalidOperationException("The database did not start.");

    public async ValueTask InitializeAsync()
    {
        var cancellation = TestContext.Current.CancellationToken;
        _database = await PgmqDatabase.StartAsync(PgmqDatabase.SupabaseImage, cancellation);
        if (_database is null)
        {
            return;
        }

        await RunAsOwnerAsync($"CREATE ROLE {LoginRole} LOGIN NOINHERIT PASSWORD '{LoginRole}';", cancellation);
        await RunAsOwnerAsync(PostgresRowAccess.SetupScript(loginRole: LoginRole), cancellation);

        await using var model = DeskScaleContext.Create(Database.ConnectionString);
        await RunAsOwnerAsync(model.Database.GenerateCreateScript(), cancellation);
        await RunAsOwnerAsync(
            $$"""
            DO $$
            DECLARE
                t record;
            BEGIN
                FOR t IN SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname = 'desk_scale' LOOP
                    EXECUTE format('ALTER TABLE desk_scale.%I OWNER TO {{LoginRole}}', t.tablename);
                END LOOP;
            END
            $$;
            ALTER SCHEMA desk_scale OWNER TO {{LoginRole}};
            GRANT USAGE ON SCHEMA desk_scale TO anon, authenticated;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA desk_scale TO anon, authenticated;

            INSERT INTO desk_scale."Tickets" ("Id", "Title", "Owner", "Team", "Status", "IsPublic", "Version")
            SELECT md5('ticket ' || n)::uuid, 'Ticket ' || n,
                   CASE WHEN n % 10 = 0 THEN NULL ELSE md5('owner ' || (n % 200))::uuid END,
                   CASE n % 3 WHEN 0 THEN 'north' WHEN 1 THEN 'south' END,
                   n % 2, false, 1
            FROM generate_series(1, {{TicketCount}}) n;

            INSERT INTO desk_scale."TicketWatcher" ("Id", "TicketId", "User")
            SELECT md5('first watcher ' || n)::uuid, md5('ticket ' || n)::uuid, md5('person ' || (n % 1000))::uuid
            FROM generate_series(1, {{TicketCount}}) n
            UNION ALL
            SELECT md5('second watcher ' || n)::uuid, md5('ticket ' || n)::uuid, md5('person ' || ((n * 7 + 3) % 1000))::uuid
            FROM generate_series(1, {{TicketCount}}) n
            UNION ALL
            SELECT md5('watcher ' || n)::uuid, md5('ticket ' || n)::uuid, '{{Watcher}}'::uuid
            FROM generate_series(200, {{TicketCount}}, 200) n;

            ANALYZE desk_scale."Tickets";
            ANALYZE desk_scale."TicketWatcher";
            """,
            cancellation);

        await RunAsOwnerAsync(PostgresRowAccess.Script(model, [DeskRules.WatchersBySet], [DeskRules.WatchedSet]), cancellation);
    }

    /// <summary>Skips the calling test without Docker, or fails it where containers are required.</summary>
    public void Require()
        => RequiredContainers.EnforceOrSkip(
            Available,
            RequiredContainers.Required,
            "PostgreSQL",
            $"No Docker here, so '{PgmqDatabase.SupabaseImage}' could not be started. How Postgres plans a set-shaped question is not covered on this machine.");

    /// <summary>An open connection as the superuser, which a test narrows to a caller with <see cref="BecomeAsync"/>.</summary>
    public Task<NpgsqlConnection> OpenAsOwnerAsync(CancellationToken cancellationToken) => Database.OpenAsync(cancellationToken);

    /// <summary>
    /// Makes the rest of <paramref name="transaction"/> run as <paramref name="user"/> would through the
    /// interceptor: the role of a signed-in user, and the claims of their token.
    /// </summary>
    public static async Task BecomeAsync(NpgsqlConnection connection, Guid user, CancellationToken cancellationToken)
    {
        await using (var claims = new NpgsqlCommand("SELECT set_config('request.jwt.claims', $1, true)", connection))
        {
            claims.Parameters.Add(new NpgsqlParameter { Value = $$"""{"sub":"{{user}}","role":"authenticated"}""" });
            await claims.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var role = new NpgsqlCommand("SET LOCAL ROLE authenticated", connection);
        await role.ExecuteNonQueryAsync(cancellationToken);
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
}

/// <summary>The help desk's tickets in the schema <c>desk_scale</c>, for the tests that need many of them.</summary>
public sealed class DeskScaleContext(DbContextOptions<DeskScaleContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("desk_scale");
        modelBuilder.Entity<Ticket>().OwnsMany(ticket => ticket.Comments, comment => comment.OwnsMany(each => each.Reactions));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }

    /// <summary>A context on <paramref name="connectionString"/>, or on nothing for a script, which never connects.</summary>
    public static DeskScaleContext Create(string connectionString = "Host=nowhere.invalid;Database=unused")
        => new(new DbContextOptionsBuilder<DeskScaleContext>().UseNpgsql(connectionString).Options);
}
