# D-004 -- EngineProfile-Modell statt flacher Bool-Record

- **Status:** Accepted
- **Date:** 2026-05-16
- **Scope:** `Internal/Capabilities/` engine-routing model
- **Implementation:** deferred to a follow-up commit

## Context

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

## Decision

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

## Consequences

### Positive

- Adding support for a new MySQL or MariaDB release becomes a table-append
  operation; consumers do not need to change.
- Dead knobs disappear naturally: a capability without a consumer is simply
  absent from the profile, not declared as `true` everywhere.
- Syntax-variant routing moves out of ad-hoc `isMariaDb` branches and into a
  single lookup (`profile.SyntaxBehaviors[SyntaxFeature.Regexp]`), which
  documents the per-engine differences in one place.
- The overlay seam pre-positions the provider for MySQL-protocol forks
  without requiring a hard fork of `ServerCapabilities`.

### Negative

- The migration from `ServerCapabilities` to `EngineProfile` touches every
  call site that currently reads a capability field. The change is mechanical
  but wide.
- A profile-table lookup on the hot path costs one `FrozenDictionary` read
  per capability check; the existing `ServerCapabilities` is a direct field
  read. Measured cost is well within noise for normal query translation, but
  any benchmark-sensitive caller (HiLo selection, batch sizing) should
  cache the lookup result locally.

### Neutral

- The model is internal; consumers continue to interact with engine
  differences through fluent-API surface (`UseMySql(...)`,
  `MySqlServerVersion.MariaDb(...)`).

## Re-evaluation triggers

- A MySQL or MariaDB release introduces a capability that the
  `FrozenDictionary<Capability, bool>` shape cannot express (for example,
  tri-state: supported / supported-with-caveat / unsupported).
- A MySQL-protocol fork ships with version strings that the binary-search
  lower-bound logic cannot resolve correctly; the overlay path would need
  to grow a fork-identification probe.
- A future EF Core release moves capability-detection into the core
  abstractions; the provider profile would then need to align with the
  upstream shape rather than maintain its own.

## Alternatives considered

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
