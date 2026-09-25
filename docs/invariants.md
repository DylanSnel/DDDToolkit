# Invariants

An invariant is a rule that must be true of a whole aggregate every time anyone can look at it. Not
"this field is required" and not "this input looks wrong", but "an order with no lines is not an
order", "a tab may never total more than its limit", "exactly one address on a customer is the
billing address". Vernon's first rule of aggregate design is to model true invariants within one
consistency boundary, and this is the place the toolkit gives you to write them down.

Written as an `if` in the handler that changes the aggregate, such a rule holds for as long as every
handler remembers it. The next handler that removes a line without checking stores an order the domain
says cannot exist, and nothing notices until somebody reads the row. A rule stated on the aggregate is
asked of the aggregate, whichever method changed it.

The toolkit does not define any invariants for you. It cannot: yours are about your domain. What it
supplies is somewhere to write them, two moments at which they are asked, and the guarantee that the
second of those moments is never skipped.

This page starts by writing a rule, then says how and when it is asked and how much one question
covers. It ends with where the rules stop, and with the reasoning behind the design.

## Stating a rule

There are two shapes, and the difference between them is not a style preference.

### The seam

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

`InvariantViolation(...)` is a protected helper on `Entity<TId>`. It builds an
`InvariantViolationException` that already knows this aggregate's type and id, so the message names
the thing that is broken. There is an overload taking several messages, so one check can report
everything it found:

```csharp
partial void CheckInvariants()
{
    var problems = new List<string>();

    if (Lines.Count == 0) problems.Add("An order must have at least one line.");
    if (Total > CreditLimit) problems.Add($"An order may not exceed the credit limit of {CreditLimit}.");

    if (problems.Count > 0) throw InvariantViolation(problems);
}
```

The seam reports by throwing, but an aggregate can also be asked without throwing, which is the first
of the [two stages](#the-two-stages) below. Asked, it catches whatever the seam threw, unpacks it, and
reports violations carrying `InvariantViolation.SeamCode`, which is the literal string
`"CheckInvariants"`. The seam has nowhere to put a code of its own, so it gets one that says where it
came from.

### A named invariant

The other shape is one rule, one type, implementing `IInvariant<T>`, nested inside the entity it is
about:

```csharp
// Ordering/Invariants/MustHaveLines.cs
public partial class Order
{
    public sealed class MustHaveLines : IInvariant<Order>
    {
        /// <summary>What a caller branches on, so nobody has to match on the message.</summary>
        public const string ViolationCode = "ORDER_HAS_NO_LINES";

        public string Code => ViolationCode;

        public InvariantFailure? Check(Order order)
            => order.Status != OrderStatus.Draft && order._lines.Count == 0
                ? "A placed order must have at least one line."
                : null;
    }
}
```

`Check` returns `null` when the rule holds, and otherwise what is wrong in the domain's own words. It
returns an `InvariantFailure`, and a string converts to one, so a rule with nothing more to say returns
its message. Not a violation object so that holding is free: this runs for every changed entity on
every save, and the consistent path returns `null` and allocates nothing. The generated code pairs the
failure with `Code`, so the code is written down once.

A message that names values should hand them on as well, so it can be
[phrased in another language](localization.md) without the number already baked into the sentence:

```csharp
public InvariantFailure? Check(Order order)
    => order.Total <= order.CreditLimit
        ? null
        : new InvariantFailure($"An order may total at most {order.CreditLimit}.")
            .With("CreditLimit", order.CreditLimit);
```

They arrive on the violation as `InvariantViolation.Arguments`.

The generator finds these, builds one instance of each in a static array, and runs them before the
seam. Both stages run the same array. For the `Order` above, with `MustHaveLines` and a seam, it writes
the seam's declaration, the array, and the one routine that runs them:

```csharp title="Order.g.cs, shortened"
partial class Order : AggregateRoot<Shop.OrderId>
{
    // ...

    partial void CheckInvariants();

    private static readonly IInvariant<Shop.Order>[] __invariants =
    [
        new Shop.Order.MustHaveLines(),
    ];

    private void CollectInvariantViolations(
        ref List<InvariantViolation>? violations,
        out InvariantViolationException? seamFailure)
    {
        foreach (var invariant in __invariants)
        {
            // Null means the rule holds, and a rule that holds allocates nothing.
            var failure = invariant.Check(this);
            if (failure is not null)
            {
                violations ??= new List<InvariantViolation>();
                violations.Add(new InvariantViolation(invariant.Code, failure.Message, typeof(Shop.Order), Id) { Arguments = failure.Arguments });
            }
        }

        seamFailure = null;

        try
        {
            CheckInvariants();
        }
        catch (InvariantViolationException failure)
        {
            // ... kept as seamFailure, and each message it carries becomes a violation with InvariantViolation.SeamCode
        }
    }
```

Nothing is looked up when the application runs: the rules are named in the array, and the list of
violations is created on the first failure, so a consistent order allocates nothing here.

### Which one to reach for

The seam is less ceremony, and for a small rule that is the whole argument. One `if` inside the class
it is about, no extra type, no extra file, no code to invent, no name to agree on. An aggregate with
a single rule that fits on one line is not better off with a nested class, and a codebase that
insists on one is paying for structure it does not use. Reach for the seam by default.

An invariant object earns its keep when one of these is true:

- **The rule deserves a name.** `MustShipSomewhereWeDeliver` says in the file tree what a condition
  buried in an `if` says only to whoever reads the body.
- **A caller needs to branch on which rule broke.** This is the big one. Violations from the seam all
  carry `SeamCode`, so an application that wants to answer differently for "no lines" than for "over
  the credit limit" cannot tell them apart without matching on the message, and a message is not an
  API. A named rule has a code, and the nested type gives that code a home:
  `Order.MustHaveLines.ViolationCode`.
- **The rule deserves a test of its own.** `new Order.MustHaveLines().Check(order)` is a unit test
  with no aggregate mutation, no exception to catch and no database. The seam can only be tested
  through the entity.
- **There are several rules and they keep arriving.** A seam with six `if` statements and a
  hand-built list of problems has become a file of its own trying to get out. Six rules report
  themselves without any of them knowing about the others.

An entity can have both, and mixing them is normal: the rules that earned a name get one, and the
one-liner that never will stays in the seam. That is what
[`Examples/ModularMonolith.Supabase`](../Examples/ModularMonolith.Supabase) does, with a named rule on the order, a
named rule on the line, and one seam left where it belongs.

If you want to find the rules that should be promoted, branch on `InvariantViolation.SeamCode`: every
violation carrying it came from a seam and has no code a caller can use.

## The two stages

A broken invariant means two different things depending on when you ask.

**Before the save, the application is asking a question.** A user added a line, a handler took the
last item out of a basket, a command half-built an aggregate it intends to finish later. "Not
consistent yet" is a real answer here, and the caller wants to do something with it: show it, log it,
put it in a response. `GetInvariantViolations()` answers that question and never throws. An empty
list means consistent.

**At the save, nobody is asking.** The transaction is the consistency boundary, and an aggregate that
is about to be written broken has already gone wrong somewhere upstream. `EnsureInvariants()` throws
`InvariantViolationException`, the save stops, and nothing reaches the database.

```csharp
var broken = order.GetInvariantViolations();   // stage 1: what is wrong, if anything
order.EnsureInvariants();                      // stage 2: nothing is wrong, or nothing is written
```

| | `GetInvariantViolations()` | `EnsureInvariants()` |
|---|---|---|
| Asks | "Is this acceptable yet?" | "Prove you may be stored." |
| Answers with | `IReadOnlyList<InvariantViolation>` | nothing, or an exception |
| Consistent | an empty list | returns |
| Broken | the violations, each with its code | `InvariantViolationException` |
| Called by | your application, when it wants to know | the save, before every write ([At the save](#at-the-save)) |
| Both answer for | the whole aggregate, children included | the same |

That last row is the one people are surprised by, so it gets a section of its own below.

### Why one of them throws

Because of who is holding it. The save-time call happens in the middle of a save, which has nowhere to
put a list: there is no caller in the middle waiting to read one, and a return value nobody is
positioned to read is a return value that gets dropped. An exception is the only answer that cannot be
ignored by accident, and being unable to ignore it by accident is the entire guarantee. Turning it into
a return value would make the strongest promise in the toolkit depend on every future caller
remembering to check.

And because of what has already happened. By the save, the aggregate has been mutated by its own
methods and the state is a fact, not a proposal. There is no question left to answer.

What the two stages do *not* do is disagree. The generated code runs both through one routine that
collects violations, and the two differ only in how they end: one returns the list, the other throws
once with every message in it:

```csharp title="Order.g.cs, shortened"
    public override IReadOnlyList<InvariantViolation> GetInvariantViolations()
    {
        List<InvariantViolation>? violations = null;
        CollectInvariantViolations(ref violations, out _);
        CollectChildInvariantViolations(ref violations);

        if (violations is null)
        {
            return Array.Empty<InvariantViolation>();
        }

        return violations;
    }

    public override void EnsureInvariants()
    {
        List<InvariantViolation>? violations = null;
        CollectInvariantViolations(ref violations, out var seamFailure);

        CollectChildInvariantViolations(ref violations);

        if (violations is null)
        {
            return;
        }

        ThrowInvariantViolations(violations, seamFailure);
    }
```

`CollectChildInvariantViolations` is the part that asks the lines, and the next section is about it.
Nothing in the toolkit uses exceptions as control flow to get there, either. An `IInvariant<T>` never
throws at all, and the only throw on the way is the one out of your own seam, which is caught in one
place so the asking stage can see what it found.

## What one question covers

Asking an aggregate root whether it is consistent answers for the whole aggregate: this object's own
rules and its seam first, then every child entity it holds, each of which answers for its own children
the same way. One call, one list, and the aggregate either may exist or may not.

That is what "the aggregate is the consistency boundary" means once it is code instead of prose. The
root *is* the boundary, and a boundary that answers only for the object at its centre is not a
boundary, it is a field check. A handler that adds a line, asks the order whether that was allowed,
and hears nothing about lines has been given an answer about half the aggregate, with no sign that the
other half was skipped.

A child entity states its own rules in the same two shapes. A line that names nothing is broken
whatever order it is on, so that rule belongs to the line:

```csharp
public partial class OrderLine
{
    public sealed class MustNameASku : IInvariant<OrderLine>
    {
        public const string ViolationCode = "LINE_HAS_NO_SKU";

        public string Code => ViolationCode;

        public InvariantFailure? Check(OrderLine line)
            => string.IsNullOrWhiteSpace(line.Sku) ? "A line must name the thing it is ordering." : null;
    }
}
```

`Order` never mentions it, and asking the order reports it:

```csharp
var broken = order.GetInvariantViolations();   // the order's rules, and every line's
order.EnsureInvariants();                      // the same, and throws instead of answering
```

The walk is generated from the collections the entity declares, so it costs nothing to opt into and
nothing to keep true. For `Order` that is the field behind `Lines`:

```csharp title="Order.g.cs, shortened"
    private void CollectChildInvariantViolations(ref List<InvariantViolation>? violations)
    {
        foreach (var child in _lines)
        {
            if (child is null)
            {
                continue;
            }

            var broken = child.GetInvariantViolations();
            if (broken.Count == 0)
            {
                continue;
            }

            violations ??= new List<InvariantViolation>();
            for (var index = 0; index < broken.Count; index++)
            {
                violations.Add(broken[index]);
            }
        }
    }
```

Each line answers with its own `GetInvariantViolations()`, so a child that held children of its own
would ask them in turn. A child collection added next year is asked without anybody editing the root,
and a rule added to `OrderLine` is reported by `Order` the day it compiles.

### A handler asks before it saves

This is the case the walk exists for, and it is a domain question from end to end: the handler acts on
an aggregate it is holding, asks what that broke, and refuses. Loading and saving need a `DbContext`,
of course. Deciding whether the aggregate may exist does not, and none takes part in it: the subject
is a graph of objects in memory, and nothing a database knows changes the answer.

```csharp
public async Task<IResult> Handle(AddLine command, CancellationToken cancellationToken)
{
    var order = await orders.FindAsync([command.Order], cancellationToken);
    if (order is null)
    {
        return Results.NotFound();
    }

    order.AddLine(command.Sku, command.Quantity);        // the domain acts

    var violations = order.GetInvariantViolations();     // and answers for its lines as well
    if (violations.Count > 0)
    {
        return Results.UnprocessableEntity(violations.Select(v => new
        {
            v.Code,
            v.Message,
            entity = v.EntityType?.Name,                 // "OrderLine"
            id = v.EntityId?.ToString(),                 // "LINE_0199b1c0-..."
        }));
    }

    await context.SaveChangesAsync(cancellationToken);   // still guarded, by the interceptor
    return Results.Ok();
}
```

Every violation names the entity that reported it, which is the difference between "something in this
order is wrong" and "line LINE_0199b1c0 names no SKU". The caller never has to search the aggregate
for the object a message was about, and never has to read the message to find out which rule broke,
because the code is there to branch on:

```csharp
if (violations.FirstOrDefault(v => v.Code == OrderLine.MustNameASku.ViolationCode) is { } blank)
{
    return Results.UnprocessableEntity(new { blank.Code, blank.Message, line = blank.EntityId });
}
```

A named rule is what makes that line possible; a rule left in the seam reports
`InvariantViolation.SeamCode` and tells the caller only where it came from. `EntityType` and
`EntityId` are nullable because a violation you construct by hand need not carry them. Every violation
the toolkit produces does.

`EnsureInvariants()` puts the same information in a message. The exception already names the boundary
that was asked, so the aggregate's own violations read plainly, and only a child's is prefixed with
that child's type and id:

```
The Order 'ORD_0199b1c0' broke 2 invariants: An order may not name the same SKU on two lines.
OrderLine LINE_0199b1c2 LINE_HAS_NO_SKU: A line must name the thing it is ordering.
```

That is one line, wrapped here. One `InvariantViolationException`, whatever the seam threw kept as its
inner exception, and `Violations` carrying the same messages for anyone who would rather read them
than the text.

## When it runs

### At the save

The consistency boundary is the transaction. An aggregate is allowed to be inconsistent halfway
through one of its own methods; what it promises is that it is never *stored* broken. So that is
where the toolkit checks.

`UseDDDToolkit` registers an `InvariantInterceptor`. Before every `SaveChanges` it runs the throwing
stage on every tracked entity the save adds or modifies, child entities included, and on the aggregate
root of each changed child. Anything that throws stops the save, and nothing is written.

```csharp
services.AddDDDToolkitEntityFramework();
services.AddDbContext<ShopContext>((sp, db) => db.UseSqlite(cs).UseDDDToolkit(sp));
```

Nothing else to switch on. The argument `AddDDDToolkitEntityFramework` usually takes says how domain
events are delivered, which is a separate matter; see
[Domain event delivery](event-delivery.md). The interceptors run in this order:

| Order | Interceptor | Why there |
|---|---|---|
| 1 | `PublishDomainEventsInterceptor` | Handlers run before the save and may change tracked aggregates. |
| 2 | `InvariantInterceptor` | So it sees whatever those handlers changed. |
| 3 | `AggregateVersionInterceptor` | Last, so a save the invariants reject leaves no version bumped. |

Two things it deliberately does not do. It does not check an entity that is being deleted: a row on
its way out has no state left to be consistent about. And it does not check an aggregate that this
save does not touch, even when that aggregate is loaded and broken. Rows that were already wrong are
a migration problem, not this save's problem.

### Children answer for their own rules

A child `[Entity<TId>]` has all four members of its own, and the save asks it directly. A line that
breaks a rule about lines reports it itself, in its own words, naming its own id, without the root
having to know the rule exists.

What the save calls on each of them is `EnsureOwnInvariants()`. It already has the whole list in front
of it, the root and each changed child alike, so asking any of them to walk would report a child
twice: once as itself and once through its root. Only a caller that is not enumerating the graph wants
the walking pair, which is the whole of the difference between the two pairs.

The list of who gets asked is built from the change tracker and never from a navigation property.
That matters more than it sounds: the tracker knows by construction only what was loaded and what
changed, so building the list cannot trigger a lazy load, cannot issue a query, and cannot fail on a
graph whose children were never loaded. A generated `foreach (var line in _lines)` could do all
three, which is why this lives in the persistence layer rather than in the entity.

**A rule that spans children still belongs on the root.** "No two lines may name the same SKU" is not
a rule of a line; no line can see its siblings, and no line knows it is the one at fault. Only the
root knows the whole, and only the root can state a rule about a child this save never touched:

```csharp
partial void CheckInvariants()
{
    if (_lines.Select(line => line.Sku).Distinct().Count() != _lines.Count)
    {
        throw InvariantViolation("An order may not name the same SKU on two lines.");
    }
}
```

**Do not call `child.EnsureInvariants()` from the root's seam.** The root already asks every child, so
a seam that does it again reports every broken line twice: once from the seam and once from the
generated walk. The rule about the children, like the one above, is what belongs in the seam.

### By hand

All four members are public on every entity and aggregate root, so a unit test or a command handler
can ask with no database anywhere:

```csharp
var order = new Order(OrderId.CreateUnique(), customer);
order.AddLine(sku: "", quantity: 1);

// [ InvariantViolation("LINE_HAS_NO_SKU", "A line must name...", typeof(OrderLine), LINE_0199...) ]
order.GetInvariantViolations();      // the order's rules and every line's
order.EnsureInvariants();            // the same, and throws instead of answering

order.GetOwnInvariantViolations();   // the order's own rules only, for a caller walking the graph
order.EnsureOwnInvariants();         // the same, and throws
```

For a whole unit of work rather than one aggregate, `InvariantInterceptor` exposes the same two stages
over a `DbContext`:

```csharp
InvariantInterceptor.CheckInvariants(context);                // throws, like the save would
var broken = InvariantInterceptor.GetInvariantViolations(context);   // asks, and writes nothing
```

Both pick their targets exactly as the interceptor does, so what they report is what the save would
do. Neither of them saves. Reach for these when the question spans several aggregates in one unit of
work; reach for the aggregate's own pair when you are holding the aggregate, which is most of the
time, and when you would rather your domain code did not mention a `DbContext` at all.

## Putting the violations in your own result type

The toolkit hands you the failures. It does not hand you a `Result<T>`, for the same reason
[value objects](value-objects.md#why-not-a-result-type) do not: teams already use FluentResults,
ErrorOr, OneOf or something of their own, a result type is infectious, and a codebase with one result
type ends up with two. Wrapping the call is a few lines, and they are yours:

```csharp
public static Result Consistency(this IHasInvariants entity)
{
    var violations = entity.GetInvariantViolations();

    return violations.Count == 0
        ? Result.Ok()
        : Result.Fail(violations.Select(v => new Error(v.Message)
            .WithMetadata("code", v.Code)
            .WithMetadata("entity", v.EntityId)));      // which child, when it was a child
}
```

One call per aggregate is the whole aggregate, children included, so a wrapper like this never needs
to know the shape of the graph it is asking about. Which makes the check stage an ordinary step in a
handler, with nothing thrown and nothing saved:

```csharp
public async Task<Result> Handle(AddLine command, CancellationToken cancellationToken)
{
    var order = await orders.FindAsync([command.Order], cancellationToken);

    order.AddLine(command.Sku, command.Quantity);

    var consistency = order.Consistency();
    if (consistency.IsFailed)
    {
        return consistency;      // nothing was saved, and nothing was thrown
    }

    await context.SaveChangesAsync(cancellationToken);
    return Result.Ok();
}
```

The `SaveChangesAsync` on the last line is still guarded. The handler asking first does not replace
the interceptor; it just means the answer arrives as a value in the one place that wanted it, and the
exception stays for the case nobody foresaw.

The code is what makes this worth doing. Branch on it, never on the message:

```csharp
if (violations.Any(v => v.Code == Order.MustStayWithinTheCreditLimit.ViolationCode))
{
    return Result.Fail(new CreditLimitExceeded());
}
```

And for a whole request, ask the context rather than each aggregate:

```csharp
var violations = InvariantInterceptor.GetInvariantViolations(context);
if (violations.Count > 0)
{
    return Results.UnprocessableEntity(violations.Select(v => new { v.Code, v.Message }));
}
```

To answer in the reader's language, pass them through `Localized(localizer)` first; see
[Localization](localization.md). On the throwing path the same violations, codes and arguments
included, are on `InvariantViolationException.InvariantViolations`.

Violations come back roots before the children they own, so the order is deterministic and reads the
way the aggregate is shaped. The aggregate's own walk orders them the same way: this object's rules,
then its seam, then each child collection in the order it was declared.

## Why here and not in a validator

Validation and invariants answer different questions, and the toolkit keeps them apart on purpose.

| | Validation | Invariant |
|---|---|---|
| Question | "Is this input acceptable?" | "Can this state exist at all?" |
| Subject | One value object or one field | The whole aggregate |
| Audience | The user who typed it | The programmer who wrote the method |
| Toolkit support | `[ValueObject]`, `ValidationError`, FluentValidation | `CheckInvariants()`, `IInvariant<T>` |
| Failure | `ValidationError`s you show in a form | `InvariantViolation`, or an exception at the save |

A validator sees one object at a time. "An order may not exceed the customer's credit limit" is about
the lines, the header and a number that arrived from elsewhere; there is nothing to hang a property
rule on. And a validator runs when you ask it to, which means it runs where somebody remembered. The
save-time stage runs at the commit, every time, which is the only thing that makes it a guarantee.

`InvariantViolation` and `ValidationError` are separate types for the same reason, so code reading one
is never handed the other. An `InvariantViolationException` that reaches production is not something
to show a user: an aggregate method let its own object into a state the domain says cannot exist, and
the fix is in the method or its caller. Catch `ConcurrencyConflictException` and retry; catch a
validation failure and report it; an invariant violation is neither.

## The limitation you should know about

**An invariant that spans two aggregates cannot be checked this way, and should not be.**

"A customer's outstanding orders may not exceed their credit limit" is a rule about a `Customer` and
about every `Order` that references it. Neither shape can enforce it. `Order`'s rules only see the
order; they would have to load the customer and every sibling order, and even then two concurrent
saves could each see a total that was true a moment ago and write a pair that is not.

This is not a gap in the toolkit. It is the reason the aggregate boundary exists. A rule you insist
on enforcing transactionally is a rule that says those objects are one aggregate, with one root, one
version and one lock, which is a real design choice with a real cost in contention. If they are not
one aggregate, the rule is eventually consistent, and you have to say what happens in the window:

- Raise a domain event (`OrderPlaced`), handle it, and have the handler check the rule and react:
  reject the order, flag the customer, open a task for a human. See
  [Domain events](domain-events.md) and the [outbox](event-delivery.md#the-outbox).
- Or move the number that has to be exact inside one aggregate. A `CreditLine` aggregate that holds
  the reserved amount can guarantee its own total, and the order reserves against it first.

Either way, the gap is in your design, and the honest thing is to name it rather than to let a
framework pretend it closed it. What the toolkit checks is what it can actually guarantee: one
aggregate, one transaction.

## Why a rule is a nested type

Nesting is required, not encouraged, and there are two reasons.

**It can read private state.** A nested type sees the enclosing type's private members, so
`MustHaveLines` above reads `_lines` directly. A rule living anywhere else would have to be handed
what it needs, which in practice means widening the aggregate's surface until the rule can see it.
Encapsulation lost so that a rule about encapsulation can run is a bad trade.

**It is free to discover.** The generator already holds the entity's symbol at the moment it writes
the entity, and nested types hang off that symbol. Finding rules by scanning the compilation for
implementations of `IInvariant<Order>` would make the generated output of every entity depend on
every file in the project, which is exactly what stops an incremental generator from being
incremental.

A file per rule, as another part of the entity, keeps that from turning into one enormous class:

```
Ordering/
  Order.cs                          the aggregate
  Invariants/
    MustHaveLines.cs                public partial class Order { public sealed class ... }
    MustShipSomewhereWeDeliver.cs
    MustStayWithinTheCreditLimit.cs
```

Two more requirements, both because the generator creates one instance per entity type and reuses it
for every check: a rule must be **stateless**, the entity it judges being the argument and never a
field, and it must have an **accessible parameterless constructor**.

Getting any of this wrong is reported rather than silently ignored, which is the point of the whole
design:

| | |
|---|---|
| [DDD00024](diagnostics.md#ddd00024) | A rule that is not nested inside an entity, so nothing runs it |
| [DDD00025](diagnostics.md#ddd00025) | A rule nested inside one type but written about another |
| [DDD00026](diagnostics.md#ddd00026) | Two rules of one entity answering to the same code |
| [DDD00027](diagnostics.md#ddd00027) | A rule the generated code cannot construct |

A rule that compiles, reads well, has its own passing unit test and never runs is the failure this
library exists to make impossible to ship unnoticed.

### Why the seam returns nothing

A seam that returned a list would read better than one that throws. It cannot work here. C# only
erases a partial method that returns `void`; a partial method with any other return type must have an
implementing declaration, so every entity in your solution would be forced to write an empty one.
Because the seam is `partial void`, an aggregate that states no invariants at all is left with an
`EnsureInvariants` whose compiled body is a single `ret` instruction. There is a test that reads the
emitted IL and asserts exactly that, so the claim stays true.

For a `Customer` with no rules, no seam and no child collections, the throwing checks the generator
writes consist of the call to the seam and nothing else, and the compiler removes that call:

```csharp title="Customer.g.cs, shortened"
    partial void CheckInvariants();

    // ...

    public override void EnsureOwnInvariants()
    {
        CheckInvariants();
    }

    public override IReadOnlyList<InvariantViolation> GetInvariantViolations() => GetOwnInvariantViolations();

    public override void EnsureInvariants()
    {
        CheckInvariants();
    }
```

Named rules do not have that constraint, which is the other thing they buy: an `IInvariant<T>` is an
ordinary type with an ordinary return value, and returning `null` for "fine" costs nothing.

## Collections, and not single references

The walk covers the child entity collections a type declares, and it reads the generated backing
field rather than the read-only property. It does not follow a single reference from one entity to
another.

That is a decision, not a missing line. A child holding a property back to its parent is an ordinary
shape, and following single references would walk root, child, root, child until the stack ran out.
Stopping that needs a visited set, and a visited set is an allocation on every call, including the
overwhelming majority where nothing is broken at all. The consistent path through an invariant check
allocates nothing today, and it runs for every changed entity on every save; paying for a `HashSet`
there to support a shape an aggregate does not have is the wrong trade.

What an aggregate does have is a root owning collections of children. Ruling cycles out by
construction costs nothing and misses nothing real. A single reference to *another* aggregate is not a
child at all, it is an id, and that aggregate's consistency is [its own
problem](#the-limitation-you-should-know-about).

## What the save asks, and why the two never disagree

The domain walk and the save-time pass have different subjects, on purpose.

| | The domain walk | The save |
|---|---|---|
| Asked of | an aggregate you are holding | a `DbContext` |
| Reaches | every child in the graph | every tracked entity this save adds or modifies |
| Finds them | through the generated `foreach` over `_lines` | through the change tracker |
| Queries the database | never, and it cannot lazy load | never, and it reads no navigation |
| Called by | your handler, when it wants to know | `InvariantInterceptor`, before every `SaveChanges` |
| Calls | `EnsureInvariants` / `GetInvariantViolations` | `EnsureOwnInvariants` / `GetOwnInvariantViolations` |

All four members live on `IHasInvariants`, so the persistence layer can ask any entity without knowing
its id type.

The domain walk asks more objects. The save asks the ones it is about to write, because a save deals
in partial graphs: a root loaded without its collection, a child the unit of work never saw, an
aggregate assembled from two queries. A generated `foreach` over `_lines` on a graph like that could
trigger a lazy load, could issue a query and could fail, which is exactly why finding who to ask lives
in the persistence layer rather than in the entity.

Which is also why "the collection might not be loaded" does not get to shape this API. It is a fact
about persistence, it is answered in persistence, and the aggregate a handler is working on is in
memory and whole, because that is the only state an aggregate is ever in while a handler works on it.

The consequence is the one worth having: **the domain answer is the stricter of the two**. Everything
the save would ask is a subset of what the walk already asked, so an aggregate that answers clean is
never contradicted at the commit. The reverse does not hold, and should not: the walk reports a broken
line that the save would pass over because nothing about it changed. A rule broken by a row nobody
touched is still a broken rule when you ask the domain, and is still not this save's problem.

## When to ask about one object only

`EnsureOwnInvariants()` and `GetOwnInvariantViolations()` answer for one object and say nothing about
what it holds. There is one reason to want them, and it is narrow: **you are walking the graph
yourself** and asking every object you find. Ask the walking pair from inside such a walk and every
child is reported twice, once by itself and once by its root. The interceptor is that caller, which is
why it asks the self-only pair, and a hand-written traversal of your own is the same situation.

Anything holding an aggregate and wanting to know whether it is consistent wants
`GetInvariantViolations()`. If you are reaching for the self-only pair to avoid a collection you are
not sure is loaded, you are in the persistence case, and
[`InvariantInterceptor`](#by-hand) already handles it from the change tracker.

There is one more place the pair matters: an entity you write by hand rather than generate. State its
rules in `EnsureOwnInvariants` / `GetOwnInvariantViolations`, because `Entity<TId>`'s walking pair
delegates to the self-only one. Override only `GetInvariantViolations` and the object still answers
for itself, but the save, which asks the self-only member, hears nothing at all.

## What this costs

An entity that states nothing and holds no children pays nothing at run time. The compiler removes an
unimplemented partial method and every call to it, no array of rules is emitted when there are none,
no walk is emitted when there is nothing to walk, and the generated `EnsureInvariants` is an empty
method body the interceptor calls for nothing.

An entity that does state rules pays for one static array, built once per entity type and never
again, and one virtual call per rule per check. The list of violations is allocated on the first
failure and never on the consistent path, which is the path that runs for every changed entity on
every save. There is no reflection, no attribute scan, no expression tree and no LINQ anywhere in the
generated code.

The walk pays for a `foreach` over each child collection and one call per child, and a child whose
answer is empty adds nothing to the list, because there is no list yet. A consistent aggregate of a
root and a hundred lines therefore allocates nothing at all, which is what lets the domain question be
asked on a hot path without anybody having to think about it. The walk is emitted only over
collections whose element type is an `[Entity]` or `[AggregateRoot]`, so a collection of value objects,
identifiers or strings costs nothing.

The interceptor walks the change tracker to decide who to ask, which is the same walk
`AggregateVersionInterceptor` already does for versioning, and it reads no navigation properties at
all.

## See also

- [Entities and aggregates](entities-and-aggregates.md) for what belongs inside the boundary.
- [Value objects](value-objects.md) for validation of a single value, which is the other half.
- [Entity Framework](entity-framework.md) for the interceptors and what each one guarantees.
- [Testing aggregates](testing.md#testing-invariants) for asserting on invariants in a unit test.
- [Diagnostics](diagnostics.md#ddd00024) for DDD00024 to DDD00027, the four ways a rule can be
  written so that it never runs.
