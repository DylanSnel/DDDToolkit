-- Row level security for the modules' own queries. With Supabase:Url set, the host runs every request's
-- queries as the role PostgREST would give that request: authenticated for a signed-in user, anon without
-- a token (DDDToolkit.AspNetCore.Supabase). Those two roles have no privileges in the module schemas
-- until this migration gives them some.
--
-- Granting a table does not open it to anybody by itself. The module schemas are not among the Data API's
-- exposed schemas (supabase/config.toml, and the project's API settings), so nothing Supabase serves
-- reaches them: only the application does, and policies decide which rows each caller gets, as the next
-- migration does for orders. Keep the module schemas out of the exposed list; exposing one would hand
-- these privileges to anyone holding the publishable key.
--
-- Written by hand, not exported. The default privileges cover every table a module's migrations create
-- from now on; a module added later needs its schema in this list, in a migration of its own.
do $$
declare
    module text;
begin
    foreach module in array array['catalog', 'ordering', 'inventory', 'payments', 'shipping'] loop
        execute format('grant usage on schema %I to anon, authenticated', module);
        execute format('grant select, insert, update, delete on all tables in schema %I to anon, authenticated', module);
        execute format('alter default privileges in schema %I grant select, insert, update, delete on tables to anon, authenticated', module);
    end loop;
end $$;
