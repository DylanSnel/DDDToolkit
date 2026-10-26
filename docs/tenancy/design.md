---
title: "Tenancy: design and limits"
sidebar_label: Design and limits
---

# Tenancy: design and limits

Why Tenancy stores what it stores, what it leaves to your application, and how to upgrade from the prereleases that
called a permission a key. Read it when you weigh Tenancy for an application, or upgrade one. The words are on the
[overview](overview.md).

## Why it is shaped this way

Three choices keep the access data small and the questions fast. [Performance](../performance.md#tenancy-the-shape-of-the-access-data)
has the measurements behind them.

- **Facts, not outcomes.**
  - Tenancy stores who is placed where and holds which role.
  - It does not store what that adds up to per person per project, and neither does the sample's Projects module:
    rights on a project are worked out when asked for, from the team and the permissions of each role.
  - For the organization it also keeps the permissions a seat holds at each unit: one row per seat, unit, permission
    and the role that grants it, with that grant's period, written by the same transaction that makes the change.
  - So the rows grow with the grants, never with the number of projects.
- **Start from the person.** A question starts from what the current seat holds and works outward, so its cost
  grows with what that person may do, not with the size of the tenant.
- **One tree, no copies.**
  - The organization is a closure table on ids.
  - A project stores only the unit it belongs to, never a copy of that unit's path.
  - So moving a unit moves everything under it, and no project is touched.

## What it does not do

- **Decide access to your resources.** The module that owns a resource does, with Tenancy's answers. For a resource
  with members, [Membership](../membership.md#with-tenancy) asks them for you: a member is a seat, and a permission
  given at a unit counts for what sits there and below.
- **Give a module names.** The read model carries access facts; what a seat, a unit or a role is called is asked of
  the directory, by id ([Names](names-and-languages.md#names)).
- **Publish integration events or create tables.** Those are your module's.
- **Link a seat to a person by e-mail address.** A seat is linked to a verified identity.
- **Copy a pack you add later into existing tenants.** A pack is a starter role: a role every new tenant gets a copy
  of.
  - A tenant gets the packs seeded for its shape when it is provisioned, and those seeded for a new shape when it
    changes shape.
  - The [sync](roles-and-permissions.md#packs-after-provisioning) brings the roles a tenant has up to their packs,
    and makes none.
- **Send a message about what the sync changed.** It raises the event, and the
  [access history](operators-and-history.md#access-history) keeps it.

## Upgrading from key to permission

Up to 3.2.0-preview.4 a permission was called a **key**, in the packages' names and in the database. From
3.2.0-preview.5 each is a permission:

| Up to 3.2.0-preview.4 | From 3.2.0-preview.5 |
|---|---|
| Columns `"Key"`, `"Keys"`, `"KeysFromPack"` | `"Permission"`, `"Permissions"`, `"PermissionsFromPack"` |
| Functions `holds_key`, `units_where_i_hold` and their kin | `holds_permission`, `units_where_i_hold_permission` and their kin |
| The refusals' argument `Key` | `Permission` |

Nothing is dropped on the way: the columns are renamed, and every row keeps its value. An application that used the
earlier names does three things:

1. **Rebuild, and follow the compiler.** `[RequiresKey]` is `[RequiresPermission]`, `TenancyKeys` is
   `TenancyPermissions`, a role's `Keys` are its `Permissions`, and so on. The [changelog](../../CHANGELOG.md) lists
   each name, was and now.
2. **Add one migration** to the context that maps Tenancy's tables, and to each context with a custom role class.
   Entity Framework writes it as renames, never as a drop and an add:

   ```csharp
   migrationBuilder.RenameColumn(name: "Key", schema: "tenancy", table: "SeatRights", newName: "Permission");
   migrationBuilder.RenameIndex(name: "IX_SeatRights_SeatId_Key", schema: "tenancy", table: "SeatRights",
       newName: "IX_SeatRights_SeatId_Permission");
   migrationBuilder.RenameColumn(name: "KeysFromPack", schema: "tenancy", table: "Roles", newName: "PermissionsFromPack");
   migrationBuilder.RenameColumn(name: "Keys", schema: "tenancy", table: "Roles", newName: "Permissions");
   ```

   - The event a role's permissions changing raises is stored under its new name,
     `tenancy.role-permissions-changed`.
   - Add the rows written under the old name to the same migration, so the
     [access history](operators-and-history.md#access-history) still reads them:

   ```csharp
   migrationBuilder.Sql("""UPDATE "tenancy"."TenancyEventLog" SET "EventName" = 'tenancy.role-permissions-changed' WHERE "EventName" = 'tenancy.role-keys-changed';""");
   migrationBuilder.Sql("""UPDATE "tenancy"."TenancyOutboxMessages" SET "EventName" = 'tenancy.role-permissions-changed' WHERE "EventName" = 'tenancy.role-keys-changed';""");
   ```

   - Use your own schema and table names where you named them otherwise.
   - The sample's migration is `PermissionsRenamed`, in each module that has these tables.
3. **On Postgres, let the export write the new access file** after that migration.
   - It writes the functions under their new names, and drops the old ones.
   - Until it is applied, the start-up checks refuse the database ([Setting it up](postgres.md#setting-it-up)).
