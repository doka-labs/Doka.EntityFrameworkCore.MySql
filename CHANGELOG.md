# Changelog

All notable changes to this project will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [10.0.0-rc.4] - 2026-08-08

This release candidate supersedes `10.0.0-rc.3`, which failed hosted
qualification because the accepted performance baseline had been recorded under
an earlier evidence contract. It closes the manual handover in baseline
acceptance, so that evidence is produced, validated, and proposed by the
benchmark workflow itself rather than moved between runs by hand.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.4
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.4
```

### Fixed

- Validate hosted performance evidence before qualification begins, so a
  candidate fails on its preflight rather than after the full matrix has run.
- Produce baseline proposals from the benchmark workflow itself, with no
  artifact download, run-identifier handover, or second dispatch between
  measuring and proposing.
- Keep the fixed large-write and HiLo populations inside their deadlines by
  batching per-context inserts and preserving cancellation across the
  synchronous and asynchronous setup paths.
- Bound scorecard measurement retries to a second attempt on an independent
  runner, and carry the contract provenance of an accepted baseline so a
  rerun cannot silently rebind it to a different contract.

## [10.0.0-rc.3] - 2026-08-07

This release candidate supersedes `10.0.0-rc.2` after hosted qualification
exposed the same undersized hang deadline for the fixed 10,000-row
`SaveChanges` evidence populations.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.3
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.3
```

### Fixed

- Preserve all 128 independent scorecard observations for both 10,000-row
  `SaveChanges` workloads while assigning their fixed database work a bounded
  300-second hang deadline. Latency, allocation, GC, and historical-regression
  budgets remain unchanged.
- Cover every fixed large write population with one regression contract so a
  synchronous, asynchronous, SaveChanges, or HiLo variant cannot silently lose
  its workload-local deadline.

## [10.0.0-rc.2] - 2026-08-04

This release candidate supersedes `10.0.0-rc.1` after hosted qualification
exposed an undersized per-workload deadline for the fixed large HiLo evidence
population.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.2
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.2
```

### Fixed

- Preserve the complete large HiLo evidence population while applying bounded
  workload-local timeout floors on shared hosted runners. Sample counts,
  latency budgets, allocation budgets, GC budgets, and regression budgets
  remain unchanged.

### Documentation

- Define the canonical green-`main`, signed-tag, hosted-candidate, NuGet
  publication, public-readback, and immutable-release procedure.
- Reconcile installation, supported-engine, hosted-target, example, and
  project-layout guidance with the current provider contract.

## [10.0.0-rc.1] - 2026-08-04

First public release candidate for the `10.0.x` package line.

Install the release candidate explicitly because NuGet excludes prerelease
packages from normal stable-version resolution:

```bash
dotnet add package Doka.EntityFrameworkCore.MySql --version 10.0.0-rc.1
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.0.0-rc.1
```

### Changed (breaking)

- Default `decimal` mapping changed from `decimal(65,30)` (the MySQL maximum) to `decimal(18,2)` (the real-world common case for currency). Unannotated decimal properties now resolve to the new default; properties annotated with `[Precision(p, s)]` or `HasPrecision(p, s)` are unaffected. Existing schemata that have unannotated decimal columns wider than `(18,2)` should be audited via `SELECT MAX(ABS(x))` before the next migration runs. The `ImplicitDecimalPrecisionDefaulted` warning fires on first use per `DbContext`. See ADR D-006 for the full rationale.
- GUID stored as `char(36)` / `varchar(36)` now declares `unicode: false`, matching the ASCII-only canonical hex representation. The on-disk footprint and the network payload shrink to one byte per character. Existing schemata that declared GUID columns with utf8mb4 collation continue to read and write correctly; the migration only re-emits the type mapping.
- Server versions outside MySQL 8.4 and MariaDB 11.4 / 11.8 now require
  the explicit `MySqlServerVersionCompatibilityMode.AllowUnsupported`
  opt-in. Legacy, unvalidated, and future lines remain executable without
  a support guarantee and emit `MySqlEventId.UnsupportedServerVersion`.
- Object-bearing provider diagnostics now expose a stable 16-character
  `ObjectScopeId` instead of raw model or database object names. Invalid
  configuration events expose a bounded `Reason` value and no longer carry
  caller-provided messages or connection-string representations. Detailed
  validation errors remain available through the thrown exception.

### Added

**Core package (`Doka.EntityFrameworkCore.MySql`)**

- Entity Framework Core 10 provider for MySQL 8.4 LTS and MariaDB 11.4 / 11.8 LTS
- Three connection configuration paths: connection string, `DbConnection`, and `MySqlDataSource`
- `MySqlServerVersion` with explicit `MySql(...)` / `MariaDb(...)`
  factories, `AutoDetect(...)`, support classification, and an
  unsupported-version compatibility mode
- Separate engine-fact and provider-support contracts; provider
  capabilities resolve as native, emulated, or unavailable because of an
  engine limitation
- GUID storage format selection: `Binary16` (default) and `Char36`, both configurable via `DefaultGuidFormat(...)` and per-property `HasMySqlGuidFormat(...)`
- Value generation strategies: `AutoIncrement`, `ClientGuid`, and `HiLo` via `UseHiLo(...)`
- Native MariaDB sequences (10.3+) plus table-based sequence emulation for MySQL
- Advisory-lock-backed migration serialization via `GET_LOCK` / `RELEASE_LOCK` on a dedicated non-pooled connection
- Idempotent migration scripting via `DROP PROCEDURE` / `CREATE PROCEDURE` stored-procedure wrappers (`dotnet ef migrations script --idempotent`)
- JSON pipeline: native JSON on MySQL, `longtext COLLATE utf8mb4_bin CHECK (JSON_VALID(...))` alias on MariaDB, with scaffolding detection
- JSON CLR-type preservation: `JsonElement`, `JsonDocument`, `JsonNode`, `JsonObject`, `JsonArray` with embedded `ValueConverter` and deep-equality `ValueComparer`
- Query translation coverage for common string, DateTime, DateOnly, TimeOnly, Math, and aggregate (`string.Join` -> `GROUP_CONCAT ... SEPARATOR`) operations
- `EF.Functions` extensions: `Regexp`, `Match`, `MatchInBooleanMode`, `JsonSet`, `JsonReplace`, `JsonRemove`, `JsonArray`, `JsonObject`, `JsonDepth`, `JsonLength`, `JsonType`, `JsonKeys`, `JsonContains`
- Engine-aware REGEXP dialect (`REGEXP_LIKE(...)` on MySQL, infix `REGEXP` on MariaDB)
- Full-text search via `MATCH(col) AGAINST(term [IN BOOLEAN MODE])` with sentinel-rewrite SQL generation
- MariaDB `INVISIBLE` column support (10.3.3+) via `IsInvisible()` fluent API
- SQL-generation hardening: shared ASCII grammar-token validation for
  charsets, storage engines, and query, table, and column collations;
  JSON-path property-name escaping for single quotes and backslashes
- Transient-exception detection with depth-limited inner-exception traversal for retrying execution strategies
- Stable `MySqlEventId` catalog and seven logger categories (`Configuration`, `Query`, `Update`, `Migrations`, `Scaffolding`, `Resilience`, `Spatial`)
- Trim-aware runtime surface; NativeAOT readiness deferred until upstream EF Core stabilizes the precompiled-query workflow (see ADR D-017)

**Optional spatial package (`Doka.EntityFrameworkCore.MySql.NetTopologySuite`)**

- `UseNetTopologySuite()` opt-in activation for NTS-backed spatial types
- Geometry-first type mapping for `Point`, `LineString`, `Polygon`, `MultiPoint`, `MultiLineString`, `MultiPolygon`, `GeometryCollection`, and `Geometry`
- Spatial index DDL generation (`CREATE SPATIAL INDEX`) with model-validator rejection of unique, multi-column, or non-NTS spatial indexes
- SRID-aware scaffolding and design-time warnings for unsupported spatial configurations

### Tested

- 668 unit tests and 463 provider-local functional tests
- Upstream specification contracts covering 29,746 MySQL 8.4,
  29,410 MariaDB 11.4, and 29,411 MariaDB 11.8 test cases
- 171 discovered live integration cases: 166 supported-matrix cases and five
  explicit skips reserved for the external-only MySQL 8.0 baseline
- Live integration coverage against MySQL 8.4 LTS, MariaDB 11.4 LTS, and
  MariaDB 11.8 LTS, plus an external-only opt-in MySQL 8.0 compatibility
  baseline
- Representative dual-engine benchmark smoke and scorecard runs

[Unreleased]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/compare/v10.0.0-rc.4...HEAD
[10.0.0-rc.4]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.4
[10.0.0-rc.3]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.3
[10.0.0-rc.2]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.2
[10.0.0-rc.1]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases/tag/v10.0.0-rc.1
