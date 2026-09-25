-- Exported by DDDToolkit from the Entity Framework migration 20260925111351_OutboxNextAttemptAt of OrderingContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE ordering."OutboxMessages" ADD "NextAttemptAt" timestamp with time zone;

INSERT INTO ordering."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260925111351_OutboxNextAttemptAt', '10.0.12');
