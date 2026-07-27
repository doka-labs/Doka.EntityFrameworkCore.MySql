---
id: D-014
status: accepted
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Advertised database support and compatibility matrix"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-014 -- Exclude MySQL 8.0 from the supported release matrix

## Context and Problem Statement

MySQL 8.0 reaches the end of Extended Support on 2026-04-30 (per
endoflife.date/mysql (see Sources)). The first publishable
v1.0 release of this provider is targeting a window in which MySQL 8.0 is at
most a few weeks from leaving vendor support. Bug reports, security advisories,
and CVE fixes for 8.0 will stop flowing from Oracle.

Prior to this decision the README, CHANGELOG, csproj description, and the
scheduled `container-matrix.yml` workflow all advertised MySQL 8.0 alongside
MySQL 8.4 as a supported target. Operators reading the v1.0 release notes would
reasonably conclude that running the provider against 8.0 in production is
sanctioned by the project.

A second, smaller drift surfaced in the same review: README and CHANGELOG
implicitly classify MariaDB 11.8 as non-LTS, while the upstream
endoflife.date/mariadb (see Sources) entry lists 11.8 as
LTS with community support until 2028-06-04. The misclassification is a stale
artifact from the early 11.x release planning.

## Decision Drivers

- Supported engines need active upstream maintenance.
- Documentation, package metadata, and CI must advertise one matrix.
- Enterprise adoption needs explicit lifecycle evidence.

## Considered Options

- Support only MySQL 8.4, MariaDB 11.4, and MariaDB 11.8
- Keep MySQL 8.0 with an EOL warning
- Run MySQL 8.0 as undocumented best effort

## Decision Outcome

Chosen option: "Support only MySQL 8.4, MariaDB 11.4, and MariaDB 11.8", because an enterprise support matrix should contain only maintained and continuously tested targets.

**Option A: drop MySQL 8.0 from the v1.0 supported matrix.**

- The v1.0 support matrix is **MySQL 8.4 LTS, MariaDB 11.4 LTS, MariaDB 11.8
  LTS** -- three LTS engines, every one of them covered by upstream support
  through 2028 or later.
- The provider source code retains the 8.0-aware capability thresholds in
  `ServerCapabilities` / the future `EngineProfile`. Consumers who insist on
  running against 8.0 can still compile and execute the provider; they receive
  no support guarantee, no compatibility coverage, and the CI matrix no longer
  exercises 8.0.
- README, CHANGELOG, and the csproj `<Description>` no longer list 8.0 as a
  supported engine. The scheduled `container-matrix.yml` workflow removes its
  `mysql80` service definition.
- MariaDB 11.8 is reclassified as LTS in the same edit pass.

### Consequences

- Good, because documentation, package metadata, and CI describe one defensible LTS matrix.
- Bad, because MySQL 8.0 users must upgrade or accept an explicitly unsupported path.

#### Positive

- The advertised support matrix only contains engines that upstream actively
  supports through at least 2028. Enterprise procurement processes that
  require vendor-supported runtimes see a consistent picture.
- The CI bandwidth previously spent on the 8.0 compatibility job moves to
  faster turnaround on the remaining three engines.
- The MariaDB 11.8 LTS correction removes a doc-vs-reality drift that would
  surface as bug reports during v1.0 evaluation.

#### Negative

- Consumers still running on 8.0 (in particular those who deferred the 8.4
  upgrade) lose the project's explicit blessing and must either upgrade their
  database or accept the unsupported runtime. The provider continues to compile
  and run for them, but bug reports will be treated as best-effort.
- The CI compatibility matrix no longer detects regressions against 8.0. If a
  later patch breaks the 8.0-specific code path in `ServerCapabilities`, the
  failure surfaces only when an external consumer reports it.

#### Neutral

- Provider source code is unchanged; the 8.0 capability thresholds remain in
  place so the drop is reversible. A future ADR can promote 8.0 back into the
  matrix if external demand warrants it.

### Confirmation

- Run support-matrix contract tests and inspect package metadata.
- Review Oracle and MariaDB lifecycle sources before every minor support-matrix change.

## Pros and Cons of the Options

### Support only MySQL 8.4, MariaDB 11.4, and MariaDB 11.8

- Good, because all advertised targets are maintained LTS release lines.
- Bad, because consumers remaining on MySQL 8.0 receive no support guarantee.

### Keep MySQL 8.0 with an EOL warning

- Good, because legacy consumers retain an advertised compatibility path.
- Bad, because the provider would endorse a line in Oracle Sustaining Support.

### Run MySQL 8.0 as undocumented best effort

- Good, because regressions could still be detected occasionally.
- Bad, because CI and documentation would communicate different support contracts.

## More Information

### Current lifecycle evidence

Oracle now records MySQL 8.0 as EOL with 8.0.46 and under Sustaining Support
from April 2026. MariaDB's first-party maintenance table records 11.4 and 11.8
as LTS lines. These primary sources replace the historical aggregator evidence
retained below, and the support-matrix outcome remains unchanged.

### Additional Alternative Rationale

- **Option B: hold 8.0 with an explicit EOL-risk note.** Rejected: the
  provider would inherit a published EOL risk for an engine that upstream no
  longer security-patches. Enterprise consumers reading the README would have
  to evaluate the risk per deployment; the project's position is cleaner if
  the matrix only advertises supported engines.
- **Option C: best-effort 8.0 in the nightly compatibility matrix without
  README mention.** Rejected: the doc-vs-CI split would confuse contributors
  who read the CI workflow to understand the supported surface. A single
  source of truth (the README + csproj description) is more honest.
- **Status quo (keep 8.0 advertised).** Rejected by this decision: ships a
  support matrix that is structurally inconsistent with upstream lifecycle
  reality as of v1.0 release.

### Implementation pointers

- README support-matrix table -- drop the 8.0 row, reclassify 11.8 as LTS.
- CHANGELOG `[Unreleased]` -- drop 8.0 from the "Live integration coverage"
  bullet and the "Entity Framework Core 10 provider for ..." headline.
- `src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj`
  `<Description>` -- drop 8.0.
- `.github/workflows/container-matrix.yml` -- remove the `mysql80` service and
  the corresponding `DOKA_INTEGRATION_TARGETS` entry plus
  `DOKA_MYSQL80_CONNECTION_STRING` env var.

### Re-evaluation Triggers

- Operator-direct feedback from v1.0 beta evaluators that 8.0 support is
  load-bearing for adoption. In that case Option B (hold with EOL-risk doc) or
  Option C (best-effort nightly) gets a fresh review.
- A regression report against the 8.0-specific capability path surfaces
  through GitHub issues, indicating that the dropped CI coverage left a real
  gap.
- MariaDB or MySQL release lifecycle changes that move the supported-LTS
  matrix and require a re-rendering of the README / csproj advertised set.
- Documented adoption demand makes MySQL 8.0 support load-bearing.
- A supported engine lifecycle changes or a new LTS line is adopted.

### Decision History

- 2026-05-16: Decision recorded with status accepted.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.

### Implementation References

- `README.md`
- `src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj`
- `.github/workflows/container-matrix.yml`

### Sources

- [MySQL 8.0 release notes](https://dev.mysql.com/doc/relnotes/mysql/8.0/en/) (primary source; retrieved 2026-07-27)
- [Oracle Lifetime Support Policy for Technology Products](https://www.oracle.com/us/support/library/lsp-tech-chart-069290.pdf) (primary source; retrieved 2026-07-27)
- [MariaDB Server maintenance policy](https://mariadb.org/about/) (primary source; retrieved 2026-07-27)
