---
title: "Tenancy API: Callers and requirements"
sidebar_label: Callers and requirements
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Tenancy API: Callers and requirements

Who is calling and in which tenant, the actor a change records, the selection of a seat in the tenant a request names, and the requirements a request states with an attribute.

## Callers

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `TenancyCaller<TTenantId, TSeatId>`

```csharp
public sealed record TenancyCaller<TTenantId, TSeatId> : ITenancyCaller
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Who is asking Tenancy, with the application's ids. Tenancy never takes this from what a caller sends: a seat is found through the caller's verified identity, and system work is begun on purpose with [`TenancyWork`](#tenancywork).

**Type parameters**

- `TTenantId`: The application's tenant id.
- `TSeatId`: The application's seat id.

| Member | Summary |
|---|---|
| `static TenancyCaller<TTenantId, TSeatId> System { get; }` | System work outside any tenant: provisioning only. Recorded as the system, in Tenancy's own scope. |
| `TenancyCallerKind Kind { get; }` | Which kind of caller this is. |
| `TTenantId? Tenant { get; }` | The tenant a seat or system work in a tenant acts in. |
| `TSeatId? Seat { get; }` | The caller's seat; for system work in a tenant, the seat it acts for, if any, which is only recorded as who placed or granted something and gives no rights of its own. |
| `string? Refusal { get; }` | For [`TenancyCallerKind.Nobody`](#tenancycallerkind), the refusal code that says why; otherwise `null`. |
| `TenancyActor<TSeatId>? Actor { get; }` | Who a change this caller makes is recorded as: a seat as itself; system work as the system with its scope, or as the operator or the token [`TenancyWork`](#tenancywork) began it for. `null` for nobody, who changes nothing. An event log's rows, the columns of `RecordsWhoChanged` and Tenancy's own records all read this one answer, so they cannot come to disagree about who acted. |
| [`Nobody(string)`](#nobodystring) | Nobody Tenancy lets in, and why. |
| [`InSeat(TTenantId, TSeatId)`](#inseatttenantid-tseatid) | A person, through their seat in a tenant. |
| [`SystemIn(TTenantId, TSeatId?)`](#systeminttenantid-tseatid) | System work inside one tenant, holding every permission there. |
| [`SystemIn(TTenantId, TenancyActor<TSeatId>)`](#systeminttenantid-tenancyactortseatid) | System work inside one tenant, holding every permission there, recorded as `actor`: the system in a scope of its own, an operator the work is carried out for, or the token a link carried. |
| [`SystemBy(TenancyActor<TSeatId>)`](#systembytenancyactortseatid) | System work outside any tenant, recorded as `actor`: an operator a tenant is provisioned for. |

#### `Nobody(string)`

```csharp
public static TenancyCaller<TTenantId, TSeatId> Nobody(string refusal)
```

Nobody Tenancy lets in, and why.

**Parameters**

- `refusal`: One of the codes of [`TenancyRefusals`](refusals.md#tenancyrefusals), such as [`TenancyRefusals.NotSeated`](refusals.md#tenancyrefusals).

**Exceptions**

- `ArgumentException`: `refusal` is not one of Tenancy's codes.

#### `InSeat(TTenantId, TSeatId)`

```csharp
public static TenancyCaller<TTenantId, TSeatId> InSeat(TTenantId tenant, TSeatId seat)
```

A person, through their seat in a tenant.

**Parameters**

- `tenant`: The tenant.
- `seat`: The person's seat in it.

#### `SystemIn(TTenantId, TSeatId?)`

```csharp
public static TenancyCaller<TTenantId, TSeatId> SystemIn(
    TTenantId tenant,
    TSeatId? actingSeat = null)
```

System work inside one tenant, holding every permission there. Recorded as the system, in Tenancy's own scope.

**Parameters**

- `tenant`: The tenant.
- `actingSeat`: The seat the work is done for, recorded as who placed or granted; it adds no rights.

#### `SystemIn(TTenantId, TenancyActor<TSeatId>)`

```csharp
public static TenancyCaller<TTenantId, TSeatId> SystemIn(
    TTenantId tenant,
    TenancyActor<TSeatId> actor)
```

System work inside one tenant, holding every permission there, recorded as `actor`: the system in a scope of its own, an operator the work is carried out for, or the token a link carried. The seat the actor names, if any, is the seat the work is done for. [`TenancyWork`](#tenancywork) begins such a caller together with the toolkit's own, which is how work is meant to begin it.

**Parameters**

- `tenant`: The tenant.
- `actor`: Who the work is recorded as: any kind but a seat, which acts through its own seat. Made with [`TenancyActor.OfSystem`](#ofsystemstring-tseatid), [`TenancyActor.OfOperator`](#ofoperatorguid-string) or [`TenancyActor.OfToken`](#oftokentseatid-string), which name each kind in full.

**Exceptions**

- `ArgumentException`: `actor` is a seat, or does not say who it is: an operator without an identity, a token without a seat, or any of them without a scope.

#### `SystemBy(TenancyActor<TSeatId>)`

```csharp
public static TenancyCaller<TTenantId, TSeatId> SystemBy(TenancyActor<TSeatId> actor)
```

System work outside any tenant, recorded as `actor`: an operator a tenant is provisioned for. It only provisions, as [`System`](#tenancycallerttenantid-tseatid) does.

**Parameters**

- `actor`: Who the work is recorded as: any kind but a seat, which is in a tenant.

**Exceptions**

- `ArgumentException`: `actor` is a seat, or does not say who it is: an operator without an identity, a token without a seat, or any of them without a scope.

### `ITenancyCaller`

```csharp
public interface ITenancyCaller
```

A [`TenancyCaller<TTenantId, TSeatId>`](#tenancycallerttenantid-tseatid) without its id types, for code that cannot name them: a query filter that only compares the tenant, and the save check.

| Member | Summary |
|---|---|
| `TenancyCallerKind Kind { get; }` | Which kind of caller this is. |
| `object? TenantId { get; }` | The tenant a seat or system work in a tenant acts in, boxed; `null` for the others. |
| `object? SeatId { get; }` | The caller's seat, or the seat system work acts for, boxed; `null` when there is none. |
| `string? Refusal { get; }` | For [`TenancyCallerKind.Nobody`](#tenancycallerkind), the refusal code that says why; otherwise `null`. |
| `ITenancyActor? Actor { get; }` | Who a change this caller makes is recorded as, without its id type; `null` for nobody, who changes nothing. |

### `TenancyCallers`

```csharp
public static class TenancyCallers
```

The Tenancy caller of the current flow of work, which every use case, access question and tenant filter reads. It is kept apart from the toolkit's own `Callers`: that one says which person or which system is calling, this one which seat in which tenant, and a system caller there is never a system caller here.

It is stored without its id types, so a query filter that only compares the tenant can read it without knowing the application's classes. It follows the flow the way an `AsyncLocal<T>` does: into awaited calls and tasks started inside, not back out.

Nothing makes a caller up. Outside any scope there is none, which every reader treats as nobody: [`TenantSelection<TTenantId, TSeatId>`](#tenantselectionttenantid-tseatid) begins a seat for a request, and [`TenancyWork`](#tenancywork) begins system work on purpose.

| Member | Summary |
|---|---|
| `static ITenancyCaller? Ambient { get; }` | The caller [`Begin`](#beginitenancycaller) made current for this flow of work, or `null` outside any. |
| [`Begin(ITenancyCaller)`](#beginitenancycaller) | Makes `caller` the Tenancy caller for everything that runs until the result is disposed, and then the one before it again. |
| [`BeginNone()`](#beginnone) | Makes nobody the Tenancy caller for everything that runs until the result is disposed, and then the one before it again: what runs inside acts in no tenant, whatever the flow of work around it began, as it would outside any scope. |
| [`Current()`](#current) | The current caller with the application's ids; nobody, refused as not seated, outside any scope. |
| [`CurrentTenantOrNull()`](#currenttenantornull) | The tenant the current caller acts in, boxed, for a seat or system work in a tenant; `null` for nobody and for system work outside any tenant, which a tenant filter then matches with nothing. |

#### `Begin(ITenancyCaller)`

```csharp
public static IDisposable Begin(ITenancyCaller caller)
```

Makes `caller` the Tenancy caller for everything that runs until the result is disposed, and then the one before it again. Scopes nest.

**Exceptions**

- `ArgumentNullException`: `caller` is null.

#### `BeginNone()`

```csharp
public static IDisposable BeginNone()
```

Makes nobody the Tenancy caller for everything that runs until the result is disposed, and then the one before it again: what runs inside acts in no tenant, whatever the flow of work around it began, as it would outside any scope. For a read that is no seat's and no tenant's, made in the middle of somebody's work: the tenant of the caller around it must not travel with it.

#### `Current()`

```csharp
public static TenancyCaller<TTenantId, TSeatId> Current<TTenantId, TSeatId>()
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

The current caller with the application's ids; nobody, refused as not seated, outside any scope. A project that sees the application's classes asks the same closed over them, through the class the use cases are named through where they are declared, `TenancyUseCases.CurrentCaller()`. Code that needs only what kind of caller it is reads [`Ambient`](#tenancycallers), which needs no id: none there is nobody, not seated.

**Exceptions**

- `InvalidOperationException`: The current caller was made with other id types: two Tenancy registrations with different ids.

#### `CurrentTenantOrNull()`

```csharp
public static object? CurrentTenantOrNull()
```

The tenant the current caller acts in, boxed, for a seat or system work in a tenant; `null` for nobody and for system work outside any tenant, which a tenant filter then matches with nothing.

### `TenancyWork`

```csharp
public static class TenancyWork
```

The ways system power in Tenancy is meant to be begun. Nothing becomes system work in Tenancy by itself, whatever the toolkit's own caller says: a person reaches Tenancy through a seat, and system work is begun here, on purpose, each method beginning Tenancy's caller together with the toolkit's scoped system caller.

```csharp
using (TenancyWork.BeginSystemIn(tenant, actingSeat))
{
    await seats.PlaceAsync(seat, unit, primary: true, cancellationToken);
}
```

Every method is generic over the application's tenant and seat ids. C# infers both where both are arguments, as above. Where one is not, as for [`BeginSystem<TTenantId, TSeatId>`](#beginsystem) or system work in a tenant for no seat, a project that sees the application's classes calls the same method closed over them, through the class the use cases are named through: `TenancyUseCases.BeginSystem()`, `TenancyUseCases.BeginSystemIn(tenant)`. Only a project that sees nothing but the ids, another module's, writes them: `TenancyWork.BeginSystemIn<TenantId, SeatId>(tenant)`.

Who may send a request is not what its handler runs with. A handler that provisions a tenant begins system work here itself, in trusted code, whatever its request requires: `[AllowAnonymous]` for a registration form anyone may fill in, `[SignedIn]` for a signed-in user who registers an organization. A tool only the application runs is trusted code itself: it begins the work here and calls the use case, or sends its request inside it. The requirement decides who gets as far as the handler; what the work may then do is what is begun here, and nothing of the caller's. System work is no requirement a request declares: it is this scope, and once its result is disposed the caller before it is current again.

Each method begins two callers, and ends both when its result is disposed: the Tenancy caller, which says in which tenant the work acts, and the toolkit's scoped system caller, `Caller.SystemIn(scope)`, which says what kind of work it is. Keeping them in one scope keeps them from drifting apart. Neither begins the toolkit's `Caller.System`: that one stands for the application itself, which row level security lets past every policy, while system work in a tenant must stay inside that tenant. On Postgres the scoped caller runs as a role that cannot bypass the policies, and the tenant reaches them next to it; Tenancy's own policies come with `DDDToolkit.Supporting.Tenancy.Postgres`.

They are not the only way. [`TenancyCallers.Begin`](#beginitenancycaller) with a caller made by [`TenancyCaller.System`](#tenancycallerttenantid-tseatid), `SystemIn` or `SystemBy` begins Tenancy's caller alone, a handle for tests and for code that begins the toolkit's caller itself: it leaves the toolkit's caller, and so the role the database runs as, as it is.

Each also says who the work is recorded as ([`TenancyCaller.Actor`](#tenancycallerttenantid-tseatid)): the system in its scope, unless the work is carried out for an operator ([`BeginOperator<TTenantId, TSeatId>`](#beginoperatorguid), [`BeginOperatorIn<TTenantId, TSeatId>`](#beginoperatorinttenantid-guid-string)) or answers a link that carries a seat's token ([`BeginTokenIn<TTenantId, TSeatId>`](#begintokeninttenantid-tseatid-string)). These are the one way an operator's act is named: an operator holds no seat and changes nothing as itself, so what it asked for is done by system work that names it.

| Member | Summary |
|---|---|
| `SystemScope = "tenancy"` | The scope of the toolkit's scoped system caller for Tenancy's own work. Under the policies of `DDDToolkit.Supporting.Tenancy.Postgres`, only work in this scope writes Tenancy's tables; work in another scope may read them. |
| [`BeginSystem()`](#beginsystem) | Begins system work outside any tenant, for provisioning a tenant. |
| [`BeginSystemIn(TTenantId, TSeatId?, string)`](#beginsysteminttenantid-tseatid-string) | Begins system work inside one tenant, such as an import, seeding or an operator's command: it holds every permission there, and nothing in any other tenant. |
| [`BeginSystemIn(TTenantId, TSeatId, string)`](#beginsysteminttenantid-tseatid-string-1) | Begins system work inside one tenant for a seat: [`BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string) with the seat given, so C# infers both ids from the arguments and the call names neither, `TenancyWork.BeginSystemIn(tenant, seat, "projects")`, in a module that sees only the ids as much as in one that sees the classes. |
| [`BeginOperator(Guid)`](#beginoperatorguid) | Begins system work outside any tenant that provisions a tenant for an operator: the callers of [`BeginSystem<TTenantId, TSeatId>`](#beginsystem), recorded as the operator rather than as the system. |
| [`BeginOperatorIn(TTenantId, Guid, string)`](#beginoperatorinttenantid-guid-string) | Begins system work inside one tenant that carries out what an operator asked for, such as suspending the tenant: the callers of [`BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string), recorded as the operator rather than as the system. |
| [`BeginTokenIn(TTenantId, TSeatId, string)`](#begintokeninttenantid-tseatid-string) | Begins system work inside one tenant that answers a link carrying a token a seat made, such as a feed only its owner knows the address of: the callers of [`BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string) in the module's own scope, recorded as the token and the seat it stands for. |

#### `BeginSystem()`

```csharp
public static IDisposable BeginSystem<TTenantId, TSeatId>()
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Begins system work outside any tenant, for provisioning a tenant. It reads nothing inside one: every tenant filter matches nothing for it. The toolkit's caller is the scoped system caller in [`SystemScope`](#tenancywork), which under the policies of `DDDToolkit.Supporting.Tenancy.Postgres` reads and writes nothing until provisioning narrows it to the tenant it makes.

#### `BeginSystemIn(TTenantId, TSeatId?, string)`

```csharp
public static IDisposable BeginSystemIn<TTenantId, TSeatId>(
    TTenantId tenant,
    TSeatId? actingSeat = null,
    string scope = "tenancy")
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Begins system work inside one tenant, such as an import, seeding or an operator's command: it holds every permission there, and nothing in any other tenant. The last-administrator rule still applies to it.

Work that calls Tenancy's use cases keeps the default scope. A module's own system work in a tenant, that only reads Tenancy's rows next to its own, passes the module's name instead, and then cannot write Tenancy's tables under the policies of `DDDToolkit.Supporting.Tenancy.Postgres`.

**Parameters**

- `tenant`: The tenant.
- `actingSeat`: The seat the work is done for, recorded as who placed or granted; it adds no rights.
- `scope`: The scope of the toolkit's scoped system caller: lower case letters, digits, `_` and `-`.

**Exceptions**

- `ArgumentException`: `scope` is not a scope; nothing is begun then.

#### `BeginSystemIn(TTenantId, TSeatId, string)`

```csharp
public static IDisposable BeginSystemIn<TTenantId, TSeatId>(
    TTenantId tenant,
    TSeatId actingSeat,
    string scope = "tenancy")
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Begins system work inside one tenant for a seat: [`BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string) with the seat given, so C# infers both ids from the arguments and the call names neither, `TenancyWork.BeginSystemIn(tenant, seat, "projects")`, in a module that sees only the ids as much as in one that sees the classes. A seat that may be missing, a `TSeatId?`, goes to that one.

It yields to that one wherever both apply, which is wherever the type arguments are written: there a bare `default` for the seat would otherwise make an empty id the seat the work is recorded for, where it has always meant no seat, as it does for `TenancyUseCases.BeginSystemIn(tenant, default)`. Where they are inferred, this is the only one that applies.

**Parameters**

- `tenant`: The tenant.
- `actingSeat`: The seat the work is done for, recorded as who placed or granted; it adds no rights.
- `scope`: The scope of the toolkit's scoped system caller: lower case letters, digits, `_` and `-`.

**Exceptions**

- `ArgumentException`: `scope` is not a scope; nothing is begun then.

#### `BeginOperator(Guid)`

```csharp
public static IDisposable BeginOperator<TTenantId, TSeatId>(Guid operatorIdentity)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Begins system work outside any tenant that provisions a tenant for an operator: the callers of [`BeginSystem<TTenantId, TSeatId>`](#beginsystem), recorded as the operator rather than as the system. Provisioning then makes the tenant as that operator's act.

**Parameters**

- `operatorIdentity`: The operator's verified identity: the `sub` of the token they signed in with.

**Exceptions**

- `ArgumentException`: `operatorIdentity` is empty; nothing is begun then.

#### `BeginOperatorIn(TTenantId, Guid, string)`

```csharp
public static IDisposable BeginOperatorIn<TTenantId, TSeatId>(
    TTenantId tenant,
    Guid operatorIdentity,
    string scope = "tenancy")
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Begins system work inside one tenant that carries out what an operator asked for, such as suspending the tenant: the callers of [`BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string), recorded as the operator rather than as the system. The operator gets nothing by it: the work holds what system work in the tenant holds, and the operator is only who it is recorded as.

The identity is the verified one of the request that asked, never one a caller sends: whoever begins this took it from the token, or from a record written by the request that did.

**Parameters**

- `tenant`: The tenant.
- `operatorIdentity`: The operator's verified identity: the `sub` of the token they signed in with.
- `scope`: The scope of the toolkit's scoped system caller: lower case letters, digits, `_` and `-`.

**Exceptions**

- `ArgumentException`: `operatorIdentity` is empty, or `scope` is not a scope; nothing is begun then.

#### `BeginTokenIn(TTenantId, TSeatId, string)`

```csharp
public static IDisposable BeginTokenIn<TTenantId, TSeatId>(
    TTenantId tenant,
    TSeatId seat,
    string scope)
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Begins system work inside one tenant that answers a link carrying a token a seat made, such as a feed only its owner knows the address of: the callers of [`BeginSystemIn<TTenantId, TSeatId>`](#beginsysteminttenantid-tseatid-string) in the module's own scope, recorded as the token and the seat it stands for.

It is system work, not the seat: it holds what system work in the tenant holds, so the module that begins it narrows what it reads to that seat itself. Begin it only once the token was checked and found to be that seat's, and the seat to be in use.

**Parameters**

- `tenant`: The tenant.
- `seat`: The seat the token stands for.
- `scope`: The scope of the toolkit's scoped system caller, the module's name: lower case letters, digits, `_` and `-`.

**Exceptions**

- `ArgumentException`: `scope` is not a scope; nothing is begun then.

### `TenancyOperatorTokenRoles`

```csharp
public sealed class TenancyOperatorTokenRoles
```

The token roles of the application's operators, as Tenancy was registered with them (`TenancyOptions.OperatorTokenRoles`), for code that cannot name the application's id types: a start-up check that compares them with what the database has. Registered once, by `AddTenancy`.

| Member | Summary |
|---|---|
| [`TenancyOperatorTokenRoles(IEnumerable<string>)`](#tenancyoperatortokenrolesienumerablestring) | The primary constructor: the type's summary says what it makes. |
| `IReadOnlyList<string> TokenRoles { get; }` | The operators' token roles, each once, in ordinal order. Empty for an application without operators. |

#### `TenancyOperatorTokenRoles(IEnumerable<string>)`

```csharp
public TenancyOperatorTokenRoles(IEnumerable<string> tokenRoles)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `tokenRoles`: The operators' token roles, as the tokens spell them.

### `TenancyCallerKind`

```csharp
public enum TenancyCallerKind
```

Who is asking Tenancy, as Tenancy sees it.

| Member | Summary |
|---|---|
| `Nobody = 0` | Nobody Tenancy lets in: no tenant named, no seat in it, or a seat or tenant that is not active. Holds nothing and changes nothing; [`ITenancyCaller.Refusal`](#itenancycaller) says why. |
| `Seat = 1` | A person, through a seat that is active, in a tenant that is too. Holds what the seat's roles grant. |
| `SystemInTenant = 2` | System work inside one tenant, such as an import, seeding or an operator's command. Holds every permission in that tenant and nothing in any other. |
| `System = 3` | System work outside any tenant. It only provisions tenants; it reads and changes nothing inside one. |

## Actors

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `ITenancyActor`

```csharp
public interface ITenancyActor
```

A [`TenancyActor<TSeatId>`](#tenancyactortseatid) without its id type, for code that cannot name it: the save that fills the columns of `RecordsWhoChanged`, and the accessor that tells an event log who acted.

| Member | Summary |
|---|---|
| `TenancyActorKind Kind { get; }` | What kind of actor it is. |
| `object? SeatId { get; }` | The seat, boxed: a seat's own, the seat a token stands for, or the seat system work is done for; `null` when there is none. |
| `Guid? Operator { get; }` | An operator's verified identity; `null` for every other kind. |
| `string? Scope { get; }` | The scope of the application's own work; `null` for a seat. |
| [`ToActedBy()`](#toactedby) | The actor as the toolkit's own records keep it: a kind and one id. |

#### `ToActedBy()`

```csharp
ActedBy ToActedBy()
```

The actor as the toolkit's own records keep it: a kind and one id.

### `TenancyActorKindJsonConverter`

```csharp
public sealed class TenancyActorKindJsonConverter : JsonConverter<TenancyActorKind>
```

Writes a kind as the word a record keeps it by. An event that names who made a change is stored for good, and a number would change its meaning with the order the members are declared in.

The kind of a [`TenancyActor<TSeatId>`](#tenancyactortseatid) names this converter itself, so the word does not depend on what the serializer is told about enums in general: an outbox whose options write every enum by its member's name still stores `seat`, the word the row of an event log keeps next to it. It is public so that a serializer context generated at compile time can name it too.

| Member | Summary |
|---|---|
| [`Read(ref Utf8JsonReader, Type, JsonSerializerOptions)`](#readref-utf8jsonreader-type-jsonserializeroptions) | Reads and converts the JSON to type `T`. |
| [`Write(Utf8JsonWriter, TenancyActorKind, JsonSerializerOptions)`](#writeutf8jsonwriter-tenancyactorkind-jsonserializeroptions) | Writes a specified value as JSON. |

#### `Read(ref Utf8JsonReader, Type, JsonSerializerOptions)`

```csharp
public override TenancyActorKind Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options)
```

Reads and converts the JSON to type `T`.

**Parameters**

- `reader`: The reader.
- `typeToConvert`: The type to convert.
- `options`: An object that specifies serialization options to use.

**Returns**: The converted value.

**Exceptions**

- `JsonException`: The value is not one of the four words.

#### `Write(Utf8JsonWriter, TenancyActorKind, JsonSerializerOptions)`

```csharp
public override void Write(
    Utf8JsonWriter writer,
    TenancyActorKind value,
    JsonSerializerOptions options)
```

Writes a specified value as JSON.

**Parameters**

- `writer`: The writer to write to.
- `value`: The value to convert to JSON.
- `options`: An object that specifies serialization options to use.

### `TenancyActorKinds`

```csharp
public static class TenancyActorKinds
```

The kinds Tenancy writes into a record that keeps who acted, next to the toolkit's own (`ActedByKinds`): an event log's row, and the columns of `RecordsWhoChanged`.

| Member | Summary |
|---|---|
| `Seat = "seat"` | A person, through their seat; the id is the seat's. |
| `Operator = "operator"` | An operator of the application; the id is the operator's verified identity. |
| `System = "system"` | The application's own work; the id is the scope it runs in. The toolkit's own kind. |
| `Token = "token"` | A link that carries a token; the id is the seat the token stands for. |
| [`From(TenancyActorKind)`](#fromtenancyactorkind) | The kind as a record keeps it. |

#### `From(TenancyActorKind)`

```csharp
public static string From(TenancyActorKind kind)
```

The kind as a record keeps it.

**Exceptions**

- `ArgumentOutOfRangeException`: `kind` is none of the four.

### `TenancyActor<TSeatId>`

```csharp
public readonly record struct TenancyActor<TSeatId> : ITenancyActor
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Who made a change Tenancy records. Ids only: a seat, an operator's verified identity, or the scope of the application's own work. Never a name, and never an address.

Make one with [`OfSeat`](#ofseattseatid), [`OfOperator`](#ofoperatorguid-string), [`OfSystem`](#ofsystemstring-tseatid) or [`OfToken`](#oftokentseatid-string), which give each kind what it is named by. The Tenancy caller carries the actor of the work it stands for ([`TenancyCaller.Actor`](#tenancycallerttenantid-tseatid)), so nothing works one out for itself: a request's seat, and what [`TenancyWork`](#tenancywork) began on purpose.

Every domain event of Tenancy's carries one as `By`, the actor of the caller whose command raised it, so an event that is stored, kept in an event log or published says who made the change without anything else being read.

**Type parameters**

- `TSeatId`: The application's seat id.

| Member | Summary |
|---|---|
| [`TenancyActor(TenancyActorKind, TSeatId?, Guid?, string?)`](#tenancyactortenancyactorkind-tseatid-guid-string) | The primary constructor: the type's summary says what it makes. |
| `TenancyActorKind Kind { get; init; }` | What kind of actor it is. |
| `TSeatId? Seat { get; init; }` | The seat: a seat's own, the seat a token stands for, or, for the application's own work, the seat it is done for, which is not who acted. |
| `Guid? Operator { get; init; }` | An operator's verified identity. |
| `string? Scope { get; init; }` | The scope the application's own work runs in. |
| [`OfSeat(TSeatId)`](#ofseattseatid) | A person, through `seat`. |
| [`OfOperator(Guid, string)`](#ofoperatorguid-string) | An operator of the application, by the verified identity they signed in with, in the scope of the work that carries out what they asked. |
| [`OfSystem(string, TSeatId?)`](#ofsystemstring-tseatid) | The application's own work in `scope`. |
| [`OfToken(TSeatId, string)`](#oftokentseatid-string) | A link that carries a token `seat` made, in the scope of the work that answers it. |
| [`ToActedBy()`](#toactedby-1) | The toolkit's own `ActedBy`: `seat` with the seat's id, `operator` with the identity, `system` with the scope, and `token` with the seat's id. |

#### `TenancyActor(TenancyActorKind, TSeatId?, Guid?, string?)`

```csharp
public TenancyActor(
    TenancyActorKind Kind,
    TSeatId? Seat = null,
    Guid? Operator = null,
    string? Scope = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Kind`: What kind of actor it is.
- `Seat`: The seat: a seat's own, the seat a token stands for, or, for the application's own work, the seat it is done for, which is not who acted.
- `Operator`: An operator's verified identity.
- `Scope`: The scope the application's own work runs in.

#### `OfSeat(TSeatId)`

```csharp
public static TenancyActor<TSeatId> OfSeat(TSeatId seat)
```

A person, through `seat`.

#### `OfOperator(Guid, string)`

```csharp
public static TenancyActor<TSeatId> OfOperator(Guid identity, string scope)
```

An operator of the application, by the verified identity they signed in with, in the scope of the work that carries out what they asked. An operator holds no seat, so there is none to name.

**Parameters**

- `identity`: The operator's verified identity.
- `scope`: The scope of the work that acts for them.

**Exceptions**

- `ArgumentException`: `identity` is empty, or `scope` is blank.

#### `OfSystem(string, TSeatId?)`

```csharp
public static TenancyActor<TSeatId> OfSystem(string scope, TSeatId? forSeat = null)
```

The application's own work in `scope`.

**Parameters**

- `scope`: The scope it runs in.
- `forSeat`: The seat it is done for, if any: recorded next to the scope, and not who acted.

**Exceptions**

- `ArgumentException`: `scope` is blank.

#### `OfToken(TSeatId, string)`

```csharp
public static TenancyActor<TSeatId> OfToken(TSeatId seat, string scope)
```

A link that carries a token `seat` made, in the scope of the work that answers it.

**Parameters**

- `seat`: The seat the token stands for.
- `scope`: The scope of the work that answers the link.

**Exceptions**

- `ArgumentException`: `scope` is blank.

#### `ToActedBy()`

```csharp
public ActedBy ToActedBy()
```

The toolkit's own `ActedBy`: `seat` with the seat's id, `operator` with the identity, `system` with the scope, and `token` with the seat's id. An id is written as its value, with the invariant culture and a `Guid` as `D`, which is how a database writes the same value as text: a policy can compare the two.

### `TenancyActorKind`

```csharp
public enum TenancyActorKind
```

What kind of actor made a change Tenancy records. As JSON it is the word of [`TenancyActorKinds`](#tenancyactorkinds), `seat`, `operator`, `system` or `token`: the word an event log and the columns of `RecordsWhoChanged` keep as well ([`TenancyActorKindJsonConverter`](#tenancyactorkindjsonconverter)).

| Member | Summary |
|---|---|
| `Seat = 0` | A person, through their seat. |
| `Operator = 1` | An operator of the application, named by their verified identity: never a seat of any tenant. |
| `System = 2` | The application's own work, named by the scope it runs in. |
| `Token = 3` | A link that carries a token a seat made: the seat it stands for. |

## Tenant selection

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `ISeatDirectory<TTenantId, TSeatId>`

```csharp
public interface ISeatDirectory<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Finds a person's seats by their verified identity: the one lookup that looks across tenants, and only ever for the identity asking. Tenancy's storage implements it.

| Member | Summary |
|---|---|
| [`FindAsync(Guid, string, CancellationToken)`](#findasyncguid-string-cancellationtoken) | The identity's seat in the tenant with this slug, or `null` when there is no such tenant or no seat in it for this identity; the two are not told apart. |
| [`AllOfAsync(Guid, CancellationToken)`](#allofasyncguid-cancellationtoken) | Every seat the identity has, in every tenant and in any status, for a tenant picker: the application's own seats, whole, each beside its tenant ([`SeatInTenant<TSeat>`](#seatintenanttseat)). |

#### `FindAsync(Guid, string, CancellationToken)`

```csharp
Task<SeatOfCaller<TTenantId, TSeatId>?> FindAsync(
    Guid identity,
    string tenantSlug,
    CancellationToken cancellationToken)
```

The identity's seat in the tenant with this slug, or `null` when there is no such tenant or no seat in it for this identity; the two are not told apart.

**Parameters**

- `identity`: The verified identity, the subject of the caller's token.
- `tenantSlug`: The tenant's slug, trimmed and lowercased.
- `cancellationToken`: Cancels the lookup.

#### `AllOfAsync(Guid, CancellationToken)`

```csharp
Task<IReadOnlyList<SeatInTenant<TSeat>>> AllOfAsync<TSeat>(
    Guid identity,
    CancellationToken cancellationToken)
    where TSeat : class
```

Every seat the identity has, in every tenant and in any status, for a tenant picker: the application's own seats, whole, each beside its tenant ([`SeatInTenant<TSeat>`](#seatintenanttseat)). A seat has no name in Tenancy, so a picker shows one by what the application keeps on its seat class, such as the name it is shown by in that tenant, from the seats read here, with no statement more.

**Parameters**

- `identity`: The verified identity, the subject of the caller's token.
- `cancellationToken`: Cancels the lookup.

**Type parameters**

- `TSeat`: The application's seat class, which the storage keeps; or a class it derives from.

**Exceptions**

- `InvalidOperationException`: `TSeat` is no class the storage's seats are.

### `ITenantSelection`

```csharp
public interface ITenantSelection
```

[`TenantSelection<TTenantId, TSeatId>`](#tenantselectionttenantid-tseatid) without its id types, for the code that makes a request's Tenancy caller current and needs no id to do it. A host's middleware takes the caller it is answered and begins it, and [`TenancyCallers.Begin`](#beginitenancycaller) takes any [`ITenancyCaller`](#itenancycaller), so the middleware names neither of the application's ids, and reads the same in every application:

```csharp
var seat = await context.RequestServices.GetRequiredService<ITenantSelection>()
    .ResolveAsync(caller, context.Request.Headers["Tenant"], context.RequestAborted);
using (TenancyCallers.Begin(seat))
{
    await next(context);
}
```

`AddTenancy` registers it per scope as the very selection it registers closed over the ids, so the two answer alike, unless the host registered one of its own before, which then stays. What answers records with the ids in them, a person's own seats for a tenant picker ([`TenantSelection.SeatsOfAsync<TSeat>`](#seatsofasynccaller-cancellationtoken)), is that one's alone.

| Member | Summary |
|---|---|
| [`ResolveAsync(Caller, string?, CancellationToken)`](#resolveasynccaller-string-cancellationtoken) | The Tenancy caller for `caller` in the tenant `tenantSlug` names: its active seat there, or nobody and why, as [`TenantSelection.ResolveAsync`](#resolveasynccaller-string-cancellationtoken-1) answers it. |

#### `ResolveAsync(Caller, string?, CancellationToken)`

```csharp
Task<ITenancyCaller> ResolveAsync(
    Caller caller,
    string? tenantSlug,
    CancellationToken cancellationToken)
```

The Tenancy caller for `caller` in the tenant `tenantSlug` names: its active seat there, or nobody and why, as [`TenantSelection.ResolveAsync`](#resolveasynccaller-string-cancellationtoken-1) answers it. Every refusal comes back as nobody, with the refusal's code; nothing is thrown for a caller Tenancy does not let in.

**Parameters**

- `caller`: Who is calling, as the toolkit says.
- `tenantSlug`: The tenant the request names, by its slug; trimmed and lowercased.
- `cancellationToken`: Cancels the lookup.

### `TenantSelection<TTenantId, TSeatId>`

```csharp
public sealed class TenantSelection<TTenantId, TSeatId> : ITenantSelection
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

Turns the toolkit's caller and the tenant a request names into a Tenancy caller: the caller's active seat in that tenant, or nobody and why.

The request names the tenant by its slug only. The tenant's id and the seat's id always come from the directory, found through the caller's verified identity, so nothing a caller sends picks a seat. The toolkit's system caller is nobody here: system work in Tenancy is begun on purpose with [`TenancyWork`](#tenancywork), never inherited from a request.

A host's middleware, which only makes the caller it is answered current, asks the same of [`ITenantSelection`](#itenantselection), the very instance without its id types, and names no id.

Only a signed-in user whose token carries a seated role is looked up at all, [`TenantSelectionOptions.SeatedTokenRoles`](#tenantselectionoptions): `authenticated` unless the application lists others. A user with another token role is nobody in every tenant, and is told what a person without a seat is told, so the answer says nothing about the seats their identity has. The list of a person's own seats, [`SeatsOfAsync<TSeat>`](#seatsofasynccaller-cancellationtoken), follows the same rule.

| Member | Summary |
|---|---|
| [`TenantSelection(ISeatDirectory<TTenantId, TSeatId>, TenantSelectionOptions?)`](#tenantselectioniseatdirectoryttenantid-tseatid-tenantselectionoptions) | The primary constructor: the type's summary says what it makes. |
| [`ResolveAsync(Caller, string?, CancellationToken)`](#resolveasynccaller-string-cancellationtoken-1) | The Tenancy caller for `caller` in the tenant `tenantSlug` names. |
| [`SeatsOfAsync(Caller, CancellationToken)`](#seatsofasynccaller-cancellationtoken) | Every seat `caller` has, in every tenant and in any status, for a tenant picker: the application's own seats, whole, each beside its tenant ([`SeatInTenant<TSeat>`](#seatintenanttseat)), so a picker shows a seat by the name the application keeps on its seat class, say, as every other answer about the seat does. |

#### `TenantSelection(ISeatDirectory<TTenantId, TSeatId>, TenantSelectionOptions?)`

```csharp
public TenantSelection(
    ISeatDirectory<TTenantId, TSeatId> seats,
    TenantSelectionOptions? options = null)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `seats`: Finds the caller's seats.
- `options`: Which token roles are seated; signed-in users with the role `authenticated` when left out.

#### `ResolveAsync(Caller, string?, CancellationToken)`

```csharp
public Task<TenancyCaller<TTenantId, TSeatId>> ResolveAsync(
    Caller caller,
    string? tenantSlug,
    CancellationToken cancellationToken)
```

The Tenancy caller for `caller` in the tenant `tenantSlug` names. Every refusal comes back as nobody, with the refusal's code; nothing is thrown for a caller Tenancy does not let in.

**Parameters**

- `caller`: Who is calling, as the toolkit says.
- `tenantSlug`: The tenant the request names, by its slug; trimmed and lowercased here.
- `cancellationToken`: Cancels the lookup.

#### `SeatsOfAsync(Caller, CancellationToken)`

```csharp
public Task<IReadOnlyList<SeatInTenant<TSeat>>> SeatsOfAsync<TSeat>(
    Caller caller,
    CancellationToken cancellationToken)
    where TSeat : class
```

Every seat `caller` has, in every tenant and in any status, for a tenant picker: the application's own seats, whole, each beside its tenant ([`SeatInTenant<TSeat>`](#seatintenanttseat)), so a picker shows a seat by the name the application keeps on its seat class, say, as every other answer about the seat does. Looked up by the verified identity of the caller's own token, and by nothing a request could supply. None for a caller that is no signed-in user, and none for a user whose token role holds no seat: nothing is looked up for them, and they are answered what a person without a seat is answered, as in [`ResolveAsync`](#resolveasynccaller-string-cancellationtoken-1).

**Parameters**

- `caller`: Who is calling, as the toolkit says.
- `cancellationToken`: Cancels the lookup.

**Type parameters**

- `TSeat`: The application's seat class, or a class it derives from.

**Exceptions**

- `InvalidOperationException`: `TSeat` is no class the storage's seats are.

### `TenantSelectionOptions`

```csharp
public sealed class TenantSelectionOptions
```

Which signed-in users may hold a seat: those whose token carries one of the listed roles. A host that gives some of its users a token role of their own, analysts who look across tenants say, leaves that role off the list, and such a user is nobody in every tenant, whatever seats their identity has.

```csharp
services.AddTenancy<ShopTenancyContext>(options =>
{
    ...
    options.TenantSelection.SeatedTokenRoles.Add("member");
});
```

| Member | Summary |
|---|---|
| `AuthenticatedTokenRole = "authenticated"` | The role of a signed-in user whose token says nothing else, and of a caller made without a token. |
| `IList<string> SeatedTokenRoles { get; }` | The token roles whose users are seated, `Caller.Role` as the token spells it: `authenticated` by default. A caller without a role counts as `authenticated`. |

### `SeatInTenant<TSeat>`

```csharp
public sealed record SeatInTenant<TSeat>
    where TSeat : class
```

One of a person's seats, for a tenant picker: the application's own seat, whole, and only what is not on the seat itself, the tenant it is in by what a picker shows of it. The seat carries its id, its tenant's id, its status and every field the application added, such as the name the person is shown by in that tenant, so a picker shows a seat by what the application chose, as every other answer about the seat does ([`TenantSelection.SeatsOfAsync<TSeat>`](#seatsofasynccaller-cancellationtoken)).

**Type parameters**

- `TSeat`: The application's seat class, or a class it derives from.

**Remarks**

The seat was read for the picker and is tracked by nobody: nothing done to it is saved. It was read across tenants, before one is picked, so where it is placed and the roles it holds are its tenant's to answer, in that tenant: a database that keeps tenants apart hands them to nobody outside it, and its placements may be missing here. What leaves is what the application selects: the seat has its identity.

| Member | Summary |
|---|---|
| [`SeatInTenant(TSeat, string, string, TenantStatus)`](#seatintenanttseat-string-string-tenantstatus) | The primary constructor: the type's summary says what it makes. |
| `TSeat Seat { get; init; }` | The seat, the application's own class. |
| `string Slug { get; init; }` | The slug its tenant is selected by. |
| `string OrganizationName { get; init; }` | Its tenant's name. |
| `TenantStatus TenantStatus { get; init; }` | Where its tenant is in its life. |

#### `SeatInTenant(TSeat, string, string, TenantStatus)`

```csharp
public SeatInTenant(TSeat Seat, string Slug, string OrganizationName, TenantStatus TenantStatus)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Seat`: The seat, the application's own class.
- `Slug`: The slug its tenant is selected by.
- `OrganizationName`: Its tenant's name.
- `TenantStatus`: Where its tenant is in its life.

**Remarks**

The seat was read for the picker and is tracked by nobody: nothing done to it is saved. It was read across tenants, before one is picked, so where it is placed and the roles it holds are its tenant's to answer, in that tenant: a database that keeps tenants apart hands them to nobody outside it, and its placements may be missing here. What leaves is what the application selects: the seat has its identity.

### `SeatOfCaller<TTenantId, TSeatId>`

```csharp
public sealed record SeatOfCaller<TTenantId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

A person's seat in one tenant, as the directory finds it for their verified identity when the caller's seat is selected ([`ISeatDirectory.FindAsync`](#findasyncguid-string-cancellationtoken)): the tenant and the seat, each by its id and status, which is all the selection decides by. A tenant picker is answered the application's own seats instead ([`ISeatDirectory.AllOfAsync<TSeat>`](#allofasyncguid-cancellationtoken)).

| Member | Summary |
|---|---|
| [`SeatOfCaller(TTenantId, string, string, TenantStatus, TSeatId, SeatStatus)`](#seatofcallerttenantid-string-string-tenantstatus-tseatid-seatstatus) | The primary constructor: the type's summary says what it makes. |
| `TTenantId Tenant { get; init; }` | The tenant's id. |
| `string Slug { get; init; }` | The slug the tenant is selected by. |
| `string OrganizationName { get; init; }` | The tenant's name. |
| `TenantStatus TenantStatus { get; init; }` | Where the tenant is in its life. |
| `TSeatId Seat { get; init; }` | The seat's id. |
| `SeatStatus SeatStatus { get; init; }` | Whether the seat counts. |

#### `SeatOfCaller(TTenantId, string, string, TenantStatus, TSeatId, SeatStatus)`

```csharp
public SeatOfCaller(
    TTenantId Tenant,
    string Slug,
    string OrganizationName,
    TenantStatus TenantStatus,
    TSeatId Seat,
    SeatStatus SeatStatus)
```

The primary constructor: the type's summary says what it makes.

**Parameters**

- `Tenant`: The tenant's id.
- `Slug`: The slug the tenant is selected by.
- `OrganizationName`: The tenant's name.
- `TenantStatus`: Where the tenant is in its life.
- `Seat`: The seat's id.
- `SeatStatus`: Whether the seat counts.

## Requirements

Namespace `DDDToolkit.Supporting.Tenancy.Access`.

### `AtTenantAttribute`

```csharp
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class AtTenantAttribute : PermissionLevelAttribute
```

The level of a permission that is asked for the whole tenant: held by a role of the organization granted at its root, the way [`TenancyAccess.AtTenant`](#attenantstring) asks it, and what a request's `RequiresPermissionAttribute` without a marker requires. A constant of a `PermissionsAttribute` class carries it.

```csharp
[Permissions]
public static class BoardPermissions
{
    /// <summary>Make and change the roles of boards.</summary>
    [AtTenant]
    public const string ManageRoles = "boards.roles.manage";
}
```

**Remarks**

Tenancy's generator puts it into the catalogue, under the module the project declares, with what `PermissionLevelAttribute` says of it, so an organization's role can hold it. With `[AtUnit]` beside it, a permission asked both ways, for the whole tenant and at a unit, says so.

### `AtUnitAttribute`

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
public sealed class AtUnitAttribute : PermissionLevelAttribute
```

At a unit: on a member of a request, the unit at which the permission of its `RequiresPermissionAttribute` is held, there or at a unit above it; on a constant of a `PermissionsAttribute` class, that the permission is asked at a unit. The one meaning, said where the permission is required and where it is declared.

```csharp
[Permissions]
public static class BoardPermissions
{
    /// <summary>Open a board at a unit.</summary>
    [AtUnit]
    public const string Create = "boards.create";
}
```

On a request it is what [`TenancyAccess.AtUnit<TUnitId>`](#atunitstring-tunitid) says, written on the member of the request it reads.

```csharp
[RequiresPermission(TenancyPermissions.GrantsManage)]
public sealed record MakeGrant(SeatId Seat, [AtUnit] OrganizationUnitId Unit, RoleId Role) : ICommand, ITenantsRequest;
```

is `TenancyAccess.AtUnit(TenancyPermissions.GrantsManage, request.Unit)`.

**Remarks**

It goes on a positional parameter of a record as it is, or on a property, and `[property: AtUnit]` is read as well. The member is the application's unit id, the id its class declared with `[OrganizationUnit<TUnitId>]` is declared over: on another id, a seat's say, it is DDD00081, where the project sees that class. A unit that may be left out, `OrganizationUnitId?`, is a requirement computed from whether it is given, so the request answers `RequiredAccess` itself: `Unit is { } unit ? TenancyAccess.AtUnit(permission, unit) : TenancyAccess.AtTenant(permission)`. A permission without any marker is held at the tenant.

On a permission it is its level, read by Tenancy's generator into the catalogue under the module, with what `PermissionLevelAttribute` says of it; the settings of a level say nothing on a request's member. A permission asked at a unit is held there by a role of the organization granted at that unit or above it, and reaches every unit below.

Not beside `[OrOwn]`: the caller would send the unit the permission is checked at for another subject, so a permission held at one unit would act on a seat anywhere (DDD00085). A permission at a unit is held by a seat in the tenant or by system work there, so `[InTenant]` beside it says nothing more, and is a warning (DDD00084).

### `InTenantAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class InTenantAttribute : Attribute
```

Says on a request that its caller works in the tenant the request was sent for: a seat there, or system work there. What [`TenancyAccess.InTenant`](#intenant) says, written where the request is declared:

```csharp
[InTenant]
public sealed record TenantRoles : IQuery<IReadOnlyList<RoleListing>>, ITenantsRequest;
```

**Remarks**

The toolkit's generator turns it into `TenancyAccess.InTenant()` at compile time, in the module's request interface (`[AccessRequests]`, declared `partial`), and the check that `services.AddTenancyAccess<TRequests, TContext>()` adds holds the caller to it. A request handed to a use case of the package that asks more than this, a permission the use case reads where it acts, says this much and the use case asks the rest. Beside a permission of Tenancy's, `[RequiresPermission]` with or without `[AtUnit]`, it says nothing more, since only a caller in the tenant holds one, and the generator warns of it (DDD00084).

On the module's interface it is the module's default, said once where nearly every request reads the tenant's data: a request of the module that states nothing of its own requires a caller in the tenant, and one that states its own, a permission say, keeps only its own.

```csharp
[AccessRequests, InTenant]
public partial interface IProjectsRequest : IRequireAccess;
```

### `RequiresOperatorAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class RequiresOperatorAttribute : Attribute
```

Says on a query that its caller is one of the application's operators, and nobody else is: no seat, whatever it holds, and no system work. What [`TenancyAccess.RequiresOperator`](#requiresoperator) says, written where the query is declared:

```csharp
[RequiresOperator]
public sealed record AllTenants : IQuery<IReadOnlyList<TenantListing>>, ITenantsRequest;
```

**Remarks**

For queries only: an operator changes nothing itself, and what an operator asks for is carried out by system work that names the operator, `TenancyUseCases.BeginOperator(identity)`. On a message of the Mediator library that is no query, a command or a plain request, it is DDD00082. The toolkit's generator turns it into `TenancyAccess.RequiresOperator()` at compile time, in the module's request interface (`[AccessRequests]`, declared `partial`). For the same reason it is no module's default on that interface, which a command that says nothing would take as well (DDD00089).

### `TenancyAccess`

```csharp
public static class TenancyAccess
```

How a request spells what it requires where Tenancy is the one that knows, beside the core's own (`AccessRequirement.SignedIn()` and the like) and Membership's (`MemberAccess.On(permission, resource)`):

```csharp
AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.InTenant();
AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtTenant(TenancyPermissions.RolesManage);
AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtUnit(TenancyPermissions.GrantsManage, Unit);
AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.RequiresOperator();
```

Each answers one of [`TenancyRequirement`](#tenancyrequirement)'s cases, which the check that `services.AddTenancyAccess<TRequests, TContext>()` adds holds the caller to. Where the requirement reads no value of the request but the permission and a unit it carries, the request says it with an attribute instead, and the toolkit's generator writes the same call: [`InTenantAttribute`](#intenantattribute), `[RequiresPermission(permission)]` for the whole tenant, with [`AtUnitAttribute`](#atunitattribute) on the unit for a permission there, and [`RequiresOperatorAttribute`](#requiresoperatorattribute). These methods stay for a requirement computed from the request's values: `Unit is { } unit ? TenancyAccess.AtUnit(permission, unit) : TenancyAccess.AtTenant(permission)`.

**Remarks**

They are the one spelling: the cases' constructors are internal, as the core's are, and these methods make them. Methods rather than constructors, so a case closed over the application's unit id takes it from the argument: `AtUnit(permission, unit)`.

| Member | Summary |
|---|---|
| [`InTenant()`](#intenant) | The caller works in the tenant the request was sent for: a seat there, or system work there ([`TenancyRequirement.InTenant`](#tenancyrequirementintenant)). |
| [`AtTenant(string)`](#attenantstring) | The caller holds `permission` for the whole tenant, at its root, now ([`TenancyRequirement.AtTenant`](#tenancyrequirementattenant)). |
| [`AtUnit(string, TUnitId)`](#atunitstring-tunitid) | The caller holds `permission` at `unit`, there or at a unit above it, now ([`TenancyRequirement.AtUnit<TUnitId>`](#tenancyrequirementatunittunitid)). |
| [`RequiresOperator()`](#requiresoperator) | The caller is one of the application's operators, and nobody else is: no seat, whatever it holds, and no system work ([`TenancyRequirement.Operator`](#tenancyrequirementoperator)). |
| [`Own(TSeatId)`](#owntseatid) | The request is about the caller's own seat: `seat` is the caller's seat in the tenant of the request. |

#### `InTenant()`

```csharp
public static InTenant InTenant()
```

The caller works in the tenant the request was sent for: a seat there, or system work there ([`TenancyRequirement.InTenant`](#tenancyrequirementintenant)).

#### `AtTenant(string)`

```csharp
public static AtTenant AtTenant(string permission)
```

The caller holds `permission` for the whole tenant, at its root, now ([`TenancyRequirement.AtTenant`](#tenancyrequirementattenant)).

**Parameters**

- `permission`: The permission the request needs for the whole tenant: one of the catalogue's.

**Exceptions**

- `ArgumentException`: `permission` is blank.

#### `AtUnit(string, TUnitId)`

```csharp
public static AtUnit<TUnitId> AtUnit<TUnitId>(string permission, TUnitId unit)
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

The caller holds `permission` at `unit`, there or at a unit above it, now ([`TenancyRequirement.AtUnit<TUnitId>`](#tenancyrequirementatunittunitid)).

**Parameters**

- `permission`: The permission the request needs at the unit: one of the catalogue's.
- `unit`: The unit, from the request.

**Type parameters**

- `TUnitId`: The application's unit id.

**Exceptions**

- `ArgumentException`: `permission` is blank.

**Remarks**

The type is taken from the argument, so the compiler does not tell a unit from another id: on a request that also carries a seat, `AtUnit(permission, Seat)` compiles. It lets nobody through, since the check decides the case for the application's unit id alone and stops every send of another with an `InvalidOperationException` that names it. Where a request carries more ids than one, write the type, `AtUnit<OrganizationUnitId>(permission, Unit)`, and the compiler catches the wrong one; a test that holds every request to the requirement it should declare catches it as well.

#### `RequiresOperator()`

```csharp
public static Operator RequiresOperator()
```

The caller is one of the application's operators, and nobody else is: no seat, whatever it holds, and no system work ([`TenancyRequirement.Operator`](#tenancyrequirementoperator)). For queries only.

**Remarks**

Named for the one kind of caller it lets through. A query says it with [`RequiresOperatorAttribute`](#requiresoperatorattribute) as well.

#### `Own(TSeatId)`

```csharp
public static Owned<TSeatId> Own<TSeatId>(TSeatId seat)
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
```

The request is about the caller's own seat: `seat` is the caller's seat in the tenant of the request. Anyone else is refused with `access.not-own`: another seat, a caller with no seat there, an operator, and system work, which holds every permission in its tenant but has no seat of its own. Spelled beside the other cases, for a requirement that combines them:

```csharp
AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.Any(
    TenancyAccess.Own(Seat),
    TenancyAccess.AtTenant(TenancyPermissions.SeatsManage));
```

which a request says with attributes as well: `[RequiresPermission(TenancyPermissions.SeatsManage)]` and `[OrOwn] SeatId Seat`.

**Parameters**

- `seat`: The seat the request names.

**Type parameters**

- `TSeatId`: The application's seat id.

**Remarks**

The case is the core's, `AccessRequirement.Own<TValue>`, decided by the core's check of who is calling: what the caller's own seat is, Tenancy says with the handle `AddTenancy` registers, from the caller's verified identity and the tenant the request names. So it reads nothing, and it is the same requirement as `AccessRequirement.Own(seat)`.

### `TenancyRequirement`

```csharp
public abstract record TenancyRequirement : AccessRequirement
```

What a request requires of its caller where Tenancy is the one that knows: a seat in the tenant, a permission for the whole of it or at one of its units, or an operator. A request says which case through `IRequireAccess`, spelled with [`TenancyAccess`](#tenancyaccess):

```csharp
public sealed record CloseTheBooks(int Year) : IBillingRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtTenant(BillingPermissions.CloseTheBooks);
}
```

and the check the storage package registers for the module's request interface, with `services.AddTenancyAccess<IBillingRequest, BillingContext>()`, holds the caller to it before the handler runs. A caller that does not meet a case is refused with Tenancy's own codes, whichever module the request is of.

**Remarks**

A closed set: the constructor is private, so the cases nested here are all there are, and each is a record, so two requirements that say the same are equal and a test can hold every request to the one it is meant to declare. What is about who is calling and nothing else is the core's: anyone (`AccessRequirement.AllowAnonymous`) and a signed-in user who need not have a seat yet (`AccessRequirement.SignedIn`). The application's own work is no case: it is a scope trusted code begins around the work, `TenancyUseCases.BeginSystem()` and its kin, and what only it may do the package's use cases refuse everyone else, with `access.system-only`.

A case says the first thing a request requires, the one the request itself can name. What Tenancy's own use cases ask beyond it, such as who may give a role that manages access, or whether a unit may move with what hangs below it, they ask themselves, whoever calls them, and the database's policies ask again: those rules are the package's, and a request does not spell them.

The cases are about the tenant: as a whole, or at one of its units ([`TenancyRequirement.AtUnit<TUnitId>`](#tenancyrequirementatunittunitid)). What is held on one thing an application keeps at a unit is that module's own to declare and to check, with the access questions asked inside its own statements.

### `TenancyRequirement.AtTenant`

```csharp
public sealed record AtTenant : TenancyRequirement
```

A permission is held by the caller for the whole tenant: at its root, now. Otherwise `tenancy.not-permitted`, naming the permission, exactly as the package's own use cases refuse it; nobody is refused with its own reason first. System work in the tenant holds every permission there. A request declares it with [`TenancyAccess.AtTenant`](#attenantstring).

**Remarks**

For what changes or shows something of the tenant as a whole: a use case of the package that asks for the permission first, such as making a role, or the state an application adds to one of the package's aggregates. It costs one statement, on the context the check was registered over.

| Member | Summary |
|---|---|
| `string Permission { get; }` | The permission the request needs for the whole tenant. |

### `TenancyRequirement.AtUnit<TUnitId>`

```csharp
public sealed record AtUnit<TUnitId> : TenancyRequirement
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
```

A permission is held by the caller at a unit: there, or at a unit above it, now. Otherwise `tenancy.not-permitted`, with the permission and the unit, exactly as the package's own use cases refuse it; nobody is refused with its own reason first. System work in the tenant holds every permission at every unit of its tenant. A unit of another tenant, or one that does not exist, is one where nobody holds anything, and is refused the same way. A request declares it with [`TenancyAccess.AtUnit<TUnitId>`](#atunitstring-tunitid).

**Type parameters**

- `TUnitId`: The application's unit id.

**Remarks**

For a command that acts at a unit the request names: one the package's use case decides, such as placing a seat or giving a role there, and one that makes something at a unit, where there is nothing yet a permission could be held on, such as opening a project there: the check a module would otherwise write in its handler, or as a case of its own. The unit is the request's own, so the unit checked is the unit the handler acts at. It costs one statement, on the context the check was registered over, as a permission for the whole tenant does.

```csharp
AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.AtUnit(BillingPermissions.OpenLedger, UnitId);
```

The unit's id is the application's own type, so the case is closed over it: the check registered with `AddTenancyAccess` decides it for the application's unit id, and stops a request that declares it over another type rather than letting it through.

| Member | Summary |
|---|---|
| `string Permission { get; }` | The permission the request needs at the unit. |
| `TUnitId Unit { get; }` | The unit the permission is needed at, from the request. |

### `TenancyRequirement.InTenant`

```csharp
public sealed record InTenant : TenancyRequirement
```

The caller works in the tenant the request was sent for: a seat there, or system work there. Nobody is refused with its own reason, such as `tenancy.not-seated`. A request declares it with [`TenancyAccess.InTenant`](#intenant).

**Remarks**

For a request that answers only what the caller itself holds or may see, where holding nothing is an answer and not a refusal; for one whose handler asks for whatever else it takes in plain sight; and for one handed to a use case of the package whose first rule needs something read, such as the unit an invitation is for, or the parent a unit hangs under now. That use case asks the rest itself.

### `TenancyRequirement.Operator`

```csharp
public sealed record Operator : TenancyRequirement
```

The caller is one of the application's operators: a signed-in user whose token carries one of [`TenancyOptions.OperatorTokenRoles`](use-cases.md#operatortokenroles). Anyone else is refused with `tenancy.operators-only`, a seat and system work included. A request declares it with [`TenancyAccess.RequiresOperator`](#requiresoperator).

**Remarks**

An operator works in no tenant and holds no permission, so nothing is asked about either: the request names the tenant it reads. It is the toolkit's own caller that is asked, never the Tenancy caller, which is nobody for an operator in every tenant. For queries only; an operator changes nothing itself.
