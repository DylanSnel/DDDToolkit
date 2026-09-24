-- Exported by DDDToolkit from the Entity Framework migration 20260923095202_TwoDecimalAmounts of CatalogContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE catalog."Products" ALTER COLUMN "Price_Amount" TYPE numeric(18,2);

INSERT INTO catalog."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260923095202_TwoDecimalAmounts', '10.0.12');
