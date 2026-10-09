using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;

// Two modules for an answer that needs two lookups of one module at once, each for several keys: People owns
// workers and trades, and Shifts names one of each per shift, by key.

/// <summary>Registers the two modules.</summary>
internal static class ShiftModules
{
    public const string People = "people";

    public const string Shifts = "shifts";

    /// <summary>The module that owns workers and trades, each behind a lookup of its own.</summary>
    public static IRequestExecutorBuilder AddPeople(this IServiceCollection services)
        => services
            .AddGraphQLServer(People)
            .AddSourceSchemaDefaults()
            .AddQueryType()
            .AddTypeExtension<PeopleQueries>();

    /// <summary>The module that names a worker and a trade by their keys.</summary>
    public static IRequestExecutorBuilder AddShifts(this IServiceCollection services)
        => services
            .AddGraphQLServer(Shifts)
            .AddSourceSchemaDefaults()
            .AddQueryType()
            .AddTypeExtension<ShiftQueries>();
}

/// <summary>A worker as its owner answers it.</summary>
[GraphQLName("Worker")]
[EntityKey("id")]
public sealed record WorkerRow(int Id, string? Name);

/// <summary>A trade as its owner answers it.</summary>
[GraphQLName("Trade")]
[EntityKey("id")]
public sealed record TradeRow(int Id, string? Title);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class PeopleQueries
{
    private static readonly string[] Names = ["Jorik", "Marek", "Odile"];

    private static readonly string[] Titles = ["Welder", "Rigger", "Diver"];

    [Lookup]
    [Internal]
    public WorkerRow? GetWorkerById(int id) => id >= 1 && id <= Names.Length ? new WorkerRow(id, Names[id - 1]) : null;

    [Lookup]
    [Internal]
    public TradeRow? GetTradeById(int id) => id >= 1 && id <= Titles.Length ? new TradeRow(id, Titles[id - 1]) : null;

    /// <summary>A query field of the module's own that clients do see: a source schema needs one.</summary>
    public int GetWorkerCount() => Names.Length;
}

/// <summary>A shift, which names its worker and the trade it is worked in.</summary>
[GraphQLName("Shift")]
public sealed record ShiftRow(int Id, ReferencedWorker? Worker, ReferencedTrade? Trade);

/// <summary>A worker as Shifts knows it: its key, and nothing else.</summary>
[GraphQLName("Worker")]
[EntityKey("id")]
public sealed record ReferencedWorker(int Id);

/// <summary>A trade as Shifts knows it: its key, and nothing else.</summary>
[GraphQLName("Trade")]
[EntityKey("id")]
public sealed record ReferencedTrade(int Id);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class ShiftQueries
{
    /// <summary>Three shifts, each with a worker and a trade of its own.</summary>
    public ShiftRow[] GetShifts() =>
    [
        new(1, new ReferencedWorker(1), new ReferencedTrade(3)),
        new(2, new ReferencedWorker(2), new ReferencedTrade(1)),
        new(3, new ReferencedWorker(3), new ReferencedTrade(2)),
    ];
}
