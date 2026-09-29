# Tenancy: the organization tree

The measurements behind the choice of a closure table for the Tenancy supporting domain's organization
tree, described in [Performance](../../docs/performance.md#the-organization-tree). They answer one question
three ways, "which projects are under the units where I hold `project.update`", for a regional manager, a
director at the root and the lead of one team, and time moving a branch under another region.

The data is the same everywhere: one large tenant of 3,111 units in five levels with 49,776 projects, and
49 small tenants of 111 units and 999 projects each.

| File | What it does |
|---|---|
| `postgres.sql` | Builds the data on PostgreSQL with the tree as `ltree`, a text path in the `C` collation and a closure table |
| `run_postgres.py` | Times each form, and a path copied onto every project and checked per row, and prints the plans |
| `postgres-move.sql` | Moves a branch with its 30 units under another region in each form, and rolls back |
| `sqlserver.sql`, `run_sqlserver.py` | The text path, in a binary collation, against the closure table on SQL Server |
| `run_sqlite.py` | The same two on SQLite, in memory, with nothing to install |

## Running it

PostgreSQL and SQL Server run in Docker, under the container names the scripts use:

```bash
docker run -d --name tenancy-bench-pg -e POSTGRES_PASSWORD=bench postgres:17
docker cp postgres.sql tenancy-bench-pg:/tmp/postgres.sql
docker exec tenancy-bench-pg psql -U postgres -q -f /tmp/postgres.sql
python run_postgres.py
docker cp postgres-move.sql tenancy-bench-pg:/tmp/postgres-move.sql
docker exec tenancy-bench-pg psql -U postgres -f /tmp/postgres-move.sql
```

```bash
export BENCH_MSSQL_PASSWORD='<a password of your own>'
docker run -d --name tenancy-bench-mssql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$BENCH_MSSQL_PASSWORD" mcr.microsoft.com/mssql/server:2022-latest
docker cp sqlserver.sql tenancy-bench-mssql:/tmp/sqlserver.sql
docker exec tenancy-bench-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$BENCH_MSSQL_PASSWORD" -i /tmp/sqlserver.sql
python run_sqlserver.py
```

```bash
python run_sqlite.py
```

Every query runs once to warm up and is then timed several times, and the median is reported: seven
times on PostgreSQL, where the time is the execution time `EXPLAIN ANALYZE` reports, and nine on SQL
Server and SQLite. The absolute numbers depend on the machine; compare the forms within one database, not
the databases.
