---
name: dddtoolkit
description: Write, review and fix .NET domain code built on DDDToolkit, the source generators behind [AggregateRoot<T>], [Entity<T>], [EntityId<T>], [ValueObject] and [SingleValueObject<T>]. Use when a project references a DDDToolkit package; when code uses those attributes, IInvariant<T>, DomainEvent, [assembly: Module], [ModuleContract] or [IntegrationEvent]; when a build reports a DDD000xx diagnostic; or when modelling aggregates, value objects, identifiers, invariants, domain events, module boundaries or their Entity Framework persistence in such a project.
---

# DDDToolkit

DDDToolkit is a set of Roslyn source generators and analyzers for domain-driven design in .NET. You put
an attribute on a `partial` type. While the project compiles, the generator writes the rest: the base
class, constructors, equality, identifier plumbing, backing fields for collections, invariant checks,
Entity Framework converters and GraphQL bindings. Nothing runs by reflection.

Three consequences for how you work:

1. **Write the declaration, not what the generator writes.** No base class on an entity, no `Id`
   property, no equality members, no backing field for a collection, no value converters, no
   Entity Framework configuration for owned types or value objects. Writing them yourself collides with
   the generated members (CS0102, CS0111) or the generated base class (CS0263).
2. **Every declaration is `partial`**, and the kind of type is fixed: a record for identifiers and value
   objects, a class for entities and aggregate roots.
3. **The build is the feedback loop.** A misused attribute reports a diagnostic whose id starts with
   `DDD`, and nothing is generated for that type. The errors that follow it (no `Id`, no `_lines`, no
   `base(id)`) are the same mistake, so fix the DDD diagnostic first. Every id, what it means and the
   fix: [references/diagnostics.md](references/diagnostics.md).

## Before writing code

- Read the project files for which DDDToolkit packages are referenced, and use only their APIs. Each
  integration is a package of its own that brings its own generator: `DDDToolkit` (core, always),
  `DDDToolkit.EntityFramework`, `DDDToolkit.Mediator`, `DDDToolkit.FluentValidation`,
  `DDDToolkit.HotChocolate`, `DDDToolkit.Localization`, `DDDToolkit.Testing`, `DDDToolkit.Messaging.*`,
  `DDDToolkit.EntityFramework.Supabase`.
- Follow the layout the solution already has for aggregates, invariants, events and handlers.
- When unsure what the generator produced, read it: go to definition on a generated member, or set
  `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` and look under `obj/` after a build.
  Do not guess at generated members.

Namespaces:

| Namespace | Holds |
|---|---|
| `DDDToolkit.Abstractions.Attributes` | every attribute: `[EntityId<T>]`, `[ValueObject]`, `[AggregateRoot<T>]`, `[DontCompare]`, `[Module]`, ... |
| `DDDToolkit.BaseTypes` | `DomainEvent`, `DomainEventClock` |
| `DDDToolkit.Invariants` | `IInvariant<T>`, `InvariantFailure`, `InvariantViolation` |
| `DDDToolkit.Validation` | `ValidationErrorBuilder`, `ValidationError`, `TryToValid`, `Prefixed`, `ToErrorDictionary` |
| `DDDToolkit.Exceptions` | `ConcurrencyConflictException`, `InvariantViolationException`, `InvalidValueObjectException` |

## Identifiers

```csharp
[EntityId<Guid>("ORD")]                          // the prefix is optional: ToString() gives "ORD_0199..."
public readonly partial record struct OrderId;
```

- Use `readonly partial record struct`. The record class form, `partial record OrderId`, is only for an
  id that needs inheritance or an always-valid twin, which is rare.
- Generated: `Value`, a public constructor `new OrderId(guid)`, `Empty` and `IsEmpty`, `Parse` and
  `TryParse` (prefix optional), `IParsable<T>` (so minimal API route binding works), `IComparable<T>`,
  explicit conversions to and from the raw value, and a System.Text.Json converter that writes the bare
  value. Do not add implicit conversions: they undo the type safety.
- Create stored ids with `OrderId.CreateSequential()` (a version 7 `Guid`, index friendly), otherwise
  `CreateUnique()`. Both exist for `Guid` only; construct other ids with the constructor.
- A struct id cannot be null, and `default` is `Empty`. Guard with `IsEmpty`; use `OrderId?` for an
  optional id.
- The raw type is a value type or `string` ([DDD00008](references/diagnostics.md)).
- Short form: `[AggregateRoot<Guid>("ORD")]` or `[Entity<Guid>("LINE")]` on the entity also generates
  `OrderId` or `OrderLineId` (entity name plus `Id`, same namespace). Use it for an id nobody outside the
  aggregate names. Declare the id explicitly when other aggregates, modules, DTOs or APIs use it. A type
  of the generated name that already exists is DDD00007.
- The record class form has protected constructors: add `public static CustomerId Create(Guid value) => new(value);`.

## Value objects

```csharp
[SingleValueObject<string>(ColumnLength: MaxLength)]    // ColumnLength only matters with Entity Framework
public partial record EmailAddress
{
    public const int MaxLength = 255;

    public static EmailAddress Create(string value) => new(value);     // generated constructors are protected

    protected override void Validate(ValidationErrorBuilder errors)
    {
        if (!Value.Contains('@'))
        {
            // message, property name, code (what callers branch on), attempted value
            errors.Add("An email address needs an @.", nameof(Value), "NoAtSign", Value);
        }
    }
}

[ValueObject]
public partial record Money(decimal Amount, string Currency);   // positional: properties are generated

[ValueObject]
public partial record Address
{
    public Address(string street, string city) => (Street, City) = (street, city);

    [JsonInclude] public string Street { get; protected init; }
    [JsonInclude] public string City { get; protected init; }
}
```

- A `partial record`, never a class or struct (DDD00001), never `sealed` (DDD00013): the generator
  derives the always-valid twin `Valid<Name>` from it.
- Properties you declare are `{ get; protected init; }` (DDD00010, DDD00011; a code fix exists). Put
  `[JsonInclude]` on them when the value object goes through System.Text.Json, for example inside a
  domain event stored in the outbox. Without it they deserialize as defaults. Positional records get it
  generated.
- Equality covers every property. Leave one out with `[DontCompare]` (`[property: DontCompare]` on a
  positional parameter). `[Internal]` also keeps a member out of Entity Framework, Newtonsoft JSON and
  the GraphQL schema.
- Validation: override `Validate(ValidationErrorBuilder errors)` to say what is wrong, or
  `bool Validate()` for a plain yes or no. With `DDDToolkit.FluentValidation` referenced, write the rules
  in a nested `partial class Validator { public Validator() => RuleFor(x => x.Value).EmailAddress(); }`
  instead. Validation is synchronous and sees one value object only.
- An invalid instance may exist; `IsValid` says whether it is. The twin (`ValidEmailAddress`,
  `ValidMoney`) can only hold a value that passed. Take the twin where a method needs a checked value:
  `public Order(OrderId id, ValidAddress shipTo)`.
- Getting the twin:
  - `ToValid()` throws `InvalidValueObjectException`. Use it where an invalid value is a bug.
  - `TryToValid(out var valid, out var errors)` never throws. Use it for input from outside. For a whole
    request, gather `errors.Prefixed("shipTo")` into one list and return
    `Results.ValidationProblem(errors.ToErrorDictionary())`.
- A single value object's `ToString()` is the record's, listing its validation state too. Print
  `email.Value`.
- Change a value with the generated `money.With(amount: 12.50m)`. A `with { }` expression only compiles
  inside the type (CS0272). That is intended, so do not widen the setters to make `with` work.
- The toolkit has no `Result<T>` on purpose. Wrap `TryToValid` in the project's own result type if it
  has one.

## Entities and aggregates

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public Order(OrderId id, CustomerId customer, ValidAddress shipTo) : base(id)
    {
        Customer = customer;
        ShipTo = shipTo;
        Status = OrderStatus.Draft;
        RaiseDomainEvent(new OrderPlaced(id, customer));
    }

    public CustomerId Customer { get; private set; }         // another aggregate: hold its id

    public Address ShipTo { get; private set; }

    public OrderStatus Status { get; private set; }

    public partial IReadOnlyList<OrderLine> Lines { get; }   // the generator writes _lines and the view

    public void AddLine(ProductId product, int quantity)
    {
        if (Status != OrderStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft order takes new lines.");
        }

        _lines.Add(new OrderLine(OrderLineId.CreateSequential(), product, quantity));
        RaiseDomainEvent(new LineAdded(Id, product, quantity));
    }
}

[Entity<Guid>("LINE")]                                         // also generates OrderLineId
public partial class OrderLine
{
    public OrderLine(OrderLineId id, ProductId product, int quantity) : base(id)
        => (Product, Quantity) = (product, quantity);

    public ProductId Product { get; private set; }

    public int Quantity { get; private set; }
}
```

- A `partial class` (DDD00002, DDD00005), not generic (DDD00006). `[AggregateRoot<T>]` for what is
  loaded, saved and referenced from elsewhere; `[Entity<T>]` for a child that only exists inside one
  aggregate. Never both (DDD00009).
- Do not declare a base class: the generator writes `AggregateRoot<OrderId>` or `Entity<OrderLineId>`,
  and a second one is CS0263. Share behaviour through an interface instead. The generator also writes
  the protected parameterless constructor Entity Framework and serializers use; your constructor calls
  `: base(id)`.
- Setters are private. State changes through methods on the root, which check the rules; a child is
  changed through its root.
- Collections: declare `public partial IReadOnlyList<T> Name { get; }` (or `IReadOnlyCollection<T>`,
  `IEnumerable<T>`, `IReadOnlySet<T>`), get-only (DDD00020). Mutate the generated field inside the
  entity: `Lines` gives `_lines`, `OrderLines` gives `_orderLines`. Do not declare the field. With
  Entity Framework, a set of primitives cannot be mapped; use `IReadOnlyList<string>`.
- A computed property that returns entities, such as `IEnumerable<OrderLine> OpenLines => _lines.Where(...)`,
  is a navigation to Entity Framework and fails the first save ("Collection navigations must implement
  'ICollection<>'"). Make it a method, or mark it `[Internal]`, which keeps it out of the model.
- Reference another aggregate by its id, never the object (DDD00021). The one allowed reference is a
  child pointing back at the root that owns it.
- Equality compares ids. Every root has a `long Version` for optimistic concurrency; do not add another.
- There are no audit fields (`CreatedAt`, `UpdatedBy`) and no soft delete, on purpose. A fact the
  domain uses is an ordinary property set by the method that does the thing; row bookkeeping belongs in
  Entity Framework shadow properties and an interceptor of your own.

## Invariants

An invariant is a rule about the state of the whole aggregate, not about input. There are two shapes,
and one entity may use both.

The seam, the default for a small rule:

```csharp
public partial class Order
{
    partial void CheckInvariants()                // exactly this: partial void, no access modifier
    {
        if (_lines.Select(line => line.Product).Distinct().Count() != _lines.Count)
        {
            throw InvariantViolation("An order may not name the same product on two lines.");
        }
    }
}
```

A named rule, when it deserves a name, a code a caller branches on, or a test of its own:

```csharp
public partial class Order                                     // e.g. Invariants/MustHaveLines.cs
{
    public sealed class MustHaveLines : IInvariant<Order>      // nested in the entity it is about
    {
        public const string ViolationCode = "ORDER_HAS_NO_LINES";

        public string Code => ViolationCode;

        public InvariantFailure? Check(Order order)            // null means the rule holds
            => order.Status != OrderStatus.Draft && order._lines.Count == 0
                ? "A placed order must have at least one line."
                : null;
    }
}
```

- A named rule is nested in the entity it checks (DDD00024), is about that entity (DDD00025), has a
  code no other rule of that entity has (DDD00026), is stateless and has an accessible parameterless
  constructor (DDD00027). Attach values for translation with
  `new InvariantFailure("...").With("CreditLimit", limit)`.
- Nothing checks invariants while a method runs. They are checked when asked:
  `order.GetInvariantViolations()` returns `IReadOnlyList<InvariantViolation>` (each with `Code`,
  `Message`, `EntityType`, `EntityId`) without throwing, `order.EnsureInvariants()` throws
  `InvariantViolationException`, and with Entity Framework every `SaveChanges` runs the check on what
  it writes, so a broken aggregate is never stored. Asking the root covers every child in its
  collections as well.
- So a command that must be refused needs a guard in its method too: check, throw, and only then
  change state or raise an event. The invariant is the rule that holds whichever method ran; the guard
  is what makes the method refuse cleanly, with nothing raised and nothing changed.
- Do not call `child.EnsureInvariants()` from the root's seam; the generated walk already asks every
  child. A rule about how children relate to each other belongs in the root.
- Branch on `Code`, never on the message. Violations from a seam all carry `InvariantViolation.SeamCode`.
- A rule across two aggregates cannot be an invariant: either they are one aggregate, or the rule is
  eventually consistent and a handler of a domain event checks it and reacts.
- An `InvariantViolationException` in production is a bug in an aggregate method, not a message for a
  user. A handler can ask `GetInvariantViolations()` after acting and before saving, and answer with
  the codes; the aggregate it acted on is then broken, so it must not be saved or reused.

## Domain events

```csharp
[DomainEventName("ordering.order-cancelled")]   // a stable stored name, for anything stored or published
public sealed record OrderCancelled(OrderId OrderId, string Reason) : DomainEvent;
```

- An ordinary `sealed record` deriving from `DomainEvent`, with no attribute required and no `partial`.
  `EventId` and `OccurredAt` come from the base.
- Only an aggregate root raises, with `RaiseDomainEvent(...)` from its own methods (it is protected).
  Children have no events; their root raises. Raise after the change, not before a check that can
  still throw.
- Do not drain or clear events in application code: the Entity Framework integration takes them during
  the save. `order.DomainEvents` is a read-only view.
- With Entity Framework, an aggregate with pending events refuses to save until a delivery mode is
  configured; see [references/persistence-and-modules.md](references/persistence-and-modules.md).

## Testing

With `DDDToolkit.Testing` in the test project (`using DDDToolkit.Testing;`; it brings no test framework):

```csharp
AggregateScenario.Given(order)
    .When(o => o.Cancel("out of stock"))
    .RaisedExactly<OrderCancelled>();

AggregateScenario.Given(order)
    .WhenThrows<InvalidOperationException>(o => o.AddLine(product, 1));   // and asserts nothing was raised

var scenario = AggregateScenario.Given(Draft());               // several steps: keep the scenario
scenario.When(o => o.AddLine(product, 2)).RaisedExactly<LineAdded>();
Assert.Single(scenario.Subject.Lines);                         // Subject is on the scenario, not on what When returns

Assert.Single(order.GetInvariantViolations(), v => v.Code == Order.MustHaveLines.ViolationCode);
```

- `Given` returns an `AggregateScenario<T>`, with `Subject` (the aggregate), `When`, `WhenThrows`,
  `WhenAsync`, `WhenThrowsAsync`, `PendingEvents` and `IgnorePendingEvents()`. `When` returns
  `RaisedEvents`, the batch of events that call raised, and the assertions are on it.
- `Given` takes an aggregate built with its real constructor and methods. There is no event replay.
- Assertions on the batch: `RaisedExactly<T>()`, `Raised<T>()`, `Raised(expected)` (compares the
  payload, never `EventId` or `OccurredAt`), `RaisedNo<T>()`, `RaisedNothing()`, `SingleEvent<T>()`,
  `EventsOf<T>()`. They throw `AggregateAssertionException`. Assertions on state and invariants use the
  project's own test framework; the kit adds none.
- To assert on event timestamps: `using var clock = DomainEventClock.Use(timeProvider);`.

## Persistence, event delivery and modules

Wiring Entity Framework, choosing in-process dispatch or the outbox, declaring modules and publishing or
consuming integration events: [references/persistence-and-modules.md](references/persistence-and-modules.md).
The mistakes it prevents most often:

- `UseDDDToolkit(services)` takes the provider from the `AddDbContext((services, options) => ...)`
  callback, not the root provider.
- `ConfigureConventions` calls `AddDDDToolkitConventions()` and one generated `Add{Module}Converters()`
  per assembly that declares identifiers or single value objects.
- Child entities, value objects and collections need no mapping code, and child entities get no `DbSet`.
- A module never holds another module's entity (DDD00023) or names what it does not publish (DDD00022).

## Further reading

Every page of the documentation is Markdown at `https://dylansnel.github.io/DDDToolkit/docs/<page>.md`,
and `https://dylansnel.github.io/DDDToolkit/llms.txt` lists them. Fetch the page when a task goes past
this skill:

| Task | Page |
|---|---|
| A whole module end to end, as a worked example | `getting-started` |
| What exactly is generated for a declaration | `generated-code` |
| Identifiers, value objects, entities in depth | `identifiers`, `value-objects`, `entities-and-aggregates` |
| Invariants, their two stages and their limits | `invariants` |
| Deciding what belongs in one aggregate | `aggregate-design` |
| Events, testing | `domain-events`, `testing` |
| Entity Framework mapping, concurrency, migrations | `entity-framework` |
| Tables keyed on more than the id (`[KeyPart]`) | `composite-keys` |
| In-process dispatch versus the outbox | `event-delivery` |
| Modules, contracts, integration events, versioning | `modules`, `module-contracts`, `integration-events` |
| pgmq, Wolverine, MassTransit, a custom sink | `transports` |
| GraphQL with HotChocolate, Fusion across modules | `graphql` |
| Failures in the reader's language | `localization` |
| Supabase migrations from Entity Framework | `supabase` |
| Upgrading from 2.x | `migrating-to-3` |
