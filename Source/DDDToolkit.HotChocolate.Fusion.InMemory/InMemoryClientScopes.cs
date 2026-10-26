using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Types;
using HotChocolate.Transport.Formatters;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// The clients every gateway of the application calls its source schemas with: in memory, on the application's own
/// executors, with the requests of a batch kept apart where the connector would mix them up.
/// </summary>
/// <remarks>
/// A gateway answers a request with the request's services, which are the application's: a resolver of a module
/// runs in the application's scope, not in one of the gateway's container. So the application's container holds
/// what the gateway asks those services for, the factory of its client scopes. One serves every gateway: a scope is
/// made for the composed schema it is given, and which source schemas it reaches, and under which names, that
/// schema says. Nothing of it belongs to one gateway.
/// </remarks>
internal sealed class InMemoryClientScopes(IRequestExecutorProvider sourceSchemas, IRequestExecutorEvents sourceSchemaEvents) : ISourceSchemaClientScopeFactory
{
    private readonly ISourceSchemaClientFactory[] _clients =
        [new RequestsKeptApart(new InMemorySourceSchemaClientFactory(sourceSchemas, sourceSchemaEvents, JsonResultFormatter.Default))];

    /// <inheritdoc />
    public ISourceSchemaClientScope CreateScope(ISchemaDefinition schemaDefinition)
        => schemaDefinition is FusionSchemaDefinition composed
            ? new DefaultSourceSchemaClientScope(composed, _clients)
            : throw new ArgumentException("The schema is not one a gateway composed.", nameof(schemaDefinition));
}
