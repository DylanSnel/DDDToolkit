\timing on
begin;
-- move branch 12 (region 2) under region 3: text path
update unit set path_text = '/1/3/' || substr(path_text, length('/1/2/') + 1)
 where tenant_id = 1 and path_text >= '/1/2/12/' and path_text < '/1/2/120';
rollback;
begin;
-- same move: ltree
update unit set path_ltree = 'n1.n3'::ltree || subpath(path_ltree, 2)
 where tenant_id = 1 and path_ltree <@ 'n1.n2.n12';
rollback;
begin;
-- same move: closure table (drop old ancestors outside the subtree, add new ones)
delete from unit_ancestor ua
 where ua.tenant_id = 1
   and ua.unit_id in (select unit_id from unit_ancestor where tenant_id = 1 and ancestor_id = 12)
   and ua.ancestor_id not in (select unit_id from unit_ancestor where tenant_id = 1 and ancestor_id = 12);
insert into unit_ancestor
select 1, a.ancestor_id, s.unit_id
from (select unit_id from unit_ancestor where tenant_id = 1 and ancestor_id = 12) s
cross join (select ancestor_id from unit_ancestor where tenant_id = 1 and unit_id = 3) a;
rollback;
