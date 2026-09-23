# Fusion in-process spike

Why the example monolith does not (yet) compose its modules with Fusion in-process.

The idea: each module serves a GraphQL source schema of its own, with its own version of a shared type
(Catalog's `Product` and Inventory's `Product`, each with a lookup by id), and
`HotChocolate.Fusion.Connectors.InMemory` composes them in the one process and calls them directly,
with no HTTP between them. That is the model the microservices samples already use across processes.

With the published 16.6.6 packages it does not work, and this project shows the two problems apart from
the example:

```bash
dotnet run --project Tests/Spikes/DDDToolkit.Spikes.FusionInProcess -- compose
dotnet run --project Tests/Spikes/DDDToolkit.Spikes.FusionInProcess -- endpoint
```

- `compose` runs ChilliCream's own `TwoSchemas` test from `InMemoryConnectorTests` (tag 16.6.6) verbatim.
  `BuildGatewayAsync()` does not finish, and the composition reports neither a result nor an error.
- `endpoint` serves the same two source schemas through `AddGraphQLGatewayServer()` and `MapGraphQL()`.
  With more than one source schema registered, the endpoint looks the gateway up among the source schemas
  and answers "The requested schema '_Default' does not exist". With a single source schema it answers,
  but from that schema directly, not through the gateway.

Nothing on ChilliCream's GitHub mentions Fusion for a modular monolith or the in-memory connector; the
connector came in with ChilliCream/graphql-platform#9461 without a description.
