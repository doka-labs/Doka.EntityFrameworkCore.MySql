---
id: D-009
status: implemented
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "EF Core package range and compatibility verification"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-009 -- Support an EF Core patch range with a floor/latest matrix

## Context and Problem Statement

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

## Decision Drivers

- Consumers need patched EF Core versions without a provider republish.
- EF1001 coupling requires early detection of patch-level breaks.
- Resolved package versions need machine-readable evidence.

## Considered Options

- Patch range with floor and latest matrix
- Exact EF Core patch pin
- Accept every EF Core version below the next major

## Decision Outcome

Chosen option: "Patch range with floor and latest matrix", because a bounded patch range balances consumer patching with evidence-backed compatibility.

Pin `Microsoft.EntityFrameworkCore.*` packages to the range
`[10.0.8, 10.1.0)` in `Directory.Packages.props`. The range covers the
current known-good floor (10.0.8, raised from the original 10.0.4
baseline) and accepts a consumer-selected later patch without crossing
into the next minor. A standalone repository restore remains
deterministic at the lower bound; the matrix uses an explicit floating
override only to discover and validate the latest patch.

The risk that `EF1001` patch-drift breaks the decorator is structurally
mitigated by the `EfCorePatchMatrixCI` (foundation backbone 9). Every
repository CI run validates both the supported floor and the floating
"latest 10.0.x" tag against non-live tests, two-engine specification and
live tests, and a representative two-engine integration matrix. The
provider's response to a detected break is either a minimum-version bump
in the range or a hot-fix release.

### Consequences

- Good, because supported patch updates do not require a provider release.
- Bad, because the matrix consumes CI time and can fail when Microsoft publishes a breaking patch.

#### Positive

- Consumers can take EF Core patch updates without waiting for a
  provider release.
- Provider release cadence decouples from EF Core's patch cadence; the
  provider releases on its own schedule plus reactive hot-fixes when CI
  catches a patch break.
- The CI matrix's structural mitigation makes the loose-range choice
  auditable through resolved-package JSON, test reports, and database
  lifecycle evidence.

#### Negative

- The provider takes on the operational cost of the wider matrix on
  every repository CI run.
- A patch-induced silent semantic change (no compile-time break, no
  test-time break, but a behavioral drift) can slip past the matrix.
  This risk is shared with every loose-pinning choice anywhere in the
  ecosystem; the explicit accept-rate is documented here.
- Diamond-dependency conflicts can still arise if a sibling package
  pins outside the range; the range is the provider's commitment, not
  a guarantee that every dependent declares a compatible range.

#### Neutral

- The pin shape becomes part of the provider's contract; widening or
  narrowing it later is itself a SemVer event (a widening is non-
  breaking for consumers, a narrowing is breaking).

### Confirmation

- Run the `efcore-patch-matrix` CI job for the floor and latest 10.0.x patch.
- Retain the resolved package JSON for both matrix entries.

## Pros and Cons of the Options

### Patch range with floor and latest matrix

- Good, because consumers receive compatible patches while CI checks both support boundaries.
- Bad, because floating latest-patch validation can reveal upstream breaks after merge.

### Exact EF Core patch pin

- Good, because every build uses one completely deterministic dependency graph.
- Bad, because consumers cannot take a compatible security or reliability patch independently.

### Accept every EF Core version below the next major

- Good, because the install range is maximally flexible.
- Bad, because untested minor versions can change internal provider contracts.

## More Information

### Implementation Snapshot

- `Directory.Packages.props` applies the `DokaEfCoreVersion` property to the three Microsoft.EntityFrameworkCore.* packages and defaults it to `[10.0.8, 10.1.0)`. `.github/workflows/ci.yml` overrides that property with `10.0.8` and `10.0.*`, asserts the resolved package graph, and runs non-live, specification, live, and integration coverage for both matrix entries.

### Implementation Notes

- Range chosen: `10.0.8, 10.1.0)` per operator decision -- the conservative patch-only variant. The minor-tolerance variant `[10.0.8, 11.0.0)` from the alternatives section was rejected because the EF1001 surface drift across 10.x minors cannot be covered by a CI matrix without doubling the per-push CI minutes.
- The `efcore-patch-matrix` job overrides `DokaEfCoreVersion` at MSBuild evaluation time. It does not edit source files. Central Package Management floating versions remain disabled by default and are enabled only inside this matrix job, as documented by [NuGet error NU1011 (see Sources).
- Every matrix restore is followed by a machine-readable `dotnet package list` readback. The job fails unless Design, Relational, and Relational.Specification.Tests resolve to one version matching the matrix contract. The JSON readback is retained as an artifact.
- Each entry runs `eng/test.sh`, specification plus live functional tests against MySQL 8.4 and MariaDB 11.8, and the representative MySQL 8.4 plus MariaDB 11.8 integration matrix. Test-owned containers make the live paths identical locally and in CI.
- The next minor (10.1.0) requires a deliberate provider response -- either a range widening (`[10.0.8, 10.2.0)` with the matrix gaining a `10.1.x` axis) or the EF Core 11 jump (ADR D-013). The decision is deferred until 10.1.0 is published.
- **Floor-raise log:** 2026-05-18 -- lower bound moved from `10.0.4` to `10.0.8` to absorb four published patches in one step. Consumers pinned to `10.0.4..10.0.7` fall outside the provider range and must upgrade. The change closes the diamond-dependency exposure on downstream graphs that already pin `>= 10.0.8`.

### Additional Alternative Rationale

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

### Re-evaluation Triggers

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
- EF Core 10.1 or 11 reaches a release the provider intends to support.
- A 10.0.x patch breaks an EF1001 or specification-test contract.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.

### Implementation References

- `Directory.Packages.props`
- `.github/workflows/ci.yml`

### Sources

- [NuGet warning NU1011](https://learn.microsoft.com/nuget/reference/errors-and-warnings/nu1011) (primary source; retrieved 2026-07-27)
