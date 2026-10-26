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
    -- The triggers of the column rules, which stand in the way of a column as a policy does.
    FOR generated IN
        SELECT t.tgname, n.nspname, c.relname
        FROM pg_catalog.pg_trigger t
        JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_catalog.pg_description d ON d.objoid = t.oid AND d.classoid = 'pg_catalog.pg_trigger'::regclass
        WHERE d.description = 'DDDToolkit column rule'
          AND (n.nspname, c.relname) IN (('ordering', 'CatalogPrices'), ('ordering', 'InboxMessages'), ('ordering', 'OrderLine'), ('ordering', 'Orders'), ('ordering', 'OutboxMessages'))
    LOOP
        EXECUTE format('DROP TRIGGER %I ON %I.%I', generated.tgname, generated.nspname, generated.relname);
    END LOOP;
END
$ddd$;

-- What the policies below ask: the ddd schema, and the function that tells a row written by the running
-- transaction, savepoints included, from one written before it began. With them, the procedure that sets a
-- caller for one transaction, which only the role the application logs in as may call. Each is made, replaced
-- or granted only when it is missing or differs, so a role that does not own them may run this as well.
DO $ddd$
DECLARE
    body constant text := $function$
    SELECT CASE WHEN written.ahead >= 2147483648 THEN false
                ELSE coalesce(pg_catalog.pg_xact_status((written.top + written.ahead)::pg_catalog.text::pg_catalog.xid8) = 'in progress', false)
           END
    FROM (SELECT current.id AS top, (row_xmin::pg_catalog.text::bigint - (current.id & 4294967295)) & 4294967295 AS ahead
          FROM (SELECT pg_catalog.pg_current_xact_id()::pg_catalog.text::bigint AS id) current) written
$function$;
    use_caller constant text := $procedure$
BEGIN
    PERFORM pg_catalog.set_config('role', role_name, true);
    PERFORM pg_catalog.set_config('request.jwt.claims', claims, true);
    PERFORM pg_catalog.set_config('request.jwt.claim.sub', '', true);
    PERFORM pg_catalog.set_config('request.jwt.claim.role', '', true);
    PERFORM pg_catalog.set_config('request.jwt.claim.email', '', true);
    PERFORM pg_catalog.set_config('request.jwt.claim', '', true);
    FOR i IN 1 .. coalesce(pg_catalog.array_length(setting_names, 1), 0) LOOP
        PERFORM pg_catalog.set_config(setting_names[i], setting_values[i], true);
    END LOOP;
END
$procedure$;
BEGIN
    IF pg_catalog.to_regnamespace('ddd') IS NULL THEN
        CREATE SCHEMA ddd;
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_proc
                   WHERE oid = pg_catalog.to_regprocedure('ddd.written_in_this_transaction(xid)') AND prosrc = body) THEN
        EXECUTE 'CREATE OR REPLACE FUNCTION ddd.written_in_this_transaction(row_xmin xid) RETURNS boolean LANGUAGE sql STABLE SET search_path = '''' AS ' || pg_catalog.quote_literal(body);
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_proc
                   WHERE oid = pg_catalog.to_regprocedure('ddd.use_caller(text, text, text[], text[])') AND prosrc = use_caller) THEN
        EXECUTE 'CREATE OR REPLACE PROCEDURE ddd.use_caller(role_name text, claims text, setting_names text[], setting_values text[]) LANGUAGE plpgsql AS ' || pg_catalog.quote_literal(use_caller);
    END IF;
    IF pg_catalog.has_function_privilege('public', 'ddd.use_caller(text, text, text[], text[])', 'EXECUTE') THEN
        REVOKE ALL ON PROCEDURE ddd.use_caller(text, text, text[], text[]) FROM PUBLIC;
    END IF;
    IF NOT coalesce(pg_catalog.has_schema_privilege(pg_catalog.to_regrole('anon'), 'ddd', 'USAGE'), false) THEN
        GRANT USAGE ON SCHEMA ddd TO anon;
    END IF;
    IF NOT coalesce(pg_catalog.has_function_privilege(pg_catalog.to_regrole('anon'), 'ddd.written_in_this_transaction(xid)', 'EXECUTE'), false) THEN
        GRANT EXECUTE ON FUNCTION ddd.written_in_this_transaction(xid) TO anon;
    END IF;
    IF NOT coalesce(pg_catalog.has_schema_privilege(pg_catalog.to_regrole('authenticated'), 'ddd', 'USAGE'), false) THEN
        GRANT USAGE ON SCHEMA ddd TO authenticated;
    END IF;
    IF NOT coalesce(pg_catalog.has_function_privilege(pg_catalog.to_regrole('authenticated'), 'ddd.written_in_this_transaction(xid)', 'EXECUTE'), false) THEN
        GRANT EXECUTE ON FUNCTION ddd.written_in_this_transaction(xid) TO authenticated;
    END IF;
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
          -- A function a policy still asks, of a module whose file comes after this one, stays until the next file.
          AND NOT EXISTS (SELECT FROM pg_catalog.pg_depend dependent
                          WHERE dependent.refclassid = 'pg_catalog.pg_proc'::regclass AND dependent.refobjid = p.oid AND dependent.deptype = 'n')
    LOOP
        EXECUTE format('DROP FUNCTION %s', generated.signature);
    END LOOP;
END
$ddd$;

ALTER TABLE ordering."Orders" ENABLE ROW LEVEL SECURITY;
ALTER TABLE ordering."Orders" FORCE ROW LEVEL SECURITY;

CREATE POLICY "A customer has their orders (select) for anon" ON ordering."Orders" FOR SELECT TO anon
    USING (("PlacedBy" IS NULL) OR ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())));
COMMENT ON POLICY "A customer has their orders (select) for anon" ON ordering."Orders" IS 'DDDToolkit row access rule';

CREATE POLICY "A customer has their orders (select) for authenticated" ON ordering."Orders" FOR SELECT TO authenticated
    USING (("PlacedBy" IS NULL) OR ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())));
COMMENT ON POLICY "A customer has their orders (select) for authenticated" ON ordering."Orders" IS 'DDDToolkit row access rule';

CREATE POLICY "Nobody orders for somebody else (insert) for anon" ON ordering."Orders" FOR INSERT TO anon
    WITH CHECK ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid()));
COMMENT ON POLICY "Nobody orders for somebody else (insert) for anon" ON ordering."Orders" IS 'DDDToolkit row access rule';

CREATE POLICY "Nobody orders for somebody else (insert) for authenticated" ON ordering."Orders" FOR INSERT TO authenticated
    WITH CHECK ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid()));
COMMENT ON POLICY "Nobody orders for somebody else (insert) for authenticated" ON ordering."Orders" IS 'DDDToolkit row access rule';

CREATE POLICY "A customer has their orders (update) for anon" ON ordering."Orders" FOR UPDATE TO anon
    USING (("PlacedBy" IS NULL) OR ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())))
    WITH CHECK (("PlacedBy" IS NULL) OR ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())));
COMMENT ON POLICY "A customer has their orders (update) for anon" ON ordering."Orders" IS 'DDDToolkit row access rule';

CREATE POLICY "A customer has their orders (update) for authenticated" ON ordering."Orders" FOR UPDATE TO authenticated
    USING (("PlacedBy" IS NULL) OR ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())))
    WITH CHECK (("PlacedBy" IS NULL) OR ("PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())));
COMMENT ON POLICY "A customer has their orders (update) for authenticated" ON ordering."Orders" IS 'DDDToolkit row access rule';

-- OrderLine belongs to the aggregate: it is read with its Orders, and written as the rules let a caller write its Orders.
ALTER TABLE ordering."OrderLine" ENABLE ROW LEVEL SECURITY;
ALTER TABLE ordering."OrderLine" FORCE ROW LEVEL SECURITY;
CREATE POLICY "OrderLine (select) for anon" ON ordering."OrderLine" FOR SELECT TO anon
    USING (EXISTS (SELECT 1 FROM ordering."Orders" parent WHERE parent."Id" = ordering."OrderLine"."OrderId"));
COMMENT ON POLICY "OrderLine (select) for anon" ON ordering."OrderLine" IS 'DDDToolkit row access rule';
CREATE POLICY "OrderLine (select) for authenticated" ON ordering."OrderLine" FOR SELECT TO authenticated
    USING (EXISTS (SELECT 1 FROM ordering."Orders" parent WHERE parent."Id" = ordering."OrderLine"."OrderId"));
COMMENT ON POLICY "OrderLine (select) for authenticated" ON ordering."OrderLine" IS 'DDDToolkit row access rule';
CREATE POLICY "OrderLine (insert) for anon" ON ordering."OrderLine" FOR INSERT TO anon
    WITH CHECK (EXISTS (SELECT 1 FROM ordering."Orders" r WHERE r."Id" = ordering."OrderLine"."OrderId" AND (((r."PlacedBy" IS NULL) OR (r."PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid()))) OR ((r."PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())) AND ddd.written_in_this_transaction(r.xmin)))));
COMMENT ON POLICY "OrderLine (insert) for anon" ON ordering."OrderLine" IS 'DDDToolkit row access rule';
CREATE POLICY "OrderLine (insert) for authenticated" ON ordering."OrderLine" FOR INSERT TO authenticated
    WITH CHECK (EXISTS (SELECT 1 FROM ordering."Orders" r WHERE r."Id" = ordering."OrderLine"."OrderId" AND (((r."PlacedBy" IS NULL) OR (r."PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid()))) OR ((r."PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())) AND ddd.written_in_this_transaction(r.xmin)))));
COMMENT ON POLICY "OrderLine (insert) for authenticated" ON ordering."OrderLine" IS 'DDDToolkit row access rule';
CREATE POLICY "OrderLine (update) for anon" ON ordering."OrderLine" FOR UPDATE TO anon
    USING (EXISTS (SELECT 1 FROM ordering."Orders" r WHERE r."Id" = ordering."OrderLine"."OrderId" AND ((r."PlacedBy" IS NULL) OR (r."PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())))))
    WITH CHECK (EXISTS (SELECT 1 FROM ordering."Orders" r WHERE r."Id" = ordering."OrderLine"."OrderId" AND ((r."PlacedBy" IS NULL) OR (r."PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())))));
COMMENT ON POLICY "OrderLine (update) for anon" ON ordering."OrderLine" IS 'DDDToolkit row access rule';
CREATE POLICY "OrderLine (update) for authenticated" ON ordering."OrderLine" FOR UPDATE TO authenticated
    USING (EXISTS (SELECT 1 FROM ordering."Orders" r WHERE r."Id" = ordering."OrderLine"."OrderId" AND ((r."PlacedBy" IS NULL) OR (r."PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())))))
    WITH CHECK (EXISTS (SELECT 1 FROM ordering."Orders" r WHERE r."Id" = ordering."OrderLine"."OrderId" AND ((r."PlacedBy" IS NULL) OR (r."PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())))));
COMMENT ON POLICY "OrderLine (update) for authenticated" ON ordering."OrderLine" IS 'DDDToolkit row access rule';
CREATE POLICY "OrderLine (delete) for anon" ON ordering."OrderLine" FOR DELETE TO anon
    USING (EXISTS (SELECT 1 FROM ordering."Orders" r WHERE r."Id" = ordering."OrderLine"."OrderId" AND ((r."PlacedBy" IS NULL) OR (r."PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())))));
COMMENT ON POLICY "OrderLine (delete) for anon" ON ordering."OrderLine" IS 'DDDToolkit row access rule';
CREATE POLICY "OrderLine (delete) for authenticated" ON ordering."OrderLine" FOR DELETE TO authenticated
    USING (EXISTS (SELECT 1 FROM ordering."Orders" r WHERE r."Id" = ordering."OrderLine"."OrderId" AND ((r."PlacedBy" IS NULL) OR (r."PlacedBy" IS NOT DISTINCT FROM (SELECT auth.uid())))));
COMMENT ON POLICY "OrderLine (delete) for authenticated" ON ordering."OrderLine" IS 'DDDToolkit row access rule';

-- The roles the policies and the privileges above are written for, as the project that exports says them in
-- SupabaseRowAccessRoles, recorded on the ddd schema: the application compares the roles it switches to with
-- them when it starts, in the start-up check supabase.roles-match-access-files.
DO $ddd$
DECLARE
    recorded constant text := 'DDDToolkit row access roles: {"user":"authenticated","anonymous":"anon","system-in":"ddd_system_in","system":null,"token":{}}';
BEGIN
    IF pg_catalog.to_regnamespace('ddd') IS NULL THEN
        CREATE SCHEMA ddd;
    END IF;
    IF pg_catalog.obj_description(pg_catalog.to_regnamespace('ddd')::pg_catalog.oid, 'pg_namespace') IS DISTINCT FROM recorded THEN
        EXECUTE 'COMMENT ON SCHEMA ddd IS ' || pg_catalog.quote_literal(recorded);
    END IF;
END
$ddd$;
