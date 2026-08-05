---
id: D-004
status: implemented
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Engine capability modeling and version routing"
supersedes: []
superseded-by: []
amends: []
amended-by: [D-024]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-004 -- Route engine behavior through EngineProfile

## Context and Problem Statement

`ServerCapabilities` is currently a flat record of 16 boolean fields
(`SupportsDateTime6`, `SupportsSavepoints`, `SupportsReturningClause`, ...).
The shape scales poorly along two axes:

1. **Per-engine drift.** Every new MySQL or MariaDB release that introduces a
   distinct behavior either requires a new boolean field plus three branches
   in `ServerCapabilities.Create(...)`, or it gets coerced into one of the
   existing fields and the engine routing becomes lossy.
2. **Dead-knob accumulation.** Several capabilities are declared as `true`
   for both engines without an actual consumer in the provider
   (`SupportsDateTime6`, `SupportsSavepoints`). `SupportsReturningClause` is
   declared but unread. The dead knobs disguise which capabilities actually
   gate engine-specific behavior.

The capability surface also has a second axis the current record cannot
represent: syntax-level differences (REGEXP infix vs. `REGEXP_LIKE(...)`,
ALTER-TABLE forms, JSON-path quoting rules) that are not binary "supports yes
or no" but "uses syntax form X". Today these are scattered across translator
classes as ad-hoc `isMariaDb` branches.

## Decision Drivers

- Engine differences need one typed source of truth.
- Every declared capability must have a production consumer.
- New engine versions should extend data rather than scatter branches.

## Considered Options

- Typed EngineProfile table
- Flat boolean ServerCapabilities record
- Version checks at each consumer

## Decision Outcome

Chosen option: "Typed EngineProfile table", because a typed table gives engine behavior one maintainable and testable routing boundary.

Introduce an `EngineProfile` record that contains only version-derived engine
facts and dialect traits:

```csharp
internal sealed record EngineProfile(
    EngineFamily Family,
    Version Version,
    FrozenSet<EngineCapability> Capabilities);
```

Derive provider availability through a separate `ProviderProfile` contract:

```csharp
internal enum ProviderSupportStatus
{
    Native,
    Emulated,
    UnsupportedByEngine,
}
```

Every `ProviderCapability` maps exhaustively from engine facts to one of these
three states. There is deliberately no `UnsupportedByProvider` state: a
declared provider capability either has an implementation route or is absent
from the contract. `EngineProfileTable` accumulates facts from version
thresholds and caches the frozen profile per `(family, version)`.

### Consequences

- Good, because capability additions are reviewable data changes with explicit consumers.
- Bad, because incorrect table entries can affect multiple provider subsystems at once.

#### Positive

- Adding support for a new MySQL or MariaDB release becomes a table-append
  operation; consumers do not need to change.
- Dead knobs disappear naturally: a capability without a consumer is simply
  absent from the profile, not declared as `true` everywhere.
- Syntax-variant routing moves out of ad-hoc `isMariaDb` branches and into
  named `EngineCapability` facts.
- Provider emulation is visible instead of being mistaken for missing engine
  or provider support.

#### Negative

- The migration from `ServerCapabilities` to `EngineProfile` touches every
  call site that currently reads a capability field. The change is mechanical
  but wide.
- An engine-fact check costs one `FrozenSet` lookup. Profiles are built once
  per family/version and reused through the cache.

#### Neutral

- The model is internal; consumers continue to interact with engine
  differences through fluent-API surface (`UseMySql(...)`,
  `MySqlServerVersion.MariaDb(...)`).

### Confirmation

- Run engine-profile and declared-and-consumed contract tests.
- Run the complete supported-engine matrix after capability changes.

## Pros and Cons of the Options

### Typed EngineProfile table

- Good, because it centralizes version thresholds and makes capability consumers explicit.
- Bad, because the table must be maintained whenever an engine contract changes.

### Flat boolean ServerCapabilities record

- Good, because call sites can read simple properties.
- Bad, because the record accumulates dead flags and obscures version provenance.

### Version checks at each consumer

- Good, because each component owns its local engine rule.
- Bad, because thresholds duplicate and drift across the provider.

## More Information

### Implementation Snapshot

- `EngineCapability`, `EngineFamily`, `EngineProfile`, and
  `EngineProfileTable` own version-derived engine facts.
- `ProviderCapability`, `ProviderSupportStatus`, and `ProviderProfile` own
  provider availability and native/emulated routing.
- Query, migration, update, transaction, scaffolding, and NetTopologySuite
  behavior reads these contracts instead of `IsMariaDb` or `EngineFamily`.
- Source conformance tests require an active behavior consumer for every
  provider capability and a consumer or provider mapping for every engine
  capability.

### Implementation Notes

- `EngineProfileTable.Resolve(family, version)` accumulates capabilities from
  the exact version thresholds that affect provider behavior.
- MariaDB generated columns are modeled from their 5.2 introduction. Versions
  before 10.2.1 route stored columns through the engine's `PERSISTENT` keyword;
  later releases use the MySQL-compatible `STORED` alias.
- The MariaDB JSON-column emulation requires `JSON_VALID` and therefore starts
  at 10.2.3. Earlier releases fail explicitly instead of receiving invalid DDL.
- Diagnostic-only flags for window functions, DateTime6, generated invisible
  primary keys, INTERSECT/EXCEPT, and full-text indexes were removed. D-024
  reintroduces CTE and system-versioning capabilities only where the provider
  has active production consumers for version routing.
- `Savepoints` remains because `MySqlRelationalTransaction` actively consumes
  the corresponding provider capability.
- `IMySqlTransientExceptionDetector.ShouldRetryOn` lost its unused `ServerCapabilities` parameter; the detector never branched on capabilities and the parameter was already dead.
- `EngineProfileTable.s_cache` is a `ConcurrentDictionary<(EngineFamily, Version), EngineProfile>` so two `MySqlServerVersion.MySql(8.4.0)` calls return the same `EngineProfile` reference. Without the cache, every fresh `MySqlServerVersion` instance produced a fresh `FrozenSet<Capability>` (reference equality only) and EF Core's internal service-provider cache invalidated on every test-DbContext build -- the `ManyServiceProvidersCreatedWarning` escalated to an error in suites that build many contexts per run.

### Additional Alternative Rationale

- **Status quo (flat boolean record).** Rejected: drift accelerates with
  every new engine version; dead knobs already obscure the real routing.
- **Hexagonal architecture with per-capability interfaces.** Rejected:
  overkill for 16 boolean knobs plus a handful of syntax variants.
  the engine/provider profile pair delivers the required routing with fewer
  moving parts.
- **External configuration file (JSON/YAML) for the profile table.**
  Rejected: the table is small, change-controlled with the source, and
  benefits from compile-time enum exhaustiveness.

### Re-evaluation Triggers

- A MySQL or MariaDB release introduces an engine behavior that the
  `FrozenSet<EngineCapability>` plus provider-support status cannot express.
- A MySQL-protocol fork ships with version strings that the threshold resolver
  cannot classify correctly; engine-family detection would need to grow a
  fork-identification probe.
- A future EF Core release moves capability-detection into the core
  abstractions; the provider profile would then need to align with the
  upstream shape rather than maintain its own.
- A supported engine family or major version requires a new behavior boundary.
- Runtime probing becomes necessary for capabilities that version data cannot determine.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.
- 2026-07-30: Separated engine facts from provider support and removed unconsumed capability metadata.
- 2026-08-04: D-024 amended the decision with consumed CTE and temporal
  capabilities backed by explicit engine-version boundaries.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Capabilities/EngineProfile.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Capabilities/EngineProfileTable.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Capabilities/ProviderCapability.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Capabilities/ProviderProfile.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Contracts/ArchitectureConformanceTests.cs`

### Sources

- [MySQL 5.7.6 generated-column release notes][mysql-generated-columns]
  (primary source; retrieved 2026-07-30)
- [MySQL 5.7 native JSON documentation][mysql-native-json]
  (primary source; retrieved 2026-07-30)
- [MySQL 8.0.3 spatial and RENAME COLUMN release notes][mysql-803]
  (primary source; retrieved 2026-07-30)
- [MySQL 8.0.4 regular-expression release notes][mysql-804]
  (primary source; retrieved 2026-07-30)
- [MySQL 8.0.13 functional-index release notes][mysql-8013]
  (primary source; retrieved 2026-07-30)
- [MySQL lateral-derived-table documentation][mysql-lateral]
  (primary source; retrieved 2026-07-30)
- [MariaDB JSON data-type documentation][mariadb-json]
  (primary source; retrieved 2026-07-30)
- [MariaDB 5.2 generated-column release notes][mariadb-generated-columns]
  (primary source; retrieved 2026-07-30)
- [MariaDB generated-column syntax documentation][mariadb-generated-column-syntax]
  (primary source; retrieved 2026-07-30)
- [MariaDB 10.2.0 parser without the `STORED` alias][mariadb-1020-parser]
  (primary source; retrieved 2026-07-30)
- [MariaDB 10.2.1 parser with the `STORED` alias][mariadb-1021-parser]
  (primary source; retrieved 2026-07-30)
- [MariaDB 10.2.2 SQL source tree without JSON functions][mariadb-1022-sql-tree]
  (primary source; retrieved 2026-07-30)
- [MariaDB 10.2.3 `JSON_VALID` implementation][mariadb-1023-json-valid]
  (primary source; retrieved 2026-07-30)
- [MariaDB 10.3 sequence overview][mariadb-sequences]
  (primary source; retrieved 2026-07-30)
- [MariaDB INSERT RETURNING documentation][mariadb-returning]
  (primary source; retrieved 2026-07-30)
- [MariaDB 10.5.2 release notes][mariadb-rename-column]
  (primary source; retrieved 2026-07-30)

[mysql-generated-columns]: https://dev.mysql.com/doc/relnotes/mysql/5.7/en/news-5-7-6.html
[mysql-native-json]: https://dev.mysql.com/doc/refman/5.7/en/json.html
[mysql-803]: https://dev.mysql.com/doc/relnotes/mysql/8.0/en/news-8-0-3.html
[mysql-804]: https://dev.mysql.com/doc/relnotes/mysql/8.0/en/news-8-0-4.html
[mysql-8013]: https://dev.mysql.com/doc/relnotes/mysql/8.0/en/news-8-0-13.html
[mysql-lateral]: https://dev.mysql.com/doc/refman/8.0/en/lateral-derived-tables.html
[mariadb-json]: https://mariadb.com/kb/en/json-data-type/
[mariadb-generated-columns]: https://mariadb.com/kb/en/mariadb-520-release-notes/
[mariadb-generated-column-syntax]: https://mariadb.com/kb/en/generated-columns/
[mariadb-1020-parser]: https://github.com/MariaDB/server/blob/mariadb-10.2.0/sql/sql_yacc.yy#L6284
[mariadb-1021-parser]: https://github.com/MariaDB/server/blob/mariadb-10.2.1/sql/sql_yacc.yy#L6164-L6168
[mariadb-1022-sql-tree]: https://github.com/MariaDB/server/tree/mariadb-10.2.2/sql
[mariadb-1023-json-valid]: https://github.com/MariaDB/server/blob/mariadb-10.2.3/sql/item_jsonfunc.cc#L235
[mariadb-sequences]: https://mariadb.com/kb/en/what-is-mariadb-103/
[mariadb-returning]: https://mariadb.com/kb/en/insertreturning/
[mariadb-rename-column]: https://mariadb.com/kb/en/mariadb-1052-release-notes/
