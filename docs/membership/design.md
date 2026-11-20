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
| `[Owner]` on `OwnerId` | The owner is a fact of your resource. The access manager, the functions and the mapping all read the owner from that property, so you say it once. |
| The collection, `Shares` | The members live inside your aggregate, under its single version. |
| Your guard in `OnChangingMembers` | The resource keeps its own rules. The member service calls the hook before every change, so an archived document refuses each one in one place. |
| The permissions class | Each permission is named once. The request requires it, the rules read it, and the functions are written from it. A permission that is in no class is held by nobody. |
| The roles in the rules | Your product decides which roles exist and what each role gives. |
| The two registrations | Only you know which context maps the resource and which rules apply. A check is added for one request interface, and a module may have several resources. |
| `DocumentShareService` in the handler | The generator writes the member service closed over the document and its ids, and names it after the member class. A module can have several resources with members, so each has its own service, and each access manager names its resource: `MembershipAccessManager<Document>`. |
| `ManagesMembers` and `NamesOwner` on `Share` | Your product decides which permission changes the members. The member service requires it, and the database's lock checks the same one. |

## Why the services change the members

- **One way in.** Every change of the members goes through the member service, so each change checks the caller, the
  member and the role in the same order, whichever handler makes it.
- **The resource stays yours.** Its fields, its constructor and its rules stay in your class. The hooks carry your
  guard and your events into each change.
- **The check is asked once.** A request whose check asked the same permission on the same resource hands its answer
  to the service through the request in hand. A service called elsewhere asks the access manager itself.
- **A change to the caller's own place saves.** Where the database checks every row, a member that gives up its own
  place loses the right that the save needs. The service saves that change as the resource's own work, only for the
  request whose check read the resource.

## Limits

The [overview](overview.md#what-it-does-not-do) lists what the package does not do. These consequences follow:

- **The roles the package keeps belong to one resource** and count on that resource alone. An organization's roles
  stay in the organization: in [Tenancy](beside-an-organization.md#with-tenancy), or in
  [your own organization](beside-an-organization.md#an-organization-of-your-own).
- **There is no access through another resource.** A member is one id. When access to a thing depends on another
  resource, the thing's code asks the access manager of that other resource, such as
  `MembershipAccessManager<Folder>`.
- **On Postgres one marked function answers a kind of place of your own for every resource at it.** A crate and a
  pallet in bays share one function. Two resources at the same kind of place cannot be answered by two functions. In
  C# each resource has its own `IPlacesReached` ([A resource at several places](beside-an-organization.md#a-resource-at-several-places)).
- **`Via` says `Above`, not which place.** A resource reached from two places says the same for both. An application
  that shows the place asks its own question.
- **The check of a role against its set compares the entries as they are written.** Membership does not know which side
  of the organization a kind is on. So a set of `[employee]` with a permission whose `Kinds` is
  `[SeatKinds.InOrganization]` is refused, although `employee` is inside. List the group in the set instead. The check
  never lets a role give what a kind of its set may not hold.
- **A kind in two sets through a group is found only when the host starts.** A set of `[SeatKinds.OutsideOrganization]`
  and a set of `[guest]` overlap only if `guest` is outside, which the catalogue says. The rules refuse the same entry
  in two sets when they are made. The join's start-up check refuses the rest
  ([Kinds of seat and role sets](permissions-and-roles.md#kinds-of-seat-and-role-sets)).
