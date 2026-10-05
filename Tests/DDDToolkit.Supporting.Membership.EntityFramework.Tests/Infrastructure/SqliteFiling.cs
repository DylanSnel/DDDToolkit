using Microsoft.Data.Sqlite;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>
/// The application over one SQLite database in memory, of the calling test's own. The connection stays open
/// for as long as the test runs (closing it would drop the database), and every context uses it, so a context
/// made for one reading sees what the request's own context wrote.
/// </summary>
public sealed class SqliteFiling : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <param name="configure">Registers what a test needs before the resources are registered.</param>
    /// <param name="ownContexts">Whether the host registers a factory for its context.</param>
    /// <param name="wiring">What a context is given besides the toolkit's interceptors.</param>
    public SqliteFiling(Action<IServiceCollection>? configure = null, bool ownContexts = false, Action<DbContextOptionsBuilder, IServiceProvider>? wiring = null)
    {
        _connection.Open();
        Services = new FilingServices(options => options.UseSqlite(_connection), Clock, configure, ownContexts, wiring);

        using var scope = Services.Provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<FilingContext>().Database.EnsureCreated();
        Scenario = new FilingScenario(Clock.Now);
    }

    /// <summary>The clock the access questions ask, which a test moves on.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>The application's services.</summary>
    public FilingServices Services { get; }

    /// <summary>Every command the contexts sent since the scenario was saved.</summary>
    public CommandCounter Commands => Services.Commands;

    /// <summary>The data, as it is at the moment the clock started at.</summary>
    public FilingScenario Scenario { get; }

    /// <summary>The application with the scenario saved, and nothing counted yet.</summary>
    public static async Task<SqliteFiling> SeededAsync(Action<IServiceCollection>? configure = null, bool ownContexts = false, Action<DbContextOptionsBuilder, IServiceProvider>? wiring = null)
    {
        var filing = new SqliteFiling(configure, ownContexts, wiring);
        await filing.Scenario.SaveAsync(filing.Services);
        filing.Commands.Reset();
        return filing;
    }

    /// <summary>The access questions about documents, asked as <paramref name="caller"/>.</summary>
    public Task<T> DocumentsAsync<T>(Caller caller, Func<IMemberQuestions<DocumentId>, Task<T>> ask)
        => Services.AsAsync(caller, provider => ask(provider.GetRequiredService<IMemberQuestions<DocumentId>>()));

    /// <summary>The access questions about folders, asked as <paramref name="caller"/>.</summary>
    public Task<T> FoldersAsync<T>(Caller caller, Func<IMemberQuestions<FolderId>, Task<T>> ask)
        => Services.AsAsync(caller, provider => ask(provider.GetRequiredService<IMemberQuestions<FolderId>>()));

    /// <inheritdoc />
    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }
}
