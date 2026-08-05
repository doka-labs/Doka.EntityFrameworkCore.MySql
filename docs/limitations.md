# External Engine and EF Core Limitations

This document is the public inventory of behavior that the provider cannot
make available because the supported database engine or the consumed EF Core
framework does not expose the required contract.

These entries are external facts, not architecture decisions. D-021 defines
how an external limitation must be proved and governed. The machine-readable
authority is
`tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/SpecDispositions.json`.
This document is its readable projection.

The contract has a zero provider-gap budget. A behavior is not listed here
when the engine and EF Core can represent it: in that case the provider must
implement it. The word "unavailable" below applies only to the exact contract
described by the entry, not to the surrounding feature family.

The supported targets are MySQL 8.4, MariaDB 11.4, and MariaDB 11.8. Source
retrieval dates are recorded per entry. This projection was last reconciled
on 2026-08-05.

## Clarification of the D-004 capability cleanup

D-004 removed five unconsumed internal capability flags. It did not remove
the corresponding database or provider features. `EngineProfile` deliberately
contains only version-derived facts that have an active behavior-routing
consumer. A generally supported feature does not need a runtime switch.

### Window functions

Native window functions remain supported. MySQL 8.4 and the supported MariaDB
lines provide window-function syntax. The provider's dynamic-offset rewrite
emits EF Core's `RowNumberExpression`, and provider tests exercise that
generated `ROW_NUMBER()` shape. The removed flag had no production consumer
and controlled neither path.

The separate public boundary is the absence of a general strongly typed LINQ
API for arbitrary window expressions. Raw SQL remains available for native
window expressions; the exact boundary is documented below.

Primary sources, retrieved 2026-08-05:

- [MySQL 8.4 Window Function Descriptions][mysql-window-functions]
- [MariaDB Window Functions Overview][mariadb-window-functions]
- [dotnet/efcore window-function API epic][efcore-window-functions]

### `datetime(6)`

Fractional-second temporal mappings remain supported. MySQL and MariaDB accept
fractional-second precision from zero through six digits. The removed flag was
always true for the supported targets and had no behavior-routing consumer.
The distinct seven-digit .NET precision boundary is documented in the engine
limitations inventory below.

Primary sources, retrieved 2026-08-05:

- [MySQL 8.4 Fractional Seconds in Time Values][mysql-fractional-seconds]
- [MariaDB Microseconds in MariaDB][mariadb-microseconds]

### Generated invisible primary keys

MySQL generated invisible primary keys are server automation controlled by
`sql_generate_invisible_primary_key`. They are not an EF Core translation or
provider capability boundary. The provider does not advertise this MySQL-only
server setting as a portable model-building feature. Removing the diagnostic
flag did not disable the MySQL server behavior.

Primary source, retrieved 2026-08-05:

- [MySQL 8.4 Generated Invisible Primary Keys][mysql-gipk]

### `INTERSECT` and `EXCEPT`

Relational set operations remain supported on all supported targets. MySQL 8.4
documents `INTERSECT` and `EXCEPT`, and MariaDB has supported `INTERSECT` since
10.3. The provider uses EF Core's relational set-operation SQL tree; no
version-routing flag is required for the supported version floor.

Primary sources, retrieved 2026-08-05:

- [MySQL 8.4 Set Operations][mysql-set-operations]
- [MariaDB INTERSECT][mariadb-intersect]
- [MariaDB EXCEPT][mariadb-except]

### Full-text indexes and search

Full-text indexes and provider full-text query translation remain supported.
Both engines expose FULLTEXT indexes. The removed flag did not gate migrations,
scaffolding, or query translation and therefore represented dead metadata.

Primary sources, retrieved 2026-08-05:

- [MySQL 8.4 Full-Text Search Functions][mysql-full-text]
- [MariaDB Full-Text Index Overview][mariadb-full-text]

## Engine Limitations

The following 15 entries are exact server boundaries from the active
specification disposition ledger.

### `MYSQL-MARIADB-SCALAR-STORED-FUNCTIONS`

- **Unavailable contract:** A stored function that returns a parameterized
  rowset consumable in a `FROM` clause. MySQL and MariaDB stored functions are
  scalar routines.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [MySQL Stored Routines][mysql-stored-routines],
  [MariaDB Stored Function Overview][mariadb-stored-function-overview], and
  [MariaDB CREATE FUNCTION][mariadb-create-function], retrieved 2026-07-29.

### `MYSQL-MARIADB-TEMPORAL-MICROSECOND-PRECISION`

- **Unavailable contract:** Lossless storage of the seventh fractional-second
  digit from .NET temporal values. Both engines store at most six digits.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [MySQL Fractional Seconds][mysql-fractional-seconds] and
  [MariaDB Microseconds][mariadb-microseconds], retrieved 2026-07-29.

### `MYSQL-MARIADB-JSON-DOCUMENT-VALIDATION`

- **Unavailable contract:** Persisting malformed JSON so EF Core can exercise
  malformed-document materialization. The engines validate the value before
  it reaches the materializer.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [MySQL JSON Data Type][mysql-json] and
  [MariaDB JSON Data Type][mariadb-json], retrieved 2026-07-29.

### `MYSQL84-POINT-EMPTY`

- **Unavailable contract:** A native empty `Point` value. MySQL 8.4 rejects an
  empty Point, although it permits an empty GeometryCollection.
- **Targets:** MySQL 8.4.
- **Primary source:** [MySQL Spatial Argument Handling][mysql-spatial-arguments],
  retrieved 2026-07-29.

### `MYSQL-MARIADB-SPATIAL-ZM-ORDINATES`

- **Unavailable contract:** Lossless native persistence of Z and M ordinates.
  The supported engines persist the XY coordinate pair.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [MySQL Geometry Class Hierarchy][mysql-geometry] and
  [MariaDB GEOMETRY_COLUMNS][mariadb-geometry-columns], retrieved 2026-07-29.

### `MYSQL-MARIADB-SPATIAL-NORMALIZE`

- **Unavailable contract:** Server-side NetTopologySuite `Normalize()`
  semantics for an arbitrary geometry.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [MySQL Spatial Function Reference][mysql-spatial-functions]
  and [MariaDB Geometry Constructors][mariadb-geometry-constructors], retrieved
  2026-07-29.

### `MYSQL84-SPATIAL-RELATE`

- **Unavailable contract:** Evaluating an arbitrary DE-9IM pattern through
  `ST_Relate` on MySQL 8.4.
- **Targets:** MySQL 8.4.
- **Primary source:** [MySQL Spatial Function Reference][mysql-spatial-functions],
  retrieved 2026-07-29.

### `MYSQL-MARIADB-SPATIAL-REVERSE`

- **Unavailable contract:** Reversing arbitrary geometry component order with
  server-side NetTopologySuite `Reverse()` semantics.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [MySQL Spatial Function Reference][mysql-spatial-functions]
  and [MariaDB Geometry Constructors][mariadb-geometry-constructors], retrieved
  2026-07-29.

### `MARIADB-SPATIAL-BUFFER-STRATEGY`

- **Unavailable contract:** Passing the NetTopologySuite quadrant-segment
  buffer strategy to MariaDB `ST_Buffer`; the supported versions accept the
  distance form only.
- **Targets:** MariaDB 11.4 and MariaDB 11.8.
- **Primary source:** [MariaDB ST_Buffer][mariadb-buffer], retrieved 2026-07-29.

### `MARIADB-SPATIAL-COLLECT`

- **Unavailable contract:** Server-side `ST_Collect` on the supported MariaDB
  lines. MariaDB documents the function as available from 12.0.
- **Targets:** MariaDB 11.4 and MariaDB 11.8.
- **Primary source:** [MariaDB ST_Collect][mariadb-collect], retrieved 2026-07-29.

### `MARIADB-SPATIAL-VALIDITY`

- **Unavailable contract:** Server-side `ST_IsValid` on the supported MariaDB
  lines. MariaDB documents the function as available from 12.0.
- **Targets:** MariaDB 11.4 and MariaDB 11.8.
- **Primary source:** [MariaDB ST_IsValid][mariadb-is-valid], retrieved 2026-07-29.

### `MYSQL-MARIADB-IMMEDIATE-SELF-FK-DELETE`

- **Unavailable contract:** The affected self-referencing delete while an
  immediate `NO ACTION` foreign key still references the row.
- **Targets:** MySQL 8.4 and MariaDB 11.4. The executable MariaDB 11.8 probe
  passes and is not dispositioned.
- **Primary sources:** [MySQL Foreign Keys][mysql-foreign-keys] and
  [MariaDB Foreign Keys][mariadb-foreign-keys], retrieved 2026-07-27.

### `MYSQL-MARIADB-FILTERED-INDEXES`

- **Unavailable contract:** A partial or filtered index with a row predicate.
  Neither engine's `CREATE INDEX` grammar provides a filter predicate.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [MySQL CREATE INDEX][mysql-create-index] and
  [MariaDB CREATE INDEX][mariadb-create-index], retrieved 2026-07-28.

### `MDB-CORRELATED-DERIVED-TABLE`

- **Unavailable contract:** A correlated subquery inside the `FROM` clause,
  including the derived-table boundary needed by the affected nested JSON
  collection shapes. The supported MariaDB grammar has no LATERAL/APPLY join.
- **Targets:** MariaDB 11.4 and MariaDB 11.8.
- **Primary sources:** [MariaDB Subquery Limitations][mariadb-subquery-limitations]
  and [MariaDB JOIN Syntax][mariadb-join-syntax], retrieved 2026-07-29.

### `MDB-JSON-TABLE-SUBDOCUMENT`

- **Unavailable contract:** Extracting an object or array subdocument into a
  JSON result column of `JSON_TABLE` for complete owned-graph materialization.
- **Targets:** MariaDB 11.4 and MariaDB 11.8.
- **Primary source:** [MariaDB JSON_TABLE][mariadb-json-table], retrieved
  2026-07-27.

## EF Core Limitations

The following 25 entries fail in framework-owned translation, validation, or
materialization before provider SQL generation can supply the missing behavior.
The targets are MySQL 8.4, MariaDB 11.4, and MariaDB 11.8 for every entry.

### `EFCORE-28525-BULK-ENTITY-PROJECTION`

- **Unavailable contract:** `ExecuteDelete` over a grouped entity projection.
- **Primary source:** [dotnet/efcore issue 28525][efcore-28525], retrieved
  2026-07-27.

### `EFCORE-26753-GROUPING-FIRST-PROPERTY`

- **Unavailable contract:** Binding an entity key from `GroupBy` followed by
  `First` in the affected projection.
- **Primary source:** [dotnet/efcore issue 26753][efcore-26753], retrieved
  2026-07-27.

### `EFCORE-TPC-NONLEAF-BULK-UPDATE`

- **Unavailable contract:** `ExecuteUpdate` for a non-leaf TPC entity. EF Core
  rejects the shape before provider validation.
- **Primary sources:** [EF Core inheritance bulk-update tests][efcore-tpc-tests]
  and [EF Core ExecuteUpdate translation][efcore-execute-update], retrieved
  2026-07-27.

### `EFCORE-31397`

- **Unavailable contract:** Applying `Distinct` to a JSON collection-property
  projection that has no stable framework identifier.
- **Primary source:** [dotnet/efcore issue 31397][efcore-31397], retrieved
  2026-07-27.

### `EFCORE-29287`

- **Unavailable contract:** `GroupBy`, ordering on a JSON scalar, and
  `FirstOrDefault` in the affected query shape.
- **Primary source:** [dotnet/efcore issue 29287][efcore-29287], retrieved
  2026-07-27.

### `EFCORE-28733`

- **Unavailable contract:** Binding a JSON-owned property above the affected
  `FirstOrDefault` subquery.
- **Primary source:** [dotnet/efcore issue 28733][efcore-28733], retrieved
  2026-07-27.

### `EFCORE-28645`

- **Unavailable contract:** Backtracking from the affected nested JSON-owned
  entity to its parent.
- **Primary source:** [dotnet/efcore issue 28645][efcore-28645], retrieved
  2026-07-27.

### `EFCORE-24263`

- **Unavailable contract:** Preserving a nested JSON collection projection
  through two query pushdowns.
- **Primary source:** [dotnet/efcore issue 24263][efcore-24263], retrieved
  2026-07-27.

### `EFCORE-29416`

- **Unavailable contract:** Correct null comparison for a value converter that
  handles nulls in the affected query.
- **Primary source:** [dotnet/efcore issue 29416][efcore-29416], retrieved
  2026-07-27.

### `EFCORE-29014`

- **Unavailable contract:** Expanding a navigation through a grouping key
  after grouping.
- **Primary source:** [dotnet/efcore issue 29014][efcore-29014], retrieved
  2026-07-29.

### `EFCORE-27130`

- **Unavailable contract:** Correctly binding the affected outer aggregate
  after an inner grouping is simplified.
- **Primary source:** [dotnet/efcore issue 27130][efcore-27130], retrieved
  2026-07-29.

### `EFCORE-35028`

- **Unavailable contract:** Retaining every component of the affected nested
  anonymous join key.
- **Primary source:** [dotnet/efcore issue 35028][efcore-35028], retrieved
  2026-07-29.

### `EFCORE-COMPLEX-COLLECTION-TRACKING`

- **Unavailable contract:** Tracking every struct, readonly struct, record,
  and array shape used by complex collections.
- **Primary sources:** [dotnet/efcore issue 31411][efcore-31411],
  [issue 31621][efcore-31621], and [issue 36483][efcore-36483], retrieved
  2026-07-29.

### `EFCORE-31411-COMPLEX-COLLECTION-STORE-VALUES`

- **Unavailable contract:** Exposing complex-collection store values through
  the affected `EntityEntry` APIs.
- **Primary source:** [dotnet/efcore issue 31411][efcore-31411], retrieved
  2026-07-29.

### `EFCORE-13890-COMPLEX-CONCURRENCY-VALUES`

- **Unavailable contract:** Consistently aggregating nested complex members in
  the affected database-value APIs.
- **Primary source:** [dotnet/efcore issue 13890][efcore-13890], retrieved
  2026-07-29.

### `EFCORE-35613-TABLE-SPLITTING-COMPLEX-TYPES`

- **Unavailable contract:** Table splitting with shared complex columns when
  the model requires shadow complex properties rejected by core validation.
- **Primary source:** [dotnet/efcore issue 35613][efcore-35613], retrieved
  2026-07-29.

### `EFCORE-32303-CORRELATED-NAVIGATION-PAGINATION`

- **Unavailable contract:** Rewriting the affected correlated navigation with
  pagination to APPLY instead of leaving it in a normal join.
- **Primary source:** [dotnet/efcore issue 32303][efcore-32303], retrieved
  2026-07-29.

### `EFCORE-21332-MANY-TO-MANY-INCLUDE-MERGING`

- **Unavailable contract:** Merging the affected equivalent many-to-many
  includes.
- **Primary source:** [dotnet/efcore issue 21332][efcore-21332], retrieved
  2026-07-29.

### `EFCORE-32611-JSON-PRIMITIVE-ARRAY-PROJECTION`

- **Unavailable contract:** Materializing the affected JSON primitive-array
  projection when the framework reader receives an object token.
- **Primary source:** [dotnet/efcore issue 32611][efcore-32611], retrieved
  2026-07-29.

### `EFCORE-15743-RELATIONAL-NULL-JOIN-KEY`

- **Unavailable contract:** Preserving nullable Boolean join-key semantics
  when relational null semantics turns the affected key into a two-valued
  `CASE` expression.
- **Primary source:** [dotnet/efcore issue 15743][efcore-15743], retrieved
  2026-07-29.

### `EFCORE-33378-PRECOMPILED-JSON-SET-OPERATIONS`

- **Unavailable contract:** Expanding JSON-owned projections across set
  operations during precompiled-query generation.
- **Primary source:** [dotnet/efcore issue 33378][efcore-33378], retrieved
  2026-07-29.

### `EFCORE-31277-NULLABLE-COLLECTION-DISTINCT`

- **Unavailable contract:** Distinguishing an empty projected collection from
  a one-element null collection when every `Distinct` identifier is nullable.
- **Primary source:** [dotnet/efcore issue 31277][efcore-31277], retrieved
  2026-07-29.

### `EFCORE-18923-GROUPBY-CLIENT-EVAL-GUARD`

- **Unavailable contract:** Completing the affected no-client-evaluation
  `GroupBy` guard without entering an invalid internal collection state.
- **Primary source:** [dotnet/efcore issue 18923][efcore-18923], retrieved
  2026-07-29.

### `EFCORE-16298-INHERITANCE-SET-OPERATIONS`

- **Unavailable contract:** Set operations between the affected different
  inheritance projections. EF Core rejects them before provider translation.
- **Primary source:** [dotnet/efcore issue 16298][efcore-16298], retrieved
  2026-07-29.

### `EFCORE-35613-COMPLEX-TYPE-SHADOW-PROPERTIES`

- **Unavailable contract:** Shadow properties on complex types in the affected
  model shapes. EF Core validation rejects the model before SQL generation.
- **Primary sources:** [EF Core complex-type model tests][efcore-complex-tests],
  [complex-collection model tests][efcore-complex-collection-tests], and
  [ModelValidator][efcore-model-validator], retrieved 2026-07-27.

## Public Boundaries Outside the Specification Ledger

The specification ledger governs inherited EF Core conformance tests. The
following public provider boundaries are not represented by inherited test
dispositions and are therefore maintained explicitly here.

### General LINQ window-function API

- **Unavailable contract:** A general strongly typed LINQ API for expressing
  arbitrary window partitions, ordering, and frames.
- **Available contracts:** Engine-native window expressions can be issued
  through EF Core raw SQL. Provider-owned query rewrites that require
  `ROW_NUMBER()` continue to emit and test that SQL shape.
- **Responsibility:** EF Core has not defined the public LINQ expression
  contract. The upstream API-design issue remains open and in the backlog.
  This is not an engine limitation and did not result from removing the
  unconsumed D-004 flag.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [dotnet/efcore window-function API
  epic][efcore-window-functions] and [EF Core SQL Queries][efcore-sql-queries],
  retrieved 2026-08-05.

### `StringComparison` overloads in translated queries

- **Unavailable contract:** Exact .NET `StringComparison` semantics for
  translated `Equals`, `Contains`, `StartsWith`, and `EndsWith` overloads.
- **Available contracts:** Ordinary string operations use the configured
  database or column collation. `EF.Functions.Collate` explicitly selects a
  database collation when a query requires a different comparison contract.
- **Responsibility:** EF Core deliberately does not translate the
  `StringComparison` overload of `string.Equals` because it cannot infer which
  database collation should represent the requested .NET semantics. The
  provider applies the same explicit-collation rule to the related string
  operations rather than emitting an approximation that can change semantics
  or prevent index use.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary source:** [EF Core Collations and Case
  Sensitivity][efcore-collations], retrieved 2026-08-05.

### Database-local relational schemas

- **Unavailable contract:** Independent relational schema namespaces inside
  one configured MySQL or MariaDB database, including a schema-qualified
  migrations history table or sequence.
- **Provider behavior:** Table and view schema values are preserved as
  database qualifiers for cross-database access. Sequence schemas and a
  non-empty migrations history schema are rejected rather than silently
  treating a database name as a database-local namespace.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [MySQL CREATE DATABASE][mysql-schema-database] and
  [MariaDB CREATE DATABASE][mariadb-create-database], retrieved 2026-08-05.
  Both sources define `SCHEMA` as a synonym for `DATABASE`.

## Feature-specific Boundaries Outside the Specification Ledger

The provider fully supports common table expressions and exposes a portable
temporal-table API. Some exact engine and framework boundaries do not
correspond to inherited specification skips:

### MariaDB CTE data modification

- **Unavailable contract:** A CTE directly preceding `UPDATE` or `DELETE` on
  the supported MariaDB 11.4 and 11.8 lines.
- **Available contracts:** Read CTEs, including recursive CTEs, remain fully
  supported. MySQL 8.4 also supports the provider's documented CTE data-
  modification shapes.
- **Responsibility:** The required data-modification grammar is absent from the
  supported MariaDB lines. MariaDB documents CTE-backed `UPDATE` support as a
  later server capability beginning with MariaDB 12.3.
- **Targets:** MariaDB 11.4 and MariaDB 11.8.
- **Primary sources:** [MariaDB common table expressions][mariadb-cte] and
  [MariaDB `UPDATE`][mariadb-update], retrieved 2026-08-04.

### Compiled-query wrappers for raw SQL and temporal roots

- **Unavailable contract:** Explicit `EF.CompileQuery` or
  `EF.CompileAsyncQuery` wrappers whose query root is an affected `FromSql` or
  temporal extension call.
- **Available contracts:** The same queries execute through their normal sync
  and async LINQ paths, and EF Core's ordinary internal query-plan cache
  remains active.
- **Responsibility:** EF Core's compiled-query preprocessing replaces the
  `DbSet` receiver before these root-only extension methods are bound. The
  resulting expression is rejected by EF Core before provider translation.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [EF Core advanced performance
  topics][efcore-advanced-performance], [EF Core 10.0.10 relational SQL query
  extensions][efcore-relational-query-extensions], and [EF Core 10.0.10 SQL
  Server temporal query extensions][efcore-sqlserver-temporal-extensions],
  retrieved 2026-08-04.

### MariaDB temporal structural migrations

- **Unavailable contract:** Preserving native system-versioned history through
  every add, alter, drop, or rename operation, and mapping generated columns
  on a system-versioned table.
- **Available contracts:** The provider supports explicit destructive
  deactivation, schema change, and reactivation when the migration accepts the
  loss of prior history. Safe temporal creation and removal remain supported.
- **Responsibility:** MariaDB documents restricted structural changes for
  system-versioned tables and does not permit generated columns on them. The
  provider rejects unsafe shapes instead of enabling a session-wide history
  override or silently losing history.
- **Targets:** MariaDB 11.4 and MariaDB 11.8.
- **Primary sources:** [MariaDB system-versioned
  tables][mariadb-system-versioned-tables], [MariaDB `ALTER
  TABLE`][mariadb-alter-table], and [MariaDB `SET
  STATEMENT`][mariadb-set-statement], retrieved 2026-08-04.

### MySQL emulated-temporal cascade actions

- **Unavailable contract:** Database-side `Cascade` or `SetNull` foreign-key
  actions on a MySQL table using the provider's temporal-history emulation.
- **Available contracts:** Non-cascading referential actions remain supported,
  as do temporal inserts, updates, deletes, queries, and history maintenance.
- **Responsibility:** MySQL does not activate triggers for cascaded foreign-key
  actions. Because the portable MySQL temporal route records history through
  triggers, allowing those actions would silently omit historical versions.
- **Targets:** MySQL 8.4.
- **Primary sources:** [MySQL 8.4 trigger syntax][mysql-trigger-syntax],
  [MySQL 8.4 stored-program
  restrictions][mysql-stored-program-restrictions], and [MySQL 8.4 foreign-key
  constraints][mysql-foreign-keys], retrieved 2026-08-04.

See [Temporal Tables and Common Table Expressions](temporal-tables-and-ctes.md)
for the complete feature contract, executable examples, and migration
guidance. This inventory remains the concise cross-feature index of external
limitations; the feature guide provides the operational detail.

## Maintenance Contract

- Every active `engine-limitation` and `framework-limitation` ledger entry must
  appear exactly once in this document.
- Every manually maintained public boundary must identify its exact unavailable
  contract, supported targets, primary source, and source retrieval date.
- Structural `not-applicable` dispositions are excluded because they are not
  engine or EF Core limitations.
- Provider workarounds and emulations are supported behavior, not limitations.
- A source must be rechecked and its retrieval date updated when a supported
  engine or EF Core patch changes the affected boundary.
- A resolved external limitation is removed from the ledger, this projection,
  and the corresponding executable skip in the same change.

[mysql-window-functions]: https://dev.mysql.com/doc/refman/8.4/en/window-functions.html
[mariadb-window-functions]: https://mariadb.com/docs/server/reference/sql-functions/special-functions/window-functions/window-functions-overview
[mysql-fractional-seconds]: https://dev.mysql.com/doc/refman/8.4/en/fractional-seconds.html
[mariadb-microseconds]: https://mariadb.com/docs/server/reference/sql-functions/date-time-functions/microseconds-in-mariadb
[mysql-gipk]: https://dev.mysql.com/doc/refman/8.4/en/create-table-gipks.html
[mysql-set-operations]: https://dev.mysql.com/doc/refman/8.4/en/set-operations.html
[mariadb-intersect]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/joins-subqueries/intersect
[mariadb-except]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/joins-subqueries/except
[mysql-full-text]: https://dev.mysql.com/doc/refman/8.4/en/fulltext-search.html
[mariadb-full-text]: https://mariadb.com/docs/server/ha-and-performance/optimization-and-tuning/optimization-and-indexes/full-text-indexes/full-text-index-overview
[mysql-schema-database]: https://dev.mysql.com/doc/refman/8.4/en/create-database.html
[mariadb-create-database]: https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-database
[efcore-window-functions]: https://github.com/dotnet/efcore/issues/12747
[efcore-sql-queries]: https://learn.microsoft.com/en-us/ef/core/querying/sql-queries
[efcore-collations]: https://learn.microsoft.com/en-us/ef/core/miscellaneous/collations-and-case-sensitivity
[efcore-advanced-performance]: https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics
[efcore-relational-query-extensions]: https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Extensions/RelationalQueryableExtensions.cs
[efcore-sqlserver-temporal-extensions]: https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.SqlServer/Extensions/SqlServerDbSetExtensions.cs
[mysql-stored-routines]: https://dev.mysql.com/doc/refman/8.4/en/stored-routines-syntax.html
[mysql-trigger-syntax]: https://dev.mysql.com/doc/refman/8.4/en/trigger-syntax.html
[mysql-stored-program-restrictions]: https://dev.mysql.com/doc/refman/8.4/en/stored-program-restrictions.html
[mariadb-stored-function-overview]: https://mariadb.com/docs/server/server-usage/stored-routines/stored-functions/stored-function-overview
[mariadb-create-function]: https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-function
[mariadb-cte]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/with
[mariadb-update]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/changing-deleting-data/update
[mariadb-system-versioned-tables]: https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/system-versioned-tables
[mariadb-alter-table]: https://mariadb.com/docs/server/reference/sql-statements/data-definition/alter/alter-table
[mariadb-set-statement]: https://mariadb.com/docs/server/reference/sql-statements/administrative-sql-statements/set-commands/set-statement
[mysql-json]: https://dev.mysql.com/doc/refman/8.4/en/json.html
[mariadb-json]: https://mariadb.com/docs/server/reference/data-types/string-data-types/json
[mysql-spatial-arguments]: https://dev.mysql.com/doc/refman/8.4/en/spatial-function-argument-handling.html
[mysql-geometry]: https://dev.mysql.com/doc/refman/8.4/en/gis-class-geometry.html
[mariadb-geometry-columns]: https://mariadb.com/docs/server/reference/system-tables/information-schema/information-schema-tables/information-schema-geometry_columns-table
[mysql-spatial-functions]: https://dev.mysql.com/doc/refman/8.4/en/spatial-function-reference.html
[mariadb-geometry-constructors]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors
[mariadb-buffer]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/geometry-constructors/st_buffer
[mariadb-collect]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/miscellaneous-gis-functions/st_collect
[mariadb-is-valid]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/miscellaneous-gis-functions/st_isvalid
[mysql-foreign-keys]: https://dev.mysql.com/doc/refman/8.4/en/create-table-foreign-keys.html
[mariadb-foreign-keys]: https://mariadb.com/docs/server/ha-and-performance/optimization-and-tuning/optimization-and-indexes/foreign-keys
[mysql-create-index]: https://dev.mysql.com/doc/refman/8.4/en/create-index.html
[mariadb-create-index]: https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-index
[mariadb-subquery-limitations]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/subqueries/subquery-limitations
[mariadb-join-syntax]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/joins/join-syntax
[mariadb-json-table]: https://mariadb.com/docs/server/reference/sql-functions/special-functions/json-functions/json_table
[efcore-28525]: https://github.com/dotnet/efcore/issues/28525
[efcore-26753]: https://github.com/dotnet/efcore/issues/26753
[efcore-tpc-tests]: https://github.com/dotnet/efcore/blob/v10.0.8/test/EFCore.Specification.Tests/BulkUpdates/InheritanceBulkUpdatesTestBase.cs
[efcore-execute-update]: https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.ExecuteUpdate.cs
[efcore-31397]: https://github.com/dotnet/efcore/issues/31397
[efcore-29287]: https://github.com/dotnet/efcore/issues/29287
[efcore-28733]: https://github.com/dotnet/efcore/issues/28733
[efcore-28645]: https://github.com/dotnet/efcore/issues/28645
[efcore-24263]: https://github.com/dotnet/efcore/issues/24263
[efcore-29416]: https://github.com/dotnet/efcore/issues/29416
[efcore-29014]: https://github.com/dotnet/efcore/issues/29014
[efcore-27130]: https://github.com/dotnet/efcore/issues/27130
[efcore-35028]: https://github.com/dotnet/efcore/issues/35028
[efcore-31411]: https://github.com/dotnet/efcore/issues/31411
[efcore-31621]: https://github.com/dotnet/efcore/issues/31621
[efcore-36483]: https://github.com/dotnet/efcore/issues/36483
[efcore-13890]: https://github.com/dotnet/efcore/issues/13890
[efcore-35613]: https://github.com/dotnet/efcore/issues/35613
[efcore-32303]: https://github.com/dotnet/efcore/issues/32303
[efcore-21332]: https://github.com/dotnet/efcore/issues/21332
[efcore-32611]: https://github.com/dotnet/efcore/issues/32611
[efcore-15743]: https://github.com/dotnet/efcore/issues/15743
[efcore-33378]: https://github.com/dotnet/efcore/issues/33378
[efcore-31277]: https://github.com/dotnet/efcore/issues/31277
[efcore-18923]: https://github.com/dotnet/efcore/issues/18923
[efcore-16298]: https://github.com/dotnet/efcore/issues/16298
[efcore-complex-tests]: https://github.com/dotnet/efcore/blob/v10.0.8/test/EFCore.Specification.Tests/ModelBuilding/ModelBuilderTest.ComplexType.cs
[efcore-complex-collection-tests]: https://github.com/dotnet/efcore/blob/v10.0.8/test/EFCore.Specification.Tests/ModelBuilding/ModelBuilderTest.ComplexCollections.cs
[efcore-model-validator]: https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/Infrastructure/ModelValidator.cs
