---
id: D-008
status: implemented
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Provider, spatial, and cache package public API governance"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-008 -- Gate the public API with PublicApiAnalyzers

## Context and Problem Statement

10.0.0 is the starting point for long-term SemVer discipline on this
provider. Today the project has:

- no recorded snapshot of the public API surface,
- no mechanical drift detection on the PR-level,
- no enforced separation between "shipped" and "unshipped" additions to
  the public surface.

The premortem flagged this as a high-impact medium-probability risk: a
contributor (human or otherwise) adds, renames or removes a public type
between 10.0.0 and 10.0.1 without realizing it is a SemVer breaking
change. The change lands, downstream consumers break on the patch
upgrade, and the project is reduced to a manual changelog discipline
nobody enforces consistently.

## Decision Drivers

- Unintentional public API changes must fail before release.
- Shipped and unshipped surfaces need a reviewable baseline.
- SemVer decisions must be independent of reviewer memory.

## Considered Options

- PublicApiAnalyzers baselines
- Manual API review
- Custom reflection snapshot tests

## Decision Outcome

Chosen option: "PublicApiAnalyzers baselines", because compiler-enforced baselines provide the strongest low-friction SemVer guard.

Adopt `Microsoft.CodeAnalysis.PublicApiAnalyzers` project-wide. The centrally
managed analyzer version is currently 5.6.0.
Each package source project gains two text files:

- `PublicAPI.Shipped.txt` -- the immutable record of the stable public API.
  A new package keeps it empty through prereleases and populates it by merging
  `PublicAPI.Unshipped.txt` before its first stable release. Later stable
  releases append their accumulated additions in the same way.
- `PublicAPI.Unshipped.txt` -- the working set of public-API additions
  since the last release. Pre-release contributions add their
  declarations here.

Build-level enforcement:

- The analyzer package discovers the `PublicAPI.Shipped.txt` and
  `PublicAPI.Unshipped.txt` files in each source project.
- `RS0016` (declared API not present in shipped or unshipped) becomes
  an error.
- `RS0017` (shipped API removed) becomes an error.
- `RS0036` (annotation drift) becomes an error.

The stable-release process gains one step: the reviewed release-preparation
commit moves `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt` and resets the
unshipped file to empty before hosted qualification. Prerelease tags leave the
working surface unshipped. The move is mechanical and review-visible; the
signed stable tag points to the already qualified commit.

### Consequences

- Good, because public API additions and removals are explicit review events.
- Bad, because intentional API work includes baseline maintenance and package-validation review.

#### Positive

- Public-API changes become impossible to land silently. Every PR that
  touches the public surface either has a corresponding
  `PublicAPI.Unshipped.txt` change or fails the build.
- Drift detection happens at compile time on the contributor's machine,
  not after a release lands on NuGet.
- The shipped/unshipped split makes the difference between "added in
  this release" and "stable since at least version N" textually visible
  in source control.
- 10.0.0 is the right moment to start the discipline; before 10.0.0 the
  surface was explicitly unstable.

#### Negative

- Adds a NuGet-level dependency on `PublicApiAnalyzers`. The package is
  Microsoft-published, MIT-licensed, and exposed only at build time, so
  the dependency cost is bounded.
- Adds friction to PRs that add public surface: the contributor must
  explicitly declare the addition, not just write the C# code.
- The text-file format is a flat declaration list; large API surfaces
  produce large diffs that are hard to read. This provider's surface is
  small enough that the trade-off is acceptable.

#### Neutral

- The analyzer files are committed alongside the source; they are part
  of the source distribution but not part of the runtime distribution.

### Confirmation

- Build all package source projects with warnings treated as errors.
- Run package validation before a release candidate is tagged.

## Pros and Cons of the Options

### PublicApiAnalyzers baselines

- Good, because the compiler enforces exact public-surface drift in normal builds.
- Bad, because baseline files require disciplined updates for intentional changes.

### Manual API review

- Good, because there is no analyzer or baseline maintenance.
- Bad, because overload and signature drift can escape review.

### Custom reflection snapshot tests

- Good, because the repository would own the complete format.
- Bad, because the project would reimplement mature analyzer behavior and diagnostics.

## More Information

### Implementation Snapshot

- `Microsoft.CodeAnalysis.PublicApiAnalyzers` 5.6.0 is wired through
  `Directory.Build.props`. The provider and spatial projects record their
  stable public surfaces from 10.0.0 onward. The cache project records its
  initial stable surface in 10.1.0. Each `PublicAPI.Unshipped.txt` is reset to
  `#nullable enable` during stable preparation. CONTRIBUTING.md documents the
  contributor workflow.

### Implementation Notes

- `Microsoft.CodeAnalysis.PublicApiAnalyzers` 5.6.0 is referenced as a
  build-time analyzer (`PrivateAssets=all`) on all package source projects via a
  conditional `ItemGroup` in `Directory.Build.props`. The analyzer targets
  discover the per-project `PublicAPI.{Shipped,Unshipped}.txt` pair without
  duplicate `AdditionalFiles` registration.
- `EnforceExtendedAnalyzerRules` is **not** set: that property activates the RS1xxx rules meant for analyzer authors (we ship a library, not an analyzer) and would surface unrelated false positives on every `Environment.NewLine` call inside the provider. The global `TreatWarningsAsErrors=true` from `Directory.Build.props` already promotes RS0016 / RS0017 / RS0036 to errors, which is the structurally relevant gate.
- `RS0026` ("Do not add multiple overloads with optional parameters") fires on the `UseMySql` extension family because each overload carries the optional `mySqlOptionsAction = null` parameter. The pattern is the EF Core community standard; the rule is demoted to a warning via `WarningsNotAsErrors` with an explicit rationale comment. A future overload addition still surfaces as a warning and demands explicit reviewer attention.
- Initial surface population ran via `dotnet format analyzers <csproj> --diagnostics RS0016 --severity info`, which applies the analyzer's auto-fix and writes the exact PublicApiAnalyzer-formatted lines into `PublicAPI.Unshipped.txt`.

### PackageValidation after the first stable package

All three package projects enable the .NET SDK's package validation against
their published 10.1.0 packages:

```xml
<EnablePackageValidation>true</EnablePackageValidation>
<PackageValidationBaselineVersion>10.1.0</PackageValidationBaselineVersion>
<RunPackageValidationWithoutReferences>true</RunPackageValidationWithoutReferences>
```

This complements PublicApiAnalyzers, which guards source-level surface drift
commit-by-commit, with the SDK's package-level compatibility checks during
`dotnet pack`. The provider's private EF design-time graph is not part of the
shipped dependency contract, and the spatial project does not consume that
private graph through its project reference. Package validation therefore
compares the package assemblies directly instead of recursively resolving
build-only references. The isolated package consumer separately validates the
dependencies consumers actually restore.

A project can activate the baseline only after that package has a published
stable version. The cache baseline was therefore activated after publication
of 10.1.0, at the same time the provider and NetTopologySuite baselines advanced
to 10.1.0. The same post-publication update keeps future validation anchored to
the most recent stable release.

### Naming choice vs the Pomelo MySQL provider

The Pomelo MySQL provider has historically been the reference EF Core MySQL provider; consumers migrating to Doka have established muscle-memory around Pomelo's fluent-API names. This raised the question whether Doka should mimic Pomelo's names for source-compatibility on options-builder calls, or align with the EF Core relational base API contract.

The decision is **align with the EF Core relational base API contract**:

- `CommandTimeout(int)` -- matches `RelationalDbContextOptionsBuilder<TBuilder, TExtension>.CommandTimeout` exactly.
- `MaxBatchSize(int)` / `MinBatchSize(int)` -- match the relational base.
- `MigrationsHistoryTable(string, string?)` -- matches the relational base.
- `UseQuerySplittingBehavior(QuerySplittingBehavior)` -- matches the relational base.

This carries three properties Pomelo-mimicry would not:

1. **Consistency across providers.** A consumer who already uses `UseSqlServer(...).EnableRetryOnFailure(...).CommandTimeout(...)` reaches for the same names on Doka. Pomelo-mimicry would require Doka-specific aliases for names other relational providers do not carry.
2. **One name per concept.** Pomelo exposed `MaxBatchSize` but also accepted Pomelo-specific spellings (e.g. `MaxBatchSize` vs `MaximumStatements`). Doka exposes only the EF Core relational name; the surface stays scannable.
3. **PublicApiAnalyzers discipline carries through.** The contract this ADR codifies (Shipped vs Unshipped + analyzer-enforced drift detection) applies the same way regardless of provider; mimicking Pomelo's names would not change the discipline but would add a second alias-axis to maintain in `PublicAPI.Shipped.txt`.

Pomelo-consumers migrating to Doka adjust their `UseMySql(...)` lambda once at migration time; the rest of the EF Core surface (DbSet, SaveChanges, Include, etc.) is unchanged. The one-time migration cost is bounded; the alias-axis maintenance cost would have been recurring.

The `EnableRetryOnFailure(int, TimeSpan?)` signature matches Pomelo by coincidence -- it also matches the EF Core SqlServer provider, which set the relational community convention years before Pomelo adopted it.

### Additional Alternative Rationale

- **Status quo (manual `CHANGELOG.md` plus reviewer attention).**
  Rejected: reviewer attention does not scale and is not auditable.
- **`Microsoft.DotNet.ApiCompat`.** Rejected: heavier setup, requires a
  separate baseline package, and the use-case here (single repository,
  small surface, contributor-time enforcement) is exactly what
  PublicApiAnalyzers is designed for.
- **Hand-written API-surface tests via reflection.** Rejected: brittle,
  test-time enforcement (not build-time), and reproduces a fraction of
  what PublicApiAnalyzers gets for free.

### Re-evaluation Triggers

- A future EF Core or .NET release introduces a richer first-party
  drift-detection mechanism (for example, `Microsoft.DotNet.ApiCompat`
  becomes standard); the project would migrate to the upstream choice.
- An operator report from the 10.0.0 release cycle documents a SemVer
  break that PublicApiAnalyzers did not catch (for example, a behavioral
  break that has no API-surface signal); the discipline would extend to
  include a behavior-snapshot complement.
- The shipped/unshipped split becomes unmanageable at a larger surface
  size; the project would adopt a per-public-namespace split or move to
  a generated baseline file.
- The analyzer no longer supports the active .NET or compiler version.
- A new shipped package adds a public surface outside the current baseline gate.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.
- 2026-08-16: Clarified that prerelease surfaces remain unshipped and that the
  stable baseline moves in the reviewed release-preparation commit before
  qualification rather than in a tag commit.
- 2026-08-24: Promoted the 10.0.0 public surface to the shipped baselines and
  clarified that PackageValidation activation follows publication of the
  baseline package.
- 2026-08-27: Promoted the 10.1.0 additions and initial cache surface to the
  shipped baselines. Enabled package validation for the provider and spatial
  packages against 10.0.0; the cache baseline follows publication of 10.1.0.
- 2026-08-28: Advanced the provider and spatial package baselines to 10.1.0 and
  enabled the cache baseline after publication of its first stable package.

### Implementation References

- `Directory.Build.props`
- `Directory.Packages.props`
- `src/Doka.EntityFrameworkCore.MySql/PublicAPI.Shipped.txt`
- `src/Doka.EntityFrameworkCore.MySql/PublicAPI.Unshipped.txt`
- `src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/PublicAPI.Shipped.txt`
- `src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/PublicAPI.Unshipped.txt`
- `src/Doka.Caching.MySql/PublicAPI.Shipped.txt`
- `src/Doka.Caching.MySql/PublicAPI.Unshipped.txt`

### Sources

- [.NET package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/overview)
  (primary source; retrieved 2026-08-27)
- [Baseline package validator](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/baseline-version-validator)
  (primary source; retrieved 2026-08-27)
- [.NET SDK 10.0.400 package-validation target](https://github.com/dotnet/sdk/blob/v10.0.400/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.ApiCompat.ValidatePackage.targets)
  (primary source; retrieved 2026-08-27)
