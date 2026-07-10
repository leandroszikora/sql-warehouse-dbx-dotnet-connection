# Benchmark: SQL Warehouse (ODBC + Dapper) vs Lakebase (Npgsql + EF Core)

Measured comparison of the two REST API demos in this repo serving the same data
(`sales_customers`, 300 rows) from the same Databricks workspace, captured 2026-07-10.

## TL;DR

| | SQL Warehouse (`customers-api`) | Lakebase (`customers-api-lakebase`) |
| --- | --- | --- |
| Median end-to-end latency | **~1.7–1.8 s** | **~140 ms** (≈12× faster) |
| …of which is a POC artifact | ~640 ms ODBC handshake per request (poolable) | — (Npgsql pools by default) |
| Engine-only median (query + fetch) | ~750 ms | ~140 ms (≈5× faster) |
| Latency consistency (p95/median) | ~1.3× | ~1.2–2.1× |
| Data freshness | Live Delta, no copy | Synced-table **copy** (sync lag applies) |
| ORM story | Dapper (no free EF provider) | Full EF Core (Npgsql, free/official) |
| Native dependencies | ODBC driver + unixODBC (per-machine config) | None (pure NuGet) |

**Bottom line:** for an interactive API, Lakebase-style Postgres serving is an order of
magnitude faster and materially simpler to develop against — *if* a synced-table copy
and its sync lag are acceptable. Direct SQL Warehouse access keeps the "no data copy"
guarantee at a ~1–2 s interactive floor on this (smallest, unoptimized) setup, and
roughly 40% of that floor is a fixable POC artifact (per-request ODBC handshake).

## Test environment (read before quoting numbers)

Everything below is a **floor/feasibility measurement, not a capacity test**:

- Databricks **Free Edition**; SQL Warehouse is **Serverless 2X-Small** (the free tier
  size); Lakebase is a **project endpoint at 1 CU** (min=max=1, no autoscaling).
- Delta tables had **no OPTIMIZE** / clustering; Postgres tables have **no indexes**
  (the synced table's PK aside, filters run as sequential scans).
- Tiny dataset: `samples.bakehouse.sales_customers`, **300 rows** (~88 KB JSON) — these
  numbers measure per-request latency floors, not scan or concurrency behavior.
- Single sequential client (curl), 10 iterations × 5 scenarios per backend, both
  warehouses **warm**; APIs on a MacBook (localhost), data plane in `us-east-2` — WAN
  round trips from Argentina are inside every number.
- The SQL Warehouse API opens **one ODBC connection per request** (documented POC
  simplification); the Lakebase API reuses pooled Npgsql connections.

## Results

All values in milliseconds. `engine` = the in-app timing headers (`X-Query-Ms`;
for the warehouse also `X-Connection-Open-Ms`); `end-to-end` = client-observed
(curl `time_total`).

### SQL Warehouse — ODBC + Dapper (port 5000)

| Scenario | Handshake med | Query med | End-to-end med | End-to-end p95 |
| --- | ---: | ---: | ---: | ---: |
| `?limit=10` | 645 | 685 | 1 674 | 2 227 |
| `?limit=100` | 639 | 772 | 1 769 | 2 307 |
| `?limit=1000` (300 rows) | 638 | 734 | 1 700 | 2 255 |
| `?country=USA&limit=100` | 638 | 795 | 1 759 | 2 355 |
| `/customers/{id}` | 704 | 737 | 1 774 | 1 929 |

### Lakebase — Npgsql + EF Core (port 5210)

| Scenario | Query med | End-to-end med | End-to-end p95 |
| --- | ---: | ---: | ---: |
| `?limit=10` | 136 | 140 | 162 |
| `?limit=100` | 138 | 142 | 226 |
| `?limit=1000` (300 rows) | 140 | 143 | 306 |
| `?country=USA&limit=100` | 138 | 142 | 193 |
| `/customers/{id}` | 136 | 138 | 174 |

Raw data: [benchmark-data/results.csv](benchmark-data/results.csv) (100 requests, zero
errors; both backends returned byte-identical row counts per scenario).

### Reading the numbers

- **The warehouse pays a ~1.4 s in-app floor on every request**, split roughly half
  ODBC handshake (~640 ms — the per-request-connection POC artifact) and half query
  execution (~700–800 ms even for a single-row lookup). Payload size barely matters at
  this scale: a 1-row fetch and a 300-row scan cost the same.
- **~300–400 ms of warehouse end-to-end time is outside the measured window** (ODBC
  connection teardown + JSON serialization). With connection pooling/reuse, a realistic
  warm target is the query column: **~750 ms median**.
- **Lakebase sits at a ~140 ms floor** in every scenario — essentially WAN round trip +
  Postgres executing a trivial query. It is also *flatter*: the row-lookup and the
  full-scan cost the same, and growth only shows in p95 at the largest payload.
- **Cold start is a separate axis.** The first warehouse request of the session
  exceeded a 5 s client timeout (serverless resume); once warm it held ~1.7 s medians.
  Lakebase endpoints also auto-suspend (24 h timeout on this project) and pay a resume
  on first touch — not captured in this run. Any latency SLO must decide who keeps the
  compute warm.

## Architecture: components involved

**SQL Warehouse path (no data copy):**

```
.NET API ──ODBC (native Simba driver + unixODBC)──> SQL Warehouse (serverless)
                                                        └── reads Delta tables in place
                                                            (Unity Catalog governance)
```

**Lakebase path (managed copy):**

```
.NET API ──Npgsql (managed, NuGet)──> Lakebase Postgres endpoint
                                          └── synced table  ⟵ continuous sync ⟵ Delta
.NET API ──HTTPS──> /api/2.0/postgres/credentials (mints 1 h OAuth password from PAT)
```

Key structural differences:

| Aspect | SQL Warehouse | Lakebase |
| --- | --- | --- |
| Data copies | **None** — queries hit Delta directly | **One** — synced table is a Postgres copy |
| Freshness | Always current | Bounded by sync (snapshot/triggered/continuous) |
| Governance | Unity Catalog end to end | UC on the source; Postgres roles on the copy |
| Moving parts to operate | Warehouse | Endpoint + synced-table pipeline + credential rotation |
| Auth | PAT straight in the connection string | PAT → REST call → 1 h OAuth password → rotation logic |
| Compute model | Serverless, per-use, scales with BI/analytics load | Provisioned CUs (projects can autoscale), OLTP-shaped |

## Development complexity

| Aspect | SQL Warehouse (ODBC + Dapper) | Lakebase (Npgsql + EF Core) |
| --- | --- | --- |
| Install footprint | Native ODBC driver + unixODBC; per-machine ini fixes (see CLAUDE.md gotchas) | `dotnet add package` — nothing native |
| ORM | Dapper only (no free EF provider for Databricks) | Full EF Core: LINQ, change tracking, migrations* |
| Query authoring | Hand-written SQL, positional `?` params only, quirks (no reliable parameterized LIMIT) | LINQ → parameterized SQL generated for you |
| Auth plumbing | None beyond the PAT | Credential-minting service + caching/rotation (~100 lines, written once) |
| Team fit (EF Core shops) | New patterns to learn | Their default stack |
| Portability of skills | Databricks-specific dialect/driver | Vanilla Postgres |

\* Migrations don't apply to synced tables (schema comes from Delta), but do for any
additional operational tables.

The one-time Lakebase auth work is small and generic; the ODBC platform gotchas are the
kind that resurface on every new laptop/container. Day-2 development is clearly simpler
on the EF Core path.

## Which metrics matter (and why these)

- **Median end-to-end**: what a portal user feels per API call.
- **p95**: tail consistency — an API budget is set by its bad requests, not its good ones.
- **Handshake vs query split** (warehouse): separates the fixable POC artifact from the
  engine floor, so the comparison stays honest.
- **Payload scaling** (10 → 300 rows): shows both engines are latency-bound, not
  volume-bound, at this scale — i.e., don't extrapolate to large scans from this test.
- **Cold start** (qualitative here): dominates worst-case UX on serverless/suspendable
  compute; deserves its own measured test before production decisions.
- Deliberately *not* measured: concurrency/throughput, large scans, sync lag — all need
  a bigger dataset and a load generator; none change the order-of-magnitude gap above.

## Conclusions

1. **Responsiveness:** Lakebase serves this workload at ~140 ms vs ~1.7 s — and even
   against the warehouse's *best theoretical* pooled number (~750 ms), it is ~5× faster
   with flatter tails. For sub-second interactive UX, Postgres-shaped serving wins.
2. **The warehouse is not disqualified:** ~1–2 s responses on the smallest serverless
   size, unoptimized tables, and a handshake-per-request POC is a workable floor for
   internal tools, and it is the only option that preserves "no data copy". Pooling the
   ODBC connection is the first, cheapest improvement.
3. **The trade is latency vs copy:** choosing Lakebase means accepting a synced copy,
   its sync lag, and a second governance surface — the exact concern raised in
   [lakebase-vs-sql-warehouse.md](lakebase-vs-sql-warehouse.md). This benchmark prices
   that trade: roughly one order of magnitude of latency.
4. **Development effort favors Lakebase** for EF Core teams: no native drivers, no SQL
   dialect quirks, standard tooling; the OAuth credential dance is a one-time ~100-line
   cost (already written here).
5. **Before a production decision**, rerun with: production-sized data, indexes on the
   Postgres side / OPTIMIZE + clustering on Delta, concurrent load, measured cold
   starts, and the pooled variants (ODBC connection reuse; the `-pooler` Lakebase host).

## Reproducing

Both APIs running (ports 5000 / 5210), then 10 iterations per scenario capturing the
`X-*-Ms` headers and curl's `time_total`:

```bash
curl -s -D - -o /dev/null -w "e2e=%{time_total}\n" "http://localhost:5000/customers?limit=100" | grep -iE "x-.*-ms|e2e"
curl -s -D - -o /dev/null -w "e2e=%{time_total}\n" "http://localhost:5210/customers?limit=100" | grep -iE "x-.*-ms|e2e"
```

Raw per-request data for this run: [benchmark-data/results.csv](benchmark-data/results.csv).
