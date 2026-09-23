# Fusion in-process spike

Why the example monolith does not (yet) compose its modules with Fusion in-process.

The idea: each module serves a GraphQL source schema of its own, with its own version of a shared type
(Catalog's `Product` and Inventory's `Product`, each with a lookup by id), and
`HotChocolate.Fusion.Connectors.InMemory` composes them in the one process and calls them directly,
with no HTTP between them. That is the model the microservices samples already use across processes.

```bash
dotnet run --project Tests/Spikes/DDDToolkit.Spikes.FusionInProcess -- compose
dotnet run --project Tests/Spikes/DDDToolkit.Spikes.FusionInProcess -- endpoint
dotnet run --project Tests/Spikes/DDDToolkit.Spikes.FusionInProcess -- endpoint-gateway-first
```

With the published 16.6.6 packages, and the 16.7.0-p.10 preview:

- `compose` works: two source schemas, each with its own `Product` keyed on `id`, composed in memory and
  queried through the gateway (`BuildGatewayAsync()`), with no HTTP.
- `endpoint` fails. With more than one source schema registered, `MapGraphQL()` asks HotChocolate's
  executor manager for the gateway and gets "The requested schema '_Default' does not exist". HotChocolate
  and Fusion both register the one `IRequestExecutorProvider` with `TryAdd`, and the source schemas come
  first. With a single source schema it answers, but from that schema directly, not through the gateway.
- `endpoint-gateway-first` does not start: with the gateway registered first, Fusion's manager answers
  for every name, and the in-memory connector can no longer reach the source schemas.

Two things to know when trying this:

- **The root query type of every source schema must be called `Query`.** `AddQueryType<ProductsQuery>()`
  names it `ProductsQuery`, and composition refuses it with `ROOT_QUERY_USED`.
- **A composition error is swallowed.** The in-memory connector reports it only to observers subscribed at
  that moment and keeps nothing, so the gateway waits for a schema forever: `BuildGatewayAsync()` hangs,
  and so does an application's start, with nothing in the logs. When it hangs, compose the source schemas
  yourself to see why.

Nothing on ChilliCream's GitHub mentions Fusion for a modular monolith or the in-memory connector; the
connector came in with ChilliCream/graphql-platform#9461 without a description.
