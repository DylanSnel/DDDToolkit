using Microsoft.Data.Sqlite;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>
/// The gardens over one SQLite database in memory, of the calling test's own. The connection stays open for
/// as long as the test runs, and every context uses it, so a context made for one reading sees what the
/// request's own context wrote.
/// </summary>
public sealed class SqliteGarden : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <param name="configure">Registers what a test needs before the resource is registered.</param>
    /// <param name="ownContexts">Whether the host registers a factory for its context.</param>
    public SqliteGarden(Action<IServiceCollection>? configure = null, bool ownContexts = false)
    {
        _connection.Open();
        Services = new GardenServices(options => options.UseSqlite(_connection), Clock, configure, ownContexts);

        using var scope = Services.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<GardenContext>().Database.EnsureCreated();
        Scenario = new GardenScenario(Clock.Now);
    }

    /// <summary>The clock the access questions ask, which a test moves on.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>The gardens' services.</summary>
    public GardenServices Services { get; }

    /// <summary>Every command the contexts sent since the scenario was saved.</summary>
    public CommandCounter Commands => Services.Commands;

    /// <summary>The data, as it is at the moment the clock started at.</summary>
    public GardenScenario Scenario { get; }

    /// <summary>The gardens with the scenario saved, and nothing counted or raised yet.</summary>
    public static async Task<SqliteGarden> SeededAsync(Action<IServiceCollection>? configure = null, bool ownContexts = false)
    {
        var garden = new SqliteGarden(configure, ownContexts);
        await garden.Scenario.SaveAsync(garden.Services);
        garden.Commands.Reset();
        garden.Services.Raised.Clear();
        return garden;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }
}
