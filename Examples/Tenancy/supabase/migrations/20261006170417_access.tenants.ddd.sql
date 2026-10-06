-- Written by DDDToolkit from the row access rules of TenantsContext.
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
        WHERE d.description = 'DDDToolkit access function of TenantsContext'
          AND (n.nspname, p.proname) NOT IN (('tenancy', 'caller_rights'), ('tenancy', 'caller_seat'), ('tenancy', 'caller_tenant'), ('tenancy', 'holds_key'), ('tenancy', 'holds_key_in_tenant'), ('tenancy', 'holds_tenant_wide'), ('tenancy', 'identity_tenants'), ('tenancy', 'invitation_of_digest'), ('tenancy', 'key_is_live'), ('tenancy', 'manages_access'), ('tenancy', 'pack_keys'), ('tenancy', 'readable_units'), ('tenancy', 'rewrite_tenant_rights'), ('tenancy', 'rights_a_move_changes'), ('tenancy', 'role_keys_in_use'), ('tenancy', 'roles_with_key'), ('tenancy', 'roles_with_key_in_tenant'), ('tenancy', 'seat_in_tenant'), ('tenancy', 'seated_in_tenant'), ('tenancy', 'seats_holding_at'), ('tenancy', 'seats_of_identity'), ('tenancy', 'system_tenant'), ('tenancy', 'tenant_administrators'), ('tenancy', 'tenant_placements'), ('tenancy', 'tenant_roles'), ('tenancy', 'tenant_seats'), ('tenancy', 'tenant_unit_paths'), ('tenancy', 'tenant_units'), ('tenancy', 'tenants_to_sweep'), ('tenancy', 'unit_parent'), ('tenancy', 'units_where_i_hold'), ('tenancy', 'units_where_i_hold_in_tenant'))
          -- A function a policy still asks, of a module whose file comes after this one, stays until the next file.
          AND NOT EXISTS (SELECT FROM pg_catalog.pg_depend dependent
                          WHERE dependent.refclassid = 'pg_catalog.pg_proc'::regclass AND dependent.refobjid = p.oid AND dependent.deptype = 'n')
    LOOP
        EXECUTE format('DROP FUNCTION %s', generated.signature);
    END LOOP;
END
$ddd$;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.caller_rights() RETURNS TABLE ("TenantId" uuid, "SeatId" uuid, "UnitId" uuid, "RoleId" uuid, "Key" text, "StartsAt" timestamp with time zone, "EndsAt" timestamp with time zone)
    LANGUAGE sql STABLE AS $function$
SELECT t."TenantId", t."SeatId", t."UnitId", t."RoleId", t."Key"::pg_catalog.text, t."StartsAt", t."EndsAt"
FROM "tenancy"."SeatRights" t
$function$;
COMMENT ON FUNCTION tenancy.caller_rights() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.caller_rights() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.identity_tenants() RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT DISTINCT s."TenantId" FROM "tenancy"."Seats" s WHERE s."Identity" = (SELECT auth.uid())
$function$;
COMMENT ON FUNCTION tenancy.identity_tenants() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.identity_tenants() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.invitation_of_digest(digest bytea) RETURNS TABLE ("TenantId" uuid, "InvitationId" uuid, "IssuedBy" uuid)
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT i."TenantId", i."Id", i."IssuedBy" FROM "tenancy"."InvitationDigests" d
JOIN "tenancy"."Invitations" i ON i."Id" = d."InvitationId" AND i."TenantId" = d."TenantId"
WHERE d."Digest" = $1 AND (SELECT auth.jwt() ->> 'scope') = 'tenancy'
$function$;
COMMENT ON FUNCTION tenancy.invitation_of_digest(digest bytea) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.invitation_of_digest(digest bytea) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.key_is_live(key text) RETURNS boolean
    LANGUAGE sql IMMUTABLE SET search_path = '' AS $function$
SELECT $1 IN ('inspections.record', 'projects.close', 'projects.crew.manage', 'projects.edit', 'projects.open', 'projects.owner.change', 'projects.view', 'tenancy.grants.manage', 'tenancy.history.view', 'tenancy.roles.manage', 'tenancy.seats.manage', 'tenancy.settings.manage', 'tenancy.units.manage')
$function$;
COMMENT ON FUNCTION tenancy.key_is_live(key text) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.key_is_live(key text) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.manages_access(key text) RETURNS boolean
    LANGUAGE sql IMMUTABLE SET search_path = '' AS $function$
SELECT $1 IN ('projects.crew.manage', 'projects.owner.change', 'tenancy.grants.manage', 'tenancy.roles.manage', 'tenancy.seats.manage', 'tenancy.settings.manage', 'tenancy.units.manage')
$function$;
COMMENT ON FUNCTION tenancy.manages_access(key text) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.manages_access(key text) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.pack_keys(pack text) RETURNS text[]
    LANGUAGE sql IMMUTABLE SET search_path = '' AS $function$
SELECT CASE $1 WHEN 'access-admin' THEN ARRAY['projects.crew.manage', 'projects.owner.change', 'projects.view', 'tenancy.grants.manage', 'tenancy.history.view', 'tenancy.roles.manage', 'tenancy.seats.manage', 'tenancy.settings.manage', 'tenancy.units.manage']::pg_catalog.text[] WHEN 'area-manager' THEN ARRAY['inspections.record', 'projects.close', 'projects.crew.manage', 'projects.edit', 'projects.open', 'projects.owner.change', 'projects.view', 'tenancy.grants.manage', 'tenancy.seats.manage', 'tenancy.units.manage']::pg_catalog.text[] WHEN 'crew-lead' THEN ARRAY['inspections.record', 'projects.close', 'projects.crew.manage', 'projects.edit', 'projects.view']::pg_catalog.text[] WHEN 'observer' THEN ARRAY['projects.view']::pg_catalog.text[] WHEN 'people-office' THEN ARRAY['tenancy.grants.manage']::pg_catalog.text[] WHEN 'surveyor' THEN ARRAY['inspections.record', 'projects.view']::pg_catalog.text[] WHEN 'tenant-admin' THEN ARRAY['inspections.record', 'projects.close', 'projects.crew.manage', 'projects.edit', 'projects.open', 'projects.owner.change', 'projects.view', 'tenancy.grants.manage', 'tenancy.history.view', 'tenancy.roles.manage', 'tenancy.seats.manage', 'tenancy.settings.manage', 'tenancy.units.manage']::pg_catalog.text[] END
$function$;
COMMENT ON FUNCTION tenancy.pack_keys(pack text) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.pack_keys(pack text) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.role_keys_in_use() RETURNS SETOF text
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT DISTINCT held.k::pg_catalog.text FROM "tenancy"."Roles" r
CROSS JOIN LATERAL pg_catalog.unnest(r."Keys") AS held(k)
WHERE (SELECT auth.jwt() ->> 'scope') = 'tenancy'
$function$;
COMMENT ON FUNCTION tenancy.role_keys_in_use() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.role_keys_in_use() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.seat_in_tenant(tenant uuid) RETURNS uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT s."Id" FROM "tenancy"."Seats" s
JOIN "tenancy"."Tenants" t ON t."Id" = s."TenantId"
WHERE s."Identity" = (SELECT auth.uid())
  AND s."TenantId" = $1
  AND s."Status" = 'Active' AND t."Status" = 'Active'
$function$;
COMMENT ON FUNCTION tenancy.seat_in_tenant(tenant uuid) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.seat_in_tenant(tenant uuid) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.holds_key_in_tenant(tenant uuid, key text) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT EXISTS (SELECT 1 FROM "tenancy"."SeatRights" r
               WHERE r."SeatId" = (SELECT tenancy.seat_in_tenant($1)) AND r."Key" = $2 AND tenancy.key_is_live($2) AND r."StartsAt" <= pg_catalog.now() AND (r."EndsAt" IS NULL OR r."EndsAt" > pg_catalog.now()))
$function$;
COMMENT ON FUNCTION tenancy.holds_key_in_tenant(tenant uuid, key text) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.holds_key_in_tenant(tenant uuid, key text) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.roles_with_key_in_tenant(tenant uuid, key text) RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT r."Id" FROM "tenancy"."Roles" r
WHERE r."TenantId" = $1 AND r."Status" = 'Active'
  AND $2 = ANY (r."Keys") AND tenancy.key_is_live($2)
  AND (SELECT tenancy.seat_in_tenant($1)) IS NOT NULL
$function$;
COMMENT ON FUNCTION tenancy.roles_with_key_in_tenant(tenant uuid, key text) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.roles_with_key_in_tenant(tenant uuid, key text) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.seated_in_tenant(tenant uuid) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT tenancy.seat_in_tenant($1) IS NOT NULL
$function$;
COMMENT ON FUNCTION tenancy.seated_in_tenant(tenant uuid) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.seated_in_tenant(tenant uuid) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.seats_of_identity(identity uuid) RETURNS TABLE ("TenantId" uuid, "SeatId" uuid)
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT s."TenantId", s."Id" FROM "tenancy"."Seats" s
WHERE s."Identity" = $1 AND (SELECT auth.jwt() ->> 'scope') = 'tenancy'
$function$;
COMMENT ON FUNCTION tenancy.seats_of_identity(identity uuid) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.seats_of_identity(identity uuid) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.system_tenant() RETURNS uuid
    LANGUAGE sql STABLE SET search_path = '' AS $function$
SELECT nullif(pg_catalog.current_setting('tenancy.caller_tenant', true), '')::pg_catalog.uuid
$function$;
COMMENT ON FUNCTION tenancy.system_tenant() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.system_tenant() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.caller_seat() RETURNS uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT s."Id" FROM "tenancy"."Seats" s
JOIN "tenancy"."Tenants" t ON t."Id" = s."TenantId"
WHERE s."Identity" = (SELECT auth.uid())
  AND s."TenantId" = (SELECT tenancy.system_tenant())
  AND s."Status" = 'Active' AND t."Status" = 'Active'
$function$;
COMMENT ON FUNCTION tenancy.caller_seat() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.caller_seat() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.caller_tenant() RETURNS uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT s."TenantId" FROM "tenancy"."Seats" s WHERE s."Id" = (SELECT tenancy.caller_seat())
$function$;
COMMENT ON FUNCTION tenancy.caller_tenant() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.caller_tenant() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.holds_key(key text) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT EXISTS (SELECT 1 FROM "tenancy"."SeatRights" r
               WHERE r."SeatId" = (SELECT tenancy.caller_seat()) AND r."Key" = $1 AND tenancy.key_is_live($1) AND r."StartsAt" <= pg_catalog.now() AND (r."EndsAt" IS NULL OR r."EndsAt" > pg_catalog.now()))
$function$;
COMMENT ON FUNCTION tenancy.holds_key(key text) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.holds_key(key text) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.holds_tenant_wide(key text) RETURNS boolean
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT EXISTS (SELECT 1 FROM "tenancy"."SeatRights" r
               JOIN "tenancy"."OrganizationUnits" u ON u."Id" = r."UnitId" AND u."TenantId" = r."TenantId"
               WHERE r."SeatId" = (SELECT tenancy.caller_seat()) AND r."Key" = $1 AND tenancy.key_is_live($1) AND r."StartsAt" <= pg_catalog.now() AND (r."EndsAt" IS NULL OR r."EndsAt" > pg_catalog.now())
                 AND u."ParentId" IS NULL)
$function$;
COMMENT ON FUNCTION tenancy.holds_tenant_wide(key text) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.holds_tenant_wide(key text) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.readable_units() RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT DISTINCT p."DescendantId" FROM "tenancy"."SeatPlacements" pl
JOIN "tenancy"."OrganizationUnitPaths" p ON p."AncestorId" = pl."UnitId" AND p."TenantId" = pl."TenantId"
WHERE pl."SeatId" = (SELECT tenancy.caller_seat()) AND p."TenantId" = (SELECT tenancy.caller_tenant())
$function$;
COMMENT ON FUNCTION tenancy.readable_units() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.readable_units() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.rewrite_tenant_rights() RETURNS integer
    LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path = '' AS $function$
WITH removed AS (
    DELETE FROM "tenancy"."SeatRights" r
    WHERE r."TenantId" = (SELECT tenancy.system_tenant()) AND (SELECT auth.jwt() ->> 'scope') = 'tenancy'
      AND NOT EXISTS (SELECT 1 FROM (
            SELECT s."TenantId" AS tenant, g."SeatId" AS seat, g."UnitId" AS unit, g."RoleId" AS role, held.k AS key, g."StartsAt" AS starts, g."EndsAt" AS ends
            FROM "tenancy"."SeatRoleGrants" g
            JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId" AND s."Status" = 'Active'
            JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId" AND ro."TenantId" = s."TenantId" AND ro."Status" = 'Active'
            CROSS JOIN LATERAL (SELECT DISTINCT each.k FROM pg_catalog.unnest(ro."Keys") AS each(k)) held
            WHERE s."TenantId" = (SELECT tenancy.system_tenant()) AND (SELECT auth.jwt() ->> 'scope') = 'tenancy' AND tenancy.key_is_live(held.k)) d
        WHERE d.seat = r."SeatId" AND d.unit = r."UnitId" AND d.role = r."RoleId" AND d.key = r."Key")
    RETURNING 1),
written AS (
    INSERT INTO "tenancy"."SeatRights" AS r ("TenantId", "SeatId", "UnitId", "RoleId", "Key", "StartsAt", "EndsAt")
    SELECT d.tenant, d.seat, d.unit, d.role, d.key, d.starts, d.ends FROM (
        SELECT s."TenantId" AS tenant, g."SeatId" AS seat, g."UnitId" AS unit, g."RoleId" AS role, held.k AS key, g."StartsAt" AS starts, g."EndsAt" AS ends
        FROM "tenancy"."SeatRoleGrants" g
        JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId" AND s."Status" = 'Active'
        JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId" AND ro."TenantId" = s."TenantId" AND ro."Status" = 'Active'
        CROSS JOIN LATERAL (SELECT DISTINCT each.k FROM pg_catalog.unnest(ro."Keys") AS each(k)) held
        WHERE s."TenantId" = (SELECT tenancy.system_tenant()) AND (SELECT auth.jwt() ->> 'scope') = 'tenancy' AND tenancy.key_is_live(held.k)) d
    ON CONFLICT ("SeatId", "UnitId", "RoleId", "Key") DO UPDATE SET "TenantId" = EXCLUDED."TenantId", "StartsAt" = EXCLUDED."StartsAt", "EndsAt" = EXCLUDED."EndsAt"
    WHERE (r."TenantId", r."StartsAt", r."EndsAt") IS DISTINCT FROM (EXCLUDED."TenantId", EXCLUDED."StartsAt", EXCLUDED."EndsAt")
    RETURNING 1)
SELECT ((SELECT pg_catalog.count(*) FROM removed) + (SELECT pg_catalog.count(*) FROM written))::pg_catalog.int4
$function$;
COMMENT ON FUNCTION tenancy.rewrite_tenant_rights() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.rewrite_tenant_rights() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.roles_with_key(key text) RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT r."Id" FROM "tenancy"."Roles" r
WHERE r."TenantId" = (SELECT tenancy.caller_tenant()) AND r."Status" = 'Active'
  AND $1 = ANY (r."Keys") AND tenancy.key_is_live($1)
$function$;
COMMENT ON FUNCTION tenancy.roles_with_key(key text) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.roles_with_key(key text) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.tenant_placements() RETURNS TABLE ("SeatId" uuid, "UnitId" uuid, "IsPrimary" boolean, "TenantId" uuid)
    LANGUAGE sql STABLE AS $function$
SELECT t."SeatId", t."UnitId", t."IsPrimary", t."TenantId"
FROM "tenancy"."SeatPlacements" t
$function$;
COMMENT ON FUNCTION tenancy.tenant_placements() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.tenant_placements() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.tenant_roles() RETURNS TABLE ("Id" uuid, "TenantId" uuid, "FromPack" text, "Status" text, "Keys" text[])
    LANGUAGE sql STABLE AS $function$
SELECT t."Id", t."TenantId", t."FromPack"::pg_catalog.text, CASE t."Status" WHEN 'Active' THEN 'Active' WHEN 'Archived' THEN 'Archived' END, t."Keys"
FROM "tenancy"."Roles" t
$function$;
COMMENT ON FUNCTION tenancy.tenant_roles() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.tenant_roles() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.tenant_seats() RETURNS TABLE ("Id" uuid, "TenantId" uuid, "Status" text)
    LANGUAGE sql STABLE AS $function$
SELECT t."Id", t."TenantId", CASE t."Status" WHEN 'Active' THEN 'Active' WHEN 'Suspended' THEN 'Suspended' WHEN 'Deactivated' THEN 'Deactivated' END
FROM "tenancy"."Seats" t
$function$;
COMMENT ON FUNCTION tenancy.tenant_seats() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.tenant_seats() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.tenant_unit_paths() RETURNS TABLE ("TenantId" uuid, "AncestorId" uuid, "DescendantId" uuid, "Distance" integer)
    LANGUAGE sql STABLE AS $function$
SELECT t."TenantId", t."AncestorId", t."DescendantId", t."Distance"
FROM "tenancy"."OrganizationUnitPaths" t
$function$;
COMMENT ON FUNCTION tenancy.tenant_unit_paths() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.tenant_unit_paths() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.tenant_units() RETURNS TABLE ("Id" uuid, "TenantId" uuid, "ParentId" uuid, "Status" text)
    LANGUAGE sql STABLE AS $function$
SELECT t."Id", t."TenantId", t."ParentId", CASE t."Status" WHEN 'Active' THEN 'Active' WHEN 'Archived' THEN 'Archived' END
FROM "tenancy"."OrganizationUnits" t
$function$;
COMMENT ON FUNCTION tenancy.tenant_units() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.tenant_units() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.tenants_to_sweep() RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT t."Id" FROM "tenancy"."Tenants" t
WHERE t."Status" IN ('Active', 'Suspended')
$function$;
COMMENT ON FUNCTION tenancy.tenants_to_sweep() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.tenants_to_sweep() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.unit_parent(unit uuid) RETURNS uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT u."ParentId" FROM "tenancy"."OrganizationUnits" u WHERE u."Id" = $1 AND u."TenantId" = (SELECT tenancy.caller_tenant())
$function$;
COMMENT ON FUNCTION tenancy.unit_parent(unit uuid) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.unit_parent(unit uuid) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.units_where_i_hold(key text) RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT DISTINCT p."DescendantId" FROM "tenancy"."SeatRights" r
JOIN "tenancy"."OrganizationUnitPaths" p ON p."AncestorId" = r."UnitId" AND p."TenantId" = r."TenantId"
WHERE r."SeatId" = (SELECT tenancy.caller_seat()) AND r."Key" = $1 AND tenancy.key_is_live($1)
  AND r."StartsAt" <= pg_catalog.now() AND (r."EndsAt" IS NULL OR r."EndsAt" > pg_catalog.now())
$function$;
COMMENT ON FUNCTION tenancy.units_where_i_hold(key text) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.units_where_i_hold(key text) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.rights_a_move_changes(parent uuid, new_parent uuid) RETURNS TABLE ("UnitId" uuid, "Key" text, "EndsAt" timestamp with time zone, "Parent" uuid, "OfCaller" boolean)
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT r."UnitId", r."Key"::pg_catalog.text, r."EndsAt", p."DescendantId",
       coalesce(r."SeatId" = (SELECT tenancy.caller_seat()), false)
FROM "tenancy"."SeatRights" r
JOIN "tenancy"."OrganizationUnitPaths" p ON p."AncestorId" = r."UnitId" AND p."TenantId" = r."TenantId"
WHERE r."TenantId" = (SELECT tenancy.caller_tenant())
  AND (r."EndsAt" IS NULL OR r."EndsAt" > pg_catalog.now())
  AND (r."SeatId" = (SELECT tenancy.caller_seat()) OR (tenancy.manages_access(r."Key") AND NOT EXISTS (SELECT 1 FROM "tenancy"."OrganizationUnitPaths" o WHERE o."AncestorId" = r."UnitId" AND o."TenantId" = r."TenantId" AND o."DescendantId" = CASE WHEN p."DescendantId" = $1 THEN $2 ELSE $1 END)))
  AND p."DescendantId" IN ($1, $2)
  AND $1 = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.units.manage'))) AND $2 = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.units.manage')))
$function$;
COMMENT ON FUNCTION tenancy.rights_a_move_changes(parent uuid, new_parent uuid) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.rights_a_move_changes(parent uuid, new_parent uuid) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.seats_holding_at(key text, unit uuid) RETURNS TABLE ("SeatId" uuid)
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT DISTINCT r."SeatId" FROM "tenancy"."SeatRights" r
JOIN "tenancy"."OrganizationUnitPaths" p ON p."AncestorId" = r."UnitId" AND p."TenantId" = r."TenantId"
JOIN "tenancy"."Seats" s ON s."Id" = r."SeatId" AND s."TenantId" = r."TenantId" AND s."Status" = 'Active'
WHERE r."TenantId" = (SELECT tenancy.caller_tenant()) AND r."Key" = $1 AND tenancy.key_is_live($1)
  AND r."StartsAt" <= pg_catalog.now() AND (r."EndsAt" IS NULL OR r."EndsAt" > pg_catalog.now())
  AND p."DescendantId" = $2
  AND (r."SeatId" = (SELECT tenancy.caller_seat()) OR r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.grants.manage'))) OR r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.seats.manage'))) OR r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.units.manage'))) OR (SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')))
$function$;
COMMENT ON FUNCTION tenancy.seats_holding_at(key text, unit uuid) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.seats_holding_at(key text, unit uuid) FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.tenant_administrators() RETURNS TABLE ("SeatId" uuid, "RoleId" uuid)
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT r."SeatId", r."RoleId" FROM "tenancy"."SeatRights" r
JOIN "tenancy"."OrganizationUnits" u ON u."Id" = r."UnitId" AND u."TenantId" = r."TenantId"
WHERE r."TenantId" = (SELECT tenancy.caller_tenant()) AND r."Key" = 'tenancy.roles.manage'
  AND r."EndsAt" IS NULL AND r."StartsAt" <= pg_catalog.now()
  AND u."ParentId" IS NULL
  AND (r."SeatId" = (SELECT tenancy.caller_seat()) OR r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.grants.manage'))) OR r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.seats.manage'))) OR r."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.units.manage'))) OR (SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')))
$function$;
COMMENT ON FUNCTION tenancy.tenant_administrators() IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.tenant_administrators() FROM PUBLIC;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION tenancy.units_where_i_hold_in_tenant(tenant uuid, key text) RETURNS SETOF uuid
    LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $function$
SELECT DISTINCT p."DescendantId" FROM "tenancy"."SeatRights" r
JOIN "tenancy"."OrganizationUnitPaths" p ON p."AncestorId" = r."UnitId" AND p."TenantId" = r."TenantId"
WHERE r."SeatId" = (SELECT tenancy.seat_in_tenant($1)) AND r."Key" = $2 AND tenancy.key_is_live($2)
  AND r."StartsAt" <= pg_catalog.now() AND (r."EndsAt" IS NULL OR r."EndsAt" > pg_catalog.now())
$function$;
COMMENT ON FUNCTION tenancy.units_where_i_hold_in_tenant(tenant uuid, key text) IS 'DDDToolkit access function of TenantsContext';
REVOKE ALL ON FUNCTION tenancy.units_where_i_hold_in_tenant(tenant uuid, key text) FROM PUBLIC;

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
        WHERE d.description = 'DDDToolkit access function of TenantsContext'
          AND (n.nspname, p.proname) IN (('tenancy', 'caller_rights'), ('tenancy', 'caller_seat'), ('tenancy', 'caller_tenant'), ('tenancy', 'holds_key'), ('tenancy', 'holds_key_in_tenant'), ('tenancy', 'holds_tenant_wide'), ('tenancy', 'identity_tenants'), ('tenancy', 'invitation_of_digest'), ('tenancy', 'key_is_live'), ('tenancy', 'manages_access'), ('tenancy', 'pack_keys'), ('tenancy', 'readable_units'), ('tenancy', 'rewrite_tenant_rights'), ('tenancy', 'rights_a_move_changes'), ('tenancy', 'role_keys_in_use'), ('tenancy', 'roles_with_key'), ('tenancy', 'roles_with_key_in_tenant'), ('tenancy', 'seat_in_tenant'), ('tenancy', 'seated_in_tenant'), ('tenancy', 'seats_holding_at'), ('tenancy', 'seats_of_identity'), ('tenancy', 'system_tenant'), ('tenancy', 'tenant_administrators'), ('tenancy', 'tenant_placements'), ('tenancy', 'tenant_roles'), ('tenancy', 'tenant_seats'), ('tenancy', 'tenant_unit_paths'), ('tenancy', 'tenant_units'), ('tenancy', 'tenants_to_sweep'), ('tenancy', 'unit_parent'), ('tenancy', 'units_where_i_hold'), ('tenancy', 'units_where_i_hold_in_tenant'))
          AND acl.grantee <> 0 AND acl.grantee <> p.proowner
    LOOP
        EXECUTE pg_catalog.format('REVOKE ALL ON FUNCTION %s FROM %s', granted.function::pg_catalog.regprocedure, granted.grantee::pg_catalog.regrole);
    END LOOP;
END
$ddd$;
GRANT EXECUTE ON FUNCTION tenancy.caller_rights() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.identity_tenants() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.invitation_of_digest(digest bytea) TO ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.manages_access(key text) TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.pack_keys(pack text) TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.role_keys_in_use() TO ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.seat_in_tenant(tenant uuid) TO authenticated;
GRANT EXECUTE ON FUNCTION tenancy.holds_key_in_tenant(tenant uuid, key text) TO authenticated;
GRANT EXECUTE ON FUNCTION tenancy.roles_with_key_in_tenant(tenant uuid, key text) TO authenticated;
GRANT EXECUTE ON FUNCTION tenancy.seated_in_tenant(tenant uuid) TO authenticated;
GRANT EXECUTE ON FUNCTION tenancy.seats_of_identity(identity uuid) TO ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.system_tenant() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.caller_seat() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.caller_tenant() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.holds_key(key text) TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.holds_tenant_wide(key text) TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.readable_units() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.rewrite_tenant_rights() TO ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.roles_with_key(key text) TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.tenant_placements() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.tenant_roles() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.tenant_seats() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.tenant_unit_paths() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.tenant_units() TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.tenants_to_sweep() TO ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.unit_parent(unit uuid) TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.units_where_i_hold(key text) TO authenticated, ddd_system_in;
GRANT EXECUTE ON FUNCTION tenancy.rights_a_move_changes(parent uuid, new_parent uuid) TO authenticated;
GRANT EXECUTE ON FUNCTION tenancy.seats_holding_at(key text, unit uuid) TO authenticated;
GRANT EXECUTE ON FUNCTION tenancy.tenant_administrators() TO authenticated;
GRANT EXECUTE ON FUNCTION tenancy.units_where_i_hold_in_tenant(tenant uuid, key text) TO authenticated;

ALTER TABLE tenancy."InvitationDigests" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."InvitationDigests" FORCE ROW LEVEL SECURITY;

-- Issuers keep their token's digest (insert) for authenticated asks the policy 'Issuers keep their token's digest' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Issuers keep their token's digest (insert) for authenticated" ON tenancy."InvitationDigests" FOR INSERT TO authenticated
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND (EXISTS (SELECT 1 FROM "tenancy"."Invitations" i WHERE i."Id" = "tenancy"."InvitationDigests"."InvitationId" AND i."TenantId" = "tenancy"."InvitationDigests"."TenantId" AND ddd.written_in_this_transaction(i.xmin) AND i."IssuedBy" = (SELECT tenancy.caller_seat()) AND i."State" = 'Open')));
COMMENT ON POLICY "Issuers keep their token's digest (insert) for authenticated" ON tenancy."InvitationDigests" IS 'DDDToolkit row access rule';

-- Tenancy work keeps a token's digest (insert) for ddd_system_in asks the policy 'Tenancy work keeps a token's digest' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work keeps a token's digest (insert) for ddd_system_in" ON tenancy."InvitationDigests" FOR INSERT TO ddd_system_in
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy') AND (EXISTS (SELECT 1 FROM "tenancy"."Invitations" i WHERE i."Id" = "tenancy"."InvitationDigests"."InvitationId" AND i."TenantId" = "tenancy"."InvitationDigests"."TenantId" AND ddd.written_in_this_transaction(i.xmin))));
COMMENT ON POLICY "Tenancy work keeps a token's digest (insert) for ddd_system_in" ON tenancy."InvitationDigests" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."InvitationDigests" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."InvitationDigests" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."InvitationDigests" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."InvitationDigests" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."InvitationDigests" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."InvitationDigests" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."InvitationDigests" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."InvitationDigests" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."InvitationDigests" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."InvitationDigests" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."InvitationDigests" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."InvitationDigests" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."Invitations" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."Invitations" FORCE ROW LEVEL SECURITY;

-- Seat managers read invitations (select) for authenticated asks the policy 'Seat managers read invitations' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seat managers read invitations (select) for authenticated" ON tenancy."Invitations" FOR SELECT TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.seats.manage')))));
COMMENT ON POLICY "Seat managers read invitations (select) for authenticated" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."Invitations" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."Invitations" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Seat managers issue invitations (insert) for authenticated asks the policy 'Seat managers issue invitations' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seat managers issue invitations (insert) for authenticated" ON tenancy."Invitations" FOR INSERT TO authenticated
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_tenant_wide('tenancy.seats.manage')) AND ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.grants.manage')))) AND (EXISTS (SELECT 1 FROM "tenancy"."Roles" r WHERE r."Id" = "tenancy"."Invitations"."RoleId" AND r."TenantId" = (SELECT tenancy.caller_tenant()) AND r."Status" = 'Active')) AND (NOT EXISTS (SELECT 1 FROM "tenancy"."Roles" r CROSS JOIN LATERAL pg_catalog.unnest(r."Keys") AS managed(k) WHERE r."Id" = "tenancy"."Invitations"."RoleId" AND r."Status" = 'Active' AND tenancy.manages_access(managed.k) AND NOT ("tenancy"."Invitations"."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold(managed.k)))))) AND (NOT "IssuedAsSystem") AND ("IssuedBy" = (SELECT tenancy.caller_seat())) AND ("State" = 'Open') AND ("AcceptedAs" IS NULL AND "AcceptedAt" IS NULL));
COMMENT ON POLICY "Seat managers issue invitations (insert) for authenticated" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (insert) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."Invitations" FOR INSERT TO ddd_system_in
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Seat managers cancel invitations (update) for authenticated asks the policy 'Seat managers cancel invitations' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seat managers cancel invitations (update) for authenticated" ON tenancy."Invitations" FOR UPDATE TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.seats.manage')))) AND ("State" = 'Open'))
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.seats.manage')))) AND ("State" IN ('Open', 'Cancelled')) AND ("AcceptedAs" IS NULL AND "AcceptedAt" IS NULL));
COMMENT ON POLICY "Seat managers cancel invitations (update) for authenticated" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (update) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."Invitations" FOR UPDATE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'))
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (delete) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."Invitations" FOR DELETE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."Invitations" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."Invitations" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."Invitations" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."Invitations" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."Invitations" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."Invitations" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."Invitations" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."OrganizationUnitPaths" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."OrganizationUnitPaths" FORCE ROW LEVEL SECURITY;

-- Members read the tree (select) for authenticated asks the policy 'Members read the tree' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Members read the tree (select) for authenticated" ON tenancy."OrganizationUnitPaths" FOR SELECT TO authenticated
    USING ("TenantId" = (SELECT tenancy.caller_tenant()));
COMMENT ON POLICY "Members read the tree (select) for authenticated" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."OrganizationUnitPaths" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."OrganizationUnitPaths" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Unit managers write the tree (insert) for authenticated asks the policy 'Unit managers write the tree' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Unit managers write the tree (insert) for authenticated" ON tenancy."OrganizationUnitPaths" FOR INSERT TO authenticated
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_key('tenancy.units.manage')));
COMMENT ON POLICY "Unit managers write the tree (insert) for authenticated" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (insert) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."OrganizationUnitPaths" FOR INSERT TO ddd_system_in
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Unit managers write the tree (update) for authenticated asks the policy 'Unit managers write the tree' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Unit managers write the tree (update) for authenticated" ON tenancy."OrganizationUnitPaths" FOR UPDATE TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_key('tenancy.units.manage')))
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_key('tenancy.units.manage')));
COMMENT ON POLICY "Unit managers write the tree (update) for authenticated" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (update) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."OrganizationUnitPaths" FOR UPDATE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'))
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Unit managers write the tree (delete) for authenticated asks the policy 'Unit managers write the tree' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Unit managers write the tree (delete) for authenticated" ON tenancy."OrganizationUnitPaths" FOR DELETE TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_key('tenancy.units.manage')));
COMMENT ON POLICY "Unit managers write the tree (delete) for authenticated" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (delete) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."OrganizationUnitPaths" FOR DELETE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."OrganizationUnitPaths" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."OrganizationUnitPaths" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."OrganizationUnitPaths" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."OrganizationUnitPaths" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."OrganizationUnitPaths" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."OrganizationUnitPaths" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."OrganizationUnitPaths" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."OrganizationUnits" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."OrganizationUnits" FORCE ROW LEVEL SECURITY;

-- Members read the units (select) for authenticated asks the policy 'Members read the units' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Members read the units (select) for authenticated" ON tenancy."OrganizationUnits" FOR SELECT TO authenticated
    USING ("TenantId" = (SELECT tenancy.caller_tenant()));
COMMENT ON POLICY "Members read the units (select) for authenticated" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."OrganizationUnits" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."OrganizationUnits" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Unit managers add below their units (insert) for authenticated asks the policy 'Unit managers add below their units' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Unit managers add below their units (insert) for authenticated" ON tenancy."OrganizationUnits" FOR INSERT TO authenticated
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND ("ParentId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.units.manage')))));
COMMENT ON POLICY "Unit managers add below their units (insert) for authenticated" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (insert) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."OrganizationUnits" FOR INSERT TO ddd_system_in
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Unit managers change their units (update) for authenticated asks the policy 'Unit managers change their units' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Unit managers change their units (update) for authenticated" ON tenancy."OrganizationUnits" FOR UPDATE TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND ("Id" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.units.manage'))) OR "ParentId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.units.manage')))))
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND (("ParentId" IS NOT NULL AND "ParentId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.units.manage')))) OR ("Id" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.units.manage'))) AND "ParentId" IS NOT DISTINCT FROM (SELECT tenancy.unit_parent("Id")))));
COMMENT ON POLICY "Unit managers change their units (update) for authenticated" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (update) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."OrganizationUnits" FOR UPDATE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'))
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (delete) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."OrganizationUnits" FOR DELETE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."OrganizationUnits" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."OrganizationUnits" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."OrganizationUnits" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."OrganizationUnits" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."OrganizationUnits" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."OrganizationUnits" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."OrganizationUnits" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."Organizations" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."Organizations" FORCE ROW LEVEL SECURITY;

-- Seats read their organizations (select) for authenticated asks the policy 'Seats read their organizations' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seats read their organizations (select) for authenticated" ON tenancy."Organizations" FOR SELECT TO authenticated
    USING ("Id" = (SELECT tenancy.caller_tenant()) OR "Id" = ANY (ARRAY(SELECT tenancy.identity_tenants())));
COMMENT ON POLICY "Seats read their organizations (select) for authenticated" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."Organizations" FOR SELECT TO ddd_system_in
    USING ("Id" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."Organizations" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (insert) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."Organizations" FOR INSERT TO ddd_system_in
    WITH CHECK (("Id" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Unit and settings managers change it (update) for authenticated asks the policy 'Unit and settings managers change it' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Unit and settings managers change it (update) for authenticated" ON tenancy."Organizations" FOR UPDATE TO authenticated
    USING (("Id" = (SELECT tenancy.caller_tenant())) AND ((SELECT tenancy.holds_key('tenancy.units.manage')) OR (SELECT tenancy.holds_tenant_wide('tenancy.settings.manage'))))
    WITH CHECK (("Id" = (SELECT tenancy.caller_tenant())) AND ((SELECT tenancy.holds_key('tenancy.units.manage')) OR (SELECT tenancy.holds_tenant_wide('tenancy.settings.manage'))));
COMMENT ON POLICY "Unit and settings managers change it (update) for authenticated" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (update) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."Organizations" FOR UPDATE TO ddd_system_in
    USING (("Id" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'))
    WITH CHECK (("Id" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (delete) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."Organizations" FOR DELETE TO ddd_system_in
    USING (("Id" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."Organizations" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("Id" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("Id" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."Organizations" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."Organizations" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."Organizations" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."Organizations" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."Organizations" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."Organizations" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."Roles" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."Roles" FORCE ROW LEVEL SECURITY;

-- Members read roles (select) for authenticated asks the policy 'Members read roles' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Members read roles (select) for authenticated" ON tenancy."Roles" FOR SELECT TO authenticated
    USING ("TenantId" = (SELECT tenancy.caller_tenant()));
COMMENT ON POLICY "Members read roles (select) for authenticated" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."Roles" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."Roles" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Role and settings managers add roles (insert) for authenticated asks the policy 'Role and settings managers add roles' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Role and settings managers add roles (insert) for authenticated" ON tenancy."Roles" FOR INSERT TO authenticated
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND (((SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')) AND ("tenancy"."Roles"."FromPack" IS NULL AND "tenancy"."Roles"."KeysFromPack" IS NULL)) OR (((SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')) OR (SELECT tenancy.holds_tenant_wide('tenancy.settings.manage'))) AND (tenancy.pack_keys("tenancy"."Roles"."FromPack") IS NOT NULL AND NOT EXISTS (SELECT held.k FROM pg_catalog.unnest("tenancy"."Roles"."Keys") AS held(k) EXCEPT SELECT pg_catalog.unnest(tenancy.pack_keys("tenancy"."Roles"."FromPack"))) AND NOT EXISTS (SELECT pg_catalog.unnest(tenancy.pack_keys("tenancy"."Roles"."FromPack")) EXCEPT SELECT held.k FROM pg_catalog.unnest("tenancy"."Roles"."Keys") AS held(k)) AND "tenancy"."Roles"."KeysFromPack" IS NOT NULL AND NOT EXISTS (SELECT held.k FROM pg_catalog.unnest("tenancy"."Roles"."KeysFromPack") AS held(k) EXCEPT SELECT pg_catalog.unnest(tenancy.pack_keys("tenancy"."Roles"."FromPack"))) AND NOT EXISTS (SELECT pg_catalog.unnest(tenancy.pack_keys("tenancy"."Roles"."FromPack")) EXCEPT SELECT held.k FROM pg_catalog.unnest("tenancy"."Roles"."KeysFromPack") AS held(k))))));
COMMENT ON POLICY "Role and settings managers add roles (insert) for authenticated" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (insert) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."Roles" FOR INSERT TO ddd_system_in
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Role managers change roles (update) for authenticated asks the policy 'Role managers change roles' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Role managers change roles (update) for authenticated" ON tenancy."Roles" FOR UPDATE TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')))
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_tenant_wide('tenancy.roles.manage')));
COMMENT ON POLICY "Role managers change roles (update) for authenticated" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (update) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."Roles" FOR UPDATE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'))
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (delete) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."Roles" FOR DELETE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."Roles" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."Roles" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."Roles" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."Roles" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."Roles" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."Roles" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."Roles" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."Roles" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."SeatPlacements" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."SeatPlacements" FORCE ROW LEVEL SECURITY;

-- Members read placements (select) for authenticated asks the policy 'Members read placements' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Members read placements (select) for authenticated" ON tenancy."SeatPlacements" FOR SELECT TO authenticated
    USING ("TenantId" = (SELECT tenancy.caller_tenant()));
COMMENT ON POLICY "Members read placements (select) for authenticated" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."SeatPlacements" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."SeatPlacements" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Seat managers place at the unit (insert) for authenticated asks the policy 'Seat managers place at the unit' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seat managers place at the unit (insert) for authenticated" ON tenancy."SeatPlacements" FOR INSERT TO authenticated
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND (EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatPlacements"."SeatId" AND s."TenantId" = (SELECT tenancy.caller_tenant()))) AND ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.seats.manage')))));
COMMENT ON POLICY "Seat managers place at the unit (insert) for authenticated" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (insert) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."SeatPlacements" FOR INSERT TO ddd_system_in
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Seat managers change placements (update) for authenticated asks the policy 'Seat managers change placements' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seat managers change placements (update) for authenticated" ON tenancy."SeatPlacements" FOR UPDATE TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_key('tenancy.seats.manage')))
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND (EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatPlacements"."SeatId" AND s."TenantId" = (SELECT tenancy.caller_tenant()))) AND (SELECT tenancy.holds_key('tenancy.seats.manage')) AND (NOT "IsPrimary" OR "UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.seats.manage')))));
COMMENT ON POLICY "Seat managers change placements (update) for authenticated" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (update) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."SeatPlacements" FOR UPDATE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'))
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Seat managers withdraw at the unit (delete) for authenticated asks the policy 'Seat managers withdraw at the unit' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seat managers withdraw at the unit (delete) for authenticated" ON tenancy."SeatPlacements" FOR DELETE TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.seats.manage'))) OR "tenancy"."SeatPlacements"."SeatId" = (SELECT tenancy.caller_seat())) AND (NOT EXISTS (SELECT 1 FROM "tenancy"."SeatRoleGrants" g WHERE g."SeatId" = "tenancy"."SeatPlacements"."SeatId" AND g."UnitId" = "tenancy"."SeatPlacements"."UnitId")));
COMMENT ON POLICY "Seat managers withdraw at the unit (delete) for authenticated" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (delete) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."SeatPlacements" FOR DELETE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."SeatPlacements" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."SeatPlacements" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."SeatPlacements" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."SeatPlacements" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."SeatPlacements" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."SeatPlacements" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."SeatPlacements" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."SeatRights" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."SeatRights" FORCE ROW LEVEL SECURITY;

-- A seat reads its own rights (select) for authenticated asks the policy 'A seat reads its own rights' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "A seat reads its own rights (select) for authenticated" ON tenancy."SeatRights" FOR SELECT TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND ("tenancy"."SeatRights"."SeatId" = (SELECT tenancy.caller_seat())));
COMMENT ON POLICY "A seat reads its own rights (select) for authenticated" ON tenancy."SeatRights" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."SeatRights" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."SeatRights" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."SeatRights" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."SeatRights" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."SeatRights" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."SeatRights" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."SeatRights" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."SeatRights" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."SeatRights" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."SeatRights" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."SeatRights" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."SeatRights" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."SeatRights" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."SeatRights" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."SeatRights" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."SeatRights" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."SeatRoleGrants" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."SeatRoleGrants" FORCE ROW LEVEL SECURITY;

-- Seats and managers read grants (select) for authenticated asks the policy 'Seats and managers read grants' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seats and managers read grants (select) for authenticated" ON tenancy."SeatRoleGrants" FOR SELECT TO authenticated
    USING ((EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.caller_tenant()))) AND ("tenancy"."SeatRoleGrants"."SeatId" = (SELECT tenancy.caller_seat()) OR "tenancy"."SeatRoleGrants"."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.grants.manage'))) OR "tenancy"."SeatRoleGrants"."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.seats.manage'))) OR "tenancy"."SeatRoleGrants"."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.units.manage'))) OR (SELECT tenancy.holds_tenant_wide('tenancy.roles.manage'))));
COMMENT ON POLICY "Seats and managers read grants (select) for authenticated" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."SeatRoleGrants" FOR SELECT TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.system_tenant())));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."SeatRoleGrants" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Grants managers give at the unit (insert) for authenticated asks the policy 'Grants managers give at the unit' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Grants managers give at the unit (insert) for authenticated" ON tenancy."SeatRoleGrants" FOR INSERT TO authenticated
    WITH CHECK ((EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.caller_tenant()))) AND ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.grants.manage')))) AND (EXISTS (SELECT 1 FROM "tenancy"."Roles" r WHERE r."Id" = "tenancy"."SeatRoleGrants"."RoleId" AND r."TenantId" = (SELECT tenancy.caller_tenant()) AND r."Status" = 'Active')) AND ("GrantedBy" = (SELECT tenancy.caller_seat())) AND (NOT EXISTS (SELECT 1 FROM "tenancy"."Roles" r CROSS JOIN LATERAL pg_catalog.unnest(r."Keys") AS managed(k) WHERE r."Id" = "tenancy"."SeatRoleGrants"."RoleId" AND r."Status" = 'Active' AND tenancy.manages_access(managed.k) AND NOT ("tenancy"."SeatRoleGrants"."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold(managed.k)))))) AND (NOT EXISTS (SELECT 1 FROM "tenancy"."Roles" r CROSS JOIN LATERAL pg_catalog.unnest(r."Keys") AS managed(k) WHERE r."Id" = "tenancy"."SeatRoleGrants"."RoleId" AND r."Status" = 'Active' AND tenancy.manages_access(managed.k)) OR "tenancy"."SeatRoleGrants"."SeatId" <> (SELECT tenancy.caller_seat())));
COMMENT ON POLICY "Grants managers give at the unit (insert) for authenticated" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (insert) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."SeatRoleGrants" FOR INSERT TO ddd_system_in
    WITH CHECK ((EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.system_tenant()))) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Grants managers change at the unit (update) for authenticated asks the policy 'Grants managers change at the unit' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Grants managers change at the unit (update) for authenticated" ON tenancy."SeatRoleGrants" FOR UPDATE TO authenticated
    USING ((EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.caller_tenant()))) AND ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.grants.manage')))) AND (EXISTS (SELECT 1 FROM "tenancy"."Roles" r WHERE r."Id" = "tenancy"."SeatRoleGrants"."RoleId" AND r."TenantId" = (SELECT tenancy.caller_tenant()) AND r."Status" = 'Active')) AND (NOT EXISTS (SELECT 1 FROM "tenancy"."Roles" r CROSS JOIN LATERAL pg_catalog.unnest(r."Keys") AS managed(k) WHERE r."Id" = "tenancy"."SeatRoleGrants"."RoleId" AND r."Status" = 'Active' AND tenancy.manages_access(managed.k) AND NOT ("tenancy"."SeatRoleGrants"."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold(managed.k)))))))
    WITH CHECK ((EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.caller_tenant()))) AND ("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.grants.manage')))) AND (EXISTS (SELECT 1 FROM "tenancy"."Roles" r WHERE r."Id" = "tenancy"."SeatRoleGrants"."RoleId" AND r."TenantId" = (SELECT tenancy.caller_tenant()) AND r."Status" = 'Active')) AND (NOT EXISTS (SELECT 1 FROM "tenancy"."Roles" r CROSS JOIN LATERAL pg_catalog.unnest(r."Keys") AS managed(k) WHERE r."Id" = "tenancy"."SeatRoleGrants"."RoleId" AND r."Status" = 'Active' AND tenancy.manages_access(managed.k) AND NOT ("tenancy"."SeatRoleGrants"."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold(managed.k)))))) AND (NOT EXISTS (SELECT 1 FROM "tenancy"."Roles" r CROSS JOIN LATERAL pg_catalog.unnest(r."Keys") AS managed(k) WHERE r."Id" = "tenancy"."SeatRoleGrants"."RoleId" AND r."Status" = 'Active' AND tenancy.manages_access(managed.k)) OR "tenancy"."SeatRoleGrants"."SeatId" <> (SELECT tenancy.caller_seat())));
COMMENT ON POLICY "Grants managers change at the unit (update) for authenticated" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (update) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."SeatRoleGrants" FOR UPDATE TO ddd_system_in
    USING ((EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.system_tenant()))) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'))
    WITH CHECK ((EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.system_tenant()))) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Grants managers revoke at the unit (delete) for authenticated asks the policy 'Grants managers revoke at the unit' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Grants managers revoke at the unit (delete) for authenticated" ON tenancy."SeatRoleGrants" FOR DELETE TO authenticated
    USING ((EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.caller_tenant()))) AND ((("UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold('tenancy.grants.manage')))) AND (NOT EXISTS (SELECT 1 FROM "tenancy"."Roles" r CROSS JOIN LATERAL pg_catalog.unnest(r."Keys") AS managed(k) WHERE r."Id" = "tenancy"."SeatRoleGrants"."RoleId" AND r."Status" = 'Active' AND tenancy.manages_access(managed.k) AND NOT ("tenancy"."SeatRoleGrants"."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold(managed.k))))))) OR "tenancy"."SeatRoleGrants"."SeatId" = (SELECT tenancy.caller_seat())));
COMMENT ON POLICY "Grants managers revoke at the unit (delete) for authenticated" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (delete) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."SeatRoleGrants" FOR DELETE TO ddd_system_in
    USING ((EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.system_tenant()))) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."SeatRoleGrants" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING (EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.system_tenant())))
    WITH CHECK (EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Id" = "tenancy"."SeatRoleGrants"."SeatId" AND s."TenantId" = (SELECT tenancy.system_tenant())));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."SeatRoleGrants" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."SeatRoleGrants" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."SeatRoleGrants" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."SeatRoleGrants" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."SeatRoleGrants" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."SeatRoleGrants" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."Seats" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."Seats" FORCE ROW LEVEL SECURITY;

-- Members and the person read seats (select) for authenticated asks the policy 'Members and the person read seats' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Members and the person read seats (select) for authenticated" ON tenancy."Seats" FOR SELECT TO authenticated
    USING ("TenantId" = (SELECT tenancy.caller_tenant()) OR "Identity" = (SELECT auth.uid()));
COMMENT ON POLICY "Members and the person read seats (select) for authenticated" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."Seats" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."Seats" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Seat managers add seats (insert) for authenticated asks the policy 'Seat managers add seats' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seat managers add seats (insert) for authenticated" ON tenancy."Seats" FOR INSERT TO authenticated
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_tenant_wide('tenancy.seats.manage')));
COMMENT ON POLICY "Seat managers add seats (insert) for authenticated" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (insert) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."Seats" FOR INSERT TO ddd_system_in
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Managers and the seat change it (update) for authenticated asks the policy 'Managers and the seat change it' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Managers and the seat change it (update) for authenticated" ON tenancy."Seats" FOR UPDATE TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND ((SELECT tenancy.holds_key('tenancy.seats.manage')) OR (SELECT tenancy.holds_key('tenancy.grants.manage')) OR "Id" = (SELECT tenancy.caller_seat())))
    WITH CHECK (("TenantId" = (SELECT tenancy.caller_tenant())) AND ((SELECT tenancy.holds_key('tenancy.seats.manage')) OR (SELECT tenancy.holds_key('tenancy.grants.manage')) OR "Id" = (SELECT tenancy.caller_seat())));
COMMENT ON POLICY "Managers and the seat change it (update) for authenticated" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (update) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."Seats" FOR UPDATE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'))
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (delete) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."Seats" FOR DELETE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."Seats" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."Seats" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."Seats" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."Seats" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."Seats" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."Seats" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."Seats" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."Seats" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."TenancyAccessRevisions" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."TenancyAccessRevisions" FORCE ROW LEVEL SECURITY;

-- Seats of the tenant read the revision (select) for a ~ b6768216 asks the policy 'Seats of the tenant read the revision' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seats of the tenant read the revision (select) for a ~ b6768216" ON tenancy."TenancyAccessRevisions" FOR SELECT TO authenticated
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ("TenantId" = ANY (ARRAY(SELECT tenancy.identity_tenants()))));
COMMENT ON POLICY "Seats of the tenant read the revision (select) for a ~ b6768216" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."TenancyAccessRevisions" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."TenancyAccessRevisions" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (insert) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."TenancyAccessRevisions" FOR INSERT TO ddd_system_in
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Seats of the tenant take the revision (update) for a ~ e1817f6e asks the policy 'Seats of the tenant take the revision' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seats of the tenant take the revision (update) for a ~ e1817f6e" ON tenancy."TenancyAccessRevisions" FOR UPDATE TO authenticated
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ("TenantId" = ANY (ARRAY(SELECT tenancy.identity_tenants()))))
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ("TenantId" = ANY (ARRAY(SELECT tenancy.identity_tenants()))));
COMMENT ON POLICY "Seats of the tenant take the revision (update) for a ~ e1817f6e" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (update) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."TenancyAccessRevisions" FOR UPDATE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'))
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (delete) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."TenancyAccessRevisions" FOR DELETE TO ddd_system_in
    USING (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."TenancyAccessRevisions" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."TenancyAccessRevisions" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."TenancyAccessRevisions" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."TenancyAccessRevisions" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."TenancyAccessRevisions" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."TenancyAccessRevisions" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."TenancyAccessRevisions" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."TenancyEventLog" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."TenancyEventLog" FORCE ROW LEVEL SECURITY;

-- History readers read their tenant (select) for authenticated asks the policy 'History readers read their tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "History readers read their tenant (select) for authenticated" ON tenancy."TenancyEventLog" FOR SELECT TO authenticated
    USING (("TenantId" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_tenant_wide('tenancy.history.view')));
COMMENT ON POLICY "History readers read their tenant (select) for authenticated" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."TenancyEventLog" FOR SELECT TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."TenancyEventLog" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

-- Seats record what they do (insert) for authenticated asks the policy 'Seats record what they do' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seats record what they do (insert) for authenticated" ON tenancy."TenancyEventLog" FOR INSERT TO authenticated
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ("ActedByKind" = 'seat') AND (CASE WHEN "tenancy"."TenancyEventLog"."ActedById" = (SELECT tenancy.caller_seat())::pg_catalog.text THEN true ELSE EXISTS (SELECT 1 FROM "tenancy"."Seats" s WHERE s."Identity" = (SELECT auth.uid()) AND s."TenantId" = "tenancy"."TenancyEventLog"."TenantId" AND s."Id"::pg_catalog.text = "tenancy"."TenancyEventLog"."ActedById" AND ddd.written_in_this_transaction(s.xmin)) END));
COMMENT ON POLICY "Seats record what they do (insert) for authenticated" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

-- Tenancy work records its tenant (insert) for ddd_system_in asks the policy 'Tenancy work records its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work records its tenant (insert) for ddd_system_in" ON tenancy."TenancyEventLog" FOR INSERT TO ddd_system_in
    WITH CHECK (("TenantId" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy') AND ("ActedByKind" IN ('system', 'operator', 'token')));
COMMENT ON POLICY "Tenancy work records its tenant (insert) for ddd_system_in" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."TenancyEventLog" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("TenantId" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("TenantId" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."TenancyEventLog" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."TenancyEventLog" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."TenancyEventLog" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."TenancyEventLog" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."TenancyEventLog" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."TenancyEventLog" IS 'DDDToolkit row access rule';

ALTER TABLE tenancy."Tenants" ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenancy."Tenants" FORCE ROW LEVEL SECURITY;

-- Seats read their tenants (select) for authenticated asks the policy 'Seats read their tenants' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Seats read their tenants (select) for authenticated" ON tenancy."Tenants" FOR SELECT TO authenticated
    USING ("Id" = (SELECT tenancy.caller_tenant()) OR "Id" = ANY (ARRAY(SELECT tenancy.identity_tenants())));
COMMENT ON POLICY "Seats read their tenants (select) for authenticated" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- System work reads its tenant (select) for ddd_system_in asks the policy 'System work reads its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."Tenants" FOR SELECT TO ddd_system_in
    USING ("Id" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "System work reads its tenant (select) for ddd_system_in" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Operators read every tenant (select) for tenancy_operator asks the policy 'Operators read every tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."Tenants" FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators read every tenant (select) for tenancy_operator" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (insert) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."Tenants" FOR INSERT TO ddd_system_in
    WITH CHECK (("Id" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (insert) for ddd_system_in" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Settings managers change the tenant (update) for authenticated asks the policy 'Settings managers change the tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Settings managers change the tenant (update) for authenticated" ON tenancy."Tenants" FOR UPDATE TO authenticated
    USING (("Id" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_tenant_wide('tenancy.settings.manage')))
    WITH CHECK ((("Id" = (SELECT tenancy.caller_tenant())) AND (SELECT tenancy.holds_tenant_wide('tenancy.settings.manage'))) AND ("Status" = 'Active'));
COMMENT ON POLICY "Settings managers change the tenant (update) for authenticated" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (update) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."Tenants" FOR UPDATE TO ddd_system_in
    USING (("Id" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'))
    WITH CHECK (("Id" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (update) for ddd_system_in" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Tenancy work writes its tenant (delete) for ddd_system_in asks the policy 'Tenancy work writes its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."Tenants" FOR DELETE TO ddd_system_in
    USING (("Id" = (SELECT tenancy.system_tenant())) AND ((SELECT auth.jwt() ->> 'scope') = 'tenancy'));
COMMENT ON POLICY "Tenancy work writes its tenant (delete) for ddd_system_in" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Kept to its tenant (all) for ddd_system_in is the policy 'Kept to its tenant' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."Tenants" AS RESTRICTIVE FOR ALL TO ddd_system_in
    USING ("Id" = (SELECT tenancy.system_tenant()))
    WITH CHECK ("Id" = (SELECT tenancy.system_tenant()));
COMMENT ON POLICY "Kept to its tenant (all) for ddd_system_in" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Closed to anonymous callers (all) for anon is the policy 'Closed to anonymous callers' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Closed to anonymous callers (all) for anon" ON tenancy."Tenants" AS RESTRICTIVE FOR ALL TO anon
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Closed to anonymous callers (all) for anon" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Operators only read (select) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (select) for tenancy_operator" ON tenancy."Tenants" AS RESTRICTIVE FOR SELECT TO tenancy_operator
    USING (true);
COMMENT ON POLICY "Operators only read (select) for tenancy_operator" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Operators only read (insert) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."Tenants" AS RESTRICTIVE FOR INSERT TO tenancy_operator
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (insert) for tenancy_operator" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Operators only read (update) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (update) for tenancy_operator" ON tenancy."Tenants" AS RESTRICTIVE FOR UPDATE TO tenancy_operator
    USING (false)
    WITH CHECK (false);
COMMENT ON POLICY "Operators only read (update) for tenancy_operator" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Operators only read (delete) for tenancy_operator is the policy 'Operators only read' of the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres, which narrows what the permissive policies allow.
CREATE POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."Tenants" AS RESTRICTIVE FOR DELETE TO tenancy_operator
    USING (false);
COMMENT ON POLICY "Operators only read (delete) for tenancy_operator" ON tenancy."Tenants" IS 'DDDToolkit row access rule';

-- Privileges, from the policies above: what a table gave the roles of this file before is taken back, and a
-- role then gets the commands a permissive policy allows it, and no more. UPDATE is granted on the columns
-- that may change.
GRANT USAGE ON SCHEMA tenancy TO authenticated, ddd_system, ddd_system_in, tenancy_operator;

REVOKE ALL ON TABLE tenancy."InvitationDigests" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT INSERT ON TABLE tenancy."InvitationDigests" TO authenticated;
GRANT INSERT ON TABLE tenancy."InvitationDigests" TO ddd_system_in;

REVOKE ALL ON TABLE tenancy."Invitations" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT ON TABLE tenancy."Invitations" TO authenticated;
GRANT UPDATE ("AcceptedAs", "AcceptedAt", "Address", "ClosedAt", "DisplayName", "InvitedAccount", "State", "Version") ON TABLE tenancy."Invitations" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."Invitations" TO ddd_system_in;
GRANT UPDATE ("AcceptedAs", "AcceptedAt", "Address", "ClosedAt", "DisplayName", "InvitedAccount", "State", "Version") ON TABLE tenancy."Invitations" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."Invitations" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."OrganizationUnitPaths" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."OrganizationUnitPaths" TO authenticated;
GRANT UPDATE ("Distance") ON TABLE tenancy."OrganizationUnitPaths" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."OrganizationUnitPaths" TO ddd_system_in;
GRANT UPDATE ("Distance") ON TABLE tenancy."OrganizationUnitPaths" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."OrganizationUnitPaths" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."OrganizationUnits" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT ON TABLE tenancy."OrganizationUnits" TO authenticated;
GRANT UPDATE ("CostCentre", "Kind", "Name", "ParentId", "Status", "TenantId") ON TABLE tenancy."OrganizationUnits" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."OrganizationUnits" TO ddd_system_in;
GRANT UPDATE ("CostCentre", "Kind", "Name", "ParentId", "Status", "TenantId") ON TABLE tenancy."OrganizationUnits" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."OrganizationUnits" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."Organizations" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT ON TABLE tenancy."Organizations" TO authenticated;
GRANT UPDATE ("Name", "Version") ON TABLE tenancy."Organizations" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."Organizations" TO ddd_system_in;
GRANT UPDATE ("Name", "Version") ON TABLE tenancy."Organizations" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."Organizations" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."Roles" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT ON TABLE tenancy."Roles" TO authenticated;
GRANT UPDATE ("Description", "FromPack", "Keys", "KeysFromPack", "Name", "NormalizedName", "Status", "Version") ON TABLE tenancy."Roles" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."Roles" TO ddd_system_in;
GRANT UPDATE ("Description", "FromPack", "Keys", "KeysFromPack", "Name", "NormalizedName", "Status", "Version") ON TABLE tenancy."Roles" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."Roles" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."SeatPlacements" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."SeatPlacements" TO authenticated;
GRANT UPDATE ("IsPrimary", "PlacedAt", "PlacedBy", "TenantId") ON TABLE tenancy."SeatPlacements" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."SeatPlacements" TO ddd_system_in;
GRANT UPDATE ("IsPrimary", "PlacedAt", "PlacedBy", "TenantId") ON TABLE tenancy."SeatPlacements" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."SeatPlacements" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."SeatRights" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT ON TABLE tenancy."SeatRights" TO authenticated;
GRANT SELECT ON TABLE tenancy."SeatRights" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."SeatRights" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."SeatRoleGrants" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."SeatRoleGrants" TO authenticated;
GRANT UPDATE ("EndsAt", "GrantedBy", "Reason", "StartsAt") ON TABLE tenancy."SeatRoleGrants" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."SeatRoleGrants" TO ddd_system_in;
GRANT UPDATE ("EndsAt", "GrantedBy", "Reason", "StartsAt") ON TABLE tenancy."SeatRoleGrants" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."SeatRoleGrants" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."Seats" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT ON TABLE tenancy."Seats" TO authenticated;
GRANT UPDATE ("DisplayName", "Identity", "JobTitle", "Status", "Version") ON TABLE tenancy."Seats" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."Seats" TO ddd_system_in;
GRANT UPDATE ("DisplayName", "Identity", "JobTitle", "Status", "Version") ON TABLE tenancy."Seats" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."Seats" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."TenancyAccessRevisions" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT ON TABLE tenancy."TenancyAccessRevisions" TO authenticated;
GRANT UPDATE ("Revision") ON TABLE tenancy."TenancyAccessRevisions" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."TenancyAccessRevisions" TO ddd_system_in;
GRANT UPDATE ("Revision") ON TABLE tenancy."TenancyAccessRevisions" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."TenancyAccessRevisions" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."TenancyEventLog" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT, INSERT ON TABLE tenancy."TenancyEventLog" TO authenticated;
GRANT SELECT, INSERT ON TABLE tenancy."TenancyEventLog" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."TenancyEventLog" TO tenancy_operator;

REVOKE ALL ON TABLE tenancy."Tenants" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT SELECT ON TABLE tenancy."Tenants" TO authenticated;
GRANT UPDATE ("IsDemo", "Shape", "Slug", "Status", "StatusReason", "Version") ON TABLE tenancy."Tenants" TO authenticated;
GRANT SELECT, INSERT, DELETE ON TABLE tenancy."Tenants" TO ddd_system_in;
GRANT UPDATE ("IsDemo", "Shape", "Slug", "Status", "StatusReason", "Version") ON TABLE tenancy."Tenants" TO ddd_system_in;
GRANT SELECT ON TABLE tenancy."Tenants" TO tenancy_operator;

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
        WHERE c.oid IN ('tenancy."TenancyOutboxMessages"'::pg_catalog.regclass)
          AND acl.grantee <> c.relowner
          AND NOT (holder.rolcanlogin OR holder.rolbypassrls OR holder.rolsuper)
    LOOP
        EXECUTE pg_catalog.format('REVOKE ALL ON TABLE %s FROM %s', held.relation::pg_catalog.regclass, held.grantee::pg_catalog.regrole);
    END LOOP;
END
$ddd$;

-- The outbox takes a row from whoever saves, and nobody but the bookkeeping reads, marks or deletes one.
REVOKE ALL ON TABLE tenancy."TenancyOutboxMessages" FROM PUBLIC, anon, authenticated, ddd_system, ddd_system_in, tenancy_operator;
GRANT INSERT ON TABLE tenancy."TenancyOutboxMessages" TO authenticated, ddd_system_in;
GRANT SELECT, DELETE ON TABLE tenancy."TenancyOutboxMessages" TO ddd_system;
GRANT UPDATE ("Attempts", "LastError", "NextAttemptAt", "ProcessedAt") ON TABLE tenancy."TenancyOutboxMessages" TO ddd_system;

-- The bookkeeping role reads which migrations ran, where the database keeps a history of them.
DO $ddd$
BEGIN
    IF pg_catalog.to_regclass('tenancy."__EFMigrationsHistory"') IS NOT NULL THEN
        GRANT USAGE ON SCHEMA tenancy TO ddd_system;
        GRANT SELECT ON TABLE tenancy."__EFMigrationsHistory" TO ddd_system;
    END IF;
END
$ddd$;

-- Written by the row access contribution DDDToolkit.Supporting.Tenancy.Postgres.TenancyRowAccessContribution in DDDToolkit.Supporting.Tenancy.Postgres.
CREATE OR REPLACE FUNCTION "tenancy".rights_follow_grants() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$
DECLARE
    of_seat pg_catalog.uuid;
    at_unit pg_catalog.uuid;
    of_role pg_catalog.uuid;
    pass pg_catalog.int4;
BEGIN
    CASE TG_ARGV[0]
        WHEN 'grants' THEN
            -- The rights of the grant as it was, and as it is where that is another seat's, unit's or role's.
            FOR pass IN 1..2 LOOP
                IF pass = 1 THEN
                    CONTINUE WHEN TG_OP = 'INSERT';
                    of_seat := OLD."SeatId"; at_unit := OLD."UnitId"; of_role := OLD."RoleId";
                ELSE
                    CONTINUE WHEN TG_OP = 'DELETE';
                    CONTINUE WHEN TG_OP = 'UPDATE' AND (NEW."SeatId", NEW."UnitId", NEW."RoleId") IS NOT DISTINCT FROM (of_seat, at_unit, of_role);
                    of_seat := NEW."SeatId"; at_unit := NEW."UnitId"; of_role := NEW."RoleId";
                END IF;
                DELETE FROM "tenancy"."SeatRights" r
                WHERE r."SeatId" = of_seat AND r."UnitId" = at_unit AND r."RoleId" = of_role
                  AND NOT EXISTS (SELECT 1 FROM (
                        SELECT s."TenantId" AS tenant, g."SeatId" AS seat, g."UnitId" AS unit, g."RoleId" AS role, held.k AS key, g."StartsAt" AS starts, g."EndsAt" AS ends
                        FROM "tenancy"."SeatRoleGrants" g
                        JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId" AND s."Status" = 'Active'
                        JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId" AND ro."TenantId" = s."TenantId" AND ro."Status" = 'Active'
                        CROSS JOIN LATERAL (SELECT DISTINCT each.k FROM pg_catalog.unnest(ro."Keys") AS each(k)) held
                        WHERE g."SeatId" = of_seat AND g."UnitId" = at_unit AND g."RoleId" = of_role AND tenancy.key_is_live(held.k)) d
                    WHERE d.seat = r."SeatId" AND d.unit = r."UnitId" AND d.role = r."RoleId" AND d.key = r."Key");
                INSERT INTO "tenancy"."SeatRights" AS r ("TenantId", "SeatId", "UnitId", "RoleId", "Key", "StartsAt", "EndsAt")
                SELECT d.tenant, d.seat, d.unit, d.role, d.key, d.starts, d.ends FROM (
                    SELECT s."TenantId" AS tenant, g."SeatId" AS seat, g."UnitId" AS unit, g."RoleId" AS role, held.k AS key, g."StartsAt" AS starts, g."EndsAt" AS ends
                    FROM "tenancy"."SeatRoleGrants" g
                    JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId" AND s."Status" = 'Active'
                    JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId" AND ro."TenantId" = s."TenantId" AND ro."Status" = 'Active'
                    CROSS JOIN LATERAL (SELECT DISTINCT each.k FROM pg_catalog.unnest(ro."Keys") AS each(k)) held
                    WHERE g."SeatId" = of_seat AND g."UnitId" = at_unit AND g."RoleId" = of_role AND tenancy.key_is_live(held.k)) d
                ON CONFLICT ("SeatId", "UnitId", "RoleId", "Key") DO UPDATE SET "TenantId" = EXCLUDED."TenantId", "StartsAt" = EXCLUDED."StartsAt", "EndsAt" = EXCLUDED."EndsAt"
                WHERE (r."TenantId", r."StartsAt", r."EndsAt") IS DISTINCT FROM (EXCLUDED."TenantId", EXCLUDED."StartsAt", EXCLUDED."EndsAt");
            END LOOP;
        WHEN 'seats' THEN
            IF TG_OP = 'DELETE' THEN of_seat := OLD."Id"; ELSE of_seat := NEW."Id"; END IF;
            DELETE FROM "tenancy"."SeatRights" r
            WHERE r."SeatId" = of_seat
              AND NOT EXISTS (SELECT 1 FROM (
                    SELECT s."TenantId" AS tenant, g."SeatId" AS seat, g."UnitId" AS unit, g."RoleId" AS role, held.k AS key, g."StartsAt" AS starts, g."EndsAt" AS ends
                    FROM "tenancy"."SeatRoleGrants" g
                    JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId" AND s."Status" = 'Active'
                    JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId" AND ro."TenantId" = s."TenantId" AND ro."Status" = 'Active'
                    CROSS JOIN LATERAL (SELECT DISTINCT each.k FROM pg_catalog.unnest(ro."Keys") AS each(k)) held
                    WHERE g."SeatId" = of_seat AND tenancy.key_is_live(held.k)) d
                WHERE d.seat = r."SeatId" AND d.unit = r."UnitId" AND d.role = r."RoleId" AND d.key = r."Key");
            INSERT INTO "tenancy"."SeatRights" AS r ("TenantId", "SeatId", "UnitId", "RoleId", "Key", "StartsAt", "EndsAt")
            SELECT d.tenant, d.seat, d.unit, d.role, d.key, d.starts, d.ends FROM (
                SELECT s."TenantId" AS tenant, g."SeatId" AS seat, g."UnitId" AS unit, g."RoleId" AS role, held.k AS key, g."StartsAt" AS starts, g."EndsAt" AS ends
                FROM "tenancy"."SeatRoleGrants" g
                JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId" AND s."Status" = 'Active'
                JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId" AND ro."TenantId" = s."TenantId" AND ro."Status" = 'Active'
                CROSS JOIN LATERAL (SELECT DISTINCT each.k FROM pg_catalog.unnest(ro."Keys") AS each(k)) held
                WHERE g."SeatId" = of_seat AND tenancy.key_is_live(held.k)) d
            ON CONFLICT ("SeatId", "UnitId", "RoleId", "Key") DO UPDATE SET "TenantId" = EXCLUDED."TenantId", "StartsAt" = EXCLUDED."StartsAt", "EndsAt" = EXCLUDED."EndsAt"
            WHERE (r."TenantId", r."StartsAt", r."EndsAt") IS DISTINCT FROM (EXCLUDED."TenantId", EXCLUDED."StartsAt", EXCLUDED."EndsAt");
        WHEN 'roles' THEN
            IF TG_OP = 'DELETE' THEN of_role := OLD."Id"; ELSE of_role := NEW."Id"; END IF;
            DELETE FROM "tenancy"."SeatRights" r
            WHERE r."RoleId" = of_role
              AND NOT EXISTS (SELECT 1 FROM (
                    SELECT s."TenantId" AS tenant, g."SeatId" AS seat, g."UnitId" AS unit, g."RoleId" AS role, held.k AS key, g."StartsAt" AS starts, g."EndsAt" AS ends
                    FROM "tenancy"."SeatRoleGrants" g
                    JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId" AND s."Status" = 'Active'
                    JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId" AND ro."TenantId" = s."TenantId" AND ro."Status" = 'Active'
                    CROSS JOIN LATERAL (SELECT DISTINCT each.k FROM pg_catalog.unnest(ro."Keys") AS each(k)) held
                    WHERE g."RoleId" = of_role AND tenancy.key_is_live(held.k)) d
                WHERE d.seat = r."SeatId" AND d.unit = r."UnitId" AND d.role = r."RoleId" AND d.key = r."Key");
            INSERT INTO "tenancy"."SeatRights" AS r ("TenantId", "SeatId", "UnitId", "RoleId", "Key", "StartsAt", "EndsAt")
            SELECT d.tenant, d.seat, d.unit, d.role, d.key, d.starts, d.ends FROM (
                SELECT s."TenantId" AS tenant, g."SeatId" AS seat, g."UnitId" AS unit, g."RoleId" AS role, held.k AS key, g."StartsAt" AS starts, g."EndsAt" AS ends
                FROM "tenancy"."SeatRoleGrants" g
                JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId" AND s."Status" = 'Active'
                JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId" AND ro."TenantId" = s."TenantId" AND ro."Status" = 'Active'
                CROSS JOIN LATERAL (SELECT DISTINCT each.k FROM pg_catalog.unnest(ro."Keys") AS each(k)) held
                WHERE g."RoleId" = of_role AND tenancy.key_is_live(held.k)) d
            ON CONFLICT ("SeatId", "UnitId", "RoleId", "Key") DO UPDATE SET "TenantId" = EXCLUDED."TenantId", "StartsAt" = EXCLUDED."StartsAt", "EndsAt" = EXCLUDED."EndsAt"
            WHERE (r."TenantId", r."StartsAt", r."EndsAt") IS DISTINCT FROM (EXCLUDED."TenantId", EXCLUDED."StartsAt", EXCLUDED."EndsAt");
    END CASE;
    RETURN NULL;
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".rights_follow_grants() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_rights_follow_grants ON "tenancy"."SeatRoleGrants";
CREATE TRIGGER tenancy_rights_follow_grants AFTER INSERT OR UPDATE OR DELETE ON "tenancy"."SeatRoleGrants"
    FOR EACH ROW EXECUTE FUNCTION "tenancy".rights_follow_grants('grants');
DROP TRIGGER IF EXISTS tenancy_rights_follow_grants ON "tenancy"."Seats";
CREATE TRIGGER tenancy_rights_follow_grants AFTER INSERT OR UPDATE OF "Status" OR DELETE ON "tenancy"."Seats"
    FOR EACH ROW EXECUTE FUNCTION "tenancy".rights_follow_grants('seats');
DROP TRIGGER IF EXISTS tenancy_rights_follow_grants ON "tenancy"."Roles";
CREATE TRIGGER tenancy_rights_follow_grants AFTER INSERT OR UPDATE OF "Status", "Keys", "TenantId" OR DELETE ON "tenancy"."Roles"
    FOR EACH ROW EXECUTE FUNCTION "tenancy".rights_follow_grants('roles');
CREATE OR REPLACE FUNCTION "tenancy".administrator_remains() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$
DECLARE
    tenant pg_catalog.uuid;
BEGIN
    -- Whether the old row was part of an administrator, and whose.
    CASE TG_ARGV[0]
        WHEN 'rights' THEN
            IF NOT (EXISTS (SELECT 1 FROM "tenancy"."OrganizationUnits" u WHERE u."Id" = OLD."UnitId" AND u."TenantId" = OLD."TenantId" AND u."ParentId" IS NULL) AND OLD."StartsAt" <= pg_catalog.now()) THEN
                RETURN NULL;
            END IF;
            tenant := OLD."TenantId";
        WHEN 'seats' THEN
            tenant := (SELECT r."TenantId" FROM "tenancy"."SeatRights" r WHERE r."SeatId" = OLD."Id" AND r."Key" = 'tenancy.roles.manage' AND r."EndsAt" IS NULL AND r."StartsAt" <= pg_catalog.now() AND EXISTS (SELECT 1 FROM "tenancy"."OrganizationUnits" u WHERE u."Id" = r."UnitId" AND u."TenantId" = r."TenantId" AND u."ParentId" IS NULL) LIMIT 1);
        WHEN 'roles' THEN
            tenant := (SELECT r."TenantId" FROM "tenancy"."SeatRights" r WHERE r."RoleId" = OLD."Id" AND r."Key" = 'tenancy.roles.manage' AND r."EndsAt" IS NULL AND r."StartsAt" <= pg_catalog.now() AND EXISTS (SELECT 1 FROM "tenancy"."OrganizationUnits" u WHERE u."Id" = r."UnitId" AND u."TenantId" = r."TenantId" AND u."ParentId" IS NULL) LIMIT 1);
        WHEN 'grants' THEN
            tenant := (SELECT r."TenantId" FROM "tenancy"."SeatRights" r WHERE r."SeatId" = OLD."SeatId" AND r."UnitId" = OLD."UnitId" AND r."RoleId" = OLD."RoleId" AND r."Key" = 'tenancy.roles.manage' AND r."EndsAt" IS NULL AND r."StartsAt" <= pg_catalog.now() AND EXISTS (SELECT 1 FROM "tenancy"."OrganizationUnits" u WHERE u."Id" = r."UnitId" AND u."TenantId" = r."TenantId" AND u."ParentId" IS NULL) LIMIT 1);
        WHEN 'placements' THEN
            tenant := (SELECT r."TenantId" FROM "tenancy"."SeatRights" r WHERE r."SeatId" = OLD."SeatId" AND r."UnitId" = OLD."UnitId" AND r."Key" = 'tenancy.roles.manage' AND r."EndsAt" IS NULL AND r."StartsAt" <= pg_catalog.now() AND EXISTS (SELECT 1 FROM "tenancy"."OrganizationUnits" u WHERE u."Id" = r."UnitId" AND u."TenantId" = r."TenantId" AND u."ParentId" IS NULL) LIMIT 1);
        WHEN 'units' THEN
            tenant := (SELECT r."TenantId" FROM "tenancy"."SeatRights" r WHERE r."UnitId" = OLD."Id" AND r."Key" = 'tenancy.roles.manage' AND r."EndsAt" IS NULL AND r."StartsAt" <= pg_catalog.now() LIMIT 1);
    END CASE;
    IF tenant IS NULL THEN
        RETURN NULL;
    END IF;
    -- One transaction at a time asks, per tenant, until it ends: two that each take away another
    -- administrator would otherwise each still see the other's. Each statement after the wait reads
    -- what the one before committed.
    PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('tenancy_administrator_remains ' || tenant::pg_catalog.text, 0));
    -- A closed tenant needs none; an active or a suspended one keeps one.
    IF EXISTS (SELECT 1 FROM "tenancy"."Tenants" t WHERE t."Id" = tenant
                 AND t."Status" IN ('Active', 'Suspended'))
       AND NOT EXISTS (SELECT 1 FROM "tenancy"."SeatRights" r
                       JOIN "tenancy"."Seats" s ON s."Id" = r."SeatId" AND s."Status" = 'Active'
                       JOIN "tenancy"."Roles" ro ON ro."Id" = r."RoleId" AND ro."Status" = 'Active'
                       WHERE r."TenantId" = tenant AND r."Key" = 'tenancy.roles.manage' AND r."EndsAt" IS NULL AND r."StartsAt" <= pg_catalog.now() AND EXISTS (SELECT 1 FROM "tenancy"."OrganizationUnits" u WHERE u."Id" = r."UnitId" AND u."TenantId" = r."TenantId" AND u."ParentId" IS NULL)) THEN
        RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = 'tenancy_administrator_remains', MESSAGE = 'A tenant keeps at least one administrator.';
    END IF;
    RETURN NULL;
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".administrator_remains() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_administrator_remains ON "tenancy"."SeatRights";
CREATE CONSTRAINT TRIGGER tenancy_administrator_remains AFTER UPDATE OR DELETE ON "tenancy"."SeatRights"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (OLD."Key" = 'tenancy.roles.manage' AND OLD."EndsAt" IS NULL)
    EXECUTE FUNCTION "tenancy".administrator_remains('rights');
DROP TRIGGER IF EXISTS tenancy_administrator_remains ON "tenancy"."Seats";
CREATE CONSTRAINT TRIGGER tenancy_administrator_remains AFTER UPDATE OF "Status" OR DELETE ON "tenancy"."Seats"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (OLD."Status" = 'Active')
    EXECUTE FUNCTION "tenancy".administrator_remains('seats');
DROP TRIGGER IF EXISTS tenancy_administrator_remains ON "tenancy"."Roles";
CREATE CONSTRAINT TRIGGER tenancy_administrator_remains AFTER UPDATE OR DELETE ON "tenancy"."Roles"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (OLD."Status" = 'Active')
    EXECUTE FUNCTION "tenancy".administrator_remains('roles');
DROP TRIGGER IF EXISTS tenancy_administrator_remains ON "tenancy"."SeatRoleGrants";
CREATE CONSTRAINT TRIGGER tenancy_administrator_remains AFTER UPDATE OR DELETE ON "tenancy"."SeatRoleGrants"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (OLD."EndsAt" IS NULL)
    EXECUTE FUNCTION "tenancy".administrator_remains('grants');
DROP TRIGGER IF EXISTS tenancy_administrator_remains ON "tenancy"."SeatPlacements";
CREATE CONSTRAINT TRIGGER tenancy_administrator_remains AFTER UPDATE OR DELETE ON "tenancy"."SeatPlacements"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
    EXECUTE FUNCTION "tenancy".administrator_remains('placements');
DROP TRIGGER IF EXISTS tenancy_administrator_remains ON "tenancy"."OrganizationUnits";
CREATE CONSTRAINT TRIGGER tenancy_administrator_remains AFTER UPDATE OF "ParentId" ON "tenancy"."OrganizationUnits"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (OLD."ParentId" IS NULL)
    EXECUTE FUNCTION "tenancy".administrator_remains('units');
CREATE OR REPLACE FUNCTION "tenancy".rights_backed_by_grants() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$
DECLARE
    unbacked boolean;
BEGIN
    -- The rights rows the change reaches, as they are at commit. Each table's firing reads them in a
    -- statement of its own, which names that table's columns alone.
    CASE TG_ARGV[0]
        WHEN 'rights' THEN
            unbacked := EXISTS (SELECT 1 FROM "tenancy"."SeatRights" r WHERE r."SeatId" = NEW."SeatId" AND r."UnitId" = NEW."UnitId" AND r."RoleId" = NEW."RoleId" AND r."Key" = NEW."Key" AND NOT EXISTS (SELECT 1 FROM "tenancy"."SeatRoleGrants" g
                        JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId"
                        JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId"
                        WHERE g."SeatId" = r."SeatId" AND g."UnitId" = r."UnitId" AND g."RoleId" = r."RoleId"
                          AND g."StartsAt" = r."StartsAt" AND g."EndsAt" IS NOT DISTINCT FROM r."EndsAt"
                          AND s."TenantId" = r."TenantId" AND s."Status" = 'Active'
                          AND ro."TenantId" = r."TenantId" AND ro."Status" = 'Active'
                          AND r."Key" = ANY (ro."Keys")));
        WHEN 'grants' THEN
            unbacked := EXISTS (SELECT 1 FROM "tenancy"."SeatRights" r WHERE r."SeatId" = OLD."SeatId" AND r."UnitId" = OLD."UnitId" AND r."RoleId" = OLD."RoleId" AND NOT EXISTS (SELECT 1 FROM "tenancy"."SeatRoleGrants" g
                        JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId"
                        JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId"
                        WHERE g."SeatId" = r."SeatId" AND g."UnitId" = r."UnitId" AND g."RoleId" = r."RoleId"
                          AND g."StartsAt" = r."StartsAt" AND g."EndsAt" IS NOT DISTINCT FROM r."EndsAt"
                          AND s."TenantId" = r."TenantId" AND s."Status" = 'Active'
                          AND ro."TenantId" = r."TenantId" AND ro."Status" = 'Active'
                          AND r."Key" = ANY (ro."Keys")));
        WHEN 'roles' THEN
            unbacked := EXISTS (SELECT 1 FROM "tenancy"."SeatRights" r WHERE r."RoleId" = OLD."Id" AND NOT EXISTS (SELECT 1 FROM "tenancy"."SeatRoleGrants" g
                        JOIN "tenancy"."Seats" s ON s."Id" = g."SeatId"
                        JOIN "tenancy"."Roles" ro ON ro."Id" = g."RoleId"
                        WHERE g."SeatId" = r."SeatId" AND g."UnitId" = r."UnitId" AND g."RoleId" = r."RoleId"
                          AND g."StartsAt" = r."StartsAt" AND g."EndsAt" IS NOT DISTINCT FROM r."EndsAt"
                          AND s."TenantId" = r."TenantId" AND s."Status" = 'Active'
                          AND ro."TenantId" = r."TenantId" AND ro."Status" = 'Active'
                          AND r."Key" = ANY (ro."Keys")));
    END CASE;
    IF unbacked THEN
        RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = 'tenancy_rights_backed_by_grants', MESSAGE = 'Every right a seat holds comes from a grant of an active role that holds its key.';
    END IF;
    RETURN NULL;
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".rights_backed_by_grants() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_rights_backed_by_grants ON "tenancy"."SeatRights";
CREATE CONSTRAINT TRIGGER tenancy_rights_backed_by_grants AFTER INSERT OR UPDATE ON "tenancy"."SeatRights"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
    EXECUTE FUNCTION "tenancy".rights_backed_by_grants('rights');
DROP TRIGGER IF EXISTS tenancy_rights_backed_by_grants ON "tenancy"."SeatRoleGrants";
CREATE CONSTRAINT TRIGGER tenancy_rights_backed_by_grants AFTER UPDATE OR DELETE ON "tenancy"."SeatRoleGrants"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
    EXECUTE FUNCTION "tenancy".rights_backed_by_grants('grants');
DROP TRIGGER IF EXISTS tenancy_rights_backed_by_grants ON "tenancy"."Roles";
CREATE CONSTRAINT TRIGGER tenancy_rights_backed_by_grants AFTER UPDATE ON "tenancy"."Roles"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (OLD."Status" <> NEW."Status" OR OLD."Keys" <> NEW."Keys" OR OLD."TenantId" <> NEW."TenantId")
    EXECUTE FUNCTION "tenancy".rights_backed_by_grants('roles');
CREATE OR REPLACE FUNCTION "tenancy".paths_follow_the_tree() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$
DECLARE
    touched pg_catalog.uuid[];
    below boolean := false;
    checked pg_catalog.uuid;
BEGIN
    -- The units whose paths the change reaches, and for a unit that moved, every unit below it too.
    IF TG_ARGV[0] = 'paths' AND TG_OP = 'INSERT' THEN
        touched := ARRAY[NEW."DescendantId"];
    ELSIF TG_ARGV[0] = 'paths' AND TG_OP = 'DELETE' THEN
        touched := ARRAY[OLD."DescendantId"];
    ELSIF TG_ARGV[0] = 'paths' THEN
        touched := ARRAY[NEW."DescendantId", OLD."DescendantId"];
    ELSE
        touched := ARRAY[NEW."Id"];
        below := TG_OP = 'UPDATE';
    END IF;
    FOR checked IN
        WITH RECURSIVE reached(id, depth) AS (
            SELECT pg_catalog.unnest(touched), 0
            UNION ALL
            SELECT u."Id", reached.depth + 1 FROM "tenancy"."OrganizationUnits" u JOIN reached ON u."ParentId" = reached.id
            WHERE below AND reached.depth < 64)
        SELECT DISTINCT id FROM reached
    LOOP
        IF EXISTS (
            WITH RECURSIVE up(id, parent, tenant, distance) AS (
                SELECT u."Id", u."ParentId", u."TenantId", 0 FROM "tenancy"."OrganizationUnits" u WHERE u."Id" = checked
                UNION ALL
                SELECT p."Id", p."ParentId", p."TenantId", up.distance + 1 FROM "tenancy"."OrganizationUnits" p
                JOIN up ON p."Id" = up.parent AND p."TenantId" = up.tenant
                WHERE up.distance < 64),
            expected AS (SELECT tenant, id AS ancestor, distance FROM up),
            actual AS (SELECT p."TenantId" AS tenant, p."AncestorId" AS ancestor, p."Distance" AS distance
                       FROM "tenancy"."OrganizationUnitPaths" p WHERE p."DescendantId" = checked)
            (SELECT * FROM expected EXCEPT SELECT * FROM actual)
            UNION ALL
            (SELECT * FROM actual EXCEPT SELECT * FROM expected)) THEN
            RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = 'tenancy_paths_follow_the_tree', MESSAGE = 'The paths of a unit are the units above it in the tree, and the unit itself.';
        END IF;
    END LOOP;
    RETURN NULL;
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".paths_follow_the_tree() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_paths_follow_the_tree ON "tenancy"."OrganizationUnitPaths";
CREATE CONSTRAINT TRIGGER tenancy_paths_follow_the_tree AFTER INSERT OR UPDATE OR DELETE ON "tenancy"."OrganizationUnitPaths"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
    EXECUTE FUNCTION "tenancy".paths_follow_the_tree('paths');
DROP TRIGGER IF EXISTS tenancy_paths_follow_the_tree ON "tenancy"."OrganizationUnits";
CREATE CONSTRAINT TRIGGER tenancy_paths_follow_the_tree AFTER INSERT OR UPDATE OF "ParentId" ON "tenancy"."OrganizationUnits"
    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
    EXECUTE FUNCTION "tenancy".paths_follow_the_tree('units');
CREATE OR REPLACE FUNCTION "tenancy".seat_identity_is_fixed() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
BEGIN
    RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = 'tenancy_seat_identity_is_fixed', MESSAGE = 'A seat keeps the identity and the tenant it was made with.';
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".seat_identity_is_fixed() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_seat_identity_is_fixed ON "tenancy"."Seats";
CREATE TRIGGER tenancy_seat_identity_is_fixed BEFORE UPDATE OF "TenantId", "Identity" ON "tenancy"."Seats"
    FOR EACH ROW WHEN ((OLD."TenantId", OLD."Identity") IS DISTINCT FROM (NEW."TenantId", NEW."Identity"))
    EXECUTE FUNCTION "tenancy".seat_identity_is_fixed();
CREATE OR REPLACE FUNCTION "tenancy".placement_is_fixed() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
BEGIN
    RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = 'tenancy_placement_is_fixed', MESSAGE = 'A placement keeps its seat, its unit and its tenant.';
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".placement_is_fixed() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_placement_is_fixed ON "tenancy"."SeatPlacements";
CREATE TRIGGER tenancy_placement_is_fixed BEFORE UPDATE OF "SeatId", "UnitId", "TenantId" ON "tenancy"."SeatPlacements"
    FOR EACH ROW WHEN ((OLD."SeatId", OLD."UnitId", OLD."TenantId") IS DISTINCT FROM (NEW."SeatId", NEW."UnitId", NEW."TenantId"))
    EXECUTE FUNCTION "tenancy".placement_is_fixed();
CREATE OR REPLACE FUNCTION "tenancy".grant_is_fixed() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
BEGIN
    RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = 'tenancy_grant_is_fixed', MESSAGE = 'A grant keeps its seat, its unit, its role and the seat that gave it.';
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".grant_is_fixed() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_grant_is_fixed ON "tenancy"."SeatRoleGrants";
CREATE TRIGGER tenancy_grant_is_fixed BEFORE UPDATE OF "SeatId", "UnitId", "RoleId", "GrantedBy" ON "tenancy"."SeatRoleGrants"
    FOR EACH ROW WHEN ((OLD."SeatId", OLD."UnitId", OLD."RoleId", OLD."GrantedBy") IS DISTINCT FROM (NEW."SeatId", NEW."UnitId", NEW."RoleId", NEW."GrantedBy"))
    EXECUTE FUNCTION "tenancy".grant_is_fixed();
CREATE OR REPLACE FUNCTION "tenancy".role_pack_is_fixed() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
BEGIN
    RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = 'tenancy_role_pack_is_fixed', MESSAGE = 'A role keeps the pack it was made from.';
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".role_pack_is_fixed() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_role_pack_is_fixed ON "tenancy"."Roles";
CREATE TRIGGER tenancy_role_pack_is_fixed BEFORE UPDATE OF "FromPack" ON "tenancy"."Roles"
    FOR EACH ROW WHEN ((OLD."FromPack") IS DISTINCT FROM (NEW."FromPack"))
    EXECUTE FUNCTION "tenancy".role_pack_is_fixed();
CREATE OR REPLACE FUNCTION "tenancy".seat_status_is_managed() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$
BEGIN
    -- System work and the tables' owner are no seat, and are not held to this.
    IF (SELECT tenancy.caller_seat()) IS NULL THEN
        RETURN NEW;
    END IF;
    IF NOT (SELECT tenancy.holds_tenant_wide('tenancy.seats.manage'))
       OR EXISTS (SELECT 1 FROM "tenancy"."SeatRoleGrants" g
                   JOIN "tenancy"."Roles" r ON r."Id" = g."RoleId" AND r."Status" = 'Active'
                   CROSS JOIN LATERAL pg_catalog.unnest(r."Keys") AS managed(k)
                   WHERE g."SeatId" = NEW."Id"
                     AND (g."EndsAt" IS NULL OR g."EndsAt" > pg_catalog.now())
                     AND tenancy.manages_access(managed.k)
                     AND NOT (g."UnitId" = ANY (ARRAY(SELECT tenancy.units_where_i_hold(managed.k))))) THEN
        RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'tenancy_seat_status_is_managed', HINT = 'ddd:access.refused', MESSAGE = 'A seat''s status is changed by a seat that manages seats for the whole tenant and holds, at each of its grants, the keys that manage access the grant gives.';
    END IF;
    RETURN NEW;
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".seat_status_is_managed() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_seat_status_is_managed ON "tenancy"."Seats";
CREATE TRIGGER tenancy_seat_status_is_managed BEFORE UPDATE OF "Status" ON "tenancy"."Seats"
    FOR EACH ROW WHEN (OLD."Status" IS DISTINCT FROM NEW."Status")
    EXECUTE FUNCTION "tenancy".seat_status_is_managed();
CREATE OR REPLACE FUNCTION "tenancy".role_follows_its_pack() RETURNS trigger
    LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $body$
BEGIN
    -- System work and the tables' owner are no seat, and are not held to this.
    IF (SELECT tenancy.caller_seat()) IS NOT NULL THEN
        RAISE EXCEPTION USING ERRCODE = 'insufficient_privilege', CONSTRAINT = 'tenancy_role_follows_its_pack', HINT = 'ddd:access.refused', MESSAGE = 'What its pack gave a role is written as the role is made from the pack, and as Tenancy''s own system work makes it follow the pack: by no seat.';
    END IF;
    RETURN NEW;
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".role_follows_its_pack() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_role_follows_its_pack ON "tenancy"."Roles";
CREATE TRIGGER tenancy_role_follows_its_pack BEFORE UPDATE OF "KeysFromPack" ON "tenancy"."Roles"
    FOR EACH ROW WHEN (OLD."KeysFromPack" IS DISTINCT FROM NEW."KeysFromPack")
    EXECUTE FUNCTION "tenancy".role_follows_its_pack();
CREATE OR REPLACE FUNCTION "tenancy".invitation_terms_are_fixed() RETURNS trigger
    LANGUAGE plpgsql SET search_path = '' AS $body$
BEGIN
    RAISE EXCEPTION USING ERRCODE = 'check_violation', CONSTRAINT = 'tenancy_invitation_terms_are_fixed', MESSAGE = 'What an invitation offers does not change, and an invitation that is over stays over.';
END
$body$;
REVOKE ALL ON FUNCTION "tenancy".invitation_terms_are_fixed() FROM PUBLIC;
DROP TRIGGER IF EXISTS tenancy_invitation_terms_are_fixed ON "tenancy"."Invitations";
CREATE TRIGGER tenancy_invitation_terms_are_fixed BEFORE UPDATE ON "tenancy"."Invitations"
    FOR EACH ROW WHEN ((OLD."TenantId", OLD."UnitId", OLD."RoleId", OLD."GrantUntil", OLD."IssuedAt", OLD."ExpiresAt", OLD."IssuedBy", OLD."IssuedAsSystem") IS DISTINCT FROM (NEW."TenantId", NEW."UnitId", NEW."RoleId", NEW."GrantUntil", NEW."IssuedAt", NEW."ExpiresAt", NEW."IssuedBy", NEW."IssuedAsSystem")
        OR (OLD."State" <> 'Open' AND NEW."State" IS DISTINCT FROM OLD."State")
        OR (OLD."AcceptedAs" IS NOT NULL AND NEW."AcceptedAs" IS DISTINCT FROM OLD."AcceptedAs")
        OR (NEW."State" = 'Open' AND NEW."Address" IS DISTINCT FROM OLD."Address"))
    EXECUTE FUNCTION "tenancy".invitation_terms_are_fixed();

-- The rows of a table that only grows do not change. An update and a truncate are refused for every role,
-- the table's owner and a superuser included, and so is a delete, until a row is older than its table keeps it.
DO $ddd$
DECLARE
    body constant text := $function$
BEGIN
    IF TG_OP = 'DELETE' AND TG_ARGV[0] <> '' THEN
        IF (pg_catalog.to_jsonb(OLD) ->> TG_ARGV[1])::pg_catalog.timestamptz
               < pg_catalog.now() - pg_catalog.make_interval(secs => TG_ARGV[0]::pg_catalog.float8) THEN
            RETURN OLD;
        END IF;
    END IF;
    RAISE EXCEPTION 'A kept row does not change.' USING
        ERRCODE = '55000',
        DETAIL = pg_catalog.format('%s on %I.%I was refused: the table only grows.', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME);
END
$function$;
BEGIN
    IF pg_catalog.to_regnamespace('ddd') IS NULL THEN
        CREATE SCHEMA ddd;
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_proc
                   WHERE oid = pg_catalog.to_regprocedure('ddd.refuse_changes_to_kept_rows()') AND prosrc = body) THEN
        EXECUTE 'CREATE OR REPLACE FUNCTION ddd.refuse_changes_to_kept_rows() RETURNS trigger LANGUAGE plpgsql SET search_path = '''' AS ' || pg_catalog.quote_literal(body);
    END IF;
END
$ddd$;
CREATE OR REPLACE TRIGGER ddd_kept_rows BEFORE UPDATE OR DELETE ON tenancy."TenancyEventLog"
    FOR EACH ROW EXECUTE FUNCTION ddd.refuse_changes_to_kept_rows('', 'RecordedAt');
ALTER TABLE tenancy."TenancyEventLog" ENABLE ALWAYS TRIGGER ddd_kept_rows;
CREATE OR REPLACE TRIGGER ddd_kept_rows_truncate BEFORE TRUNCATE ON tenancy."TenancyEventLog"
    FOR EACH STATEMENT EXECUTE FUNCTION ddd.refuse_changes_to_kept_rows();
ALTER TABLE tenancy."TenancyEventLog" ENABLE ALWAYS TRIGGER ddd_kept_rows_truncate;

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
