# EF Core Limitations

This inventory contains only contracts that EF Core 10 rejects or cannot
represent before provider translation. Engine restrictions are documented
separately in [Database Engine Limitations](database-engines.md), and the
[cross-feature index](../limitations.md) defines the governing zero-gap
contract.

## Complex-Type Contract

The provider implements the relational complex-type surface that EF Core 10
can represent for CLR-backed model shapes on MySQL 8.4 and MariaDB 11.4 / 11.8.
That includes flattened and nested complex properties, JSON document mapping,
EF Core-valid reference-type complex collections in JSON, query and update
translation, materialization, tracking, compiled models, and provider
expression quoting for precompiled `JSON_TABLE` queries. No provider-specific
model or tracking API is required.

The shape-specific EF Core limitations later in this inventory remain exact
upstream boundaries. They cover unsupported collection-tracking shapes,
affected `EntityEntry` store-value and nested database-value APIs, and shadow
complex properties. The following broader framework boundaries complete the
public complex-type contract for the current EF Core 10 line.

### Complex types with TPT or TPC mapping

- **Unavailable contract:** Combining a complex or JSON property with a TPT
  or TPC entity hierarchy on EF Core 10.
- **Available contracts:** Complex properties on non-inheritance entity types,
  and provider temporal tables on TPH, TPT, and TPC hierarchies, remain
  supported. The latter is a temporal-table contract and must not be confused
  with an EF complex property.
- **Responsibility:** EF Core 11 adds complex types and JSON columns to TPT and
  TPC mapping. EF Core 10 rejects that combination before provider SQL
  translation.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary source:** [EF Core 11 complex-type
  improvements][efcore-11-whats-new], retrieved 2026-08-05.

### Nested complex members in keys or indexes

- **Unavailable contract:** Using a nested complex member as an entity key or
  index property on EF Core 10.
- **Available contracts:** Ordinary scalar entity properties remain valid key
  and index members. Nested complex members remain queryable and persistable
  outside that metadata role.
- **Responsibility:** EF Core 11 adds key and index support for nested complex
  members; the EF Core 10 metadata model does not expose the contract to the
  provider.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary source:** [EF Core 11 complex-type
  improvements][efcore-11-whats-new], retrieved 2026-08-05.

### Struct elements in complex collections

- **Unavailable contract:** Persisting a complex collection whose element type
  is a struct on EF Core 10.
- **Available contracts:** Reference-type complex collections mapped to JSON,
  and individual struct complex properties, remain supported.
- **Responsibility:** EF Core 10 documents struct complex properties but does
  not support struct elements in complex collections. The provider cannot
  supply a collection model that the framework does not represent.
- **Targets:** MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- **Primary sources:** [EF Core 10 complex-type
  improvements][efcore-10-whats-new] and [EF Core 10 breaking
  changes][efcore-10-breaking], retrieved 2026-08-05.

See [Complex Types](../complex-types.md) for configuration examples, provider
behavior, and the full support matrix.

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

The specification ledger governs inherited EF Core conformance tests. These
framework boundaries are maintained explicitly because no inherited
disposition represents them.

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

## Feature-specific Boundaries Outside the Specification Ledger

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

## Source References

[efcore-10-breaking]: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes
[efcore-10-whats-new]: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew
[efcore-11-whats-new]: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/whatsnew
[efcore-13890]: https://github.com/dotnet/efcore/issues/13890
[efcore-15743]: https://github.com/dotnet/efcore/issues/15743
[efcore-16298]: https://github.com/dotnet/efcore/issues/16298
[efcore-18923]: https://github.com/dotnet/efcore/issues/18923
[efcore-21332]: https://github.com/dotnet/efcore/issues/21332
[efcore-24263]: https://github.com/dotnet/efcore/issues/24263
[efcore-26753]: https://github.com/dotnet/efcore/issues/26753
[efcore-27130]: https://github.com/dotnet/efcore/issues/27130
[efcore-28525]: https://github.com/dotnet/efcore/issues/28525
[efcore-28645]: https://github.com/dotnet/efcore/issues/28645
[efcore-28733]: https://github.com/dotnet/efcore/issues/28733
[efcore-29014]: https://github.com/dotnet/efcore/issues/29014
[efcore-29287]: https://github.com/dotnet/efcore/issues/29287
[efcore-29416]: https://github.com/dotnet/efcore/issues/29416
[efcore-31277]: https://github.com/dotnet/efcore/issues/31277
[efcore-31397]: https://github.com/dotnet/efcore/issues/31397
[efcore-31411]: https://github.com/dotnet/efcore/issues/31411
[efcore-31621]: https://github.com/dotnet/efcore/issues/31621
[efcore-32303]: https://github.com/dotnet/efcore/issues/32303
[efcore-32611]: https://github.com/dotnet/efcore/issues/32611
[efcore-33378]: https://github.com/dotnet/efcore/issues/33378
[efcore-35028]: https://github.com/dotnet/efcore/issues/35028
[efcore-35613]: https://github.com/dotnet/efcore/issues/35613
[efcore-36483]: https://github.com/dotnet/efcore/issues/36483
[efcore-advanced-performance]: https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics
[efcore-collations]: https://learn.microsoft.com/en-us/ef/core/miscellaneous/collations-and-case-sensitivity
[efcore-complex-collection-tests]: https://github.com/dotnet/efcore/blob/v10.0.8/test/EFCore.Specification.Tests/ModelBuilding/ModelBuilderTest.ComplexCollections.cs
[efcore-complex-tests]: https://github.com/dotnet/efcore/blob/v10.0.8/test/EFCore.Specification.Tests/ModelBuilding/ModelBuilderTest.ComplexType.cs
[efcore-execute-update]: https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Query/RelationalQueryableMethodTranslatingExpressionVisitor.ExecuteUpdate.cs
[efcore-model-validator]: https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/Infrastructure/ModelValidator.cs
[efcore-relational-query-extensions]: https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Extensions/RelationalQueryableExtensions.cs
[efcore-sql-queries]: https://learn.microsoft.com/en-us/ef/core/querying/sql-queries
[efcore-sqlserver-temporal-extensions]: https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.SqlServer/Extensions/SqlServerDbSetExtensions.cs
[efcore-tpc-tests]: https://github.com/dotnet/efcore/blob/v10.0.8/test/EFCore.Specification.Tests/BulkUpdates/InheritanceBulkUpdatesTestBase.cs
[efcore-window-functions]: https://github.com/dotnet/efcore/issues/12747
