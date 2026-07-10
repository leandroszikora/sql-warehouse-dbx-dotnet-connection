# CLAUDE.md — DatabricksServing

Guidance for Claude Code (and future contributors) working in this repo. It captures
everything built and learned during the initial development sessions (June–July 2026).

## What this project is

Proof-of-concept for a client: serve Databricks data to **.NET 8** REST backends and
compare the two serving options. Two ASP.NET Core APIs expose the **same
`GET /customers` contract** over the same data:

- **WarehouseApi** — SQL Warehouse over ODBC + Dapper (live Delta, no data copy; PAT auth).
- **LakebaseApi** — Lakebase Postgres over Npgsql + EF Core (synced-table copy; OAuth
  credential minted from the PAT).

The client team uses Entity Framework — the repo's docs/ analyses explain why EF can't
run free against the warehouse and price the alternatives; the measured benchmark is in
`docs/benchmark-sql-warehouse-vs-lakebase.md`.

- Repo: `git@github.com:leandroszikora/sql-warehouse-dbx-dotnet-connection.git` (branch `main`)
- History note: the repo grew POC-by-POC (root ODBC console demo → dapper-demo console
  POC → the two APIs). The console demos were removed in the 2026-07-10 restructure;
  their learnings live on in this file and docs/.

## Layout

```
DatabricksServing.sln         # build everything: dotnet build DatabricksServing.sln
src/
  Shared/                     # classlib referenced by both APIs (DatabricksServing.Shared)
    Models/SalesCustomer.cs   # single POCO for sales_customers (both backends map it)
    CustomerFilters.cs        # query-string filter DTO — keeps the two API contracts identical
  WarehouseApi/               # DatabricksServing.WarehouseApi — Dapper + ODBC, dev port 5000
    Program.cs                # AddControllers + Swagger; MatchNamesWithUnderscores; pool singleton
    DatabricksConnection.cs   # ODBC connection-string builder + macOS libodbc resolver
    OdbcConnectionPool.cs     # in-process pool of open ODBC connections (no DM pooling in .NET)
    Controllers/CustomersController.cs  # whitelisted filters → positional ? SQL; timing headers
    Properties/launchSettings.json      # fixed port 5000
    Dockerfile                # linux/amd64 (Simba driver is x86_64-only), aspnet runtime + unixODBC
  LakebaseApi/                # DatabricksServing.LakebaseApi — EF Core + Npgsql, dev port 5210
    Program.cs                # NpgsqlDataSource + UsePasswordProvider + AddDbContext; fails fast on missing env vars
    LakebaseCredentialProvider.cs   # mints/caches the OAuth Postgres credential (both Lakebase flavors)
    Data/CustomersDbContext.cs # fluent mapping to the synced table (schema.table from LAKEBASE_TABLE)
    Controllers/CustomersController.cs  # same endpoints/filters, LINQ instead of SQL
    Properties/launchSettings.json      # fixed port 5210
    Dockerfile                # multi-arch (no native deps), aspnet runtime
docs/
  entity-framework-analysis.md    # why EF Core can't run free on Databricks
  lakebase-vs-sql-warehouse.md    # 3 options to consume Delta from .NET
  dapper-vs-ef-features.md        # Dapper (micro-ORM) vs EF Core (full ORM)
  benchmark-sql-warehouse-vs-lakebase.md  # measured latency comparison + conclusions
  benchmark-data/                 # raw per-request CSVs of the benchmark runs
.dockerignore                 # repo root — both Docker builds use the ROOT as context
```

Key wiring details:
- Both APIs reference `src/Shared` via `<ProjectReference>`; shared code is only the
  model + filters. Connection/pool helpers are warehouse-specific, the credential
  provider is lakebase-specific — keep them in their API projects.
- Both Dockerfiles are built from the **repo root** (`docker build -f
  src/<Api>/Dockerfile .`) because the build needs `src/Shared`.
- Assembly names = `DatabricksServing.<Project>` — Dockerfile ENTRYPOINTs reference
  those dll names; keep them in sync if renaming.

## Architecture decisions (and why)

1. **ODBC, not "JDBC"**: JDBC is Java-only. The Microsoft Q&A example the client found
   uses CData (commercial) to fake a JDBC-style string in .NET. Free/native path is the
   official Databricks ODBC driver + `System.Data.Odbc`.
2. **Connection string pattern**: `Driver={...};Host=<bare-host>;Port=443;HTTPPath=<path>;
   SSL=1;ThriftTransport=2;AuthMech=3;UID=token;PWD=<PAT>;`. `AuthMech=3` + `UID=token`
   is the standard PAT pattern. `Host` must be a **bare hostname** — the helper strips
   `https://` and trailing `/` because users paste full URLs.
3. **Config via env vars only**: `DATABRICKS_*`, `LAKEBASE_*`, optional
   `CUSTOMERS_TABLE`/`LAKEBASE_TABLE`. The token is never written to files.
4. **Entity Framework**: EF Core has **no free/official Databricks provider** and no
   generic ODBC provider. Only path is commercial CData. See
   `docs/entity-framework-analysis.md`. **Dapper** is the free ORM-like alternative —
   WarehouseApi uses it in production style; LakebaseApi gets full EF Core because
   Lakebase is plain Postgres (Npgsql provider).
5. **Lakebase initially rejected** for the strict "no load into a transactional DB"
   requirement (synced tables ARE a copy) — see `docs/lakebase-vs-sql-warehouse.md`.
   LakebaseApi exists to *measure* that alternative anyway; the benchmark doc prices
   the trade-off (~140 ms vs ~0.5 s medians with pooling).

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
  `/usr/lib/x86_64-linux-gnu/libodbcinst.so.2` (the WarehouseApi Dockerfile does this
  in the RUNTIME stage — the driver is a run-time native dependency).
- The driver is **x86_64 only** → WarehouseApi image pinned `--platform=linux/amd64`
  (emulated on Apple Silicon).
- Docker Desktop IS installed on the dev Mac (since 2026-07-10; start it with
  `open -a Docker` if the daemon is down).
- LakebaseApi image is multi-arch on purpose: no native deps → no platform pin,
  `aspnet:8.0` runtime. **Tested end-to-end against live Lakebase**: containers listen
  on 8080 (aspnet default; launchSettings ports only affect `dotnet run`), first
  request ~3 s (pool warm-up), then ~140 ms.

### Lakebase (LakebaseApi)
- **The workspace PAT is NOT a valid Postgres password.** Lakebase requires an OAuth
  database credential (expires ~1 h, enforced at login only). The PAT is only valid to
  MINT that credential, and **Lakebase has two flavors with different credential APIs**:
  - *Projects* (Neon-based — the ONLY flavor on Free Edition; hosts look like
    `ep-xxxx.database...`): `POST /api/2.0/postgres/credentials`, body
    `{"endpoint": "projects/{p}/branches/{b}/endpoints/{e}"}` → `{"token", "expire_time"}`.
    Env var: `LAKEBASE_ENDPOINT`. IDs discoverable via `databricks postgres list-projects`
    / `list-branches` / `list-endpoints`. (REST path confirmed from databricks-sdk-go
    `service/postgres/impl.go` — the API reference SPA is unreadable by fetch tools.)
  - *Provisioned database instances*: `POST /api/2.0/database/credentials`, body
    `{"request_id": "<uuid>", "instance_names": ["<name>"]}` → `{"token",
    "expiration_time"}` (note the different field name). Env var: `LAKEBASE_INSTANCE`.
  `LakebaseCredentialProvider` picks the API from whichever env var is set (endpoint
  wins), caches the token and refreshes 5 min before expiry.
- **Endpoint ID ≠ the `ep-xxxx` string in the hostname.** In the resource name the last
  segment is the endpoint's `endpoint_id` (often literally `primary`); the `ep-xxxx`
  value is the endpoint's `uid`, which only appears in the Postgres hostname. Using the
  uid in `LAKEBASE_ENDPOINT` returns 404 NOT_FOUND. Verified working format:
  `projects/<project>/branches/<branch>/endpoints/primary` (project/branch here are the
  user-chosen IDs, e.g. `test`/`production`).
- Discovery without the CLI (paths confirmed from databricks-sdk-go): GET
  `/api/2.0/postgres/projects`, then `/api/2.0/postgres/{project_name}/branches`, then
  `/api/2.0/postgres/{branch_name}/endpoints` — each response's `name` field is the full
  resource name to feed to the next call / to `LAKEBASE_ENDPOINT`.
- The workspace PAT works as Bearer auth for `/api/2.0/postgres/*` on **Free Edition**
  (verified live) — a 404 from the credentials API means a wrong resource name, not an
  auth problem (bad auth would be 401/403).
- Endpoints also expose a **pooled host** (`ep-xxxx-pooler.database...`, server-side
  PgBouncer-style). Untested here; candidate for comparing under many short-lived
  connections.
- Npgsql `UsePasswordProvider` requires BOTH sync and async callbacks (throws
  `ArgumentException` if either is null) — the sync one is a blocking wrapper.
- Npgsql surfaces DNS/socket failures as a raw `SocketException` (not wrapped in
  `NpgsqlException`) — the controller catches it explicitly.
- Env vars: reuses `DATABRICKS_HOST`/`DATABRICKS_TOKEN` (credentials API) + new
  `LAKEBASE_ENDPOINT` or `LAKEBASE_INSTANCE`, `LAKEBASE_HOST`, `LAKEBASE_USER` (plain
  email, not URL-encoded), optional `LAKEBASE_DATABASE`
  (default `databricks_postgres`) and `LAKEBASE_TABLE` (default `public.sales_customers`).
  Missing vars fail fast at startup (unlike WarehouseApi, which 500s per request).
- Timing headers: only `X-Query-Ms`/`X-Total-Ms` (Npgsql pools natively, so there is no
  per-request handshake to report).
- Smoke test without credentials: set all five vars to fake values (`*.invalid` hosts);
  `GET /customers` must return a 500 problem+json whose detail says
  `Could not reach the Lakebase Postgres host`, and `?limit=0` must return 400.

### Dapper / ODBC specifics (WarehouseApi)
- Databricks columns are snake_case; POCOs are PascalCase → must set
  `DefaultTypeMap.MatchNamesWithUnderscores = true;` before querying (done in Program.cs).
- ODBC parameters are **positional `?`**, not named — write SQL accordingly
  (`WHERE country = ?`), Dapper binds the anonymous-object values in order.
- `limit` is a range-checked int inlined into the SQL: parameterized LIMIT is
  unreliable on the Simba/Spark driver.
- Connections come from `OdbcConnectionPool.cs` (singleton, in-process ConcurrentBag,
  max 8 idle): `System.Data.Odbc` has NO built-in ADO.NET pooling — that's a
  driver-manager feature configured per machine — so the app pools itself for
  portability. A reused connection can be dead server-side (warehouse idle timeout):
  `Execute` retries once on a fresh connection when `OdbcException` hits a reused one.
  Measured impact: ~1.7 s → ~0.5 s median e2e; reused sessions also cut query time
  ~35% (skip per-connection session setup) — see the benchmark doc follow-up.
- Timing headers set in `Execute`: `X-Connection-Open-Ms` (0 on pool hit),
  `X-Connection-Reused`, `X-Query-Ms`, `X-Total-Ms`. Error responses carry none.
- Test table available in every workspace: `samples.bakehouse.sales_customers`
  (`customerID bigint, first_name, last_name, email_address, phone_number, address,
  city, state, country, continent, postal_zip_code bigint, gender`; 300 rows).

## How to build / run / verify

```bash
export PATH="$HOME/.dotnet:$PATH"           # dotnet 8.0.422 in ~/.dotnet
dotnet build DatabricksServing.sln           # all three projects

# WarehouseApi (needs DATABRICKS_HOST / DATABRICKS_HTTP_PATH / DATABRICKS_TOKEN):
cd src/WarehouseApi && dotnet run            # http://localhost:5000
# LakebaseApi (needs DATABRICKS_HOST/TOKEN + LAKEBASE_ENDPOINT|INSTANCE + LAKEBASE_HOST/USER):
cd src/LakebaseApi && dotnet run             # http://localhost:5210
```

**Smoke test without credentials** (validates the whole native stack): run with
`DATABRICKS_HOST=nonexistent-host.invalid` (and, for LakebaseApi, fake `LAKEBASE_*`
values). WarehouseApi: `GET /customers` → 500 whose detail contains `[HY000] ...
Could not resolve host` — meaning libodbc, the driver and the Thrift/HTTP layer all
loaded and only DNS failed; any dylib/so loading error means the platform gotchas above
regressed. LakebaseApi: 500 with `Could not reach the Lakebase Postgres host`. Both:
`?limit=0` → 400.

## Conventions

- Everything user-facing (code comments, README, docs, commit messages) is **English**;
  conversation with the repo owner is Spanish.
- Docs style: TL;DR first, comparison table, recommendation, sources with links.
  New analysis docs go in `docs/` and get linked from README.
- Push over **SSH**.
- The repo is **project-agnostic**: no client names or personal environment IDs in
  committed files (generic examples only).

## Current status / possible next steps

- Both APIs **verified end-to-end against live services** (warehouse + Lakebase Free
  Edition project), running simultaneously (5000/5210). Both images build from repo
  root; LakebaseApi container tested e2e with real data; WarehouseApi container
  smoke-tested (fake host → `Could not resolve host`, proving the whole native
  ODBC stack loads inside the emulated image) — real-credential container run pending.
- Benchmark 2026-07-10 (`docs/benchmark-sql-warehouse-vs-lakebase.md`): Lakebase
  ~140 ms median vs warehouse ~1.7 s e2e; with the ODBC pool (same day) warehouse drops
  to ~0.5 s (gap ~3.5×). Real serverless cold start captured: 21.3 s. Free-tier sizes,
  300-row table, warm, sequential — floor measurement only.
- 2026-07-10 restructure: dapper-demo and the root console demo removed; sln + src/
  layout with Shared classlib; both Dockerfiles build from repo root.
- Remaining follow-ups: `-pooler` Lakebase host, concurrency, real-sized data under
  load; one-page executive summary; Makefile/scripts.
