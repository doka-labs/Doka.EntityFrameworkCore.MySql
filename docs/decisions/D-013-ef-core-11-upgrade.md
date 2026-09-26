---
id: D-013
status: accepted
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "EF Core 11 and .NET 11 major-version strategy"
supersedes: []
superseded-by: []
amends: [D-009]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-013 -- Open EF Core 11 as an isolated major line

## Context and Problem Statement

The published 10.x packages target .NET 10 and EF Core 10. EF Core provider
dependencies include internal `EF1001` surfaces, design-time services,
migration code generation, query translation, and the upstream relational
specification fixtures. A platform-major update therefore requires a new
provider line and complete requalification rather than a dependency-only bump.

The original decision deferred implementation until either EF Core 11 reached
GA or a documented consumer requirement justified earlier work. On 2026-09-19,
the maintainer authorized the release-candidate path after Microsoft published
.NET 11 RC.1 as a go-live release and the matching EF Core RC.1 packages.

## Decision Drivers

- Keep the published .NET 10 / EF Core 10 packages stable and supportable.
- Test one exact prerelease graph instead of floating across preview builds.
- Exercise the actual .NET 11 runtime and reference assemblies.
- Revalidate every provider-owned dependency on EF Core internals.
- Preserve normal CLI and IDE operation without workstation-specific setup.

## Considered Options

- Isolated single-target 11.x provider line
- Multi-target EF Core 10 and EF Core 11 in one package
- Keep the provider on EF Core 10 until EF Core 11 GA

## Decision Outcome

Chosen option: "Isolated single-target 11.x provider line", because it keeps
the released 10.x line stable while letting the next major validate the real
.NET 11 and EF Core 11 surface without a conditional dual-provider build.

The `feature/dotnet-11` line targets `net11.0` only. It pins SDK
`11.0.100-rc.1.26425.128` and EF Core `11.0.0-rc.1.26425.128` exactly. The
matching RC dependency graph is intentional: a prerelease provider-facing API
change requires a reviewed code and evidence update rather than a floating
restore.

The published 10.x packages remain the .NET 10 / EF Core 10 maintenance line.
They are not multi-targeted and are not changed by the 11.x branch.

The 11.x line may ship only after all of the following contracts pass:

1. Provider, spatial, cache, tools, examples, and tests build warning-free on
   the pinned .NET 11 SDK.
2. Public API and package validation pass for all three shipped packages.
3. Every upstream relational specification base is mapped with zero provider
   debt, and exact discovery and TRX reconciliation pass on all six supported
   database targets.
4. Unit, functional, integration, runtime-posture, trimming, packaging, and
   release-qualification gates pass against the exact RC graph.
5. CLI and IDE test discovery work from checked-in project configuration; no
   local runner setting is part of the repository contract.

At EF Core 11 GA, the line moves from the exact RC pins to exact GA pins and is
requalified before the first stable 11.x release. An RC qualification cannot be
reused as GA evidence.

### Consequences

- Good, because .NET 10 consumers keep an unchanged stable line.
- Good, because the 11.x package has one runtime and one EF Core contract.
- Good, because preview drift cannot enter through restore without review.
- Bad, because fixes applicable to both majors require deliberate backport and
  forward-port decisions.
- Bad, because an RC-to-GA update repeats the complete qualification path.

### Confirmation

- `global.json` pins the exact .NET 11 SDK and Microsoft Testing Platform
  runner.
- `Directory.Build.props` targets `net11.0` and declares the 11.x package line.
- `Directory.Packages.props` pins one exact EF Core RC.1 graph.
- The versioned specification inventory and discovery files identify that
  exact EF Core version.
- Repository scripts reject stale framework, package, discovery, and evidence
  contracts.

## Pros and Cons of the Options

### Isolated single-target 11.x provider line

- Good, because framework and provider behavior are unambiguous.
- Good, because 10.x remains untouched.
- Bad, because the project maintains two release lines during the overlap.

### Multi-target EF Core 10 and EF Core 11 in one package

- Good, because consumers would select one package version.
- Bad, because provider internals, test fixtures, package dependencies, and
  migration design-time services would require conditional dual
  implementations in one artifact.
- Bad, because one package could no longer communicate one EF Core contract.

### Keep the provider on EF Core 10 until EF Core 11 GA

- Good, because no prerelease graph enters provider development.
- Bad, because EF Core 11 compatibility work and regressions would be deferred
  until the stable release boundary.

## More Information

### Implementation Snapshot

As of 2026-09-19, the isolated 11.x branch targets the exact .NET 11 RC.1 SDK
and EF Core RC.1 packages. The provider source, upstream specification
adapters, xUnit v3 test projects, repository scripts, runtime posture, package
locks, documentation, and release contracts are being qualified as one change.

The exact upstream contract contains 329 compliance bases, 9,299 test
definitions, and 19,659 provider assignments. Each supported target discovers
30,422 tests. The versioned inventory and six target-specific discovery files
are release inputs, not descriptive snapshots.

### Re-evaluation Triggers

- Microsoft publishes .NET 11 or EF Core 11 GA.
- A later RC changes a provider-facing API or upstream specification inventory.
- The 10.x support policy changes.
- A consumer requires one package to support both EF Core majors.

### Decision History

- 2026-05-16: Decision recorded with status accepted.
- 2026-05-16: Defined a GA or consumer-demand implementation trigger.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the outcome.
- 2026-09-19: Maintainer authorized the .NET 11 RC.1 line; replaced the planned
  multi-target shape with an isolated single-target 11.x line.

### Implementation References

- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/Contracts/`
- `eng/testing/test-spec-matrix.sh`

### Sources

- [.NET 11 RC.1 release notes][dotnet-11-rc1]
  (primary source; retrieved 2026-09-19)
- [.NET release index][dotnet-release-index]
  (primary source; retrieved 2026-09-19)
- [EF Core 11 RC.1 package][efcore-11-rc1]
  (primary source; retrieved 2026-09-19)

[dotnet-11-rc1]:
  https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/rc1/11.0.0-rc.1.md
[dotnet-release-index]:
  https://github.com/dotnet/core/blob/main/release-notes/releases-index.json
[efcore-11-rc1]:
  https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/11.0.0-rc.1.26425.128
