-- Exported by DDDToolkit from the Entity Framework migration 20261001215456_Initial of InspectionsContext.
-- Written from that migration; change the migration, not this file.

-- This module's row access rules come off before its schema changes, which a policy could stand in
-- the way of, and the file of row access rules after this one makes them again.
DO $ddd$
DECLARE
    generated record;
BEGIN
    FOR generated IN
        SELECT p.polname, n.nspname, c.relname
        FROM pg_catalog.pg_policy p
        JOIN pg_catalog.pg_class c ON c.oid = p.polrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_catalog.pg_description d ON d.objoid = p.oid AND d.classoid = 'pg_catalog.pg_policy'::regclass
        WHERE d.description = 'DDDToolkit row access rule'
          AND (n.nspname, c.relname) IN (('inspections', 'Inspections'), ('inspections', 'InspectionsOutboxMessages'))
    LOOP
        EXECUTE format('DROP POLICY %I ON %I.%I', generated.polname, generated.nspname, generated.relname);
    END LOOP;
END
$ddd$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'inspections') THEN
        CREATE SCHEMA inspections;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS inspections."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'inspections') THEN
        CREATE SCHEMA inspections;
    END IF;
END $EF$;

CREATE TABLE inspections."Inspections" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "Title" character varying(200) NOT NULL,
    "RecordedBy" uuid NOT NULL,
    "RecordedAt" timestamp with time zone NOT NULL,
    "ChangedByIdentity" uuid,
    "ChangedByKind" character varying(32) NOT NULL,
    "ChangedBySeat" uuid,
    "CreatedByIdentity" uuid,
    "CreatedByKind" character varying(32) NOT NULL,
    "CreatedBySeat" uuid,
    "DaysFrom" date NOT NULL,
    "DaysUntil" date NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_Inspections" PRIMARY KEY ("Id")
);

CREATE TABLE inspections."InspectionsOutboxMessages" (
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
    "NextAttemptAt" timestamp with time zone,
    "LastError" character varying(4000),
    CONSTRAINT "PK_InspectionsOutboxMessages" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_Inspections_ProjectId" ON inspections."Inspections" ("ProjectId");

CREATE INDEX "IX_InspectionsOutboxMessages_ProcessedAt" ON inspections."InspectionsOutboxMessages" ("ProcessedAt");

INSERT INTO inspections."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001215456_Initial', '10.0.12');
