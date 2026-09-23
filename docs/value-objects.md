# Value objects

A value object has no identity: two instances with the same contents are the same thing. The toolkit
has two flavours, one for a single wrapped value and one for a cluster of properties.

## Single value objects

```csharp
[SingleValueObject<string>(ColumnLength: 255)]
public partial record EmailAddress
{
    public const int MaxLength = 255;

    public static EmailAddress Create(string value) => new(value);
}
```

The generator derives the record from `SingleValueObject<string>`, which supplies the `Value`
property and equality over it, and adds the constructors, equality members and `ToValid()`.

Constructors are `protected`, so expose a factory like `Create` above. That keeps one obvious entry
point and leaves room to validate.

## Multi-property value objects

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

Equality is generated from the properties, in declaration order, excluding those marked
`[DontCompare]` and `[Internal]`. Here two names are equal when the first and last name match,
regardless of middle names.

`[DontCompare]` on a computed property such as `FullName` is good practice even though it derives
from compared properties: it documents the intent and keeps the equality components minimal.

### Setters must be `protected init`

Records support `with`, which would otherwise let any caller clone an object into an invalid state:

```csharp
var invalid = name with { FirstName = "" };   // prevented by protected init
```

A public or non-init setter reports [DDD00010](diagnostics.md#ddd00010) or
[DDD00011](diagnostics.md#ddd00011). Inside the type and its always-valid twin, `with` still works.

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
write rules instead.

### With FluentValidation

Reference `DDDToolkit.FluentValidation` and the generator writes the `Validate()` override for you,
along with an `Errors` collection and a nested `Validator` class. You supply only the rules:

```csharp
[SingleValueObject<string>(ColumnLength: MaxLength)]
public partial record EmailAddress
{
    public const int MaxLength = 255;

    public static EmailAddress Create(string value) => new(value);

    partial class Validator
    {
        public Validator() => RuleFor(x => x.Value).EmailAddress();
    }

    // ---- generated ----------------------------------------------------------
    [NotMapped]
    public ReadOnlyCollection<ValidationFailure> Errors => _errors.AsReadOnly();

    protected override bool Validate()                                 // runs the Validator
    protected override void Validate(ValidationErrorBuilder errors)    // copies failures into ValidationError

    /// <summary>Add rules in a partial declaration of this class.</summary>
    partial class Validator : AbstractValidator<EmailAddress>;
    // -------------------------------------------------------------------------
}
```

The two halves of `Validator` are the point: the generator states the base class, you state the rules.
Note which failure shape is which. `Errors` is FluentValidation's own `ValidationFailure`, handy when
you already work in that library; the second `Validate` overload copies the same failures into the
toolkit's `ValidationError`, which is what `ValidationErrors` and `TryToValid()` hand back and what
lets a caller read failures without referencing FluentValidation at all.

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

The twin derives from the original, so it is accepted anywhere the original is expected. It
implements `IAlwaysValid`, and both JSON integrations refuse to deserialize it directly: deserializing
straight into a twin would let invalid data enter through a type that promises the opposite.
Deserialize the base type and call `ToValid()`.

This is why value objects cannot be `sealed` ([DDD00013](diagnostics.md#ddd00013)).

## Failure handling

`ToValid()` throws. That is right when an invalid value is a bug, and wrong when it is an ordinary
answer. An endpoint validating a request body should reply with a refusal, not a 500, and a validation
pass over a whole request wants every failure at once rather than the first one to throw.

So there is a second way in, and it never throws.

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

With FluentValidation referenced you get one of these per `ValidationFailure`, carrying the same
property, code and attempted value, and its placeholder values as `Arguments`. `Code` and `Arguments`
together are what lets [`DDDToolkit.Localization`](localization.md) phrase the failure in the reader's
language. `Errors` is still there and still holds FluentValidation's own
type, for code that wants it.

Without FluentValidation, the failures are the ones your `Validate(ValidationErrorBuilder)` added. A
`Validate()` that returns `false` without describing itself produces one failure carrying
`ValidationError.UnspecifiedCode`, so a caller is never handed an empty list that reads like a pass.

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

If the containing validator is a FluentValidation one, `result.ToValidationErrors()` from
`DDDToolkit.FluentValidation` converts its failures too, so everything ends up in one list.

### Which one to reach for

| Situation | Use |
| --- | --- |
| The value came from outside and may be junk | `TryToValid` |
| You are collecting failures across a request | `TryToValid` plus `Prefixed` |
| The value was already validated at the boundary | take `ValidEmailAddress` in the signature |
| An invalid value here would be a bug | `ToValid()` and let it throw |

`InvalidValueObjectException` now carries `Errors` and `ObjectType`, so the throwing path says why as
well. That is for the log line, not for control flow.

### What this does not do

- There is no `Result<T>`, on purpose. See above.
- There is no async validation. `Validate` is synchronous, so a rule cannot call a database.
- There are no severities. A failure is a failure; there are no warnings.
- There is no localization. `Message` is whatever your rule wrote. Branch on `Code` and translate in
  your own layer.
- Nothing validates across value objects. A rule sees one value object, never the request around it.
  That is what a containing validator is for, and
  [`MustBeValid()`](#with-fluentvalidation) folds a value object into one.

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

With `DDDToolkit.EntityFramework` referenced:

- `[SingleValueObject<T>]` and identifiers get a generated `ValueConverter` and are registered by
  `Add<Module>Converters`, honouring `ColumnLength` as `HaveMaxLength`.
- `[ValueObject]` records are annotated `[ComplexType]`, so their properties are stored inline in the
  owning table rather than in a table of their own.

## Requirements

The declaration must be a `partial record` that is not sealed. A class or struct carrying
`[ValueObject]` or `[SingleValueObject<T>]` reports [DDD00001](diagnostics.md#ddd00001); a
non-partial declaration reports [DDD00005](diagnostics.md#ddd00005).
