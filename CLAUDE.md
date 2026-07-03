# CLAUDE.md — DatabricksSqlDemo

Guidance for Claude Code (and future contributors) working in this repo. It captures
everything built and learned during the initial development sessions (June–July 2026).

## What this project is

Proof-of-concept for a client: connect a **.NET 8** app directly to a **Databricks SQL
Warehouse** using a **personal access token (PAT)**, reading Delta tables **live, with no
data copy** into a transactional database. The client team uses Entity Framework, so the
repo also contains feasibility analyses of EF/Lakebase/ETL alternatives and a working
**Dapper** POC.

- Repo: `git@github.com:leandroszikora/sql-warehouse-dbx-dotnet-connection.git` (branch `main`)
- Local path: `/Users/leandroszikora/Documents/DBlandIT/DatabricksSqlDemo`

## Layout

```
DatabricksSqlDemo.csproj      # root console demo (raw ODBC), net8.0
Program.cs                    # opens connection, runs test query via OdbcDataReader
DatabricksConnection.cs       # SHARED connection helper (used by both projects)
dapper-demo/                  # separate console POC using Dapper over the same connection
  DatabricksSqlDapperDemo.csproj  # links ../DatabricksConnection.cs; Dapper 2.1.66
  Program.cs                  # Query<SalesCustomer> against samples.bakehouse.sales_customers
docs/
  entity-framework-analysis.md    # why EF Core can't run free on Databricks
  lakebase-vs-sql-warehouse.md    # 3 options to consume Delta from .NET
  dapper-vs-ef-features.md        # Dapper (micro-ORM) vs EF Core (full ORM)
Dockerfile                    # .NET SDK 8 image + unixODBC + Simba driver (linux/amd64 only)
```

Key wiring details:
- The root `.csproj` has `<Compile Remove="dapper-demo/**/*.cs" />` so the default glob
  doesn't pick up the sub-project. Keep that if adding more sub-demos.
- `dapper-demo` reuses the helper via a **linked file**
  (`<Compile Include="..\DatabricksConnection.cs" Link=... />`) — no class library, on purpose
  (keeps each demo runnable standalone with `dotnet run`).

## Architecture decisions (and why)

1. **ODBC, not "JDBC"**: JDBC is Java-only. The Microsoft Q&A example the client found
   uses CData (commercial) to fake a JDBC-style string in .NET. Free/native path is the
   official Databricks ODBC driver + `System.Data.Odbc`.
2. **Connection string pattern**: `Driver={...};Host=<bare-host>;Port=443;HTTPPath=<path>;
   SSL=1;ThriftTransport=2;AuthMech=3;UID=token;PWD=<PAT>;`. `AuthMech=3` + `UID=token`
   is the standard PAT pattern. `Host` must be a **bare hostname** — the helper strips
   `https://` and trailing `/` because users paste full URLs.
3. **Config via env vars only**: `DATABRICKS_HOST`, `DATABRICKS_HTTP_PATH`,
   `DATABRICKS_TOKEN`, optional `DATABRICKS_ODBC_DRIVER`, optional `DAPPER_TEST_QUERY`.
   The token is never written to files.
4. **Entity Framework**: EF Core has **no free/official Databricks provider** and no
   generic ODBC provider. Only path is commercial CData. See
   `docs/entity-framework-analysis.md`. **Dapper** is the free ORM-like alternative and
   is proven working in `dapper-demo/`.
5. **Lakebase rejected** for this use case: it only serves Delta via **synced tables**
   (a copy into Postgres), violating the client's "no load into a transactional DB"
   requirement. Same for a self-managed OLTP DB + ETL. See
   `docs/lakebase-vs-sql-warehouse.md`.

## Hard-won platform gotchas (do not rediscover these)

### macOS (Apple Silicon)
- `dotnet` lives in `~/.dotnet` (installed via `dotnet-install.sh`; the brew cask fails
  without interactive sudo). If `dotnet` is "not found": `export PATH="$HOME/.dotnet:$PATH"`.
- .NET can't find Homebrew's unixODBC (`/opt/homebrew/lib`). Fixed **in code**:
  `DatabricksConnection.ResolveLibodbc` registers a `DllImportResolver` that loads
  `/opt/homebrew/lib/libodbc.2.dylib` (override with `UNIXODBC_LIB`).
- The Databricks driver itself can't find `libodbcinst` (error 50483 /
  `SQLGetPrivateProfileString`). Fixed by editing
  `/Library/databricks/databricksodbc/lib/databricks.databricksodbc.ini`, adding under
  `[Driver]`: `DriverManagerEncoding=UTF-16` and
  `ODBCInstLib=/opt/homebrew/lib/libodbcinst.dylib`. **This is machine-level config** — a
  new laptop needs it again.
- Driver install path: `/Library/databricks/databricksodbc/lib/libdatabricksodbc.dylib`.

### Windows
- No unixODBC needed. The `.msi` registers the driver **name** `Databricks ODBC Driver`;
  the helper uses the name (not a path) and skips the file-exists check.

### Linux / Docker
- Simba driver download (public S3, verified working):
  `https://databricks-bi-artifacts.s3.us-east-2.amazonaws.com/simbaspark-drivers/odbc/2.9.1/SimbaSparkODBC-2.9.1.1001-Debian-64bit.zip`
- Installs to `/opt/simba/spark/lib/64/libsparkodbc_sb64.so`; its ini
  (`simba.sparkodbc.ini`) needs the same `ODBCInstLib` fix, pointing at
  `/usr/lib/x86_64-linux-gnu/libodbcinst.so.2` (the Dockerfile does this).
- The driver is **x86_64 only** → image pinned `--platform=linux/amd64` (emulated on
  Apple Silicon). Docker is NOT installed on the dev Mac; the Dockerfile is untested
  end-to-end.

### Dapper specifics
- Databricks columns are snake_case; POCOs are PascalCase → must set
  `DefaultTypeMap.MatchNamesWithUnderscores = true;` before querying.
- ODBC parameters are **positional `?`**, not named — write SQL accordingly
  (`WHERE country = ?`), Dapper binds the anonymous-object values in order.
- Test table available in every workspace: `samples.bakehouse.sales_customers`
  (`customerID bigint, first_name, last_name, email_address, phone_number, address,
  city, state, country, continent, postal_zip_code bigint, gender`).

## How to build / run / verify

```bash
export PATH="$HOME/.dotnet:$PATH"           # dotnet 8.0.422 in ~/.dotnet
dotnet build                                 # root demo
(cd dapper-demo && dotnet build)             # Dapper POC

# Real run (needs credentials):
export DATABRICKS_HOST="dbc-xxxx.cloud.databricks.com"   # scheme/slash tolerated
export DATABRICKS_HTTP_PATH="/sql/1.0/warehouses/<id>"
export DATABRICKS_TOKEN="dapi..."
dotnet run                                   # or: cd dapper-demo && dotnet run
```

**Smoke test without credentials** (validates the whole native stack): run with
`DATABRICKS_HOST=nonexistent-host.invalid` and dummy path/token. Success looks like an
ODBC error `[HY000] ... Could not resolve host` — meaning libodbc, the driver and the
Thrift/HTTP layer all loaded and only DNS failed. Any dylib/so loading error means the
platform gotchas above regressed.

## Conventions

- Everything user-facing (code comments, README, docs, commit messages) is **English**;
  conversation with the repo owner is Spanish.
- Docs style: TL;DR first, comparison table, recommendation, sources with links.
  New analysis docs go in `docs/` and get linked from README.
- Push over **SSH** (the SSH key authenticates as `leandroszikora`, the repo owner);
  `gh` CLI is logged in as a different account (`lszikoradblandit`) — don't push via gh/HTTPS.

## Current status / possible next steps

- Root ODBC demo: **verified end-to-end** against the client's real warehouse.
- Dapper POC: builds + smoke-tested; real-data mapping run pending on the owner
  (positional-parameter binding is the one point to confirm).
- Pending ideas discussed but not requested yet: one-page executive summary of the
  EF/Lakebase/Dapper analyses; flow diagram; Makefile/scripts; testing the Docker build.
