-- Exported by DDDToolkit from the Entity Framework migration 20261001215449_Initial of TenantsContext.
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
          AND (n.nspname, c.relname) IN (('tenancy', 'OrganizationUnitPaths'), ('tenancy', 'OrganizationUnits'), ('tenancy', 'Organizations'), ('tenancy', 'Roles'), ('tenancy', 'SeatPlacements'), ('tenancy', 'SeatRights'), ('tenancy', 'SeatRoleGrants'), ('tenancy', 'Seats'), ('tenancy', 'TenancyAccessRevisions'), ('tenancy', 'TenancyEventLog'), ('tenancy', 'TenancyOutboxMessages'), ('tenancy', 'Tenants'))
    LOOP
        EXECUTE format('DROP POLICY %I ON %I.%I', generated.polname, generated.nspname, generated.relname);
    END LOOP;
END
$ddd$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'tenancy') THEN
        CREATE SCHEMA tenancy;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS tenancy."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'tenancy') THEN
        CREATE SCHEMA tenancy;
    END IF;
END $EF$;

CREATE TABLE tenancy."Organizations" (
    "Id" uuid NOT NULL,
    "Version" bigint NOT NULL,
    "Name" character varying(200) NOT NULL,
    CONSTRAINT "PK_Organizations" PRIMARY KEY ("Id")
);

CREATE TABLE tenancy."OrganizationUnitPaths" (
    "AncestorId" uuid NOT NULL,
    "DescendantId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Distance" integer NOT NULL,
    CONSTRAINT "PK_OrganizationUnitPaths" PRIMARY KEY ("AncestorId", "DescendantId")
);

CREATE TABLE tenancy."Roles" (
    "Id" uuid NOT NULL,
    "Use" character varying(16) NOT NULL,
    "NormalizedName" character varying(120) NOT NULL,
    "Version" bigint NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" character varying(120) NOT NULL,
    "Description" character varying(1000) NOT NULL,
    "FromPack" character varying(64),
    "Status" character varying(32) NOT NULL,
    "Keys" text[] NOT NULL,
    CONSTRAINT "PK_Roles" PRIMARY KEY ("Id")
);

CREATE TABLE tenancy."SeatRights" (
    "SeatId" uuid NOT NULL,
    "UnitId" uuid NOT NULL,
    "RoleId" uuid NOT NULL,
    "Key" character varying(128) NOT NULL,
    "TenantId" uuid NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone,
    CONSTRAINT "PK_SeatRights" PRIMARY KEY ("SeatId", "UnitId", "RoleId", "Key")
);

CREATE TABLE tenancy."Seats" (
    "Id" uuid NOT NULL,
    "JobTitle" text,
    "Version" bigint NOT NULL,
    "TenantId" uuid NOT NULL,
    "Identity" uuid NOT NULL,
    "DisplayName" character varying(200) NOT NULL,
    "Status" character varying(32) NOT NULL,
    CONSTRAINT "PK_Seats" PRIMARY KEY ("Id")
);

CREATE TABLE tenancy."TenancyAccessRevisions" (
    "TenantId" uuid NOT NULL,
    "Revision" bigint NOT NULL,
    CONSTRAINT "PK_TenancyAccessRevisions" PRIMARY KEY ("TenantId")
);

CREATE TABLE tenancy."TenancyEventLog" (
    "Id" uuid NOT NULL,
    "EventName" character varying(256) NOT NULL,
    "Version" integer NOT NULL,
    "Payload" text NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    "RecordedAt" timestamp with time zone NOT NULL,
    "AggregateType" character varying(512),
    "AggregateId" character varying(256),
    "ActedByKind" character varying(64) NOT NULL,
    "ActedById" character varying(256),
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_TenancyEventLog" PRIMARY KEY ("Id")
);

CREATE TABLE tenancy."TenancyOutboxMessages" (
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
    CONSTRAINT "PK_TenancyOutboxMessages" PRIMARY KEY ("Id")
);

CREATE TABLE tenancy."Tenants" (
    "Id" uuid NOT NULL,
    "IsDemo" boolean NOT NULL,
    "Version" bigint NOT NULL,
    "Slug" character varying(63) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "Shape" character varying(32) NOT NULL,
    "StatusReason" character varying(500),
    CONSTRAINT "PK_Tenants" PRIMARY KEY ("Id")
);

CREATE TABLE tenancy."OrganizationUnits" (
    "Id" uuid NOT NULL,
    "CostCentre" text,
    "TenantId" uuid NOT NULL,
    "ParentId" uuid,
    "Name" character varying(200) NOT NULL,
    "Kind" character varying(64) NOT NULL,
    "Status" character varying(32) NOT NULL,
    CONSTRAINT "PK_OrganizationUnits" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_OrganizationUnits_Organizations_TenantId" FOREIGN KEY ("TenantId") REFERENCES tenancy."Organizations" ("Id") ON DELETE CASCADE
);

CREATE TABLE tenancy."SeatPlacements" (
    "UnitId" uuid NOT NULL,
    "SeatId" uuid NOT NULL,
    "IsPrimary" boolean NOT NULL,
    "PlacedAt" timestamp with time zone NOT NULL,
    "PlacedBy" uuid,
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_SeatPlacements" PRIMARY KEY ("SeatId", "UnitId"),
    CONSTRAINT "FK_SeatPlacements_Seats_SeatId" FOREIGN KEY ("SeatId") REFERENCES tenancy."Seats" ("Id") ON DELETE CASCADE
);

CREATE TABLE tenancy."SeatRoleGrants" (
    "RoleId" uuid NOT NULL,
    "SeatId" uuid NOT NULL,
    "UnitId" uuid NOT NULL,
    "StartsAt" timestamp with time zone NOT NULL,
    "EndsAt" timestamp with time zone,
    "GrantedBy" uuid,
    "Reason" character varying(500),
    CONSTRAINT "PK_SeatRoleGrants" PRIMARY KEY ("SeatId", "UnitId", "RoleId"),
    CONSTRAINT "FK_SeatRoleGrants_SeatPlacements_SeatId_UnitId" FOREIGN KEY ("SeatId", "UnitId") REFERENCES tenancy."SeatPlacements" ("SeatId", "UnitId") ON DELETE CASCADE
);

CREATE INDEX "IX_OrganizationUnitPaths_DescendantId" ON tenancy."OrganizationUnitPaths" ("DescendantId");

CREATE INDEX "IX_OrganizationUnitPaths_TenantId" ON tenancy."OrganizationUnitPaths" ("TenantId");

CREATE INDEX "IX_OrganizationUnits_ParentId" ON tenancy."OrganizationUnits" ("ParentId");

CREATE INDEX "IX_OrganizationUnits_TenantId" ON tenancy."OrganizationUnits" ("TenantId");

CREATE UNIQUE INDEX "IX_OrganizationUnits_TenantId_WhereRoot" ON tenancy."OrganizationUnits" ("TenantId") WHERE "ParentId" IS NULL;

CREATE UNIQUE INDEX "IX_Roles_TenantId_NormalizedName" ON tenancy."Roles" ("TenantId", "NormalizedName");

CREATE UNIQUE INDEX "IX_SeatPlacements_SeatId_WherePrimary" ON tenancy."SeatPlacements" ("SeatId") WHERE "IsPrimary";

CREATE INDEX "IX_SeatRights_RoleId" ON tenancy."SeatRights" ("RoleId");

CREATE INDEX "IX_SeatRights_SeatId_Key" ON tenancy."SeatRights" ("SeatId", "Key");

CREATE INDEX "IX_SeatRights_TenantId" ON tenancy."SeatRights" ("TenantId");

CREATE INDEX "IX_SeatRoleGrants_RoleId" ON tenancy."SeatRoleGrants" ("RoleId");

CREATE UNIQUE INDEX "IX_Seats_Identity_TenantId" ON tenancy."Seats" ("Identity", "TenantId");

CREATE INDEX "IX_Seats_TenantId" ON tenancy."Seats" ("TenantId");

CREATE INDEX "IX_TenancyEventLog_RecordedAt" ON tenancy."TenancyEventLog" ("RecordedAt");

CREATE INDEX "IX_TenancyEventLog_TenantId_RecordedAt" ON tenancy."TenancyEventLog" ("TenantId", "RecordedAt");

CREATE INDEX "IX_TenancyOutboxMessages_ProcessedAt" ON tenancy."TenancyOutboxMessages" ("ProcessedAt");

CREATE UNIQUE INDEX "IX_Tenants_Slug" ON tenancy."Tenants" ("Slug");

INSERT INTO tenancy."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261001215449_Initial', '10.0.12');
