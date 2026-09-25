-- Exported by DDDToolkit from the Entity Framework migration 20260925111355_OutboxNextAttemptAt of PaymentsContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE payments."OutboxMessages" ADD "NextAttemptAt" timestamp with time zone;

INSERT INTO payments."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260925111355_OutboxNextAttemptAt', '10.0.12');
