# Proposal: mutation errors as GraphQL unions from C# 15 unions

This proposal shows the errors of each mutation in the GraphQL schema. A handler declares its outcomes as a C# 15
union, and the toolkit turns the error cases of that union into the error union of the mutation. The proposal
waits for the move to .NET 11. Nothing in `Source/` changes yet.

The folder `spike/` holds the code that the findings below come from. `spike/run.sh` compiles each case and
prints the diagnostics, and `spike/results.txt` holds the output.

## Current state

- `ToolkitMutationErrors` gives every mutation the same four error types: `RefusalError`, `InvalidValuesError`,
  `BrokenRulesError` and `ConcurrencyConflictError`. The schema does not show which codes a mutation can return.
- `ResultFieldsInterceptor` throws an `ErrorException` when a resolver returns a failed `Result<T>`. The mutation
  conventions map no error type to `ErrorException`, so the failure reaches the client as a top-level GraphQL
  error. The payload's `errors` list stays empty.
- An `Error` has one record per kind (`NotFoundError`, `ConflictError` and so on), with the code as a string.
  HotChocolate makes one GraphQL type per CLR type, so it cannot show a separate type per code.

## C# 15 unions

- `public union ReserveResult(Reservation, OutOfStock);` declares a union. The compiler writes a struct with
  `[Union]`, `IUnion`, one constructor per case and an `object? Value` property.
- A case type is any type, such as a record declared elsewhere.
- A `switch` over a union that misses a case gives warning CS8509 (`c1_missing_arm.cs`). The check also covers a
  nested pattern (`c7_nested_pattern.cs`) and a struct with `[Union]` written by hand (`c3c_custom_union.cs`).
- A `return` of a type that is not a case gives error CS0029 (`c2_case_not_in_union.cs`).
- The compiler reports no case that the code never uses (`c6_unused_case.cs`).
- A `partial union` may have its list of cases in one declaration only (CS8863, `c3_partial_union.cs`). Another
  declaration of the same union may add members and operators (`c3b_partial_union_body.cs`).

The spike used Roslyn 5.12.0 from the NuGet package `Microsoft.Net.Compilers.Toolset` on the .NET 10 runtime.
`spike/Polyfill.cs` declares `UnionAttribute` and `IUnion`, which the .NET 11 runtime contains.

## HotChocolate 16

- HotChocolate has no support for C# unions. The source of `ChilliCream/graphql-platform` on `main` (16.8.0
  previews, which target `net11.0`) does not contain `IUnion` or `UnionAttribute`.
- The mutation conventions make one error union per mutation, such as
  `union ReserveStockError = StockBlocked | OutOfStock`. `ErrorTypeHelper` reads the error types from
  `[Error(typeof(T))]` and from the type arguments of `FieldResult<TResult, TError1, ..., TError5>`.
- `FieldResult` takes at most five error types.

## Why the result is a union

A handler that returns `Result<Reservation, ReserveStockErrors>` cannot write `return new OutOfStock(0);`. The
conversion needs two user-defined conversions in a row: from `OutOfStock` to the union, and from the union to the
result. C# applies one (CS0029, `c4_chain.cs` and `c8_switch_mixed_arms.cs`).

A conversion from `Result<T, Small>` to `Result<T, Big>` has no place to live either. A conversion operator must be
declared in its source type or its target type. An extension block may not declare one (CS9282,
`c5_ext_conversion.cs`).

The result is therefore a union with the success type as one of its cases. The generator declares the conversion
from a smaller union in the larger union (`d1_result_is_union.cs`):

```csharp
public union CreateStock(Reservation, SkuInvalid);
public partial union ReserveStock(Reservation, StockBlocked, OutOfStock, SkuInvalid);

public partial union ReserveStock
{
    public static implicit operator ReserveStock(CreateStock from) => from.Value switch { ... };
}
```

With this shape, a handler returns a case, a smaller union or a switch with arms of different types, also from an
`async` method (`d2_async.cs`). A return of `FindStock(Reservation, StockUnknown, StockBlocked)` fails, because
`StockUnknown` is no case of `ReserveStock`.

## What the developer writes and what the generator writes

The full example is in `spike/example`. It compiles. `Generated.cs` holds by hand the code that the generator
would write.

**Errors.** The developer writes the record and its code. The generator writes `Code`, `Message` and `Arguments`
from the properties:

```csharp
[ErrorCode("stock.out-of-stock", "Only {Available} left.")]
public sealed partial record OutOfStock(int Available) : ConflictError;
```

**Domain and store.** The developer writes every method that can fail with a union as its return type. The
success type is the first case:

```csharp
public union ReserveResult(Reservation, OutOfStock);
public union FindStockResult(Stock, StockUnknown, StockBlocked);
public union CreateStockResult(Reservation, SkuInvalid);
```

**Handler.** The developer writes the union of the handler and handles each case of a call in a switch:

```csharp
public partial union ReserveStockResult(Reservation, StockBlocked, OutOfStock, SkuInvalid);

public async Task<ReserveStockResult> Handle(ReserveStockCommand command, CancellationToken ct)
{
    var found = await store.FindAsync(command.Sku, ct);
    return found switch
    {
        Stock stock => stock.Reserve(command.Quantity),
        StockUnknown => await store.CreateAndReserveAsync(command.Sku, command.Quantity, ct),
        StockBlocked blocked => blocked,
    };
}
```

The generator writes a conversion from each smaller union to `ReserveStockResult`, when every case of the smaller
union is also a case of `ReserveStockResult`. Here it writes the conversions from `ReserveResult` and from
`CreateStockResult`. It writes none from `FindStockResult`.

**GraphQL.** The developer writes the resolver:

```csharp
[Mutation]
public static Task<ReserveStockResult> ReserveStockAsync(string sku, int quantity, ISender sender, CancellationToken ct)
    => sender.Send(new ReserveStockCommand(sku, quantity), ct);
```

A type interceptor in `DTS.DDD.HotChocolate` reads the union when the schema is built:

- The case that does not derive from `Error` becomes the field of the payload (`reservation`).
- Each case that derives from `Error` becomes an error type of the mutation, through `AddErrorType`.
- The four general error types stay on every mutation. They cover what a service throws, such as the
  `RefusalException` of Tenancy.

```graphql
type ReserveStockPayload {
  reservation: Reservation
  errors: [ReserveStockError!]
}

union ReserveStockError = StockBlockedError | OutOfStockError | SkuInvalidError
  | RefusalError | InvalidValuesError | BrokenRulesError | ConcurrencyConflictError
```

## Diagnostics and code fixes

| Case | Reported by | Code fix |
|---|---|---|
| A `return` does not fit the union | the compiler, CS0029 | "Add `X` to `ReserveStockResult`" or "Convert to switch" |
| A switch misses a case | the compiler, CS8509 (as an error through `WarningsAsErrors`) | "Add missing arms" |
| No `return` of the method produces a case of the union | a new toolkit diagnostic | "Remove `X` from `ReserveStockResult`" |
| A union has more than one case that does not derive from `Error` | a new toolkit diagnostic | none |

The diagnostic for an unused case reads the types of the `return` expressions of one method, including the arms of
a switch and the branches of `?:`. An error type counts as one case. A union counts as all of its cases.

This check reports too much in one situation. A handler checks `if (found is StockUnknown)` and then writes
`return found;`. The type of `found` still holds `StockUnknown`, so the compiler asks for `StockUnknown` in the
union, and the diagnostic accepts it. The schema then lists an error that the handler never returns. "Convert to
switch" rewrites this shape into a switch with one arm per case. The check never reports too little: every error
that a `return` can produce is a case of the union.

## Without the toolkit

`spike/plain/Plain.cs` shows the same handler with C# 15 and HotChocolate 16 alone. It compiles against
`HotChocolate.Abstractions` 16.6.9.

| | C# and HotChocolate alone | With the toolkit |
|---|---|---|
| Code, arguments and translation of an error | written by hand | written by the generator |
| A smaller union in a larger one | a switch for each call | a conversion from the generator |
| The union in the schema | a `FieldResult` and a switch in each resolver | the interceptor reads the union |
| Error types for each mutation | at most five | no limit |
| A new error of a handler | change the union, the switch of the resolver and the `FieldResult` | change the union |
| A case that never occurs | no diagnostic | the toolkit diagnostic |

## Changes to the core

- `Error` gets `Code`, `Message` and `Arguments` as abstract or virtual properties. A positional record that the
  developer writes cannot pass arguments to a base constructor from a generated part (`spike/example/Core.cs`).
- A new generator for `[ErrorCode]`.
- A new generator for the conversions between unions.
- A type interceptor in `DTS.DDD.HotChocolate` for resolvers that return a union.
- The diagnostics and code fixes in the table above.

## Open points

- Names. The proposal names a record `OutOfStock` in C# and adds `Error` in the schema (`OutOfStockError`). The
  alternative is the name `OutOfStockError` in C#.
- The place of `Result<T>`, `Bind` and `Map`. Handlers with a union return no `Result<T>`.
- The type interceptor is not built. The spike covers the compiler only.
