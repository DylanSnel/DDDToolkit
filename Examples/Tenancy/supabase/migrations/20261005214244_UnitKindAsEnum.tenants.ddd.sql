-- Exported by DDDToolkit from the Entity Framework migration 20261005214244_UnitKindAsEnum of TenantsContext.
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
    -- The triggers of the column rules, which stand in the way of a column as a policy does.
    FOR generated IN
        SELECT t.tgname, n.nspname, c.relname
        FROM pg_catalog.pg_trigger t
        JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_catalog.pg_description d ON d.objoid = t.oid AND d.classoid = 'pg_catalog.pg_trigger'::regclass
        WHERE d.description = 'DDDToolkit column rule'
          AND (n.nspname, c.relname) IN (('tenancy', 'InvitationDigests'), ('tenancy', 'Invitations'), ('tenancy', 'OrganizationUnitPaths'), ('tenancy', 'OrganizationUnits'), ('tenancy', 'Organizations'), ('tenancy', 'Roles'), ('tenancy', 'SeatPlacements'), ('tenancy', 'SeatRights'), ('tenancy', 'SeatRoleGrants'), ('tenancy', 'Seats'), ('tenancy', 'TenancyAccessRevisions'), ('tenancy', 'TenancyEventLog'), ('tenancy', 'TenancyOutboxMessages'), ('tenancy', 'Tenants'))
    LOOP
        EXECUTE format('DROP TRIGGER %I ON %I.%I', generated.tgname, generated.nspname, generated.relname);
    END LOOP;
END
$ddd$;

ALTER TABLE tenancy."OrganizationUnits" ALTER COLUMN "Kind" TYPE character varying(16);
ALTER TABLE tenancy."OrganizationUnits" ALTER COLUMN "Kind" DROP NOT NULL;

INSERT INTO tenancy."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261005214244_UnitKindAsEnum', '10.0.12');
