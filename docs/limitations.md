# External Engine and EF Core Limitations

This document is the public entry point for behavior that the provider cannot
make available because a supported database engine or the consumed EF Core
framework does not expose the required contract.

These entries are external facts, not architecture decisions. D-021 defines
how an external limitation must be proved and governed. The machine-readable
authority is
`tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/SpecDispositions.json`.
The detailed documents are its readable projection.

The contract has a zero provider-gap budget. A behavior is not listed as an
external limitation when the engine and EF Core can represent it: in that case
the provider must implement it. The word "unavailable" applies only to the
exact contract described by an entry, not to the surrounding feature family.

The supported targets are MySQL 8.4 / 9.7 and MariaDB 10.11 / 11.4 / 11.8 /
12.3. Source retrieval dates are recorded per entry. This projection was last
reconciled on 2026-08-11.

## Inventory by Responsibility

- [Database engine limitations](limitations/database-engines.md) contains
  syntax, storage, precision, spatial, indexing, schema, CTE, and temporal
  boundaries imposed by MySQL or MariaDB.
- [EF Core limitations](limitations/ef-core.md) contains query-pipeline,
  metadata, compiled-query, and complex-type boundaries imposed before
  provider translation.
- [Complex Types](complex-types.md), [Temporal Tables](temporal-tables.md), and
  [Common Table Expressions](ctes.md) describe the supported provider
  contracts around those exact boundaries.

## Clarification of the D-004 capability cleanup

D-004 removed five unconsumed internal capability flags. It did not remove
the corresponding database or provider features. `EngineProfile` deliberately
contains only version-derived facts that have an active behavior-routing
consumer. A generally supported feature does not need a runtime switch.

### Window functions

Native window functions remain supported. Both supported MySQL lines and all
supported MariaDB lines provide window-function syntax. The provider's
dynamic-offset rewrite emits EF Core's `RowNumberExpression`, and provider
tests exercise that generated `ROW_NUMBER()` shape. The removed flag had no
production consumer and controlled neither path.

The separate public boundary is the absence of a general strongly typed LINQ
API for arbitrary window expressions. Raw SQL remains available for native
window expressions; the exact boundary is documented below.

Primary sources, retrieved 2026-08-11:

- [MySQL 8.4 Window Function Descriptions][mysql-window-functions]
- [MySQL 9.7 Window Function Descriptions][mysql97-window-functions]
- [MariaDB Window Functions Overview][mariadb-window-functions]
- [dotnet/efcore window-function API epic][efcore-window-functions]

### `datetime(6)`

Fractional-second temporal mappings remain supported. MySQL and MariaDB accept
fractional-second precision from zero through six digits. The removed flag was
always true for the supported targets and had no behavior-routing consumer.
The distinct seven-digit .NET precision boundary is documented in the engine
limitations inventory below.

Primary sources, retrieved 2026-08-11:

- [MySQL 8.4 Fractional Seconds in Time Values][mysql-fractional-seconds]
- [MySQL 9.7 Fractional Seconds in Time Values][mysql97-fractional-seconds]
- [MariaDB Microseconds in MariaDB][mariadb-microseconds]

### Generated invisible primary keys

MySQL generated invisible primary keys are server automation controlled by
`sql_generate_invisible_primary_key`. They are not an EF Core translation or
provider capability boundary. The provider does not advertise this MySQL-only
server setting as a portable model-building feature. Removing the diagnostic
flag did not disable the MySQL server behavior.

Primary sources, retrieved 2026-08-11:

- [MySQL 8.4 Generated Invisible Primary Keys][mysql-gipk]
- [MySQL 9.7 Generated Invisible Primary Keys][mysql97-gipk]

### `INTERSECT` and `EXCEPT`

Relational set operations remain supported on all supported targets. MySQL 8.4
and 9.7 document `INTERSECT` and `EXCEPT`, and MariaDB has supported
`INTERSECT` since 10.3. The provider uses EF Core's relational set-operation SQL
tree; no version-routing flag is required for the supported version floor.

Primary sources, retrieved 2026-08-11:

- [MySQL 8.4 Set Operations][mysql-set-operations]
- [MySQL 9.7 Set Operations][mysql97-set-operations]
- [MariaDB INTERSECT][mariadb-intersect]
- [MariaDB EXCEPT][mariadb-except]

### Full-text indexes and search

Full-text indexes and provider full-text query translation remain supported.
Both engines expose FULLTEXT indexes. The removed flag did not gate migrations,
scaffolding, or query translation and therefore represented dead metadata.

Primary sources, retrieved 2026-08-11:

- [MySQL 8.4 Full-Text Search Functions][mysql-full-text]
- [MySQL 9.7 Full-Text Search Functions][mysql97-full-text]
- [MariaDB Full-Text Index Overview][mariadb-full-text]

## Maintenance Contract

- Every active `engine-limitation` and `framework-limitation` ledger entry
  must appear exactly once in the responsibility-specific inventory.
- Every manually maintained public boundary must identify its exact unavailable
  contract, supported targets, primary source, and source retrieval date.
- Structural `not-applicable` dispositions are excluded because they are not
  engine or EF Core limitations.
- Provider workarounds and emulations are supported behavior, not limitations.
- Provider-owned gaps are release blockers and must never be normalized as
  limitations.
- A source must be rechecked and its retrieval date updated when a supported
  engine or EF Core patch changes the affected boundary.
- A resolved external limitation is removed from the ledger, the detailed
  inventory, and the corresponding executable skip in the same change.

## Source References

[efcore-window-functions]: https://github.com/dotnet/efcore/issues/12747
[mariadb-except]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/joins-subqueries/except
[mariadb-full-text]: https://mariadb.com/docs/server/ha-and-performance/optimization-and-tuning/optimization-and-indexes/full-text-indexes/full-text-index-overview
[mariadb-intersect]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/joins-subqueries/intersect
[mariadb-microseconds]: https://mariadb.com/docs/server/reference/sql-functions/date-time-functions/microseconds-in-mariadb
[mariadb-window-functions]: https://mariadb.com/docs/server/reference/sql-functions/special-functions/window-functions/window-functions-overview
[mysql-fractional-seconds]: https://dev.mysql.com/doc/refman/8.4/en/fractional-seconds.html
[mysql-full-text]: https://dev.mysql.com/doc/refman/8.4/en/fulltext-search.html
[mysql-gipk]: https://dev.mysql.com/doc/refman/8.4/en/create-table-gipks.html
[mysql-set-operations]: https://dev.mysql.com/doc/refman/8.4/en/set-operations.html
[mysql-window-functions]: https://dev.mysql.com/doc/refman/8.4/en/window-functions.html
[mysql97-fractional-seconds]: https://dev.mysql.com/doc/refman/9.7/en/fractional-seconds.html
[mysql97-full-text]: https://dev.mysql.com/doc/refman/9.7/en/fulltext-search.html
[mysql97-gipk]: https://dev.mysql.com/doc/refman/9.7/en/create-table-gipks.html
[mysql97-set-operations]: https://dev.mysql.com/doc/refman/9.7/en/set-operations.html
[mysql97-window-functions]: https://dev.mysql.com/doc/refman/9.7/en/window-functions.html
