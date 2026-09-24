-- Exported by DDDToolkit from the Entity Framework migration 20260922201049_CreateOrdering of OrderingContext.
-- Written from that migration; change the migration, not this file.

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ordering') THEN
        CREATE SCHEMA ordering;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS ordering."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ordering') THEN
        CREATE SCHEMA ordering;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ddd') THEN
        CREATE SCHEMA ddd;
    END IF;
END $EF$;

CREATE TABLE ordering."Orders" (
    "Id" uuid NOT NULL,
    "ShipTo_City" text NOT NULL,
    "ShipTo_PostalCode" text NOT NULL,
    "ShipTo_Street" text NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_Orders" PRIMARY KEY ("Id")
);

CREATE TABLE ddd."OutboxMessages" (
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

CREATE TABLE ordering."OrderLine" (
    "Id" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "Sku" text NOT NULL,
    "Quantity" integer NOT NULL,
    CONSTRAINT "PK_OrderLine" PRIMARY KEY ("OrderId", "Id"),
    CONSTRAINT "FK_OrderLine_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES ordering."Orders" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_OutboxMessages_ProcessedAt" ON ddd."OutboxMessages" ("ProcessedAt");

INSERT INTO ordering."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260922201049_CreateOrdering', '10.0.12');
