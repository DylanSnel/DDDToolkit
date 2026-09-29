# Tenancy: the organization tree

The measurements behind the choice of a closure table for the Tenancy supporting domain's organization
tree, described in [Performance](../../docs/performance.md#the-organization-tree). They answer one question
three ways, "which projects are under the units where I hold `project.update`", for a regional manager
with the key at two branches, a director at the root and the lead of one team unit, and time moving a
branch under another region.

The data is the same everywhere: one large tenant of 3,111 units in five levels with 49,776 projects, and
49 small tenants of 111 units and 999 projects each.

| File | What it does |
|---|---|
| `postgres.sql` | Builds the data on PostgreSQL with the tree as `ltree`, a text path in the `C` collation and a closure table |
| `run_postgres.py` | Times each form, and a path copied onto every project and checked per row, prints the plans, and times moving a branch in each form |
| `postgres-move.sql` | The same moves, once each, to read by hand |
| `sqlserver.sql`, `run_sqlserver.py` | The text path, in a binary collation, against the closure table on SQL Server |
| `run_sqlite.py` | The same two on SQLite, in memory, with nothing to install |

## Running it

PostgreSQL and SQL Server run in Docker, under the container names the scripts use. Run these from
`Benchmarks/Tenancy`:

```bash
docker run -d --name tenancy-bench-pg -e POSTGRES_PASSWORD=bench postgres:17
until docker exec tenancy-bench-pg psql -U postgres -h 127.0.0.1 -Atc 'select 1' >/dev/null 2>&1; do sleep 1; done
docker cp postgres.sql tenancy-bench-pg:/tmp/postgres.sql
docker exec tenancy-bench-pg psql -U postgres -q -f /tmp/postgres.sql
python run_postgres.py
```

```bash
export BENCH_MSSQL_PASSWORD='<a password of your own>'
docker run -d --name tenancy-bench-mssql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$BENCH_MSSQL_PASSWORD" mcr.microsoft.com/mssql/server:2022-latest
until docker exec tenancy-bench-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$BENCH_MSSQL_PASSWORD" -Q 'select 1' >/dev/null 2>&1; do sleep 1; done
docker cp sqlserver.sql tenancy-bench-mssql:/tmp/sqlserver.sql
docker exec tenancy-bench-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$BENCH_MSSQL_PASSWORD" -i /tmp/sqlserver.sql
python run_sqlserver.py
```

```bash
python run_sqlite.py
```

## How the numbers are taken

Every query runs once to warm up and is then timed several times, and the median is reported:

- on PostgreSQL, seven times, as the execution time `EXPLAIN ANALYZE` reports, after a `VACUUM ANALYZE`
  and with parallel workers off for the database;
- on SQL Server, nine samples, each a batch of 200 executions timed together and divided, because
  `sysdatetime()` moves in steps of about 4 ms;
- on SQLite, nine times, in the process.

A move runs inside a transaction that is rolled back, so every run starts from the same tree, and its time
is the server's execution time, the closure table's two statements added together.

The absolute numbers depend on the machine; compare the forms within one database, not the databases.
