-- What the earlier files made under the name that said "caller". Written by hand, as the other files in this
-- directory without .ddd. in their names are.
--
-- The access file before this one makes the procedure that sets the user for one transaction under its new
-- name, ddd.use_current_user. The procedure is no access function, so no file drops the old one.
--
-- The statement drops only what is there, so the file runs on a database made by any of the files above.
DROP PROCEDURE IF EXISTS "ddd".use_caller(text, text, text[], text[]);
