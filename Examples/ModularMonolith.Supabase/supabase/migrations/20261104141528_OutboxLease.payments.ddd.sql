-- Exported by DDDToolkit from the Entity Framework migration 20261104141528_OutboxLease of PaymentsContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE payments."OutboxMessages" ADD "LockedBy" character varying(128);

ALTER TABLE payments."OutboxMessages" ADD "LockedUntil" timestamp with time zone;

INSERT INTO payments."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261104141528_OutboxLease', '10.0.12');
