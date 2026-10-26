using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Composite;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;

// A second module of internal types, beside the depot's: it knows pallets, and of a pallet's depot only the key.
// The depot's name is the depot module's to give, and the gateway fetches it there.

/// <summary>What registers the yard's source schema.</summary>
internal static class YardSchema
{
    public const string Depots = "depots";

    public const string Yard = "yard";

    public static IRequestExecutorBuilder AddYardSchema(this IRequestExecutorBuilder builder)
        => builder
            .AddSourceSchemaDefaults()
            .AddDDDToolkitErrors()
            .AddDDDToolkitMutationConventions()
            .AddQueryType()
            .AddTypeExtension<YardQueries>()
            .AddObjectType<PalletOutput>()        // HotChocolate infers an object type from a public class only
            .AddObjectType<ReferencedDepot>();
}

[ExtendObjectType(OperationTypeNames.Query)]
internal sealed class YardQueries
{
    /// <summary>Two pallets, each in a depot that exists.</summary>
    public PalletOutput[] GetPallets() => [new(1, new ReferencedDepot(1)), new(2, new ReferencedDepot(2))];

    /// <summary>A pallet that names depot 9, which the depot module does not know.</summary>
    public PalletOutput GetStrayPallet() => new(3, new ReferencedDepot(9));
}

/// <summary>A pallet as the yard answers it.</summary>
[GraphQLName("Pallet")]
internal sealed record PalletOutput(int Id, ReferencedDepot? Depot);

/// <summary>A depot as the yard knows it: its key, and nothing else.</summary>
[GraphQLName("Depot")]
[EntityKey("id")]
internal sealed record ReferencedDepot(int Id);
