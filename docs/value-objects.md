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
}
```

```csharp
var email = EmailAddress.Create("nope");
email.IsValid;                     // false
email.Errors[0].PropertyName;      // "Value"
```

The generated `Validator` derives from `AbstractValidator<EmailAddress>`; your half only adds rules.
Struct identifiers get no validator, since they are well-formed by construction.

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
