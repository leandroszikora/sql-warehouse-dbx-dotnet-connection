# Consuming Delta tables from .NET — three approaches compared

This document compares three ways to consume Databricks **Delta tables** from a .NET
application, in the context of a client that uses **Entity Framework** and wants to
**keep reading Delta tables without loading them into a transactional database**.

It is a companion to [entity-framework-analysis.md](entity-framework-analysis.md).

## TL;DR

1. **SQL Warehouse (direct, via ODBC/ADO.NET or Dapper)** reads Delta tables **live, with
   no data copy**. Already implemented in this repo and it matches the "no load"
   requirement.
2. **Lakebase** (Databricks' managed serverless Postgres) can serve Delta to a Postgres/EF
   app, but **only by copying** the data via managed **synced tables** (no zero-copy).
   Free native EF, but the copy is exactly what we want to avoid.
3. **Separate transactional DB + custom ETL** (e.g. Azure SQL / PostgreSQL fed by a
   Databricks job, dbt, ADF, etc.) — the DIY version of option 2. **Also copies** the
   data, but gives full control over the engine, schema and cost, and is the most
   EF-native (especially with SQL Server). Highest engineering/ops burden.

Only **option 1** satisfies "consume Delta without loading into a transactional DB".
Options 2 and 3 both materialize the data; they differ in *who builds and operates* the
copy (Databricks-managed vs. your team).

## What each option is

### 1. SQL Warehouse (direct)

A Databricks SQL Warehouse runs SQL **against the Delta tables in place**. The .NET app
connects over ODBC (this repo) or with Dapper on top of the same connection. Nothing is
materialized outside the lakehouse; you pay warehouse compute only while queries run.

### 2. Lakebase (managed synced tables)

A fully managed serverless **PostgreSQL** (Neon-based), GA on AWS, full ACID/OLTP. Because
it is Postgres, **Entity Framework Core works natively and for free** (Npgsql). But it
does not query Delta in place: it **replicates** Delta/Unity Catalog tables into Postgres
storage via managed Lakeflow **synced tables** (Snapshot / Triggered / Continuous, ≥15s
refresh). Data physically lives in **both** Delta and Postgres. This is a managed
reverse-ETL — you configure it, you don't build it.

### 3. Separate transactional DB + custom ETL

You stand up your own OLTP database (Azure SQL Server, PostgreSQL, etc.) and **build an
ETL/reverse-ETL pipeline** to move the relevant Delta data into it (a scheduled Databricks
job writing via JDBC, dbt, Azure Data Factory, Fivetran, a custom service, …). The .NET
app then reads that DB with **Entity Framework** normally — this is EF's home turf,
especially with SQL Server.

Trade-offs vs. Lakebase:

- **More control**: you pick the engine, schema, indexing, governance, and can co-locate
  the data with existing transactional tables the app already owns.
- **Potentially cheaper DB layer**: you can use a DB you already pay for / a cheaper tier,
  instead of Lakebase DBUs.
- **More work**: you design, run and monitor the pipeline yourself (freshness, retries,
  schema drift, CDC if you need low latency). Lakebase is essentially the "managed" answer
  to this exact pattern.

## Side-by-side

| Dimension | 1. SQL Warehouse (direct) | 2. Lakebase (synced tables) | 3. Separate DB + custom ETL |
| --- | --- | --- | --- |
| Reads Delta **without copying** | ✅ Yes, live in place | ❌ No — replicated to Postgres | ❌ No — replicated to your DB |
| Extra data duplication / storage | None | Duplicated (Delta + Postgres) | Duplicated (Delta + your DB) |
| Data freshness | Always current | Lag by sync mode (≥15s continuous) | Depends on your ETL cadence (batch → CDC) |
| Entity Framework (free/native) | ❌ No free provider (CData is paid) | ✅ Postgres → EF Core + Npgsql | ✅ Best fit (esp. SQL Server / Postgres) |
| Who builds/operates the copy | N/A (no copy) | Databricks-managed | **Your team** |
| Engineering / ops burden | Lowest (done) | Low (configure sync) | **Highest** (build + run pipeline) |
| Cost components | Warehouse compute on query | Lakebase compute + dup. storage + sync compute | Your DB + ETL compute + dup. storage + build/maintenance |
| Control over engine / schema | Lakehouse only | Postgres, limited | **Full** |
| Best fit | Analytics / reporting / read over Delta | Low-latency OLTP app serving, high-QPS lookups | Full EF CRUD, integrate with existing OLTP data, engine choice |
| Matches the "no load" requirement | ✅ Yes | ❌ No | ❌ No |

## Cost note

- **SQL Warehouse**: already paid for; bills compute while queries run and can auto-stop
  when idle. No extra storage.
- **Lakebase**: usage-based DBUs. Adds **(1)** Lakebase compute, **(2)** duplicated
  storage, **(3)** sync pipeline compute — additive on top of the warehouse you likely
  keep.
- **Separate DB + ETL**: adds **(1)** the transactional DB (license/compute/storage),
  **(2)** ETL compute (Databricks job / ADF / tool), **(3)** duplicated storage, and
  **(4)** the non-trivial **build and maintenance effort** (the biggest hidden cost). Can
  be cheaper on the DB layer than Lakebase, but you trade money for engineering time.

## Recommendation

- **Keep the SQL Warehouse (direct) approach** for the stated goal of reading Delta
  without loading a transactional DB. Use the existing ODBC/ADO.NET code, or add Dapper for
  free ORM-like ergonomics.
- **Choose Lakebase** if the use case moves to operational/app serving (sub-second
  lookups, high concurrency, transactional writes) and you want the copy **managed** for
  you — accepting the sync as a deliberate design choice, with free native EF as the payoff.
- **Choose a separate DB + custom ETL** if you specifically need **full EF CRUD**, a
  **particular engine** (e.g. the SQL Server the app already uses), or to **merge
  Delta-derived data with existing transactional data** — and the team is willing to own
  the pipeline. Note this is the same "copy into OLTP" pattern as Lakebase, just
  self-managed.
- **Entity Framework caveat**: "free EF" only exists once data is in a relational OLTP DB
  (options 2 and 3, i.e. after a copy). "EF directly on Delta, no copy" requires the
  commercial CData provider. See [entity-framework-analysis.md](entity-framework-analysis.md).

## Sources

- [Lakebase — product](https://www.databricks.com/product/lakebase)
- [Lakebase — pricing](https://www.databricks.com/product/pricing/lakebase)
- [Serve lakehouse data with synced tables](https://docs.databricks.com/aws/en/oltp/instances/sync-data/sync-table)
- [Community — access a Delta table in UC from Lakebase Postgres](https://community.databricks.com/t5/lakebase-discussions/how-to-access-a-delta-table-in-uc-from-lakebase-postgres/td-p/144833)
- [Databricks Lakebase is now Generally Available (blog)](https://www.databricks.com/blog/databricks-lakebase-generally-available)
- [EF Core — Database Providers (official list)](https://learn.microsoft.com/en-us/ef/core/providers/)
