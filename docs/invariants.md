# Invariants

An invariant is a rule that must be true of a whole aggregate every time anyone can look at it. Not
"this field is required" and not "this input looks wrong", but "an order with no lines is not an
order", "a tab may never total more than its limit", "exactly one address on a customer is the
billing address". Vernon's first rule of aggregate design is to model true invariants within one
consistency boundary, and this is the place the toolkit gives you to write them down.

The toolkit does not define any invariants for you. It cannot: yours are about your domain. What it
supplies is the seam to put them in, and the guarantee that the seam is called.

## The seam

Every `[Entity<TId>]` and `[AggregateRoot<TId>]` gets a generated partial method. Implement it in
your own part of the class:

```csharp
[AggregateRoot<OrderId>]
public partial class Order
{
    public partial IReadOnlyList<OrderLine> Lines { get; }

    public OrderStatus Status { get; private set; }

    partial void CheckInvariants()
    {
        if (Status != OrderStatus.Draft && Lines.Count == 0)
        {
            throw InvariantViolation("A placed order must have at least one line.");
        }
    }
}
```

Write it exactly like that: `partial void`, no accessibility modifier, in a file of your own. The
generator writes the other half.

This is the same shape the [FluentValidation integration](value-objects.md) uses for validators. The
toolkit writes the declaration, you write the rules, and the two halves are one class.

`InvariantViolation(...)` is a protected helper on `Entity<TId>`. It builds an
`InvariantViolationException` that already knows this aggregate's type and id, so the message names
the thing that is broken. You are free to throw anything you like instead; the toolkit only insists
that a broken invariant stops the operation.

### Reporting more than one problem

One check can report everything it found:

```csharp
partial void CheckInvariants()
{
    var problems = new List<string>();

    if (Lines.Count == 0) problems.Add("An order must have at least one line.");
    if (Total > CreditLimit) problems.Add($"An order may not exceed the credit limit of {CreditLimit}.");

    if (problems.Count > 0) throw InvariantViolation(problems);
}
```

The exception carries them in `Violations`, and its message lists them all.

### Why the seam returns nothing

A seam that returned a list of violations would read better at first glance. It cannot work here. C#
only erases a partial method that returns `void`; a partial method with any other return type must
have an implementing declaration, so every entity in your solution would be forced to write an empty
one. Because the seam is `partial void`, an aggregate that states no invariants is left with an
`EnsureInvariants` whose compiled body is a single `ret` instruction. There is a test that reads the
emitted IL and asserts exactly that, so the claim stays true.

The cost of reporting several problems at once is therefore a list you build yourself, in the one
aggregate that has several rules, rather than a method every aggregate has to declare.

## When it runs

### At the commit

The consistency boundary is the transaction. An aggregate is allowed to be inconsistent halfway
through one of its own methods; what it promises is that it is never *stored* broken. So that is
where the toolkit checks.

`UseDDDToolkit` registers an `InvariantInterceptor`. Before every `SaveChanges`, it calls
`EnsureInvariants()` on every aggregate root the save adds or modifies — including a root whose only
change is to something it owns, found the same way the concurrency interceptor finds roots to
version. A root that throws stops the save, and nothing is written.

```csharp
services.AddDDDToolkitEntityFramework(options => options.DispatchInProcess(...));
services.AddDbContext<ShopContext>((sp, db) => db.UseSqlite(cs).UseDDDToolkit(sp));
```

Nothing else to switch on. The interceptors run in this order:

| Order | Interceptor | Why there |
|---|---|---|
| 1 | `PublishDomainEventsInterceptor` | Handlers run before the save and may change tracked aggregates. |
| 2 | `InvariantInterceptor` | So it sees whatever those handlers changed. |
| 3 | `AggregateVersionInterceptor` | Last, so a save the invariants reject leaves no version bumped. |

Two things it deliberately does not do. It does not check an aggregate that is being deleted: a row
on its way out has no state left to be consistent about. And it does not check an aggregate that
this save does not touch, even when that aggregate is loaded and broken. Rows that were already
wrong are a migration problem, not this save's problem.

### By hand

`EnsureInvariants()` is public on every entity and aggregate root, so a unit test or a command
handler can assert consistency with no database anywhere:

```csharp
var order = new Order(OrderId.CreateUnique(), customer);
order.Place();

order.EnsureInvariants();   // throws InvariantViolationException: no lines
```

`InvariantInterceptor.CheckInvariants(context)` runs the same pass over a `DbContext` if you have a
unit of work of your own and want the check at a moment of your choosing.

### Not automatically on children

`EnsureInvariants()` checks the object you called it on, and no further. A child entity has a seam of
its own, but nothing calls it for you, because only the root knows which of its children a rule is
about. If you want them, call them:

```csharp
partial void CheckInvariants()
{
    foreach (var line in Lines)
    {
        line.EnsureInvariants();
    }
}
```

That is one line in the one place that knows it is wanted, instead of a reflective walk over every
object graph on every save.

## Why here and not in a validator

Validation and invariants answer different questions, and the toolkit keeps them apart on purpose.

| | Validation | Invariant |
|---|---|---|
| Question | "Is this input acceptable?" | "Can this state exist at all?" |
| Subject | One value object or one field | The whole aggregate |
| Audience | The user who typed it | The programmer who wrote the method |
| Toolkit support | `[ValueObject]`, `ValidationError`, FluentValidation | `CheckInvariants()` |
| Failure | `ValidationError`s you show in a form | `InvariantViolationException`, a bug |

A validator sees one object at a time. "An order may not exceed the customer's credit limit" is about
the lines, the header and a number that arrived from elsewhere; there is nothing to hang a property
rule on. And a validator runs when you ask it to, which means it runs where somebody remembered. The
seam runs at the commit, every time, which is the only place that makes it a guarantee.

An `InvariantViolationException` is therefore not something to show a user. If one reaches
production, an aggregate method let its own object into a state the domain says cannot exist: fix the
method, or the caller. Catch `ConcurrencyConflictException` and retry; catch a validation failure and
report it; an invariant violation is neither.

## The limitation you should know about

**An invariant that spans two aggregates cannot be checked this way, and should not be.**

"A customer's outstanding orders may not exceed their credit limit" is a rule about a `Customer` and
about every `Order` that references it. The seam cannot enforce it. `Order.CheckInvariants()` only
sees the order; it would have to load the customer and every sibling order, and even then two
concurrent saves could each see a total that was true a moment ago and write a pair that is not.

This is not a gap in the toolkit. It is the reason the aggregate boundary exists. A rule you insist
on enforcing transactionally is a rule that says those objects are one aggregate, with one root, one
version and one lock — which is a real design choice, with a real cost in contention. If they are not
one aggregate, the rule is eventually consistent, and you have to say what happens in the window:

- Raise a domain event (`OrderPlaced`), handle it, and have the handler check the rule and react —
  reject the order, flag the customer, open a task for a human. See
  [Domain events](domain-events.md) and the [outbox](entity-framework.md#the-outbox).
- Or move the number that has to be exact inside one aggregate. A `CreditLine` aggregate that holds
  the reserved amount can guarantee its own total, and the order reserves against it first.

Either way, the gap is in your design, and the honest thing is to name it rather than to let a
framework pretend it closed it. What the toolkit checks is what it can actually guarantee: one
aggregate, one transaction.

## What this costs

An aggregate that never implements `CheckInvariants()` pays nothing at run time. The compiler removes
an unimplemented partial method and every call to it, so the generated `EnsureInvariants` override is
an empty method body, and the interceptor's call on it does no work. There is no reflection, no
attribute scan and no expression tree anywhere in this feature.

The interceptor does walk the change tracker to find the roots a save touches, which is the same walk
`AggregateVersionInterceptor` already does for versioning.

## See also

- [Entities and aggregates](entities-and-aggregates.md) — what belongs inside the boundary.
- [Value objects](value-objects.md) — validation of a single value, which is the other half.
- [Entity Framework](entity-framework.md) — the interceptors and what each one guarantees.
