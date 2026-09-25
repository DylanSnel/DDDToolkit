using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DDDToolkit.Messaging.Postgres;

/// <summary>
/// What one registered sink or consumer needs from its database: the extension, and with topics a
/// version that has them. Registered once per <c>AddPgmqSink</c> or <c>AddPgmqConsumer</c> call.
/// </summary>
/// <param name="Database">
/// Which database this is, so two registrations on the same one are checked with one query: the
/// connection string of a data source, or the type of a context.
/// </param>
/// <param name="Topics">Whether it routes by topic, which needs <see cref="PgmqQueue.TopicRoutingVersion"/>.</param>
/// <param name="ReadAsync">Opens a connection, reads the database's name and its pgmq version, and closes it again.</param>
internal sealed record PgmqRequirement(
    object Database,
    bool Topics,
    Func<IServiceProvider, CancellationToken, Task<(string Name, Version? Version)>> ReadAsync)
{
    /// <summary>A database reached through <paramref name="dataSource"/>.</summary>
    public static PgmqRequirement For(NpgsqlDataSource dataSource, bool topics)
        => new(dataSource.ConnectionString, topics, async (_, cancellationToken) =>
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            return (connection.Database, await PgmqQueue.InstalledVersionAsync(connection, transaction: null, cancellationToken).ConfigureAwait(false));
        });

    /// <summary>
    /// The database of <typeparamref name="TContext"/>, on the context's own connection, so whatever
    /// the context was configured with, a data source included, is what gets checked.
    /// </summary>
    public static PgmqRequirement For<TContext>(bool topics) where TContext : DbContext
        => new(typeof(TContext), topics, async (services, cancellationToken) =>
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();

            if (context.Database.GetDbConnection() is not NpgsqlConnection connection)
            {
                throw PgmqSink<TContext>.NotPostgres(context);
            }

            var opened = await PgmqQueue.EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);
            try
            {
                return (connection.Database, await PgmqQueue.InstalledVersionAsync(connection, transaction: null, cancellationToken).ConfigureAwait(false));
            }
            finally
            {
                if (opened)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
        });
}

/// <summary>
/// Checks, before anything starts, that every database a pgmq sink or consumer of this process uses has
/// the extension, and a version with topic routing where one routes by topic. Without it a missing
/// extension shows up when the first message is sent, and a pgmq too old for topics when the first one is
/// sent or the consumer binds its queue: after the application reported itself started.
/// <para>
/// <c>StartingAsync</c> runs for every lifecycle service before any hosted service's <c>StartAsync</c>, so
/// the check comes before the consumers and the outbox processor. Lifecycle services start in the order
/// they were registered, which matters for one that installs the extension itself, such as a migration
/// with <c>CREATE EXTENSION</c> run at start-up: register that one first, or turn the check off with
/// <see cref="PgmqSinkOptions.CheckExtensionOnStart"/> and <see cref="PgmqConsumerOptions.CheckExtensionOnStart"/>.
/// </para>
/// </summary>
internal sealed class PgmqStartupCheck(
    IEnumerable<PgmqRequirement> requirements,
    IServiceProvider services,
    ILogger<PgmqStartupCheck>? logger = null) : IHostedLifecycleService
{
    private readonly ILogger _logger = logger ?? NullLogger<PgmqStartupCheck>.Instance;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // One query per database, however many sinks and consumers share it.
        foreach (var database in requirements.GroupBy(requirement => requirement.Database))
        {
            var (name, version) = await database.First().ReadAsync(services, cancellationToken).ConfigureAwait(false);

            if (version is null)
            {
                throw new PgmqNotInstalledException(name);
            }

            if (version < PgmqQueue.TopicRoutingVersion && database.Any(requirement => requirement.Topics))
            {
                throw new PgmqTopicsNotSupportedException(name, version);
            }

            _logger.LogInformation("pgmq {Version} is installed in database '{Database}'.", version, name);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
