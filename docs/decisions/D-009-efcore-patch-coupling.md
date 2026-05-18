# D-009 -- EF-Core-Patch-Coupling-Politik (Range vs. Pin)

- **Status:** Implemented
- **Date:** 2026-05-16 (floor raised 10.0.4 -> 10.0.8 on 2026-05-18)
- **Scope:** `Directory.Packages.props` -- Microsoft.EntityFrameworkCore.* package pinning
- **Implementation:** `Directory.Packages.props` pins the three Microsoft.EntityFrameworkCore.* packages to the range `[10.0.8, 10.1.0)`; `.github/workflows/ci.yml` carries the `efcore-patch-matrix` job that runs the repo-local test path against `10.0.8` (lower bound) and `10.0.*` (latest 10.0.x patch) on every push.

## Implementation notes

- Range chosen: `[10.0.8, 10.1.0)` per operator decision -- the conservative patch-only variant. The minor-tolerance variant `[10.0.8, 11.0.0)` from the alternatives section was rejected because the EF1001 surface drift across 10.x minors cannot be covered by a CI matrix without doubling the per-push CI minutes.
- The `efcore-patch-matrix` job rewrites the central `PackageVersion` entries via `sed` before restore so the matrix axis maps cleanly to the canonical NuGet version syntax (an exact version like `10.0.8` replaces the range; the floating `10.0.*` resolves to the latest 10.0.x patch at restore time).
- Scope is intentionally narrow: only the repo-local test path (`./eng/test.sh`, which runs Unit + Functional) is exercised per matrix entry. Integration tests stay on a single pin to keep CI minutes manageable; an EF Core patch that breaks the integration-only surface is caught in the nightly `container-matrix` workflow.
- The next minor (10.1.0) requires a deliberate provider response -- either a range widening (`[10.0.8, 10.2.0)` with the matrix gaining a `10.1.x` axis) or the EF Core 11 jump (ADR D-013). The decision is deferred until 10.1.0 is published.
- **Floor-raise log:** 2026-05-18 -- lower bound moved from `10.0.4` to `10.0.8` to absorb four published patches in one step (consumers on `10.0.4..10.0.7` are still inside the range when they pull this provider through transitive resolution; the floor-raise only affects fresh restores). The change closes the diamond-dependency exposure on downstream graphs that already pin `>= 10.0.8`.

## Context

`Directory.Packages.props` originally pinned
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
`[10.0.8, 10.1.0)` in `Directory.Packages.props`. The range covers the
current known-good floor (10.0.8, raised from the original 10.0.4
baseline) and floats upward across patches without crossing into the
next minor.

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
  `[10.0.8, 11.0.0)` or the provider must explicitly opt into the
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
