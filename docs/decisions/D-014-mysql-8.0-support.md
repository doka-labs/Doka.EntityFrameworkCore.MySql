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

The [MySQL 8.0 release notes][mysql80-release-notes] state that the line reaches
EOL in April 2026 with version 8.0.46. The
[Oracle Lifetime Support Policy][oracle-lifetime-support] lists Extended
Support through April 2026 and Sustaining Support thereafter. The first
publishable v1.0 release of this provider is targeting that transition.

Prior to this decision the README, CHANGELOG, csproj description, and the
scheduled `container-matrix.yml` workflow all advertised MySQL 8.0 alongside
MySQL 8.4 as a supported target. Operators reading the v1.0 release notes would
reasonably conclude that running the provider against 8.0 in production is
sanctioned by the project.

A second, smaller drift surfaced in the same review: README and CHANGELOG
implicitly classify MariaDB 11.8 as non-LTS, while the first-party
[MariaDB maintenance policy][mariadb-maintenance-policy] classifies 11.8 as
LTS with community maintenance through 2028-06-04. The misclassification is a
stale artifact from the early 11.x release planning.

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
- The provider source code retains the 8.0-aware engine-profile thresholds.
  Consumers who insist on running against 8.0 must select
  `MySqlServerVersionCompatibilityMode.AllowUnsupported` explicitly. The
  provider emits `MySqlEventId.UnsupportedServerVersion`; the release line
  receives no support guarantee or scheduled compatibility coverage.
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
  database or opt into the unsupported runtime explicitly. The provider
  continues to compile and run for them, but bug reports will be treated as
  best-effort.
- The scheduled CI compatibility matrix no longer detects regressions against
  8.0. If a later patch breaks the retained 8.0 engine-profile path, the
  failure surfaces through the optional external baseline or an external
  consumer report.

#### Neutral

- The 8.0 engine thresholds remain in place behind the explicit compatibility
  mode, so a future ADR can promote the line without reconstructing its dialect
  behavior.

### Confirmation

- Run support-policy boundary, explicit-opt-in, and structured-diagnostic tests.
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

The [MySQL 8.0 release notes][mysql80-release-notes] record EOL in April 2026
with version 8.0.46, and the [Oracle support policy][oracle-lifetime-support]
places the line under Sustaining Support after Extended Support ends that
month. MySQL 8.0 therefore remains outside the supported matrix.

### Active LTS matrix amendment

On 2026-08-11 the decision's maintained-LTS rule admitted every additional
active line proved by first-party lifecycle and GA evidence. The current matrix
is MySQL 8.4 / 9.7 plus MariaDB 10.11 / 11.4 / 11.8 / 12.3. MariaDB 10.6 is no
longer under community maintenance and remains a legacy compatibility line.

The MySQL lines follow the [vendor release model][mysql-release-model] and the
[8.4][mysql84-release-notes] and [9.7][mysql97-release-notes] release records.
The MariaDB lines follow the [maintenance policy][mariadb-maintenance-policy],
[release history][mariadb-release-history], and
[12.3 LTS announcement][mariadb123-lts].

Each admitted line has an exact digest-pinned image, provider support-policy
classification, upstream specification contract, live integration target,
migration-deployment target, runnable-example target, and release-candidate
requirement. The support decision follows the maintained LTS line; exact patch
pins move independently after successful qualification.

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
- `MySqlServerVersion` and `ServerVersionSupportPolicy` -- classify supported,
  legacy, unvalidated, and future release lines.
- `MySqlOptionsExtension` and `MySqlLoggerMessages` -- reject implicit
  unsupported use and warn on explicit compatibility opt-in.

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
- 2026-07-30: Made unsupported execution an explicit, diagnostic compatibility opt-in.
- 2026-08-11: Extended the maintained-LTS rule to MySQL 9.7 and MariaDB
  10.11 / 12.3, then bound all six active lines to the automated qualification
  contract.

### Implementation References

- `README.md`
- `src/Doka.EntityFrameworkCore.MySql/Doka.EntityFrameworkCore.MySql.csproj`
- `src/Doka.EntityFrameworkCore.MySql/MySqlServerVersion.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Capabilities/ServerVersionSupportPolicy.cs`
- `.github/workflows/container-matrix.yml`

### Sources

- [MySQL 8.0 release notes][mysql80-release-notes]
  (primary source; retrieved 2026-08-24)
- [Oracle Lifetime Support Policy for Technology Products][oracle-lifetime-support]
  (primary source; retrieved 2026-08-24)
- [MariaDB Server maintenance policy][mariadb-maintenance-policy]
  (primary source; retrieved 2026-08-24)
- [MySQL 9.7 release model and LTS policy][mysql-release-model]
  (primary source; retrieved 2026-08-24)
- [MySQL 8.4 release notes][mysql84-release-notes]
  (primary source; retrieved 2026-08-24)
- [MySQL 9.7 release notes][mysql97-release-notes]
  (primary source; retrieved 2026-08-24)
- [MariaDB Server release history][mariadb-release-history]
  (primary source; retrieved 2026-08-24)
- [MariaDB 12.3 LTS announcement][mariadb123-lts]
  (primary source; retrieved 2026-08-24)

[mysql80-release-notes]: https://dev.mysql.com/doc/relnotes/mysql/8.0/en/
[oracle-lifetime-support]:
  https://www.oracle.com/us/support/library/lsp-tech-chart-069290.pdf
[mariadb-maintenance-policy]: https://mariadb.org/about/#maintenance-policy
[mysql-release-model]: https://dev.mysql.com/doc/refman/9.7/en/mysql-releases.html
[mysql84-release-notes]: https://dev.mysql.com/doc/relnotes/mysql/8.4/en/
[mysql97-release-notes]: https://dev.mysql.com/doc/relnotes/mysql/9.7/en/
[mariadb-release-history]: https://mariadb.org/mariadb/all-releases/
[mariadb123-lts]: https://mariadb.org/mariadb-server-12-3-lts-released/
