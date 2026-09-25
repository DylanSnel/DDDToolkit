-- Exported by DDDToolkit from the Entity Framework migration 20260925111346_OutboxNextAttemptAt of CatalogContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE catalog."OutboxMessages" ADD "NextAttemptAt" timestamp with time zone;

INSERT INTO catalog."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260925111346_OutboxNextAttemptAt', '10.0.12');
