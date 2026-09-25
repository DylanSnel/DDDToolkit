# Value objects

An email address passed around as a `string` can be any string. Every method that receives one has to
decide whether to trust it, a customer's email and a supplier's name can be swapped without the
compiler noticing, and the rule for what makes an email address valid ends up copied wherever
somebody remembered it.

A value object gives the value a type of its own. It has no identity: two instances with the same
contents are the same thing, the way two ten-euro notes are the same amount. It carries its own rules,
so they live in one place, and a method can say in its signature that it wants a valid one.

The toolkit has two kinds: one for a single wrapped value, such as an email address, and one for a
cluster of properties, such as an address or an amount of money. This page starts with both, then
adds validation, the always-valid twin and changing a value, and ends with storage and the other
integrations.

## A single value

```csharp
[SingleValueObject<string>]
public partial record EmailAddress
{
    public static EmailAddress Create(string value) => new(value);
}
```

The record has to be `partial`, so the generator can add to it. It derives the record from
`SingleValueObject<string>`, which supplies the `Value` property, and writes the equality over it, the
constructors and `ToValid()`:

```csharp title="EmailAddress.g.cs, shortened"
partial record EmailAddress : SingleValueObject<string>, IValidatable<ValidEmailAddress>
{
    public virtual bool Equals(EmailAddress? other)
    {
        // ... null, type and reference checks
        return EqualityComparer<string>.Default.Equals(Value, other.Value);
    }

    public override int GetHashCode() => Value is null ? 0 : EqualityComparer<string>.Default.GetHashCode(Value);

    protected EmailAddress(string value) : base(value)
    {
    }

    [JsonConstructor]
    protected EmailAddress()
    {
    }

    public ValidEmailAddress ToValid() => new(this);
}
```

`ToValid()` and `ValidEmailAddress` come up under [validation](#the-always-valid-twin). Using it:

```csharp
var email = EmailAddress.Create("dylan@example.com");

email.Value;                                              // "dylan@example.com"
email == EmailAddress.Create("dylan@example.com");        // true: same contents, same value
```

Constructors are `protected`, so expose a factory like `Create` above. That keeps one obvious entry
point and leaves room to validate.

## Several properties

```csharp
[ValueObject]
public partial record PersonName
{
    public PersonName(string firstName, string lastName)
        => (FirstName, LastName) = (firstName, lastName);

    public string FirstName { get; protected init; }

    public string LastName { get; protected init; }
}
```

Equality is generated from the properties, in declaration order: two names are equal when the first
and the last name match.

```csharp title="PersonName.g.cs, shortened"
partial record PersonName : ValueObject, IValidatable<ValidPersonName>
{
    [Internal]
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FirstName;
        yield return LastName;
    }

    public virtual bool Equals(PersonName? other)
    {
        // ... null and type checks
        return Enumerable.SequenceEqual(GetEqualityComponents(), other.GetEqualityComponents());
    }

    // GetHashCode over the same components, a constructor for JSON, ToValid() and With(...)
}
```

Add a property and it is in `GetEqualityComponents()` at the next build. There is no list of members to
keep in step by hand, which is the part of a hand-written value object that goes wrong.

Properties you declare yourself are `{ get; protected init; }`. Anything else reports
[DDD00010](diagnostics.md#ddd00010) or [DDD00011](diagnostics.md#ddd00011), and a code fix turns it
into `protected init`. The short reason is that a value is set when it is made and never changed
afterwards; the long one is in [Why `with` is closed to callers](#why-with-is-closed-to-callers).

A value object with little behaviour can be a positional record instead, and the generator takes care
of the properties:

```csharp
[ValueObject]
public partial record Money(decimal Amount, string Currency);
```

### Leaving a property out of equality

Not every property is part of what the value *is*. Mark the ones that are not with `[DontCompare]`:

```csharp
[ValueObject]
public partial record PersonName
{
    public PersonName(string firstName, string lastName)
        => (FirstName, LastName) = (firstName, lastName);

    public string FirstName { get; protected init; }

    [DontCompare]
    public string? MiddleNames { get; protected init; }

    public string LastName { get; protected init; }

    [DontCompare]
    public string FullName => string.Join(" ", FirstName, MiddleNames, LastName).Trim();
}
```

Now two names are equal when the first and last name match, regardless of middle names. The generated
`GetEqualityComponents()` is the same as above: the marked properties are simply not in it.
`[DontCompare]` on a computed property such as `FullName` is good practice even though it derives
from compared properties: it documents the intent and keeps the equality components minimal. On a
positional record, write it as `[property: DontCompare]` on the parameter.

## Validation

Override `Validate()` and the result is cached in `IsValid`:

```csharp
[SingleValueObject<string>]
public partial record EmailAddress
{
    public static EmailAddress Create(string value) => new(value);

    protected override bool Validate() => Value.Contains('@');
}
```

```csharp
var email = EmailAddress.Create("nope");
email.IsValid;        // false
email.IsValidated;    // true, validation has run
email.EnsureValidated();   // throws InvalidValueObjectException
```

Creating an invalid value is allowed. A value object that came from a form or a request is often
invalid, and the place that made it is rarely the place that should decide what to do about that. What
the toolkit guarantees is that you can always ask, and that the [always-valid twin](#the-always-valid-twin)
below can only hold a value that passed.

`IsValid`, `IsValidated` and `Validate` are marked `[Internal]`, so they stay out of your database
tables, your JSON and your GraphQL schema.

A `bool` says no without saying why. There is a second overload for that, and it is the one to reach
for when a caller has to be told what went wrong:

```csharp
[SingleValueObject<string>]
public partial record EmailAddress
{
    public static EmailAddress Create(string value) => new(value);

    protected override void Validate(ValidationErrorBuilder errors)
    {
        if (!Value.Contains('@'))
        {
            errors.Add("An email address needs an @.", nameof(Value), "NoAtSign", Value);
        }
    }
}
```

A run that adds nothing is a pass. Both overloads run, and both have to be happy: the value is valid
when `Validate()` returned `true` and no failure was added. Override whichever suits the rule; most
types override one. With `DDDToolkit.FluentValidation` referenced, both are generated for you and you
write rules instead; see [With FluentValidation](#with-fluentvalidation).

## The always-valid twin

Every value object record gets a generated twin named `Valid<Name>`:

```csharp
EmailAddress candidate = EmailAddress.Create(userInput);
ValidEmailAddress confirmed = candidate.ToValid();   // throws if invalid
```

The point is to make validity visible in signatures. A method taking `ValidEmailAddress` cannot
receive an unvalidated one, so the check happens once at the boundary instead of defensively
everywhere:

```csharp
public Task SendWelcome(ValidEmailAddress address)   // no re-validation needed
```

The generator writes the twin next to the value object. Every way into it validates first, so there
is no way to hold a `ValidEmailAddress` that was not checked:

```csharp title="EmailAddress.g.cs, shortened"
public partial record ValidEmailAddress : EmailAddress, IAlwaysValid
{
    public ValidEmailAddress(EmailAddress value) : base(value)
    {
        value.EnsureValidated();
        _isValid = true;
    }

    public ValidEmailAddress(string value) : base(value)
    {
        EnsureValidated();
    }

    // equality, the same as on EmailAddress
}
```

The twin is made with the record's copy constructor, so it holds everything the original held:
get-only properties, `protected` ones, private fields and `[Internal]` state alike. The twin derives
from the original, so it is accepted anywhere the original is expected. It
implements `IAlwaysValid`, and both JSON integrations refuse to deserialize it directly: deserializing
straight into a twin would let invalid data enter through a type that promises the opposite.
Deserialize the base type and call `ToValid()`.

For the same reason, [`With`](#changing-a-value-with) on a twin validates the copy and throws when
it is invalid, rather than handing back a twin that does not keep its promise.

A twin is never equal to a plain value, even one with the same components, and that holds from
either side:

```csharp
Money plain = new(8m, "EUR");
ValidMoney twin = plain.ToValid();

plain == twin                  // false
twin == plain                  // false
twin == new Money(8m, "EUR").ToValid()   // true: twin to twin compares the components
```

Equality between a twin and a plain value could not be `true` in both directions. The twin is a derived
record, and the compiler writes the `Equals(Money?)` that answers for it, which only accepts a
`ValidMoney`. The compiler does not allow that member to be replaced. So the generated equality also
compares the runtime type, the way the compiler's own record equality does, and the answer is the same
whichever side is asked. To compare a twin with a plain value, compare twin to twin (call `ToValid()`
on the other side) or compare the components. Hash codes do not include the type, so a twin and a plain
value with the same components hash alike; that is allowed, and harmless.

> [!NOTE]
> **Why the twin rules out `sealed` and structs.** `ValidEmailAddress` derives from `EmailAddress`,
> and three things rest on that. A twin is accepted anywhere the original is, so a signature can ask
> for `ValidEmailAddress` while the rest of the code carries on with `EmailAddress`. The twin is built
> by the record's copy constructor, which the compiler makes `protected` on a record that is not
> sealed and `private` on one that is. And `With` is virtual, so the twin's override validates a copy
> even when the twin is held as its base type.
>
> A sealed record cannot be derived from, which is [DDD00013](diagnostics.md#ddd00013), and neither
> can a struct, which is why a value object has to be a reference record
> ([DDD00001](diagnostics.md#ddd00001)). A twin that wrapped the value instead of deriving from it
> would lose all three, and around a struct it could not even keep its promise: `default` and every
> element of a new array skip the constructor, and the check with it. The same goes for
> [struct identifiers](identifiers.md#struct-or-record), which have no twin.

## Failure handling

`ToValid()` throws. That is right when an invalid value is a bug, and wrong when it is an ordinary
answer. An endpoint validating a request body should reply with a refusal, not a 500, and a validation
pass over a whole request wants every failure at once rather than the first one to throw.

So there is a second way in, and it never throws.

### TryToValid

```csharp
var candidate = EmailAddress.Create(userInput);

if (candidate.TryToValid(out ValidEmailAddress? email, out var errors))
{
    // email is not null here
}
else
{
    // errors says why, in full
}
```

There is a shorter overload, `TryToValid(out var email)`, for a caller that only needs to know whether
it worked. And there is `TryValidate(out var errors)` for a value object you do not intend to convert,
such as one nested inside another.

Both work the same for `[ValueObject]`, `[SingleValueObject<T>]` and `[EntityId<T>] partial record`.
Struct identifiers have neither, and neither do they need it: they are well formed by construction and
have no twin to convert to.

They are extension methods, not generated members: `TryToValid` on the generated
`IValidatable<TValid>` interface, so the twin type is inferred at the call site, and `TryValidate` on
`ValueObject`, since it converts nothing and needs no twin. Nothing new lands on your types, so
nothing new turns up in an Entity Framework model or a GraphQL schema.

### What a failure looks like

Failures are `ValidationError`, which is the toolkit's own shape and needs no FluentValidation:

| Member | Meaning |
| --- | --- |
| `Message` | What is wrong, in words you could show a user. |
| `PropertyName` | The property it belongs to, or `null` for the whole value. |
| `Code` | A stable code to branch on, so you never match on text. |
| `AttemptedValue` | The value that was rejected, when the rule reported one. |
| `Arguments` | The values the message was built from, by name, such as `MaxLength`. Never `null`. |

The failures are the ones your `Validate(ValidationErrorBuilder)` added. A `Validate()` that returns
`false` without describing itself produces one failure carrying `ValidationError.UnspecifiedCode`, so
a caller is never handed an empty list that reads like a pass. With FluentValidation referenced you
get one of these per `ValidationFailure` instead; see [With FluentValidation](#with-fluentvalidation).

`ValidationErrors` on the value object itself holds the same list. Reading it runs the rules if
nothing has run them yet, exactly as reading `IsValid` does.

### Returning a refusal from an endpoint

```csharp
app.MapPost("/subscribers", (SubscribeRequest body) =>
{
    if (!EmailAddress.Create(body.Email).TryToValid(out var email, out var errors))
    {
        return Results.ValidationProblem(errors.ToErrorDictionary());
    }

    return Results.Ok(subscribers.Add(email));
});
```

`ToErrorDictionary()` groups the failures by property name and keeps the messages, which is the shape
`Results.ValidationProblem` and `ModelStateDictionary` expect. The client gets a 400 it can read field
by field, and nothing was thrown.

To answer with every failure in the request rather than the first, collect them. `Prefixed()` puts a
value object's failures back under the field they came from, because `Value` and `Street` say nothing
about where they sat in the body:

```csharp
var errors = new List<ValidationError>();

if (!EmailAddress.Create(body.Email).TryToValid(out var email, out var emailErrors))
{
    errors.AddRange(emailErrors.Prefixed("email"));     // "Value" becomes "email.Value"
}

if (!new Address(body.Street, body.City).TryToValid(out var shipTo, out var addressErrors))
{
    errors.AddRange(addressErrors.Prefixed("shipTo"));  // "Street" becomes "shipTo.Street"
}

if (errors.Count > 0)
{
    return Results.ValidationProblem(errors.ToErrorDictionary());
}

return Results.Ok(subscribers.Add(email!, shipTo!));
```

The two `!` are the one wart. The compiler knows `email` is not null inside the `if`, but not that it
is set by the time you reach the last line.

### Which one to reach for

| Situation | Use |
| --- | --- |
| The value came from outside and may be junk | `TryToValid` |
| You are collecting failures across a request | `TryToValid` plus `Prefixed` |
| The value was already validated at the boundary | take `ValidEmailAddress` in the signature |
| An invalid value here would be a bug | `ToValid()` and let it throw |

`InvalidValueObjectException` now carries `Errors` and `ObjectType`, so the throwing path says why as
well. That is for the log line, not for control flow.

### Why not a Result type

The obvious move would be to ship a `Result<T>`. The toolkit deliberately does not. Teams already use
FluentResults, ErrorOr, OneOf or something of their own, and a result type is infectious: once the
toolkit returns one, it turns up in every signature that touches a value object, and a codebase with
one result type ends up with two.

What the toolkit ships instead is the Try pattern. It hands you the value and the failures, and you
put them in whatever you already use. If that is a `Result<T>`, wrapping the call takes one line.

```csharp
public static Result<ValidEmailAddress> ToResult(this EmailAddress email)
    => email.TryToValid(out var valid, out var errors)
        ? Result.Ok(valid)
        : Result.Fail(errors.Select(e => e.Message));
```

### What this does not do

- There is no `Result<T>`, on purpose. See above.
- There is no async validation. `Validate` is synchronous, so a rule cannot call a database.
- There are no severities. A failure is a failure; there are no warnings.
- `Message` is whatever your rule wrote. To show it in the reader's language, branch on `Code`, or
  let [`DDDToolkit.Localization`](localization.md) phrase it from `Code` and `Arguments`.
- Nothing validates across value objects. A rule sees one value object, never the request around it.
  That is what a containing validator is for, and
  [`MustBeValid()`](#with-fluentvalidation) folds a value object into one.

## Changing a value: `With`

A value object is never changed in place. To get a different value, you make a copy with some
properties replaced. Every `[ValueObject]` has a generated `With(...)` for that: name the properties
to replace and leave the rest out. For a `Money` that carries an optional note:

```csharp
[ValueObject]
public partial record Money(decimal Amount, string Currency, [property: DontCompare] string? Note = null);
```

```csharp
var converted = money.With(amount: 12.50m, currency: "USD");
var cleared = money.With(note: null);   // null is a value too; leaving note out keeps it
```

On `Money`, `With` hands back a copy that is judged afresh, like any new value; ask `IsValid` or call
`ToValid()`. On `ValidMoney`, the copy is validated before anyone can hold on to it, and an invalid
copy throws `InvalidValueObjectException` on the line that asked for it:

```csharp
ValidMoney valid = money.ToValid();
valid.With(amount: 5);    // a ValidMoney
valid.With(amount: -1);   // throws here
```

What the generator writes for the two-property `Money(decimal Amount, string Currency)` and its twin:

```csharp title="Money.g.cs, shortened"
partial record Money
{
    [Internal]
    [HotChocolate.GraphQLIgnoreAttribute]
    public virtual Money With(Optional<decimal> amount = default, Optional<string> currency = default)
        => this with { Amount = amount.Or(Amount), Currency = currency.Or(Currency) };
}

public partial record ValidMoney : Money, IAlwaysValid
{
    [Internal]
    [HotChocolate.GraphQLIgnoreAttribute]
    public override ValidMoney With(Optional<decimal> amount = default, Optional<string> currency = default)
        => new(base.With(amount, currency));
}
```

A method runs after all the new values are in place, which is the one thing C#'s own `with` expression
cannot offer; [below](#why-with-is-closed-to-callers) is why that matters. It is also what makes value
objects easy to test, where copying a valid value and changing one property is the natural way to
build a case.

The base method makes the copy and the twin's constructor validates it. That is the same constructor
`ToValid()` uses. `With` is virtual, so code that only knows about `Money` still ends up in the twin's
override when it is handed a twin:

```csharp
static Money Discount(Money money) => money.With(amount: money.Amount - 100);

Discount(valid);   // throws: the discounted twin would not be valid
```

A few details:

- The parameters are `Optional<T>`, a small struct in `DDDToolkit.BaseTypes` that tells "not given"
  apart from "given as `null`". An ordinary optional parameter cannot, and a nullable property needs
  both. A value converts to it implicitly, so you never write the type.
- `With` covers the properties that have a setter and are not `[Internal]`. Computed properties are
  not parameters.
- `With` is marked `[Internal]`, and `[GraphQLIgnore]` when HotChocolate is referenced. HotChocolate
  otherwise publishes public methods as fields, and it cannot turn `Optional<T>` into an input type,
  so the whole schema would fail to build.
- If the value object declares a member named `With` itself, nothing is generated, so an existing
  method keeps working.

### Why `with` is closed to callers

A record comes with `with`, which copies a value and changes some of its properties:

```csharp
var moved = address with { City = "Utrecht" };
```

For a value object that is exactly the operation you want. It is also a way to create a value that
never went past anything that checks it. That is why properties are `protected init`: `with` only
compiles inside the value object and its twin, and everybody else uses `With(...)`. This section
explains why neither leaving `with` open nor adding a check to it works. You do not need it to use
the toolkit; it is here for the reader who wonders why the obvious design was not chosen.

#### What `with` does, step by step

`money with { Amount = -1 }` compiles to three steps:

1. **Clone.** The record's hidden, compiler-generated clone method is called. It is virtual, so the
   copy has the *runtime* type of `money`, whatever the type of the variable.
2. **Copy constructor.** The clone method runs the copy constructor, which copies every field from
   the original.
3. **Init accessors.** Only now are the new values assigned, one property at a time, in the order
   they are written between the braces.

Nothing of yours runs after step 3. C# has no hook for "the `with` expression is done", so no code
can look at the finished copy before the caller gets it.

#### On a plain value object this is harmless

The copy constructor of `ValueObject` clears the cached verdict. A copy made by `with` has never been
judged, and the first time something reads `IsValid` or `ValidationErrors`, or calls `ToValid()`,
the rules run on its new contents:

```csharp
var copy = money with { Amount = -1 };   // inside the type, where it is allowed
copy.IsValid;                            // false: judged afresh
```

A `Money` is allowed to be invalid; that is what `IsValid` is for. So `with` on the plain type does
not lie to anyone.

#### On the always-valid twin it breaks the one promise the twin makes

A `ValidMoney` exists to say "this was checked" in a signature, so the code that receives it does not
check again. `with` on a twin produces another twin, holding whatever values were put in:

```csharp
ValidMoney valid = money.ToValid();
var copy = valid with { Amount = -1 };   // a ValidMoney and IAlwaysValid, with Amount -1
```

This does not only happen when someone writes `with` on a `ValidMoney` on purpose. Step 1 above
copies the runtime type, so ordinary code that knows nothing about twins does it too:

```csharp
static Money Discount(Money money) => money with { Amount = money.Amount - 100 };

var result = Discount(valid);   // a ValidMoney, holding -90
```

Passing a twin where a `Money` is expected is normal, because it *is* a `Money`. The code in
`Discount` is reasonable, and yet `result is ValidMoney` is true for a value that is not valid.
`result.IsValid` does say `false`, since the verdict was cleared, but code that trusts the type never
asks.

#### Why the copy cannot be checked at the `with`

- **In the copy constructor:** too early. It runs in step 2, before the new values arrive, so it sees
  the old, valid values.
- **In each init accessor:** these see half-changed objects. `with { Amount = 5, Currency = "USD" }`
  sets `Amount` while `Currency` still holds the old value, and a rule about the pair, such as "this
  amount is allowed in this currency", would fail on a combination nobody asked for.
- **Throwing from the twin's copy constructor, always:** that makes every `with` on a twin fail,
  including `Discount` above when its result is perfectly valid.
- **Checking later, when a property of the copy is first read:** possible, but the exception is then
  thrown wherever the copy happens to be used, which can be far from the line that made it. The
  stack trace points at the reader, not at the cause.
- **Protecting only the twin**, by declaring its properties again as `protected init`: that stops
  `valid with { ... }`, but not `Discount`, which goes through `Money`'s own properties.

None of these is acceptable for a type whose job is to be trustworthy, so the toolkit makes the
situation impossible instead. With `protected init`, `with` only compiles inside the value object and
its twin:

```csharp
money with { Amount = -1 };   // outside the type: CS0272, the init accessor is inaccessible
```

The generated code of the value object and its twin still uses `with`. That code is written to
check what it produces, and `With(...)` is how everybody else gets the same operation with the check
included.

## Positional records

A positional record turns each parameter into a property, and that property is always `public init`.
C# has no syntax to ask for anything else. Left as it is, it would open `with` to every caller again.

C# has one way around it. When a property with a parameter's name is declared anywhere in the
record, in any of its partial parts, the compiler does not synthesize one for that parameter. The
generator uses that: it declares each positional property itself, as `protected init`, with an
initializer that reads the parameter. For `record Money(decimal Amount, string Currency)`:

```csharp title="Money.g.cs, shortened"
partial record Money : ValueObject, IValidatable<ValidMoney>
{
    [JsonInclude]
    public decimal Amount { get; protected init; } = Amount;

    [JsonInclude]
    public string Currency { get; protected init; } = Currency;

    // equality over Amount and Currency

    [JsonConstructor]
    protected Money() : this(default(decimal)!, default(string)!)
    {
    }

    // ToValid() and With(...)
}
```

`= Amount` reads the constructor parameter, not the property, so the primary constructor still fills
the properties. The parameterless constructor chains to the primary one, because in a positional
record every other constructor has to. `[JsonInclude]` is there because System.Text.Json cannot reach a
`protected init` on its own: without it the value would deserialize as its defaults, and a value
object in a domain event would come out of the outbox empty. It is left out when the project does
not reference System.Text.Json, and not repeated when the parameter already carries it.

| What the positional record gives you | After generation |
|---|---|
| The constructor, `new Money(10m, "EUR")` | unchanged |
| Deconstruction, `var (amount, currency) = money;` | unchanged |
| Value equality | the toolkit's, over the properties not marked `[DontCompare]` |
| `money with { Amount = 1 }` outside the type | does not compile (CS0272) |
| `money.With(amount: 1)` | generated, see [`With`](#changing-a-value-with) |

Attributes aimed at the property, such as `[property: DontCompare]`, only work while the compiler
synthesizes the property. The generator copies them onto the property it declares, so they still
apply. The compiler does not know that and warns that the attribute is ignored (CS0657). A
suppressor in the toolkit removes that warning, but only when the attribute really did arrive on the
property.

A property you declare yourself takes the place of the synthesized one, as it always does in a
record, and the generator leaves it alone. Use that when a parameter needs something the generated
declaration does not do:

```csharp
[ValueObject]
public partial record Money(decimal Amount, string Currency)
{
    public string Currency { get; protected init; } = Currency.ToUpperInvariant();
}
```

Such a property is checked like any other declared one, so it needs `protected init` too, and a
`[property: ...]` attribute on its parameter is lost; the CS0657 warning then stays to tell you.

## Hiding members

`[Internal]` marks a member as infrastructure. It is excluded from equality, from Entity Framework
mapping, from the Newtonsoft contract resolver and from the GraphQL schema.

```csharp
[Internal]
public string CacheKey => $"{FirstName}:{LastName}";
```

Use it for anything an outside observer should not see. `[DontCompare]` is narrower: the member stays
visible and persisted, it simply does not take part in equality.

## Entity Framework

Everything above works without a database. Once you store value objects, reference
`DDDToolkit.EntityFramework` and its generator adds the mapping:

- `[SingleValueObject<T>]` and identifiers get a generated `ValueConverter`, so an `EmailAddress` is
  stored as a plain `string` column. `Add<Module>Converters` registers them all; see
  [Entity Framework](entity-framework.md).
- `[ValueObject]` records are annotated `[ComplexType]`, so their properties are stored inline in the
  owning table rather than in a table of their own.

A single value object can say how wide its column is. `ColumnLength` becomes `HaveMaxLength` in the
generated configuration:

```csharp
[SingleValueObject<string>(ColumnLength: MaxLength)]
public partial record EmailAddress
{
    public const int MaxLength = 255;

    public static EmailAddress Create(string value) => new(value);
}
```

The generator writes a converter into the record, for it and for its twin, and one registration per
type in the project's `Add<Module>Converters`, where the length ends up:

```csharp title="EmailAddress.Converter.g.cs, shortened"
partial record EmailAddress
{
    public sealed class EmailAddressConverter : ValueConverter<EmailAddress, string>
    {
        public EmailAddressConverter() : base(static v => v.Value, static v => new EmailAddress(v))
        {
        }
    }
}
```

```csharp title="ConverterExtensions.g.cs, shortened"
modelConfigurationBuilder.Properties<EmailAddress>().HaveConversion<EmailAddress.EmailAddressConverter>().HaveMaxLength(255);
modelConfigurationBuilder.DefaultTypeMapping<EmailAddress>().HasConversion<EmailAddress.EmailAddressConverter>().HasMaxLength(255);
```

`ColumnLength` has no effect on validation and none at all without the Entity Framework package. A
constant such as `MaxLength` lets a validation rule use the same number.

A `[ValueObject]` gets no converter. Its generated part only carries the attribute that tells Entity
Framework to store its properties as columns of the owner:

```csharp title="PersonName.EntityFramework.g.cs"
[ComplexType]
partial record PersonName
{
}

[ComplexType]
partial record ValidPersonName
{
}
```

## With FluentValidation

Reference `DDDToolkit.FluentValidation` and the generator writes the `Validate()` override for you,
along with an `Errors` collection and a nested `Validator` class. You supply only the rules:

```csharp
[SingleValueObject<string>]
public partial record EmailAddress
{
    public static EmailAddress Create(string value) => new(value);

    partial class Validator
    {
        public Validator() => RuleFor(x => x.Value).EmailAddress();
    }
}
```

```csharp title="EmailAddress.FluentValidation.g.cs, shortened"
partial record EmailAddress
{
    [Internal]
    [NotMapped]
    public ReadOnlyCollection<FluentValidation.Results.ValidationFailure> Errors => _errors.AsReadOnly();

    private List<FluentValidation.Results.ValidationFailure> _errors = new();

    protected override bool Validate()
    {
        var validator = new Validator();
        var result = validator.Validate(this);
        _errors = result.Errors;
        return result.IsValid;
    }

    protected override void Validate(ValidationErrorBuilder errors)
    {
        foreach (var failure in _errors)
        {
            // ... copies each failure, with its placeholder values as arguments, into a ValidationError
        }
    }

    partial class Validator : FluentValidation.AbstractValidator<EmailAddress>
    {
    }
}
```

The two halves of `Validator` are the point: the generator states the base class, you state the rules.
Note which failure shape is which. `Errors` is FluentValidation's own `ValidationFailure`, handy when
you already work in that library; the second `Validate` overload copies the same failures into the
toolkit's `ValidationError`, which is what `ValidationErrors` and `TryToValid()` hand back and what
lets a caller read failures without referencing FluentValidation at all. Each carries the same
property, code and attempted value, and its placeholder values as `Arguments`.

```csharp
var email = EmailAddress.Create("nope");
email.IsValid;                     // false
email.Errors[0].PropertyName;      // "Value"
```

Struct identifiers get no validator, since they are well-formed by construction.

A value object validates itself, which is not the same as taking part in the validator you write for a
command or a request DTO. `MustBeValid()` folds it into one:

```csharp
public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.Email).NotNull().MustBeValid();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}
```

The failure is reported against the containing property, so the caller gets one flat result. A `null`
property passes, exactly as with FluentValidation's own rules, so chain `NotNull()` when the value is
required.

If the containing validator is a FluentValidation one, `result.ToValidationErrors()` from
`DDDToolkit.FluentValidation` converts its failures too, so everything ends up in one list with the
ones from `TryToValid`.

## Requirements

The declaration must be a `partial record` that is not sealed. A class or struct carrying
`[ValueObject]` or `[SingleValueObject<T>]` reports [DDD00001](diagnostics.md#ddd00001); a
non-partial declaration reports [DDD00005](diagnostics.md#ddd00005).

The properties of a `[ValueObject]` record you declare yourself must be `{ get; protected init; }`;
the ones a [positional record](#positional-records) declares through its parameters are taken care
of by the generator.
