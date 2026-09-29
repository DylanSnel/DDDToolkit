import sqlite3, time, statistics
db = sqlite3.connect(":memory:")
c = db.cursor()
c.executescript("""
create table unit(tenant_id int, id int, parent_id int, depth int, path_text text, primary key(tenant_id,id));
create table project(tenant_id int, id int, unit_id int, name text, primary key(tenant_id,id));
create table unit_ancestor(tenant_id int, ancestor_id int, unit_id int, primary key(tenant_id,ancestor_id,unit_id));
create table org_grant(tenant_id int, actor int, key text, unit_id int, scope_text text, scope_upper text, primary key(tenant_id,actor,key,unit_id));
""")
units = []
for t in range(1, 51):
    units.append((t, 1, None, 1, "/1/")); nid = 2
    regions = [(t, nid+i, 1, 2, f"/1/{nid+i}/") for i in range(10)]; units += regions; nid += 10
    branches = [];
    for rn, r in enumerate(regions):
        for g in range(10): branches.append((t, nid + rn*10 + g, r[1], 3, r[4] + f"{nid + rn*10 + g}/"))
    units += branches; nid += 100
    if t == 1:
        teams = []
        for rn, r in enumerate(branches):
            for g in range(10): teams.append((t, nid + rn*10 + g, r[1], 4, r[4] + f"{nid + rn*10 + g}/"))
        units += teams; nid += 1000
        subs = []
        for rn, r in enumerate(teams):
            for g in range(2): subs.append((t, nid + rn*2 + g, r[1], 5, r[4] + f"{nid + rn*2 + g}/"))
        units += subs
c.executemany("insert into unit values (?,?,?,?,?)", units)
proj = []; counters = {}
for (t, uid, _, _, _) in sorted(units, key=lambda u: (u[0], u[1])):
    for g in range(16 if t == 1 else 9):
        counters[t] = counters.get(t, 0) + 1
        proj.append((t, counters[t], uid, f"p{uid}-{g}"))
c.executemany("insert into project values (?,?,?,?)", proj)
c.execute("create index ix_project_unit on project(tenant_id, unit_id)")
c.execute("insert into unit_ancestor select d.tenant_id, a.id, d.id from unit d join unit a on a.tenant_id=d.tenant_id and substr(d.path_text,1,length(a.path_text)) = a.path_text")
c.execute("create index ix_unit_path on unit(tenant_id, path_text)")
path = {(u[0], u[1]): u[4] for u in units}
for actor, unit in [(1,12),(1,13),(2,1),(3,112)]:
    p = path[(1, unit)]
    c.execute("insert into org_grant values (1,?,?,?,?,?)", (actor, "project.update", unit, p, p[:-1] + "0"))
c.execute("analyze")
print("units", c.execute("select count(*) from unit").fetchone()[0], "projects", c.execute("select count(*) from project").fetchone()[0], "closure", c.execute("select count(*) from unit_ancestor").fetchone()[0])
G = "g.tenant_id = 1 and g.actor = ? and g.key = 'project.update'"
Q = {
 "text path range": "select count(*) from project p where p.tenant_id = 1 and p.unit_id in (select u.id from unit u join org_grant g on g.tenant_id = u.tenant_id and u.path_text >= g.scope_text and u.path_text < g.scope_upper where u.tenant_id = 1 and " + G + ")",
 "closure table": "select count(*) from project p where p.tenant_id = 1 and p.unit_id in (select ua.unit_id from unit_ancestor ua join org_grant g on g.tenant_id = ua.tenant_id and ua.ancestor_id = g.unit_id where ua.tenant_id = 1 and " + G + ")",
}
for a, who in [(1,"regional manager (2 branches)"),(2,"director (root)"),(3,"team lead (1 team)")]:
    for name, q in Q.items():
        n = c.execute(q, (a,)).fetchone()[0]; ts = []
        for _ in range(9):
            t0 = time.perf_counter(); c.execute(q, (a,)).fetchone(); ts.append((time.perf_counter()-t0)*1000)
        print(f"{who:32} | {name:18} | rows {n:>6} | median {statistics.median(ts):7.2f} ms")
print(c.execute("explain query plan " + Q["text path range"], (1,)).fetchall())
