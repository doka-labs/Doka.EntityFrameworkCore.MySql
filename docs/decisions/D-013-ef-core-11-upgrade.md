---
id: D-013
status: accepted
date: 2026-11-19
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "EF Core 11 and .NET 11 major-version strategy"
supersedes: []
superseded-by: []
amends: [D-009]
amended-by: [D-030]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-013 -- Open EF Core 11 as an isolated major line

## 2026-10-22 Amendment: Independent framework release lines

[D-030](D-030-release-lines-and-monotonic-publication.md) replaces this ADR's
proposed `next/ef-core-11`, automatic multi-target and v1/v2 branch/merge
strategy with independently qualified 10.x and 11.x package lines: main becomes
the current major after an approved GA handoff; `maintenance/10.x` retains
.NET 10 servicing; optional `preview/11.x` can qualify independent Doka platform
RCs before that handoff. The `x` is literal, not a ref pattern.

At the original amendment, main/10 publication remained active and the
maintenance/preview routes remained inactive. The GA preparation below now
contains the desired future main/11 plus maintenance/10 policy; actual
protected-main cutover and hosted activation remain separate.
Name approval and repository implementation do not imply branch creation,
hosted protection, preview adoption, package publication or GA readiness.
Microsoft's upstream go-live policy is distinct from Doka's own qualified RC
contract. Fresh approved GA dependencies/source/evidence are required before
stable 11.x; an RC's bytes or evidence cannot be relabelled as GA.

The EF-internal, specification and trim revalidation drivers below remain
applicable. Earlier dates, trigger observations and v1/v2 examples are retained
as historical context, not current rollout instructions. See the
[release-line runbook](../operations/release-lines.md) for current support,
bootstrap, backport, authorization, idle-readiness and rollback requirements.
All authorized lines retain the same `release-candidate.yml` entry, personal
signed tags and human approval. One-time old-code publisher isolation is
separate from the later framework handoff; an inactive preview does not need
to be activated to prepare or complete that GA handoff.

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

The `feature/dotnet-11` line targets `net11.0` only. Its GA preparation uses
SDK `11.0.100`, stable EF Core `[11.0.0, 11.1.0)`, and exact tool/test pins.
The reviewed GA floor and highest admitted patch are both `11.0.0`. The
repository qualifies that bound graph; scheduled floating patch discovery
reports new versions separately and cannot silently admit them.

The earlier RC.1, RC.2 and RC.3 contracts remain historical evidence. The
actual GA inventory and per-engine discovery were regenerated from restored
11.0.0 assemblies, rather than relabelled RC evidence.

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
   release-qualification gates pass against the exact reviewed GA graph.
5. CLI and IDE test discovery work from checked-in project configuration; no
   local runner setting is part of the repository contract.

The first stable 11.x release requires complete GA requalification and the
separately approved protected-main cutover. An RC qualification cannot be
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
- `Directory.Packages.props` declares the stable EF Core `[11.0.0, 11.1.0)` range.
- The versioned specification inventory and discovery files identify that
  exact EF Core version.
- Repository scripts reject stale framework, package, discovery, and evidence
  contracts.
- Native coverage declares the exact shipping-assembly scope, retains existing
  source exclusions and checks each current report's referenced modules before
  applying the unchanged merged coverage floors.
- Native xUnit TRX transport is reconciled through exact forward-encoded
  discovery identities and linked result definitions; ambiguity remains fatal.
- BenchmarkDotNet `0.16.0-preview.2` supplies official .NET 11 support while
  `0.15.8` remains the latest stable tool. This temporary benchmark/test-only pin
  preserves out-of-process execution and validators. Re-evaluate it when a
  .NET 11-capable stable tool is available; shipping package graphs are unchanged.

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

As of 2026-11-15, the isolated 11.x branch targets SDK 11.0.100 and EF Core
11.0.0 GA. The provider source, upstream specification adapters, xUnit v3 / MTP
test projects, repository scripts, runtime posture, package locks and release
contracts are prepared for qualification as one graph.

Actual GA reflection yields 329 compliance bases, 9,318 test definitions,
19,682 assignments and zero provider debt. Each supported target discovers
30,473 IDs. These define coverage; complete live GA executions and publication
remain separate evidence requirements.

### Re-evaluation Triggers

- Microsoft publishes .NET 11 or EF Core 11 GA.
- A later RC changes a provider-facing API or upstream specification inventory.
- The 10.x support policy changes.
- A consumer requires one package to support both EF Core majors.

### Decision History

- 2026-05-16: Decision recorded with status accepted.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.
- 2026-09-19: Maintainer authorized the .NET 11 RC.1 line; replaced the planned
  multi-target shape with an isolated single-target 11.x line.
- 2026-10-20: Updated the exact SDK and EF Core dependency graph to RC.2 and
  regenerated the upstream specification and discovery contracts.
- 2026-11-03: Updated the exact SDK and EF Core dependency graph to RC.3;
  regenerated and compared the upstream specification and discovery contracts.
- 2026-11-19: Aligned current metadata with the shared notification audience and
  last-update date contract; retained the original decision and status history.

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
- [.NET 11 RC.2 release notes][dotnet-11-rc2]
  (primary source; retrieved 2026-10-20)
- [EF Core 11 RC.2 package][efcore-11-rc2]
  (primary source; retrieved 2026-10-20)
- [.NET 11 RC.3 release notes][dotnet-11-rc3]
  (primary source; retrieved 2026-11-03)
- [EF Core 11 RC.3 package][efcore-11-rc3]
  (primary source; retrieved 2026-11-03)

[dotnet-11-rc1]:
  https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/rc1/11.0.0-rc.1.md
[dotnet-release-index]:
  https://github.com/dotnet/core/blob/main/release-notes/releases-index.json
[efcore-11-rc1]:
  https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/11.0.0-rc.1.26425.128
[dotnet-11-rc2]:
  https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/rc2/11.0.0-rc.2.md
[efcore-11-rc2]:
  https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/11.0.0-rc.2.26504.105
[dotnet-11-rc3]:
  https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/rc3/11.0.0-rc.3.md
[efcore-11-rc3]:
  https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/11.0.0-rc.3.26522.148
