using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Connections, with contexts from a pool. A context pool bounds how many context instances are kept, not
/// how many connections are open: that bound is the connection pool's, <c>Maximum Pool Size</c>, and a
/// context holds a connection only while a command runs, a transaction is open, or it opened one by hand.
/// So readers wait for a small connection pool instead of failing, an exhausted one fails in time and
/// recovers, and a context that is back in its pool holds nothing.
/// </summary>
[Collection(PooledContextPostgres.Name)]
public sealed class PooledContextBudgetTests(SupabaseRowLevelSecurityDatabase database)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Readers_wait_for_a_small_connection_pool_and_all_finish_each_as_itself()
    {
        database.Require();

        const string Application = "pool-small";
        const int Flows = 16;
        const int Reads = 5;
        await using var host = new PooledNotesHost(PooledNotesHost.Budgeted(database.ApplicationConnectionString, Application, maximum: 2, timeoutSeconds: 30));

        var users = Enumerable.Range(0, Flows).Select(index => (Id: Guid.NewGuid(), Team: $"team-{index}")).ToArray();
        var instances = new ConcurrentDictionary<NotesContext, bool>(ReferenceEqualityComparer.Instance);
        var answers = 0;

        // What the server sees, sampled on the owner's connection for as long as the readers run.
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var sampler = Task.Run(() => SampleBackendsAsync(Application, reading.Token), Cancellation);

        // Every flow rents its context first and reads after they all have one, so the contexts in use at
        // once outnumber the connections there are.
        var allRented = new Lineup(Flows);
        await Task.WhenAll(users.Select(user => Task.Run(
            async () =>
            {
                using (Callers.Begin(PooledNotesHost.User(user.Id, user.Team)))
                {
                    await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
                    instances.TryAdd(context, true);
                    await allRented.ArriveAsync(Cancellation);

                    for (var read = 0; read < Reads; read++)
                    {
                        var who = await PooledNotesHost.WhoAsync(context, hold: TimeSpan.FromMilliseconds(20), Cancellation);

                        who.Should().Be($"{user.Id}|authenticated|{user.Team}", "a read that waited for a connection still runs as its own caller");
                        Interlocked.Increment(ref answers);
                    }
                }
            },
            Cancellation)));

        await reading.CancelAsync();
        var (samples, most) = await sampler;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{answers} reads by {Flows} flows on {instances.Count} contexts; the server was asked {samples} times and had at most {most} connections of '{Application}'.");

        answers.Should().Be(Flows * Reads, "every reader finished: it waited for a connection instead of failing");
        instances.Count.Should().Be(Flows, "each flow held a context of its own while it waited, far more contexts than connections");
        samples.Should().BeGreaterThan(0, "the sampler has to have looked while the readers ran, or this proves nothing");
        most.Should().BeGreaterThan(0, "the sampler has to have seen the readers' connections, or this proves nothing");
        most.Should().BeLessThanOrEqualTo(2, "the connection pool's maximum is the bound, however many contexts are in use");
    }

    [Fact]
    public async Task An_exhausted_connection_pool_fails_in_time_and_recovers()
    {
        database.Require();

        await using var host = new PooledNotesHost(PooledNotesHost.Budgeted(database.ApplicationConnectionString, "pool-exhausted", maximum: 1, timeoutSeconds: 1));

        // One context holds the only connection, in a transaction.
        NotesContext holder;
        using (Callers.Begin(PooledNotesHost.User(Alice)))
        {
            holder = await host.Factory.CreateDbContextAsync(Cancellation);
            await holder.Database.BeginTransactionAsync(Cancellation);
            (await PooledNotesHost.WhoAsync(holder, cancellationToken: Cancellation)).Should().Be($"{Alice}|authenticated|");
        }

        using (Callers.Begin(PooledNotesHost.User(Bob)))
        {
            await using var waiting = await host.Factory.CreateDbContextAsync(Cancellation);
            waiting.Should().NotBeSameAs(holder, "the context pool hands out another context; it is the connection there is no second of");

            var clock = Stopwatch.StartNew();
            var read = () => PooledNotesHost.WhoAsync(waiting, cancellationToken: Cancellation);
            var failure = (await read.Should().ThrowAsync<Exception>()).Which;
            clock.Stop();

            Chain(failure).OfType<NpgsqlException>().Should().Contain(
                exception => exception.Message.Contains("connection pool has been exhausted", StringComparison.OrdinalIgnoreCase),
                "the wait ends with the connection pool's own exception, itself or inside another");
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "it fails after the pool's Timeout of a second, however busy the machine, and not after a hang");
            waiting.Database.GetDbConnection().State.Should().Be(ConnectionState.Closed);
        }

        // The holder goes back, transaction and all, and with it the connection.
        await holder.DisposeAsync();

        using (Callers.Begin(PooledNotesHost.User(Bob, "teal")))
        {
            await using var next = await host.Factory.CreateDbContextAsync(Cancellation);
            (await PooledNotesHost.WhoAsync(next, cancellationToken: Cancellation)).Should().Be($"{Bob}|authenticated|teal", "the next read gets the connection, as its own caller");
        }
    }

    [Fact]
    public async Task An_idle_pooled_context_holds_no_connection()
    {
        database.Require();

        const string Application = "pool-idle";
        const int Burst = 8;
        await using var host = new PooledNotesHost(PooledNotesHost.Budgeted(database.ApplicationConnectionString, Application, maximum: Burst, timeoutSeconds: 15));
        var used = new ConcurrentDictionary<NotesContext, bool>(ReferenceEqualityComparer.Instance);

        // A burst: every flow has its context, and its connection, at the same moment.
        var allRented = new Lineup(Burst);
        await Task.WhenAll(Enumerable.Range(0, Burst).Select(_ => Task.Run(
            async () =>
            {
                using (Callers.Begin(PooledNotesHost.User(Guid.NewGuid())))
                {
                    await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
                    used.TryAdd(context, true);
                    await context.Database.OpenConnectionAsync(Cancellation);
                    await allRented.ArriveAsync(Cancellation);
                    await PooledNotesHost.WhoAsync(context, cancellationToken: Cancellation);

                    // Given back as it is, the connection it opened by hand still open.
                }
            },
            Cancellation)));

        used.Count.Should().Be(Burst);

        // Every context of the burst, rented again and all held at once: none comes back holding a connection.
        var rented = new List<NotesContext>();
        try
        {
            for (var index = 0; index < Burst; index++)
            {
                rented.Add(await host.Factory.CreateDbContextAsync(Cancellation));
            }

            rented.Should().OnlyContain(context => used.ContainsKey(context), "the pool hands out the contexts of the burst again");
            rented.Should().OnlyContain(context => context.Database.GetDbConnection().State == ConnectionState.Closed, "a context in the pool holds no connection until its next command");
        }
        finally
        {
            foreach (var context in rented)
            {
                await context.DisposeAsync();
            }
        }

        // And the server agrees: what is left of the burst is idle in the connection pool, inside no transaction.
        (await StatesAsync(Application)).Should().OnlyContain(state => state == "idle");
    }

    /// <summary>
    /// Counts the server connections of <paramref name="application"/> again and again until cancelled, on the
    /// owner's connection, and answers how often it looked and the most it saw at once.
    /// </summary>
    private async Task<(int Samples, int Most)> SampleBackendsAsync(string application, CancellationToken until)
    {
        await using var connection = new NpgsqlConnection(database.OwnerConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT count(*)::int FROM pg_stat_activity WHERE application_name = $1", connection);
        command.Parameters.AddWithValue(application);

        var samples = 0;
        var most = 0;
        while (!until.IsCancellationRequested)
        {
            most = Math.Max(most, (int)(await command.ExecuteScalarAsync(Cancellation))!);
            samples++;

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), until);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return (samples, most);
    }

    /// <summary>The state of every server connection of <paramref name="application"/>, as the owner sees it.</summary>
    private async Task<List<string>> StatesAsync(string application)
    {
        await using var connection = new NpgsqlConnection(database.OwnerConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT coalesce(state, '') FROM pg_stat_activity WHERE application_name = $1", connection);
        command.Parameters.AddWithValue(application);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);

        var states = new List<string>();
        while (await reader.ReadAsync(Cancellation))
        {
            states.Add(reader.GetString(0));
        }

        return states;
    }

    /// <summary>Lets nobody through until <paramref name="expected"/> flows have arrived, so they all hold what they took at the same moment.</summary>
    private sealed class Lineup(int expected)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == expected)
            {
                _open.SetResult();
            }

            return _open.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
