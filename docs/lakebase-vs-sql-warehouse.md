# Lakebase vs. SQL Warehouse (direct) — for consuming Delta tables from .NET

This document compares two ways to consume Databricks **Delta tables** from a .NET
application, in the context of a client that uses **Entity Framework** and wants to
**keep reading Delta tables without loading them into a transactional database**.

It is a companion to [entity-framework-analysis.md](entity-framework-analysis.md).

## TL;DR

- **SQL Warehouse (direct, via ODBC/ADO.NET or Dapper)** reads Delta tables **live, with
  no data copy**. This is the approach already implemented in this repo and it matches the
  "no load" requirement.
- **Lakebase** (Databricks' managed serverless Postgres) can serve Delta data to a
  Postgres/EF app, but **only by copying the data** into Postgres via **synced tables**.
  There is **no zero-copy / live federation** from Lakebase Postgres to Delta.
- Therefore, for the stated goal ("consume Delta without loading into a transactional
  DB"), **Lakebase does not fit** — it introduces exactly the copy we want to avoid. Its
  upside (native, free EF via Npgsql) is tied to that copy.

## What each option actually is

### SQL Warehouse (direct)

A Databricks SQL Warehouse executes SQL **against the Delta tables in place**. The .NET
app connects over ODBC (this repo) or could use Dapper on top of the same connection.
Nothing is materialized outside the lakehouse; you pay warehouse compute only while
queries run.

### Lakebase

A fully managed serverless **PostgreSQL** (Neon-based), GA on AWS, with full ACID/OLTP.
Because it is Postgres, **Entity Framework Core works natively and for free** (Npgsql
provider). But Lakebase does **not** query Delta in place. To expose Delta data it uses
**synced tables**: managed Lakeflow pipelines that **replicate** the Delta/Unity Catalog
table into Postgres storage (Snapshot / Triggered / Continuous modes, ≥15s refresh). The
data then physically lives in **both** Delta and Postgres.

## Side-by-side

| Dimension | SQL Warehouse (direct) | Lakebase (synced tables) |
| --- | --- | --- |
| Reads Delta **without copying** | ✅ Yes, live in place | ❌ No — data is replicated into Postgres |
| Data duplication / storage | None extra | Duplicated (Delta + Postgres) |
| Data freshness | Always current (queries the source) | Lag depends on sync mode (≥15s continuous) |
| Entity Framework (free/native) | ❌ No free EF provider (CData is paid) | ✅ Yes — Postgres → EF Core + Npgsql |
| .NET access today | ✅ ODBC/ADO.NET (this repo) or Dapper | Postgres client / EF once synced |
| Cost components | Warehouse compute on query | Lakebase compute + duplicated storage + sync pipeline compute |
| Best fit | Analytics / reporting / read queries over Delta | Low-latency OLTP app serving, high-QPS point lookups, agents |
| Matches the client's "no load" requirement | ✅ Yes | ❌ No |

## Cost note

- **SQL Warehouse**: you already pay for it; it bills compute while running queries and
  can auto-stop when idle. No extra storage.
- **Lakebase**: usage-based DBUs. Autoscaling compute can scale to zero, but you add
  **(1)** Lakebase compute, **(2)** duplicated storage, and **(3)** the sync pipeline
  compute — on top of the SQL Warehouse you likely still keep. This is additive cost, not
  a replacement, unless the workload genuinely moves to OLTP serving.

## Recommendation

- **Keep the SQL Warehouse (direct) approach** for the stated goal of reading Delta
  without loading a transactional DB. Use the existing ODBC/ADO.NET code, or add Dapper if
  the team wants ORM-like ergonomics for free.
- **Consider Lakebase only if the use case changes** to operational/app serving that
  needs sub-second lookups, high concurrency, or transactional writes — that is where
  Lakebase (and free native EF) pays off, accepting the sync/copy as a deliberate design
  choice.
- **Entity Framework caveat stays the same**: "free EF" only exists once data is in
  Postgres (i.e. after a copy). "EF directly on Delta, no copy" requires the commercial
  CData provider. See [entity-framework-analysis.md](entity-framework-analysis.md).

## Sources

- [Lakebase — product](https://www.databricks.com/product/lakebase)
- [Lakebase — pricing](https://www.databricks.com/product/pricing/lakebase)
- [Serve lakehouse data with synced tables](https://docs.databricks.com/aws/en/oltp/instances/sync-data/sync-table)
- [Community — access a Delta table in UC from Lakebase Postgres](https://community.databricks.com/t5/lakebase-discussions/how-to-access-a-delta-table-in-uc-from-lakebase-postgres/td-p/144833)
- [Databricks Lakebase is now Generally Available (blog)](https://www.databricks.com/blog/databricks-lakebase-generally-available)
