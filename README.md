# DatabricksSqlDemo

A .NET 8 console app that connects to a Databricks **SQL Warehouse** over **ODBC**
using a **personal access token (PAT)**, runs a test query and prints the result.

> Note: JDBC is a Java technology and does not exist natively in .NET. The native,
> free equivalent is **ODBC** (the official Databricks driver + `System.Data.Odbc`),
> which is what this project uses.

The app reads its configuration from environment variables and works on **macOS,
Windows and Linux (via Docker)**.

## Configuration (all platforms)

| Variable                 | Description                                    | Example                                   |
| ------------------------ | ---------------------------------------------- | ----------------------------------------- |
| `DATABRICKS_HOST`        | Workspace hostname (scheme/slash are stripped) | `adb-xxxx.azuredatabricks.net`            |
| `DATABRICKS_HTTP_PATH`   | SQL Warehouse HTTP path                         | `/sql/1.0/warehouses/abc123def456`        |
| `DATABRICKS_TOKEN`       | Personal access token (PAT)                     | `dapiXXXXXXXX`                            |
| `DATABRICKS_ODBC_DRIVER` | (optional) Driver name/path; sensible per-OS default | see each section below              |

Where to find these in the Databricks UI:

- **Host / HTTP Path:** SQL Warehouses → your warehouse → **Connection details**.
- **Token:** Settings → Developer → **Access tokens** → Generate new token.

---

## Run on macOS

### Prerequisites

```bash
# .NET SDK 8
brew install --cask dotnet-sdk        # or use the dotnet-install.sh script
dotnet --version

# unixODBC (driver manager required by System.Data.Odbc on non-Windows)
brew install unixodbc
```

**Databricks ODBC Driver:** download the macOS `.pkg` from the Databricks docs
("Databricks ODBC Driver") and install it. It lands at
`/Library/databricks/databricksodbc/lib/libdatabricksodbc.dylib` (the app's default).

**macOS-specific tweak (Apple Silicon).** Homebrew installs unixODBC under
`/opt/homebrew/lib`, which neither .NET nor the driver search by default:

1. **`libodbc` loading** is already handled in code
   ([Program.cs](Program.cs), `ResolveLibodbc`) — no action needed.
2. **`libodbcinst` for the driver:** edit
   `/Library/databricks/databricksodbc/lib/databricks.databricksodbc.ini` and add
   under `[Driver]`:
   ```ini
   DriverManagerEncoding=UTF-16
   ODBCInstLib=/opt/homebrew/lib/libodbcinst.dylib
   ```
   (On Homebrew Intel use `/usr/local/lib/libodbcinst.dylib`.)

### Run

```bash
export DATABRICKS_HOST="adb-xxxx.azuredatabricks.net"
export DATABRICKS_HTTP_PATH="/sql/1.0/warehouses/abc123def456"
export DATABRICKS_TOKEN="dapiXXXXXXXX"
cd DatabricksSqlDemo
dotnet run
```

---

## Run on Windows

### Prerequisites

1. **.NET SDK 8** — download from <https://dotnet.microsoft.com/download> and install.
   Verify with `dotnet --version`.
2. **Databricks ODBC Driver** — download the Windows `.msi` from the Databricks docs
   ("Databricks ODBC Driver") and install it. It registers itself with the built-in
   Windows ODBC Driver Manager under the name **`Databricks ODBC Driver`**, which is
   the app's default on Windows. No unixODBC is needed on Windows.

### Run (PowerShell)

```powershell
$env:DATABRICKS_HOST      = "adb-xxxx.azuredatabricks.net"
$env:DATABRICKS_HTTP_PATH = "/sql/1.0/warehouses/abc123def456"
$env:DATABRICKS_TOKEN     = "dapiXXXXXXXX"
# Optional, only if the registered driver name differs:
# $env:DATABRICKS_ODBC_DRIVER = "Simba Spark ODBC Driver"
cd DatabricksSqlDemo
dotnet run
```

### Run (cmd.exe)

```bat
set DATABRICKS_HOST=adb-xxxx.azuredatabricks.net
set DATABRICKS_HTTP_PATH=/sql/1.0/warehouses/abc123def456
set DATABRICKS_TOKEN=dapiXXXXXXXX
cd DatabricksSqlDemo
dotnet run
```

---

## Run with Docker

The included [Dockerfile](Dockerfile) uses the official .NET SDK image and installs
unixODBC plus the Simba/Databricks ODBC driver for Linux — no local .NET or driver
install required, just Docker.

> The Linux ODBC driver is x86_64 only, so the image is pinned to `linux/amd64`.
> On Apple Silicon it builds and runs under emulation (fine for a demo).

### Build

```bash
docker build --platform linux/amd64 -t databricks-sql-demo .
```

### Run

Pass your credentials as environment variables at run time (never bake the token
into the image):

```bash
docker run --rm --platform linux/amd64 \
  -e DATABRICKS_HOST="adb-xxxx.azuredatabricks.net" \
  -e DATABRICKS_HTTP_PATH="/sql/1.0/warehouses/abc123def456" \
  -e DATABRICKS_TOKEN="dapiXXXXXXXX" \
  databricks-sql-demo
```

---

## Expected output

```
Connecting to the Databricks SQL Warehouse...
Connection established.

user_name | server_time
----------------------------------------
you@example.com | 2026-07-01 12:34:56.789

Done: the connection works end to end.
```

If the warehouse is stopped, Databricks starts it on connect (may take a few seconds).

---

## Running more queries

If you want to run more queries to your data, please check the local const variable `sql` inside the [Program.cs](Program.cs) file, inside you can modify it and execute your own logic to test the behavior.

---

## Entity Framework?

Wondering whether to use Entity Framework instead of ODBC? See the feasibility analysis
in [docs/entity-framework-analysis.md](docs/entity-framework-analysis.md). Short version:
EF Core has no free/official Databricks provider (only the commercial CData one), and
there is no generic ODBC provider for EF Core.

Weighing how to consume Delta from .NET (direct SQL Warehouse vs. **Lakebase** vs. a
**separate transactional DB + custom ETL**)? See
[docs/lakebase-vs-sql-warehouse.md](docs/lakebase-vs-sql-warehouse.md). Short version:
only the direct SQL Warehouse reads Delta without copying; the other two give you free
native EF but require materializing the data into an OLTP database first.

---

## Dapper feasibility test

Since EF Core has no free Databricks provider, [**Dapper**](https://github.com/DapperLib/Dapper)
is the closest free ORM-like option: it runs over the **same** ODBC connection this repo
already uses and maps result rows to plain C# classes (POCOs). A working proof of concept
lives in [dapper-demo/](dapper-demo/):

- Reuses the shared connection helper [DatabricksConnection.cs](DatabricksConnection.cs).
- Queries the sample table `samples.bakehouse.sales_customers` (available in every
  Databricks workspace) and maps rows to a `SalesCustomer` POCO with `Query<SalesCustomer>()`.
- Demonstrates two feasibility details:
  - `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true` so snake_case columns
    (`first_name`) map to PascalCase properties (`FirstName`).
  - A parameterized query using ODBC's positional `?` placeholder.

Run it with the **same environment variables** as the main app:

```bash
export DATABRICKS_HOST="adb-xxxx.azuredatabricks.net"
export DATABRICKS_HTTP_PATH="/sql/1.0/warehouses/abc123def456"
export DATABRICKS_TOKEN="dapiXXXXXXXX"
cd dapper-demo
dotnet run
```

Point it at another table by setting `DAPPER_TEST_QUERY` to your own SELECT.

Wondering whether Dapper has the same features as Entity Framework as an ORM? See the
feature comparison in [docs/dapper-vs-ef-features.md](docs/dapper-vs-ef-features.md).

---

## REST API demo (`GET /customers`)

[customers-api/](customers-api/) takes the Dapper POC one step further: a classic
ASP.NET Core MVC Web API (`[ApiController]` + attribute routing) that serves Delta
rows live from the SQL Warehouse — HTTP → Controller → Dapper → ODBC, no data copy.

### Endpoints

| Endpoint | Description |
| --- | --- |
| `GET /customers` | Lists customers; every supplied query parameter becomes a `WHERE col = ?` filter |
| `GET /customers/{id}` | Fetches one customer by `customerID` (404 if not found) |
| `GET /swagger` | Interactive Swagger UI |

Supported filters: `gender`, `country`, `city`, `state`, `continent`, `firstName`,
`lastName`, plus `limit` (default 100, max 1000). Filter names map to a **fixed
whitelist of columns** in the controller — request text never reaches the SQL as
identifiers, only as parameter values (positional `?` binding).

### Run

Same environment variables as the other demos; optionally set `CUSTOMERS_TABLE`
to point at a table other than `samples.bakehouse.sales_customers`:

```bash
export DATABRICKS_HOST="adb-xxxx.azuredatabricks.net"
export DATABRICKS_HTTP_PATH="/sql/1.0/warehouses/abc123def456"
export DATABRICKS_TOKEN="dapiXXXXXXXX"
cd customers-api
dotnet run
```

Then (adjust the port to what `dotnet run` prints):

```bash
curl "http://localhost:5000/customers?gender=female&limit=5"
curl "http://localhost:5000/customers?country=USA&city=Seattle"
curl "http://localhost:5000/customers/1234567"
```

### Response-time measurement

Every successful response carries timing headers (use `curl -i` to see them),
so you can measure how long the SQL Warehouse takes to answer:

| Header | Meaning |
|---|---|
| `X-Connection-Open-Ms` | ODBC handshake against the warehouse (paid on every request in this POC) |
| `X-Query-Ms` | Query execution + result materialization |
| `X-Total-Ms` | Sum of both — the whole warehouse round trip |

> Note: the API opens one ODBC connection per request — fine for a POC, but each
> request pays the connection handshake (a few seconds if the warehouse is cold).
> A production version would keep/pool connections.
