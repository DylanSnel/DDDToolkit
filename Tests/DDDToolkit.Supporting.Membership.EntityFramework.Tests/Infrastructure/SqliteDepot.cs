using Microsoft.Data.Sqlite;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>
/// The depot over one SQLite database in memory, of the calling test's own. The connection stays open for as
/// long as the test runs, and every context uses it, so a context made for one reading sees what the
/// request's own context wrote.
/// </summary>
public sealed class SqliteDepot : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <param name="configure">Registers what a test needs before the resources are registered.</param>
    /// <param name="ownContexts">Whether the host registers a factory for its context.</param>
    public SqliteDepot(Action<IServiceCollection>? configure = null, bool ownContexts = false)
    {
        _connection.Open();
        Services = new DepotServices(options => options.UseSqlite(_connection), Clock, configure, ownContexts);

        using var scope = Services.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<DepotContext>().Database.EnsureCreated();
        Scenario = new DepotScenario(Clock.Now);
    }

    /// <summary>The clock the access questions ask, which a test moves on.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>The depot's services.</summary>
    public DepotServices Services { get; }

    /// <summary>Every command the contexts sent since the scenario was saved.</summary>
    public CommandCounter Commands => Services.Commands;

    /// <summary>The data, as it is at the moment the clock started at.</summary>
    public DepotScenario Scenario { get; }

    /// <summary>The depot with the scenario saved, and nothing counted yet.</summary>
    public static async Task<SqliteDepot> SeededAsync(Action<IServiceCollection>? configure = null, bool ownContexts = false)
    {
        var depot = new SqliteDepot(configure, ownContexts);
        await depot.Scenario.SaveAsync(depot.Services);
        depot.Commands.Reset();
        return depot;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }
}
