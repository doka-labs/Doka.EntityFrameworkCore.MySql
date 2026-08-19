---
id: D-012
status: implemented
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "NetTopologySuite spatial translation and materialization"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-012 -- Adapt spatial materialization per engine

## Context and Problem Statement

The optional NetTopologySuite package supports both MySQL and MariaDB,
but the engine differences in spatial semantics are non-trivial and
the current implementation under-documents them:

1. **`ST_Distance` SRID semantics.** On MySQL 8.0+,
   `ST_Distance(g1, g2)` requires both geometries to declare the same
   SRID; mismatched SRIDs produce a hard error. On MariaDB the same
   call accepts the mismatch and silently treats both inputs as
   Cartesian. The provider does not warn on mismatch today; the
   silent-Cartesian path on MariaDB is a wrong-result risk that the
   premortem flagged as MAJOR.
2. **WKB binary-layout differences.** MySQL prefixes the well-known
   binary representation with a four-byte SRID header (little-endian),
   while MariaDB ships the canonical OGC WKB shape without the
   prefix. The current `MySqlGeometryTypeMapping` round-trip handles
   the MySQL form correctly; the MariaDB form has no integration-test
   coverage today, and the prefix-stripping branch is keyed off an
   `isMariaDb` boolean rather than a probe of the actual byte layout.
3. **Spatial-index DDL form.** The two engines differ in how spatial
   indexes interact with `NOT NULL` constraints on the indexed column.
   The provider currently emits a single form that happens to work on
   both engines; the test coverage for the negative case (NULLABLE
   spatial column) is absent.
4. **Function availability and relation semantics.** MariaDB exposes
   `ST_IsValid` and `ST_Collect` only from 12.0, accepts only the
   two-argument `ST_Buffer` form, and documents nullable `ST_Crosses`
   outcomes for argument orders where NetTopologySuite requires a Boolean.
5. **Column SRID enforcement.** MySQL has a native spatial-column SRID
   attribute. MariaDB accepts the same-looking syntax without enforcing it,
   while an enforced column `CHECK (ST_SRID(column) = value)` preserves the
   model contract and remains visible in `CHECK_CONSTRAINTS`.

## Decision Drivers

- MySQL and MariaDB return different spatial provider values.
- SRID and coordinate semantics must survive round trips.
- Engine differences should remain inside the spatial package.

## Considered Options

- Engine-aware spatial read adapter
- One common MySqlGeometry-only path
- Disable MariaDB spatial support

## Decision Outcome

Chosen option: "Engine-aware spatial read adapter", because engine-aware normalization preserves one EF spatial contract across both families.

Three structural improvements:

1. **SRID-mismatch warning.** A new logger event
   `SpatialSridMismatchDetected` fires inside
   `MySqlNetTopologySuiteMethodCallTranslator.TranslateDistance` when
   the translator can statically observe that the two operand SRIDs
   differ. The warning surfaces at `Warning` level with the
   participating SRIDs, so the consumer sees the silent-Cartesian
   risk before it produces a wrong result.
2. **Cross-engine spatial integration tests.** Live contracts cover every
   supported geometry CLR type through tracked, no-tracking, scalar, Include,
   and split-query materialization. MariaDB Crosses runs on every active
   MariaDB LTS target, and function-capability probes run on both MySQL lines
   plus MariaDB 12.3.
3. **Engine-difference documentation.** A "Support matrix" table in
   this ADR enumerates the engine-by-feature deltas so consumers can
   plan around them without reading the provider source.

### Consequences

- Good, because supported spatial values round-trip with SRID and coordinate fidelity.
- Bad, because the adapter must recognize multiple binary layouts and reject unknown ones safely.

#### Positive

- The SRID-mismatch warning eliminates the single largest silent-
  wrong-result risk on MariaDB spatial queries.
- MariaDB spatial pathways get integration coverage, closing the
  symmetry gap with MySQL.
- Consumers planning cross-engine spatial deployments have a documented
  delta they can route around explicitly.

#### Negative

- The warning only fires when the translator can statically observe
  the SRIDs; queries that compute SRIDs at runtime (a join through a
  GIS metadata table, for example) escape the static check. The
  provider documents this caveat next to the warning's reference.
- MariaDB integration tests add a second engine to the spatial test
  matrix, increasing CI duration. The cost is bounded by the size of
  the spatial-test set, but it is non-zero.
- The WKB-layout difference becomes more visible to consumers reading
  the source; the conditional branch grows a documented WHY comment
  citing this ADR.

#### Neutral

- The public spatial-mapping surface (`UseNetTopologySuite(...)`,
  `HasSpatialIndex(...)`) is unchanged.

### Confirmation

- Run NetTopologySuite unit tests and live spatial tests on MySQL and MariaDB.
- Verify SRID mismatch diagnostics on translated queries.

## Pros and Cons of the Options

### Engine-aware spatial read adapter

- Good, because one type mapping can normalize observed MySQL and MariaDB value shapes.
- Bad, because runtime dispatch and WKB layout handling require focused tests.

### One common MySqlGeometry-only path

- Good, because the converter remains simple.
- Bad, because MariaDB byte-array results cannot materialize through that assumption.

### Disable MariaDB spatial support

- Good, because the provider avoids cross-engine WKB differences.
- Bad, because the provider would become the limiting factor for supported engine functionality.

## More Information

### Implementation Snapshot

- SRID-mismatch warning, the MariaDB byte-array WKB read path, and MariaDB spatial integration tests are implemented.

### Implementation Notes

- **SRID-mismatch warning** (`MySqlEventId.SpatialSridMismatchDetected` = 1603) fires inside `MySqlNetTopologySuiteMethodCallTranslator.TranslateDistance` when the translator can statically observe that the two operand SRIDs differ. SRID resolution walks both the `SqlConstantExpression` side (reads the literal `Geometry.SRID` value) and the `ColumnExpression` side (walks `Column` (`IColumnBase`) -> `PropertyMappings` -> `IProperty` and reads the `MySqlAnnotationNames.SpatialReferenceSystemId` annotation set by `HasSrid`). Column-vs-constant detection therefore lights up automatically as soon as the consumer declares the column SRID via the `HasSrid` fluent extension. Queries that compute SRIDs at runtime (a join through a GIS metadata table, for example) still escape the static check; the warning is a best-effort safety net, not a guarantee, and that scope is intentional.
- **Driver-shape spatial reads** use `DbDataReader.GetValue` as the common
  boundary before EF Core buffers a row. The dispatcher accepts
  `MySqlGeometry` and raw `byte[]`, including split-query buffering, and rejects
  unknown types and geometry-family mismatches explicitly.
- **Spatial functions** are version-gated by four independent capabilities.
  MariaDB Crosses is composed from `ST_Dimension` and the NetTopologySuite
  DE-9IM masks instead of materializing MariaDB's documented `NULL` results.
  The emulation preserves SQL `NULL` when either operand is `NULL` and returns
  `false` only for non-null dimension pairs that NetTopologySuite defines as
  unable to cross. This retains relational null semantics in nullable
  projections while preserving the complete NetTopologySuite dimension table.
- **MariaDB SRID enforcement** emits a column CHECK. Reverse engineering
  recognizes only the exact provider-owned expression and consumes it before
  ordinary check-constraint scaffolding, so generated models recover
  `HasSrid(...)` without duplicating `HasCheckConstraint(...)`.

### Engine support matrix

| Feature | MySQL 8.4 / 9.7 LTS | MariaDB 10.11 / 11.4 / 11.8 / 12.3 LTS |
|---|---|---|
| `ST_Distance` SRID-strict check | yes (hard error on mismatch) | no (silent Cartesian) |
| Runtime spatial value | `MySqlGeometry` or bytes | raw bytes |
| Column-level SRID enforcement | native SRID attribute | provider `CHECK (ST_SRID(column) = value)` |
| Spatial index on NULLABLE column | rejected | accepted (functionally ignores NULLs) |
| `ST_Buffer` quadrant-segment count default | 8 | 32 |
| `Buffer(distance, quadrantSegments)` | native strategy argument | unsupported; rejected during translation |
| `ST_IsValid` and `ST_Collect` | supported | supported from 12.0 |
| NetTopologySuite `Crosses` | native `ST_Crosses` | dimension-selected `ST_Relate` masks |
| Geography (`GEOGRAPHY`) type | no (Geometry-only) | no (Geometry-only) |

### Additional Alternative Rationale

- **Status quo (MySQL-only test coverage).** Rejected: MariaDB
  spatial pathway is undocumented and untested; wrong-result risk
  documented above.
- **Mark MariaDB spatial as unsupported.** Rejected: every active MariaDB LTS
  line supports the core geometry contract. Exact function differences are
  safer as executable capabilities than as a family-wide claim.
- **Translate SRID-mismatch into a hard error on both engines.**
  Rejected: would break existing MariaDB consumers who rely on the
  silent-Cartesian behavior. The warning gives them a migration
  signal without forcing the break.

### Re-evaluation Triggers

- MariaDB introduces SRID-strict checking in a future release; the
  silent-Cartesian path becomes a hard error, the warning becomes
  redundant, and the documented delta closes.
- MySQL or MariaDB introduces a true `GEOGRAPHY` type; the matrix
  gains a new feature row.
- An operator report documents a wrong-result scenario the warning
  did not catch; the static-detection scope expands toward runtime
  observation.
- MySqlConnector unifies the runtime spatial provider value across engines.
- A supported engine changes its WKB or SRID transport layout.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.
- 2026-08-11: Reconciled the engine matrix with all six active LTS targets;
  MariaDB 12.3 capability additions remain governed by executable
  dispositions rather than a family-wide assumption.
- 2026-08-18: Added driver-shape-safe buffered materialization, exact spatial
  function capabilities, MariaDB Crosses DE-9IM semantics, and enforced
  MariaDB SRID CHECK round trips.
- 2026-08-19: Corrected MariaDB Crosses null propagation before the
  dimension-based DE-9IM dispatch and bound both nullable operands on every
  supported MariaDB LTS line.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/Internal/MySqlNetTopologySuiteGeometryTypeMapping.cs`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Spatial/MySqlNetTopologySuiteIntegrationTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Spatial/MySqlNetTopologySuiteContractIntegrationTests.cs`

### Sources

- [MariaDB ST_Buffer](https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/geometry-constructors/st_buffer)
  (primary source; retrieved 2026-08-18)
- [MariaDB ST_Collect](https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/miscellaneous-gis-functions/st_collect)
  (primary source; retrieved 2026-08-18)
- [MariaDB ST_IsValid](https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/miscellaneous-gis-functions/st_isvalid)
  (primary source; retrieved 2026-08-18)
- [MariaDB Crosses](https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/geometry-relations/crosses)
  (primary source; retrieved 2026-08-18)
- [MariaDB constraints](https://mariadb.com/docs/server/reference/sql-statements/data-definition/constraint)
  (primary source; retrieved 2026-08-18)
- [MySQL 8.0.24 release notes](https://dev.mysql.com/doc/relnotes/mysql/8.0/en/news-8-0-24.html)
  (primary source; retrieved 2026-08-18)
- [MySQL 5.7.6 release notes](https://dev.mysql.com/doc/relnotes/mysql/5.7/en/news-5-7-6.html)
  (primary source; retrieved 2026-08-18)
- [MySQL 5.7.7 release notes](https://dev.mysql.com/doc/relnotes/mysql/5.7/en/news-5-7-7.html)
  (primary source; retrieved 2026-08-18)
- [NetTopologySuite 2.6.0 `Geometry.Crosses` source](https://github.com/NetTopologySuite/NetTopologySuite/blob/v2.6.0/src/NetTopologySuite/Geometries/Geometry.cs)
  (primary source; retrieved 2026-08-18)
- [EF Core 10.0.8 `SpatialQueryTestBase.Crosses` source](https://github.com/dotnet/efcore/blob/v10.0.8/test/EFCore.Specification.Tests/Query/SpatialQueryTestBase.cs#L289-L300)
  (primary source; retrieved 2026-08-19)
