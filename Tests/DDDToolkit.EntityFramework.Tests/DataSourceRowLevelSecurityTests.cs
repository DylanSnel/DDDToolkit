using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// Row level security on a context that takes its connections from an <see cref="NpgsqlDataSource"/>, which is how
/// a host gives each purpose a pool of its own with limits of its own: the caller, its claims and the modules'
/// settings are set on every connection the context opens, as they are on one opened from a connection string,
/// and nothing of them stays on a connection that goes back to the data source.
/// </summary>
public sealed class DataSourceRowLevelSecurityTests(ExplicitCallersPostgres postgres)
{
    private const string Who = """SELECT current_user::text || '|' || coalesce(ddd.caller_id()::text, 'none') || '|' || coalesce(ddd.caller_claims() ->> 'scope', '') AS "Value" """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_context_on_a_data_source_runs_as_the_caller()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);
        await using var dataSource = NpgsqlDataSource.Create(Budgeted(database, maximum: 1));
        await using var host = database.BuildHost(dataSource: dataSource);

        // One connection in the pool, so every caller below is given the very same one.
        (await WhoAsync(host, ApiaryDatabase.Alice)).Should().Be($"authenticated|{ApiaryDatabase.AliceId}|");
        (await WhoAsync(host, ApiaryDatabase.Bob)).Should().Be($"authenticated|{ApiaryDatabase.BobId}|");
        (await WhoAsync(host, Caller.Anonymous)).Should().Be("anon|none|");
        (await WhoAsync(host, ApiaryDatabase.Ranger)).Should().StartWith("apiary_ranger|");
        (await WhoAsync(host, Caller.SystemIn("apiary"))).Should().Be("ddd_system_in|none|apiary");
        (await WhoAsync(host, Caller.System)).Should().Be("ddd_system|none|");

        // A save as a caller: the hive, and the outbox row of its event, in the caller's own transaction.
        await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, async context =>
        {
            context.Hives.Add(new Hive(HiveId.CreateSequential(), number: 1, "By the hedge", ApiaryDatabase.AliceId, isOpen: false));
            await context.SaveChangesAsync(Cancellation);
        });
        (await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Bob, context => context.Hives.CountAsync(Cancellation))).Should().Be(1);
        (await ApiaryDatabase.AsAsync(host, Caller.Anonymous, context => context.Hives.CountAsync(Cancellation))).Should().Be(0, "the hive is closed to visitors");

        // Code that takes a connection from the data source itself gets the same one, and nothing of any caller.
        await using var plain = await dataSource.OpenConnectionAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT current_user::text || '|' || coalesce(current_setting('request.jwt.claims', true), '')", plain);
        (await command.ExecuteScalarAsync(Cancellation)).Should().Be($"{ApiaryDatabase.LoginRole}|", "the data source resets a connection before it hands it out again");
    }

    [Fact]
    public async Task A_data_source_connection_carries_the_extra_settings()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);
        await using var dataSource = NpgsqlDataSource.Create(Budgeted(database, maximum: 1));
        await using var host = database.BuildHost(services => services.AddRowLevelSecuritySettings<TeamOfTheCaller>(), dataSource: dataSource);
        const string team = $"""SELECT coalesce(current_setting('{TeamOfTheCaller.Setting}', true), '') AS "Value" """;

        var north = Callers.FromClaims($$"""{"sub":"{{ApiaryDatabase.AliceId}}","role":"authenticated","team":"north"}""");
        (await ApiaryDatabase.AsAsync(host, north, context => context.Database.SqlQueryRaw<string>(team).SingleAsync(Cancellation))).Should().Be("north", "a module's setting travels with the caller, in the same statement");
        (await ApiaryDatabase.AsAsync(host, ApiaryDatabase.Bob, context => context.Database.SqlQueryRaw<string>(team).SingleAsync(Cancellation))).Should().Be("", "and is empty for a caller without one, on the same connection");

        await using var plain = await dataSource.OpenConnectionAsync(Cancellation);
        await using var command = new NpgsqlCommand($"SELECT coalesce(current_setting('{TeamOfTheCaller.Setting}', true), '')", plain);
        (await command.ExecuteScalarAsync(Cancellation)).Should().Be("", "the reset takes the settings off with the role");
    }

    [Fact]
    public async Task A_multiplexing_data_source_is_refused()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);
        await using var dataSource = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString) { Multiplexing = true }.ConnectionString);
        await using var host = database.BuildHost(dataSource: dataSource);

        var read = () => ApiaryDatabase.AsAsync(host, ApiaryDatabase.Alice, context => context.Hives.CountAsync(Cancellation));

        (await read.Should().ThrowAsync<InvalidOperationException>("one connection would carry many callers' commands at once"))
            .WithMessage("Row level security sets the caller's role and claims on the connection when a context opens it. Multiplexing*Turn Multiplexing off.");
    }

    [Fact]
    public async Task A_caller_change_inside_a_transaction_on_a_data_source_is_refused()
    {
        var database = await ApiaryDatabase.CreateAsync(postgres);
        await using var dataSource = NpgsqlDataSource.Create(Budgeted(database, maximum: 2));
        await using var host = database.BuildHost(dataSource: dataSource);

        await using var scope = host.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApiaryContext>();

        using (Callers.Begin(ApiaryDatabase.Alice))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
            (await context.Database.SqlQueryRaw<string>(Who).SingleAsync(Cancellation)).Should().StartWith("authenticated|" + ApiaryDatabase.AliceId);

            using (Callers.Begin(ApiaryDatabase.Bob))
            {
                var asBob = () => context.Database.SqlQueryRaw<string>(Who).SingleAsync(Cancellation);
                (await asBob.Should().ThrowAsync<InvalidOperationException>("a transaction runs as one caller"))
                    .WithMessage($"The caller changed from authenticated {ApiaryDatabase.AliceId} to authenticated {ApiaryDatabase.BobId} while a transaction was open on this connection.*");
            }

            // The transaction goes on as the caller it began with.
            (await context.Database.SqlQueryRaw<string>(Who).SingleAsync(Cancellation)).Should().StartWith("authenticated|" + ApiaryDatabase.AliceId);
            await transaction.CommitAsync(Cancellation);
        }
    }

    /// <summary>The application's connection string with at most <paramref name="maximum"/> connections in its pool.</summary>
    private static string Budgeted(ApiaryDatabase database, int maximum)
        => new NpgsqlConnectionStringBuilder(database.ApplicationConnectionString) { MinPoolSize = 0, MaxPoolSize = maximum }.ConnectionString;

    private static Task<string> WhoAsync(IServiceProvider host, Caller caller)
        => ApiaryDatabase.AsAsync(host, caller, context => context.Database.SqlQueryRaw<string>(Who).SingleAsync(Cancellation));
}
