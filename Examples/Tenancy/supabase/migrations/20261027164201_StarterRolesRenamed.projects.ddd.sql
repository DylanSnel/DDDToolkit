-- Exported by DDDToolkit from the Entity Framework migration 20261027164201_StarterRolesRenamed of ProjectsContext.
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
    -- The triggers of the column rules, which stand in the way of a column as a policy does.
    FOR generated IN
        SELECT t.tgname, n.nspname, c.relname
        FROM pg_catalog.pg_trigger t
        JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_catalog.pg_description d ON d.objoid = t.oid AND d.classoid = 'pg_catalog.pg_trigger'::regclass
        WHERE d.description = 'DDDToolkit column rule'
          AND (n.nspname, c.relname) IN (('projects', 'ProjectCrewMembers'), ('projects', 'ProjectCrewRoleGrants'), ('projects', 'ProjectRoles'), ('projects', 'Projects'), ('projects', 'ProjectsOutboxMessages'))
    LOOP
        EXECUTE format('DROP TRIGGER %I ON %I.%I', generated.tgname, generated.nspname, generated.relname);
    END LOOP;
END
$ddd$;

INSERT INTO projects."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261027164201_StarterRolesRenamed', '10.0.12');
