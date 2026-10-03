-- The role the sample's host logs in as. Written by hand: every other file in this directory is written by the
-- build of Examples.Tenancy.Exporter.
--
-- The role owns nothing and is given no privilege on any table. All it holds is the right to become the roles
-- its callers run as, each of which is held to its policies and to the privileges written from them:
--   authenticated    a signed-in user, through their seat
--   anon             a caller without a token, to whom every table here is closed
--   ddd_system_in    the application's own work in one tenant
--   ddd_system       the toolkit's bookkeeping: the outbox, and which migrations ran
--   tenancy_operator   the application's own staff, who read across tenants and write nothing
-- It is NOINHERIT, so it has none of their privileges until it switches to one of them. A statement that
-- reaches the host's connection can always go back to the role that logged in; this is what makes that
-- role worth nothing.
--
-- This file runs after the access files, which make the last three roles.
--
-- No LOGIN and no password here: a migration is kept in a repository, and a password is not. Whoever deploys
-- turns the login on once, as the database's owner, with a secret of that deployment:
--     ALTER ROLE tenancy_api WITH LOGIN PASSWORD '...';
DO $tenancy$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'tenancy_api') THEN
        CREATE ROLE tenancy_api NOLOGIN NOINHERIT;
    END IF;

    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'tenancy_api' AND (rolsuper OR rolbypassrls OR rolinherit)) THEN
        RAISE EXCEPTION 'The role tenancy_api is a superuser, bypasses row level security or inherits the privileges of the roles it is given, so it would not be held to the policies.'
            USING HINT = 'ALTER ROLE tenancy_api NOSUPERUSER NOBYPASSRLS NOINHERIT;';
    END IF;
END
$tenancy$;

GRANT anon, authenticated, ddd_system_in, ddd_system, tenancy_operator TO tenancy_api;
