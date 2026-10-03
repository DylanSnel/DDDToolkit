using System.Collections.Concurrent;
using Examples.Hosting;
using Examples.Webshop.Catalog;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Webshop.Tests;

/// <summary>
/// How the samples' shared hosting opens SQLite: without the driver's connection pool, so two reads that open a
/// connection at the same moment never end up on one native connection.
/// </summary>
/// <remarks>
/// With the pool on, Microsoft.Data.Sqlite can hand one native connection to two connections that are opened
/// side by side. A request then fails with "unable to delete/modify user-function due to active statements", or
/// runs its statements inside another request's transaction. The driver loses that race about once in tens of
/// thousands of opens, so no test can wait for it: what is held here is the setting, in the connection string
/// a module's context really opens with, and what the setting gives, a native connection per open.
/// <para>
/// A module of the webshop stands in for any module: what is asked is how the hosting opens its database.
/// </para>
/// </remarks>
public sealed class SqliteConnectionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("examples-webshop-").FullName;

    [Fact]
    public async Task A_file_per_module_is_opened_without_the_drivers_pool()
    {
        await using var provider = CatalogOn(ModuleDatabase.Sqlite(_directory));
        await using var scope = provider.CreateAsyncScope();
        var opened = new SqliteConnectionStringBuilder(scope.ServiceProvider.GetRequiredService<CatalogContext>().Database.GetConnectionString());

        opened.Pooling.Should().BeFalse("a module's reads run side by side");
        opened.DataSource.Should().Be(Path.Combine(_directory, CatalogContext.Schema + ".db"));
    }

    [Fact]
    public void A_path_with_a_semicolon_stays_one_path()
    {
        var path = Path.Combine(_directory, "a;b.db");

        new SqliteConnectionStringBuilder(ModuleDatabase.SqliteConnectionString(path)).DataSource.Should().Be(path);
    }

    /// <summary>
    /// What a host that has just started is asked: many things at once, each on a connection of its own, each
    /// connection given a function when it opens, as Entity Framework's SQLite provider gives every connection
    /// its functions. No two connections that are open at the same moment hold the same native connection.
    /// </summary>
    [Fact]
    public void Connections_opened_side_by_side_each_hold_a_native_connection_of_their_own()
    {
        const int Threads = 16;
        const int OpensEach = 25;

        var connectionString = ModuleDatabase.SqliteConnectionString(Path.Combine(_directory, "burst.db"));
        using (var setup = new SqliteConnection(connectionString))
        {
            setup.Open();
            using var create = setup.CreateCommand();
            create.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY, v TEXT); INSERT INTO t(v) VALUES ('one'), ('two'), ('three');";
            create.ExecuteNonQuery();
        }

        var held = new ConcurrentDictionary<IntPtr, int>();
        var failures = new ConcurrentQueue<string>();
        using var start = new Barrier(Threads);

        var workers = Enumerable.Range(0, Threads).Select(index => new Thread(() =>
        {
            start.SignalAndWait(TestContext.Current.CancellationToken);
            for (var open = 0; open < OpensEach; open++)
            {
                try
                {
                    using var connection = new SqliteConnection(connectionString);
                    connection.CreateFunction("plus_one", (long value) => value + 1);
                    connection.Open();

                    var native = connection.Handle!.DangerousGetHandle();
                    if (!held.TryAdd(native, index))
                    {
                        failures.Enqueue($"thread {index} was handed a native connection thread {held.GetValueOrDefault(native)} still holds");
                        continue;
                    }

                    try
                    {
                        using var read = connection.CreateCommand();
                        read.CommandText = "SELECT plus_one(id) FROM t ORDER BY v";
                        using var rows = read.ExecuteReader();
                        while (rows.Read())
                        {
                            _ = rows.GetInt64(0);
                        }
                    }
                    finally
                    {
                        held.TryRemove(native, out _);
                    }
                }
                catch (SqliteException failed)
                {
                    failures.Enqueue($"thread {index}: {failed.Message}");
                }
            }
        })).ToList();

        workers.ForEach(worker => worker.Start());
        workers.ForEach(worker => worker.Join());

        failures.Should().BeEmpty("every open is a native connection of its own, so nothing one connection does is seen by another");
    }

    /// <summary>
    /// Deletes the directory the test wrote its files in. Without the pool a file is closed with the last
    /// connection that used it, so nothing is left to clear first; a delete that fails all the same leaves the
    /// directory to the system's temporary folder rather than failing a test that has passed.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The Catalog module's services on <paramref name="database"/>, as a host registers them. Nothing is started.</summary>
    private static ServiceProvider CatalogOn(ModuleDatabase database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatalogModule(ModuleHost.InProcess(database));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
