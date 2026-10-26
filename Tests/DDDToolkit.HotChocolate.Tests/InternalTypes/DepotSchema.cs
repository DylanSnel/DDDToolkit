using DDDToolkit.Exceptions;
using GreenDonut;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.HotChocolate.Tests.InternalTypes;

// A source schema the way a module writes one when the module exports a single type, its entry: the type
// extensions, the records they answer, the data loader and the registration are all internal. Nothing here is
// public on purpose. The file is compiled into DDDToolkit.HotChocolate.Fusion.InMemory.Tests as well, where the
// same types are composed with a second schema.

/// <summary>What registers the depot's source schema: the one thing its module would call.</summary>
internal static class DepotSchema
{
    /// <summary>The code of a rename without a name.</summary>
    public const string NameRequired = "depots.name-required";

    /// <summary>A source schema of internal types only, with a lookup, a key, a data loader and the mutation conventions.</summary>
    public static IRequestExecutorBuilder AddDepotSchema(this IRequestExecutorBuilder builder)
        => builder
            .AddSourceSchemaDefaults()
            .AddDDDToolkitErrors()
            .AddDDDToolkitMutationConventions()
            .AddQueryType()
            .AddMutationType()
            .AddTypeExtension<DepotQueries>()
            .AddTypeExtension<DepotMutations>()
            .AddObjectType<DepotOutput>()   // HotChocolate infers an object type from a public class only
            .AddDataLoader<DepotLoader>();
}

/// <summary>The depot's lookup: one nullable object by its key, through a data loader.</summary>
[ExtendObjectType(OperationTypeNames.Query)]
internal sealed class DepotQueries
{
    [Lookup]
    public Task<DepotOutput?> GetDepotAsync(int id, DepotLoader depots, CancellationToken cancellationToken)
        => depots.LoadAsync(id, cancellationToken);
}

/// <summary>The depot's one command, which refuses a blank name.</summary>
[ExtendObjectType(OperationTypeNames.Mutation)]
internal sealed class DepotMutations
{
    public DepotOutput DepotRename(int id, string name)
        => string.IsNullOrWhiteSpace(name)
            ? throw new RefusalException(
                DepotSchema.NameRequired,
                RefusalKind.Invalid,
                "A depot needs a name.",
                new Dictionary<string, object?> { [RefusalException.FieldArgument] = "name" })
            : new DepotOutput(id, name);
}

/// <summary>A depot as its module answers it.</summary>
[GraphQLName("Depot")]
[EntityKey("id")]
internal sealed record DepotOutput(int Id, string Name);

/// <summary>Reads depots by their ids, as many as one batch asks for. Depot 1 is North, depot 2 South, and there is no other.</summary>
internal sealed class DepotLoader(IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<int, DepotOutput>(batchScheduler, options)
{
    private static readonly Dictionary<int, string> Names = new() { [1] = "North", [2] = "South" };

    protected override Task<IReadOnlyDictionary<int, DepotOutput>> LoadBatchAsync(IReadOnlyList<int> keys, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<int, DepotOutput>>(
            keys.Where(Names.ContainsKey).ToDictionary(key => key, key => new DepotOutput(key, Names[key])));
}
