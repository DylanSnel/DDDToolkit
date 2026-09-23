# GraphQL

`DDDToolkit.HotChocolate` makes a domain model built with the toolkit printable as a GraphQL schema.
Without it a strongly typed id becomes an object type with a `value` field, the validation bookkeeping
on your value objects becomes public API, and a client has to know that an `OrderId` is a wrapper. With
it an `OrderId` is a `UUID`, the bookkeeping is gone, and every domain event shares one interface.

It also carries one piece of plumbing in the other direction:
[a sink](#pushing-integration-events-to-subscribers) that pushes published contracts to subscribed
clients.

The integration works with HotChocolate 16.0.0 and later. The package asks for no more than that, and
every pull request runs the tests against both 16.0.0 and the newest release (16.6.6 at the time of
writing).

## Install

```bash
dotnet add package DDDToolkit.HotChocolate
```

The package brings its own source generator, so referencing it is the whole of the build-time setup.
It depends on `HotChocolate.AspNetCore`, so you do not add HotChocolate separately.

## Register

Two kinds of call, both on the `IRequestExecutorBuilder`:

```csharp
using DDDToolkit.HotChocolate;
using Ordering.Domain.GraphQl;   // generated
using SharedKernel.GraphQl;      // generated

builder.Services
    .AddGraphQLServer()
    .AddDDDToolkitTypes()
    .AddSharedKernelGraphQlRuntimeBindings()
    .AddOrderingGraphQlRuntimeBindings()
    .AddQueryType<Query>();
```

`AddDDDToolkitTypes()` registers the conventions, and there is one call for the whole schema. It adds
the `IgnoreInternalFieldsInterceptor`, which keeps `[Internal]` members out of the schema, and the
`DomainEvent` interface type. Calling it twice produces the same schema as calling it once.

`Add{Module}GraphQlRuntimeBindings()` registers the scalar bindings and the type converters, and there
is one call per assembly that declares identifiers or single value objects. The method is generated
into the namespace `{AssemblyName}.GraphQl`, on a static class named `HotChocolateExtensions`. The
`{Module}` part comes from the `DDD_Module` MSBuild property, so a project that sets
`<DDD_Module>Ordering</DDD_Module>` gets `AddOrderingGraphQlRuntimeBindings`. See
[Name your module](getting-started.md#name-your-module). An assembly that declares neither an
identifier nor a single value object gets no method at all.

The order of the two is not significant: the calls only record configuration, and the schema is built
afterwards. The order above reads in the direction of the dependency, from conventions to your types.

## What a typed id looks like in the schema

Take an id and a field that returns it:

```csharp
[EntityId<Guid>("TST")]
public readonly partial record struct TicketId
{
    public static TicketId Create(Guid value) => new(value);
}
```

```csharp
public sealed class Query
{
    public TicketId PrefixedId() => TicketId.CreateSequential();
}
```

Without the runtime bindings, HotChocolate binds by convention and sees a struct with public members:

```graphql
type Query {
  prefixedId: TicketId!
}

type TicketId {
  value: UUID!
  isEmpty: Boolean!
}
```

With the generated bindings registered, the id collapses into the scalar it wraps:

```graphql
type Query {
  prefixedId: UUID!
}
```

Two generated pieces make that happen.

For every identifier and single value object, the generator emits a nested
`ChangeTypeProvider` class implementing `HotChocolate.Utilities.IChangeTypeProvider`. It converts the
object to its wrapped value and back, and declines every other pair:

```csharp
var provider = new TicketId.ChangeTypeProvider();

// root is the fallback provider HotChocolate passes in; these converters never delegate to it.
provider.TryCreateConverter(typeof(TicketId), typeof(Guid), root, out var toValue);   // true
provider.TryCreateConverter(typeof(Guid), typeof(TicketId), root, out var fromValue); // true
provider.TryCreateConverter(typeof(TicketId), typeof(string), root, out var other);   // false
```

The registration method then binds the runtime type to a schema type and registers that provider:

```csharp
builder.BindRuntimeType<TicketId, UuidType>();
builder.AddTypeConverter<TicketId.ChangeTypeProvider>();
```

The binding decides how the type is printed. The converter decides how a value moves across the
boundary in either direction.

Note that the prefix is not part of the wire format. `TicketId.Create(guid).ToString()` is
`"TST_..."`, and the same id serializes as the bare `Guid`. The prefix belongs to `ToString` and
`Parse`; see [Prefixes](identifiers.md#prefixes).

### Struct ids, class ids and always-valid twins

All three bind to the same scalar.

| Declaration | Schema type |
|---|---|
| `[EntityId<Guid>] readonly partial record struct CatId` | `UUID` |
| `[EntityId<Guid>] partial record PersonId` | `UUID` |
| the generated `ValidPersonId` twin | `UUID` |

The twin is handled by the same `ChangeTypeProvider` as the type it derives from: one provider carries
the conversions for both. See [The always-valid twin](value-objects.md#the-always-valid-twin) for what
the twin is for. A struct id has no twin, so its provider carries one pair of conversions.

Nullability and lists follow from the CLR type, as they do for any other runtime type:

```graphql
cat: UUID!
nullableCat: UUID
cats: [UUID!]!
```

`CatId?` prints as `UUID` and serializes as `null` when absent, so an optional id stays off the heap
and still reads correctly on the wire.

### Arguments and input objects

The binding is on the runtime type, not on a direction, so the same mapping applies to input. An id
used as an argument is declared as the scalar and arrives at the resolver as the id:

```csharp
public string DescribeTicketId(TicketId id) => id.ToString();
```

```graphql
describeTicketId(id: UUID!): String!
```

```graphql
{ describeTicketId(id: "44444444-4444-4444-4444-444444444444") }
```

That returns `"TST_44444444-4444-4444-4444-444444444444"`, which only a real `TicketId` can produce; a
`string` argument would come back without the prefix. Literals and variables both work, and a variable
is declared with the scalar:

```graphql
query Echo($id: UUID!) { echoCatId(id: $id) }
```

Input objects work the same way. A plain class with typed id properties is published with the id
fields already collapsed:

```csharp
public sealed class SeatReservation
{
    public TicketId Ticket { get; set; }

    public SeatNumber Seat { get; set; }
}
```

```graphql
input SeatReservationInput {
  ticket: UUID!
  seat: Int!
}
```

## The default scalar mapping

When a type carries no `[GraphQLType<T>]`, the generator picks the schema type from the CLR type it
wraps:

| Wrapped CLR type | HotChocolate type | Schema type |
|---|---|---|
| `string` | `StringType` | `String` |
| `short` | `ShortType` | `Short` |
| `int` | `IntType` | `Int` |
| `long` | `LongType` | `Long` |
| `float` | `FloatType` | `Float` |
| `double` | `FloatType` | `Float` |
| `decimal` | `DecimalType` | `Decimal` |
| `bool` | `BooleanType` | `Boolean` |
| `DateTime` | `DateTimeType` | `DateTime` |
| `DateTimeOffset` | `DateTimeType` | `DateTime` |
| `DateOnly` | `DateType` | `Date` |
| `TimeOnly` | `LocalTimeType` | `LocalTime` |
| `Guid` | `UuidType` | `UUID` |

Wrap anything else and no `BindRuntimeType` line is generated. The converter is still registered, but
the type is published by convention, as an object type with a `value` field. Name a schema type
yourself if that is not what you want.

Two rows in that table are not an exact match of runtime types, and it is worth knowing which.
`DateTimeType` has a runtime type of `DateTimeOffset`, and `FloatType` has a runtime type of `double`.
So an id or single value object over `DateTime` or over `float` leans on HotChocolate's own conversion
between those pairs rather than on anything the toolkit does. Neither pairing is covered by the
toolkit's tests. The other eleven rows match exactly, and `Guid`, `int` and `string` are exercised end
to end.

### Naming the schema type yourself

`[GraphQLType<TSchemaType>]` overrides the default for one type. The generator reads it and emits the
binding it names:

```csharp
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.HotChocolate.Attributes;
using HotChocolate.Types;

[GraphQLType<EmailAddressType>]
[SingleValueObject<string>(ColumnLength: 255)]
public partial record EmailAddress
{
    public static EmailAddress Create(string value) => new(value);
}
```

```graphql
email: EmailAddress!
```

Without the attribute that field would be `String!`. `EmailAddressType` comes from
`HotChocolate.Types.Scalars`, which you reference yourself; the toolkit does not bring it in.

The attribute applies to identifiers as well as to single value objects, and to struct declarations as
well as to records:

```csharp
[EntityId<string>("USR")]
[GraphQLType<EmailAddressType>]
public readonly partial record struct LoginId
{
    public static LoginId Create(string value) => new(value);
}
```

`TSchemaType` is constrained to `HotChocolate.Types.ITypeDefinition`. This attribute is the toolkit's
own, in `DDDToolkit.HotChocolate.Attributes`, and is not HotChocolate's `GraphQLTypeAttribute`, which
annotates individual members and arguments instead of a type.

## Internal members are removed from the schema

The toolkit marks its bookkeeping with `[Internal]`, and `IgnoreInternalFieldsInterceptor` keeps every
such member out of the schema. It covers object types, input object types and interface types, so a
member stays hidden whether it is read or written. Covering interfaces matters too: a field kept on an
interface but dropped from an implementing object would make the schema invalid.

In practice this hides `IsValid`, `IsValidated`, `EnsureValidated` and the FluentValidation
integration's `Errors` on value objects, and `DomainEvents` on an aggregate root. It does not hide
anything you did not mark. `Id` and `Version` stay, because they are API:

```graphql
type Ticket {
  holder: EmailAddress!
  seat: Int!
  version: Long!
  id: UUID!
}
```

Asking for a hidden field is a validation error, not an empty result: the field is not in the schema at
all, so a query naming it comes back with an `errors` array that names the field.

```graphql
{ issuedTicket { domainEvents { eventId } } }
```

See [Hiding members](value-objects.md#hiding-members) for what `[Internal]` means elsewhere, and
[Optimistic concurrency](entities-and-aggregates.md#optimistic-concurrency) for `Version`.

### Why it removes fields instead of flagging them

HotChocolate offers a per-field ignore flag at type completion, and using only that is not enough.

A field is dropped from the printed schema when it is flagged, but the types it refers to have already
been registered by then. Discovery walks the fields to work out what each one depends on, so flagging
the FluentValidation integration's `Errors` at completion still leaves `ValidationFailure` and its
`Severity` enum registered as schema types that no field can reach.

So the interceptor removes the fields in `OnBeforeRegisterDependencies`, during type discovery and
before dependencies are computed. It still flags in `OnBeforeCompleteType`, for anything internal that
appeared after discovery, such as a merged type extension.

The visible effect is that the schema contains no type that exists only because a hidden field
mentioned it. `ValidationFailure` and `Severity` are absent, and so is the `ValidPersonName` twin that
a value object's `[Internal]` `ToValid()` would otherwise have pulled in.

## The DomainEvent interface

`AddDDDToolkitTypes()` registers one type of its own: a GraphQL interface over `IDomainEvent`.

```graphql
"Something that happened in the domain."
interface DomainEvent {
  "Unique, stable identifier of this occurrence. Use it for idempotent handling."
  eventId: UUID!
  "When the event occurred (UTC)."
  occurredAt: DateTime!
  "The runtime type name of the event."
  eventType: String!
}
```

Any event type you expose implements it automatically, because it implements `IDomainEvent`:

```graphql
type TicketIssued implements DomainEvent {
  ticketId: UUID!
  eventId: UUID!
  occurredAt: DateTime!
  eventType: String!
}
```

`eventId` and `occurredAt` come straight from the event. `eventType` is a resolver, and it gives a
client a discriminator without a fragment per concrete type:

```graphql
{ latestEvent { ... on DomainEvent { eventType } } }
```

Be aware of what `eventType` currently returns. It resolves to the CLR type name of the event, so
`TicketIssued` returns `"TicketIssued"`, and `[DomainEventName("ordering.ticket-issued")]` does not
change it. If you rename the class, the value changes with it. Treat `eventType` as a hint for
building a client, not as the stable wire name; the stable name is
[`DomainEventName.Of<T>()`](domain-events.md#stable-names).

## Pushing integration events to subscribers

`GraphQlSubscriptionSink` is an [integration event](integration-events.md) sink that publishes to
HotChocolate's `ITopicEventSender`, so a message leaving the outbox reaches the clients holding a socket
right now.

**It is a different thing from the other sinks, and worth being blunt about.** The in-process module sink
is how one module tells another that something happened. pgmq is how a module tells another deployable the
same thing. Both are integration: durable, retried, and the receiver gets the message whether or not it was
running at the time.

A subscription is none of that:

- **Not durable.** Nothing is stored. A payload with no subscriber is dropped.
- **Only the connected.** A client that reconnects has missed what happened while it was away, and has to
  re-read the state it cares about.
- **No acknowledgement.** The sink returns once the topic accepted the payload. Whether a socket survived
  long enough to deliver it is not knowable from there.

So use it to keep a screen in step with the server. Never as the path by which some other part of the
system learns that an order was placed. If a browser missing an update would be a bug in your data rather
than a stale view, this is the wrong mechanism.

### Registering it

```csharp
builder.Services
    .AddGraphQLServer()
    .AddDDDToolkitTypes()
    .AddCommonGraphQlRuntimeBindings()
    .AddInMemorySubscriptions()
    .AddQueryType<Query>()
    .AddSubscriptionType<Subscription>();

builder.Services.AddIntegrationEventSubscriptions(map => map.Publish<OrderPlacedV2>("orderPlaced"));
```

```csharp
options.UseOutbox(outbox =>
{
    outbox.PublishAs<OrderPlaced, OrderPlacedV2>(e => new OrderPlacedV2(e.OrderId.Value, e.Total.Amount));
    outbox.SendTo<GraphQlSubscriptionSink>();
});
```

The subscription field reads the same topic:

```csharp
public sealed class Subscription
{
    [Subscribe(With = nameof(OnOrderPlacedAsync))]
    public OrderPlacedV2 OrderPlaced([EventMessage] OrderPlacedV2 order) => order;

    public ValueTask<ISourceStream<OrderPlacedV2>> OnOrderPlacedAsync(
        [Service] ITopicEventReceiver receiver,
        CancellationToken cancellationToken)
        => receiver.SubscribeAsync<OrderPlacedV2>("orderPlaced", cancellationToken);
}
```

```graphql
subscription { orderPlaced { orderId total } }
```

Nothing is pushed until the map names it. That is the point: a subscription payload is part of your schema,
and a schema is a deliberate list rather than whatever happened to be published.

### It publishes the contract, never the domain event

This is not tidiness. A subscription payload is a schema type. It is declared on the subscription field, it
is resolved through the schema, and the schema's own authorisation decides what the client may see, field
by field. `[Internal]` members are already gone, and an `[Authorize]` on a field still applies.

Broadcast the domain event instead and you are pushing your internal record, with your identifiers and
whatever you last added to it, to whoever is holding a socket. The contract is the list of things you
decided to say out loud, and it is the same contract the other modules read, so there is one published
shape rather than two.

No upcasting happens on this path, and that is deliberate. An upcaster catches a reader up with a payload
written before it was deployed. A subscription payload was produced seconds ago by this process, against
this schema. If the map does not name the message's name **and version**, it is simply not pushed.

### Scoping a topic

A constant topic is a firehose that every subscriber sees. Build the topic from the message to scope it,
usually to one aggregate:

```csharp
map.Publish<OrderPlacedV2>(message => $"order:{message.AggregateId}");
```

Returning `null` from the factory drops that one occurrence.

A topic string is not authorisation. It decides who is woken; the subscription field decides who is allowed
to subscribe and what they then see. Put the `[Authorize]` on the field.

### Transports

`AddInMemorySubscriptions()` is enough for a single process. At 16.6.6 HotChocolate also ships transports
for Postgres, Redis, RabbitMQ and NATS, for when more than one server is holding sockets and the server
that published is not the server the client is connected to. The toolkit's sink goes through
`ITopicEventSender` and does not care which of them is registered.

The Postgres one is worth knowing about if you are already on Postgres. It rides `LISTEN` and `NOTIFY`, so
several servers share subscriptions with no extra infrastructure at all: no Redis, no broker, nothing new to
run or pay for. Combined with the [pgmq sink](integration-events.md#when-a-module-becomes-its-own-deployable-pgmq),
a Postgres deployment can carry both the durable path and the live path without another service.

`Tests/DDDToolkit.HotChocolate.Tests/SubscriptionSinkTests.cs` runs the whole thing against the real
in-memory transport, including a client subscribing through the schema and receiving what the outbox
published.

## Failures as GraphQL errors

Without help, a resolver that throws `InvalidValueObjectException` or `InvariantViolationException`
reaches the client as "Unexpected Execution Error", with no code and no way to tell which field or
which rule it was. `AddDDDToolkitErrors()` fixes that:

```csharp
services
    .AddGraphQLServer()
    .AddDDDToolkitTypes()
    .AddDDDToolkitErrors();
```

Each failure becomes an error of its own, with the code in `extensions.code`:

```json
{
  "errors": [{
    "message": "Een order mag hooguit € 1.000,00 zijn.",
    "path": ["placeOrder"],
    "extensions": {
      "code": "Order.OverCreditLimit",
      "entity": "Order",
      "entityId": "ORD_…",
      "arguments": { "CreditLimit": 1000 }
    }
  }]
}
```

| Exception | Errors | Extensions |
| --- | --- | --- |
| `InvalidValueObjectException` | one per `ValidationError` | `code`, `field`, `arguments` |
| `InvariantViolationException` | one per `InvariantViolation`, children included | `code`, `entity`, `entityId`, `arguments` |

The rejected value itself is never sent back; it may be a password. Every other error passes through
untouched.

The message is phrased in the reader's language when the application registered an
`IFailureLocalizer`, by calling `AddDDDToolkitLocalization()` from `DDDToolkit.Localization`, and is the
domain's own sentence otherwise. The language is the request's UI culture, so put
`app.UseRequestLocalization(...)` before `app.MapGraphQL()`. See [Localization](localization.md).

## Upgrading to HotChocolate 16

If you are moving your own HotChocolate code onto 16.6.6 alongside this package, these are the names
that caught the toolkit out.

**The type-system configuration classes are gone under their old names.** `DefinitionBase` and
`ObjectTypeDefinition` do not exist in the 16.6.6 assemblies. The equivalents are
`TypeSystemConfiguration`, `ObjectTypeConfiguration`, `InterfaceTypeConfiguration`,
`InputObjectTypeConfiguration` and `FieldConfiguration`, all in
`HotChocolate.Types.Descriptors.Configurations`. A `TypeInterceptor` override therefore takes a
`TypeSystemConfiguration`, which is what `IgnoreInternalFieldsInterceptor` is written against.

**`INamedType` is gone.** `HotChocolate.Types.ITypeDefinition` replaces it, and that is the constraint
on `[GraphQLType<TSchemaType>]`.

**`HotChocolate.Execution` has no stable 16.x release.** Execution moved into the `HotChocolate`
package, so a project that referenced `HotChocolate.Execution` references `HotChocolate` instead. The
`HotChocolate.Execution` namespace itself is unchanged, so `IRequestExecutor`,
`BuildRequestExecutorAsync` and `IRequestExecutorBuilder` are used exactly as before.

What did not change is the part the toolkit relies on most:

| Still the same in 16.6.6 | Where |
|---|---|
| `BindRuntimeType<TRuntimeType, TSchemaType>()` | `Microsoft.Extensions.DependencyInjection` |
| `AddTypeConverter<T>()` | `Microsoft.Extensions.DependencyInjection` |
| `IChangeTypeProvider` and the `ChangeType` delegate | `HotChocolate.Utilities` |
| `[InterfaceType<T>]` with a `static partial void Configure` | `HotChocolate.Types.Analyzers` |

## What this package does not do

It maps identifiers and single value objects onto scalars. It does not turn a multi-property
`[ValueObject]` into anything other than the object type HotChocolate would infer, and it generates no
queries, mutations or resolvers. The schema is still yours to write.

That includes subscriptions. The sink publishes a contract to a topic; the subscription field, its
arguments and its authorisation are yours, and so is the choice of transport.
