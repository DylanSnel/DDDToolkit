-- Exported by DDDToolkit from the Entity Framework migration 20261104141445_OutboxLease of CatalogContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE catalog."OutboxMessages" ADD "LockedBy" character varying(128);

ALTER TABLE catalog."OutboxMessages" ADD "LockedUntil" timestamp with time zone;

INSERT INTO catalog."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261104141445_OutboxLease', '10.0.12');
