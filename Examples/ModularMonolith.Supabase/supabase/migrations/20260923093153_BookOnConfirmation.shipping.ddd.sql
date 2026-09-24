-- Exported by DDDToolkit from the Entity Framework migration 20260923093153_BookOnConfirmation of ShippingContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE ddd."InboxMessages" SET SCHEMA shipping;

ALTER TABLE shipping."Shipments" RENAME COLUMN "OrderedAt" TO "ConfirmedAt";

INSERT INTO shipping."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260923093153_BookOnConfirmation', '10.0.12');
