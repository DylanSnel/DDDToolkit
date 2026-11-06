-- Exported by DDDToolkit from the Entity Framework migration 20261104141500_OutboxLease of InventoryContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE inventory."OutboxMessages" ADD "LockedBy" character varying(128);

ALTER TABLE inventory."OutboxMessages" ADD "LockedUntil" timestamp with time zone;

INSERT INTO inventory."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261104141500_OutboxLease', '10.0.12');
