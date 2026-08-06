# Database Engine Limitations

This inventory contains only boundaries imposed by MySQL 8.4 or MariaDB 11.4
and 11.8. A provider workaround or emulation is documented as supported
behavior instead of an engine limitation. The
[cross-feature index](../limitations.md) defines the governing zero-gap
contract.

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

## Public Boundaries Outside the Specification Ledger

The specification ledger governs inherited EF Core conformance tests. These
engine boundaries are maintained explicitly because no inherited disposition
represents them.

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

See [Temporal Tables](../temporal-tables.md) and
[Common Table Expressions](../ctes.md) for the complete supported contracts,
executable examples, and migration guidance.

## Source References

[mariadb-alter-table]: https://mariadb.com/docs/server/reference/sql-statements/data-definition/alter/alter-table
[mariadb-buffer]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/geometry-constructors/st_buffer
[mariadb-collect]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/miscellaneous-gis-functions/st_collect
[mariadb-create-database]: https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-database
[mariadb-create-function]: https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-function
[mariadb-create-index]: https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-index
[mariadb-cte]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/with
[mariadb-foreign-keys]: https://mariadb.com/docs/server/ha-and-performance/optimization-and-tuning/optimization-and-indexes/foreign-keys
[mariadb-geometry-columns]: https://mariadb.com/docs/server/reference/system-tables/information-schema/information-schema-tables/information-schema-geometry_columns-table
[mariadb-geometry-constructors]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors
[mariadb-is-valid]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/miscellaneous-gis-functions/st_isvalid
[mariadb-join-syntax]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/joins/join-syntax
[mariadb-json]: https://mariadb.com/docs/server/reference/data-types/string-data-types/json
[mariadb-json-table]: https://mariadb.com/docs/server/reference/sql-functions/special-functions/json-functions/json_table
[mariadb-microseconds]: https://mariadb.com/docs/server/reference/sql-functions/date-time-functions/microseconds-in-mariadb
[mariadb-set-statement]: https://mariadb.com/docs/server/reference/sql-statements/administrative-sql-statements/set-commands/set-statement
[mariadb-stored-function-overview]: https://mariadb.com/docs/server/server-usage/stored-routines/stored-functions/stored-function-overview
[mariadb-subquery-limitations]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/subqueries/subquery-limitations
[mariadb-system-versioned-tables]: https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/system-versioned-tables
[mariadb-update]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/changing-deleting-data/update
[mysql-create-index]: https://dev.mysql.com/doc/refman/8.4/en/create-index.html
[mysql-foreign-keys]: https://dev.mysql.com/doc/refman/8.4/en/create-table-foreign-keys.html
[mysql-fractional-seconds]: https://dev.mysql.com/doc/refman/8.4/en/fractional-seconds.html
[mysql-geometry]: https://dev.mysql.com/doc/refman/8.4/en/gis-class-geometry.html
[mysql-json]: https://dev.mysql.com/doc/refman/8.4/en/json.html
[mysql-schema-database]: https://dev.mysql.com/doc/refman/8.4/en/create-database.html
[mysql-spatial-arguments]: https://dev.mysql.com/doc/refman/8.4/en/spatial-function-argument-handling.html
[mysql-spatial-functions]: https://dev.mysql.com/doc/refman/8.4/en/spatial-function-reference.html
[mysql-stored-program-restrictions]: https://dev.mysql.com/doc/refman/8.4/en/stored-program-restrictions.html
[mysql-stored-routines]: https://dev.mysql.com/doc/refman/8.4/en/stored-routines-syntax.html
[mysql-trigger-syntax]: https://dev.mysql.com/doc/refman/8.4/en/trigger-syntax.html
