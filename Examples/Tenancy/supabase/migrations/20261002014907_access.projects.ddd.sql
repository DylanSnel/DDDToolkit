-- Written by DDDToolkit from the row access rules of ProjectsContext.
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
          AND (n.nspname, c.relname) IN (('projects', 'ProjectCrewMembers'), ('projects', 'ProjectCrewRoleGrants'), ('projects', 'Projects'), ('projects', 'ProjectsOutboxMessages'))
    LOOP
        EXECUTE format('DROP POLICY %I ON %I.%I', generated.polname, generated.nspname, generated.relname);
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
        WHERE d.description = 'DDDToolkit access function of ProjectsContext'
          AND (n.nspname, p.proname) NOT IN (('projects', 'crew_member_project_ids'), ('projects', 'crew_project_ids'), ('projects', 'project_ids_i_see'), ('projects', 'project_ids_where_i_hold'))
          -- A function a policy still asks, of a module whose file comes after this one, stays until the next file.
          AND NOT EXISTS (SELECT FROM pg_catalog.pg_depend dependent
                          WHERE dependent.refclassid = 'pg_catalog.pg_proc'::regclass AND dependent.refobjid = p.oid AND dependent.deptype = 'n')
    LOOP
        EXECUTE format('DROP FUNCTION %s', generated.signature);
    END LOOP;
END
$ddd$;

CREATE OR REPLACE FUNCTION projects.crew_member_project_ids() RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
    SELECT DISTINCT e1."ProjectId" FROM projects."ProjectCrewMembers" e1
    WHERE (((e1."SeatId" = (SELECT tenancy.caller_seat())) AND coalesce(e1."StartsAt" <= now(), FALSE)) AND ((e1."EndsAt" IS NULL) OR coalesce(e1."EndsAt" > now(), FALSE)))
$function$;
COMMENT ON FUNCTION projects.crew_member_project_ids() IS 'DDDToolkit access function of ProjectsContext';
REVOKE ALL ON FUNCTION projects.crew_member_project_ids() FROM PUBLIC;

CREATE OR REPLACE FUNCTION projects.crew_project_ids(text) RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
    SELECT DISTINCT e1."ProjectId" FROM projects."ProjectCrewMembers" e1
    WHERE ((((e1."SeatId" = (SELECT tenancy.caller_seat())) AND coalesce(e1."StartsAt" <= now(), FALSE)) AND ((e1."EndsAt" IS NULL) OR coalesce(e1."EndsAt" > now(), FALSE))) AND EXISTS (SELECT 1 FROM projects."ProjectCrewRoleGrants" e2 WHERE e2."ProjectId" = e1."ProjectId" AND e2."CrewMemberId" = e1."Id" AND ((((e2."RoleId" = ANY (ARRAY(SELECT tenancy.roles_with_key($1)))) AND coalesce(e2."StartsAt" <= now(), FALSE)) AND ((e2."EndsAt" IS NULL) OR coalesce(e2."EndsAt" > now(), FALSE))))))
$function$;
COMMENT ON FUNCTION projects.crew_project_ids(text) IS 'DDDToolkit access function of ProjectsContext';
REVOKE ALL ON FUNCTION projects.crew_project_ids(text) FROM PUBLIC;

CREATE OR REPLACE FUNCTION projects.project_ids_i_see() RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
    SELECT root."Id" FROM projects."Projects" root
    WHERE ((root."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.view')))) OR (root."Id" = ANY (ARRAY(SELECT projects.crew_member_project_ids()))))
$function$;
COMMENT ON FUNCTION projects.project_ids_i_see() IS 'DDDToolkit access function of ProjectsContext';
REVOKE ALL ON FUNCTION projects.project_ids_i_see() FROM PUBLIC;

CREATE OR REPLACE FUNCTION projects.project_ids_where_i_hold(text) RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
    SELECT root."Id" FROM projects."Projects" root
    WHERE ((root."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold($1)))) OR ((($1 IS DISTINCT FROM 'projects.open') AND ($1 IS DISTINCT FROM 'projects.owner.change')) AND (root."Id" = ANY (ARRAY(SELECT projects.crew_project_ids($1))))))
$function$;
COMMENT ON FUNCTION projects.project_ids_where_i_hold(text) IS 'DDDToolkit access function of ProjectsContext';
REVOKE ALL ON FUNCTION projects.project_ids_where_i_hold(text) FROM PUBLIC;

-- Who may execute the functions above: the roles granted it below, and nobody else. Every other grant
-- on them is taken back first, what an earlier file gave and what a schema's default privileges gave alike.
DO $ddd$
DECLARE
    granted record;
BEGIN
    FOR granted IN
        SELECT DISTINCT p.oid AS function, acl.grantee
        FROM pg_catalog.pg_proc p
        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        JOIN pg_catalog.pg_description d ON d.objoid = p.oid AND d.classoid = 'pg_catalog.pg_proc'::regclass
        CROSS JOIN LATERAL pg_catalog.aclexplode(p.proacl) acl
        WHERE d.description = 'DDDToolkit access function of ProjectsContext'
          AND (n.nspname, p.proname) IN (('projects', 'crew_member_project_ids'), ('projects', 'crew_project_ids'), ('projects', 'project_ids_i_see'), ('projects', 'project_ids_where_i_hold'))
          AND acl.grantee <> 0 AND acl.grantee <> p.proowner
    LOOP
        EXECUTE pg_catalog.format('REVOKE ALL ON FUNCTION %s FROM %s', granted.function::pg_catalog.regprocedure, granted.grantee::pg_catalog.regrole);
    END LOOP;
END
$ddd$;
GRANT EXECUTE ON FUNCTION projects.crew_member_project_ids() TO authenticated;
GRANT EXECUTE ON FUNCTION projects.crew_project_ids(text) TO authenticated;
GRANT EXECUTE ON FUNCTION projects.project_ids_i_see() TO authenticated;
GRANT EXECUTE ON FUNCTION projects.project_ids_where_i_hold(text) TO authenticated;

ALTER TABLE projects."Projects" ENABLE ROW LEVEL SECURITY;
ALTER TABLE projects."Projects" FORCE ROW LEVEL SECURITY;

CREATE POLICY "Seats see the projects they reach (select) for authenticated" ON projects."Projects" FOR SELECT TO authenticated
    USING (("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.view')))) OR ("Id" = ANY (ARRAY(SELECT projects.crew_member_project_ids()))));
COMMENT ON POLICY "Seats see the projects they reach (select) for authenticated" ON projects."Projects" IS 'DDDToolkit row access rule';

-- System work in its tenant (select) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (select) for ddd_system_in" ON projects."Projects" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (select) for ddd_system_in" ON projects."Projects" IS 'DDDToolkit row access rule';

CREATE POLICY "Operators see every project (select) for tenancy_operator" ON projects."Projects" FOR SELECT TO tenancy_operator
    USING (TRUE);
COMMENT ON POLICY "Operators see every project (select) for tenancy_operator" ON projects."Projects" IS 'DDDToolkit row access rule';

CREATE POLICY "Seats open projects where they may (insert) for authenticated" ON projects."Projects" FOR INSERT TO authenticated
    WITH CHECK ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))));
COMMENT ON POLICY "Seats open projects where they may (insert) for authenticated" ON projects."Projects" IS 'DDDToolkit row access rule';

-- System work in its tenant (insert) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."Projects" FOR INSERT TO ddd_system_in
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."Projects" IS 'DDDToolkit row access rule';

CREATE POLICY "Seats change the projects they work on (update) for ~ 63b69574" ON projects."Projects" FOR UPDATE TO authenticated
    USING (((((((("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.edit')))) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.close'))))) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.crew.manage'))))) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR ("Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.edit'))))) OR ("Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.close'))))) OR ("Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.crew.manage')))))
    WITH CHECK (((((((("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.edit')))) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.close'))))) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.crew.manage'))))) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR ("Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.edit'))))) OR ("Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.close'))))) OR ("Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.crew.manage')))));
COMMENT ON POLICY "Seats change the projects they work on (update) for ~ 63b69574" ON projects."Projects" IS 'DDDToolkit row access rule';

-- System work in its tenant (update) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (update) for ddd_system_in" ON projects."Projects" FOR UPDATE TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (update) for ddd_system_in" ON projects."Projects" IS 'DDDToolkit row access rule';

-- System work in its tenant (delete) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (delete) for ddd_system_in" ON projects."Projects" FOR DELETE TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (delete) for ddd_system_in" ON projects."Projects" IS 'DDDToolkit row access rule';

-- ProjectCrewMembers belongs to the aggregate: it is read with its Projects, and written as the rules let a caller write its Projects.
ALTER TABLE projects."ProjectCrewMembers" ENABLE ROW LEVEL SECURITY;
ALTER TABLE projects."ProjectCrewMembers" FORCE ROW LEVEL SECURITY;
CREATE POLICY "ProjectCrewMembers (select) for authenticated" ON projects."ProjectCrewMembers" FOR SELECT TO authenticated
    USING (EXISTS (SELECT 1 FROM projects."Projects" parent WHERE parent."Id" = projects."ProjectCrewMembers"."ProjectId"));
COMMENT ON POLICY "ProjectCrewMembers (select) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
-- System work in its tenant (select) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (select) for ddd_system_in" ON projects."ProjectCrewMembers" FOR SELECT TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant())));
COMMENT ON POLICY "System work in its tenant (select) for ddd_system_in" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewMembers (select) for tenancy_operator" ON projects."ProjectCrewMembers" FOR SELECT TO tenancy_operator
    USING (EXISTS (SELECT 1 FROM projects."Projects" parent WHERE parent."Id" = projects."ProjectCrewMembers"."ProjectId"));
COMMENT ON POLICY "ProjectCrewMembers (select) for tenancy_operator" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewMembers (insert) for authenticated" ON projects."ProjectCrewMembers" FOR INSERT TO authenticated
    WITH CHECK (EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = projects."ProjectCrewMembers"."ProjectId" AND (((((((((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.edit')))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.close'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.crew.manage'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.edit'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.crew.manage'))))) OR ((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))) AND ddd.written_in_this_transaction(r.xmin)))));
COMMENT ON POLICY "ProjectCrewMembers (insert) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
-- System work in its tenant (insert) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."ProjectCrewMembers" FOR INSERT TO ddd_system_in
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant())));
COMMENT ON POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewMembers (update) for authenticated" ON projects."ProjectCrewMembers" FOR UPDATE TO authenticated
    USING (EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = projects."ProjectCrewMembers"."ProjectId" AND ((((((((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.edit')))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.close'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.crew.manage'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.edit'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.crew.manage')))))))
    WITH CHECK (EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = projects."ProjectCrewMembers"."ProjectId" AND ((((((((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.edit')))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.close'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.crew.manage'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.edit'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.crew.manage')))))));
COMMENT ON POLICY "ProjectCrewMembers (update) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
-- System work in its tenant (update) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (update) for ddd_system_in" ON projects."ProjectCrewMembers" FOR UPDATE TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant())))
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant())));
COMMENT ON POLICY "System work in its tenant (update) for ddd_system_in" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewMembers (delete) for authenticated" ON projects."ProjectCrewMembers" FOR DELETE TO authenticated
    USING (EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = projects."ProjectCrewMembers"."ProjectId" AND ((((((((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.edit')))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.close'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.crew.manage'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.edit'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.crew.manage')))))));
COMMENT ON POLICY "ProjectCrewMembers (delete) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
-- System work in its tenant (delete) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (delete) for ddd_system_in" ON projects."ProjectCrewMembers" FOR DELETE TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant())));
COMMENT ON POLICY "System work in its tenant (delete) for ddd_system_in" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- ProjectCrewRoleGrants belongs to the aggregate: it is read with its ProjectCrewMembers, and written as the rules let a caller write its Projects.
ALTER TABLE projects."ProjectCrewRoleGrants" ENABLE ROW LEVEL SECURITY;
ALTER TABLE projects."ProjectCrewRoleGrants" FORCE ROW LEVEL SECURITY;
CREATE POLICY "ProjectCrewRoleGrants (select) for authenticated" ON projects."ProjectCrewRoleGrants" FOR SELECT TO authenticated
    USING (EXISTS (SELECT 1 FROM projects."ProjectCrewMembers" parent WHERE parent."ProjectId" = projects."ProjectCrewRoleGrants"."ProjectId" AND parent."Id" = projects."ProjectCrewRoleGrants"."CrewMemberId"));
COMMENT ON POLICY "ProjectCrewRoleGrants (select) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
-- System work in its tenant (select) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (select) for ddd_system_in" ON projects."ProjectCrewRoleGrants" FOR SELECT TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))));
COMMENT ON POLICY "System work in its tenant (select) for ddd_system_in" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewRoleGrants (select) for tenancy_operator" ON projects."ProjectCrewRoleGrants" FOR SELECT TO tenancy_operator
    USING (EXISTS (SELECT 1 FROM projects."ProjectCrewMembers" parent WHERE parent."ProjectId" = projects."ProjectCrewRoleGrants"."ProjectId" AND parent."Id" = projects."ProjectCrewRoleGrants"."CrewMemberId"));
COMMENT ON POLICY "ProjectCrewRoleGrants (select) for tenancy_operator" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewRoleGrants (insert) for authenticated" ON projects."ProjectCrewRoleGrants" FOR INSERT TO authenticated
    WITH CHECK (EXISTS (SELECT 1 FROM projects."ProjectCrewMembers" p1 WHERE p1."ProjectId" = projects."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = projects."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = p1."ProjectId" AND (((((((((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.edit')))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.close'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.crew.manage'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.edit'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.crew.manage'))))) OR ((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))) AND ddd.written_in_this_transaction(r.xmin))))));
COMMENT ON POLICY "ProjectCrewRoleGrants (insert) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
-- System work in its tenant (insert) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."ProjectCrewRoleGrants" FOR INSERT TO ddd_system_in
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))));
COMMENT ON POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewRoleGrants (update) for authenticated" ON projects."ProjectCrewRoleGrants" FOR UPDATE TO authenticated
    USING (EXISTS (SELECT 1 FROM projects."ProjectCrewMembers" p1 WHERE p1."ProjectId" = projects."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = projects."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = p1."ProjectId" AND ((((((((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.edit')))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.close'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.crew.manage'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.edit'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.crew.manage'))))))))
    WITH CHECK (EXISTS (SELECT 1 FROM projects."ProjectCrewMembers" p1 WHERE p1."ProjectId" = projects."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = projects."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = p1."ProjectId" AND ((((((((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.edit')))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.close'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.crew.manage'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.edit'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.crew.manage'))))))));
COMMENT ON POLICY "ProjectCrewRoleGrants (update) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
-- System work in its tenant (update) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (update) for ddd_system_in" ON projects."ProjectCrewRoleGrants" FOR UPDATE TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))))
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))));
COMMENT ON POLICY "System work in its tenant (update) for ddd_system_in" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewRoleGrants (delete) for authenticated" ON projects."ProjectCrewRoleGrants" FOR DELETE TO authenticated
    USING (EXISTS (SELECT 1 FROM projects."ProjectCrewMembers" p1 WHERE p1."ProjectId" = projects."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = projects."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = p1."ProjectId" AND ((((((((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.edit')))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.close'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.crew.manage'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.edit'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.crew_project_ids('projects.crew.manage'))))))));
COMMENT ON POLICY "ProjectCrewRoleGrants (delete) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
-- System work in its tenant (delete) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (delete) for ddd_system_in" ON projects."ProjectCrewRoleGrants" FOR DELETE TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))));
COMMENT ON POLICY "System work in its tenant (delete) for ddd_system_in" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant())))
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant())));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for authenticated is the policy 'Kept to its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for authenticated" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR ALL TO authenticated
    USING (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.caller_tenant())))
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.caller_tenant())));
COMMENT ON POLICY "Kept to its tenant (all) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))))
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for authenticated is the policy 'Kept to its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for authenticated" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR ALL TO authenticated
    USING (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.caller_tenant()))))
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.caller_tenant()))));
COMMENT ON POLICY "Kept to its tenant (all) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON projects."Projects" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON projects."Projects" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for authenticated is the policy 'Kept to its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for authenticated" ON projects."Projects" AS RESTRICTIVE FOR ALL TO authenticated
    USING ("TenantId" = (SELECT tenancy.caller_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.caller_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for authenticated" ON projects."Projects" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON projects."Projects" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON projects."Projects" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON projects."Projects" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON projects."Projects" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON projects."Projects" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON projects."Projects" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON projects."Projects" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON projects."Projects" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON projects."Projects" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON projects."Projects" IS 'DDDToolkit row access rule';

-- Privileges, from the policies above: what a table gave the roles of this file before is taken back, and a
-- role then gets the commands a permissive policy allows it, and no more. UPDATE is granted on the columns
-- that may change.
GRANT USAGE ON SCHEMA projects TO authenticated, ddd_system, ddd_system_in, tenancy_operator;

REVOKE ALL ON TABLE projects."ProjectCrewMembers" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT, DELETE ON TABLE projects."ProjectCrewMembers" TO authenticated;
GRANT UPDATE ("AddedBy", "EndsAt", "SeatId", "StartsAt") ON TABLE projects."ProjectCrewMembers" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE projects."ProjectCrewMembers" TO ddd_system_in;
GRANT UPDATE ("AddedBy", "EndsAt", "SeatId", "StartsAt") ON TABLE projects."ProjectCrewMembers" TO ddd_system_in;
GRANT SELECT ON TABLE projects."ProjectCrewMembers" TO tenancy_operator;

REVOKE ALL ON TABLE projects."ProjectCrewRoleGrants" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT, DELETE ON TABLE projects."ProjectCrewRoleGrants" TO authenticated;
GRANT UPDATE ("EndsAt", "GivenBy", "RoleId", "StartsAt") ON TABLE projects."ProjectCrewRoleGrants" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE projects."ProjectCrewRoleGrants" TO ddd_system_in;
GRANT UPDATE ("EndsAt", "GivenBy", "RoleId", "StartsAt") ON TABLE projects."ProjectCrewRoleGrants" TO ddd_system_in;
GRANT SELECT ON TABLE projects."ProjectCrewRoleGrants" TO tenancy_operator;

REVOKE ALL ON TABLE projects."Projects" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT ON TABLE projects."Projects" TO authenticated;
GRANT UPDATE ("ChangedByIdentity", "ChangedByKind", "ChangedBySeat", "Name", "OwnerSeatId", "PlannedFrom", "PlannedUntil", "State", "UnitId", "Version") ON TABLE projects."Projects" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE projects."Projects" TO ddd_system_in;
GRANT UPDATE ("ChangedByIdentity", "ChangedByKind", "ChangedBySeat", "Name", "OwnerSeatId", "PlannedFrom", "PlannedUntil", "State", "UnitId", "Version") ON TABLE projects."Projects" TO ddd_system_in;
GRANT SELECT ON TABLE projects."Projects" TO tenancy_operator;

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
        WHERE c.oid IN ('projects."ProjectsOutboxMessages"'::pg_catalog.regclass)
          AND acl.grantee <> c.relowner
          AND NOT (holder.rolcanlogin OR holder.rolbypassrls OR holder.rolsuper)
    LOOP
        EXECUTE pg_catalog.format('REVOKE ALL ON TABLE %s FROM %s', held.relation::pg_catalog.regclass, held.grantee::pg_catalog.regrole);
    END LOOP;
END
$ddd$;

-- The outbox takes a row from whoever saves, and nobody but the bookkeeping reads, marks or deletes one.
REVOKE ALL ON TABLE projects."ProjectsOutboxMessages" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT INSERT ON TABLE projects."ProjectsOutboxMessages" TO authenticated, ddd_system_in;
GRANT SELECT, DELETE ON TABLE projects."ProjectsOutboxMessages" TO ddd_system;
GRANT UPDATE ("Attempts", "LastError", "NextAttemptAt", "ProcessedAt") ON TABLE projects."ProjectsOutboxMessages" TO ddd_system;

-- The bookkeeping role reads which migrations ran, where the database keeps a history of them.
DO $ddd$
BEGIN
    IF pg_catalog.to_regclass('projects."__EFMigrationsHistory"') IS NOT NULL THEN
        GRANT USAGE ON SCHEMA projects TO ddd_system;
        GRANT SELECT ON TABLE projects."__EFMigrationsHistory" TO ddd_system;
    END IF;
END
$ddd$;

-- Written by the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE OR REPLACE FUNCTION "projects".attribution_matches_caller() RETURNS trigger
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
            RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', MESSAGE = 'A seat records a row as written and changed by itself.';
        END IF;
    ELSIF CURRENT_USER = 'ddd_system_in' THEN
        IF (written ->> TG_ARGV[4]) IS NOT DISTINCT FROM 'seat' OR (TG_OP = 'INSERT' AND (written ->> TG_ARGV[1]) IS NOT DISTINCT FROM 'seat') THEN
            RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', MESSAGE = 'System work records a row as written and changed by no seat.';
        END IF;
    ELSE
        RETURN NEW;
    END IF;
    IF TG_OP = 'UPDATE' THEN
        as_it_was := pg_catalog.to_jsonb(OLD);
        IF ((written ->> TG_ARGV[0]), (written ->> TG_ARGV[1]), (written ->> TG_ARGV[2])) IS DISTINCT FROM ((as_it_was ->> TG_ARGV[0]), (as_it_was ->> TG_ARGV[1]), (as_it_was ->> TG_ARGV[2])) THEN
            RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', MESSAGE = 'Who wrote a row first does not change.';
        END IF;
    END IF;
    RETURN NEW;
END
$body$;
REVOKE ALL ON FUNCTION "projects".attribution_matches_caller() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_attribution_matches_caller ON "projects"."Projects";
CREATE TRIGGER tenancy_attribution_matches_caller BEFORE INSERT OR UPDATE ON "projects"."Projects"
    FOR EACH ROW EXECUTE FUNCTION "projects".attribution_matches_caller('CreatedBySeat', 'CreatedByKind', 'CreatedByIdentity', 'ChangedBySeat', 'ChangedByKind', 'ChangedByIdentity');
