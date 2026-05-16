# D-012 -- NetTopologySuite Spatial-Engine-Detail

- **Status:** Accepted
- **Date:** 2026-05-16
- **Scope:** `src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/`
- **Implementation:** deferred to a follow-up commit

## Context

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

## Decision

Three structural improvements:

1. **SRID-mismatch warning.** A new logger event
   `SpatialSridMismatchDetected` fires inside
   `MySqlNetTopologySuiteMethodCallTranslator.TranslateDistance` when
   the translator can statically observe that the two operand SRIDs
   differ. The warning surfaces at `Warning` level with the
   participating SRIDs, so the consumer sees the silent-Cartesian
   risk before it produces a wrong result.
2. **MariaDB spatial integration tests.** A new test class
   `MariaDbSpatialIntegrationTests` covers the WKB round-trip
   (`Geometry` -> bytes -> `Geometry` identity check), `ST_Distance`
   against a deliberate SRID-matched pair and against a mismatched
   pair (asserting the warning fires), and spatial-index DDL emission
   plus runtime usage. The tests run against the MariaDB 11.x LTS
   targets in the integration matrix.
3. **Engine-difference documentation.** A "Support matrix" table in
   this ADR enumerates the engine-by-feature deltas so consumers can
   plan around them without reading the provider source.

## Engine support matrix

| Feature | MySQL 8.4 LTS | MariaDB 11.4 LTS | MariaDB 11.8 LTS |
|---|---|---|---|
| `ST_Distance` SRID-strict check | yes (hard error on mismatch) | no (silent Cartesian) | no (silent Cartesian) |
| WKB SRID-header prefix on read | yes (4-byte LE prefix) | no (canonical OGC) | no (canonical OGC) |
| Spatial index on NULLABLE column | rejected | accepted (functionally ignores NULLs) | accepted (functionally ignores NULLs) |
| `ST_Buffer` quadrant-segment count default | 8 | 32 | 32 |
| Geography (`GEOGRAPHY`) type | no (Geometry-only) | no (Geometry-only) | no (Geometry-only) |

## Consequences

### Positive

- The SRID-mismatch warning eliminates the single largest silent-
  wrong-result risk on MariaDB spatial queries.
- MariaDB spatial pathways get integration coverage, closing the
  symmetry gap with MySQL.
- Consumers planning cross-engine spatial deployments have a documented
  delta they can route around explicitly.

### Negative

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

### Neutral

- The public spatial-mapping surface (`UseNetTopologySuite(...)`,
  `HasSpatialIndex(...)`) is unchanged.

## Re-evaluation triggers

- MariaDB introduces SRID-strict checking in a future release; the
  silent-Cartesian path becomes a hard error, the warning becomes
  redundant, and the documented delta closes.
- MySQL or MariaDB introduces a true `GEOGRAPHY` type; the matrix
  gains a new feature row.
- An operator report documents a wrong-result scenario the warning
  did not catch; the static-detection scope expands toward runtime
  observation.

## Alternatives considered

- **Status quo (MySQL-only test coverage).** Rejected: MariaDB
  spatial pathway is undocumented and untested; wrong-result risk
  documented above.
- **Mark MariaDB spatial as unsupported.** Rejected: MariaDB 11.4 LTS
  and 11.8 LTS both support spatial fully. Dropping the support
  would be a functional regression for a real subset of consumers.
- **Translate SRID-mismatch into a hard error on both engines.**
  Rejected: would break existing MariaDB consumers who rely on the
  silent-Cartesian behavior. The warning gives them a migration
  signal without forcing the break.
