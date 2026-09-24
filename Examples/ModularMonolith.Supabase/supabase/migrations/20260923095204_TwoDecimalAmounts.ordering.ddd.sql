-- Exported by DDDToolkit from the Entity Framework migration 20260923095204_TwoDecimalAmounts of OrderingContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE ordering."Orders" ALTER COLUMN "Total_Amount" TYPE numeric(18,2);

ALTER TABLE ordering."OrderLine" ALTER COLUMN "UnitPriceAmount" TYPE numeric(18,2);

ALTER TABLE ordering."CatalogPrices" ALTER COLUMN "Price_Amount" TYPE numeric(18,2);

INSERT INTO ordering."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260923095204_TwoDecimalAmounts', '10.0.12');
