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
amended-by: []
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

Introduce an `EngineProfile` record that captures both capabilities and
syntax variants in a structured form:

```csharp
internal sealed record EngineProfile(
    EngineFamily Family,
    Version Version,
    FrozenDictionary<Capability, bool> Capabilities,
    FrozenDictionary<SyntaxFeature, SyntaxBehavior> SyntaxBehaviors);
```

A static table `s_profiles` of `(family, version)` -> `EngineProfile`
entries is consulted by binary-search for the lower-bound version. New
engine releases become a single table append; consumers reading
`profile.Capabilities[X]` do not change.

A second optional layer `EngineProfile.WithProbedOverrides(IProbeRunner)`
takes a runtime probe (server-version SELECT plus selected feature-detection
queries) and overlays the static table with what the actual server reports.
The overlay is the seam for Aurora, TiDB, Vitess and other MySQL-protocol
forks whose version string does not directly map into the static table.

### Consequences

- Good, because capability additions are reviewable data changes with explicit consumers.
- Bad, because incorrect table entries can affect multiple provider subsystems at once.

#### Positive

- Adding support for a new MySQL or MariaDB release becomes a table-append
  operation; consumers do not need to change.
- Dead knobs disappear naturally: a capability without a consumer is simply
  absent from the profile, not declared as `true` everywhere.
- Syntax-variant routing moves out of ad-hoc `isMariaDb` branches and into a
  single lookup (`profile.SyntaxBehaviors[SyntaxFeature.Regexp]`), which
  documents the per-engine differences in one place.
- The overlay seam pre-positions the provider for MySQL-protocol forks
  without requiring a hard fork of `ServerCapabilities`.

#### Negative

- The migration from `ServerCapabilities` to `EngineProfile` touches every
  call site that currently reads a capability field. The change is mechanical
  but wide.
- A profile-table lookup on the hot path costs one `FrozenDictionary` read
  per capability check; the existing `ServerCapabilities` is a direct field
  read. Measured cost is well within noise for normal query translation, but
  any benchmark-sensitive caller (HiLo selection, batch sizing) should
  cache the lookup result locally.

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

- `Capability` enum + `EngineFamily` enum + `EngineProfile` record (with `FrozenSet<Capability>`) + `EngineProfileTable` static lookup with per-`(family, version)` instance cache. `ServerCapabilities.cs` deleted; all 11 consumers (MigrationsSqlGenerator, RelationalTransaction, LoggerMessages, ValueGeneratorSelector, ExecutionStrategy, TransientExceptionDetector, ScaffoldingPipelineContext, SpatialColumnLoader, DatabaseModelFactory, ServerVersion, SingletonOptions) plus 6 test fixtures migrated to `Profile.Has(Capability.X)`.

### Implementation Notes

- `EngineProfileTable.Resolve(family, version)` accumulates capabilities by walking a small set of version thresholds (MySQL 5.7 / 8.0 / 8.0.31; MariaDB 10.2 / 10.3 / 10.3.4 / 10.5). Adding a new engine version becomes a single static-table entry append.
- The three "always-true" capabilities (`SupportsDateTime6`, `SupportsSavepoints`, `SupportsFullTextIndex`) are retained because the transaction surface (`MySqlRelationalTransaction.SupportsSavepoints`), the diagnostic logging (`MySqlLoggerMessages.ServerVersionResolved`), and the engine-baseline tests (`MariaDbCompatibilityBaselineTests`, `MySql80CompatibilityBaselineTests`, `MySqlServerVersionTests`) genuinely consume them. They sit as explicit baseline entries in `EngineProfileTable.Resolve` so a future engine that drops one surfaces as a profile change rather than a silent global assumption.
- `IMySqlTransientExceptionDetector.ShouldRetryOn` lost its unused `ServerCapabilities` parameter; the detector never branched on capabilities and the parameter was already dead.
- `EngineProfileTable.s_cache` is a `ConcurrentDictionary<(EngineFamily, Version), EngineProfile>` so two `MySqlServerVersion.MySql(8.4.0)` calls return the same `EngineProfile` reference. Without the cache, every fresh `MySqlServerVersion` instance produced a fresh `FrozenSet<Capability>` (reference equality only) and EF Core's internal service-provider cache invalidated on every test-DbContext build -- the `ManyServiceProvidersCreatedWarning` escalated to an error in suites that build many contexts per run.
- The `WithProbedOverrides(IProbeRunner)` overlay layer from the ADR is intentionally not implemented yet: no consumer needs it today; the static-table form serves every supported MySQL / MariaDB version through the v1.0 release line.

### Additional Alternative Rationale

- **Status quo (flat boolean record).** Rejected: drift accelerates with
  every new engine version; dead knobs already obscure the real routing.
- **Hexagonal architecture with per-capability interfaces.** Rejected:
  overkill for 16 boolean knobs plus a handful of syntax variants.
  `FrozenDictionary` plus a tiny `SyntaxBehavior` enum delivers the same
  pluggability at a fraction of the moving parts.
- **External configuration file (JSON/YAML) for the profile table.**
  Rejected: the table is small, change-controlled with the source, and
  benefits from compile-time enum exhaustiveness on the `Capability` and
  `SyntaxFeature` keys.

### Re-evaluation Triggers

- A MySQL or MariaDB release introduces a capability that the
  `FrozenDictionary<Capability, bool>` shape cannot express (for example,
  tri-state: supported / supported-with-caveat / unsupported).
- A MySQL-protocol fork ships with version strings that the binary-search
  lower-bound logic cannot resolve correctly; the overlay path would need
  to grow a fork-identification probe.
- A future EF Core release moves capability-detection into the core
  abstractions; the provider profile would then need to align with the
  upstream shape rather than maintain its own.
- A supported engine family or major version requires a new behavior boundary.
- Runtime probing becomes necessary for capabilities that version data cannot determine.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Capabilities/EngineProfile.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Capabilities/EngineProfileTable.cs`

### Sources

- No external sources; repository evidence only.
