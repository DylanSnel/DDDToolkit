-- Exported by DDDToolkit from the Entity Framework migration 20260923093144_CreateCatalog of CatalogContext.
-- Written from that migration; change the migration, not this file.

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'catalog') THEN
        CREATE SCHEMA catalog;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS catalog."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'catalog') THEN
        CREATE SCHEMA catalog;
    END IF;
END $EF$;

CREATE TABLE catalog."OutboxMessages" (
    "Id" uuid NOT NULL,
    "EventName" character varying(256) NOT NULL,
    "Payload" text NOT NULL,
    "Version" integer NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "AggregateType" character varying(512),
    "AggregateId" character varying(256),
    "CreatedAt" timestamp with time zone NOT NULL,
    "ProcessedAt" timestamp with time zone,
    "Attempts" integer NOT NULL,
    "LastError" character varying(4000),
    CONSTRAINT "PK_OutboxMessages" PRIMARY KEY ("Id")
);

CREATE TABLE catalog."Products" (
    "Id" uuid NOT NULL,
    "Sku" text NOT NULL,
    "Name" text NOT NULL,
    "Price_Amount" numeric NOT NULL,
    "Price_Currency" text NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_Products" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_OutboxMessages_ProcessedAt" ON catalog."OutboxMessages" ("ProcessedAt");

CREATE UNIQUE INDEX "IX_Products_Sku" ON catalog."Products" ("Sku");

INSERT INTO catalog."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260923093144_CreateCatalog', '10.0.12');
