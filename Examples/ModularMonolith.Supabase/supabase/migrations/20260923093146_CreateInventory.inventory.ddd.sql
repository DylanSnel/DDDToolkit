-- Exported by DDDToolkit from the Entity Framework migration 20260923093146_CreateInventory of InventoryContext.
-- Written from that migration; change the migration, not this file.

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'inventory') THEN
        CREATE SCHEMA inventory;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS inventory."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'inventory') THEN
        CREATE SCHEMA inventory;
    END IF;
END $EF$;

CREATE TABLE inventory."InboxMessages" (
    "MessageId" uuid NOT NULL,
    "Consumer" character varying(256) NOT NULL,
    "MessageName" character varying(256),
    "ProcessedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_InboxMessages" PRIMARY KEY ("MessageId", "Consumer")
);

CREATE TABLE inventory."OutboxMessages" (
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

CREATE TABLE inventory."StockItems" (
    "Id" uuid NOT NULL,
    "Sku" text NOT NULL,
    "OnHand" integer NOT NULL,
    "Reserved" integer NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_StockItems" PRIMARY KEY ("Id")
);

CREATE TABLE inventory."StockReservations" (
    "Id" uuid NOT NULL,
    "Order" uuid NOT NULL,
    "Status" integer NOT NULL,
    "Refusal" text,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_StockReservations" PRIMARY KEY ("Id")
);

CREATE TABLE inventory."ReservedLine" (
    "Id" uuid NOT NULL,
    "StockReservationId" uuid NOT NULL,
    "Sku" text NOT NULL,
    "Quantity" integer NOT NULL,
    CONSTRAINT "PK_ReservedLine" PRIMARY KEY ("StockReservationId", "Id"),
    CONSTRAINT "FK_ReservedLine_StockReservations_StockReservationId" FOREIGN KEY ("StockReservationId") REFERENCES inventory."StockReservations" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_InboxMessages_ProcessedAt" ON inventory."InboxMessages" ("ProcessedAt");

CREATE INDEX "IX_OutboxMessages_ProcessedAt" ON inventory."OutboxMessages" ("ProcessedAt");

CREATE UNIQUE INDEX "IX_StockItems_Sku" ON inventory."StockItems" ("Sku");

CREATE UNIQUE INDEX "IX_StockReservations_Order" ON inventory."StockReservations" ("Order");

INSERT INTO inventory."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260923093146_CreateInventory', '10.0.12');
