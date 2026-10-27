---
title: "Tenancy: design and limits"
sidebar_label: Design and limits
---

# Tenancy: design and limits

Why Tenancy is shaped as it is, what it leaves to you, and upgrading from the prereleases that called a
permission a key. Read it when you weigh Tenancy, or upgrade.

## Why it is shaped this way

Three choices keep access data small and questions fast
([measurements](../performance.md#tenancy-the-shape-of-the-access-data)):

- **Facts, not outcomes.** Tenancy stores who is placed where with which role, and a row per seat, unit,
  permission and granting role, written with the change. Rights on a project are worked out when asked, so rows
  grow with grants, not projects.
- **Start from the person.** A question starts from what the calling seat holds, so its cost grows with what that
  person may do, not with the tenant.
- **One tree, no copies.** The organization is a closure table (a row per unit and each unit above it), and a
  project stores only its unit, so moving a unit moves everything below it and touches no project.

## What it does not do

- **Decide access to your resources**: their module does, or [Membership](../membership/beside-an-organization.md#with-tenancy) for a
  resource with members.
- **Give a module names**: the directory answers those, by id ([Names](names-and-languages.md#names)).
- **Publish integration events or create tables**: your module's.
- **Link a seat to an e-mail address**: a seat links to a verified identity.
- **Copy a starter role you add later into existing tenants**: a tenant gets those seeded for its shape when
  provisioned or reshaped; the [sync](roles-and-permissions.md#packs-after-provisioning) only updates its roles.
- **Send a message about what the sync changed**: the
  [access history](operators-and-history.md#access-history) keeps its event.

## Upgrading from key to permission

Up to 3.2.0-preview.4 a permission was called a **key**, in the code and the database. Upgrading to
3.2.0-preview.5 loses no row:

1. **Rebuild, and follow the compiler**: `[RequiresKey]` is `[RequiresPermission]`, `TenancyKeys` is
   `TenancyPermissions`, a role's `Keys` its `Permissions`. A refusal's argument `Key` is `Permission`. The
   [changelog](../../CHANGELOG.md) lists every name.
2. **Add one migration** to the context that maps Tenancy's tables, and to each with a custom role class. Entity
   Framework writes renames:

   ```csharp
   migrationBuilder.RenameColumn(name: "Key", schema: "tenancy", table: "SeatRights", newName: "Permission");
   migrationBuilder.RenameIndex(name: "IX_SeatRights_SeatId_Key", schema: "tenancy", table: "SeatRights",
       newName: "IX_SeatRights_SeatId_Permission");
   migrationBuilder.RenameColumn(name: "KeysFromPack", schema: "tenancy", table: "Roles", newName: "PermissionsFromPack");
   migrationBuilder.RenameColumn(name: "Keys", schema: "tenancy", table: "Roles", newName: "Permissions");
   ```

   Add by hand the event's new name, so the access history reads older rows (your schema and table names
   where they differ):

   ```csharp
   migrationBuilder.Sql("""UPDATE "tenancy"."TenancyEventLog" SET "EventName" = 'tenancy.role-permissions-changed' WHERE "EventName" = 'tenancy.role-keys-changed';""");
   migrationBuilder.Sql("""UPDATE "tenancy"."TenancyOutboxMessages" SET "EventName" = 'tenancy.role-permissions-changed' WHERE "EventName" = 'tenancy.role-keys-changed';""");
   ```

3. **On Postgres, export the new access file** after that migration: it renames the functions (`holds_key` is
   `holds_permission`) and drops the old ones. Until it is applied, the start-up checks refuse the database
   ([Setting it up](postgres.md#setting-it-up)).
