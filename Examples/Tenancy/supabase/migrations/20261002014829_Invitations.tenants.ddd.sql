-- Exported by DDDToolkit from the Entity Framework migration 20261002014829_Invitations of TenantsContext.
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
          AND (n.nspname, c.relname) IN (('tenancy', 'InvitationDigests'), ('tenancy', 'Invitations'), ('tenancy', 'OrganizationUnitPaths'), ('tenancy', 'OrganizationUnits'), ('tenancy', 'Organizations'), ('tenancy', 'Roles'), ('tenancy', 'SeatPlacements'), ('tenancy', 'SeatRights'), ('tenancy', 'SeatRoleGrants'), ('tenancy', 'Seats'), ('tenancy', 'TenancyAccessRevisions'), ('tenancy', 'TenancyEventLog'), ('tenancy', 'TenancyOutboxMessages'), ('tenancy', 'Tenants'))
    LOOP
        EXECUTE format('DROP POLICY %I ON %I.%I', generated.polname, generated.nspname, generated.relname);
    END LOOP;
END
$ddd$;

CREATE TABLE tenancy."Invitations" (
    "Id" uuid NOT NULL,
    "Version" bigint NOT NULL,
    "TenantId" uuid NOT NULL,
    "Address" character varying(254),
    "UnitId" uuid NOT NULL,
    "RoleId" uuid NOT NULL,
    "GrantUntil" timestamp with time zone,
    "DisplayName" character varying(200),
    "State" character varying(32) NOT NULL,
    "IssuedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "IssuedBy" uuid,
    "IssuedAsSystem" boolean NOT NULL,
    "AcceptedAt" timestamp with time zone,
    "AcceptedAs" uuid,
    "ClosedAt" timestamp with time zone,
    CONSTRAINT "PK_Invitations" PRIMARY KEY ("Id")
);

CREATE TABLE tenancy."InvitationDigests" (
    "InvitationId" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Digest" bytea NOT NULL,
    CONSTRAINT "PK_InvitationDigests" PRIMARY KEY ("InvitationId"),
    CONSTRAINT "FK_InvitationDigests_Invitations_InvitationId" FOREIGN KEY ("InvitationId") REFERENCES tenancy."Invitations" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_InvitationDigests_Digest" ON tenancy."InvitationDigests" ("Digest");

CREATE INDEX "IX_InvitationDigests_TenantId" ON tenancy."InvitationDigests" ("TenantId");

CREATE INDEX "IX_Invitations_TenantId_State_ExpiresAt" ON tenancy."Invitations" ("TenantId", "State", "ExpiresAt");

INSERT INTO tenancy."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261002014829_Invitations', '10.0.12');
