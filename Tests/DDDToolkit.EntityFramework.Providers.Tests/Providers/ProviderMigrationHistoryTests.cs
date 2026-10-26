using DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Providers.Tests.Providers;

/// <summary>
/// Two modules in one database, each in a schema of its own, each migrated by a context wired with
/// <c>UseDDDToolkit</c> and nothing that names a history table: each keeps its migration history in its own schema,
/// reads its own migration as applied and not the other's, and the context a design-time factory makes with
/// <c>UseDDDToolkitDesignTime</c> reads the same history. Left to Entity Framework both histories would be one table in
/// the provider's default schema.
/// </summary>
public abstract class ProviderMigrationHistoryTests(ProviderFixture fixture) : ProviderTestBase(fixture)
{
    /// <summary>Where the provider keeps a history that nothing places: the schema it writes an unqualified name in.</summary>
    protected abstract string ProvidersDefaultSchema { get; }

    [Fact]
    public async Task Two_modules_in_one_database_keep_a_migration_history_each_in_their_own_schema()
    {
        SkipIfUnavailable();
        await using var services = new ServiceCollection().AddDDDToolkitEntityFramework().BuildServiceProvider();

        await using (var stockroom = Running<StockroomContext>(services))
        {
            await stockroom.Database.MigrateAsync(Cancellation);
        }

        await using (var counter = Running<CounterContext>(services))
        {
            await counter.Database.MigrateAsync(Cancellation);
        }

        (await HistorySchemasAsync()).Should().BeEquivalentTo(
            [StockroomContext.Schema, CounterContext.Schema],
            "each module keeps a history of its own, and none is in {0}", ProvidersDefaultSchema);

        await using (var stockroom = Running<StockroomContext>(services))
        {
            (await stockroom.Database.GetAppliedMigrationsAsync(Cancellation)).Should().Equal([CreateStockroom.Id], "the stockroom reads its own migration, and not the counter's");
        }

        await using (var counter = Running<CounterContext>(services))
        {
            (await counter.Database.GetAppliedMigrationsAsync(Cancellation)).Should().Equal([CreateCounter.Id]);
        }

        await using var designTime = DesignTime<StockroomContext>();
        (await designTime.Database.GetPendingMigrationsAsync(Cancellation)).Should().BeEmpty("dotnet ef reads the history the application wrote");
    }

    /// <summary>A module's context as the host wires it: the provider, then the one call.</summary>
    private TContext Running<TContext>(IServiceProvider services)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        Database.Configure(options);
        options.UseDDDToolkit(services);
        return Create(options);
    }

    /// <summary>A module's context as its design-time factory makes it: the provider, and the call that needs no services.</summary>
    private TContext DesignTime<TContext>()
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        Database.Configure(options);
        return Create(options.UseDDDToolkitDesignTime());
    }

    private static TContext Create<TContext>(DbContextOptionsBuilder<TContext> options)
        where TContext : DbContext
        => (TContext)Activator.CreateInstance(typeof(TContext), options.Options)!;

    /// <summary>The schemas the database has a migration history table in.</summary>
    private async Task<List<string>> HistorySchemasAsync()
    {
        await using var context = Database.CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(Cancellation);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT table_schema FROM information_schema.tables WHERE table_name = '{HistoryRepository.DefaultTableName}'";

        List<string> schemas = [];
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        while (await reader.ReadAsync(Cancellation))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas;
    }
}
