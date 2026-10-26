-- Exported by DDDToolkit from the Entity Framework migration 20261001215453_Initial of ProjectsContext.
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
          AND (n.nspname, c.relname) IN (('projects', 'ProjectCrewMembers'), ('projects', 'ProjectCrewRoleGrants'), ('projects', 'Projects'), ('projects', 'ProjectsOutboxMessages'))
    LOOP
        EXECUTE format('DROP POLICY %I ON %I.%I', generated.polname, generated.nspname, generated.relname);
    END LOOP;
END
$ddd$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'projects') THEN
        CREATE SCHEMA projects;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS projects."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'projects') THEN
        CREATE SCHEMA projects;
    END IF;
END $EF$;

CREATE TABLE projects."Projects" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Number" character varying(40) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "UnitId" uuid NOT NULL,
    "State" character varying(16) NOT NULL,
    "OwnerSeatId" uuid NOT NULL,
    "ChangedByIdentity" uuid,
    "ChangedByKind" character varying(32) NOT NULL,
    "ChangedBySeat" uuid,
    "CreatedByIdentity" uuid,
    "CreatedByKind" character varying(32) NOT NULL,
    "CreatedBySeat" uuid,
    "PlannedFrom" date,
    "PlannedUntil" date,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_Projects" PRIMARY KEY ("Id")
);

CREATE TABLE projects."ProjectsOutboxMessages" (
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
    CONSTRAINT "PK_ProjectsOutboxMessages" PRIMARY KEY ("Id")
);

CREATE TABLE projects."ProjectCrewMembers" (
    "Id" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "SeatId" uuid NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone,
    "AddedBy" uuid,
    CONSTRAINT "PK_ProjectCrewMembers" PRIMARY KEY ("ProjectId", "Id"),
    CONSTRAINT "FK_ProjectCrewMembers_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES projects."Projects" ("Id") ON DELETE CASCADE
);

CREATE TABLE projects."ProjectCrewRoleGrants" (
    "Id" uuid NOT NULL,
    "ProjectId" uuid NOT NULL,
    "CrewMemberId" uuid NOT NULL,
    "RoleId" uuid NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone,
    "GivenBy" uuid,
    CONSTRAINT "PK_ProjectCrewRoleGrants" PRIMARY KEY ("ProjectId", "CrewMemberId", "Id"),
    CONSTRAINT "FK_ProjectCrewRoleGrants_ProjectCrewMembers_ProjectId_CrewMemb~" FOREIGN KEY ("ProjectId", "CrewMemberId") REFERENCES projects."ProjectCrewMembers" ("ProjectId", "Id") ON DELETE CASCADE
);

CREATE INDEX "IX_ProjectCrewMembers_SeatId" ON projects."ProjectCrewMembers" ("SeatId");

CREATE UNIQUE INDEX "IX_ProjectCrewRoleGrants_ProjectId_CrewMemberId_RoleId" ON projects."ProjectCrewRoleGrants" ("ProjectId", "CrewMemberId", "RoleId");

CREATE UNIQUE INDEX "IX_Projects_TenantId_Number" ON projects."Projects" ("TenantId", "Number");

CREATE INDEX "IX_Projects_UnitId" ON projects."Projects" ("UnitId");

CREATE INDEX "IX_ProjectsOutboxMessages_ProcessedAt" ON projects."ProjectsOutboxMessages" ("ProcessedAt");

INSERT INTO projects."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001215453_Initial', '10.0.12');
