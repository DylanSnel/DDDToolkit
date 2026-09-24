-- Exported by DDDToolkit from the Entity Framework migration 20260923095207_TwoDecimalAmounts of PaymentsContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE payments."Payments" ALTER COLUMN "Amount_Amount" TYPE numeric(18,2);

INSERT INTO payments."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260923095207_TwoDecimalAmounts', '10.0.12');
