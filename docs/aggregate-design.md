# Designing aggregates

Declaring an aggregate is one attribute. Deciding what belongs inside it is the part that matters, and
the part no attribute does for you.

An aggregate is a trade. Everything inside the boundary is consistent at the end of every transaction:
its rules are checked at every save, and one version guards all of it, so two writers cannot change it
at once without one of them being refused. The price is paid on every load and every save. The whole
aggregate is loaded to change any part of it, and a write to any part conflicts with a write to any
other. Draw the boundary too wide and you pay that price for rules that did not need it. Draw it too
narrow and a rule that must always hold can be broken between two saves.

Neither mistake shows where it is made. Both compile, and both read reasonably in review. That is why
the pattern comes with rules of thumb, and why they are worth knowing before the first aggregate rather
than after the tenth. This page goes through them and says where the toolkit helps and where it does
not. [Entities and aggregates](entities-and-aggregates.md) has the mechanics.

## The four rules of aggregate design

Vaughn Vernon's *Effective Aggregate Design* gives four rules of thumb. They are the best short
summary of the pattern anyone has written, and they are a fair way to ask what a toolkit is actually
worth. Here is how this one scores against them.

| Rule | What the toolkit does |
|---|---|
| 1. Model true invariants in consistency boundaries | Gives you the boundary, the token, and two places to state a rule, both run at every commit. You write the rules. |
| 2. Design small aggregates | Nothing. Arguably it makes large ones easier to build. |
| 3. Reference other aggregates by identity | Generates the identity and warns when you do not use it. |
| 4. Use eventual consistency outside the boundary | Domain events, the outbox and the inbox. |

### Rule 1: model true invariants in consistency boundaries

Supported, as far as a library can go. The rules are still yours to write; where they run is not.

`[AggregateRoot<TId>]` draws the boundary and `Version` makes it real: one save is one aggregate, and
a second writer with a stale version is refused rather than merged. Child entities are owned, so they
load and save with the root and cannot be written behind its back. Private setters and a generated
protected constructor mean the only way into the state is through a method you wrote.

On top of that, every entity and aggregate root gets somewhere to state its rules, either a generated
`partial void CheckInvariants()` seam or a nested `IInvariant<T>` per rule, and `UseDDDToolkit`
registers an interceptor that runs them before every `SaveChanges` that writes the entity. A broken
rule stops the save. [Invariants](entities-and-aggregates.md#invariants) on the entities page shows
the seam.

Asking the root asks the whole aggregate: its own rules and then every child entity it holds, which is
Vernon's first rule stated in code rather than in prose. A boundary that answered only for the object
at its centre would not be one.

That is the whole of what a library can promise here: the place to write the rule, the guarantee that
it runs at the commit rather than wherever somebody remembered, and one call that covers everything
inside the boundary. [Invariants](invariants.md) has both shapes of rule, the second stage that asks
instead of throwing, what one question covers, the interceptor order, and the limitation that matters,
which is that an invariant spanning two aggregates cannot be checked this way and should not be.

A guard clause in the method that makes the change is still right, and the two are not in
competition. The guard refuses the command with a message the caller can act on; the seam is the net
under every path into the state, including the ones you add next year:

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

Value objects are a different thing again: `[ValueObject]` and `[SingleValueObject<T>]` carry rules
through `Validate` and the always-valid twin. See [Value objects](value-objects.md). That is
validation of one value, not an invariant across a cluster, and the two are worth keeping apart in
your head. [Invariants](invariants.md#why-here-and-not-in-a-validator) lays the two side by side.

The word "true" in the rule is doing the work anyway, and no tool can check it. A true invariant is a
rule that must hold at the end of every single transaction. A rule that may be a minute late is not
one, and dragging it inside the boundary to be safe is how aggregates get big.

### Rule 2: design small aggregates

Not supported. This is the rule the toolkit is least help with, and on one reading it works against
it.

The mechanism is [read-only collections](entities-and-aggregates.md#read-only-collections). Declaring
one is a single line:

```csharp
public partial IReadOnlyList<OrderLine> Lines { get; }
```

and you get the backing field, the read-only view and the Entity Framework mapping. That is a good
feature, and the cost of it is that the moment of friction is gone. Writing the field, the view and the
`[BackingField]` by hand takes a minute, and a minute is long enough to wonder whether the collection
belongs there. A one-line declaration is not.

Nothing downstream catches it either. `[BackingField]` maps whatever you declared.
[DDD00021](diagnostics.md#ddd00021) checks the *type* in the collection, not the size of it: it stops
`IReadOnlyList<Customer>` and says nothing at all about `IReadOnlyList<OrderLine>` holding a hundred
thousand lines. There is no diagnostic for aggregate size, and there is not going to be a useful one,
because "too big" is a question about your invariants and your write patterns and a compiler can see
neither.

So the check is yours. Before adding a collection to an aggregate, ask:

**Does a rule in this aggregate read the whole collection?** Not one element, all of them. "The order
total may not exceed the credit limit" reads every line, so the lines belong inside. If no rule needs
the collection as a whole, it is not part of any invariant, and you are storing a query result in an
object graph.

**Can an element exist without this root?** If a `Customer` can outlive an `Order`, the collection is a
reference to another aggregate wearing a collection's clothes. Typed as `IReadOnlyList<Customer>` the
analyzer catches it. Typed as `IReadOnlyList<CustomerSummary>`, where `CustomerSummary` is a child
entity you invented to get around it, nothing catches it and the boundary is just as broken.

**Is it bounded by something the domain guarantees?** "An order has lines" is bounded by what one
person will buy in one go. "A customer has orders" is bounded by nothing: it grows for as long as the
customer stays. Unbounded collections are where aggregates go wrong, and they are obvious in the
domain language long before they are obvious in a profiler.

**How many people write to it at once?** Every write to any element takes the root's `Version`. Two
users adding a line to the same order is fine. Two hundred warehouse scanners adding events to the same
shipment is a queue of `ConcurrencyConflictException`, and the fix is a smaller aggregate, not a retry
loop.

**What would break if this were a list of ids?** Often the honest answer is "a few queries would get
longer". That is the trade, and it is usually the right one.

A large aggregate does not fail a build or a test. It fails in production, as lock contention and as
`SaveChanges` calls that load more than they needed, and by then it is in your schema. The toolkit
gives you no warning about it. This section is the warning.

### Rule 3: reference other aggregates by identity

Supported, and checked.

`[EntityId<T>]` gives you the id type, `[AggregateRoot<Guid>("ORD")]` generates it for you, and
[DDD00021](diagnostics.md#ddd00021) reports a field or property typed as another root. The analyzer
knows which of your types are roots and which are ids because the attributes told it, so this is one of
the few DDD rules a tool can genuinely check rather than lecture about.

It is a warning, not an error, and it covers fields and properties only. A method that takes another
root as a parameter is fine and is often the right shape:
`order.PlaceFor(customer)` reads better than `order.PlaceFor(customer.Id)` and stores the id either
way. [Reference other aggregates by id](entities-and-aggregates.md#reference-other-aggregates-by-id)
has the full reasoning and the one navigation that is allowed.

### Rule 4: use eventual consistency outside the boundary

Supported, and it is the part of the toolkit with the most machinery behind it.

An aggregate raises a domain event. The event leaves the boundary, and whatever it touches catches up
afterwards:

- `RaiseDomainEvent` inside the aggregate, drained by the persistence layer. See
  [Domain events](domain-events.md).
- The [outbox](event-delivery.md#the-outbox) writes one row per event in the same transaction as the
  aggregate, so the event cannot be lost when the save succeeds or survive when it fails.
- [Integration events](integration-events.md) give the event a published contract and a sink, so the
  thing catching up can be in another process.
- The [inbox](integration-events.md#the-inbox-on-the-other-side) makes the receiving side idempotent,
  which is what at-least-once delivery requires of it.

What this does not do is make eventual consistency free. Delivery is at-least-once and ordering is
best-effort, a handler can fail after other handlers succeeded, and a reader can see one aggregate
updated and another not yet. Those are properties of the approach, not gaps in the implementation, and
the pages above say where each one bites.

The rule that matters here is the one about rule 1: if you find yourself wanting a transaction across
two aggregates, the question is whether the rule forcing it is really a true invariant. If it is, the
two aggregates are one. If it is not, an event is the answer.
