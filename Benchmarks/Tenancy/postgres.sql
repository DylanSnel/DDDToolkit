-- Tree containment benchmark: ltree vs text path (C collation, range) vs closure table.
-- Big tenant 1: root, 10 regions, 100 branches, 1000 teams, 2000 subteams = 3111 units, 16 projects each = 49,776 projects.
-- 49 small tenants: root, 10 regions, 100 branches = 111 units, 9 projects each = 999 projects each.
\timing off
create extension if not exists ltree;
-- for every session that measures, not only this one: the timings are of one plan, not of a parallel one
alter database postgres set max_parallel_workers_per_gather = 0;

create table unit (
  tenant_id int not null,
  id int not null,
  parent_id int,
  depth int not null,
  path_ltree ltree not null,
  path_text text collate "C" not null,
  primary key (tenant_id, id)
);

-- generate tree per tenant
do $$
declare t int; next_id int; big boolean;
begin
  for t in 1..50 loop
    big := (t = 1);
    insert into unit values (t, 1, null, 1, 'n1', '/1/');
    next_id := 2;
    -- regions
    insert into unit select t, next_id + g - 1, 1, 2, ('n1.n' || (next_id + g - 1))::ltree, '/1/' || (next_id + g - 1) || '/' from generate_series(1,10) g;
    next_id := next_id + 10;
    -- branches: 10 per region
    insert into unit
    select t, next_id + (r.rn - 1) * 10 + g - 1, r.id, 3,
           (r.path_ltree::text || '.n' || (next_id + (r.rn - 1) * 10 + g - 1))::ltree,
           r.path_text || (next_id + (r.rn - 1) * 10 + g - 1) || '/'
    from (select id, path_ltree, path_text, row_number() over (order by id) rn from unit where tenant_id = t and depth = 2) r,
         generate_series(1,10) g;
    next_id := next_id + 100;
    if big then
      insert into unit
      select t, next_id + (r.rn - 1) * 10 + g - 1, r.id, 4,
             (r.path_ltree::text || '.n' || (next_id + (r.rn - 1) * 10 + g - 1))::ltree,
             r.path_text || (next_id + (r.rn - 1) * 10 + g - 1) || '/'
      from (select id, path_ltree, path_text, row_number() over (order by id) rn from unit where tenant_id = t and depth = 3) r,
           generate_series(1,10) g;
      next_id := next_id + 1000;
      insert into unit
      select t, next_id + (r.rn - 1) * 2 + g - 1, r.id, 5,
             (r.path_ltree::text || '.n' || (next_id + (r.rn - 1) * 2 + g - 1))::ltree,
             r.path_text || (next_id + (r.rn - 1) * 2 + g - 1) || '/'
      from (select id, path_ltree, path_text, row_number() over (order by id) rn from unit where tenant_id = t and depth = 4) r,
           generate_series(1,2) g;
    end if;
  end loop;
end $$;

create table project (
  tenant_id int not null,
  id int not null,
  unit_id int not null,
  name text not null,
  primary key (tenant_id, id)
);
-- deterministic spread of projects over all units of the tenant
insert into project
select u.tenant_id, row_number() over (partition by u.tenant_id order by u.id, g), u.id, 'p' || u.id || '-' || g
from unit u
cross join lateral generate_series(1, case when u.tenant_id = 1 then 16 else 9 end) g;
create index ix_project_unit on project (tenant_id, unit_id);

-- closure table: (ancestor, descendant) including self
create table unit_ancestor (
  tenant_id int not null,
  ancestor_id int not null,
  unit_id int not null,
  primary key (tenant_id, ancestor_id, unit_id)
);
insert into unit_ancestor
select d.tenant_id, a.id, d.id
from unit d join unit a on a.tenant_id = d.tenant_id and a.path_ltree @> d.path_ltree;

create index ix_unit_path_gist on unit using gist (path_ltree);
create index ix_unit_path_text on unit (tenant_id, path_text);

-- org grants projection: actor holds key at unit (scope)
create table org_grant (
  tenant_id int not null,
  actor int not null,
  key text not null,
  unit_id int not null,
  scope_ltree ltree not null,
  scope_text text collate "C" not null,
  scope_upper text collate "C" not null,
  primary key (tenant_id, actor, key, unit_id)
);
-- actor 1: regional manager, holds project.update at two branches (depth 3)
-- actor 2: director, holds project.update at the root
-- actor 3: team lead, holds it at one team (depth 4)
insert into org_grant
select u.tenant_id, a.actor, 'project.update', u.id, u.path_ltree, u.path_text,
       left(u.path_text, length(u.path_text) - 1) || '0'
from (values (1, 12), (1, 13), (2, 1), (3, 112)) a(actor, unit)
join unit u on u.tenant_id = 1 and u.id = a.unit;

analyze;

select 'units' what, count(*) from unit union all
select 'units tenant 1', count(*) from unit where tenant_id = 1 union all
select 'projects', count(*) from project union all
select 'projects tenant 1', count(*) from project where tenant_id = 1 union all
select 'closure rows', count(*) from unit_ancestor union all
select 'grants', count(*) from org_grant;
