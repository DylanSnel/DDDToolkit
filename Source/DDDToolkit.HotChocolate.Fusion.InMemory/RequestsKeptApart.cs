using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using HotChocolate.Fusion.Execution;
using HotChocolate.Fusion.Execution.Clients;
using HotChocolate.Fusion.Types;

namespace DDDToolkit.HotChocolate.Fusion.InMemory;

/// <summary>
/// The in-memory connector's clients, with the requests of one batch kept apart where the connector would mix
/// them up.
/// </summary>
/// <remarks>
/// <para>
/// When one answer needs several lookups of one source schema at the same step, the gateway hands them to that
/// schema's client as one batch: the seats and the roles a list of crews names, say. HotChocolate Fusion's
/// in-memory client (16.6.6 and 16.6.7) writes the variables of every request of a batch into one buffer, and reads
/// those of a request with more than one set of variables from the start of that buffer. The first such request
/// reads its own; the second reads the first's followed by its own, which is no JSON, and the whole batch fails
/// with "Unexpected Execution Error" on every field it was to fill.
/// </para>
/// <para>
/// So a batch with more than one request of that kind is not handed over as a batch: its requests are sent one
/// after the other, each with a buffer of its own, and answered under the place they had in the batch. Every other
/// batch, and everything that is not a batch, goes to the connector's client untouched. The client can be dropped
/// once the connector reads each request's variables where it wrote them.
/// </para>
/// </remarks>
/// <param name="inner">The connector's own factory.</param>
internal sealed class RequestsKeptApart(ISourceSchemaClientFactory inner) : ISourceSchemaClientFactory
{
    /// <inheritdoc />
    public bool CanHandle(ISourceSchemaClientConfiguration configuration) => inner.CanHandle(configuration);

    /// <inheritdoc />
    public ISourceSchemaClient CreateClient(FusionSchemaDefinition schema, ISourceSchemaClientConfiguration configuration)
        => new Client(inner.CreateClient(schema, configuration));

    private sealed class Client(ISourceSchemaClient inner) : ISourceSchemaClient
    {
        public SourceSchemaClientCapabilities Capabilities => inner.Capabilities;

        public IAsyncEnumerable<SourceSchemaResult> ExecuteAsync(OperationPlanContext context, SourceSchemaClientRequest request, CancellationToken cancellationToken)
            => inner.ExecuteAsync(context, request, cancellationToken);

        public IAsyncEnumerable<SourceSchemaBatchResult> ExecuteBatchAsync(OperationPlanContext context, ImmutableArray<SourceSchemaClientRequest> requests, CancellationToken cancellationToken)
            => requests.Count(static request => request.Variables.Length > 1) > 1
                ? OneByOneAsync(context, requests, cancellationToken)
                : inner.ExecuteBatchAsync(context, requests, cancellationToken);

        public IAsyncEnumerable<SourceSchemaResult> SubscribeAsync(OperationPlanContext context, SourceSchemaClientRequest request, CancellationToken cancellationToken)
            => inner.SubscribeAsync(context, request, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();

        private async IAsyncEnumerable<SourceSchemaBatchResult> OneByOneAsync(
            OperationPlanContext context,
            ImmutableArray<SourceSchemaClientRequest> requests,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var index = 0; index < requests.Length; index++)
            {
                await foreach (var result in inner.ExecuteAsync(context, requests[index], cancellationToken).ConfigureAwait(false))
                {
                    yield return new SourceSchemaBatchResult(index, result);
                }
            }
        }
    }
}
