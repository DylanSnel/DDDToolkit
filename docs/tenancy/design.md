---
title: "Tenancy: design and limits"
sidebar_label: Design and limits
---

# Tenancy: design and limits

This page explains why Tenancy is shaped as it is, what it leaves to you, and how to upgrade from the prereleases
that called a permission a key and a starter role a pack. Read it when you evaluate Tenancy, or when you upgrade.

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
- **Give a module names.** The managers return names by id ([Names](names-and-languages.md#names)).
- **Publish integration events or create tables.** Your module publishes integration events, and your migrations
  create the tables.
- **Link a seat to an e-mail address.** A seat links to a verified identity.
- **Copy a starter role you add later into existing tenants.** A tenant gets the starter roles seeded for its shape
  when it is provisioned or changes shape. The [sync](roles-and-permissions.md#starter-roles-after-provisioning) only updates
  the roles a tenant already has.
- **Send a message about what the sync changed.** The [access history](operators-and-history.md#access-history)
  keeps the sync's event, `tenancy.role-followed-its-starter-role`.

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
   database ([The start-up checks](postgres.md#the-start-up-checks)).

## Upgrading from pack to starter role

Up to 3.2.0-preview.5, the code called a starter role a **pack**. The docs already said starter role. Now the code
says starter role too, in C# and in SQL. Upgrading loses no row:

1. **Rebuild, and follow the compiler.** `RolePack` is now `StarterRole`, `services.SyncRolePacks()` is now
   `services.SyncStarterRoles()`, `RoleManager.FollowPacksAsync` is now `FollowStarterRolesAsync`, and a role's
   `FromPack` is now `FromStarterRole`. The [changelog](../../CHANGELOG.md) lists every renamed name.
2. **Add one migration** to the context that maps Tenancy's tables. Entity Framework writes the two column renames:

   ```csharp
   migrationBuilder.RenameColumn(name: "FromPack", schema: "tenancy", table: "Roles", newName: "FromStarterRole");
   migrationBuilder.RenameColumn(name: "PermissionsFromPack", schema: "tenancy", table: "Roles", newName: "PermissionsFromStarterRole");
   ```

   On Postgres, add the drop of the two old triggers and their functions by hand. They still fire on the renamed
   columns:

   ```csharp
   migrationBuilder.Sql("""DROP TRIGGER IF EXISTS tenancy_role_pack_is_fixed ON "tenancy"."Roles";""");
   migrationBuilder.Sql("""DROP FUNCTION IF EXISTS "tenancy".role_pack_is_fixed();""");
   migrationBuilder.Sql("""DROP TRIGGER IF EXISTS tenancy_role_follows_its_pack ON "tenancy"."Roles";""");
   migrationBuilder.Sql("""DROP FUNCTION IF EXISTS "tenancy".role_follows_its_pack();""");
   ```

   Add the event's new name and the renamed members of two events by hand, so the access history and the outbox read
   older rows. Run the two statements for the event log's table and for the outbox's table:

   ```csharp
   migrationBuilder.Sql("""UPDATE "tenancy"."TenancyEventLog" SET "Payload" = replace("Payload", '"FromPack":', '"FromStarterRole":') WHERE "EventName" = 'tenancy.role-created';""");
   migrationBuilder.Sql("""UPDATE "tenancy"."TenancyEventLog" SET "EventName" = 'tenancy.role-followed-its-starter-role', "Payload" = replace("Payload", '"Pack":', '"StarterRole":') WHERE "EventName" = 'tenancy.role-followed-its-pack';""");
   ```

3. **On Postgres, export the new access file** after that migration. The function `pack_permissions(pack)` becomes
   `starter_role_permissions(starter_role)`. The read function `tenant_roles_with_permissions()` becomes
   `tenant_roles_with_starter_roles()`, which returns `"FromStarterRole"`. The access file drops the old functions. It
   does not drop the old triggers, so the migration in step 2 does.

The sample's migration `StarterRolesRenamed` does all of this.
