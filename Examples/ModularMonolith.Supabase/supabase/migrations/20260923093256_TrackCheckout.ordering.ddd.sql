-- Exported by DDDToolkit from the Entity Framework migration 20260923093256_TrackCheckout of OrderingContext.
-- Written from that migration; change the migration, not this file.

ALTER TABLE ddd."OutboxMessages" SET SCHEMA ordering;

ALTER TABLE ordering."Orders" ADD "CancellationReason" text;

ALTER TABLE ordering."Orders" ADD "ConfirmedAt" timestamp with time zone;

ALTER TABLE ordering."Orders" ADD "Paid" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE ordering."Orders" ADD "Status" integer NOT NULL DEFAULT 0;

ALTER TABLE ordering."Orders" ADD "StockReserved" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE ordering."Orders" ADD "Total_Amount" numeric NOT NULL DEFAULT 0.0;

ALTER TABLE ordering."Orders" ADD "Total_Currency" text NOT NULL DEFAULT '';

ALTER TABLE ordering."OrderLine" ADD "UnitPriceAmount" numeric NOT NULL DEFAULT 0.0;

ALTER TABLE ordering."OrderLine" ADD "UnitPriceCurrency" text NOT NULL DEFAULT '';

CREATE TABLE ordering."CatalogPrices" (
    "Sku" text NOT NULL,
    "PricedAt" timestamp with time zone NOT NULL,
    "Price_Amount" numeric NOT NULL,
    "Price_Currency" text NOT NULL,
    CONSTRAINT "PK_CatalogPrices" PRIMARY KEY ("Sku")
);

CREATE TABLE ordering."InboxMessages" (
    "MessageId" uuid NOT NULL,
    "Consumer" character varying(256) NOT NULL,
    "MessageName" character varying(256),
    "ProcessedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_InboxMessages" PRIMARY KEY ("MessageId", "Consumer")
);

CREATE INDEX "IX_InboxMessages_ProcessedAt" ON ordering."InboxMessages" ("ProcessedAt");

INSERT INTO ordering."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260923093256_TrackCheckout', '10.0.12');
