-- What the earlier files made under the names that said "caller". Written by hand: every other file in this
-- directory but *_tenancy_login_role.sql is written by the build of Examples.Tenancy.Exporter.
--
-- The access files before this one make the same functions under their new names, and no policy asks an old
-- one after them. An access file drops the functions it no longer writes, unless a policy of a module whose
-- file comes later still asks one, so Tenancy's old functions outlived the tenants' file. The trigger that held
-- who changed a row, and the procedure that sets the user for one transaction, are no access functions, so no
-- file drops them. The old trigger asks tenancy.caller_seat(), and would refuse every write once that is gone,
-- so it goes first.
--
-- Each statement drops only what is there, so the file runs on a database made by any of the files above.
DROP TRIGGER IF EXISTS tenancy_attribution_matches_caller ON "projects"."Projects";
DROP FUNCTION IF EXISTS "projects".attribution_matches_caller();
DROP TRIGGER IF EXISTS tenancy_attribution_matches_caller ON "inspections"."Inspections";
DROP FUNCTION IF EXISTS "inspections".attribution_matches_caller();

DROP FUNCTION IF EXISTS "tenancy".caller_permissions();
DROP FUNCTION IF EXISTS "tenancy".caller_seat_row();
DROP FUNCTION IF EXISTS "tenancy".caller_tenant();
DROP FUNCTION IF EXISTS "tenancy".caller_seat();
DROP FUNCTION IF EXISTS "tenancy".permissions_a_move_changes(uuid, uuid);

DROP PROCEDURE IF EXISTS "ddd".use_caller(text, text, text[], text[]);
