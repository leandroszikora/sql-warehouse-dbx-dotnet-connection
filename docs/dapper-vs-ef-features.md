# Dapper vs. Entity Framework Core — ORM feature comparison

A common question: *does Dapper do the same things as Entity Framework as an ORM?*
Short answer: **no — they are different categories of tool.**

- **Entity Framework Core** is a **full ORM**: it manages objects, tracks changes,
  generates SQL for you (including `INSERT/UPDATE/DELETE`), models relationships, and
  handles schema migrations.
- **Dapper** is a **micro-ORM** (a "data mapper"): **you** write the SQL, and Dapper's job
  is to map the result rows onto C# objects (and map parameters in). It deliberately does
  *not* do change tracking, migrations, or SQL generation.

Think of it as **productivity/abstraction (EF)** vs. **control/performance (Dapper)**.

## Feature-by-feature

| Capability | Entity Framework Core | Dapper |
| --- | --- | --- |
| Category | Full ORM | Micro-ORM (data mapper) |
| Map rows → objects (POCOs) | ✅ | ✅ (core purpose) |
| Write SQL yourself | Optional (LINQ or raw) | **Always** (you own the SQL) |
| LINQ query provider (`Where`, `Join`, …) | ✅ | ❌ (write SQL) |
| Change tracking / dirty checking | ✅ | ❌ |
| `SaveChanges` / Unit of Work | ✅ | ❌ (execute statements yourself) |
| Auto-generated `INSERT/UPDATE/DELETE` | ✅ (from entities) | ❌ (raw SQL, or add-ons¹) |
| Relationships & navigation props (`Include`, lazy/eager loading) | ✅ | ⚠️ Manual (multi-mapping + `splitOn`) |
| Migrations / schema management | ✅ | ❌ |
| Identity map / first-level cache | ✅ | ❌ (returns fresh objects) |
| Optimistic concurrency tokens | ✅ | ⚠️ Manual |
| Value conversions, owned types, JSON columns, global filters, interceptors | ✅ | ❌ |
| Raw SQL / stored procedures | ✅ (`FromSqlRaw`) | ✅ |
| Parameterization (SQL-injection safe) | ✅ | ✅ |
| Async | ✅ | ✅ |
| Transactions | ✅ (integrated) | ✅ (manual, pass the tx) |
| Bulk operations | ⚠️ Add-ons / `ExecuteUpdate` | ⚠️ Add-ons¹ |
| Runtime overhead | Higher (tracking, translation) | Very low (thin over ADO.NET) |
| Raw read performance | Good | **Excellent** (near hand-written ADO.NET) |
| Learning curve | Steeper | Minimal (know SQL → productive) |
| **Requires a DB-specific provider** | ✅ **Yes** (`UseSqlServer`, `UseNpgsql`, …) | ❌ **No** — any ADO.NET/ODBC connection |

¹ Community add-ons close some gaps: **Dapper.Contrib** / **Dapper.SimpleCRUD**
(basic CRUD helpers from POCOs), **Dapper Plus** (bulk). They add convenience but Dapper
stays SQL-first.

## Why the differences matter *less* for Databricks

The features where EF clearly wins are mostly **write-side / OLTP** concerns — change
tracking, `SaveChanges`, migrations, generated CRUD, relationship graphs, concurrency
tokens. A Databricks **SQL Warehouse is analytical (OLAP)**:

- The typical .NET use case is **reading** Delta tables (reporting, lookups, feeding a
  service), not row-by-row ORM writes.
- Databricks has **no enforced primary/foreign keys or identity columns** in the OLTP
  sense, so EF's change tracking, migrations and relationship mapping have little to grab
  onto.
- **The decisive point:** EF Core **needs a database-specific provider**, and there is
  **no free/official Databricks provider** (only the commercial CData one). Dapper needs
  **only an ADO.NET connection**, so it works today over the ODBC driver this repo already
  uses — see [dapper-demo/](../dapper-demo/).

So for this scenario, most of EF's extra features are things you either **wouldn't use** or
**can't use** against Databricks anyway. Dapper covers the read/query use case fully, for
free, right now.

## Bottom line

| If you need… | Prefer |
| --- | --- |
| Read/query Delta from .NET, live, no data copy, no license | **Dapper** (or raw ADO.NET) |
| Full ORM (change tracking, migrations, generated CRUD, relationships) on Databricks | EF Core **+ commercial CData provider** (paid), accepting the OLAP mismatch |
| Full ORM against a **relational OLTP** copy of the data | EF Core on that DB (see [lakebase-vs-sql-warehouse.md](lakebase-vs-sql-warehouse.md)) |

For consuming Delta tables directly, **Dapper is the right-sized tool**: it gives the ORM
ergonomics that actually apply here (clean object mapping, parameters, async) without the
ORM machinery that Databricks can't leverage — and without a paid provider.

## Related docs

- [entity-framework-analysis.md](entity-framework-analysis.md) — why EF Core has no free
  Databricks provider.
- [lakebase-vs-sql-warehouse.md](lakebase-vs-sql-warehouse.md) — three ways to consume
  Delta from .NET (direct vs. Lakebase vs. separate DB + ETL).
