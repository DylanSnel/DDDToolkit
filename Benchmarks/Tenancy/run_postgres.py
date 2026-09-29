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

# the update above leaves a dead copy of every project row and clears the visibility map; without this the
# index-only scans the set-based forms use go to the heap for every row, and they are measured slow for it
psql("vacuum analyze project")

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

# Moving branch 12, with its 10 teams and 20 sub-teams, under region 3, in each form. Every move runs inside
# a transaction that is rolled back, so each run starts from the same tree; the time is the server's
# execution time from EXPLAIN ANALYZE, the closure table's two statements added together.
MOVES = {
    "text path": ["""update unit set path_text = '/1/3/' || substr(path_text, length('/1/2/') + 1)
        where tenant_id = 1 and path_text >= '/1/2/12/' and path_text < '/1/2/120'"""],
    "ltree": ["""update unit set path_ltree = 'n1.n3'::ltree || subpath(path_ltree, 2)
        where tenant_id = 1 and path_ltree <@ 'n1.n2.n12'"""],
    "closure table": [
        """delete from unit_ancestor ua
            where ua.tenant_id = 1
              and ua.unit_id in (select unit_id from unit_ancestor where tenant_id = 1 and ancestor_id = 12)
              and ua.ancestor_id not in (select unit_id from unit_ancestor where tenant_id = 1 and ancestor_id = 12)""",
        """insert into unit_ancestor
            select 1, a.ancestor_id, s.unit_id
              from (select unit_id from unit_ancestor where tenant_id = 1 and ancestor_id = 12) s
             cross join (select ancestor_id from unit_ancestor where tenant_id = 1 and unit_id = 3) a""",
    ],
}
print()
for name, statements in MOVES.items():
    times = []
    for _ in range(8):
        script = "begin;\n" + "\n".join("explain (analyze) " + s + ";" for s in statements) + "\nrollback;"
        out = subprocess.run(["docker", "exec", "-i", "tenancy-bench-pg", "psql", "-U", "postgres", "-At"],
                             input=script, capture_output=True, text=True)
        if out.returncode != 0:
            raise RuntimeError(out.stderr)
        times.append(sum(float(t) for t in re.findall(r"Execution Time: ([\d.]+) ms", out.stdout)))
    print(f"move {name:18} | median {statistics.median(times[1:]):6.2f} ms")
