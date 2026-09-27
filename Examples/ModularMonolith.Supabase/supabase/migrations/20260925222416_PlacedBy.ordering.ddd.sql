-- Exported by DDDToolkit from the Entity Framework migration 20260925222416_PlacedBy of OrderingContext.
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
          AND (n.nspname, c.relname) IN (('ordering', 'CatalogPrices'), ('ordering', 'InboxMessages'), ('ordering', 'OrderLine'), ('ordering', 'Orders'), ('ordering', 'OutboxMessages'))
    LOOP
        EXECUTE format('DROP POLICY %I ON %I.%I', generated.polname, generated.nspname, generated.relname);
    END LOOP;
END
$ddd$;

ALTER TABLE ordering."Orders" ADD "PlacedBy" uuid;

CREATE INDEX "IX_Orders_PlacedBy" ON ordering."Orders" ("PlacedBy");

INSERT INTO ordering."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260925222416_PlacedBy', '10.0.12');
