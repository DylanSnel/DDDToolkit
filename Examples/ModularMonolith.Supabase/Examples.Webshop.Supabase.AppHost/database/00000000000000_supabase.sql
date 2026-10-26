-- What every Supabase project has before its first migration and a plain Postgres does not: the roles
-- PostgREST switches between, and the auth functions policies call, defined as Supabase defines them.
-- Only for this AppHost's container. A Supabase project and the CLI's local stack have their own, which
-- is why this file is not in supabase/migrations. Named to run first: Postgres runs its init files in
-- name order, and the migrations are named by timestamp.
create role anon nologin noinherit;
create role authenticated nologin noinherit;
create role service_role nologin noinherit bypassrls;

create schema auth;

create function auth.uid() returns uuid language sql stable as $$
    select coalesce(
        nullif(current_setting('request.jwt.claim.sub', true), ''),
        (nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'sub'))::uuid
$$;

create function auth.jwt() returns jsonb language sql stable as $$
    select coalesce(
        nullif(current_setting('request.jwt.claim', true), ''),
        nullif(current_setting('request.jwt.claims', true), ''))::jsonb
$$;

create function auth.role() returns text language sql stable as $$
    select coalesce(
        nullif(current_setting('request.jwt.claim.role', true), ''),
        (nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'role'))::text
$$;

grant usage on schema auth to anon, authenticated, service_role;
