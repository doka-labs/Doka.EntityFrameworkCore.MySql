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
- **MariaDB byte[]-WKB read path** is handled by extending `MySqlNetTopologySuiteGeometryTypeMapping.CustomizeDataReaderExpression` to pattern-match the standard `reader.GetFieldValue<T>(ordinal)` shape and route through a new `ReadSpatialColumn` dispatcher. The dispatcher inspects the actual runtime value: `MySqlGeometry` follows the existing MySQL conversion path; raw `byte[]` is the MariaDB path. Two WKB layouts are accepted on the MariaDB side: canonical OGC WKB (byte-order indicator at index 0) and MySQL-style SRID-prefixed WKB (4-byte little-endian SRID then byte-order indicator at index 4). The extracted SRID lands on the materialized geometry; canonical OGC WKB leaves the SRID at 0 because the format does not embed one. Keeping `MySqlGeometry` as the value-converter provider type preserves the write path on both engines (MariaDB rejects raw `byte[]` WKB on inline parameter binding because it parses the bytes as text).
- **MariaDB spatial integration tests** in `MySqlNetTopologySuiteIntegrationTests`: `MariaDb118_wkb_roundtrip_preserves_srid_and_coordinates` writes a Point, clears the change tracker, and asserts the materialized round-trip preserves SRID + coordinates; `MariaDb118_spatial_index_ddl_creates_index_on_geometry_column` emits the `CREATE SPATIAL INDEX` form via the migration generator, runs it against the live MariaDB server, then queries `information_schema.statistics` to confirm the index landed. `TranslateDistance_warns_when_column_and_constant_srids_differ` and `TranslateDistance_does_not_warn_when_column_and_constant_srids_match` pin the SRID-warning contract for the realistic column-vs-constant query shape.

### Engine support matrix

| Feature | MySQL 8.4 LTS | MariaDB 11.4 LTS | MariaDB 11.8 LTS |
|---|---|---|---|
| `ST_Distance` SRID-strict check | yes (hard error on mismatch) | no (silent Cartesian) | no (silent Cartesian) |
| WKB SRID-header prefix on read | yes (4-byte LE prefix) | no (canonical OGC) | no (canonical OGC) |
| Spatial index on NULLABLE column | rejected | accepted (functionally ignores NULLs) | accepted (functionally ignores NULLs) |
| `ST_Buffer` quadrant-segment count default | 8 | 32 | 32 |
| Geography (`GEOGRAPHY`) type | no (Geometry-only) | no (Geometry-only) | no (Geometry-only) |

### Additional Alternative Rationale

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

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/Internal/Storage/MySqlNetTopologySuiteGeometryTypeMapping.cs`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Spatial/MySqlNetTopologySuiteIntegrationTests.cs`

### Sources

- No external sources; repository evidence only.
