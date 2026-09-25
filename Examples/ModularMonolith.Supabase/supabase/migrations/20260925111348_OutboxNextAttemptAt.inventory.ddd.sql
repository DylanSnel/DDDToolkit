-- Exported by DDDToolkit from the Entity Framework migration 20260925111348_OutboxNextAttemptAt of InventoryContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE inventory."OutboxMessages" ADD "NextAttemptAt" timestamp with time zone;

INSERT INTO inventory."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260925111348_OutboxNextAttemptAt', '10.0.12');
