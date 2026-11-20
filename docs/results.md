# Results

A handler can answer with a `Result<T>` instead of throwing. The result holds the value or an `Error`. The mediator,
the GraphQL schema and the logging read the error from the result, so a refusal is a normal answer and no exception.

```csharp
public sealed record ReserveStock(string Sku, int Quantity) : IResultCommand<Reservation>;

public sealed class ReserveStockHandler(IStockStore stock) : IResultCommandHandler<ReserveStock, Reservation>
{
    public async ValueTask<Result<Reservation>> Handle(ReserveStock command, CancellationToken cancellationToken)
    {
        if (await stock.FindAsync(command.Sku, cancellationToken) is not { } item)
        {
            return new NotFoundError("stock.unknown", $"There is no stock with SKU {command.Sku}.");
        }

        return item.Reserve(command.Quantity);   // a Reservation, converted to a success
    }
}
```

`Result`, `Result<T>` and the errors are in `DTS.DDD.Results`, in the package `DylansTechStack.DDD`.

## The types

- `Result<T>` is a struct with a value of `T` or an `Error`. A value and an error both convert to it, so a handler
  writes `return reservation;` or `return error;`.
- `Result` is the same without a value, for a command that answers nothing: `ICommand<Result>`.
- `default(Result<T>)` is a failure with the code `ddd.result-not-set`, so a result that was never set is never read
  as a success. `default(Result)` is a success.
- `Value` of a failure throws an `ErrorException` with the error. `Error` of a success throws an
  `InvalidOperationException`.

## Requests that answer a result

A request answers a result when its response type is `Result` or `Result<T>`. The short interfaces of
`DTS.DDD.Mediator` say the same with less text:

| Short | Long |
|---|---|
| `IResultCommand<T>` | `ICommand<Result<T>>` |
| `IResultCommand` | `ICommand<Result>` |
| `IResultQuery<T>` | `IQuery<Result<T>>` |
| `IResultCommandHandler<TCommand, T>` | `ICommandHandler<TCommand, Result<T>>` |
| `IResultCommandHandler<TCommand>` | `ICommandHandler<TCommand, Result>` |
| `IResultQueryHandler<TQuery, T>` | `IQueryHandler<TQuery, Result<T>>` |

Both spellings are the same type to the mediator and to the generated `Add{Module}Handlers()`. A request that answers
anything else keeps throwing, so a project chooses per request.

## The errors

An error has a `Code`, a `Message` and `Arguments`. The code is stable across releases and languages, and a client
branches on it. The arguments are the values the message was built from, by name. The localizer fills a template from
them, as it does for a refusal (see [Localization](localization.md)). The argument `Field` names the input the error is
about, and `Error.Field` reads it.

The toolkit has five error types. The type is the kind of the error:

| Type | Kind | HTTP | Example |
|---|---|---|---|
| `InvalidError` | `INVALID` | 400 | A value is missing or malformed. `Failures` holds one failure per input. |
| `NotPermittedError` | `NOT_PERMITTED` | 403 | The caller has no seat or no permission. |
| `NotFoundError` | `NOT_FOUND` | 404 | The SKU names no stock. |
| `ConflictError` | `CONFLICT` | 409 | The slug is taken. |
| `UnexpectedError` | `UNEXPECTED` | 500 | Another service or the database does not answer. |

An application derives its own error from one of the five and adds the data it carries:

```csharp
public sealed record RateLimitedError(DateTimeOffset NextAttemptAt)
    : ConflictError("api.rate-limited", "Too many requests.", [new("NextAttemptAt", NextAttemptAt)]);
```

The signature stays `Result<T>`. Code that wants the kind tests the type: `if (result.Error is NotFoundError)`.

`InvalidError.ForField(code, message, field)` makes an error about one input.

## Combining results

| Method | What it does |
|---|---|
| `Map(value => ...)` | A success with a new value. A failure passes on. |
| `Bind(value => result)` | The next result after a success. A failure passes on. |
| `Ensure(predicate, value => error)` | The error when the predicate is false for the value. |
| `Tap(value => ...)`, `TapError(error => ...)` | Runs an action and returns the same result. |
| `Match(value => ..., error => ...)` | One answer for either side. |
| `TryGetValue(out value)`, `GetValueOrDefault(fallback)` | Reads the value without throwing. |
| `ThrowIfFailure()` | Throws the error as an `ErrorException`, for a failure that is a bug. |

A `Result<T>` converts to a `Result` with the same success or failure.

## In the mediator

A request whose response is a `Result` or `Result<T>` passes the toolkit's mediator like any other. The mediator adds
two things for such a request:

- **A thrown refusal becomes a failed result.** An access check that refuses, a manager of a package that throws a
  `RefusalException`, or an invalid value object that throws an `InvalidValueObjectException` gives a failed result.
  The handler does not run after a refused check. A `RefusalException` keeps its code, message and arguments, and its
  kind decides the error type. An `InvalidValueObjectException` becomes an `InvalidError` with the code
  `ddd.value-invalid`, the name of the value object as the argument `ValueObject`, and its failures.
- **Every failure is logged.** The line names the request, the error type and the code:
  `ReserveStock gave NotFoundError stock.unknown`. It leaves out the message and the arguments, because they may hold
  personal data such as an address.

A request with another response throws as before. So a module can move one command at a time.

The log level depends on the error type. The defaults: `UnexpectedError` at Error, `NotPermittedError` at Warning,
every other error at Information. The application sets its own levels; the most specific type wins, and
`LogLevel.None` leaves a type out:

```csharp
services.AddDDDToolkitResultLogging(options => options
    .Level<NotFoundError>(LogLevel.Debug)
    .Level<RateLimitedError>(LogLevel.Warning));
```

## In GraphQL

With `AddDDDToolkitErrors()` a resolver may return a `Result<T>`, also in a `Task` or `ValueTask`. The field has the
type of `T`, and a field of a `Result` is a `Boolean` that is `true` after a success:

```csharp
[Mutation]
public static async Task<Result<Reservation>> ReserveStockAsync(string sku, int quantity, ISender sender, CancellationToken cancellationToken)
    => await sender.Send(new ReserveStock(sku, quantity), cancellationToken);
```

```graphql
type Mutation {
  reserveStock(sku: String!, quantity: Int!): Reservation!
}
```

A failure is a GraphQL error with the code, the kind, the field and the arguments in its extensions, as a refusal
is. An `InvalidError` with failures is one error per failure:

```json
{ "message": "There is no stock with SKU tea.", "extensions": { "code": "stock.unknown", "kind": "NOT_FOUND", "arguments": {} } }
```

## A dropped result

A call that returns a result as a statement of its own loses the failure. [DDD00111](diagnostics.md#ddd00111)
reports it. Read the result, return it, call `ThrowIfFailure()`, or assign it to the discard (`_ = Call();`) where the
failure does not matter.

## Results and exceptions

- Return a result for an answer the caller can act on: not found, not allowed, taken, invalid.
- Throw for a bug, such as an argument that may not be null or a state the code never reaches.
- The managers of Tenancy and Membership throw `RefusalException`. Inside a request with a result the mediator turns
  the refusal into a failed result, so a handler calls them without a try.