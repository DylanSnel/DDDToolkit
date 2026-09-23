# Localization

A failure the toolkit reports carries three things: a stable **code**, a **message** in the domain's
own words, and the **arguments** that message was built from. `DDDToolkit.Localization` uses the
first and the third to phrase the failure again in the reader's language, at the moment it becomes a
response.

This covers every failure the toolkit hands you: `ValidationError` from a value object or a
FluentValidation validator, and `InvariantViolation` from `GetInvariantViolations()` or from
`InvariantViolationException.InvariantViolations` on the throwing path.

## The rule: the domain stays language-free

The domain never looks at a culture. An invariant runs on a save in a background job as readily as in
a request, and there is no reader there to ask. So the domain reports *what* is wrong, and the edge
decides *how to say it*:

```text
domain                                   edge
──────                                   ────
Code       "Till.OverLimit"      ──►     looked up in your resx for the current UI culture
Message    "A till holds at most 100…"   kept as the fallback when nobody translated the code
Arguments  { Limit: 100, Cash: 150 }     fill the translated template
```

A failure whose code nobody translated keeps the domain's own message, so adding localization never
makes a response worse than it was.

## Setting it up

```bash
dotnet add package DDDToolkit.Localization
```

`IFailureLocalizer` itself lives in the core `DDDToolkit` package, so an integration can ask for one
without depending on how translations are stored. `DDDToolkit.Localization` provides the implementation
that reads them through `IStringLocalizer`.

```csharp
builder.Services.AddLocalization();
builder.Services.AddDDDToolkitLocalization(options => options.AddResource<SharedFailures>());

var app = builder.Build();
app.UseRequestLocalization("en", "nl");
```

`SharedFailures` is an empty marker class with `SharedFailures.resx`, `SharedFailures.nl.resx` and so
on beside it, found exactly as `IStringLocalizer<SharedFailures>` finds them. Sources are asked in the
order they were added. `AddLocalizer(IStringLocalizer)` adds one you built yourself, such as one that
reads translations from a database.

The language is `CultureInfo.CurrentUICulture`, which the request localization middleware sets per
request. Numbers and dates inside a message are formatted with `CultureInfo.CurrentCulture`.

## Writing translations

The key is the failure's code. The value is a template with named placeholders:

```xml
<data name="Till.OverLimit" xml:space="preserve">
  <value>In deze kassa mag hooguit {Limit:C} zitten, en er zit {Cash:C} in.</value>
</data>
<data name="MaximumLengthValidator" xml:space="preserve">
  <value>{PropertyName} is hooguit {MaxLength} tekens lang.</value>
</data>
```

- `{Name}` is replaced by the argument of that name, matched without regard to case.
- `{Name:format}` applies a .NET format string, so `{Limit:C}` is a currency in the current culture.
- `{{` and `}}` are literal braces.
- A placeholder with no value is left as written, so a typo shows on screen rather than as an exception
  on the failure path.

Besides its arguments, a template can name the failure's own members:

| Failure | Placeholders |
| --- | --- |
| `ValidationError` | `{PropertyName}`, `{AttemptedValue}`, `{Code}` |
| `InvariantViolation` | `{EntityType}`, `{EntityId}`, `{Code}` |

An argument of the same name wins.

An invariant violation is looked up as `{EntityType}.{Code}` first and then as the bare code. A rule
shared by several entities can then have one translation, and still get a different one where the
sentence has to differ: `Drawer.NotNegative` beats `NotNegative`.

## Giving a failure its arguments

### Value objects

A rule you write by hand adds its values next to its message:

```csharp
protected override void Validate(ValidationErrorBuilder errors)
{
    if (Value.Length > 40)
    {
        errors.Add("A street is at most 40 characters.", nameof(Value), "Street.TooLong", Value,
            new Dictionary<string, object?> { ["MaxLength"] = 40 });
    }
}
```

`new ValidationError(...).With("MaxLength", 40)` does the same for a single failure.

With FluentValidation you write nothing: every placeholder value FluentValidation knows (`MaxLength`,
`TotalLength`, `ComparisonValue`, `PropertyName`, and anything you add with
`context.MessageFormatter.AppendArgument`) arrives in `Arguments`, and the error code
(`MaximumLengthValidator`, `NotEmptyValidator`, ...) is the key. The same goes for
`result.ToValidationErrors()` on a validator you wrote yourself.

FluentValidation also has its own translations, and they are used for `Message`. But a value object
validates once and caches the verdict, so that message is in whichever language was current *the first
time* anything asked. The localizer phrases the failure when it is read, which is the moment that
counts.

### Invariants

`IInvariant<T>.Check` returns an `InvariantFailure`. A string converts to one, so a rule with nothing
to add returns its message as before. A rule whose message names values adds them:

```csharp
public sealed class MustStayWithinTheCreditLimit : IInvariant<Order>
{
    public const string ViolationCode = "Order.OverCreditLimit";

    public string Code => ViolationCode;

    public InvariantFailure? Check(Order order)
        => order.Total <= order.CreditLimit
            ? null
            : new InvariantFailure($"An order may total at most {order.CreditLimit}.")
                .With("CreditLimit", order.CreditLimit)
                .With("Total", order.Total);
}
```

The happy path still returns `null` and allocates nothing.

The `CheckInvariants()` seam reports strings, so its violations carry `InvariantViolation.SeamCode`
and have no code worth translating. A rule that needs translating needs a code, which is one more
reason to give it [a type of its own](invariants.md#a-named-invariant).

## Using it

`IFailureLocalizer` phrases one failure. The extension methods phrase a list and keep everything else,
so the result can still be branched on:

```csharp
app.MapPost("/subscribers", (SubscribeRequest body, IFailureLocalizer localizer) =>
{
    if (!EmailAddress.Create(body.Email).TryToValid(out var email, out var errors))
    {
        return Results.ValidationProblem(errors.Prefixed("email").ToErrorDictionary(localizer));
    }

    return Results.Ok(subscribers.Add(email));
});
```

```csharp
var violations = order.GetInvariantViolations();
if (violations.Count > 0)
{
    return Results.UnprocessableEntity(violations.Localized(localizer).Select(v => new { v.Code, v.Message }));
}
```

On the throwing path, `InvariantViolationException.InvariantViolations` holds the same violations,
codes and arguments included, one for one with the phrased `Violations`:

```csharp
catch (InvariantViolationException exception)
{
    return Results.UnprocessableEntity(exception.InvariantViolations.Localized(localizer)
        .Select(v => new { v.Code, v.Message }));
}
```

## GraphQL

`DDDToolkit.HotChocolate` has an error filter that does all of the above for you: every failure the
toolkit throws becomes a GraphQL error of its own, with its code in the extensions and its message in
the reader's language.

```csharp
services.AddGraphQLServer()
    .AddDDDToolkitTypes()
    .AddDDDToolkitErrors();
```

See [GraphQL](graphql.md#failures-as-graphql-errors).

## One module, one resx

Calls to `AddDDDToolkitLocalization()` add up, so each module registers its own translations from its
own composition method and every one of them is asked, in the order the calls were made:

```csharp
public static IServiceCollection AddOrdering(this IServiceCollection services)
    => services.AddDDDToolkitLocalization(o => o.AddResource<OrderingFailures>());

public static IServiceCollection AddShipping(this IServiceCollection services)
    => services.AddDDDToolkitLocalization(o => o.AddResource<ShippingFailures>());
```

## The toolkit's own messages

The toolkit writes two messages itself, and ships them in English and Dutch:

| Code | Where it comes from | Placeholders |
| --- | --- | --- |
| `Unspecified` | a `Validate()` that returned `false` without saying why | `{ValueObject}` |
| `ValueObjectValidator` | `MustBeValid()` in `DDDToolkit.FluentValidation` | `{PropertyName}`, `{ValueObject}` |

They are asked last, so a key of the same name in your own resx overrides them, and adding another
language is adding those two keys to it.

Override them in every language you support, not only in your neutral resx. `IStringLocalizer` falls
back from `SharedFailures.nl.resx` to `SharedFailures.resx` before the toolkit is asked at all, so an
override in the neutral file alone gives Dutch readers your English text instead of the toolkit's
Dutch. The check below reports exactly that.

## Checking that everything is translated

`FailureTranslations` checks that every failure you expect is translated in every language you support,
so a missing translation fails a test instead of reaching a user:

```csharp
[Fact]
public void Every_failure_is_translated_in_every_language()
    => FailureTranslations
        .Check(localizer, "en", "nl", "de")         // the first is the language of your neutral resx
        .Invariants(typeof(Order).Assembly)          // every IInvariant, found and asked for its code
        .Codes("Street.TooLong", "City.Required")    // codes your value objects report
        .ToolkitCodes()                              // Unspecified and ValueObjectValidator
        .Verify();
```

`localizer` is the one the application registers, taken from a service provider built the way the
application builds it. The check follows each key through each source the way the localizer does at
run time, one language level at a time (`nl-NL`, then `nl`, then the neutral resx), and `Verify()`
throws with a report:

```text
3 failure translations are missing or hidden:

  de     Order.OverCreditLimit            fallsback: readers get SharedFailures.resx's neutral (en) text.
  nl     Nobody.Knows                     missing: no source knows it, so readers get the domain's own message.
  nl     ValueObjectValidator             hidden: SharedFailures.resx has it only in its neutral (en) resx, which
                                          wins over the toolkit's nl text. Add it to the nl resx too.
```

| Problem | Meaning |
| --- | --- |
| `Missing` | No source knows the key, so readers get the domain's own message. |
| `FallsBack` | Nothing translates it into this language, so readers get your neutral text or the toolkit's English. |
| `Hidden` | The toolkit translates it, but your neutral resx wins first. |

Invariants are found for you: every `IInvariant` in the assemblies you name is created and asked for
its code, and either of its two keys counts. Codes your value objects report and FluentValidation's
codes are strings in your code, so name the ones you expect with `Codes(...)`.

`Findings()` returns the same report as a list, for a test that wants to assert something narrower,
and nothing here needs a test framework, so the same check works at startup in development:

```csharp
if (app.Environment.IsDevelopment())
{
    FailureTranslations.Check(app.Services.GetRequiredService<IFailureLocalizer>(), "en", "nl")
        .Invariants(typeof(Order).Assembly)
        .Verify();
}
```
