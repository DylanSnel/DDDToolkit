using DDDToolkit.EntityFramework.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>
/// A database server in a container, started once for the whole collection.
/// <para>
/// Starting it is allowed to fail. A machine without Docker, or without the image, is a normal
/// machine, and these tests must not turn it red: the failure is captured as
/// <see cref="SkipReason"/> and every test in the collection skips with that text, so a reader can
/// tell "Docker is not running here" apart from "the convention is broken on Postgres".
/// </para>
/// <para>
/// It is allowed to fail on a laptop, that is. In CI it is not: see <see cref="RequiredContainers"/>,
/// which turns the same skip into a failure so a run that never started a database cannot report
/// green.
/// </para>
/// </summary>
public abstract class ProviderFixture : IAsyncLifetime
{
    private string? _template;

    /// <summary>The provider as a person would name it, used in skip messages.</summary>
    public abstract string ProviderName { get; }

    /// <summary>The exact image tag this fixture starts, so a test can report what it ran against.</summary>
    public abstract string Image { get; }

    /// <summary>Why the tests are skipping, or <see langword="null"/> when the server is up.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>Whether the container started and the tests can really run.</summary>
    public bool IsAvailable => SkipReason is null;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        try
        {
            _template = await StartContainerAsync();
        }
        catch (Exception exception)
        {
            SkipReason =
                $"{ProviderName} was not reachable, so this test did not run. Testcontainers could not start {Image}: " +
                $"{exception.GetType().Name}: {exception.Message} " +
                $"Start Docker Desktop (or set DOCKER_HOST) and run the suite again to exercise {ProviderName} for real.";
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await StopContainerAsync();
    }

    /// <summary>
    /// Creates a database nobody else is using and returns a handle that drops it again. Dispose the
    /// handle in the test's own teardown.
    /// </summary>
    /// <exception cref="InvalidOperationException">The container never started; check <see cref="IsAvailable"/> first.</exception>
    public async Task<ProviderDatabase> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_template is null)
        {
            throw new InvalidOperationException($"The {ProviderName} container is not running. {SkipReason}");
        }

        // Lower case and no punctuation: legal as an unquoted identifier on every provider here.
        var name = "ddd_" + Guid.NewGuid().ToString("N");
        var connectionString = ConnectionStringFor(_template, name);
        var database = new ProviderDatabase(builder => Configure(builder, connectionString));
        await database.CreateAsync(cancellationToken);
        return database;
    }

    /// <summary>Starts the container and returns a connection string to build the others from.</summary>
    protected abstract Task<string> StartContainerAsync();

    /// <summary>Stops and removes the container, if it ever started.</summary>
    protected abstract ValueTask StopContainerAsync();

    /// <summary>The same connection string pointed at <paramref name="databaseName"/>.</summary>
    protected abstract string ConnectionStringFor(string template, string databaseName);

    /// <summary>Points <paramref name="builder"/> at <paramref name="connectionString"/> with this provider.</summary>
    protected abstract void Configure(DbContextOptionsBuilder builder, string connectionString);
}
