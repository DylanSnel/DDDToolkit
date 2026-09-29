set nocount on;
if db_id('bench') is null create database bench;
go
use bench;
go
if object_id('org_grant') is not null drop table org_grant;
if object_id('unit_ancestor') is not null drop table unit_ancestor;
if object_id('project') is not null drop table project;
if object_id('unit') is not null drop table unit;
create table unit (
  tenant_id int not null, id int not null, parent_id int null, depth int not null,
  path_text varchar(400) collate Latin1_General_BIN2 not null,
  constraint pk_unit primary key (tenant_id, id));
go
declare @t int = 1, @next int, @big bit;
while @t <= 50
begin
  set @big = case when @t = 1 then 1 else 0 end;
  insert into unit values (@t, 1, null, 1, '/1/');
  set @next = 2;
  insert into unit select @t, @next + n - 1, 1, 2, '/1/' + cast(@next + n - 1 as varchar) + '/'
    from (select top (10) row_number() over (order by (select null)) n from sys.all_objects) g;
  set @next = @next + 10;
  insert into unit select @t, @next + (r.rn - 1) * 10 + g.n - 1, r.id, 3, r.path_text + cast(@next + (r.rn - 1) * 10 + g.n - 1 as varchar) + '/'
    from (select id, path_text, row_number() over (order by id) rn from unit where tenant_id = @t and depth = 2) r
    cross join (select top (10) row_number() over (order by (select null)) n from sys.all_objects) g;
  set @next = @next + 100;
  if @big = 1
  begin
    insert into unit select @t, @next + (r.rn - 1) * 10 + g.n - 1, r.id, 4, r.path_text + cast(@next + (r.rn - 1) * 10 + g.n - 1 as varchar) + '/'
      from (select id, path_text, row_number() over (order by id) rn from unit where tenant_id = @t and depth = 3) r
      cross join (select top (10) row_number() over (order by (select null)) n from sys.all_objects) g;
    set @next = @next + 1000;
    insert into unit select @t, @next + (r.rn - 1) * 2 + g.n - 1, r.id, 5, r.path_text + cast(@next + (r.rn - 1) * 2 + g.n - 1 as varchar) + '/'
      from (select id, path_text, row_number() over (order by id) rn from unit where tenant_id = @t and depth = 4) r
      cross join (select top (2) row_number() over (order by (select null)) n from sys.all_objects) g;
  end
  set @t = @t + 1;
end
go
create table project (tenant_id int not null, id int not null, unit_id int not null, name varchar(40) not null,
  constraint pk_project primary key (tenant_id, id));
insert into project
select u.tenant_id, row_number() over (partition by u.tenant_id order by u.id, g.n), u.id, 'p' + cast(u.id as varchar) + '-' + cast(g.n as varchar)
from unit u
cross apply (select top (case when u.tenant_id = 1 then 16 else 9 end) row_number() over (order by (select null)) n from sys.all_objects) g;
create index ix_project_unit on project (tenant_id, unit_id);
go
create table unit_ancestor (tenant_id int not null, ancestor_id int not null, unit_id int not null,
  constraint pk_unit_ancestor primary key (tenant_id, ancestor_id, unit_id));
insert into unit_ancestor
select d.tenant_id, a.id, d.id from unit d join unit a on a.tenant_id = d.tenant_id and d.path_text like a.path_text + '%';
create index ix_unit_path on unit (tenant_id, path_text);
go
create table org_grant (tenant_id int not null, actor int not null, [key] varchar(60) not null, unit_id int not null,
  scope_text varchar(400) collate Latin1_General_BIN2 not null, scope_upper varchar(400) collate Latin1_General_BIN2 not null,
  constraint pk_org_grant primary key (tenant_id, actor, [key], unit_id));
insert into org_grant
select u.tenant_id, a.actor, 'project.update', u.id, u.path_text, left(u.path_text, len(u.path_text) - 1) + '0'
from (values (1, 12), (1, 13), (2, 1), (3, 112)) a(actor, unit)
join unit u on u.tenant_id = 1 and u.id = a.unit;
update statistics unit; update statistics project; update statistics unit_ancestor; update statistics org_grant;
go
select 'units', count(*) from unit union all select 'projects', count(*) from project
union all select 'closure', count(*) from unit_ancestor union all select 'grants', count(*) from org_grant;
go
