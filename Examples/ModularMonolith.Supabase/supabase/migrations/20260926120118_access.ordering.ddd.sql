-- Written by DDDToolkit from the row access rules of OrderingContext.
-- Written from those rules; change the rules, not this file. Every file like it says what the
-- rules are now: it drops the policies the one before it made, and makes them again.

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

DO $ddd$
DECLARE
    generated record;
BEGIN
    FOR generated IN
        SELECT p.oid::regprocedure AS signature
        FROM pg_catalog.pg_proc p
        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        JOIN pg_catalog.pg_description d ON d.objoid = p.oid AND d.classoid = 'pg_catalog.pg_proc'::regclass
        WHERE d.description = 'DDDToolkit access function of OrderingContext'
    LOOP
        EXECUTE format('DROP FUNCTION %s', generated.signature);
    END LOOP;
END
$ddd$;

ALTER TABLE ordering."Orders" ENABLE ROW LEVEL SECURITY;

CREATE POLICY "A customer has their orders (read)" ON ordering."Orders" FOR SELECT TO anon, authenticated
    USING (("PlacedBy" IS NULL) OR ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())));
COMMENT ON POLICY "A customer has their orders (read)" ON ordering."Orders" IS 'DDDToolkit row access rule';

CREATE POLICY "A customer has their orders (change)" ON ordering."Orders" FOR UPDATE TO anon, authenticated
    USING (("PlacedBy" IS NULL) OR ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())))
    WITH CHECK (("PlacedBy" IS NULL) OR ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())));
COMMENT ON POLICY "A customer has their orders (change)" ON ordering."Orders" IS 'DDDToolkit row access rule';

CREATE POLICY "Nobody orders for somebody else" ON ordering."Orders" FOR INSERT TO anon, authenticated
    WITH CHECK ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid()));
COMMENT ON POLICY "Nobody orders for somebody else" ON ordering."Orders" IS 'DDDToolkit row access rule';

-- OrderLine belongs to the aggregate, and is seen and changed with it.
ALTER TABLE ordering."OrderLine" ENABLE ROW LEVEL SECURITY;
CREATE POLICY "OrderLine goes with its Orders" ON ordering."OrderLine" FOR ALL TO anon, authenticated
    USING (EXISTS (SELECT 1 FROM ordering."Orders" parent WHERE parent."Id" = "OrderLine"."OrderId"))
    WITH CHECK (EXISTS (SELECT 1 FROM ordering."Orders" parent WHERE parent."Id" = "OrderLine"."OrderId"));
COMMENT ON POLICY "OrderLine goes with its Orders" ON ordering."OrderLine" IS 'DDDToolkit row access rule';
