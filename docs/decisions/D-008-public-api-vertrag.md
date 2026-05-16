# D-008 -- Public-API-Vertrag via PublicApiAnalyzers

- **Status:** Accepted
- **Date:** 2026-05-16
- **Scope:** `src/Doka.EntityFrameworkCore.MySql/` + `src/Doka.EntityFrameworkCore.MySql.NetTopologySuite/` public surface
- **Implementation:** deferred to a follow-up commit

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
