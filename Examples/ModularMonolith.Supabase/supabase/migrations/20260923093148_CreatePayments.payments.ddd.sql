-- Exported by DDDToolkit from the Entity Framework migration 20260923093148_CreatePayments of PaymentsContext.
-- Written from that migration; change the migration, not this file.

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'payments') THEN
        CREATE SCHEMA payments;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS payments."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'payments') THEN
        CREATE SCHEMA payments;
    END IF;
END $EF$;

CREATE TABLE payments."InboxMessages" (
    "MessageId" uuid NOT NULL,
    "Consumer" character varying(256) NOT NULL,
    "MessageName" character varying(256),
    "ProcessedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_InboxMessages" PRIMARY KEY ("MessageId", "Consumer")
);

CREATE TABLE payments."OutboxMessages" (
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

CREATE TABLE payments."Payments" (
    "Id" uuid NOT NULL,
    "Order" uuid NOT NULL,
    "Status" integer NOT NULL,
    "Refusal" text,
    "Amount_Amount" numeric NOT NULL,
    "Amount_Currency" text NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_Payments" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_InboxMessages_ProcessedAt" ON payments."InboxMessages" ("ProcessedAt");

CREATE INDEX "IX_OutboxMessages_ProcessedAt" ON payments."OutboxMessages" ("ProcessedAt");

CREATE UNIQUE INDEX "IX_Payments_Order" ON payments."Payments" ("Order");

INSERT INTO payments."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260923093148_CreatePayments', '10.0.12');
