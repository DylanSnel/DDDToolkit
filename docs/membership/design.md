---
title: "Membership: design and limits"
sidebar_label: Design and limits
---

# Membership: design and limits

Why each piece of a resource with members is yours to write, and what the package does not do. Read it when you weigh
Membership, or wonder why a line is needed.

## Why each piece is yours to write

The package knows nothing of your resource but what these lines of
[the document](resources-and-members.md#a-document-and-the-people-it-is-shared-with) say:

| You write | Because |
|---|---|
| The member class, with its row id | Your class and your table, a child entity of your aggregate: the place for what only you know of a member. |
| `NamedRole` in it | A role is held by name where roles are in code, by id where [they are rows](permissions-and-roles.md#roles-a-customer-makes). |
| `[Owner]` on `OwnerId` | The owner is a fact of your resource. The questions, the functions and the mapping read it there, so it is said once. |
| The collection, `Shares` | Members live inside your aggregate, under its one version. |
| Your guard in `ShareWith` | The whole hook: a document that changes no more does not call the member list. |
| The permissions class | Each named once: the request requires it, the rules read it, the functions are written from it. A permission in no class is held by nobody. |
| The roles in the rules | Which roles exist and what each gives is your product's to say. |
| The two registrations | Only you know which context maps the resource and which rules apply; a check is added for one request interface, and a module may have several resources. |
| `MemberAdmission<DocumentId, UserId, NamedRole>` | The use cases are generic over your ids alone, so a module with several resources names each by its own. Unlike [Tenancy's](../tenancy/your-module.md#calling-a-use-case), none is nested in a class to alias. |

## What it does not do

The [overview](overview.md#what-it-does-not-do) lists it. Two consequences:

- **The roles it keeps are a resource's own**, counting on that resource alone. An organization's roles stay in the
  organization: [Tenancy](beside-an-organization.md#with-tenancy), or
  [yours](beside-an-organization.md#an-organization-of-your-own).
- **No access through another resource**: a member is one id. A thing whose access is another resource's asks that
  resource's questions.
