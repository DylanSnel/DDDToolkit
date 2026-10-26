using DDDToolkit.EntityFramework.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>
/// The tables of the sample, read from the models of its three contexts: what a test that says "every table"
/// goes through, so a table added to a module is in that test the day it is added.
/// </summary>
public static class SampleTables
{
    /// <summary>Entity Framework's own record of the migrations that ran, one in each module's schema: no model maps it.</summary>
    public static IReadOnlyList<string> MigrationHistories { get; } =
    [
        $"{InspectionsContext.Schema}.{HistoryRepository.DefaultTableName}",
        $"{ProjectsContext.Schema}.{HistoryRepository.DefaultTableName}",
        $"{TenantsContext.Schema}.{HistoryRepository.DefaultTableName}",
    ];

    /// <summary>
    /// Every table a module's model maps, as <c>schema.Name</c>, with whether it holds a tenant's rows: all of
    /// them do but a module's outbox, which is the application's own bookkeeping.
    /// </summary>
    /// <param name="services">The services of a host on Postgres, where a table has a schema.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<IReadOnlyList<(string Name, bool OfATenant)>> OfTheModelsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var tables = new HashSet<(string Name, bool OfATenant)>();
        await AddAsync<TenantsContext>();
        await AddAsync<ProjectsContext>();
        await AddAsync<InspectionsContext>();
        return [.. tables.OrderBy(table => table.Name, StringComparer.Ordinal)];

        // The model alone is read: no statement is sent, so no caller is needed.
        async Task AddAsync<TContext>()
            where TContext : DbContext
        {
            await using var context = await services.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContextAsync(cancellationToken);
            foreach (var entity in context.Model.GetEntityTypes())
            {
                if (entity.GetTableName() is { } name)
                {
                    tables.Add(($"{entity.GetSchema()}.{name}", OfATenant: entity.ClrType != typeof(OutboxMessage)));
                }
            }
        }
    }
}
