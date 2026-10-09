-- Exported by DDDToolkit from the Entity Framework migration 20261003012507_ProjectRoles of ProjectsContext.
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
          AND (n.nspname, c.relname) IN (('projects', 'ProjectCrewMembers'), ('projects', 'ProjectCrewRoleGrants'), ('projects', 'ProjectRoles'), ('projects', 'Projects'), ('projects', 'ProjectsOutboxMessages'))
    LOOP
        EXECUTE format('DROP POLICY %I ON %I.%I', generated.polname, generated.nspname, generated.relname);
    END LOOP;
END
$ddd$;

ALTER TABLE projects."ProjectCrewRoleGrants" DROP CONSTRAINT "PK_ProjectCrewRoleGrants";

DROP INDEX projects."IX_ProjectCrewRoleGrants_ProjectId_CrewMemberId_RoleId";

ALTER TABLE projects."ProjectCrewRoleGrants" DROP COLUMN "Id";

ALTER TABLE projects."ProjectCrewRoleGrants" ADD CONSTRAINT "PK_ProjectCrewRoleGrants" PRIMARY KEY ("ProjectId", "CrewMemberId", "RoleId");

CREATE TABLE projects."ProjectRoles" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Version" bigint NOT NULL,
    "Name" character varying(120) NOT NULL,
    "Description" character varying(1000) NOT NULL,
    "Keys" text[] NOT NULL,
    "Status" character varying(16) NOT NULL,
    "MadeFrom" character varying(64),
    CONSTRAINT "PK_ProjectRoles" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX "IX_ProjectRoles_TenantId_MadeFrom" ON projects."ProjectRoles" ("TenantId", "MadeFrom");

CREATE UNIQUE INDEX "IX_ProjectRoles_TenantId_Name" ON projects."ProjectRoles" ("TenantId", "Name");

INSERT INTO projects."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261003012507_ProjectRoles', '10.0.12');
