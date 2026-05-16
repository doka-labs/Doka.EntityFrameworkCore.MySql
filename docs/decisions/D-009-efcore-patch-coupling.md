# D-009 -- EF-Core-Patch-Coupling-Politik (Range vs. Pin)

- **Status:** Accepted
- **Date:** 2026-05-16
- **Scope:** `Directory.Packages.props` -- Microsoft.EntityFrameworkCore.* package pinning
- **Implementation:** deferred to a follow-up commit

## Context

`Directory.Packages.props` currently pins
`Microsoft.EntityFrameworkCore.Relational` to an exact version
(`10.0.4`). Two consequences for downstream consumers:

1. **Diamond-dependency friction.** A consumer who depends on this
   provider and on another library that pulls `EntityFrameworkCore.*` at
   `10.0.5` ends up with a NuGet warning at best and a runtime version
   mismatch at worst. The provider's exact pin forces the entire
   transitive graph to its specific patch version.
2. **Provider release cadence couples to EF Core's patch cadence.**
   Every EF Core patch (frequent on the 10.0.x line) requires a provider
   bump even when no provider code changes. The cost is a steady stream
   of "version-bump-only" releases that consumers have to track.

The risk on the other side is EF Core's `EF1001` internal surface: the
provider's `EfCoreServiceDecorator` (per D-001) reaches into internal
types whose binary stability is explicitly not promised across patch
versions. A loose floating range opens the door to silent break-on-patch
scenarios that the consumer would hit at runtime.

## Decision

Pin `Microsoft.EntityFrameworkCore.*` packages to the range
`[10.0.4, 10.1.0)` in `Directory.Packages.props`. The range covers the
known-good baseline (10.0.4) and floats upward across patches without
crossing into the next minor.

The risk that `EF1001` patch-drift breaks the decorator is structurally
mitigated by the `EfCorePatchMatrixCI` (foundation backbone 9): a
scheduled GitHub Actions workflow that runs the provider's full test
suite against every published `10.0.x` version, plus the floating
"latest 10.0.x" tag. The CI matrix catches a patch-induced break within
24 hours of the patch publication; the provider's response is either a
minimum-version bump in the range or a hot-fix release.

## Consequences

### Positive

- Consumers can take EF Core patch updates without waiting for a
  provider release.
- Provider release cadence decouples from EF Core's patch cadence; the
  provider releases on its own schedule plus reactive hot-fixes when CI
  catches a patch break.
- The CI matrix's structural mitigation makes the loose-range choice
  auditable: any reported break has a matching CI failure in the
  scheduled runs.

### Negative

- The provider takes on the operational cost of monitoring the CI
  matrix; a break that lands during a non-business-hour window can be
  open for the matrix's poll interval (currently nightly).
- A patch-induced silent semantic change (no compile-time break, no
  test-time break, but a behavioral drift) can slip past the matrix.
  This risk is shared with every loose-pinning choice anywhere in the
  ecosystem; the explicit accept-rate is documented here.
- Diamond-dependency conflicts can still arise if a sibling package
  pins outside the range; the range is the provider's commitment, not
  a guarantee that every dependent declares a compatible range.

### Neutral

- The pin shape becomes part of the provider's contract; widening or
  narrowing it later is itself a SemVer event (a widening is non-
  breaking for consumers, a narrowing is breaking).

## Re-evaluation triggers

- EF Core 10.1.0 is published; the range must be widened to
  `[10.0.4, 11.0.0)` or the provider must explicitly opt into the
  minor-version cadence. The choice depends on whether 10.1.0 changes
  the `EF1001` surface in a way that requires provider work; D-013
  governs the major-version case.
- The EF-Core-Patch-Matrix-CI reports a sustained pattern of breaks
  inside the current range (more than a one-off); the policy would
  swing back toward exact pinning and the consumer cost would be
  re-evaluated.
- A future Microsoft announcement that the `EF1001` surface becomes
  stable across patches would invalidate the structural rationale for
  the CI matrix; the policy would simplify accordingly.

## Alternatives considered

- **Exact pin (status quo, `10.0.4`).** Rejected: diamond-dependency
  friction documented above; release cadence couples to EF Core.
- **Floating range across minors (`[10.0.0, 11.0.0)`).** Rejected as
  v1.0 default: the range is too wide to make the CI matrix
  manageable. A minor-version jump within 10.x could ship a substantive
  internal-surface change without the patch-matrix catching it. The
  case for widening reopens in a future ADR when 10.x stabilizes.
- **Pin per-package, range on `Relational` only.** Rejected: the
  Microsoft.EntityFrameworkCore.* packages are versioned together; a
  per-package mix creates diamond conflicts inside the provider's own
  graph.
