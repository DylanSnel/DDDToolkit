-- Written by DDDToolkit from the row access rules of InspectionsContext.
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
          AND (n.nspname, c.relname) IN (('inspections', 'Inspections'), ('inspections', 'InspectionsOutboxMessages'))
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
          AND (n.nspname, c.relname) IN (('inspections', 'Inspections'), ('inspections', 'InspectionsOutboxMessages'))
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
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'tenancy_operator') THEN
        BEGIN
            CREATE ROLE tenancy_operator NOLOGIN NOINHERIT;
        EXCEPTION WHEN duplicate_object OR unique_violation THEN
            NULL; -- made by a script that ran at the same time
        END;
    END IF;
    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'tenancy_operator'
               AND (rolbypassrls OR rolsuper OR rolcanlogin)) THEN
        RAISE EXCEPTION USING MESSAGE = 'The role tenancy_operator exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.';
    END IF;
    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_class c
               WHERE scoped.rolname = 'tenancy_operator' AND c.relkind IN ('r', 'p')
                 AND pg_catalog.pg_has_role(scoped.oid, c.relowner, 'USAGE')) THEN
        RAISE EXCEPTION USING MESSAGE = 'The role tenancy_operator has the privileges of a role that owns tables, and Postgres does not hold an owner to row level security. It must neither own a table nor be granted a role that does.';
    END IF;
    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_roles callers
               WHERE scoped.rolname = 'tenancy_operator' AND callers.rolname IN ('authenticated', 'anon')
                 AND pg_catalog.pg_has_role(callers.oid, scoped.oid, 'USAGE')) THEN
        RAISE EXCEPTION USING MESSAGE = 'The role tenancy_operator is granted to authenticated or anon, so their callers would get every policy written for it. Grant it only to the role the application logs in as.';
    END IF;
    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_roles callers
               WHERE scoped.rolname = 'tenancy_operator' AND callers.rolname IN ('authenticated', 'anon')
                 AND pg_catalog.pg_has_role(scoped.oid, callers.oid, 'USAGE')) THEN
        RAISE EXCEPTION USING MESSAGE = 'The role tenancy_operator has the privileges of authenticated or anon, so the holder of a token mapped to it would get every policy and every privilege written for those callers. Take that grant back: a mapped role has what is written for it and no more.';
    END IF;
    IF NOT pg_catalog.pg_has_role(CURRENT_USER, 'tenancy_operator'::pg_catalog.name,
            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
        BEGIN
            GRANT tenancy_operator TO CURRENT_USER;
        EXCEPTION WHEN unique_violation THEN
            NULL; -- granted by a script that ran at the same time
        END;
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'ddd_system_in') THEN
        BEGIN
            CREATE ROLE ddd_system_in NOLOGIN NOINHERIT;
        EXCEPTION WHEN duplicate_object OR unique_violation THEN
            NULL; -- made by a script that ran at the same time
        END;
    END IF;
    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'ddd_system_in'
               AND (rolbypassrls OR rolsuper OR rolcanlogin)) THEN
        RAISE EXCEPTION USING MESSAGE = 'The role ddd_system_in exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.';
    END IF;
    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_class c
               WHERE scoped.rolname = 'ddd_system_in' AND c.relkind IN ('r', 'p')
                 AND pg_catalog.pg_has_role(scoped.oid, c.relowner, 'USAGE')) THEN
        RAISE EXCEPTION USING MESSAGE = 'The role ddd_system_in has the privileges of a role that owns tables, and Postgres does not hold an owner to row level security. It must neither own a table nor be granted a role that does.';
    END IF;
    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_roles callers
               WHERE scoped.rolname = 'ddd_system_in' AND callers.rolname IN ('authenticated', 'anon', 'tenancy_operator')
                 AND pg_catalog.pg_has_role(callers.oid, scoped.oid, 'USAGE')) THEN
        RAISE EXCEPTION USING MESSAGE = 'The role ddd_system_in is granted to authenticated, anon or tenancy_operator, so their callers would get every policy written for it. Grant it only to the role the application logs in as.';
    END IF;
    IF NOT pg_catalog.pg_has_role(CURRENT_USER, 'ddd_system_in'::pg_catalog.name,
            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
        BEGIN
            GRANT ddd_system_in TO CURRENT_USER;
        EXCEPTION WHEN unique_violation THEN
            NULL; -- granted by a script that ran at the same time
        END;
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'ddd_system') THEN
        BEGIN
            CREATE ROLE ddd_system NOLOGIN NOINHERIT;
        EXCEPTION WHEN duplicate_object OR unique_violation THEN
            NULL; -- made by a script that ran at the same time
        END;
    END IF;
    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'ddd_system'
               AND (rolbypassrls OR rolsuper OR rolcanlogin)) THEN
        RAISE EXCEPTION USING MESSAGE = 'The role ddd_system exists, but it can bypass row level security or log in. It must be NOLOGIN, without BYPASSRLS and not a superuser.';
    END IF;
    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_class c
               WHERE scoped.rolname = 'ddd_system' AND c.relkind IN ('r', 'p')
                 AND pg_catalog.pg_has_role(scoped.oid, c.relowner, 'USAGE')) THEN
        RAISE EXCEPTION USING MESSAGE = 'The role ddd_system has the privileges of a role that owns tables, and Postgres does not hold an owner to row level security. It must neither own a table nor be granted a role that does.';
    END IF;
    IF EXISTS (SELECT FROM pg_catalog.pg_roles scoped, pg_catalog.pg_roles callers
               WHERE scoped.rolname = 'ddd_system' AND callers.rolname IN ('authenticated', 'anon', 'ddd_system_in', 'tenancy_operator')
                 AND pg_catalog.pg_has_role(callers.oid, scoped.oid, 'USAGE')) THEN
        RAISE EXCEPTION USING MESSAGE = 'The role ddd_system is granted to authenticated, anon, ddd_system_in or tenancy_operator, so their callers would hold the outbox, the inbox and the migration history. Grant it only to the role the application logs in as.';
    END IF;
    IF NOT pg_catalog.pg_has_role(CURRENT_USER, 'ddd_system'::pg_catalog.name,
            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
        BEGIN
            GRANT ddd_system TO CURRENT_USER;
        EXCEPTION WHEN unique_violation THEN
            NULL; -- granted by a script that ran at the same time
        END;
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
    IF NOT coalesce(pg_catalog.has_schema_privilege(pg_catalog.to_regrole('ddd_system_in'), 'ddd', 'USAGE'), false) THEN
        GRANT USAGE ON SCHEMA ddd TO ddd_system_in;
    END IF;
    IF NOT coalesce(pg_catalog.has_function_privilege(pg_catalog.to_regrole('ddd_system_in'), 'ddd.written_in_this_transaction(xid)', 'EXECUTE'), false) THEN
        GRANT EXECUTE ON FUNCTION ddd.written_in_this_transaction(xid) TO ddd_system_in;
    END IF;
    IF NOT coalesce(pg_catalog.has_schema_privilege(pg_catalog.to_regrole('tenancy_operator'), 'ddd', 'USAGE'), false) THEN
        GRANT USAGE ON SCHEMA ddd TO tenancy_operator;
    END IF;
    IF NOT coalesce(pg_catalog.has_function_privilege(pg_catalog.to_regrole('tenancy_operator'), 'ddd.written_in_this_transaction(xid)', 'EXECUTE'), false) THEN
        GRANT EXECUTE ON FUNCTION ddd.written_in_this_transaction(xid) TO tenancy_operator;
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
        WHERE d.description = 'DDDToolkit access function of InspectionsContext'
          -- A function a policy still asks, of a module whose file comes after this one, stays until the next file.
          AND NOT EXISTS (SELECT FROM pg_catalog.pg_depend dependent
                          WHERE dependent.refclassid = 'pg_catalog.pg_proc'::regclass AND dependent.refobjid = p.oid AND dependent.deptype = 'n')
    LOOP
        EXECUTE format('DROP FUNCTION %s', generated.signature);
    END LOOP;
END
$ddd$;

ALTER TABLE inspections."Inspections" ENABLE ROW LEVEL SECURITY;
ALTER TABLE inspections."Inspections" FORCE ROW LEVEL SECURITY;

CREATE POLICY "Seats see the inspections of projects they see (sele ~ e2b699c1" ON inspections."Inspections" FOR SELECT TO authenticated
    USING ("ProjectId" = ANY (ARRAY(SELECT projects.project_ids_i_see())));
COMMENT ON POLICY "Seats see the inspections of projects they see (sele ~ e2b699c1" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- System work in its tenant (select) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work in its tenant (select) for ddd_system_in" ON inspections."Inspections" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (select) for ddd_system_in" ON inspections."Inspections" IS 'DDDToolkit row access rule';

CREATE POLICY "Operators see every inspection (select) for tenancy_operator" ON inspections."Inspections" FOR SELECT TO tenancy_operator
    USING (TRUE);
COMMENT ON POLICY "Operators see every inspection (select) for tenancy_operator" ON inspections."Inspections" IS 'DDDToolkit row access rule';

CREATE POLICY "Seats record where they may (insert) for authenticated" ON inspections."Inspections" FOR INSERT TO authenticated
    WITH CHECK (("ProjectId" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('inspections.record')))) AND ("RecordedBy" = (SELECT tenancy.caller_seat())));
COMMENT ON POLICY "Seats record where they may (insert) for authenticated" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- System work in its tenant (insert) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work in its tenant (insert) for ddd_system_in" ON inspections."Inspections" FOR INSERT TO ddd_system_in
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (insert) for ddd_system_in" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- System work in its tenant (update) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work in its tenant (update) for ddd_system_in" ON inspections."Inspections" FOR UPDATE TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (update) for ddd_system_in" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- System work in its tenant (delete) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work in its tenant (delete) for ddd_system_in" ON inspections."Inspections" FOR DELETE TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (delete) for ddd_system_in" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON inspections."Inspections" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for authenticated is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for authenticated" ON inspections."Inspections" AS RESTRICTIVE FOR ALL TO authenticated
    USING ("TenantId" = (SELECT tenancy.caller_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.caller_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for authenticated" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON inspections."Inspections" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON inspections."Inspections" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON inspections."Inspections" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON inspections."Inspections" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON inspections."Inspections" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON inspections."Inspections" IS 'DDDToolkit row access rule';

-- Privileges, from the policies above: what a table gave the roles of this file before is taken back, and a
-- role then gets the commands a permissive policy allows it, and no more. UPDATE is granted on the columns
-- that may change.
GRANT USAGE ON SCHEMA inspections TO authenticated, ddd_system, ddd_system_in, tenancy_operator;

REVOKE ALL ON TABLE inspections."Inspections" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT ON TABLE inspections."Inspections" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE inspections."Inspections" TO ddd_system_in;
GRANT UPDATE ("ChangedByIdentity", "ChangedByKind", "ChangedBySeat", "DaysFrom", "DaysUntil", "RecordedAt", "RecordedBy", "Title", "Version") ON TABLE inspections."Inspections" TO ddd_system_in;
GRANT SELECT ON TABLE inspections."Inspections" TO tenancy_operator;

-- The toolkit's own tables have no policies, so a privilege is their only lock. What an earlier file gave a
-- role this one no longer names is taken back too: from every role that holds a privilege on them, cannot
-- log in and is held to row level security.
DO $ddd$
DECLARE
    held record;
BEGIN
    FOR held IN
        SELECT DISTINCT c.oid AS relation, acl.grantee
        FROM pg_catalog.pg_class c
        CROSS JOIN LATERAL (
            SELECT whole.grantee FROM pg_catalog.aclexplode(c.relacl) whole
            UNION ALL
            SELECT part.grantee FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) part
            WHERE a.attrelid = c.oid AND NOT a.attisdropped) acl
        JOIN pg_catalog.pg_roles holder ON holder.oid = acl.grantee
        WHERE c.oid IN ('inspections."InspectionsOutboxMessages"'::pg_catalog.regclass)
          AND acl.grantee <> c.relowner
          AND NOT (holder.rolcanlogin OR holder.rolbypassrls OR holder.rolsuper)
    LOOP
        EXECUTE pg_catalog.format('REVOKE ALL ON TABLE %s FROM %s', held.relation::pg_catalog.regclass, held.grantee::pg_catalog.regrole);
    END LOOP;
END
$ddd$;

-- The outbox takes a row from whoever saves, and nobody but the bookkeeping reads, marks or deletes one.
REVOKE ALL ON TABLE inspections."InspectionsOutboxMessages" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT INSERT ON TABLE inspections."InspectionsOutboxMessages" TO authenticated, ddd_system_in;
GRANT SELECT, DELETE ON TABLE inspections."InspectionsOutboxMessages" TO ddd_system;
GRANT UPDATE ("Attempts", "LastError", "NextAttemptAt", "ProcessedAt") ON TABLE inspections."InspectionsOutboxMessages" TO ddd_system;

-- The bookkeeping role reads which migrations ran, where the database keeps a history of them.
DO $ddd$
BEGIN
    IF pg_catalog.to_regclass('inspections."__EFMigrationsHistory"') IS NOT NULL THEN
        GRANT USAGE ON SCHEMA inspections TO ddd_system;
        GRANT SELECT ON TABLE inspections."__EFMigrationsHistory" TO ddd_system;
    END IF;
END
$ddd$;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION "inspections".attribution_matches_caller() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
DECLARE
    written pg_catalog.jsonb := pg_catalog.to_jsonb(NEW);
    as_it_was pg_catalog.jsonb;
    caller_seat pg_catalog.text;
BEGIN
    IF CURRENT_USER = 'authenticated' THEN
        caller_seat := (SELECT tenancy.caller_seat())::pg_catalog.text;
        IF caller_seat IS NULL OR (written ->> TG_ARGV[4]) IS DISTINCT FROM 'seat' OR (written ->> TG_ARGV[3]) IS DISTINCT FROM caller_seat OR (written ->> TG_ARGV[5]) IS NOT NULL
           OR (TG_OP = 'INSERT' AND ((written ->> TG_ARGV[1]) IS DISTINCT FROM 'seat' OR (written ->> TG_ARGV[0]) IS DISTINCT FROM caller_seat OR (written ->> TG_ARGV[2]) IS NOT NULL)) THEN
            RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'tenancy_attribution_matches_caller', HINT = 'ddd:access.refused', MESSAGE = 'A seat records a row as written and changed by itself.';
        END IF;
    ELSIF CURRENT_USER = 'ddd_system_in' THEN
        IF (written ->> TG_ARGV[4]) IS NOT DISTINCT FROM 'seat' OR (TG_OP = 'INSERT' AND (written ->> TG_ARGV[1]) IS NOT DISTINCT FROM 'seat') THEN
            RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'tenancy_attribution_matches_caller', HINT = 'ddd:access.refused', MESSAGE = 'System work records a row as written and changed by no seat.';
        END IF;
    ELSE
        RETURN NEW;
    END IF;
    IF TG_OP = 'UPDATE' THEN
        as_it_was := pg_catalog.to_jsonb(OLD);
        IF ((written ->> TG_ARGV[0]), (written ->> TG_ARGV[1]), (written ->> TG_ARGV[2])) IS DISTINCT FROM ((as_it_was ->> TG_ARGV[0]), (as_it_was ->> TG_ARGV[1]), (as_it_was ->> TG_ARGV[2])) THEN
            RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'tenancy_attribution_matches_caller', HINT = 'ddd:access.refused', MESSAGE = 'Who wrote a row first does not change.';
        END IF;
    END IF;
    RETURN NEW;
END
$body$;
REVOKE ALL ON FUNCTION "inspections".attribution_matches_caller() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_attribution_matches_caller ON "inspections"."Inspections";
CREATE TRIGGER tenancy_attribution_matches_caller BEFORE INSERT OR UPDATE ON "inspections"."Inspections"
    FOR EACH ROW EXECUTE FUNCTION "inspections".attribution_matches_caller('CreatedBySeat', 'CreatedByKind', 'CreatedByIdentity', 'ChangedBySeat', 'ChangedByKind', 'ChangedByIdentity');

-- The roles the policies and the privileges above are written for, as the project that exports says them in
-- SupabaseRowAccessRoles, recorded on the ddd schema: the application compares the roles it switches to with
-- them when it starts, in the start-up check supabase.roles-match-access-files.
DO $ddd$
DECLARE
    recorded constant text := 'DDDToolkit row access roles: {"user":"authenticated","anonymous":"anon","system-in":"ddd_system_in","system":"ddd_system","token":{"tenancy_operator":"tenancy_operator"}}';
BEGIN
    IF pg_catalog.to_regnamespace('ddd') IS NULL THEN
        CREATE SCHEMA ddd;
    END IF;
    IF pg_catalog.obj_description(pg_catalog.to_regnamespace('ddd')::pg_catalog.oid, 'pg_namespace') IS DISTINCT FROM recorded THEN
        EXECUTE 'COMMENT ON SCHEMA ddd IS ' || pg_catalog.quote_literal(recorded);
    END IF;
END
$ddd$;
