# Entity Framework + Databricks SQL Warehouse — Feasibility Analysis

This document evaluates whether the ODBC connection in this repo can be rewritten to
use **Entity Framework**, and gives a recommendation. It is meant to be shared with the
client team.

## TL;DR

- Using **EF Core** against a Databricks SQL Warehouse is possible **only through a
  commercial provider (CData)**. There is **no free and no official** EF provider for
  Databricks — Databricks is not on Microsoft's supported EF Core providers list.
- The current ODBC script **cannot simply be "converted" to EF Core**: EF Core requires
  a database-specific provider (`UseSqlServer`, `UseNpgsql`, …) and there is **no generic
  ODBC provider** for EF Core.
- A Databricks SQL Warehouse is an **analytical (OLAP)** engine. EF's ORM features
  (migrations, change tracking, primary/foreign keys, identity columns, transactions)
  map poorly to it. The idiomatic approach for Databricks from .NET is **raw SQL / read
  queries**, not full ORM CRUD.

## Why the current ODBC code does not translate to EF Core

EF Core talks to a database through a **provider** that you register at startup, e.g.
`optionsBuilder.UseSqlServer(...)`. Each provider is specific to one database engine.

- `System.Data.Odbc` (what this repo uses) is an ADO.NET data access API, **not** an EF
  Core provider. There is **no `UseOdbc(...)`** in EF Core.
- The classic **EF6** framework had a broader provider model, but there is still **no
  free EF6 provider for Databricks** either.

So there is no drop-in, free way to keep the ODBC driver and get EF on top of it.

## Options compared

| Option | Is it EF? | Cost | Testable locally today | Fit for OLAP/Databricks | Effort |
| --- | --- | --- | --- | --- | --- |
| **CData `CData.Databricks.EntityFrameworkCore`** | Yes (EF Core) | Commercial / paid license | Only with a CData license | Read-focused; ORM CRUD still awkward | Medium |
| **Dapper** (micro-ORM over the existing ODBC connection) | No (but ORM-like: maps rows to C# classes) | Free | Yes | Good — thin layer over SQL | Low |
| **ADO.NET / ODBC directly** (already implemented in [`Program.cs`](../Program.cs)) | No | Free | Yes (already working) | Good | Done |

Notes:

- **CData** provides both an ADO.NET provider and an EF Core provider
  (`CData.Databricks.EntityFrameworkCore`). It uses its own managed connector, so it does
  **not** require the Simba/Databricks ODBC driver installed in this repo. It is the only
  path that gives "real" EF Core (LINQ, `DbContext`, entities). It requires a paid
  license and the CData product.
- **Dapper** is the closest **free** option to an ORM experience: you write SQL and it
  maps results into your C# classes with minimal boilerplate, reusing the ODBC connection
  already working here. It is not Entity Framework, but for querying Databricks it gives
  most of the ergonomic benefit without a license.

## The OLAP caveat (important)

Databricks SQL Warehouse is optimized for **analytics**, not transactional workloads:

- No enforced primary keys / no auto-increment identity in the OLTP sense.
- EF **migrations** and **change tracking** assume an OLTP schema lifecycle that does not
  apply here.
- Row-by-row `INSERT/UPDATE/DELETE` through an ORM is an anti-pattern; Delta tables prefer
  bulk operations and `MERGE`.

Because of this, teams often **do not** use EF for the Databricks part of their system
even when the rest of the app is EF-based — they use a thin SQL layer (Dapper / ADO.NET)
for reporting and read queries.

## Recommendation

- **If EF is a hard requirement** (e.g. for consistency with the client's existing EF
  codebase): use the **CData EF Core provider** and budget for the license. Expect to use
  it mainly for reads/projections, not ORM-driven writes.
- **If the goal is simply to query Databricks from .NET**: prefer **Dapper** (free,
  testable now) or the **ADO.NET/ODBC** approach already in this repo. This is the lower
  cost, lower risk path and fits Databricks' analytical nature.

## Questions to confirm with the client

1. **EF Core or EF6?** And which **.NET version** does their app target?
2. Do they already have (or would they buy) a **CData license**?
3. Is the intended usage **read/reporting only**, or do they need **CRUD** against
   Databricks?
4. Is EF required for **code consistency**, or is any data-access approach acceptable for
   the Databricks integration specifically?

Once answered, we can scaffold the chosen approach as a **separate console project** in
this repo (Dapper immediately and testable, or a CData EF Core `DbContext` documented for
when the license is available).

## Sources

- [CData.Databricks.EntityFrameworkCore (NuGet)](https://www.nuget.org/packages/CData.Databricks.EntityFrameworkCore)
- [CData — Getting started with EF Core for Databricks](https://cdn.cdata.com/help/LKH/ado/pg_efCoreOverview.htm)
- [EF Core — Database Providers (official list)](https://learn.microsoft.com/en-us/ef/core/providers/)
- [abp #11826 — Azure Databricks integration with EF Core](https://github.com/abpframework/abp/issues/11826)
- [Databricks ODBC Driver docs](https://docs.databricks.com/aws/en/integrations/odbc/)
