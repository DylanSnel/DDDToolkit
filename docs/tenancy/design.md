---
title: "Tenancy: design and limits"
sidebar_label: Design and limits
---

# Tenancy: design and limits

This page explains why Tenancy is shaped as it is, what it leaves to you, and how to upgrade from the prereleases
that called a permission a key. Read it when you evaluate Tenancy, or when you upgrade.

## Why Tenancy is shaped this way

Three choices keep the access data small and the checks fast
([measurements](../performance.md#tenancy-the-shape-of-the-access-data)):

- **Tenancy stores facts and computes rights on resources.** Tenancy stores who is placed where with which role. In
  the same save it writes the rights, one row per seat, unit, permission and granting role. A check works out the
  rights on a project when the check runs. So the number of rows grows with the grants, and adding projects adds no
  rows.
- **A check starts from the caller.** A check starts from what the calling seat holds. So its cost grows with what
  that person may do. A larger tenant does not make the check slower.
- **The organization is one tree, stored once.** The organization is a closure table, with one row per unit and
  each unit above it. A project stores only its unit. So moving a unit moves everything below it, and no project
  row changes.

## What Tenancy does not do

- **Decide access to your resources.** The resource's module decides, or
  [Membership](../membership/beside-an-organization.md#with-tenancy) does for a resource with members.
- **Give a module names.** The directory returns names by id ([Names](names-and-languages.md#names)).
- **Publish integration events or create tables.** Your module publishes integration events, and your migrations
  create the tables.
- **Link a seat to an e-mail address.** A seat links to a verified identity.
- **Copy a starter role you add later into existing tenants.** A tenant gets the starter roles seeded for its shape
  when it is provisioned or changes shape. The [sync](roles-and-permissions.md#packs-after-provisioning) only updates
  the roles a tenant already has.
- **Send a message about what the sync changed.** The [access history](operators-and-history.md#access-history)
  keeps the sync's event, `tenancy.role-followed-its-pack`.

## Upgrading from key to permission

Up to 3.2.0-preview.4, a permission was called a **key**, in the code and in the database. Upgrading to
3.2.0-preview.5 loses no row:

1. **Rebuild, and follow the compiler.** `[RequiresKey]` is now `[RequiresPermission]`, `TenancyKeys` is now
   `TenancyPermissions`, and a role's `Keys` are now its `Permissions`. A refusal's argument `Key` is now
   `Permission`. The [changelog](../../CHANGELOG.md) lists every renamed name.
2. **Add one migration** to the context that maps Tenancy's tables, and to each context with a custom role class.
   Entity Framework writes the renames:

   ```csharp
   migrationBuilder.RenameColumn(name: "Key", schema: "tenancy", table: "SeatRights", newName: "Permission");
   migrationBuilder.RenameIndex(name: "IX_SeatRights_SeatId_Key", schema: "tenancy", table: "SeatRights",
       newName: "IX_SeatRights_SeatId_Permission");
   migrationBuilder.RenameColumn(name: "KeysFromPack", schema: "tenancy", table: "Roles", newName: "PermissionsFromPack");
   migrationBuilder.RenameColumn(name: "Keys", schema: "tenancy", table: "Roles", newName: "Permissions");
   ```

   Add the event's new name by hand, so the access history reads older rows. Use your schema and table names where
   they differ:

   ```csharp
   migrationBuilder.Sql("""UPDATE "tenancy"."TenancyEventLog" SET "EventName" = 'tenancy.role-permissions-changed' WHERE "EventName" = 'tenancy.role-keys-changed';""");
   migrationBuilder.Sql("""UPDATE "tenancy"."TenancyOutboxMessages" SET "EventName" = 'tenancy.role-permissions-changed' WHERE "EventName" = 'tenancy.role-keys-changed';""");
   ```

3. **On Postgres, export the new access file** after that migration. The file renames the functions (`holds_key`
   becomes `holds_permission`) and drops the old ones. Until you apply the file, the start-up checks refuse the
   database ([Setting it up](postgres.md#setting-it-up)).
