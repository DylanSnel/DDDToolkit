using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// A plain Postgres with the pallet depot on it, set up as an application off Supabase sets itself up: the roles
/// and the caller functions from <see cref="PostgresRowAccess.SetupScript"/>, the tables from the model, owned by
/// the login role, and the depot's two rules as policies. Alice and Bob each own a pallet, and both read both.
/// Alice's pallet carries a stamp, and one policy written by hand lets nobody change a stamp once it is made,
/// where the rules let a pallet's owner: a table of an aggregate's entities with a policy stricter than its root's.
/// </summary>
public sealed class PalletDepotDatabase : IAsyncLifetime
{
    /// <summary>The collection whose test classes share this one database, and so run one after the other.</summary>
    public const string Collection = "The pallet depot on Postgres";

    public const string LoginRole = "depot_app";

    public static readonly Guid AliceId = Guid.Parse("a11ce000-0000-4000-8000-000000000011");

    public static readonly Guid BobId = Guid.Parse("b0b00000-0000-4000-8000-000000000012");

    public static readonly Caller Alice = Callers.FromClaims($$"""{"sub":"{{AliceId}}","role":"authenticated"}""");

    public static readonly Caller Bob = Callers.FromClaims($$"""{"sub":"{{BobId}}","role":"authenticated"}""");

    public static readonly DepotId North = new(1);

    public static readonly DepotId South = new(2);

    /// <summary>Alice's pallet, number 1 of the north depot.</summary>
    public static readonly PalletId AlicesPallet = new(Guid.Parse("11111111-0000-4000-8000-000000000001"));

    /// <summary>Bob's pallet, number 2 of the north depot.</summary>
    public static readonly PalletId BobsPallet = new(Guid.Parse("22222222-0000-4000-8000-000000000002"));

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
        _database = await PgmqDatabase.StartAsync(PgmqDatabase.SupabaseImage, cancellation);
        if (_database is null)
        {
            return;
        }

        await RunAsOwnerAsync($"CREATE ROLE {LoginRole} LOGIN NOINHERIT PASSWORD '{LoginRole}';", cancellation);
        await RunAsOwnerAsync(PostgresRowAccess.SetupScript(loginRole: LoginRole), cancellation);

        await using (var model = new PalletContext(new DbContextOptionsBuilder<PalletContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options))
        {
            await RunAsOwnerAsync(model.Database.GenerateCreateScript(), cancellation);
            await RunAsOwnerAsync(
                $"""
                ALTER TABLE {PalletContext.Schema}."Pallets" OWNER TO {LoginRole};
                ALTER TABLE {PalletContext.Schema}."PalletStamp" OWNER TO {LoginRole};
                ALTER SCHEMA {PalletContext.Schema} OWNER TO {LoginRole};
                GRANT USAGE ON SCHEMA {PalletContext.Schema} TO anon, authenticated;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {PalletContext.Schema} TO anon, authenticated;
                """,
                cancellation);
            await RunAsOwnerAsync(PostgresRowAccess.Script(model, [PalletRules.Owners, PalletRules.SignedIn], []), cancellation);
            await RunAsOwnerAsync(
                $"""CREATE POLICY "A stamp stays as it was made" ON {PalletContext.Schema}."PalletStamp" AS RESTRICTIVE FOR UPDATE TO authenticated USING (false);""",
                cancellation);
        }

        // As the application itself, which owns the tables: the rules are for callers.
        await using var seed = CreateContext(Caller.System);
        var alices = new Pallet(AlicesPallet, North, 1, "Alice's", AliceId);
        alices.Stamp("Fragile");
        seed.Pallets.AddRange(alices, new Pallet(BobsPallet, North, 2, "Bob's", BobId));
        await seed.SaveChangesAsync(cancellation);
    }

    /// <summary>Skips the calling test without Docker, or fails it where containers are required.</summary>
    public void Require()
        => RequiredContainers.EnforceOrSkip(
            Available,
            RequiredContainers.Required,
            "PostgreSQL",
            $"No Docker here, so '{PgmqDatabase.SupabaseImage}' could not be started. What Postgres refuses in a save is not covered on this machine.");

    /// <summary>
    /// A context as the application logs in, running as <paramref name="caller"/>, with nothing of the toolkit's
    /// but the interceptor that sets the caller: what Entity Framework and Npgsql do on their own.
    /// </summary>
    public PalletContext CreateContext(Caller caller) => CreateContext(caller, [], logs: null);

    /// <summary>
    /// A context as the application logs in, running as <paramref name="caller"/>, that saves the way
    /// <c>UseDDDToolkit</c> makes a context save: optimistic concurrency, and then what the database refuses.
    /// </summary>
    public PalletContext CreateSavingContext(Caller caller, ILoggerFactory? logs = null)
        => CreateContext(caller, [new AggregateVersionInterceptor(), new DatabaseRefusalInterceptor()], logs);

    /// <summary>
    /// A context as the application logs in, which asks <paramref name="callers"/> who is calling and gives each
    /// caller the role <paramref name="roles"/> names: a host whose roles are not the defaults.
    /// </summary>
    public PalletContext CreateContext(ICallerAccessor callers, PostgresRowLevelSecurityOptions roles)
        => new(new DbContextOptionsBuilder<PalletContext>()
            .UseNpgsql(ApplicationConnectionString)
            .AddInterceptors(new PostgresRowLevelSecurityInterceptor(callers, roles))
            .Options);

    /// <summary>The depot's model on Npgsql, for a script to be written from: it never connects.</summary>
    public static PalletContext Model()
        => new(new DbContextOptionsBuilder<PalletContext>().UseNpgsql("Host=nowhere.invalid;Database=unused").Options);

    /// <summary>The depot's own rules, as its policies were made from them.</summary>
    public static IReadOnlyList<RowAccessRule> Rules { get; } = [PalletRules.Owners, PalletRules.SignedIn];

    /// <summary>The one value <paramref name="sql"/> answers, as text, asked as the superuser.</summary>
    public async Task<string?> ScalarAsOwnerAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await Database.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private PalletContext CreateContext(Caller caller, IInterceptor[] interceptors, ILoggerFactory? logs)
        => new(new DbContextOptionsBuilder<PalletContext>()
            .UseNpgsql(ApplicationConnectionString)
            .UseLoggerFactory(logs)
            .AddInterceptors([new PostgresRowLevelSecurityInterceptor(new Fixed(caller), new PostgresRowLevelSecurityOptions()), .. interceptors])
            .Options);

    /// <summary>How many pallets there are, counted as the superuser.</summary>
    public async Task<long> CountAsync(CancellationToken cancellationToken)
    {
        await using var connection = await Database.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {PalletContext.Schema}.\"Pallets\"", connection);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// The depot as it was seeded, whatever the test before left behind: no pallet but Alice's and Bob's, each
    /// with its own number, label and owner. Every class of the collection ends each of its tests with it, a
    /// failed one included, so no class counts on the order the classes run in.
    /// </summary>
    public async Task RestoreAsync()
    {
        if (!Available)
        {
            return;
        }

        // As the application itself, which owns the tables, and without the test's token: one that was cancelled
        // still leaves the depot clean.
        await using var context = CreateContext(Caller.System);
        var pallets = await context.Pallets.ToListAsync(CancellationToken.None);
        foreach (var pallet in pallets)
        {
            if (pallet.Id == AlicesPallet)
            {
                Seeded(pallet, 1, "Alice's", AliceId);
            }
            else if (pallet.Id == BobsPallet)
            {
                Seeded(pallet, 2, "Bob's", BobId);
            }
            else
            {
                context.Pallets.Remove(pallet);
            }
        }

        await context.SaveChangesAsync(CancellationToken.None);

        static void Seeded(Pallet pallet, int number, string label, Guid owner)
        {
            pallet.Renumber(number);
            pallet.Retitle(label);
            pallet.HandTo(owner);
        }
    }

    /// <summary>Runs <paramref name="sql"/> as <paramref name="caller"/> and returns the number of rows it affected.</summary>
    public async Task<int> RunAsAsync(Caller caller, string sql, CancellationToken cancellationToken, params object[] parameters)
    {
        await using var context = CreateContext(caller);
        return await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
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

    private sealed class Fixed(Caller caller) : ICallerAccessor
    {
        public Caller Current => caller;
    }
}

/// <summary>One Postgres for the classes that ask what it refuses: the probes, and the toolkit's answer to each.</summary>
[CollectionDefinition(PalletDepotDatabase.Collection)]
public sealed class PalletDepotCollection : ICollectionFixture<PalletDepotDatabase>;
