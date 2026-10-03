using System.Collections.Concurrent;
using System.Data;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static DDDToolkit.EntityFramework.Tests.Infrastructure.SupabaseRowLevelSecurityDatabase;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Row level security on contexts taken from a context pool, against a real Postgres. A pool shares one
/// interceptor between every context and every caller, and hands one context, with the connection object it
/// keeps, to one caller after another. What is held here: each rental runs as its own caller, side by side
/// and one after the other, and nothing of a caller is left for the next, whatever state the context was
/// given back in.
/// </summary>
[Collection(PooledContextPostgres.Name)]
public sealed class PooledContextRowLevelSecurityTests(SupabaseRowLevelSecurityDatabase database) : IAsyncLifetime
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (database.Available)
        {
            await database.ClearAsync();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Contexts_of_one_pool_run_as_their_own_callers_side_by_side()
    {
        database.Require();
        await using var host = new PooledNotesHost(database.ApplicationConnectionString);

        const int Flows = 12;
        const int Rounds = 4;
        var users = Enumerable.Range(0, 4).Select(index => (Id: Guid.NewGuid(), Team: $"team-{index}")).ToArray();
        var instances = new ConcurrentDictionary<NotesContext, bool>(ReferenceEqualityComparer.Instance);

        await Task.WhenAll(Enumerable.Range(0, Flows).Select(flow => Task.Run(
            async () =>
            {
                var (id, team) = users[flow % users.Length];

                for (var round = 0; round < Rounds; round++)
                {
                    using (Callers.Begin(PooledNotesHost.User(id, team)))
                    {
                        await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
                        instances.TryAdd(context, true);

                        // Held on the server for a moment, so the statements of the flows overlap.
                        var who = await PooledNotesHost.WhoAsync(context, hold: TimeSpan.FromMilliseconds(50), Cancellation);

                        who.Should().Be($"{id}|authenticated|{team}", "a rental runs as the caller of its own flow, whoever else is using the pool");
                    }
                }
            },
            Cancellation)));

        TestContext.Current.TestOutputHelper?.WriteLine($"{Flows * Rounds} rentals by {Flows} flows were served by {instances.Count} context instances.");

        instances.Count.Should().BeGreaterThan(1, "the flows ran side by side, on contexts of their own");
        instances.Count.Should().BeLessThan(Flows * Rounds, "and the pool handed the same instances out again and again");
    }

    [Fact]
    public async Task A_context_rented_again_runs_as_the_next_caller()
    {
        database.Require();
        await using var host = new PooledNotesHost(database.ApplicationConnectionString);
        NotesContext? first = null;

        async Task<string> AsAsync(Caller caller)
        {
            using (Callers.Begin(caller))
            {
                await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
                first ??= context;
                context.Should().BeSameAs(first, "one renter at a time, so the pool hands out the one instance it has");

                return await PooledNotesHost.WhoAsync(context, cancellationToken: Cancellation);
            }
        }

        (await AsAsync(PooledNotesHost.User(Alice, "amber"))).Should().Be($"{Alice}|authenticated|amber");
        (await AsAsync(PooledNotesHost.User(Bob, "teal"))).Should().Be($"{Bob}|authenticated|teal");
        (await AsAsync(Caller.Anonymous)).Should().Be("none|anon|");
        (await AsAsync(Caller.System)).Should().Be($"none|{SupabaseRowLevelSecurity.ServiceRole}|");
        (await AsAsync(PooledNotesHost.User(Alice, "amber"))).Should().Be($"{Alice}|authenticated|amber", "and back to a user after the system");
    }

    [Fact]
    public async Task A_context_given_back_with_its_connection_open_serves_the_next_caller_as_itself()
    {
        database.Require();
        await using var host = new PooledNotesHost(database.ApplicationConnectionString);

        NotesContext first;
        NpgsqlConnection connection;
        using (Callers.Begin(PooledNotesHost.User(Alice, "amber")))
        {
            first = await host.Factory.CreateDbContextAsync(Cancellation);
            await first.Database.OpenConnectionAsync(Cancellation);
            connection = (NpgsqlConnection)first.Database.GetDbConnection();
            (await PooledNotesHost.WhoAsync(first, cancellationToken: Cancellation)).Should().Be($"{Alice}|authenticated|amber");
            connection.State.Should().Be(ConnectionState.Open);

            // Given back without closing what it opened.
            await first.DisposeAsync();
        }

        using (Callers.Begin(PooledNotesHost.User(Bob)))
        {
            await using var next = await host.Factory.CreateDbContextAsync(Cancellation);
            next.Should().BeSameAs(first);

            var state = next.Database.GetDbConnection().State;
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"A context given back with its connection open is rented again with the connection {state}; the connection object is {(ReferenceEquals(next.Database.GetDbConnection(), connection) ? "the same" : "another")}.");

            state.Should().Be(ConnectionState.Closed, "Entity Framework closes the connection a context left open when it goes back, so an idle pooled context holds none");
            next.Database.GetDbConnection().Should().BeSameAs(connection, "a pooled context keeps its connection object from one rental to the next");
            (await PooledNotesHost.WhoAsync(next, cancellationToken: Cancellation)).Should().Be($"{Bob}|authenticated|", "the next open sets the next caller, and empties the setting the one before had");
        }
    }

    [Fact]
    public async Task A_context_given_back_inside_a_transaction_rolls_it_back_and_serves_the_next_caller()
    {
        database.Require();
        await using var host = new PooledNotesHost(database.ApplicationConnectionString);
        var id = Guid.CreateVersion7();

        NotesContext first;
        using (Callers.Begin(PooledNotesHost.User(Alice)))
        {
            first = await host.Factory.CreateDbContextAsync(Cancellation);
            await first.Database.BeginTransactionAsync(Cancellation);
            first.Notes.Add(new PrivateNote { Id = id, Text = "Never committed" });
            await first.SaveChangesAsync(Cancellation);
            (await first.Notes.CountAsync(Cancellation)).Should().Be(1, "inside its transaction the row is there");

            // Given back with the transaction still open.
            await first.DisposeAsync();
        }

        using (Callers.Begin(PooledNotesHost.User(Bob)))
        {
            await using var next = await host.Factory.CreateDbContextAsync(Cancellation);
            next.Should().BeSameAs(first);
            next.Database.CurrentTransaction.Should().BeNull();
            next.ChangeTracker.Entries().Should().BeEmpty();

            // This host requires explicit callers, where a caller that changes inside a transaction is refused:
            // the transaction of the renter before is gone, so this is a first command, not a change.
            (await PooledNotesHost.WhoAsync(next, cancellationToken: Cancellation)).Should().Be($"{Bob}|authenticated|");
        }

        (await OwnersAsync()).Should().BeEmpty("giving the context back rolled the transaction back");
    }

    [Fact]
    public async Task Settings_follow_each_rental_and_are_empty_for_the_next()
    {
        database.Require();

        // One server connection, so every open below gets the same one.
        var connectionString = PooledNotesHost.Budgeted(database.ApplicationConnectionString, "pool-settings", maximum: 1, timeoutSeconds: 15);
        await using var host = new PooledNotesHost(connectionString);

        string backend;
        NotesContext first;
        using (Callers.Begin(PooledNotesHost.User(Alice, "amber")))
        {
            await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
            first = context;
            (await PooledNotesHost.WhoAsync(context, cancellationToken: Cancellation)).Should().Be($"{Alice}|authenticated|amber");
            backend = await BackendAsync(context);
        }

        using (Callers.Begin(PooledNotesHost.User(Bob)))
        {
            await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
            context.Should().BeSameAs(first);
            (await PooledNotesHost.WhoAsync(context, cancellationToken: Cancellation)).Should().Be($"{Bob}|authenticated|", "a caller without a team gets an empty setting, not the team of the renter before");
            (await BackendAsync(context)).Should().Be(backend, "on the very same server connection");
        }

        using (Callers.Begin(PooledNotesHost.User(Alice, "teal")))
        {
            await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
            (await PooledNotesHost.WhoAsync(context, cancellationToken: Cancellation)).Should().Be($"{Alice}|authenticated|teal");
        }

        // Code that does not go through the interceptor gets that server connection with none of it.
        await using var plain = new NpgsqlConnection(connectionString);
        await plain.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand(
            $"SELECT pg_backend_pid()::text || '|' || current_user || '|' || coalesce(current_setting('{TeamOfTheCaller.Setting}', true), '') || '|' || coalesce(current_setting('request.jwt.claims', true), '')",
            plain);

        (await command.ExecuteScalarAsync(Cancellation)).Should().Be($"{backend}|{LoginRole}||", "the connection pool's reset takes the role, the claims and the settings off");
    }

    [Fact]
    public async Task A_save_from_a_pooled_context_runs_as_its_caller()
    {
        database.Require();
        await using var host = new PooledNotesHost(database.ApplicationConnectionString);

        NotesContext first;
        using (Callers.Begin(PooledNotesHost.User(Alice)))
        {
            await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
            first = context;
            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's" });
            await context.SaveChangesAsync(Cancellation);
        }

        using (Callers.Begin(PooledNotesHost.User(Bob)))
        {
            await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
            context.Should().BeSameAs(first);
            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Bob's" });
            await context.SaveChangesAsync(Cancellation);

            (await context.Notes.Select(note => note.Text).ToListAsync(Cancellation)).Should().Equal(["Bob's"], "and what it reads is its caller's too");
        }

        // The column's default is auth.uid(): each row is whose save it was.
        (await OwnersAsync()).Should().BeEquivalentTo(new Dictionary<string, Guid?> { ["Alice's"] = Alice, ["Bob's"] = Bob });

        // The whole pipeline is there on the pooled context: a row the policy denies is answered with a code.
        using (Callers.Begin(PooledNotesHost.User(Bob)))
        {
            await using var context = await host.Factory.CreateDbContextAsync(Cancellation);
            context.Notes.Add(new PrivateNote { Id = Guid.CreateVersion7(), Text = "Alice's, says Bob", Owner = Alice });

            var save = () => context.SaveChangesAsync(Cancellation);
            (await save.Should().ThrowAsync<RefusalException>()).Which.Code.Should().Be(ToolkitRefusals.Refused);
        }
    }

    /// <summary>Every note's text and owner, read as the tables' owner, past row level security.</summary>
    private async Task<Dictionary<string, Guid?>> OwnersAsync()
    {
        await using var connection = new NpgsqlConnection(database.OwnerConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand("""SELECT "Text", "Owner" FROM notes."Notes" """, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancellation);

        var owners = new Dictionary<string, Guid?>();
        while (await reader.ReadAsync(Cancellation))
        {
            owners[reader.GetString(0)] = await reader.IsDBNullAsync(1, Cancellation) ? null : reader.GetGuid(1);
        }

        return owners;
    }

    private static async Task<string> BackendAsync(NotesContext context)
    {
#pragma warning disable EF1003
        return await context.Database.SqlQueryRaw<string>("""SELECT pg_backend_pid()::text AS "Value" """).SingleAsync(Cancellation);
#pragma warning restore EF1003
    }
}
