---
title: "What the packages guarantee"
sidebar_label: What the packages guarantee
---

# What the packages guarantee

Tenancy and Membership hold rules about kinds of seat, role sets and places. Each row below is one rule, with the test
class that proves it. The test classes are in the DTS Pro repository, under `Tests/`.

| Test project | What it runs on |
|---|---|
| `DTS.Tenancy.Tests`, `DTS.Membership.Tests` | C#, with the stores in memory |
| `DTS.Tenancy.EntityFramework.Tests`, `DTS.Membership.EntityFramework.Tests` | Entity Framework on SQLite |
| `DTS.Tenancy.Postgres.Tests`, `DTS.Membership.Postgres.Tests` | Postgres in a container, every statement as the caller |
| `DTS.Pro.Analyzers.Tests` | The packages' generators, and the diagnostics they report in the build |
| `DTS.Together.Tests` | Tenancy and Membership joined. A class with an `OnSqlite` and an `OnPostgres` variant runs every claim on both. |
| `Examples.Tenancy.Tests` | The Tenancy sample, on Supabase's Postgres image |

## Kinds of seat

| The packages guarantee | Test class | Runs on |
|---|---|---|
| An application that declares no kinds has one kind, `member`, inside the organization. | `SeatKindCatalogueTests` | C# |
| A catalogue has exactly one default kind, and the default is inside the organization. | `SeatKindCatalogueTests` | C# |
| A seat added without a kind gets the default kind. | `SeatKindManagerTests` | C# |
| A kind the catalogue does not declare is refused, and nothing is added. | `SeatKindManagerTests`, `OutsideSeatTests` | C#, Postgres |
| A seat keeps the kind it was made with. | `SeatKindTests`, `SeatKindStoreTests`, `OutsideSeatTests` | C#, SQLite, Postgres |
| A seat outside the organization is placed at no unit, so it is granted no role. | `SeatKindTests`, `SeatKindManagerTests` | C# |
| No statement places a seat outside the organization, system work included. | `OutsideSeatTests` | Postgres |
| An invitation from outside needs `tenancy.externals.invite` at its unit, and offers no role. | `OutsideInvitationTests`, `OutsideSeatTests` | C#, Postgres |
| Accepting such an invitation makes a seat of its kind, placed at no unit. | `OutsideInvitationTests`, `SeatKindStoreTests` | C#, SQLite |
| An invitation whose issuer no longer holds `tenancy.externals.invite` at the unit is refused on acceptance. | `OutsideInvitationTests` | C# |

## What a seat outside the organization reads

| The packages guarantee | Test class | Runs on |
|---|---|---|
| It reads its own seats and its tenant, and no unit, role, placement or other seat. | `SeatKindManagerTests`, `SeatKindStoreTests`, `OutsideSeatTests` | C#, SQLite, Postgres |
| The managers answer it what the database's policies let it read. | `OutsideSeatTests` | Postgres |
| Tenancy's access queries answer it its own seat alone. | `SeatKindManagerTests` | C# |
| With `NarrowOutsideSeats` off, it reads what any seat reads, and is still placed at no unit. | `SeatKindManagerTests`, `OutsideSeatTests` | C#, Postgres |
| The start-up check refuses a database written with other kinds, or with the setting the other way round. | `OutsideSeatTests` | Postgres |

## The kind filter

| The packages guarantee | Test class | Runs on |
|---|---|---|
| A permission without `Kinds` is held by the kinds inside the organization only. So is a permission that no resource with members declares. | `KindsAndSetsRulesTests`, `KindsOfOtherResourcesTests` | C# |
| A permission of another resource that a role gives has the `Kinds` that resource lists. A seat outside the organization whose role at a place gives such a permission, where its kind is on the list, reaches every resource that hangs there. Both checks read the one list, at the place and at the resource reached from above. | `KindsOfOtherResourcesTests`, `PartnerRulesTests`, `InternshipsReachedThroughTheirPartnerTests` | C#, SQLite, Postgres |
| A role of a set lists no permission of another resource whose `Kinds` leave out a kind of the set. Such a starter role stops the host at start-up, also when the resource that declares the permission is loaded after the rules are made. Such a role a customer makes is refused with `permission-not-for-set`. A role row that lists one anyway gives a member of that kind nothing of it, in C# and on Postgres. | `KindsOfOtherResourcesTests`, `RegistrationTests`, `PartnerRulesTests`, `InternshipsReachedThroughTheirPartnerTests` | C#, SQLite, Postgres |
| Two resources that declare one permission with different `Kinds` are refused for a resource whose role may give it. The start-up check names both lists, and the rules answer no kinds for the permission. | `KindsOfOtherResourcesTests` | C# |
| A group, `SeatKinds.InOrganization` or `SeatKinds.OutsideOrganization`, holds every kind of its side. | `KindsAndSetsRulesTests` | C# |
| A list of kinds that is no list of kinds of seat is refused. | `KindsAndSetsRulesTests`, `MembershipDiagnosticTests` | C#, the build |
| A member holds a permission through a role, or as every member, only when the kind on its row is on the list. | `KindsOfSeatTests`, `SeatKindsOnCoursesTests` | SQLite, Postgres |
| A member whose kind may not hold the permission every member holds does not see the resource. | `KindsOfSeatTests` | SQLite |
| Reach from above gives a permission only to a caller whose own kind may hold it. A seat outside the organization that a place of the application's own answers holds a permission that lists its kind, and sees and holds nothing through a permission that does not. | `InternshipsReachedThroughTheirPartnerTests` | SQLite, Postgres |
| C# reads the caller's kind from tenant selection, so reach from above with the kind check is still one statement. | `InternshipsReachedThroughTheirPartnerTests` | SQLite, Postgres |
| The filter adds no statement: a caller inside and one outside each send one statement of one shape. | `KindsOfSeatTests` | SQLite |
| A member row carries its seat's kind. A row written with another kind is refused. | `MemberManagerKindTests`, `KindGuardTests` | C#, Postgres |
| A member row without a kind breaks the member's own rule, `membership.member-kind-missing`. | `MemberManagerKindTests` | C# |

## Role sets and who manages members

| The packages guarantee | Test class | Runs on |
|---|---|---|
| Rules without sets have one set, for the kinds inside the organization. | `KindsAndSetsRulesTests` | C# |
| A member is in the one set its kind is in. | `KindsAndSetsRulesTests`, `KindsOfSeatTests`, `SeatKindsOnCoursesTests` | C#, SQLite, Postgres |
| A kind is in at most one set. | `KindsAndSetsRulesTests` | C# |
| A kind in two sets through the group of its side is refused at start-up, naming both sets. | `KindsFitTheCatalogueTests` | C# |
| The first set is managed with the permission marked `ManagesMembers`. Every other set names its own permission. | `KindsAndSetsRulesTests`, `MemberManagerKindTests` | C# |
| Adding, taking a role from or removing a member needs the permission of the member's set. | `MemberManagerKindTests` | C# |
| A permission that manages members is held by the kinds its `Kinds` lists. A kind outside the organization that the list names manages the set's members through a role of its set. | `KindsAndSetsRulesTests`, `KindsFitTheCatalogueTests`, `KindsOfSeatTests`, `OutsideMembersManageOnPostgresTests`, `OutsideSeatsManageMembersTests` | C#, SQLite, Postgres |
| Without its kind in that list, a member outside the organization manages no members, whatever its role gives. | `KindsAndSetsRulesTests`, `KindsOfSeatTests`, `OutsideMembersManageOnPostgresTests` | C#, SQLite, Postgres |
| A kind no set takes is not added, with `kind-in-no-set`. | `MemberManagerKindTests` | C# |
| A role goes only to a member of its set, with `role-not-in-set` otherwise. | `MemberManagerKindTests`, `SeatKindsOnCoursesTests` | C#, SQLite, Postgres |
| A role holds only permissions every kind of its set may hold. | `KindsAndSetsRulesTests`, `CustomRoleSetTests` | C# |
| A custom role keeps the set it was made in. A starter role is made in its own set. | `CustomRoleSetTests` | C# |
| A role name is used once across the sets. | `KindsAndSetsRulesTests` | C# |
| Roles kept outside Membership, such as the organization's, take no seat outside the organization. | `SeatKindsOnCoursesTests` | SQLite, Postgres |
| Members that no permission manages are written by the application's own work alone. | `MembersNoPermissionManagesOnPostgresTests` | Postgres |

## The owner

| The packages guarantee | Test class | Runs on |
|---|---|---|
| An owner is a seat inside the organization. Opening or naming an owner outside it is refused. | `MemberManagerKindTests` | C# |
| Opening gives the owner's row the owner's kind. | `MemberManagerKindTests` | C# |
| The owner column takes no seat outside the organization, whatever statement writes it. | `KindGuardTests` | Postgres |

## Fellow members

| The packages guarantee | Test class | Runs on |
|---|---|---|
| A seat outside the organization reads the seats of its fellow members beside its own. | `SeatKindManagerTests`, `OutsideSeatTests`, `SeatKindsOnCoursesTests` | C#, SQLite, Postgres |
| It reads a fellow member's seat without that seat's placements and grants. | `OutsideSeatTests`, `SeatKindsOnCoursesTests` | SQLite, Postgres |
| The fellow members are the members now of the resources the caller is a member of now. | `KindsOfSeatTests` | SQLite |
| With `NarrowOutsideSeats` off, the fellow members are not asked. | `SeatKindManagerTests` | C# |
| `fellow_seats()` is written from the marks. With no mark it reads no seat. | `FellowSeatsScriptTests` | the export |

## Admitting a seat

| The packages guarantee | Test class | Runs on |
|---|---|---|
| The member manager admits a seat by its id, whatever seats the caller reads. While `NarrowOutsideSeats` is on, a seat outside the organization adds a seat it does not read and gives it a role. | `OutsideSeatsManageMembersTests`, `SeatReadRuleTests` | SQLite, Postgres |
| Admitting reads whether the seat is a seat of the caller's tenant, whether it is active, and its kind, and nothing else of it. | `SeatKindManagerTests`, `OutsideSeatsManageMembersTests` | C#, SQLite, Postgres |
| An id no seat has, a seat of another tenant and a seat that is not active get the same refusal, `member-not-active`. | `OutsideSeatsManageMembersTests`, `SeatKindManagerTests` | C#, SQLite, Postgres |
| What the caller reads does not change. After the add the new member is a fellow member, and the caller reads no other seat. | `OutsideSeatsManageMembersTests` | SQLite, Postgres |
| The kind guard reads the one seat through `seat_to_admit(seat)`. A member row for a seat the caller does not read passes, and a row with another kind than its seat's is refused. | `OutsideSeatsManageMembersTests`, `KindGuardTests` | Postgres |
| To a signed-in user `seat_to_admit(seat)` answers one active seat of the caller's tenant. A seat that is not active, a seat of another tenant and an id no seat has give no row alike. It takes one id, so it lists no seats. | `OutsideSeatsManageMembersTests` | Postgres |
| To scoped system work `seat_to_admit(seat)` answers a seat of the work's tenant whatever its status, so the application's own work writes a member row for a suspended seat. | `OutsideSeatsManageMembersTests` | Postgres |
| `seat_to_admit(seat)` runs as its owner with an empty `search_path`. Only signed-in users and the scoped system role may run it. | `OutsideSeatsManageMembersTests`, `ContributionTests` | Postgres |
| A seat outside the organization issues no invitation itself. It is refused with `tenancy.not-permitted`, and on Postgres its own invitation row with `42501`. | `InvitationsIssuedByYourOwnCodeTests` | SQLite, Postgres |
| An invitation issued by scoped system work is accepted without asking its issuer again. It records the seat the work was done for, and a cancelled one is refused. | `InvitationsIssuedByYourOwnCodeTests` | SQLite, Postgres |

## Places

| The packages guarantee | Test class | Runs on |
|---|---|---|
| A resource is reached from above from each of its places that something answers. | `SeveralPlacesTests`, `PlacesOfTwoKindsTests` | SQLite, Postgres |
| A place whose kind nothing answers reaches nothing. | `SeveralPlacesTests` | SQLite |
| What the caller sees from two places is one statement. | `SeveralPlacesTests` | SQLite |
| A permission kept from above is kept from every place. | `SeveralPlacesTests` | SQLite |
| A resource with members of the application's own can be a place of another resource. Its own membership answers it: `WhereIHold` in C#, and its `where_i_hold` function on Postgres, marked once with `[PlacesWhereIHold]`. A seat whose role on it gives a permission holds that permission on what hangs there. C# and the database's read policies give the same answers, and C# asks it in one statement. | `InternshipsReachedThroughTheirPartnerTests`, `InternshipsReachedThroughTheirPartnerInTheDatabaseTests` | SQLite, Postgres |

## C# and Postgres agree

| The packages guarantee | Test class | Runs on |
|---|---|---|
| One table of who holds what on a course gives the same answers on SQLite and on Postgres. | `SeatKindsOnCoursesTests` | SQLite, Postgres |
| The start-up check refuses a database whose functions were written from other rules than the host runs with. | `StartUpCheckTests` in `DTS.Membership.Postgres.Tests` | Postgres |
| The start-up check refuses a database written for other places than C# reaches a resource from. | `StartUpCheckTests` in `DTS.Membership.Postgres.Tests`, `PlacesOfTwoKindsTests` | Postgres |
| The start-up check refuses Tenancy's policies written from another catalogue. | `StartUpCheckTests` in `DTS.Tenancy.Postgres.Tests` | Postgres |
| The join checks the rules against the catalogue the host runs with, when the host starts. | `KindsFitTheCatalogueTests` | C# |
| In the sample, a guest's answers through the application equal the answers of the database's functions. | `GuestScenarios` | Postgres |
