# Designing aggregates

Declaring an aggregate takes one attribute. Deciding what belongs inside the aggregate matters more, and no attribute
does that for you.

An aggregate is a trade-off. Everything inside the boundary is consistent at the end of every transaction. Every save
checks the aggregate's rules, and one version guards all of it. So two writers cannot change the aggregate at the same
time without one of them being refused. You pay for this on every load and every save. Code loads the whole aggregate
to change any part of it, and a write to any part conflicts with a write to any other part. Draw the boundary too
wide, and you pay for rules that did not need it. Draw it too narrow, and a rule that must always hold can break
between two saves.

Neither mistake shows where it is made. Both compile, and both look reasonable in review. So the pattern comes with
rules of thumb, and they are worth knowing before the first aggregate. This page goes through them and says where the
toolkit helps and where it does not. [Entities and aggregates](entities-and-aggregates.md) has the mechanics.

## The four rules of aggregate design

Vaughn Vernon's *Effective Aggregate Design* gives four rules of thumb. They are a good short summary of the pattern,
and a fair way to measure what a toolkit does. This is how this toolkit measures up:

| Rule | What the toolkit does |
|---|---|
| 1. Model true invariants in consistency boundaries | Gives you the boundary, the version, and two places to state a rule. Both run at every commit. You write the rules. |
| 2. Design small aggregates | Nothing. It can even make large aggregates easier to build. |
| 3. Reference other aggregates by identity | Generates the id type, and warns when you hold a root instead of its id. |
| 4. Use eventual consistency outside the boundary | Domain events, the outbox and the inbox. |

### Rule 1: model true invariants in consistency boundaries

Supported, as far as a library can go. You still write the rules. The toolkit decides where they run.

`[AggregateRoot<TId>]` draws the boundary, and `Version` enforces it. One save is one aggregate, and a second writer
with an old version is refused instead of merged. Child entities are owned, so they load and save with the root, and
no code can write them without the root. Private setters and a generated protected constructor mean that only your
methods can change the state.

Every entity and aggregate root also gets a place to state its rules: the generated seam,
`partial void CheckInvariants()`, or one nested `IInvariant<T>` per rule. `UseDDDToolkit` registers an interceptor that
runs the rules before every `SaveChanges` that writes the entity. A broken rule stops the save.
[Invariants](entities-and-aggregates.md#invariants) on the entities page shows the seam.

A check on the root covers the whole aggregate: the root's rules, then every child entity the root holds. That is
Vernon's first rule in code. A check that covered only the root object would not check the boundary.

A library can promise three things here:

- a place to write each rule;
- a guarantee that the rule runs at the commit, and not only where somebody called it;
- one call that covers everything inside the boundary.

[Invariants](invariants.md) has both kinds of rule, the stage that returns violations instead of throwing, what one
check covers, and the order of the interceptors. It also has the important limit. The checks cannot check an
invariant that spans two aggregates, and they should not.

A guard clause in the method that makes the change is still right, next to the invariants. The guard refuses the
command with a message the caller can act on. The seam checks every path into the state, including paths you add
later:

```csharp
public void Ship(TrackingCode code)
{
    if (Status != OrderStatus.Paid)
    {
        throw new InvalidOperationException("Only a paid order can ship.");
    }

    Status = OrderStatus.Shipped;
    RaiseDomainEvent(new OrderShipped(Id, code));
}
```

Value objects are a separate topic. `[ValueObject]` and `[SingleValueObject<T>]` carry rules through `Validate` and the
always-valid twin. See [Value objects](value-objects.md). That is the validation of one value, which differs from an
invariant across several objects. [Invariants and validation](invariants.md#invariants-and-validation) compares the
two.

The word "true" in the rule carries the meaning, and no tool can check it. A true invariant must hold at the end of
every transaction. A rule that may be a minute late is not a true invariant. Pulling such a rule inside the boundary,
to be safe, is how aggregates grow too large.

### Rule 2: design small aggregates

Not supported. The toolkit helps least with this rule, and in one way it works against the rule.

The reason is [read-only collections](entities-and-aggregates.md#read-only-collections). Declaring one takes a single
line:

```csharp
public partial IReadOnlyList<OrderLine> Lines { get; }
```

With that line, you get the backing field, the read-only view and the Entity Framework mapping. The feature is useful,
but it removes a moment of friction. Writing the field, the view and the `[BackingField]` by hand takes a minute. In
that minute, you may wonder whether the collection belongs in the aggregate. A one-line declaration gives you no such
minute.

Nothing later catches a large collection either. `[BackingField]` maps whatever you declared.
[DDD00021](diagnostics.md#ddd00021) checks the *type* in a collection, and never its size. It reports
`IReadOnlyList<Customer>`, and it says nothing about an `IReadOnlyList<OrderLine>` that holds a hundred thousand
lines. No diagnostic checks aggregate size, and no useful one can. "Too big" depends on your invariants and your write
patterns, and a compiler can see neither.

So you make this check. Before you add a collection to an aggregate, ask these questions:

**Does a rule in this aggregate read the whole collection?** The rule must read all elements, not one. "The order total
may not exceed the credit limit" reads every line, so the lines belong inside. If no rule needs the whole collection,
the collection is part of no invariant, and you are storing a query result in an object graph.

**Can an element exist without this root?** If a `Customer` can outlive an `Order`, the collection refers to another
aggregate. Typed as `IReadOnlyList<Customer>`, the analyzer reports it. Typed as
`IReadOnlyList<CustomerSummary>`, where `CustomerSummary` is a child entity made up to avoid the diagnostic, nothing
reports it, but the boundary is broken in the same way.

**Does the domain set an upper limit?** "An order has lines" is limited by what one person buys at one time. "A
customer has orders" has no limit. The list grows for as long as the customer stays. Collections without a limit are
where aggregates go wrong. The domain language shows them long before a profiler does.

**How many people write to the collection at the same time?** Every write to any element changes the root's
`Version`. Two users who add a line to the same order cause no problem. Two hundred warehouse scanners that add events
to the same shipment produce a queue of `ConcurrencyConflictException`s. The fix is a smaller aggregate. A retry loop
does not help.

**What would break if this were a list of ids?** Often the answer is "a few queries would get longer". That is the
trade-off, and it is usually worth it.

A large aggregate fails no build and no test. It fails in production, as lock contention and as `SaveChanges` calls
that load more than they need. By then the aggregate is in your schema. The toolkit gives no warning about it, so this
section is the warning.

### Rule 3: reference other aggregates by identity

Supported, and checked.

`[EntityId<T>]` gives you the id type, and `[AggregateRoot<Guid>("ORD")]` generates it for you.
[DDD00021](diagnostics.md#ddd00021) reports a field or property typed as another root. The attributes tell the
analyzer which of your types are roots and which are ids. So this is one of the few DDD rules that a tool can check.

DDD00021 is a warning, and it covers fields and properties only. A method that takes another root as a parameter is
fine, and it is often the right shape. `order.PlaceFor(customer)` reads better than `order.PlaceFor(customer.Id)`, and
it stores the id either way.
[Reference other aggregates by id](entities-and-aggregates.md#reference-other-aggregates-by-id) has the full
reasoning, and the one navigation that is allowed.

### Rule 4: use eventual consistency outside the boundary

Supported. This part of the toolkit has the most code behind it.

An aggregate raises a domain event. The event leaves the boundary, and the objects it affects catch up afterwards:

- `RaiseDomainEvent` raises the event inside the aggregate, and the persistence layer collects it. See
  [Domain events](domain-events.md).
- The [outbox](event-delivery.md#the-outbox) writes one row per event in the same transaction as the aggregate. So the
  event is not lost when the save succeeds, and it is not sent when the save fails.
- [Integration events](integration-events.md) give the event a published contract and a sink, so the code that catches
  up can run in another process.
- The [inbox](integration-events.md#each-handler-has-its-own-inbox-row) makes the receiving side idempotent, which
  at-least-once delivery requires.

Eventual consistency still has costs. Delivery is at-least-once, and the order of events is best-effort. A handler can
fail after other handlers succeeded. A reader can see one aggregate updated and another not yet. These are properties
of the approach, and the pages above say where each one matters.

Rule 4 connects back to rule 1. If you want a transaction across two aggregates, ask whether the rule that forces it is
a true invariant. If it is, the two aggregates are one aggregate. If it is not, use an event.
