-- Exported by DDDToolkit from the Entity Framework migration 20260922201056_CreateShipping of ShippingContext.
-- Written from that migration; change the migration, not this file.

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'shipping') THEN
        CREATE SCHEMA shipping;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS shipping."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ddd') THEN
        CREATE SCHEMA ddd;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'shipping') THEN
        CREATE SCHEMA shipping;
    END IF;
END $EF$;

CREATE TABLE ddd."InboxMessages" (
    "MessageId" uuid NOT NULL,
    "Consumer" character varying(256) NOT NULL,
    "MessageName" character varying(256),
    "ProcessedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_InboxMessages" PRIMARY KEY ("MessageId", "Consumer")
);

CREATE TABLE shipping."Shipments" (
    "Id" uuid NOT NULL,
    "Order" uuid NOT NULL,
    "Destination" text NOT NULL,
    "OrderedAt" timestamp with time zone NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_Shipments" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_InboxMessages_ProcessedAt" ON ddd."InboxMessages" ("ProcessedAt");

INSERT INTO shipping."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260922201056_CreateShipping', '10.0.12');
