---
title: "Tenancy: the sample"
sidebar_label: Overview
---

# Tenancy: the sample

[`Examples/Tenancy`](../../Examples/Tenancy) is a small application on Tenancy: teams work on projects, and a
project's team is its **crew**. This page: what is in it, who is in its seed, and its parts. The words are in the
[overview](overview.md).

## What is in it

- Three modules: **Tenants** (the application's Tenancy classes, context and migrations), **Projects** (projects and
  their crews; decides who may see and change one) and **Inspections** (inspections on projects; it asks Projects
  through a contract whether the caller may, and reads none of its tables).
- REST routes and one GraphQL schema over the same commands and queries; a Blazor UI calling the API.
- Postgres as Supabase runs it, and no other database, with row level security (RLS): the host logs in as a role that
  owns nothing, and every table forces its policies.
- Tables, policies and roles are files under `Examples/Tenancy/supabase/migrations`, written by
  `Examples.Tenancy.Exporter`. The host refuses to start while one is missing or the database is not set up as the
  policies rely on ([the samples' page](../../Examples/README.md#on-postgres)).

## Its seed

The API seeds two tenants on its first start, with fixed ids:

- **Harbor Works** (`harbor`), hierarchical: the root, Harbor Works; below it the regions North and South; below
  North the areas North Coast and North Inland, below South the area South Bay.
- **Meadow Gardens** (`meadow`), flat: its root is its only unit. A unit below it is refused with
  `tenancy.flat-tenant` until the tenant changes shape.
- Projects: P-001 Pier 7 (North Coast), P-002 Inland depot (North Inland, the only one with planned days), P-003 Bay
  bridge (South Bay), P-004 HQ refit (the root), and meadow's M-001 Garden shed.

Each person shows a rule. An **org role** is granted at a unit; a **crew role** is held on one project's crew.

| Person | Seat | Org role | On a crew | Shows |
|---|---|---|---|---|
| ada | harbor, root | Access admin, Area manager | owns Bay bridge, HQ refit | the first administrator, who also runs the work; she and maud administer, so either may step down, not both |
| rhea | harbor, North | Area manager | owns Inland depot | sees Pier 7 and Inland depot, not Bay bridge (`projects.not-found`); grants Observer and Crew lead at North Coast, not Access admin (`tenancy.grant-exceeds-own`), nothing at South Bay (`tenancy.not-permitted`) |
| leo | harbor, North Coast | none | owns Pier 7, no crew role | renames, closes, reopens and manages its crew by owning it; records no inspection and names no owner (`projects.not-permitted`), gives no org role on the crew (`projects.role-not-for-members`), stays on it with no role (`projects.owner-protected`) |
| juno | harbor, North Coast | none | Surveyor on Pier 7 | records an inspection; is refused a rename |
| vic | harbor, North Inland | Observer, ended yesterday | Observer on Pier 7 | sees Pier 7 through the crew, not Inland depot, which only the expired grant reached |
| seth | harbor, South Bay, suspended | Observer | | every call in harbor is `tenancy.seat-suspended`; `/me/seats` still lists the seat |
| tove | harbor, South Bay; meadow, root | meadow: Tenant admin | Surveyor on Bay bridge | two tenants, switched with the `Tenant` header; meadow's only administrator, so may not revoke her grant, suspend herself or withdraw her placement there (`tenancy.last-admin`) |
| hana | harbor, root | People office | | gives Surveyor and Observer, which she does not hold, and People office; refused Crew lead, Area manager, Access admin for others (`tenancy.grant-exceeds-own`), any access-managing role for herself (`tenancy.self-appointment`) |
| maud | harbor, root | Access admin | | runs access, none of the work: renaming Pier 7 is `projects.not-permitted` until she joins its crew as Crew lead |
| orla | none: an [operator](operators-and-history.md#operators) | | | the application's staff: reads every tenant, changes nothing |

## Who may do what, in the sample

[Who may do what, in the sample](sample/who-may-do-what.md): a module's layers, a request from route to save, what
each request declares, roles and crews, paging, operators, and invitations.

## GraphQL in the sample

[GraphQL in the sample](sample/graphql.md): one schema over the three modules, what a query costs, the rules each
module's schema keeps, and the administration's gateway.

## Try it

[Try it](sample/try-it.md): its UI, one call followed to its refusal, and the presets, each breaking one rule. It
needs Docker; from the repository's root:

```bash
dotnet run --project Examples/Tenancy/Examples.Tenancy.AppHost
```

## Design choices and where to see them

[Design choices and where to see them](sample/design-choices.md): every choice the Tenancy pages describe, with its
code, something to try, and the test that holds it.
