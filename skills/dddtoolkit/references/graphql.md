# GraphQL with HotChocolate

Package `DDDToolkit.HotChocolate`, and `DDDToolkit.HotChocolate.Fusion.InMemory` for one schema over the
modules of a monolith. Both ask for HotChocolate 16.6.6 or later, and every HotChocolate package of an
application has the same version. Full page:
`https://dylansnel.github.io/DDDToolkit/docs/graphql.md`. The rule: do not build what HotChocolate
already offers. The toolkit adds bindings and conventions; types, loaders, paging and authorization are
HotChocolate's own.

## Registering

```csharp
using DDDToolkit.HotChocolate;
using Ordering.Domain.GraphQl;                       // generated: {AssemblyName}.GraphQl

builder.Services
    .AddGraphQLServer()
    .AddDDDToolkitTypes()                            // once per schema: [Internal] members and methods are no fields
    .AddOrderingGraphQlRuntimeBindings()             // generated, per assembly that declares ids or single value objects
    .AddDDDToolkitErrors()                           // failures as coded errors
    .AddDDDToolkitMutationConventions()              // refusals as typed errors in a mutation's payload
    .AddTypes();                                     // HotChocolate's generator: [assembly: HotChocolate.Module("Types")]
```

- An identifier is its raw value in the schema (`OrderId` is `UUID`). Write no scalar or converter for an
  id or a single value object: the generated bindings have them.
- Write HotChocolate's `Module` attribute with its namespace in a project that also declares the
  toolkit's `[assembly: Module]`: the two have one name.
- Do not put a sample or an application in a namespace that starts with `DDDToolkit.`: code HotChocolate
  generates for `[ObjectType<T>]` names `HotChocolate...` without `global::`, and binds to the toolkit's
  `DDDToolkit.HotChocolate` there.

## Operations

```csharp
public static class OrderQueries
{
    [Query]
    public static Task<OrderOverview?> GetOrderAsync(OrderId id, [Service] ISender sender, CancellationToken cancellationToken)
        => sender.Send(new OrderDetail(id), cancellationToken).AsTask();
}
```

- An operation is a static method of a static class, marked `[Query]`, `[Mutation]` or `[Subscription]`.
  No `[ExtendObjectType(OperationTypeNames.Query)]`, no `[QueryType]` on a class of plain fields. The
  one exception is a paged field: HotChocolate writes the connection type only for a class it generates the
  type of, so the paged fields go in a `static partial class` marked `[QueryType]` that holds nothing else
  (`OrdersPagedQueries`). A field that does not page stays a `[Query]` method.
- Mark an injected parameter `[Service]`, so a reader sees which parameters are arguments.
- **A resolver decides nothing.** In a module whose use cases are commands and queries, it sends the one
  the feature's route sends and takes no `DbContext`, so what a caller may do is checked where it is for a
  route.
- A mutation takes its arguments and answers what it changed, read after the save. It declares nothing
  about errors: with the mutation conventions a `RefusalException` is a `RefusalError` in the payload, and
  a stale version a `ConcurrencyConflictError`. A client sends the version it read as `expectedVersion`.

## Types, loaders and paging

```csharp
[ObjectType<ProjectOverview>]                       // the record the application's query answers: nothing is mapped
[EntityKey("id")]
internal static partial class ProjectType
{
    static partial void Configure(IObjectTypeDescriptor<ProjectOverview> descriptor) => descriptor.Name("Project");

    public static async Task<IReadOnlyList<CrewOverview>?> GetCrewAsync(     // asked for, it costs one read for a batch: a page, as a rule
        [Parent] ProjectOverview project, ICrewByProjectIdDataLoader crews, CancellationToken cancellationToken)
        => (await crews.LoadAsync(project.Id, cancellationToken))?.Members;
}

[DataLoader]                                         // HotChocolate writes ICrewByProjectIdDataLoader from this
public static async Task<IReadOnlyDictionary<ProjectId, ProjectCrew>> GetCrewByProjectIdAsync(
    IReadOnlyList<ProjectId> projects, ISender sender, CancellationToken cancellationToken)
    => await sender.Send(new CrewsOfProjects(projects), cancellationToken);
```

- Declare a type over the application's own record with `[ObjectType<T>]`. No output record, no `From(...)`
  mapping. The exception is a reference to another module's entity, which is a record of its key alone.
- A field a client may not ask for is a resolver behind a generated `[DataLoader]`, never a list loaded
  with its parent. No hand-written `BatchDataLoader` class.
- A list is paged by HotChocolate: the query takes GreenDonut's `PagingArguments` and answers `Page<T>`,
  the read orders by keys that together are unique and calls `ToPageAsync`, and the field answers
  `PageConnection<T>` (`[UseConnection]` for page sizes or a total count). A REST route maps `size` and
  `after` to the same arguments. No cursor type of your own. A struct id is a paging key as it is:
  `.ThenBy(order => order.Id)`.
- A cursor comes from a client, and the paging library reads it leniently: a text that is no cursor is an
  empty page, a cursor of a list with other keys fails inside the read, and a cursor's head is believed.
  Check it against the query's own keys before paging and refuse it with the list's code, as `ListCursors`
  in the Tenancy sample does (`Examples/Tenancy/Shared/Examples.Tenancy.Shared.Infrastructure/Paging`).
- A field that needs a permission key carries HotChocolate's `[Authorize("<key>")]`. The module answers
  who holds it with one `IFieldKeys<TParent>` per type, registered in its services and awaited through a
  data loader, and the schema calls `AddDDDToolkitKeyAuthorization()`. The field has to be nullable. It
  stands beside the request's own access check, never in its place. When the value is part of a record a
  query answers, hold the rule in the query (answer `null` without the key), or a REST route over the same
  record gives it away; the attribute then only declares the rule.
- Enum values in one spelling for REST and GraphQL: `AddDDDToolkitEnumValues(EnumValueSpelling.LowerSnakeCase)`
  and `AddDDDToolkitErrors(EnumValueSpelling.LowerSnakeCase)`.

## One schema over several modules

Each module serves a source schema from its API project, and the host composes them in the process:
`AddInMemoryFusionGateway()` and `MapInMemoryFusionGateway()`.

- **The host gives every source schema the same conventions**, in one method: `AddDDDToolkitTypes()`,
  the errors, the mutation conventions, the enum spelling, `AddDDDToolkitEntityNullability()` and
  `AddDDDToolkitKeyAuthorization()`. Schemas that spell an enum differently do not compose.
- **A module names another module's entity by its key and nothing else**: a record of the key with
  `[GraphQLName("Seat")]` and `[EntityKey("id")]`. The owner declares the same key and a lookup, marked
  `[Lookup]` and `[Internal]` unless a client needs it on its own. No resolver sends another module's
  query, and no root field exists only to join two modules.
- **A module adds its own fields to a type another module owns** by declaring that type as the key and
  those fields: a project's inspections come from the Inspections schema. Load them through a data loader
  that takes the parents of a batch, so a batch, as a rule a page, costs one access question and one
  statement.
- **Every field of an entity but its key may be null** (`AddDDDToolkitEntityNullability()`), so a
  reference the owner answers nothing for is its key with empty fields and no error. A lookup answers
  nothing for what the caller may not read, exactly as for what is not there.
- **Scopes**: a scope per resolver for queries, the request's for mutations
  (`DefaultQueryDependencyInjectionScope`, `DefaultMutationDependencyInjectionScope`). Fields run side by
  side, so a read takes a context of its own from a pool.
- **Middleware goes before the gateway.** The gateway is a branch of the pipeline, not an endpoint:
  `RequireAuthorization()` cannot be put on it. Authentication, the caller (`Callers.Begin`) and request
  localization come first, and a check for a token is middleware on the path.
- Commit each schema as `schema.graphql` and compare it in a test, so a change of the API is seen.
