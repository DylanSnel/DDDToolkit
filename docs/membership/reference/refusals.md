---
title: "Membership API: Refusals"
sidebar_label: Refusals
---

<!-- Generated from the packages' code and XML documentation by build/ApiReference; do not edit by hand. Regenerate with build/generate-api-reference.sh. -->

# Membership API: Refusals

Every rule Membership refuses with, in the members of `MembershipRefusals`; the codes one resource refuses them under, `MembershipCodes`; and the resource that holds their texts.

## Refusal codes

Namespace `DDDToolkit.Supporting.Membership`.

### `MembershipRefusals`

```csharp
public static class MembershipRefusals
```

Every way Membership refuses, named once, with its kind and its English text: the package's own word for each rule.

A caller is never refused with one of these as it stands. Members belong to a resource of the application, and an application may have several kinds of resource with members, each with its own codes: a refusal carries the code that resource's [`MembershipCodes`](#membershipcodes) give the rule, `documents.already-member` for the rule named [`AlreadyMember`](#membershiprefusals) here. The names below are what a code map is written over, what the entries of `MembershipFailures.resx` and its Dutch sibling are called, and what the one rule a member checks by itself reports ([`RoleHeld`](#membershiprefusals)), since a member does not know whose it is. A role kept for a resource reports the two rules it checks by itself the same way ([`RoleNameInvalid`](#membershiprefusals), [`RolePermissionsNotNormalized`](#membershiprefusals)).

An argument a message shows is a value, such as a permission or an id, never a word: the English and the Dutch texts are whole sentences of their own. A refusal of kind `RefusalKind.Invalid` names the input it is about in the argument `RefusalException.FieldArgument`, in the word the member list calls that input by, `role` or `until`, or the word a role calls it by, `name`, `description` or `permissions`, so a form puts the text under the input; an edge whose input is called otherwise maps the word.

Where a rule below says its argument is `Member`, a resource with a word of its own for a member carries it under that word ([`MembershipCodes.WithMemberArgument`](#withmemberargumentstring)).

The texts say "member" and "role" and never what the resource is: a resource that wants its own words, "staff" for its members say, registers a resource file of its own for its own codes in front of the package's. The Dutch texts have no word for the reader: which of the familiar and the formal one fits is the application's to say.

| Member | Summary |
|---|---|
| `NotFound = "membership.not-found"` | The resource is not one the caller may see: it does not exist, or is out of the caller's reach. One answer for both, so a refusal never tells anyone a resource exists. |
| `NotPermitted = "membership.not-permitted"` | The caller may see the resource but does not hold the permission the request needs on it. Argument: `Permission`. |
| `MemberNotActive = "membership.member-not-active"` | The one to be made a member, or owner, is not active, or is not known where members come from. Argument: `Member`. |
| `RoleNotForMembers = "membership.role-not-for-members"` | The role is not one that goes to a member of this resource: its rules do not know it, or what decides which roles go on a member list rules it out. Arguments: `Role`, and `Field`, which is `role`. |
| `AlreadyMember = "membership.already-member"` | The member has a membership already, now or from a later start: a member is on the list once, and holds its roles there. A membership that has ended is not in the way. Argument: `Member`. |
| `MemberNotFound = "membership.member-not-found"` | The one named is not a member, or its membership has ended, which a role would not bring back. Argument: `Member`. |
| `RoleHeld = "membership.role-held"` | The member holds the role already, now or from a later start. Arguments: `Member`, `Role`. |
| `RoleNotHeld = "membership.role-not-held"` | The member does not hold the role. Arguments: `Member`, `Role`. |
| `OwnerProtected = "membership.owner-protected"` | The change would take the owner off the member list, or give the owner a role: the owner stays a member, with no end, and holds what it holds by owning the resource, not through a role. |
| `AlreadyOwner = "membership.already-owner"` | The member named owner is the owner already. |
| `InvalidPeriod = "membership.invalid-period"` | A membership, or a role, would end at or before its start. Argument: `Field`, which is `until`. |
| `RoleIsArchived = "membership.role-archived"` | The role is archived: it gives nothing, and it is not renamed, given permissions or archived again. |
| `RoleNameInvalid = "membership.role-name-invalid"` | A role's name is blank or too long, or its description is too long. Arguments: `Min`, `Max`, and `Field`, which is `name` or `description`. |
| `PermissionNotForMembers = "membership.permission-not-for-members"` | A role would give a permission the resource's rules do not let a member's role give. Arguments: `Permissions`, the permissions refused, and `Field`, which is `permissions`. |
| `RolePermissionsNotNormalized = "membership.role-permissions-not-normalized"` | A role's permissions are not distinct, not blank and in ordinal order: what a role checks of itself after the fact. |
| `static IReadOnlyCollection<string> Codes { get; }` | Every name, in the order the table lists them. |
| [`KindOf(string)`](#kindofstring) | The kind of refusal a rule is. |
| [`TemplateOf(string)`](#templateofstring) | The English text of a rule, as a template: a placeholder such as `{Permission}` is filled from the argument of that name. |

#### `KindOf(string)`

```csharp
public static RefusalKind KindOf(string code)
```

The kind of refusal a rule is.

**Parameters**

- `code`: One of the names above.

**Exceptions**

- `ArgumentException`: The name is not one of Membership's.

#### `TemplateOf(string)`

```csharp
public static string TemplateOf(string code)
```

The English text of a rule, as a template: a placeholder such as `{Permission}` is filled from the argument of that name.

**Parameters**

- `code`: One of the names above.

**Exceptions**

- `ArgumentException`: The name is not one of Membership's.

## A resource's codes

Namespace `DDDToolkit.Supporting.Membership`.

### `MembershipCodes`

```csharp
public sealed class MembershipCodes
```

The codes one resource refuses with: for each rule [`MembershipRefusals`](#membershiprefusals) names, the code a caller of that resource branches on.

```csharp
public static MembershipCodes Membership { get; } = MembershipCodes.Under("documents");
```

gives `documents.not-found`, `documents.already-member` and so on: the rule's own name under the resource's prefix. A resource that calls a rule otherwise says so:

```csharp
MembershipCodes.Under("folders").With(MembershipRefusals.AlreadyMember, "already-on-folder");
```

And a resource that has a word of its own for a member has a client read the member from an argument of that name, `Staff` instead of `Member`:

```csharp
MembershipCodes.Under("folders").WithMemberArgument("Staff");
```

A resource has its own codes because an application may have several kinds of resource with members, and a client should read from a code which one refused it. The member list of a resource refuses with them ([`MemberList<TMember, TId, TMemberId, TRoleId>`](resource-and-members.md#memberlisttmember-tid-tmemberid-troleid)), and so do the access questions and the admission of that resource, which read them from its rules. Declare them once, where the aggregate can reach them, and hand the same instance to both: a static property of the aggregate, which the member list the package's generator writes there is made with, and which the rules take as their `codes`.

The kind and the English text of a refusal are the package's whatever its code. For the reader's language the package ships its texts in English and Dutch, and whatever registers the resource offers them under this resource's codes ([`TextKeys`](#membershipcodes)), so an application that localizes its failures adds no line for them. A resource file of the application's own for these codes, in its own words, is asked first.

It never changes: [`With`](#withstring-string) and [`WithMemberArgument`](#withmemberargumentstring) answer another map.

| Member | Summary |
|---|---|
| `DefaultMemberArgument = "Member"` | The name of the argument a refusal carries the member in, unless the resource has a word of its own for it ([`WithMemberArgument`](#withmemberargumentstring)): `Member`. |
| `string Prefix { get; }` | What every code of the resource starts with. |
| `string MemberArgument { get; }` | The name of the argument a refusal of this resource carries the member in: [`DefaultMemberArgument`](#membershipcodes) unless the resource has a word of its own ([`WithMemberArgument`](#withmemberargumentstring)). |
| `IReadOnlyList<string> All { get; }` | Every code of the resource, one per rule, in the order [`MembershipRefusals.Codes`](#membershiprefusals) lists the rules. |
| `IReadOnlyDictionary<string, string> TextKeys { get; }` | For each code a failure about this resource's members carries, the entry of `MembershipFailures.resx` that holds its text: the resource's own codes, and the names of the rules a member, or a role, checks by itself, which it reports as they stand. |
| [`this[string]`](#thisstring) | The code this resource gives the rule `code`. |
| [`Under(string)`](#understring) | The codes of a resource whose codes start with `prefix`: each rule's own name, under that prefix. |
| [`With(string, string)`](#withstring-string) | The same codes, but for the rule `code`, which this resource calls `suffix`: for a resource whose codes were there before, or that has a word of its own for its members. |
| [`WithMemberArgument(string)`](#withmemberargumentstring) | The same codes, for a resource whose refusals carry the member in an argument called `name` instead of [`DefaultMemberArgument`](#membershipcodes): a resource that has a word of its own for a member, or whose clients read that argument already. |
| [`Refuse(string, params (string Name, object? Value)[])`](#refusestring-params-string-name-object-value) | The refusal for the rule `code`, under this resource's code for it, with the rule's kind, its English message filled from `arguments`, and the arguments themselves for a translation to use. |

#### `this[string]`

```csharp
pub[string code]public string this { get; }
```

The code this resource gives the rule `code`.

**Parameters**

- `code`: One of the constants of [`MembershipRefusals`](#membershiprefusals).

**Exceptions**

- `ArgumentException`: `code` is not one of Membership's.

#### `Under(string)`

```csharp
public static MembershipCodes Under(string prefix)
```

The codes of a resource whose codes start with `prefix`: each rule's own name, under that prefix.

**Parameters**

- `prefix`: What the resource's codes start with, usually the name of its module: lower case letters, digits and dashes, in parts joined by dots, each part starting with a letter, such as `documents`.

**Exceptions**

- `ArgumentException`: `prefix` is not a prefix a code can have.

#### `With(string, string)`

```csharp
public MembershipCodes With(string code, string suffix)
```

The same codes, but for the rule `code`, which this resource calls `suffix`: for a resource whose codes were there before, or that has a word of its own for its members.

**Parameters**

- `code`: One of the constants of [`MembershipRefusals`](#membershiprefusals).
- `suffix`: What comes after the prefix: lower case letters, digits and dashes, starting with a letter.

**Exceptions**

- `ArgumentException`: `code` is not one of Membership's, `suffix` is not one a code can end in, or another rule of the resource has that code already: two rules a client could not tell apart.

#### `WithMemberArgument(string)`

```csharp
public MembershipCodes WithMemberArgument(string name)
```

The same codes, for a resource whose refusals carry the member in an argument called `name` instead of [`DefaultMemberArgument`](#membershipcodes): a resource that has a word of its own for a member, or whose clients read that argument already. What a client reads from a refusal is its code and its arguments, so a resource's own words go for both.

Every refusal of this resource then carries the member under that name, whoever makes it: the member list, the admission, the two checks of the whole list. The one rule a member checks by itself keeps [`DefaultMemberArgument`](#membershipcodes), as it keeps the package's name for the rule: a member does not know whose it is.

**Parameters**

- `name`: The argument's name: letters and digits, starting with a letter, such as `Staff`.

**Exceptions**

- `ArgumentException`: `name` cannot name an argument, or is the name of another argument a refusal of Membership carries: two values under one name.

#### `Refuse(string, params (string Name, object? Value)[])`

```csharp
public RefusalException Refuse(string code, params (string Name, object? Value)[] arguments)
```

The refusal for the rule `code`, under this resource's code for it, with the rule's kind, its English message filled from `arguments`, and the arguments themselves for a translation to use. A rule that is about one input also carries that input's name, as the argument `RefusalException.FieldArgument`, unless `arguments` name one themselves. The member, given as [`DefaultMemberArgument`](#membershipcodes), is carried as this resource's [`MemberArgument`](#membershipcodes).

**Parameters**

- `code`: One of the constants of [`MembershipRefusals`](#membershiprefusals).
- `arguments`: The values the message names, by name.

**Exceptions**

- `ArgumentException`: `code` is not one of Membership's: a refusal nobody can look up is a bug.

## Texts

Namespace `DDDToolkit.Supporting.Membership`.

### `MembershipFailures`

```csharp
public sealed class MembershipFailures
```

The marker type `MembershipFailures.resx` and `MembershipFailures.nl.resx` are named after: the English and Dutch text of every rule [`MembershipRefusals`](#membershiprefusals) names, each under that name.

A resource refuses under its own codes ([`MembershipCodes`](#membershipcodes)), so its texts are read under those, once for each resource that has members. An application writes nothing for that: whatever registers a resource offers these texts under that resource's codes, to the localizer of an application that phrases its failures in the reader's language (`AddDDDToolkitLocalization`).

The texts say "member" and "role", never what the resource or its members are called. A resource with words of its own, or a language the package does not ship, is a resx of the application's with that resource's codes as its permissions: the application's own texts are asked before the package's.

The Dutch texts never use a word for the reader, neither the familiar one nor the formal one: which of the two fits is the application's to say.
