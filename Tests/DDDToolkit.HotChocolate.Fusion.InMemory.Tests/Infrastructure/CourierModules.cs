using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Composite;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.HotChocolate.Fusion.InMemory.Tests.Infrastructure;

// Two modules for a lookup no client is offered: Couriers owns a courier's name and phone, and its lookup is
// there for the gateway alone. Routes names a courier by its key, and the gateway fetches the rest.

/// <summary>Registers the two modules.</summary>
internal static class CourierModules
{
    public const string Couriers = "couriers";

    public const string Routes = "routes";

    /// <summary>The module that owns couriers. Its only lookup is internal.</summary>
    public static IRequestExecutorBuilder AddCouriers(this IServiceCollection services)
    {
        services.TryAddSingleton<SourceCalls>();

        return services
            .AddGraphQLServer(Couriers)
            .AddSourceSchemaDefaults()
            .AddQueryType()
            .AddTypeExtension<CourierQueries>()
            .AddDiagnosticEventListener(schema => new CountsCalls(Couriers, schema.GetRootServiceProvider().GetRequiredService<SourceCalls>()));
    }

    /// <summary>The module that names a courier by its key.</summary>
    public static IRequestExecutorBuilder AddRoutes(this IServiceCollection services)
        => services
            .AddGraphQLServer(Routes)
            .AddSourceSchemaDefaults()
            .AddQueryType()
            .AddTypeExtension<RouteQueries>();
}

/// <summary>A courier as its owner answers it: a name that is never null, and a phone that may be.</summary>
[GraphQLName("Courier")]
[EntityKey("id")]
public sealed record CourierRow(int Id, string Name, string? Phone);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class CourierQueries
{
    private static readonly Dictionary<int, CourierRow> Known = new()
    {
        [1] = new CourierRow(1, "Jorik", "555-0101"),
        [2] = new CourierRow(2, "Marek", null),
    };

    /// <summary>
    /// The lookup the gateway resolves a reference to a courier by. It is no field of the composed schema, and
    /// it answers nothing for a courier that is not there, or that the caller may not read.
    /// </summary>
    [Lookup]
    [Internal]
    public CourierRow? GetCourierById(int id) => Known.GetValueOrDefault(id);

    /// <summary>A query field of the module's own that clients do see: a source schema needs one.</summary>
    public int GetCourierCount() => Known.Count;
}

/// <summary>A route, which names its courier.</summary>
[GraphQLName("Route")]
public sealed record RouteRow(int Id, ReferencedCourier? Courier);

/// <summary>A courier as Routes knows it: its key, and nothing else.</summary>
[GraphQLName("Courier")]
[EntityKey("id")]
public sealed record ReferencedCourier(int Id);

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class RouteQueries
{
    /// <summary>Two routes, each with a courier its owner knows.</summary>
    public RouteRow[] GetRoutes() => [new(1, new ReferencedCourier(1)), new(2, new ReferencedCourier(2))];

    /// <summary>A route that names courier 9, for whom the owner's lookup answers nothing.</summary>
    public RouteRow GetStrayRoute() => new(3, new ReferencedCourier(9));
}
