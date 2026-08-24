---
id: D-024
status: accepted
date: 2026-08-04
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Common-table-expression and temporal-table provider contracts"
supersedes: []
superseded-by: []
amends: [D-004]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-024 -- Support native CTEs and portable temporal tables

## Context and Problem Statement

Common table expressions and temporal tables are required provider features.
The repository previously exercised neither feature through a complete public
provider contract. A test named after a non-recursive CTE generated a `UNION`
query instead of a `WITH` expression, while temporal-table references covered
only ordinary date and time mappings.

The engines have different capabilities. MySQL and MariaDB both support
non-recursive and recursive CTE syntax. MariaDB also supports native
system-versioned tables. MySQL 8.4 does not provide the corresponding native
table feature, so portable provider behavior requires a provider-owned history
table and trigger implementation rather than an unsupported-provider result.

## Decision Drivers

- The provider must not be the limiting factor when an engine can perform the
  requested operation.
- Engine limitations and provider implementation status must remain separate.
- CTE composition must retain EF Core parameterization, tracking, async, and
  query-pipeline behavior.
- Temporal queries need one public, engine-independent EF-style contract.
- MariaDB must use its native system-versioning syntax and semantics.
- MySQL must receive equivalent provider semantics through explicit emulation.
- Schema operations that cannot preserve temporal history safely must fail
  before destructive SQL is executed.
- Reverse engineering must reconstruct temporal metadata rather than silently
  returning an ordinary table model.

## Considered Options

- Native CTEs plus native-or-emulated temporal routing
- Expose only raw SQL and native MariaDB temporal tables
- Treat CTEs and temporal tables as unsupported provider features

## Decision Outcome

Chosen option: "Native CTEs plus native-or-emulated temporal routing", because
it preserves native engine behavior while presenting one complete provider
contract on every supported engine.

CTEs use EF Core's existing composable SQL surface. The provider recognizes
`WITH` and `WITH RECURSIVE`, validates the configured engine boundary, and
retains parameter binding through `FromSql`, `FromSqlRaw`, `SqlQuery`, and
`SqlQueryRaw`. The provider does not invent a LINQ-to-named-CTE rewrite API:
EF Core 10 exposes CTE composition through its raw SQL query roots, and a
provider-specific expression DSL would duplicate query-shaping surface without
an upstream contract.

Explicit `EF.CompileQuery` delegates around `FromSql` or temporal `DbSet`
extensions are not part of this provider contract. EF Core replaces a `DbSet`
receiver with an `IQueryable` query root while funcletizing a compiled query,
then rejects the extension call before provider translation because its method
parameter remains `DbSet<T>`. The official EF Core 10 SQL Server temporal API
uses the same receiver contract. Normal query execution still uses EF Core's
query-shape compilation cache; the provider does not introduce a second,
incompatible query API to bypass an upstream expression-pipeline boundary.

MySQL 8.4 / 9.7 also accept CTEs as read sources for `UPDATE` and `DELETE`.
MariaDB 12.3 provides the corresponding data-modification grammar; MariaDB
10.11 / 11.4 / 11.8 do not. The provider exposes the available behavior and
records the older MariaDB boundary as an engine limitation, not as missing
provider support.

Temporal tables use public model-builder extensions for history-table and
period-column metadata plus query roots for `AsOf`, `All`, `FromTo`, `Between`,
and `ContainedIn`. Temporal query roots are no-tracking because one result can
contain several historical versions with the same primary key.

MariaDB 10.11 / 11.4 / 11.8 / 12.3 use `WITH SYSTEM VERSIONING`, `PERIOD FOR
SYSTEM_TIME`, and `FOR SYSTEM_TIME` queries. MySQL 8.4 / 9.7 use provider-owned
history tables and triggers whose names, annotations, rollback behavior, and
scaffolding contract are deterministic. Both routes share the same public
metadata and query API.

MariaDB introduced the dedicated [PERIODS][mariadb-periods] and
[KEY_PERIOD_USAGE][mariadb-key-period-usage] information-schema catalogs in
11.4, after application-time periods themselves. Reverse engineering therefore
uses those catalogs on 11.4 and later. On 10.11 it reads `ROW START` / `ROW END`
from `GENERATION_EXPRESSION` and parses only grammar tokens from canonical
`SHOW CREATE TABLE` output. The missing bulk catalogs require one metadata
round trip per selected table on 10.11. `SET STATEMENT` fixes the rendering SQL
mode for each query without mutating the caller-owned session and clears
`ANSI_QUOTES`, so server-rendered identifiers cannot be mistaken for quoted
string literals.

MariaDB application-time periods and bitemporal tables use a separate typed
provider contract. Model-builder extensions configure the application period,
`WITHOUT OVERLAPS`, and both dimensions of a bitemporal table. Migrations,
reverse engineering, generated model code, and typed `FOR PORTION OF`
`ExecuteUpdate` / `ExecuteDelete` roots preserve that contract. MySQL rejects
these exact operations as engine limitations because MySQL 8.4 / 9.7 have no
application-time period grammar.

Action-based application-time configuration resolves caller-selected endpoints
before adding conventional defaults. This keeps explicit CLR or named period
properties as the exact physical schema while retaining `ValidFrom` or
`ValidTo` for any endpoint the callback intentionally leaves unspecified.

Migration snapshots and migration designer models emit system-time,
application-time, and bitemporal metadata through the provider annotation code
generator. That is the design-time seam EF Core invokes for both artifacts.
The generated public Fluent API is compiled and compared with the source model;
an unchanged temporal model must produce no migration operations.

System-time mapping belongs to the physical table. A convention-owned
`OwnsOne` mapped to the owner's table therefore inherits the owner's history
and period contract. Explicit conflicts remain invalid. A separately stored
owned type does not inherit the contract, and temporal entity materialization
rejects its implicit current-only expansion. Scalar projections that do not
materialize that separate owned navigation remain valid.

### Consequences

- Good, because supported engines expose one provider contract without hiding
  native-versus-emulated behavior.
- Good, because CTEs remain inside EF Core's parameterization and composition
  pipeline instead of introducing string concatenation or a parallel query DSL.
- Good, because temporal schema and query differences are isolated behind
  typed capabilities and annotations.
- Good, because MariaDB application-time and bitemporal behavior is available
  without raw SQL and round-trips through migrations and scaffolding.
- Bad, because MySQL temporal emulation owns trigger correctness, schema-change
  safety, and history-table lifecycle.
- Bad, because reverse engineering must inspect both native metadata and the
  provider's emulation markers.

### Confirmation

- Run capability-boundary tests at MySQL 8.0.0/8.0.1, MariaDB 10.2.1/10.2.2,
  and MariaDB 10.3.3/10.3.4.
- Run live CTE contracts for non-recursive, recursive, multi-CTE, parameterized,
  sync, async, tracking, no-tracking, LINQ-composed, and server-aggregate use on
  every supported engine. Verify that the caller's exact cancellation token
  reaches the relational command boundary. Run data-modification use on MySQL
  and verify the documented MariaDB engine boundary independently.
- Run temporal metadata validation and migration round trips for every supported
  engine route.
- Run temporal query-boundary contracts for every query root, UTC precision,
  empty history, repeated keys, invalid shapes, sync, async, server aggregates,
  and exact cancellation-token forwarding.
- Run scaffolding round trips for native MariaDB and provider-emulated MySQL
  schemas.
- Run MariaDB 10.11, 11.4, 11.8, and 12.3 bitemporal contracts through generated
  DDL, version-appropriate metadata readback, generated model code, and typed
  portion update and delete operations.
- Run the complete unit, functional, integration, documentation, trimming, and
  release-candidate verification gates.

## Pros and Cons of the Options

### Native CTEs plus native-or-emulated temporal routing

- Good, because it implements the complete requested provider surface.
- Good, because native engine facilities remain visible in generated SQL.
- Bad, because the MySQL emulation path requires more migration and lifecycle
  tests than a native-only implementation.

### Expose only raw SQL and native MariaDB temporal tables

- Good, because it minimizes provider-owned SQL generation.
- Bad, because MySQL users receive no portable temporal contract and temporal
  metadata cannot round-trip across supported engines.

### Treat CTEs and temporal tables as unsupported provider features

- Good, because it avoids new provider code.
- Bad, because it violates the provider completeness contract even where the
  engine supports the required behavior.

## More Information

The CTE version boundaries are MySQL 8.0.1 and MariaDB 10.2.2. MariaDB native
system-versioned tables start at 10.3.4. The supported release matrix is newer,
but exact thresholds remain in `EngineProfileTable` so legacy compatibility
mode fails or routes deterministically.

MariaDB `BETWEEN` includes both bounds, while `FROM ... TO ...` excludes the
upper bound. The provider's temporal query expressions preserve those distinct
semantics instead of normalizing them into one approximate range operation.

### Re-evaluation Triggers

- EF Core adds a provider-independent CTE expression or temporal metadata
  contract that can replace the provider-specific public surface.
- MySQL adds native system-versioned tables compatible with the portable
  temporal contract.
- MariaDB changes native history retention or safe `ALTER TABLE` behavior.
- A supported engine cannot preserve temporal history for a required schema
  operation without an explicit migration procedure.

### Decision History

- 2026-08-04: Decision recorded with status accepted.
- 2026-08-05: Added the typed MariaDB application-time and bitemporal contract,
  including overlap constraints, reverse engineering, and portion mutations.
- 2026-08-11: Reconciled the six-line active-LTS matrix and admitted MariaDB
  12.3 CTE data modification while retaining the exact 10.11 / 11.4 / 11.8
  engine boundary.
- 2026-08-11: Closed the MariaDB 10.11 reverse-engineering gap with
  generation-expression metadata and a canonical table-definition parser.
- 2026-08-18: Bound temporal migration snapshots and designer models to the
  annotation-code-generator seam, added same-table owned inheritance, and made
  the separate current-owned materialization boundary explicit.
- 2026-08-18: Deferred application-time endpoint defaults until action-based
  configuration completes, preventing unconsumed conventional shadow columns.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Capabilities/EngineProfileTable.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Query`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Metadata`
- `src/Doka.EntityFrameworkCore.MySql/MySqlDbSetExtensions.cs`
- `src/Doka.EntityFrameworkCore.MySql/MySqlTableBuilderExtensions.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Query`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests`

### Sources

- [MySQL 8.4 common table expressions](https://dev.mysql.com/doc/refman/8.4/en/with.html)
  (primary source; retrieved 2026-08-04)
- [MySQL 8.4 DELETE statement](https://dev.mysql.com/doc/refman/8.4/en/delete.html)
  (primary source; retrieved 2026-08-04)
- [MySQL 8.4 trigger syntax](https://dev.mysql.com/doc/refman/8.4/en/trigger-syntax.html)
  (primary source; retrieved 2026-08-04)
- [MySQL 8.4 stored-program restrictions](https://dev.mysql.com/doc/refman/8.4/en/stored-program-restrictions.html)
  (primary source; retrieved 2026-08-04)
- [MySQL 9.7 common table expressions](https://dev.mysql.com/doc/refman/9.7/en/with.html)
  (primary source; retrieved 2026-08-11)
- [MySQL 9.7 DELETE statement](https://dev.mysql.com/doc/refman/9.7/en/delete.html)
  (primary source; retrieved 2026-08-11)
- [MySQL 9.7 trigger syntax](https://dev.mysql.com/doc/refman/9.7/en/trigger-syntax.html)
  (primary source; retrieved 2026-08-11)
- [MySQL 9.7 stored-program restrictions](https://dev.mysql.com/doc/refman/9.7/en/stored-program-restrictions.html)
  (primary source; retrieved 2026-08-11)
- [MySQL 8.0.1 release notes](https://dev.mysql.com/doc/relnotes/mysql/8.0/en/news-8-0-1.html)
  (primary source; retrieved 2026-08-04)
- [MariaDB common table expressions](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/with)
  (primary source; retrieved 2026-08-04)
- [MariaDB recursive common table expressions](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/recursive-common-table-expressions-overview)
  (primary source; retrieved 2026-08-04)
- [MariaDB 10.2.2 release notes](https://mariadb.com/docs/release-notes/community-server/old-releases/release-notes-mariadb-10-2-series/mariadb-1022-release-notes)
  (primary source; retrieved 2026-08-04)
- [MariaDB UPDATE statement](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/changing-deleting-data/update)
  (primary source; retrieved 2026-08-04)
- [MariaDB system-versioned tables](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/system-versioned-tables)
  (primary source; retrieved 2026-08-04)
- [MariaDB application-time periods](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/application-time-periods)
  (primary source; retrieved 2026-08-04)
- [MariaDB bitemporal tables](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/bitemporal-tables)
  (primary source; retrieved 2026-08-05)
- [MariaDB PERIODS information-schema table][mariadb-periods]
  (primary source; retrieved 2026-08-24)
- [MariaDB KEY_PERIOD_USAGE information-schema table][mariadb-key-period-usage]
  (primary source; retrieved 2026-08-24)
- [MariaDB SHOW CREATE TABLE](https://mariadb.com/docs/server/reference/sql-statements/administrative-sql-statements/show/show-create-table)
  (primary source; retrieved 2026-08-11)
- [MariaDB ALTER TABLE](https://mariadb.com/docs/server/reference/sql-statements/data-definition/alter/alter-table)
  (primary source; retrieved 2026-08-04)
- [MariaDB SET STATEMENT](https://mariadb.com/docs/server/reference/sql-statements/administrative-sql-statements/set-commands/set-statement)
  (primary source; retrieved 2026-08-04)
- [EF Core SQL Server temporal tables](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/temporal-tables)
  (primary source; retrieved 2026-08-04)
- [EF Core SQL queries](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)
  (primary source; retrieved 2026-08-04)
- [EF Core advanced performance topics](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics)
  (primary source; retrieved 2026-08-04)
- [EF Core async programming](https://learn.microsoft.com/en-us/ef/core/miscellaneous/async)
  (primary source; retrieved 2026-08-04)
- [EF Core 10.0.10 relational SQL query extensions](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Extensions/RelationalQueryableExtensions.cs)
  (primary source; retrieved 2026-08-04)
- [EF Core 10.0.10 SQL Server temporal query extensions](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.SqlServer/Extensions/SqlServerDbSetExtensions.cs)
  (primary source; retrieved 2026-08-04)
- [EF Core 10.0.11 snapshot generator](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Design/Migrations/Design/CSharpSnapshotGenerator.cs)
  (primary source; retrieved 2026-08-18)
- [EF Core 10.0.10 SQL Server temporal annotation generator](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.SqlServer/Design/Internal/SqlServerAnnotationCodeGenerator.cs)
  (primary source; retrieved 2026-08-18)
- [EF Core eager loading and owned-navigation behavior](https://learn.microsoft.com/en-us/ef/core/querying/related-data/eager)
  (primary source; retrieved 2026-08-18)
- [EF Core issue 29303: temporal table splitting](https://github.com/dotnet/efcore/issues/29303)
  (primary source; retrieved 2026-08-18)

[mariadb-periods]:
  https://mariadb.com/docs/server/reference/system-tables/information-schema/information-schema-tables/information-schema-periods-table
[mariadb-key-period-usage]:
  https://mariadb.com/docs/server/reference/system-tables/information-schema/information-schema-tables/information-schema-key_period_usage-table
