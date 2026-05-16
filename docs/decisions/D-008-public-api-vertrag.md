# D-008 -- Public-API-Vertrag via PublicApiAnalyzers

- **Status:** Implemented
- **Date:** 2026-05-16
- **Scope:** `src/Doka.EntityFrameworkCore.MySql/` + `src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/` public surface
- **Implementation:** `Microsoft.CodeAnalysis.PublicApiAnalyzers` 3.3.4 wired through `Directory.Build.props`; `PublicAPI.Shipped.txt` (empty) and `PublicAPI.Unshipped.txt` (current surface) per src project; CONTRIBUTING.md documents the contributor workflow.

## Implementation notes

- `Microsoft.CodeAnalysis.PublicApiAnalyzers` 3.3.4 is referenced as a build-time analyzer (`PrivateAssets=all`) on both src projects via a conditional `ItemGroup` in `Directory.Build.props`. The same `ItemGroup` registers the two `AdditionalFiles` entries so the analyzer sees the per-project `PublicAPI.{Shipped,Unshipped}.txt` pair.
- `EnforceExtendedAnalyzerRules` is **not** set: that property activates the RS1xxx rules meant for analyzer authors (we ship a library, not an analyzer) and would surface unrelated false positives on every `Environment.NewLine` call inside the provider. The global `TreatWarningsAsErrors=true` from `Directory.Build.props` already promotes RS0016 / RS0017 / RS0036 to errors, which is the structurally relevant gate.
- `RS0026` ("Do not add multiple overloads with optional parameters") fires on the `UseMySql` extension family because each overload carries the optional `mySqlOptionsAction = null` parameter. The pattern is the EF Core community standard; the rule is demoted to a warning via `WarningsNotAsErrors` with an explicit rationale comment. A future overload addition still surfaces as a warning and demands explicit reviewer attention.
- Initial surface population ran via `dotnet format analyzers <csproj> --diagnostics RS0016 --severity info`, which applies the analyzer's auto-fix and writes the exact PublicApiAnalyzer-formatted lines into `PublicAPI.Unshipped.txt`.

## Post-v1.0 follow-up: PackageValidation

Per operator decision (belt-and-suspenders): after the first NuGet release of `Doka.EntityFrameworkCore.MySql` v1.0, the project additionally activates `PackageValidation` (built into the .NET 8+ SDK) so the released `.nupkg` is compared against the previous baseline at every `dotnet pack` time. This complements PublicApiAnalyzers (which guards pre-release surface drift commit-by-commit) with a package-level baseline check that catches assembly-binary-compat regressions PublicApiAnalyzers cannot see (for example, attribute-only changes that affect runtime binding).

The activation is a `Directory.Build.props` edit at release time:

```xml
<EnablePackageValidation>true</EnablePackageValidation>
<PackageValidationBaselineVersion>10.0.0</PackageValidationBaselineVersion>
```

Until the first release publishes a baseline `.nupkg` on nuget.org, PackageValidation has nothing to compare against; PublicApiAnalyzers carries the entire SemVer-discipline load during the pre-v1.0 phase.

## Context

v1.0 is the starting point for long-term SemVer discipline on this
provider. Today the project has:

- no recorded snapshot of the public API surface,
- no mechanical drift detection on the PR-level,
- no enforced separation between "shipped" and "unshipped" additions to
  the public surface.

The premortem flagged this as a high-impact medium-probability risk: a
contributor (human or otherwise) adds, renames or removes a public type
between v1.0.0 and v1.0.1 without realizing it is a SemVer breaking
change. The change lands, downstream consumers break on the patch
upgrade, and the project is reduced to a manual changelog discipline
nobody enforces consistently.

## Decision

Adopt `Microsoft.CodeAnalysis.PublicApiAnalyzers` (3.3.4) project-wide.
Each source project gains two text files:

- `PublicAPI.Shipped.txt` -- the immutable record of the public API as
  of the most recent release. Initially empty for v1.0; the file is
  populated by merging `PublicAPI.Unshipped.txt` at release time.
- `PublicAPI.Unshipped.txt` -- the working set of public-API additions
  since the last release. Pre-release contributions add their
  declarations here.

Build-level enforcement:

- `EnforceExtendedAnalyzerRules` in `Directory.Build.props`.
- `RS0016` (declared API not present in shipped or unshipped) becomes
  an error.
- `RS0017` (shipped API removed) becomes an error.
- `RS0036` (annotation drift) becomes a warning.

The release process gains one step: at tag time, the contents of
`PublicAPI.Unshipped.txt` move to `PublicAPI.Shipped.txt` and the
unshipped file is reset to empty. The move is mechanical and
review-visible.

## Consequences

### Positive

- Public-API changes become impossible to land silently. Every PR that
  touches the public surface either has a corresponding
  `PublicAPI.Unshipped.txt` change or fails the build.
- Drift detection happens at compile time on the contributor's machine,
  not after a release lands on NuGet.
- The shipped/unshipped split makes the difference between "added in
  this release" and "stable since at least version N" textually visible
  in source control.
- v1.0 is the right moment to start the discipline; pre-v1.0 the
  surface was explicitly unstable.

### Negative

- Adds a NuGet-level dependency on `PublicApiAnalyzers`. The package is
  Microsoft-published, MIT-licensed, and exposed only at build time, so
  the dependency cost is bounded.
- Adds friction to PRs that add public surface: the contributor must
  explicitly declare the addition, not just write the C# code.
- The text-file format is a flat declaration list; large API surfaces
  produce large diffs that are hard to read. This provider's surface is
  small enough that the trade-off is acceptable.

### Neutral

- The analyzer files are committed alongside the source; they are part
  of the source distribution but not part of the runtime distribution.

## Re-evaluation triggers

- A future EF Core or .NET release introduces a richer first-party
  drift-detection mechanism (for example, `Microsoft.DotNet.ApiCompat`
  becomes standard); the project would migrate to the upstream choice.
- An operator report from the v1.0 release cycle documents a SemVer
  break that PublicApiAnalyzers did not catch (for example, a behavioral
  break that has no API-surface signal); the discipline would extend to
  include a behavior-snapshot complement.
- The shipped/unshipped split becomes unmanageable at a larger surface
  size; the project would adopt a per-public-namespace split or move to
  a generated baseline file.

## Naming choice vs the Pomelo MySQL provider

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

## Alternatives considered

- **Status quo (manual `CHANGELOG.md` plus reviewer attention).**
  Rejected: reviewer attention does not scale and is not auditable.
- **`Microsoft.DotNet.ApiCompat`.** Rejected: heavier setup, requires a
  separate baseline package, and the use-case here (single repository,
  small surface, contributor-time enforcement) is exactly what
  PublicApiAnalyzers is designed for.
- **Hand-written API-surface tests via reflection.** Rejected: brittle,
  test-time enforcement (not build-time), and reproduces a fraction of
  what PublicApiAnalyzers gets for free.
