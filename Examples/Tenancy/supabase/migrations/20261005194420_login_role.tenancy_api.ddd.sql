-- Written by DDDToolkit for the role the application logs in as, tenancy_api.
-- Written from SupabaseLoginRole and SupabaseRowAccessRoles; change those, not this file. Every file like it
-- says what the role is now: it makes it, takes back what the one before it granted and no caller runs as
-- any more, and grants the roles callers run as.
--
-- The role owns nothing and is given no privilege on a table, a schema or a function. All it holds is the
-- right to switch to the roles its callers run as, each held to its policies and its privileges:
--   anon              a caller without a token
--   authenticated     a signed-in user
--   ddd_system_in     the application's own work, inside the policies
--   ddd_system        the toolkit's bookkeeping: the outbox, the inbox and which migrations ran
--   tenancy_operator  a signed-in user whose token carries the role tenancy_operator
-- It is NOINHERIT, so it has none of their privileges until it switches to one of them. A statement that
-- reaches the application's connection can always go back to the role that logged in; this is what makes
-- that role worth nothing.
--
-- No LOGIN and no password here: a migration is kept in a repository, and a password is not. Whoever deploys
-- turns the login on once, as the database's owner, with a secret of that deployment:
--     ALTER ROLE tenancy_api WITH LOGIN PASSWORD '...';

DO $ddd$
DECLARE
    attributes text;
    inherited text;
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'tenancy_api') THEN
        BEGIN
            CREATE ROLE tenancy_api NOLOGIN NOINHERIT;
        EXCEPTION WHEN duplicate_object OR unique_violation THEN
            NULL; -- made by a migration that ran at the same time
        END;
    END IF;
    SELECT pg_catalog.concat_ws(' ', CASE WHEN rolsuper THEN 'NOSUPERUSER' END, CASE WHEN rolbypassrls THEN 'NOBYPASSRLS' END,
               CASE WHEN rolcreaterole THEN 'NOCREATEROLE' END, CASE WHEN rolreplication THEN 'NOREPLICATION' END,
               CASE WHEN rolinherit THEN 'NOINHERIT' END) INTO attributes
    FROM pg_catalog.pg_roles WHERE rolname = 'tenancy_api';
    IF attributes <> '' THEN
        RAISE EXCEPTION USING
            MESSAGE = 'The role tenancy_api exists, and is a superuser, may bypass row level security, may create roles, may replicate or has the privileges of the roles it is granted without switching to them: the hint says which. Whoever reaches its connection would not be held to the policies.',
            HINT = 'ALTER ROLE tenancy_api ' || attributes || ';';
    END IF;
    SELECT pg_catalog.string_agg(DISTINCT member_of, ', ' ORDER BY member_of) INTO inherited
    FROM (SELECT m.roleid::pg_catalog.regrole::pg_catalog.text AS member_of FROM pg_catalog.pg_auth_members m
          WHERE m.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = 'tenancy_api')
            AND coalesce((pg_catalog.to_jsonb(m) ->> 'inherit_option')::pg_catalog.bool, false)) inheriting;
    IF inherited IS NOT NULL THEN
        RAISE EXCEPTION USING
            MESSAGE = 'The role tenancy_api has the privileges of ' || inherited || ' without switching to them: they were granted while it inherited, and NOINHERIT holds only for the grants after it. Whoever reaches its connection would not be held to their policies.',
            HINT = 'GRANT ' || inherited || ' TO tenancy_api WITH INHERIT FALSE;';
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'ddd_system_in') THEN
        BEGIN
            CREATE ROLE ddd_system_in NOLOGIN NOINHERIT;
        EXCEPTION WHEN duplicate_object OR unique_violation THEN
            NULL; -- made by a migration that ran at the same time
        END;
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'ddd_system') THEN
        BEGIN
            CREATE ROLE ddd_system NOLOGIN NOINHERIT;
        EXCEPTION WHEN duplicate_object OR unique_violation THEN
            NULL; -- made by a migration that ran at the same time
        END;
    END IF;
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'tenancy_operator') THEN
        BEGIN
            CREATE ROLE tenancy_operator NOLOGIN NOINHERIT;
        EXCEPTION WHEN duplicate_object OR unique_violation THEN
            NULL; -- made by a migration that ran at the same time
        END;
    END IF;
END
$ddd$;

DO $ddd$
BEGIN
    IF NOT pg_catalog.pg_has_role('tenancy_api'::pg_catalog.name, 'anon'::pg_catalog.name,
            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
        BEGIN
            GRANT anon TO tenancy_api;
        EXCEPTION WHEN unique_violation THEN
            NULL; -- granted by a migration that ran at the same time
        END;
    END IF;
    IF NOT pg_catalog.pg_has_role('tenancy_api'::pg_catalog.name, 'authenticated'::pg_catalog.name,
            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
        BEGIN
            GRANT authenticated TO tenancy_api;
        EXCEPTION WHEN unique_violation THEN
            NULL; -- granted by a migration that ran at the same time
        END;
    END IF;
    IF NOT pg_catalog.pg_has_role('tenancy_api'::pg_catalog.name, 'ddd_system_in'::pg_catalog.name,
            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
        BEGIN
            GRANT ddd_system_in TO tenancy_api;
        EXCEPTION WHEN unique_violation THEN
            NULL; -- granted by a migration that ran at the same time
        END;
    END IF;
    IF NOT pg_catalog.pg_has_role('tenancy_api'::pg_catalog.name, 'ddd_system'::pg_catalog.name,
            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
        BEGIN
            GRANT ddd_system TO tenancy_api;
        EXCEPTION WHEN unique_violation THEN
            NULL; -- granted by a migration that ran at the same time
        END;
    END IF;
    IF NOT pg_catalog.pg_has_role('tenancy_api'::pg_catalog.name, 'tenancy_operator'::pg_catalog.name,
            CASE WHEN pg_catalog.current_setting('server_version_num')::integer >= 160000 THEN 'SET' ELSE 'MEMBER' END) THEN
        BEGIN
            GRANT tenancy_operator TO tenancy_api;
        EXCEPTION WHEN unique_violation THEN
            NULL; -- granted by a migration that ran at the same time
        END;
    END IF;
END
$ddd$;
