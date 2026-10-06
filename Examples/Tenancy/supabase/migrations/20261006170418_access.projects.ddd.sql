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
          AND (n.nspname, p.proname) NOT IN (('projects', 'crew_member_project_ids'), ('projects', 'crew_project_ids'), ('projects', 'project_ids_i_see'), ('projects', 'project_ids_where_i_hold'), ('projects', 'projects_name_plannedfrom_planneduntil_column_rule'), ('projects', 'projects_state_column_rule'))
          -- A function a policy still asks, of a module whose file comes after this one, stays until the next file.
          AND NOT EXISTS (SELECT FROM pg_catalog.pg_depend dependent
                          WHERE dependent.refclassid = 'pg_catalog.pg_proc'::regclass AND dependent.refobjid = p.oid AND dependent.deptype = 'n')
    LOOP
        EXECUTE format('DROP FUNCTION %s', generated.signature);
    END LOOP;
END
$ddd$;

-- Written by the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0.
CREATE OR REPLACE FUNCTION projects.crew_member_project_ids() RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
-- Membership of projects, in form 5, written from the rules 3c9e61bdf91bb526e0134fd25645ae309861d667f3460499e9d462dfdb825b12
SELECT DISTINCT m."ProjectId" FROM "projects"."ProjectCrewMembers" m
WHERE m."SeatId" = (SELECT tenancy.caller_seat()) AND m."StartsAt" <= pg_catalog.now() AND (m."EndsAt" IS NULL OR m."EndsAt" > pg_catalog.now())
$function$;
COMMENT ON FUNCTION projects.crew_member_project_ids() IS 'DDDToolkit access function of ProjectsContext';
REVOKE ALL ON FUNCTION projects.crew_member_project_ids() FROM PUBLIC;

-- Written by the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0.
CREATE OR REPLACE FUNCTION projects.crew_project_ids(text) RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
-- Membership of projects, in form 5, written from the rules 3c9e61bdf91bb526e0134fd25645ae309861d667f3460499e9d462dfdb825b12
SELECT DISTINCT m."ProjectId" FROM "projects"."ProjectCrewMembers" m
WHERE $1 IS NOT NULL AND m."SeatId" = (SELECT tenancy.caller_seat()) AND m."StartsAt" <= pg_catalog.now() AND (m."EndsAt" IS NULL OR m."EndsAt" > pg_catalog.now())
  AND $1 NOT IN ('projects.open', 'projects.owner.change', 'tenancy.settings.manage', 'tenancy.units.manage', 'tenancy.seats.manage', 'tenancy.grants.manage', 'tenancy.roles.manage', 'tenancy.history.view')
  AND EXISTS (SELECT 1 FROM "projects"."ProjectCrewRoleGrants" h
              JOIN "projects"."ProjectRoles" k ON k."Id" = h."RoleId"
              WHERE h."ProjectId" = m."ProjectId" AND h."CrewMemberId" = m."Id" AND h."StartsAt" <= pg_catalog.now() AND (h."EndsAt" IS NULL OR h."EndsAt" > pg_catalog.now())
                AND k."Status" = 'Active' AND $1 = ANY (k."Keys"))
$function$;
COMMENT ON FUNCTION projects.crew_project_ids(text) IS 'DDDToolkit access function of ProjectsContext';
REVOKE ALL ON FUNCTION projects.crew_project_ids(text) FROM PUBLIC;

-- Written by the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0.
CREATE OR REPLACE FUNCTION projects.project_ids_i_see() RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
-- Membership of projects, in form 5, written from the rules 3c9e61bdf91bb526e0134fd25645ae309861d667f3460499e9d462dfdb825b12
SELECT joined.id FROM projects.crew_member_project_ids() AS joined(id)
UNION
SELECT r."Id" FROM "projects"."Projects" r WHERE r."OwnerSeatId" = (SELECT tenancy.caller_seat())
UNION
SELECT r."Id" FROM "projects"."Projects" r WHERE r."UnitId" IN (SELECT reached.place FROM tenancy.units_where_i_hold('projects.view') AS reached(place))
UNION
SELECT r."Id" FROM "projects"."Projects" r WHERE (SELECT auth.role()) = 'ddd_system_in' AND (SELECT auth.jwt() ->> 'scope') IN ('tenancy', 'projects')
$function$;
COMMENT ON FUNCTION projects.project_ids_i_see() IS 'DDDToolkit access function of ProjectsContext';
REVOKE ALL ON FUNCTION projects.project_ids_i_see() FROM PUBLIC;

-- Written by the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0.
CREATE OR REPLACE FUNCTION projects.project_ids_where_i_hold(text) RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
-- Membership of projects, in form 5, written from the rules 3c9e61bdf91bb526e0134fd25645ae309861d667f3460499e9d462dfdb825b12
SELECT holding.id FROM projects.crew_project_ids($1) AS holding(id)
UNION
SELECT joined.id FROM projects.crew_member_project_ids() AS joined(id) WHERE $1 = 'projects.view'
UNION
SELECT r."Id" FROM "projects"."Projects" r WHERE $1 IN ('projects.view', 'projects.edit', 'projects.close', 'projects.crew.manage') AND r."OwnerSeatId" = (SELECT tenancy.caller_seat())
UNION
SELECT r."Id" FROM "projects"."Projects" r WHERE $1 IS NOT NULL AND r."UnitId" IN (SELECT reached.place FROM tenancy.units_where_i_hold($1) AS reached(place))
UNION
SELECT r."Id" FROM "projects"."Projects" r WHERE (SELECT auth.role()) = 'ddd_system_in' AND (SELECT auth.jwt() ->> 'scope') IN ('tenancy', 'projects') AND $1 IS NOT NULL
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
          AND (n.nspname, p.proname) IN (('projects', 'crew_member_project_ids'), ('projects', 'crew_project_ids'), ('projects', 'project_ids_i_see'), ('projects', 'project_ids_where_i_hold'), ('projects', 'projects_name_plannedfrom_planneduntil_column_rule'), ('projects', 'projects_state_column_rule'))
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

ALTER TABLE projects."ProjectRoles" ENABLE ROW LEVEL SECURITY;
ALTER TABLE projects."ProjectRoles" FORCE ROW LEVEL SECURITY;

CREATE POLICY "Seats read the project roles of their tenant (select ~ 6fe4c6d7" ON projects."ProjectRoles" FOR SELECT TO authenticated
    USING ("TenantId" = (SELECT tenancy.caller_tenant()));
COMMENT ON POLICY "Seats read the project roles of their tenant (select ~ 6fe4c6d7" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

-- System work in its tenant (select) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (select) for ddd_system_in" ON projects."ProjectRoles" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (select) for ddd_system_in" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

CREATE POLICY "Role managers change the project roles (insert) for ~ 17f4ed23" ON projects."ProjectRoles" FOR INSERT TO authenticated
    WITH CHECK ((SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')));
COMMENT ON POLICY "Role managers change the project roles (insert) for ~ 17f4ed23" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

-- System work in its tenant (insert) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."ProjectRoles" FOR INSERT TO ddd_system_in
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

CREATE POLICY "Role managers change the project roles (update) for ~ 92d13b87" ON projects."ProjectRoles" FOR UPDATE TO authenticated
    USING ((SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')))
    WITH CHECK ((SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')));
COMMENT ON POLICY "Role managers change the project roles (update) for ~ 92d13b87" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

-- System work in its tenant (update) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (update) for ddd_system_in" ON projects."ProjectRoles" FOR UPDATE TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (update) for ddd_system_in" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

-- System work in its tenant (delete) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (delete) for ddd_system_in" ON projects."ProjectRoles" FOR DELETE TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (delete) for ddd_system_in" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

ALTER TABLE projects."Projects" ENABLE ROW LEVEL SECURITY;
ALTER TABLE projects."Projects" FORCE ROW LEVEL SECURITY;

CREATE POLICY "Seats see the projects they reach (select) for authenticated" ON projects."Projects" FOR SELECT TO authenticated
    USING ("Id" = ANY (ARRAY(SELECT projects.project_ids_i_see())));
COMMENT ON POLICY "Seats see the projects they reach (select) for authenticated" ON projects."Projects" IS 'DDDToolkit row access rule';

-- System work in its tenant (select) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (select) for ddd_system_in" ON projects."Projects" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (select) for ddd_system_in" ON projects."Projects" IS 'DDDToolkit row access rule';

CREATE POLICY "Operators see every project (select) for tenancy_operator" ON projects."Projects" FOR SELECT TO tenancy_operator
    USING (TRUE);
COMMENT ON POLICY "Operators see every project (select) for tenancy_operator" ON projects."Projects" IS 'DDDToolkit row access rule';

CREATE POLICY "Seats open projects where they may (insert) for authenticated" ON projects."Projects" FOR INSERT TO authenticated
    WITH CHECK (("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))) AND (("OwnerSeatId" = (SELECT tenancy.caller_seat())) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change'))))));
COMMENT ON POLICY "Seats open projects where they may (insert) for authenticated" ON projects."Projects" IS 'DDDToolkit row access rule';

-- System work in its tenant (insert) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."Projects" FOR INSERT TO ddd_system_in
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."Projects" IS 'DDDToolkit row access rule';

CREATE POLICY "Seats change the projects they work on (update) for ~ 63b69574" ON projects."Projects" FOR UPDATE TO authenticated
    USING ((((("Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR ("Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close'))))) OR ("Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.crew.manage'))))) OR ("Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.owner.change'))))) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))))
    WITH CHECK ((((("Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR ("Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close'))))) OR ("Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.crew.manage'))))) OR ("Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.owner.change'))))) OR ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))));
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
    WITH CHECK (EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = projects."ProjectCrewMembers"."ProjectId" AND ((((((r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.crew.manage'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR (((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))) AND ((r."OwnerSeatId" = (SELECT tenancy.caller_seat())) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change')))))) AND ddd.written_in_this_transaction(r.xmin)))));
COMMENT ON POLICY "ProjectCrewMembers (insert) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
-- System work in its tenant (insert) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."ProjectCrewMembers" FOR INSERT TO ddd_system_in
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant())));
COMMENT ON POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewMembers (update) for authenticated" ON projects."ProjectCrewMembers" FOR UPDATE TO authenticated
    USING (EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = projects."ProjectCrewMembers"."ProjectId" AND (((((r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.crew.manage'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))))))
    WITH CHECK (EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = projects."ProjectCrewMembers"."ProjectId" AND (((((r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.crew.manage'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))))));
COMMENT ON POLICY "ProjectCrewMembers (update) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
-- System work in its tenant (update) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (update) for ddd_system_in" ON projects."ProjectCrewMembers" FOR UPDATE TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant())))
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = "projects"."ProjectCrewMembers"."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant())));
COMMENT ON POLICY "System work in its tenant (update) for ddd_system_in" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewMembers (delete) for authenticated" ON projects."ProjectCrewMembers" FOR DELETE TO authenticated
    USING (EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = projects."ProjectCrewMembers"."ProjectId" AND (((((r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.crew.manage'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))))));
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
    WITH CHECK (EXISTS (SELECT 1 FROM projects."ProjectCrewMembers" p1 WHERE p1."ProjectId" = projects."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = projects."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = p1."ProjectId" AND ((((((r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.crew.manage'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))) OR (((r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))) AND ((r."OwnerSeatId" = (SELECT tenancy.caller_seat())) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.owner.change')))))) AND ddd.written_in_this_transaction(r.xmin))))));
COMMENT ON POLICY "ProjectCrewRoleGrants (insert) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
-- System work in its tenant (insert) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."ProjectCrewRoleGrants" FOR INSERT TO ddd_system_in
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))));
COMMENT ON POLICY "System work in its tenant (insert) for ddd_system_in" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewRoleGrants (update) for authenticated" ON projects."ProjectCrewRoleGrants" FOR UPDATE TO authenticated
    USING (EXISTS (SELECT 1 FROM projects."ProjectCrewMembers" p1 WHERE p1."ProjectId" = projects."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = projects."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = p1."ProjectId" AND (((((r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.crew.manage'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))))))
    WITH CHECK (EXISTS (SELECT 1 FROM projects."ProjectCrewMembers" p1 WHERE p1."ProjectId" = projects."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = projects."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = p1."ProjectId" AND (((((r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.crew.manage'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))))));
COMMENT ON POLICY "ProjectCrewRoleGrants (update) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
-- System work in its tenant (update) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (update) for ddd_system_in" ON projects."ProjectCrewRoleGrants" FOR UPDATE TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))))
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))));
COMMENT ON POLICY "System work in its tenant (update) for ddd_system_in" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
CREATE POLICY "ProjectCrewRoleGrants (delete) for authenticated" ON projects."ProjectCrewRoleGrants" FOR DELETE TO authenticated
    USING (EXISTS (SELECT 1 FROM projects."ProjectCrewMembers" p1 WHERE p1."ProjectId" = projects."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = projects."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM projects."Projects" r WHERE r."Id" = p1."ProjectId" AND (((((r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.crew.manage'))))) OR (r."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.owner.change'))))) OR (r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open'))))))));
COMMENT ON POLICY "ProjectCrewRoleGrants (delete) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';
-- System work in its tenant (delete) for ddd_system_in asks the policy 'System work in its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0.
CREATE POLICY "System work in its tenant (delete) for ddd_system_in" ON projects."ProjectCrewRoleGrants" FOR DELETE TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "projects"."ProjectCrewMembers" p1 WHERE p1."ProjectId" = "projects"."ProjectCrewRoleGrants"."ProjectId" AND p1."Id" = "projects"."ProjectCrewRoleGrants"."CrewMemberId" AND EXISTS (SELECT 1 FROM "projects"."Projects" r WHERE r."Id" = p1."ProjectId" AND r."TenantId" = (SELECT tenancy.system_tenant()))));
COMMENT ON POLICY "System work in its tenant (delete) for ddd_system_in" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Members change with the key (insert) for authenticated is the policy 'Members change with the key' of the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Members change with the key (insert) for authenticated" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR INSERT TO authenticated
    WITH CHECK ("ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.crew.manage') AS held(id)) OR "ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.owner.change') AS held(id)));
COMMENT ON POLICY "Members change with the key (insert) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- Members change with the key (update) for authenticated is the policy 'Members change with the key' of the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Members change with the key (update) for authenticated" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR UPDATE TO authenticated
    USING ("ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.crew.manage') AS held(id)) OR "ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.owner.change') AS held(id)))
    WITH CHECK ("ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.crew.manage') AS held(id)) OR "ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.owner.change') AS held(id)));
COMMENT ON POLICY "Members change with the key (update) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- Members change with the key (delete) for authenticated is the policy 'Members change with the key' of the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Members change with the key (delete) for authenticated" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR DELETE TO authenticated
    USING ("ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.crew.manage') AS held(id)) OR "ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.owner.change') AS held(id)));
COMMENT ON POLICY "Members change with the key (delete) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

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

-- Crews hold seats of their tenant (insert) for authenticated is the policy 'Crews hold seats of their tenant' of the row access contribution Examples.Tenancy.Projects.Infrastructure.Access.CrewSeatsOfTheProjectsTenant in Examples.Tenancy.Projects.Infrastructure 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Crews hold seats of their tenant (insert) for authenticated" ON projects."ProjectCrewMembers" AS RESTRICTIVE FOR INSERT TO authenticated
    WITH CHECK (EXISTS (SELECT 1 FROM tenancy.tenant_seats() s JOIN "projects"."Projects" r ON r."TenantId" = s."TenantId" WHERE s."Id" = "projects"."ProjectCrewMembers"."SeatId" AND r."Id" = "projects"."ProjectCrewMembers"."ProjectId"));
COMMENT ON POLICY "Crews hold seats of their tenant (insert) for authenticated" ON projects."ProjectCrewMembers" IS 'DDDToolkit row access rule';

-- Members change with the key (insert) for authenticated is the policy 'Members change with the key' of the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Members change with the key (insert) for authenticated" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR INSERT TO authenticated
    WITH CHECK ("ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.crew.manage') AS held(id)) OR "ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.owner.change') AS held(id)));
COMMENT ON POLICY "Members change with the key (insert) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Members change with the key (update) for authenticated is the policy 'Members change with the key' of the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Members change with the key (update) for authenticated" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR UPDATE TO authenticated
    USING ("ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.crew.manage') AS held(id)) OR "ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.owner.change') AS held(id)))
    WITH CHECK ("ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.crew.manage') AS held(id)) OR "ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.owner.change') AS held(id)));
COMMENT ON POLICY "Members change with the key (update) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Members change with the key (delete) for authenticated is the policy 'Members change with the key' of the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Members change with the key (delete) for authenticated" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR DELETE TO authenticated
    USING ("ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.crew.manage') AS held(id)) OR "ProjectId" IN (SELECT held.id FROM projects.project_ids_where_i_hold('projects.owner.change') AS held(id)));
COMMENT ON POLICY "Members change with the key (delete) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Members hold roles the caller sees (insert) for authenticated is the policy 'Members hold roles the caller sees' of the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Members hold roles the caller sees (insert) for authenticated" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR INSERT TO authenticated
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."ProjectRoles" k WHERE k."Id" = "projects"."ProjectCrewRoleGrants"."RoleId"));
COMMENT ON POLICY "Members hold roles the caller sees (insert) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

-- Members hold roles the caller sees (update) for authenticated is the policy 'Members hold roles the caller sees' of the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Members hold roles the caller sees (update) for authenticated" ON projects."ProjectCrewRoleGrants" AS RESTRICTIVE FOR UPDATE TO authenticated
    USING (true)
    WITH CHECK (EXISTS (SELECT 1 FROM "projects"."ProjectRoles" k WHERE k."Id" = "projects"."ProjectCrewRoleGrants"."RoleId"));
COMMENT ON POLICY "Members hold roles the caller sees (update) for authenticated" ON projects."ProjectCrewRoleGrants" IS 'DDDToolkit row access rule';

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
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON projects."ProjectRoles" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for authenticated is the policy 'Kept to its tenant' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for authenticated" ON projects."ProjectRoles" AS RESTRICTIVE FOR ALL TO authenticated
    USING ("TenantId" = (SELECT tenancy.caller_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.caller_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for authenticated" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON projects."ProjectRoles" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON projects."ProjectRoles" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON projects."ProjectRoles" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON projects."ProjectRoles" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution Examples.Tenancy.Catalogue.SampleTenancyContribution in Examples.Tenancy.Catalogue 1.0.0, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON projects."ProjectRoles" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON projects."ProjectRoles" IS 'DDDToolkit row access rule';

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

-- The column rules: a policy cannot see which column a statement changes, so a trigger before an update of
-- the columns a rule holds asks it of the row as it was and as it is about to be. The policies for UPDATE
-- above still decide which rows a caller changes at all.

-- A change of "Name", "PlannedFrom" or "PlannedUntil" of projects."Projects" is held to the column rule 'Name and plan change with the edit key'.
CREATE OR REPLACE FUNCTION projects.projects_name_plannedfrom_planneduntil_column_rule() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
BEGIN
    -- The roles a caller's statement runs as are held; the application's own work and the tables' owner are not.
    IF CURRENT_USER = 'authenticated' THEN
        IF (OLD."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) IS NOT TRUE OR (OLD."Id" IS DISTINCT FROM NEW."Id" AND (NEW."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) IS NOT TRUE) THEN
            RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'projects_name_plannedfrom_planneduntil_column_rule', HINT = 'ddd:access.refused', MESSAGE = 'The column rule ''Name and plan change with the edit key'' does not let this caller change "Name", "PlannedFrom" or "PlannedUntil" of projects."Projects".';
        END IF;
    ELSIF CURRENT_USER IN ('anon', 'tenancy_operator') THEN
        RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'projects_name_plannedfrom_planneduntil_column_rule', HINT = 'ddd:access.refused', MESSAGE = 'No column rule is for this caller''s role, so it may not change "Name", "PlannedFrom" or "PlannedUntil" of projects."Projects".';
    END IF;
    RETURN NEW;
END
$body$;
COMMENT ON FUNCTION projects.projects_name_plannedfrom_planneduntil_column_rule() IS 'DDDToolkit access function of ProjectsContext';
REVOKE ALL ON FUNCTION projects.projects_name_plannedfrom_planneduntil_column_rule() FROM PUBLIC;
DROP TRIGGER IF EXISTS projects_name_plannedfrom_planneduntil_column_rule ON projects."Projects";
CREATE TRIGGER projects_name_plannedfrom_planneduntil_column_rule BEFORE UPDATE OF "Name", "PlannedFrom", "PlannedUntil" ON projects."Projects"
    FOR EACH ROW WHEN ((OLD."Name", OLD."PlannedFrom", OLD."PlannedUntil") IS DISTINCT FROM (NEW."Name", NEW."PlannedFrom", NEW."PlannedUntil"))
    EXECUTE FUNCTION projects.projects_name_plannedfrom_planneduntil_column_rule();
COMMENT ON TRIGGER projects_name_plannedfrom_planneduntil_column_rule ON projects."Projects" IS 'DDDToolkit column rule';

-- A change of "State" of projects."Projects" is held to the column rule 'State changes with the close key'.
CREATE OR REPLACE FUNCTION projects.projects_state_column_rule() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
BEGIN
    -- The roles a caller's statement runs as are held; the application's own work and the tables' owner are not.
    IF CURRENT_USER = 'authenticated' THEN
        IF (OLD."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close')))) IS NOT TRUE OR (OLD."Id" IS DISTINCT FROM NEW."Id" AND (NEW."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.close')))) IS NOT TRUE) THEN
            RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'projects_state_column_rule', HINT = 'ddd:access.refused', MESSAGE = 'The column rule ''State changes with the close key'' does not let this caller change "State" of projects."Projects".';
        END IF;
    ELSIF CURRENT_USER IN ('anon', 'tenancy_operator') THEN
        RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'projects_state_column_rule', HINT = 'ddd:access.refused', MESSAGE = 'No column rule is for this caller''s role, so it may not change "State" of projects."Projects".';
    END IF;
    RETURN NEW;
END
$body$;
COMMENT ON FUNCTION projects.projects_state_column_rule() IS 'DDDToolkit access function of ProjectsContext';
REVOKE ALL ON FUNCTION projects.projects_state_column_rule() FROM PUBLIC;
DROP TRIGGER IF EXISTS projects_state_column_rule ON projects."Projects";
CREATE TRIGGER projects_state_column_rule BEFORE UPDATE OF "State" ON projects."Projects"
    FOR EACH ROW WHEN (OLD."State" IS DISTINCT FROM NEW."State")
    EXECUTE FUNCTION projects.projects_state_column_rule();
COMMENT ON TRIGGER projects_state_column_rule ON projects."Projects" IS 'DDDToolkit column rule';

-- Privileges, from the policies above: what a table gave the roles of this file before is taken back, and a
-- role then gets the commands a permissive policy allows it, and no more. UPDATE is granted on the columns
-- that may change.
GRANT USAGE ON SCHEMA projects TO authenticated, ddd_system, ddd_system_in, tenancy_operator;

REVOKE ALL ON TABLE projects."ProjectCrewMembers" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT, DELETE ON TABLE projects."ProjectCrewMembers" TO authenticated;
GRANT UPDATE ("AddedBy", "EndsAt", "StartsAt") ON TABLE projects."ProjectCrewMembers" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE projects."ProjectCrewMembers" TO ddd_system_in;
GRANT UPDATE ("AddedBy", "EndsAt", "StartsAt") ON TABLE projects."ProjectCrewMembers" TO ddd_system_in;
GRANT SELECT ON TABLE projects."ProjectCrewMembers" TO tenancy_operator;

REVOKE ALL ON TABLE projects."ProjectCrewRoleGrants" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT, DELETE ON TABLE projects."ProjectCrewRoleGrants" TO authenticated;
GRANT UPDATE ("EndsAt", "GivenBy", "StartsAt") ON TABLE projects."ProjectCrewRoleGrants" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE projects."ProjectCrewRoleGrants" TO ddd_system_in;
GRANT UPDATE ("EndsAt", "GivenBy", "StartsAt") ON TABLE projects."ProjectCrewRoleGrants" TO ddd_system_in;
GRANT SELECT ON TABLE projects."ProjectCrewRoleGrants" TO tenancy_operator;

REVOKE ALL ON TABLE projects."ProjectRoles" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT ON TABLE projects."ProjectRoles" TO authenticated;
GRANT UPDATE ("Description", "Keys", "Name", "Status", "Version") ON TABLE projects."ProjectRoles" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE projects."ProjectRoles" TO ddd_system_in;
GRANT UPDATE ("Description", "Keys", "Name", "Status", "Version") ON TABLE projects."ProjectRoles" TO ddd_system_in;

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

-- Written by the row access contribution Examples.Tenancy.Catalogue.ProjectMembershipFunctions in Examples.Tenancy.Catalogue 1.0.0.
CREATE OR REPLACE FUNCTION "projects".projects_owner_stays() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
BEGIN
    -- Membership of projects, in form 5, written from the rules 3c9e61bdf91bb526e0134fd25645ae309861d667f3460499e9d462dfdb825b12
    -- The callers the rules name are held to this. The application's own work and the tables' owner are not.
    IF CURRENT_USER IN ('authenticated')
       AND NOT EXISTS (SELECT 1 FROM projects.project_ids_where_i_hold('projects.owner.change') AS held(id) WHERE held.id = OLD."Id") THEN
        RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'projects_owner_stays', HINT = 'ddd:access.refused', MESSAGE = 'The owner of a row of "projects"."Projects" is changed by a caller that holds projects.owner.change on it.';
    END IF;
    RETURN NEW;
END
$body$;
REVOKE ALL ON FUNCTION "projects".projects_owner_stays() FROM PUBLIC;
DROP TRIGGER IF EXISTS projects_owner_stays ON "projects"."Projects";
CREATE TRIGGER projects_owner_stays BEFORE UPDATE OF "OwnerSeatId" ON "projects"."Projects"
    FOR EACH ROW WHEN (OLD."OwnerSeatId" IS DISTINCT FROM NEW."OwnerSeatId")
    EXECUTE FUNCTION "projects".projects_owner_stays();
CREATE OR REPLACE FUNCTION "projects".projects_owner_role_stays() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
BEGIN
    -- Membership of projects, in form 5, written from the rules 3c9e61bdf91bb526e0134fd25645ae309861d667f3460499e9d462dfdb825b12
    -- Whoever writes the row: an owner is named into this role, so it stays in use.
    IF NEW."MadeFrom" = 'crew-lead' AND NEW."Status" IS DISTINCT FROM 'Active' THEN
        RAISE EXCEPTION USING ERRCODE = 'check_violation', MESSAGE = 'A row of "projects"."ProjectRoles" made from the starter role ''crew-lead'' is the role every owner of a Project holds, and is not archived.';
    END IF;
    RETURN NEW;
END
$body$;
REVOKE ALL ON FUNCTION "projects".projects_owner_role_stays() FROM PUBLIC;
DROP TRIGGER IF EXISTS projects_owner_role_stays ON "projects"."ProjectRoles";
CREATE TRIGGER projects_owner_role_stays BEFORE INSERT OR UPDATE OF "MadeFrom", "Status" ON "projects"."ProjectRoles"
    FOR EACH ROW EXECUTE FUNCTION "projects".projects_owner_role_stays();

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
REVOKE ALL ON FUNCTION "projects".attribution_matches_caller() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_attribution_matches_caller ON "projects"."Projects";
CREATE TRIGGER tenancy_attribution_matches_caller BEFORE INSERT OR UPDATE ON "projects"."Projects"
    FOR EACH ROW EXECUTE FUNCTION "projects".attribution_matches_caller('CreatedBySeat', 'CreatedByKind', 'CreatedByIdentity', 'ChangedBySeat', 'ChangedByKind', 'ChangedByIdentity');

-- Written by the row access contribution Examples.Tenancy.Projects.Infrastructure.Access.UnitChangesWithItsKeys in Examples.Tenancy.Projects.Infrastructure 1.0.0.
DROP TRIGGER IF EXISTS projects_unit_and_owner_are_held ON "projects"."Projects";
DROP FUNCTION IF EXISTS "projects".unit_and_owner_are_held();
CREATE OR REPLACE FUNCTION "projects".unit_is_held() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
BEGIN
    -- Only a signed-in user's statement is held: system work and the tables' owner act for the application.
    IF CURRENT_USER <> 'authenticated' THEN
        RETURN NEW;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM tenancy.tenant_units() u WHERE u."Id" = NEW."UnitId" AND u."TenantId" = NEW."TenantId") THEN
        RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'projects_unit_is_held', HINT = 'ddd:access.refused', MESSAGE = 'A project is at a unit of its own tenant.';
    END IF;
    IF NOT (OLD."Id" = ANY (ARRAY(SELECT projects.project_ids_where_i_hold('projects.edit')))) OR NOT (NEW."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('projects.open')))) THEN
        RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'projects_unit_is_held', HINT = 'ddd:access.refused', MESSAGE = 'A project is moved by a seat that may edit it and may open projects at the unit it moves to.';
    END IF;
    RETURN NEW;
END
$body$;
REVOKE ALL ON FUNCTION "projects".unit_is_held() FROM PUBLIC;
DROP TRIGGER IF EXISTS projects_unit_is_held ON "projects"."Projects";
CREATE TRIGGER projects_unit_is_held BEFORE UPDATE OF "UnitId" ON "projects"."Projects"
    FOR EACH ROW WHEN (OLD."UnitId" IS DISTINCT FROM NEW."UnitId")
    EXECUTE FUNCTION "projects".unit_is_held();

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
