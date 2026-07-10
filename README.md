# Databricks serving from .NET: SQL Warehouse vs Lakebase

Two ASP.NET Core backends exposing the **same REST API** (`GET /customers`) over the
same Databricks data, so the two serving options can be compared side by side:

| | [src/WarehouseApi](src/WarehouseApi/) | [src/LakebaseApi](src/LakebaseApi/) |
| --- | --- | --- |
| Reads from | **SQL Warehouse** (Delta, live, no copy) | **Lakebase Postgres** (synced-table copy) |
| Data access | **Dapper** over ODBC | **EF Core** over Npgsql |
| Native deps | Databricks/Simba ODBC driver + unixODBC | none (pure NuGet) |
| Dev port | 5000 | 5210 |
| Auth | PAT in the connection string | OAuth credential minted from the PAT |
| Median latency (measured) | ~0.5 s (pooled) | ~140 ms |

The measured comparison (latency, architecture, development complexity, conclusions)
lives in [docs/benchmark-sql-warehouse-vs-lakebase.md](docs/benchmark-sql-warehouse-vs-lakebase.md).

## Layout

```
DatabricksServing.sln
src/
  Shared/          # class library shared by both APIs (SalesCustomer POCO, CustomerFilters)
  WarehouseApi/    # Dapper + ODBC against the SQL Warehouse (+ its Dockerfile)
  LakebaseApi/     # EF Core + Npgsql against Lakebase Postgres (+ its Dockerfile)
docs/              # analyses and the measured benchmark
```

## The API (identical in both backends)

| Endpoint | Description |
| --- | --- |
| `GET /customers` | Lists customers; every supplied query parameter becomes an equality filter |
| `GET /customers/{id}` | Fetches one customer by `customerID` (404 if not found) |
| `GET /swagger` | Interactive Swagger UI |

Supported filters: `gender`, `country`, `city`, `state`, `continent`, `firstName`,
`lastName`, plus `limit` (default 100, max 1000). Filter names map to a **fixed
whitelist of columns** — request text never reaches the query as identifiers, only as
parameter values (positional `?` binding in Dapper; LINQ translation in EF Core).

Both backends default to the sample table `samples.bakehouse.sales_customers`
(available in every workspace); override with `CUSTOMERS_TABLE` (WarehouseApi) or
`LAKEBASE_TABLE` (LakebaseApi).

### Timing headers

Every successful response reports how long the engine took (use `curl -i`):

| Header | WarehouseApi | LakebaseApi |
|---|---|---|
| `X-Connection-Open-Ms` | ODBC handshake (0 on a pool hit) | — (Npgsql pools natively) |
| `X-Connection-Reused` | pool hit/miss | — |
| `X-Query-Ms` | query + materialization | query + materialization |
| `X-Total-Ms` | whole round trip | whole round trip |

WarehouseApi keeps an in-process pool of open ODBC connections
([OdbcConnectionPool.cs](src/WarehouseApi/OdbcConnectionPool.cs)) because
`System.Data.Odbc` has no built-in ADO.NET pooling. Measured impact: median end-to-end
~1.7 s → ~0.5 s (see the benchmark doc).

## Build everything

```bash
dotnet build DatabricksServing.sln
```

## WarehouseApi (SQL Warehouse, Dapper + ODBC)

### Configuration

| Variable | Description | Example |
| --- | --- | --- |
| `DATABRICKS_HOST` | Workspace hostname (scheme/slash are stripped) | `adb-xxxx.azuredatabricks.net` |
| `DATABRICKS_HTTP_PATH` | SQL Warehouse HTTP path | `/sql/1.0/warehouses/abc123def456` |
| `DATABRICKS_TOKEN` | Personal access token (PAT) | `dapiXXXXXXXX` |
| `DATABRICKS_ODBC_DRIVER` | (optional) driver name/path; sensible per-OS default | see below |
| `CUSTOMERS_TABLE` | (optional) target table | `samples.bakehouse.sales_customers` |

Host / HTTP path: SQL Warehouses → your warehouse → **Connection details**.
Token: Settings → Developer → **Access tokens**.

### Native prerequisites (the ODBC driver is a real install)

- **macOS:** `brew install unixodbc`, install the Databricks ODBC `.pkg` (lands at
  `/Library/databricks/databricksodbc/lib/libdatabricksodbc.dylib`). On Apple Silicon,
  edit `/Library/databricks/databricksodbc/lib/databricks.databricksodbc.ini` and add
  under `[Driver]`:
  ```ini
  DriverManagerEncoding=UTF-16
  ODBCInstLib=/opt/homebrew/lib/libodbcinst.dylib
  ```
  (`libodbc` loading is already handled in code — `DatabricksConnection.ResolveLibodbc`.)
- **Windows:** install the `.msi`; it registers the driver name
  `Databricks ODBC Driver` (the app's default). No unixODBC needed.
- **Linux:** use the Docker image below, or replicate its Simba driver install.

### Run

```bash
export DATABRICKS_HOST="adb-xxxx.azuredatabricks.net"
export DATABRICKS_HTTP_PATH="/sql/1.0/warehouses/abc123def456"
export DATABRICKS_TOKEN="dapiXXXXXXXX"
cd src/WarehouseApi
dotnet run          # listens on http://localhost:5000
```

## LakebaseApi (Lakebase Postgres, EF Core + Npgsql)

### Authentication: PAT ≠ Postgres password

Lakebase does **not** accept the workspace PAT as the Postgres password. It requires a
short-lived OAuth database credential (~1 h). The PAT *is* valid to mint one via the
REST API, so the app does exactly that:
[LakebaseCredentialProvider](src/LakebaseApi/LakebaseCredentialProvider.cs) calls the
API with your PAT, caches the credential, and refreshes it a few minutes before
expiry — Npgsql asks for it whenever it opens a physical connection.

Lakebase comes in **two flavors**, with different credential APIs — set the matching
env var and the provider picks the right one:

| Flavor | How to spot it | Env var | Credentials API |
| --- | --- | --- | --- |
| **Project** (Neon-based; the only flavor on Free Edition) | Postgres host looks like `ep-xxxx...database...` | `LAKEBASE_ENDPOINT=projects/{p}/branches/{b}/endpoints/{e}` | `POST /api/2.0/postgres/credentials` |
| **Provisioned database instance** | Listed under Compute → Database instances | `LAKEBASE_INSTANCE=<instance name>` | `POST /api/2.0/database/credentials` |

Find the project/branch/endpoint IDs in the Lakebase project UI, via CLI
(`databricks postgres list-projects`, then `list-branches projects/<id>`, then
`list-endpoints projects/<id>/branches/<id>`), or with plain curl against
`GET /api/2.0/postgres/projects` (then `.../{project}/branches`,
`.../{branch}/endpoints`) — each response's `name` field is the full resource name.

> **Gotcha:** the last segment of the endpoint resource name is the `endpoint_id`
> (often literally `primary`), **not** the `ep-xxxx` string from the Postgres hostname —
> that one is the endpoint's `uid`, and using it in `LAKEBASE_ENDPOINT` gets a 404.

### Run

```bash
export DATABRICKS_HOST="adb-xxxx.azuredatabricks.net"     # workspace (for the credentials API)
export DATABRICKS_TOKEN="dapiXXXXXXXX"                    # PAT (for the credentials API)
export LAKEBASE_ENDPOINT="projects/my-project/branches/production/endpoints/primary"  # project flavor…
# …or, for a provisioned instance: export LAKEBASE_INSTANCE="my-lakebase-instance"
export LAKEBASE_HOST="ep-xxxx.database.us-east-2.cloud.databricks.com"  # Postgres hostname
export LAKEBASE_USER="you@example.com"                    # your Databricks identity (plain, not URL-encoded)
# optional: LAKEBASE_DATABASE (default databricks_postgres)
# optional: LAKEBASE_TABLE   (default public.sales_customers, format schema.table)
cd src/LakebaseApi
dotnet run          # listens on http://localhost:5210
```

## Side-by-side comparison

```bash
# terminal 1
cd src/WarehouseApi && dotnet run     # port 5000
# terminal 2
cd src/LakebaseApi && dotnet run      # port 5210
# compare:
curl -si "http://localhost:5000/customers?limit=10" | grep -i x-.*-ms
curl -si "http://localhost:5210/customers?limit=10" | grep -i x-.*-ms
```

## Docker

Both images build from the **repo root** (they need `src/Shared`). Never bake the
token into an image — pass credentials at run time.

```bash
# LakebaseApi — multi-arch, no native deps, runs natively on Apple Silicon:
docker build -f src/LakebaseApi/Dockerfile -t lakebase-api .
docker run --rm -p 5210:8080 \
  -e DATABRICKS_HOST=... -e DATABRICKS_TOKEN=... \
  -e LAKEBASE_ENDPOINT=... -e LAKEBASE_HOST=... -e LAKEBASE_USER=... \
  lakebase-api

# WarehouseApi — pinned to linux/amd64 (the Simba ODBC driver is x86_64 only;
# emulated on Apple Silicon):
docker build --platform linux/amd64 -f src/WarehouseApi/Dockerfile -t warehouse-api .
docker run --rm --platform linux/amd64 -p 5000:8080 \
  -e DATABRICKS_HOST=... -e DATABRICKS_HTTP_PATH=... -e DATABRICKS_TOKEN=... \
  warehouse-api
```

Inside the containers the apps listen on the aspnet image default (`8080`);
`launchSettings.json` ports only apply to `dotnet run`.

## Analyses (docs/)

- [benchmark-sql-warehouse-vs-lakebase.md](docs/benchmark-sql-warehouse-vs-lakebase.md)
  — **the measured comparison**: latency (median/p95 per scenario, pooling follow-up),
  architecture, development complexity, conclusions. Raw data in
  [docs/benchmark-data/](docs/benchmark-data/).
- [entity-framework-analysis.md](docs/entity-framework-analysis.md) — why EF Core has
  no free path to Databricks SQL Warehouses (hence Dapper in WarehouseApi).
- [lakebase-vs-sql-warehouse.md](docs/lakebase-vs-sql-warehouse.md) — the three options
  to consume Delta from .NET and their trade-offs; only the direct SQL Warehouse avoids
  copying data.
- [dapper-vs-ef-features.md](docs/dapper-vs-ef-features.md) — Dapper (micro-ORM) vs
  EF Core (full ORM) feature comparison — exactly the two data-access styles the two
  backends use.
