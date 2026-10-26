using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace DDDToolkit.Supporting.Together.Tests.Infrastructure;

/// <summary>
/// The campus over one database of the calling test's own, with the scenario saved: what every test of both
/// domains together asks. The same on SQLite and on Postgres, so a claim is proven on both by one test.
/// </summary>
public sealed class CampusApp : IDisposable
{
    private readonly IDisposable? _database;
    private readonly Func<CampusContexts, CampusServices> _host;
    private readonly List<CampusServices> _others = [];

    /// <summary>The campus over a database.</summary>
    /// <param name="host">Makes the application's services over the database, for a host that takes its contexts from somewhere.</param>
    /// <param name="contexts">Where the host the scenario is saved through takes its contexts from.</param>
    /// <param name="scenario">The data, not saved yet.</param>
    /// <param name="provider">The provider's name, as a test's reason names it.</param>
    /// <param name="readModel">What Tenancy's rights are called in a statement of the courses' context on this database.</param>
    /// <param name="database">What is disposed of with the campus, after its services.</param>
    public CampusApp(Func<CampusContexts, CampusServices> host, CampusContexts contexts, CampusScenario scenario, string provider, string readModel, IDisposable? database = null)
    {
        _host = host;
        Services = host(contexts);
        Scenario = scenario;
        Provider = provider;
        ReadModel = readModel;
        _database = database;
    }

    /// <summary>The application's services.</summary>
    public CampusServices Services { get; }

    /// <summary>The data, as it was saved.</summary>
    public CampusScenario Scenario { get; }

    /// <summary>Every command the contexts sent since the scenario was saved.</summary>
    public CommandCounter Commands => Services.Commands;

    /// <summary>The provider's name.</summary>
    public string Provider { get; }

    /// <summary>
    /// What Tenancy's rights are called in a statement of the courses' context on this database: the table a
    /// view of the read model reads, or the function a module asks where the database keeps the rights.
    /// </summary>
    public string ReadModel { get; }

    /// <summary>How the application connects to the database, as its login role, where the database is a server's; <see langword="null"/> in memory.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// The clock both domains ask, where a test can move it: in memory, where nothing else keeps time.
    /// <see langword="null"/> on a server, whose functions and policies ask the database's own clock.
    /// </summary>
    public FixedClock? Clock { get; init; }

    /// <summary>
    /// A second host of the same application over the same database, which takes its contexts from somewhere
    /// else: what the first one saved is there for it, and it is disposed of with the campus.
    /// </summary>
    public CampusServices AnotherHost(CampusContexts contexts)
    {
        var other = _host(contexts);
        _others.Add(other);
        return other;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var other in _others)
        {
            other.Dispose();
        }

        Services.Dispose();
        _database?.Dispose();
    }
}

/// <summary>Where a test class's campus comes from: a new database for each test.</summary>
public abstract class CampusDatabases
{
    /// <summary>A new, empty SQLite database in memory for each.</summary>
    public static CampusDatabases Sqlite { get; } = new InMemory();

    /// <summary>The campus over a new database of the calling test's own, with the scenario saved and nothing counted yet.</summary>
    /// <param name="contexts">Where the host takes the courses' context from. The scenario is saved through a context of a request, so not from a factory alone.</param>
    public abstract Task<CampusApp> CreateAsync(CampusContexts contexts = CampusContexts.OfTheRequest);

    /// <summary>
    /// One SQLite database in memory: the connection stays open for as long as the test runs, and every context
    /// uses it, so Tenancy's tables and the courses' are in one database, as the read model needs.
    /// </summary>
    private sealed class InMemory : CampusDatabases
    {
        public override async Task<CampusApp> CreateAsync(CampusContexts contexts = CampusContexts.OfTheRequest)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var clock = new FixedClock();
            var campus = new CampusApp(
                from => new CampusServices(options => options.UseSqlite(connection), clock, contexts: from),
                contexts,
                new CampusScenario(clock.Now),
                "SQLite",
                "\"SeatRights\"",
                connection)
            {
                Clock = clock,
            };
            var services = campus.Services;
            using (var scope = services.Provider.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<TenancyContext>().Database.EnsureCreated();

                // EnsureCreated does nothing on a database that already has tables, so the courses' own are
                // created by hand; the read model's views over Tenancy's tables are no tables, and are not created.
                scope.ServiceProvider.GetRequiredService<CampusContext>().GetService<IRelationalDatabaseCreator>().CreateTables();
            }

            await campus.Scenario.SaveAsync(services);
            services.Commands.Reset();
            return campus;
        }
    }
}
