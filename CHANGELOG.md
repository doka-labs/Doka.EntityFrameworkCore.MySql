# Changelog

All notable changes to this project will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Initial release line preparing the first publishable `10.0.x` package.

### Changed (breaking)

- Default `decimal` mapping changed from `decimal(65,30)` (the MySQL maximum) to `decimal(18,2)` (the real-world common case for currency). Unannotated decimal properties now resolve to the new default; properties annotated with `[Precision(p, s)]` or `HasPrecision(p, s)` are unaffected. Existing schemata that have unannotated decimal columns wider than `(18,2)` should be audited via `SELECT MAX(ABS(x))` before the next migration runs. The `ImplicitDecimalPrecisionDefaulted` warning fires on first use per `DbContext`. See ADR D-006 for the full rationale.
- GUID stored as `char(36)` / `varchar(36)` now declares `unicode: false`, matching the ASCII-only canonical hex representation. The on-disk footprint and the network payload shrink to one byte per character. Existing schemata that declared GUID columns with utf8mb4 collation continue to read and write correctly; the migration only re-emits the type mapping.

### Added

**Core package (`Doka.EntityFrameworkCore.MySql`)**

- Entity Framework Core 10 provider for MySQL 8.4 LTS and MariaDB 11.4 / 11.8 LTS
- Three connection configuration paths: connection string, `DbConnection`, and `MySqlDataSource`
- `MySqlServerVersion` with explicit `MySql(...)` / `MariaDb(...)` factories and `AutoDetect(...)` helper
- Capability-driven engine differences via the internal `ServerCapabilities` model (single source of truth for MySQL vs. MariaDB behavior)
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
- DDL hardening: `CharSet` and `StorageEngine` identifier validation to prevent injection via model metadata; JSON-path property-name escaping for single quotes and backslashes
- Transient-exception detection with depth-limited inner-exception traversal for retrying execution strategies
- Connection-string redaction for safe diagnostic logging
- Stable `MySqlEventId` catalog and seven logger categories (`Configuration`, `Query`, `Update`, `Migrations`, `Scaffolding`, `Resilience`, `Spatial`)
- Trim- and AOT-aware runtime surface

**Optional spatial package (`Doka.EntityFrameworkCore.MySql.NetTopologySuite`)**

- `UseNetTopologySuite()` opt-in activation for NTS-backed spatial types
- Geometry-first type mapping for `Point`, `LineString`, `Polygon`, `MultiPoint`, `MultiLineString`, `MultiPolygon`, `GeometryCollection`, and `Geometry`
- Spatial index DDL generation (`CREATE SPATIAL INDEX`) with model-validator rejection of unique, multi-column, or non-NTS spatial indexes
- SRID-aware scaffolding and design-time warnings for unsupported spatial configurations

### Tested

- 146 unit tests, 275 functional tests, 81 live integration tests
- Live integration coverage against Dockerized MySQL 8.4 LTS, MariaDB 11.4 LTS, and MariaDB 11.8 LTS
- Representative dual-engine benchmark smoke and scorecard runs

[Unreleased]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/compare/HEAD
