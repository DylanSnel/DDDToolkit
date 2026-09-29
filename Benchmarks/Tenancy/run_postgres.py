import subprocess, re, statistics, os

os.environ["MSYS_NO_PATHCONV"] = "1"

def psql(sql):
    out = subprocess.run(["docker", "exec", "tenancy-bench-pg", "psql", "-U", "postgres", "-At", "-c", sql],
                         capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(out.stderr)
    return out.stdout

# reference: the organization path copied onto every project, checked per row
psql("""
do $$ begin
  if not exists (select 1 from information_schema.columns where table_name='project' and column_name='org_path') then
    alter table project add column org_path ltree;
    update project p set org_path = u.path_ltree from unit u where u.tenant_id = p.tenant_id and u.id = p.unit_id;
    alter table project alter column org_path set not null;
    create index ix_project_org_path on project using gist (org_path);
    analyze project;
  end if;
end $$;
""")

GRANT = "g.tenant_id = 1 and g.actor = {a} and g.key = 'project.update'"
QUERIES = {
    "ltree (units, set)": """select count(*) from project p where p.tenant_id = 1 and p.unit_id in (
        select u.id from unit u join org_grant g on g.tenant_id = u.tenant_id and u.path_ltree <@ g.scope_ltree
        where u.tenant_id = 1 and """ + GRANT + ")",
    "text path range (C collation)": """select count(*) from project p where p.tenant_id = 1 and p.unit_id in (
        select u.id from unit u join org_grant g on g.tenant_id = u.tenant_id
          and u.path_text >= g.scope_text and u.path_text < g.scope_upper
        where u.tenant_id = 1 and """ + GRANT + ")",
    "closure table": """select count(*) from project p where p.tenant_id = 1 and p.unit_id in (
        select ua.unit_id from unit_ancestor ua join org_grant g on g.tenant_id = ua.tenant_id and ua.ancestor_id = g.unit_id
        where ua.tenant_id = 1 and """ + GRANT + ")",
    "path copied on every project, per row": """select count(*) from project p where p.tenant_id = 1 and exists (
        select 1 from org_grant g where """ + GRANT + " and g.scope_ltree @> p.org_path)",
}
ACTORS = {1: "regional manager (2 branches)", 2: "director (root)", 3: "team lead (1 team)"}

rows = []
for a, who in ACTORS.items():
    for name, q in QUERIES.items():
        sql = q.format(a=a)
        count = psql(sql).strip()
        times = []
        for _ in range(7):
            plan = psql("explain (analyze, buffers) " + sql)
            times.append(float(re.search(r"Execution Time: ([\d.]+) ms", plan).group(1)))
        plan = psql("explain (analyze, buffers, costs off, timing off) " + sql)
        rows.append((who, name, count, statistics.median(times), plan))

for who, name, count, med, plan in rows:
    print(f"{who:32} | {name:44} | rows {count:>6} | median {med:8.2f} ms")
print()
for who, name, count, med, plan in rows:
    if who.startswith("regional"):
        print("=== plan:", who, "/", name)
        print(plan)
