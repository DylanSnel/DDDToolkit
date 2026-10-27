---
title: "Membership: design and limits"
sidebar_label: Design and limits
---

# Membership: design and limits

Read this page when you weigh Membership, or wonder why a line is needed. It covers why you write each piece of a
resource with members, and what the package does not do.

## Why you write each piece

The package reads only these lines of [the document example](resources-and-members.md#a-document-and-the-people-it-is-shared-with):

| You write | Why |
|---|---|
| The member class, with its row id | The class and its table are yours, a child entity of your aggregate. Put what only you know about a member there. |
| `NamedRole` in the member class | A fixed role is held by its name. A role kept [as a row](permissions-and-roles.md#roles-a-customer-makes) is held by its id. |
| `[Owner]` on `OwnerId` | The owner is a fact of your resource. The questions, the functions and the mapping all read the owner from that property, so you say it once. |
| The collection, `Shares` | The members live inside your aggregate, under its single version. |
| Your guard in `ShareWith` | The guard is the only hook. A document that no longer changes does not call the member list. |
| The permissions class | Each permission is named once. The request requires it, the rules read it, and the functions are written from it. A permission that is in no class is held by nobody. |
| The roles in the rules | Your product decides which roles exist and what each role gives. |
| The two registrations | Only you know which context maps the resource and which rules apply. A check is added for one request interface, and a module may have several resources. |
| `MemberAdmission<DocumentId, UserId, NamedRole>` | The use cases are generic over your ids only, so a module with several resources names each use case by its ids. Unlike [Tenancy's use cases](../tenancy/your-module.md#the-managers-and-the-access-manager), none is nested in a class that you could alias. |

## Limits

The [overview](overview.md#what-it-does-not-do) lists what the package does not do. Two consequences follow:

- **The roles the package keeps belong to one resource** and count on that resource alone. An organization's roles
  stay in the organization: in [Tenancy](beside-an-organization.md#with-tenancy), or in
  [your own organization](beside-an-organization.md#an-organization-of-your-own).
- **There is no access through another resource.** A member is one id. When access to a thing depends on another
  resource, the thing's code calls `IMemberQuestions` of that other resource.
