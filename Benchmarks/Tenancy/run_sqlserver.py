import subprocess, statistics, os, time

os.environ["MSYS_NO_PATHCONV"] = "1"
SQLCMD = ["docker", "exec", "tenancy-bench-mssql", "/opt/mssql-tools18/bin/sqlcmd", "-C", "-S", "localhost",
          "-U", "sa", "-P", os.environ["BENCH_MSSQL_PASSWORD"], "-d", "bench", "-h", "-1", "-W"]

def sql(q):
    out = subprocess.run(SQLCMD + ["-Q", "set nocount on; " + q], capture_output=True, text=True)
    if out.returncode != 0 or "Msg " in out.stdout:
        raise RuntimeError(out.stdout + out.stderr)
    return out.stdout.strip()

GRANT = "g.tenant_id = 1 and g.actor = {a} and g.[key] = 'project.update'"
QUERIES = {
    "text path range (BIN2 collation)": """select count(*) from project p where p.tenant_id = 1 and p.unit_id in (
        select u.id from unit u join org_grant g on g.tenant_id = u.tenant_id
          and u.path_text >= g.scope_text and u.path_text < g.scope_upper
        where u.tenant_id = 1 and """ + GRANT + ")",
    "closure table": """select count(*) from project p where p.tenant_id = 1 and p.unit_id in (
        select ua.unit_id from unit_ancestor ua join org_grant g on g.tenant_id = ua.tenant_id and ua.ancestor_id = g.unit_id
        where ua.tenant_id = 1 and """ + GRANT + ")",
}
ACTORS = {1: "regional manager (2 branches)", 2: "director (root)", 3: "team lead (1 team)"}

for a, who in ACTORS.items():
    for name, q in QUERIES.items():
        query = q.format(a=a)
        # sysdatetime() moves in steps of about 4 ms, so each of the nine samples is a batch of 200 executions
        # timed together and divided: a single execution is shorter than the clock can see.
        timing = f"""
declare @c int, @t0 datetime2, @i int, @r int = 0, @n int = 200; declare @ms table (v float);
select @c = ({query});  -- warm
while @r < 9 begin
  set @i = 0; set @t0 = sysdatetime();
  while @i < @n begin select @c = ({query}); set @i += 1; end
  insert into @ms values (datediff(microsecond, @t0, sysdatetime()) / 1000.0 / @n);
  set @r += 1;
end
select cast(@c as varchar) + ' ' + (select string_agg(cast(v as varchar), ',') from @ms);"""
        count, times = sql(timing).split(" ")
        med = statistics.median(float(x) for x in times.split(","))
        print(f"{who:32} | {name:34} | rows {count:>6} | median {med:8.2f} ms")
