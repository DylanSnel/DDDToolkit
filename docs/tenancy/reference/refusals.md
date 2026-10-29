---
title: "Tenancy API: Refusals"
sidebar_label: Refusals
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Tenancy API: Refusals

Every refusal code of Tenancy, in the members of `TenancyRefusals`, and the resources that hold their texts.

## Refusal codes

Namespace `DDDToolkit.Supporting.Tenancy`.

### `TenancyRefusals`

```csharp
public static class TenancyRefusals
```

Every way Tenancy refuses a command, as codes a caller branches on, and the one place their kind and English text are written down.

An argument a message shows is a value, such as a permission or an id, never a word: the English and the Dutch texts are whole sentences of their own. Where a client wants to say more, it reads the stable tokens the arguments carry. A refusal of kind `RefusalKind.Invalid` that is about one input names it in the argument `RefusalException.FieldArgument`, in the word the managers call that input by, such as `name` or `until`, so a form puts the text under the input; an edge whose input is called otherwise maps the word. `tenancy.tenant-required` carries none, because the tenant is no input of the command: a request names it beside the command, in a header or in its address.

The Dutch texts have no word for the reader. Dutch makes a writer choose between a familiar and a formal word for "you", and that choice is the application's, so a Dutch sentence says what is the case or what is needed and leaves the word out. An application that wants another tone, or other words, registers a resx of its own in front of this one.

Each code is also the code of the nested invariant that states the same rule after the fact, where a rule has both: the method refuses before anything changes, and the invariant is the net under code that got round the method. `TenancyFailures.resx` holds the same English text, and its Dutch sibling a translation, for `DDDToolkit.Localization` to phrase a refusal in the reader's language:

```csharp
services.AddLocalization();
services.AddDDDToolkitLocalization(options => options.AddResource<TenancyFailures>());
```

| Member | Summary |
|---|---|
| `NotSeated = "tenancy.not-seated"` | The caller has no seat in the tenant it named. |
| `TenantRequired = "tenancy.tenant-required"` | The caller named no tenant. It carries no field: the tenant is named beside the command, not in it. |
| `SeatSuspended = "tenancy.seat-suspended"` | The caller's seat in the tenant is suspended. |
| `TenantInactive = "tenancy.tenant-inactive"` | The tenant the caller named is not active. |
| `NotPermitted = "tenancy.not-permitted"` | The caller does not hold the permission the command needs where it needs it. Arguments: `Permission` and `Unit` (the unit asked about, or `null` for the whole tenant). The message names only the permission: a client that shows the unit finds it among the arguments. |
| `GrantExceedsOwn = "tenancy.grant-exceeds-own"` | A seat would give or take away, at a unit, a role that manages access without holding there each of its permissions that do, for at least as long as the grant runs, or suspend, deactivate or reactivate a seat that holds one; would give itself a role for longer than it holds `tenancy.grants.manage` there; would add or take out a permission that manages access, or archive a role that holds one, without being an administrator; or would, by moving a unit, gain permissions or hold one for longer, or give or take away from anyone a permission that manages access it lacks at the unit for that long. Arguments: `Missing`, the permissions it lacks there or lacks for long enough, and `Role`, the role refused, for all but a move. |
| `SelfAssignment = "tenancy.self-assignment"` | A seat would be placed by itself. |
| `SelfAppointment = "tenancy.self-appointment"` | A seat would give itself a role that manages access. Argument: `Role`. |
| `OtherTenant = "tenancy.other-tenant"` | A change would write a row of another tenant. |
| `StartSystemOnly = "tenancy.start-system-only"` | Only system work chooses when a grant starts. |
| `OperatorsOnly = "tenancy.operators-only"` | Only an operator of the application lists the tenants (`TenantManager.ListAsync`), or sends a request that requires one (`[Operator]`, `TenancyAccess.Operator()`). |
| `TenantMismatch = "tenancy.tenant-mismatch"` | An operator's request names one tenant with its `[ForTenant]` member and another beside it, as tenant selection reads it. It carries no field, since the tenant beside the request is no input of the command. |
| `AddressMismatch = "tenancy.address-mismatch"` | The invitation is for another address than the one the application says the accepting identity has. It names neither address. |
| `UnitNotFound = "tenancy.unit-not-found"` | The unit is not one of the tenant's. |
| `SeatNotFound = "tenancy.seat-not-found"` | The seat is not one of the tenant's, or belongs to another tenant. |
| `RoleNotFound = "tenancy.role-not-found"` | The role is not one of the tenant's, or belongs to another tenant. |
| `PlacementNotFound = "tenancy.placement-not-found"` | The seat has no placement at the unit. |
| `GrantNotFound = "tenancy.grant-not-found"` | The seat was never granted the role at the unit, or it was revoked. |
| `InvitationNotFound = "tenancy.invitation-not-found"` | No invitation has the token, or what was sent is not written as a token is. It is the one answer for both, and for an invitation of another tenant, so it says nothing about which tokens exist. |
| `LastAdmin = "tenancy.last-admin"` | After the change the tenant, which has an administrator now, would have none. |
| `SlugTaken = "tenancy.slug-taken"` | Another tenant has the slug. Argument: `Slug`. |
| `IdentityHasSeat = "tenancy.identity-has-seat"` | The person already has a seat in the tenant. |
| `RoleNameTaken = "tenancy.role-name-taken"` | Another role of the tenant has the name, ignoring case. Argument: `Name`. |
| `TenantState = "tenancy.tenant-state"` | The tenant's status does not allow the change. Arguments: `Status`, the status in lowercase, and `Action`, one of `activate`, `suspend`, `reactivate`, `close` and `change-shape`. |
| `SeatState = "tenancy.seat-state"` | The seat's status does not allow the change. Arguments: `Status`, the status in lowercase, and `Action`, one of `suspend`, `reactivate`, `deactivate`, `place` and `grant`. |
| `UnitNotActive = "tenancy.unit-not-active"` | Something new would go to an archived unit. Argument: `Unit`. |
| `RootImmovable = "tenancy.root-immovable"` | A move names the root. |
| `RootNotArchivable = "tenancy.root-not-archivable"` | An archive names the root. |
| `OneRoot = "tenancy.one-root"` | The organization has no root, or more than one. |
| `Cycle = "tenancy.cycle"` | A move would hang a unit below itself. |
| `SameParent = "tenancy.same-parent"` | A move names the parent the unit has now. |
| `DepthExceeded = "tenancy.depth-exceeded"` | The tree would have more levels than it may. Argument: `Max`. |
| `UnitHasActiveChildren = "tenancy.unit-has-active-children"` | An archive names a unit that still has active children. |
| `UnitArchived = "tenancy.unit-archived"` | A rename, move or archive names an archived unit. |
| `FlatTenant = "tenancy.flat-tenant"` | A unit would be added to a flat tenant, which has its root alone. |
| `ShapeChange = "tenancy.shape-change"` | A shape change other than flat to hierarchical. |
| `DuplicatePlacement = "tenancy.duplicate-placement"` | A second placement of one seat at one unit. |
| `SecondPrimary = "tenancy.second-primary"` | A second primary placement of one seat. |
| `DuplicateGrant = "tenancy.duplicate-grant"` | A second grant of one role at one placement. |
| `RoleNotActive = "tenancy.role-not-active"` | A grant names an archived role. |
| `RoleArchived = "tenancy.role-archived"` | A change names an archived role. |
| `InvitationLapsed = "tenancy.invitation-lapsed"` | The invitation's time ran out before it was accepted. |
| `InvitationUsed = "tenancy.invitation-used"` | The invitation was accepted already, by somebody else. |
| `InvitationCancelled = "tenancy.invitation-cancelled"` | The invitation was cancelled before it was accepted. |
| `InvitationState = "tenancy.invitation-state"` | The invitation's state does not allow the change. Arguments: `State`, the state in lowercase, and `Action`, one of `cancel` and `accept`. |
| `InvitationUnbacked = "tenancy.invitation-unbacked"` | The seat that issued the invitation may no longer give what it offers: it is not active, or it lacks a permission it needed to issue it, or holds one for too short a time. It names neither the seat nor the permissions: the person it is told to is no member of the tenant. |
| `NameInvalid = "tenancy.name-invalid"` | A name is blank, or a name or text is too long. Arguments: `What`, one of `tenant-name`, `unit-name`, `role-name`, `role-description` and `reason`; `Min` and `Max`, the lengths allowed; and `Field`, the input it is about: `name` for the name of a tenant, a unit or a role, `description` or `reason`. A seat has no name in Tenancy, so none is refused here. |
| `InvalidSlug = "tenancy.invalid-slug"` | A slug does not follow the pattern. Argument: `Field`, which is `slug`. |
| `InvalidPeriod = "tenancy.invalid-period"` | A grant would end at or before its start. Argument: `Field`, which is `until`. |
| `ReasonRequired = "tenancy.reason-required"` | A tenant would be suspended or closed without a reason. Argument: `Field`, which is `reason`. |
| `UnknownPermission = "tenancy.unknown-permission"` | Permissions that are new to a role are unknown to the catalogue, or retired. Arguments: `Permissions`, the permissions refused, and `Field`, which is `permissions`. |
| `IdentityRequired = "tenancy.identity-required"` | A seat would have no verified identity. Argument: `Field`, which is `identity`. |
| `PermissionsNotNormalized = "tenancy.permissions-not-normalized"` | A role's permissions are not expanded, or hold duplicates, or are out of order. Argument: `Field`, which is `permissions`. |
| `TooManyIds = "tenancy.too-many-ids"` | A question by id asks about more ids than one question takes. Arguments: `Max`, and `Field`, which is `ids`. |
| `PageSizeInvalid = "tenancy.page-size-invalid"` | A page of the tenants an operator lists would hold no tenant, or more than a page holds. Arguments: `Max`, and `Field`, which is `size`. |
| `CursorInvalid = "tenancy.cursor-invalid"` | The marker a page of the tenants an operator lists continues from is not one `TenantManager.ListAsync` gave. Argument: `Field`, which is `after`. |
| `AddressInvalid = "tenancy.address-invalid"` | An invitation is not for one address a message can be sent to. Arguments: `Max`, the most characters an address has, and `Field`, which is `address`. |
| `InvitationLifetime = "tenancy.invitation-lifetime"` | An invitation would stay open for less or for longer than an invitation may. Arguments: `Min` and `Max`, in minutes, and `Field`, which is `lifetime`. |
| `InvitationGrantEndsFirst = "tenancy.invitation-grant-ends-first"` | The grant an invitation offers would end before the invitation does, so it could be accepted for a role that is already over. Argument: `Field`, which is `grantUntil`. |
| `static IReadOnlyCollection<string> Codes { get; }` | Every code, in the order the table lists them. |
| [`KindOf(string)`](#kindofstring) | The kind of refusal a code is. |
| [`TemplateOf(string)`](#templateofstring) | The English text of a code, as a template: a placeholder such as `{Max}` is filled from the argument of that name. |
| [`Refuse(string, params (string Name, object? Value)[])`](#refusestring-params-string-name-object-value) | The refusal for `code`, with its kind, its English message filled from `arguments`, and the arguments themselves for a translation to use. |

#### `KindOf(string)`

```csharp
public static RefusalKind KindOf(string code)
```

The kind of refusal a code is.

**Parameters**

- `code`: One of the codes above.

**Exceptions**

- `ArgumentException`: The code is not one of Tenancy's.

#### `TemplateOf(string)`

```csharp
public static string TemplateOf(string code)
```

The English text of a code, as a template: a placeholder such as `{Max}` is filled from the argument of that name.

**Parameters**

- `code`: One of the codes above.

**Exceptions**

- `ArgumentException`: The code is not one of Tenancy's.

#### `Refuse(string, params (string Name, object? Value)[])`

```csharp
public static RefusalException Refuse(string code, params (string Name, object? Value)[] arguments)
```

The refusal for `code`, with its kind, its English message filled from `arguments`, and the arguments themselves for a translation to use. A code that is always about the same input also carries that input's name, as the argument `RefusalException.FieldArgument`, unless `arguments` name one themselves.

**Parameters**

- `code`: One of the codes above.
- `arguments`: The values the message names, by name.

**Exceptions**

- `ArgumentException`: The code is not one of Tenancy's: a refusal nobody can look up is a bug.

## Texts

Namespace `DDDToolkit.Supporting.Tenancy`.

### `TenancyFailures`

```csharp
public sealed class TenancyFailures
```

The marker type `TenancyFailures.resx` and `TenancyFailures.nl.resx` are named after: the English and Dutch text of every `tenancy.*` code in [`TenancyRefusals`](#tenancyrefusals). Add it to the localizer so refusals reach the reader in their language:

```csharp
services.AddLocalization();
services.AddDDDToolkitLocalization(options => options.AddResource<TenancyFailures>());
```

A language the package does not ship is a resx of your own with the same keys, added before this one.

The Dutch texts never use a word for the reader, neither the familiar one nor the formal one: which of the two fits is the application's to say. Another tone, or another word for a tenant or a seat, is a resx of your own as well, with the permissions you want to say differently, in every language you support, added before this one.
