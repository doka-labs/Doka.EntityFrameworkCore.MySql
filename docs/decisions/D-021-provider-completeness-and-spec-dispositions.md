---
id: D-021
status: implemented
date: 2026-07-27
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Specification coverage, skips, and engine limitation evidence"
supersedes: []
superseded-by: []
amends: [D-011]
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-021 -- Enforce a zero provider-gap specification ledger

## Context and Problem Statement

The provider is intended to become a primary production provider after publication. A passing
test count is not sufficient evidence for that role when an inherited specification method can
return without invoking the base assertion, or when a provider gap is labeled as an engine
limitation without vendor evidence.

The previous prose-only skip catalog was descriptive rather than enforcing. It allowed three
failure modes:

1. an engine-conditional method could return `Task.CompletedTask` and be reported as passed;
2. a skip could exist without a stable identifier or machine-readable source record;
3. an engine restriction could cause the provider to skip behavior even when a
   semantics-preserving SQL rewrite was available.

## Decision Drivers

- A provider-owned gap must never be mislabeled as engine behavior.
- Skips need executable discovery behavior and machine-readable evidence.
- Engine and framework limits need primary sources and reproducible probes.

## Considered Options

- Zero provider-gap ledger with executable dispositions
- Descriptive skip-catalog prose
- Forbid every skip without classification

## Decision Outcome

Chosen option: "Zero provider-gap ledger with executable dispositions",
because typed executable dispositions preserve full provider responsibility
without falsifying engine reality.

The specification suite has a zero provider-gap budget.

Live specification execution requires a target in the test-host environment.
The functional-test project supplies the version-controlled local default
`mariadb118` through `LocalMariaDb118.runsettings`; explicit environment targets
from CI and operators bypass that file. The database fixture still refuses to
start without a resolved target, and an external endpoint must declare a
server-version token from the selected target's engine and major/minor line.
The per-event CI matrix runs `Category=Spec|Category=Live` in a separate process
for every supported target. A local IDE run therefore cannot be mistaken for
complete matrix evidence.

Every applicable inherited relational behavior must execute on every supported target. A test
may be skipped only when it belongs to one of these three classifications:

- `engine-limitation`: the database engine cannot express the required server-side relational
  operation. The record must contain an official vendor source, retrieval date, reproducible
  probe, provider-workaround assessment, affected targets, and re-evaluation trigger.
- `framework-limitation`: the consumed EF Core version skips the same inherited specification
  shape and fails in framework-owned translation or semantics before provider SQL generation
  can solve it. The record must contain the upstream `dotnet/efcore` issue, retrieval date,
  reproducible probe on every supported target, framework-boundary assessment, and
  re-evaluation trigger.
- `not-applicable`: the premise of the upstream test does not exist for this provider. This is
  limited to structural cases such as compatibility with provider snapshots from before the
  provider existed.

`provider-gap` is never an accepted active disposition. If a specification failure can be
addressed in provider translation, metadata conventions, migrations, type mapping, or test
infrastructure, the provider is changed and the inherited assertion continues to run.

### Consequences

- Good, because every active exception is visible, reproducible, and re-evaluated against supported targets.
- Bad, because upstream suite and vendor-documentation drift can break the governance gate.

#### Positive

- Test reports distinguish a real assertion pass from a documented engine skip.
- Every database limitation is reviewable without relying on agent memory or prose alone.
- Upstream framework skips are visible, source-backed, and routinely re-probed.
- Provider-side workarounds remain the default response to an engine syntax restriction.
- A release cannot quietly accumulate provider gaps.

#### Negative

- Supported-target additions require explicit ledger and probe updates.
- Vendor documentation drift requires periodic source review.
- Some skipped theories appear as one skipped method-level case rather than one case per
  `InlineData` row because xUnit applies the skip during discovery.

### Confirmation

- Run `SpecDispositionContractTests` and
  `eng/testing/check-spec-discovery.sh`.
- Run the specification suite on every active LTS target.

## Pros and Cons of the Options

### Zero provider-gap ledger with executable dispositions

- Good, because every skip is typed, source-backed, target-aware, and reconciled to source.
- Bad, because vendor and upstream evidence require continuing maintenance.

### Descriptive skip-catalog prose

- Good, because contributors can record exceptions with little infrastructure.
- Bad, because silent passes, stale entries, and unsupported claims cannot fail CI.

### Forbid every skip without classification

- Good, because the suite has a simple all-green requirement.
- Bad, because real engine and upstream framework impossibilities become indistinguishable from provider bugs.

## More Information

### Executable Contract

The machine-readable contract is
`tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/SpecDispositions.json`.

### Executable skip contract

Engine limitations use `SpecEngineLimitationTheoryAttribute`. The attribute:

- evaluates the exact `DOKA_SPEC_TEST_TARGET` during xUnit discovery;
- produces a visible skipped test case rather than a successful no-op;
- includes the stable ledger ID in the skip reason;
- can be deliberately bypassed with `DOKA_SPEC_TEST_PROBE_ENGINE_LIMITS=true` to reproduce the
  documented engine failure without editing source.

Upstream framework limitations use `SpecFrameworkLimitationTheoryAttribute`.
The provider override declares its inherited data source explicitly, emits a
visible skip linked to the ledger ID, and can be bypassed with
`DOKA_SPEC_TEST_PROBE_FRAMEWORK_LIMITS=true`. The override still invokes the
inherited assertion, so removing the attribute after an EF Core update
immediately restores the original specification test.

Historical not-applicable tests keep an explicit xUnit skip, but their reason includes the
stable disposition ID.

The contract test reconciles source annotations against
`Specification/SpecDispositions.json`. CI and release-candidate paths must fail when:

- an executable skip is absent from the ledger;
- a ledger test method has no matching source annotation;
- an engine or framework limitation lacks official primary-source evidence or a retrieval date;
- a framework limitation is not reproduced on every supported target;
- an upstream skip is inherited without provider activation or an executable framework disposition;
- an active provider-gap entry exists;
- a silent-pass pattern is reintroduced.

The disposition ledger is only one layer of the executable contract. The
version-bound files below `Specification/Contracts/` additionally enforce:

- the exact official `ComplianceTestBase` and
  `RelationalComplianceTestBase` inventory for every supported EF Core patch;
- a monotonic provider mapping baseline in which provider-owned debt may only
  decrease and can never be reclassified as an engine disposition;
- exact target-specific xUnit discovery IDs, including every Theory row;
- exact TRX reconciliation, so missing, duplicate, failed, unexpected, or
  undeclared `NotExecuted` results fail the gate;
- the official relational compliance assertion and zero provider debt before
  publication.

The provider baseline is now zero. Internal scheduling metadata remains
development bookkeeping; it does not create or amend an architecture decision.

### Public external-limitations inventory

External engine and EF Core limitations are current facts rather than
architecture decisions. Their complete readable inventory, exact affected
targets, primary sources, and retrieval dates therefore live in
`docs/limitations.md`.

The machine-readable disposition ledger remains authoritative for executable
test methods, discovered test IDs, probes, workaround assessments, and
re-evaluation predicates. A contract test keeps every active engine and
framework disposition present exactly once in the public inventory and keeps
structural `not-applicable` dispositions out of it.

### Provider workarounds chosen over skips

Six discovered engine constraints remain fully supported by the provider:

- MySQL and MariaDB reject `LIMIT` directly inside an `IN` subquery.
  `MySqlQuerySqlGenerator.GenerateIn` places the limited query behind a derived-table boundary.
- MySQL 8.4 optimizer bug 114897 returns an incorrect result for the affected
  `EXISTS(JSON_TABLE(...))` shape. `GenerateExists` emits an equivalent limited scalar
  subquery and avoids the faulty semijoin transformation.
- MySQL and MariaDB cap the relevant schema identifiers at 64 characters.
  `MySqlConventionSetBuilder` registers EF Core's maximum-identifier convention so names are
  truncated deterministically with collision suffixes before DDL generation.
- MariaDB exposes `ST_Relate` but no named Covers predicate. The spatial
  translator composes the four OGC DE-9IM covers masks and reverses operands
  for CoveredBy.
- MariaDB `ST_Crosses` returns `NULL` for documented mixed-dimension argument
  orders that NetTopologySuite defines as Boolean. The translator selects the
  applicable NetTopologySuite DE-9IM mask from both runtime dimensions and
  returns `false` for non-null dimension pairs that cannot cross while
  preserving SQL `NULL` when either operand is `NULL`.
- MariaDB's `ST_SRID` is getter-only. Static geometry arguments are serialized
  with `ST_AsWKB` and reconstructed through `ST_GeomFromWKB` with the model
  column's SRID.
- MariaDB uses `ST_NumInteriorRings` and returns a non-null sentinel for
  `ST_IsSimple(NULL)` on the supported 11.x lines. The member translator
  selects the dialect name and preserves the nullable NTS contract with
  `CASE`.

The ledger retains the vendor sources and test evidence for these resolved restrictions. They
are not skips.

### Relational ordering assertions

Four inherited methods, representing eight sync and async test rows, contained
stronger ordering expectations than their SQL queries expressed:

- two TPC methods order only by `Rank`, although two fixture rows share the
  same rank;
- the regular and shared-type complex-navigation methods order the parent by
  ID, flatten an unordered child collection, and then take one child.

The provider overrides execute the original query shapes on every target. They
verify the complete SQL-guaranteed contract: the requested outer ordering,
the exact result set where all rows are retained, or membership in the first
parent's valid child set where `Take(1)` follows an unordered collection.
These rows are real passes, not dispositions, and production SQL generation
does not receive artificial tie-breaker columns.

MySQL explicitly permits any relative order for equal `ORDER BY` keys.
MariaDB documents that another ordering expression is required to order ties.
Both sources were retrieved on 2026-07-30.

### Verified target matrix

The complete `Category=Spec` matrix was executed on 2026-07-30. Every TRX
total matched its version-bound discovery contract:

| EF Core | Target | Passed | Skipped | Failed | Total |
| --- | --- | ---: | ---: | ---: | ---: |
| 10.0.8 | MySQL 8.4.10 | 29,418 | 327 | 0 | 29,745 |
| 10.0.8 | MariaDB 11.4.12 | 28,707 | 702 | 0 | 29,409 |
| 10.0.8 | MariaDB 11.8.8 | 28,709 | 701 | 0 | 29,410 |
| 10.0.10 | MySQL 8.4.10 | 29,426 | 327 | 0 | 29,753 |
| 10.0.10 | MariaDB 11.4.12 | 28,715 | 702 | 0 | 29,417 |
| 10.0.10 | MariaDB 11.8.8 | 28,717 | 701 | 0 | 29,418 |

Discovery regenerated on 2026-08-05 additionally contains one
repository-only documentation-consistency contract per target. Current
discovery totals are 29,746, 29,410, and 29,411 for EF Core 10.0.8, and
29,754, 29,418, and 29,419 for EF Core 10.0.10. The table remains the dated
full live-matrix evidence; the regeneration is not represented as another
full execution.

### Active LTS expansion

The 2026-08-11 support expansion retains one authoritative target set per
disposition in `SpecDispositions.json`. Attribute target arguments are the
source annotation that existed when a method was classified: discovery
requires that annotation to remain a subset of the ledger, while the ledger
extends the executable skip to a newly admitted LTS target only after that
target has a primary-source entry and a probe observation. This avoids copying
one lifecycle change into hundreds of inherited overrides without allowing an
old source annotation to contradict the current evidence owner.

The exact discovery contracts now bind both EF Core patch endpoints to all six
active LTS targets:

| Target | EF Core 10.0.8 | EF Core 10.0.10 |
| --- | ---: | ---: |
| MySQL 8.4 | 29,746 | 29,754 |
| MySQL 9.7 | 29,746 | 29,754 |
| MariaDB 10.11 | 29,412 | 29,420 |
| MariaDB 11.4 | 29,410 | 29,418 |
| MariaDB 11.8 | 29,411 | 29,419 |
| MariaDB 12.3 | 29,417 | 29,425 |

Engine probes on MySQL 9.7, MariaDB 10.11, and MariaDB 12.3 mapped every
observed failure to an existing external disposition and found no provider
gap. The normal EF Core 10.0.8 suites then completed on those three new targets
with exact TRX reconciliation and zero failures. Existing targets retain their
prior complete evidence; the automated per-change matrix now runs all six.

### Superseded process

This decision replaces the descriptive skip-catalog mechanism in D-011 with the executable,
machine-readable disposition contract. D-011 remains the decision to adopt the Microsoft
specification corpus; D-021 governs how exceptions are classified and enforced.

### Re-evaluation Triggers

- A supported MariaDB release adds correlated `FROM` subqueries or a LATERAL/APPLY join form.
- MariaDB adds JSON-subdocument result columns to `JSON_TABLE`.
- The supported MySQL floor reaches 9.3 or newer; bug 114897 is fixed there, but the provider
  rewrite remains valid and should be benchmarked before removal.
- EF Core changes specification method names or discovery semantics.
- Any linked `dotnet/efcore` issue is fixed or closed with a usable framework release.
- A new supported engine target is introduced.
- A linked engine or EF Core limitation is fixed in a supported release.
- EF Core changes specification discovery or method identifiers.

### Decision History

- 2026-07-27: Decision recorded with status implemented.
- 2026-07-27: Executable zero provider-gap disposition contract implemented.
- 2026-07-27: D-011 amended so this decision governs specification exceptions.
- 2026-07-27: Migrated to Doka MADR profile 1.0.
- 2026-07-27: Added version-bound inventory, exact discovery/TRX reconciliation,
  monotonic provider-debt enforcement, and the zero-debt publication gate.
- 2026-07-29: Expanded exact discovery to every provider test below the
  `Specification` namespace and added a classification-drift gate.
- 2026-07-29: Reconciled native JSON validation, temporal precision, empty
  Point, Z/M ordinates, and the inapplicable TPT graph with executable
  dispositions and target-specific probes.
- 2026-07-29: Added exhaustive MariaDB correlated-derived-table and spatial
  dispositions, implemented every semantics-preserving spatial workaround,
  and completed the six-target EF Core 10.0.8/10.0.10 matrix with zero
  failures.
- 2026-07-30: Activated every solvable inherited upstream skip, added
  primary-source-backed framework dispositions for the remaining upstream
  boundaries, and enforced the inherited-skip gate.
- 2026-07-30: Corrected under-specified upstream ordering assertions without
  adding skips or production tie-breakers, then repeated the complete
  six-target matrix with exact TRX reconciliation and zero failures.
- 2026-08-05: Moved the mutable external-limitations inventory to the public
  limitations document while retaining this decision as the classification
  and evidence-governance contract.
- 2026-08-11: Added all active LTS targets, centralized disposition target
  expansion in the ledger, recorded target-specific probes, and retained zero
  provider debt.
- 2026-08-15: Required explicit targets for live local execution and bound
  standalone live functional tests to the six-target per-event CI matrix.
- 2026-08-18: Bound spatial validity, buffer strategies, and collection
  aggregates to their exact engine-version capabilities, and implemented the
  NetTopologySuite Crosses relation through MariaDB DE-9IM predicates.

### Implementation References

- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/SpecDispositions.json`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/SpecDispositionContractTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/Contracts/`
- `docs/limitations.md`
- `eng/testing/check-spec-contract.sh`
- `eng/testing/check-spec-discovery.sh`
- `eng/testing/check-spec-results.sh`
- `eng/check-publication-readiness.sh`

### Sources

- [MariaDB subquery limitations][mariadb-subquery-limitations]
  (primary source; retrieved 2026-07-29)
- [MariaDB JOIN syntax][mariadb-join]
  (primary source; retrieved 2026-07-29)
- [MariaDB JSON_TABLE][mariadb-json-table]
  (primary source; retrieved 2026-07-27)
- [MySQL 8.4 spatial function reference][mysql-spatial]
  (primary source; retrieved 2026-07-29)
- [MariaDB geometry statements][mariadb-geometry]
  (primary source; retrieved 2026-07-29)
- [MariaDB ST_Buffer][mariadb-buffer]
  (primary source; retrieved 2026-07-29)
- [MariaDB ST_Collect][mariadb-collect]
  (primary source; retrieved 2026-07-29)
- [MariaDB ST_IsValid][mariadb-is-valid]
  (primary source; retrieved 2026-07-29)
- [MariaDB ST_Relate][mariadb-relate]
  (primary source; retrieved 2026-07-29)
- [MySQL LIMIT query optimization][mysql-limit]
  (primary source; retrieved 2026-07-30)
- [MariaDB ORDER BY][mariadb-order-by]
  (primary source; retrieved 2026-07-30)
- [dotnet/efcore issue 31397][ef-31397]
  (primary source; retrieved 2026-07-27)
- [dotnet/efcore issue 29287][ef-29287]
  (primary source; retrieved 2026-07-27)
- [dotnet/efcore issue 28733][ef-28733]
  (primary source; retrieved 2026-07-27)
- [dotnet/efcore issue 28645][ef-28645]
  (primary source; retrieved 2026-07-27)
- [dotnet/efcore issue 24263][ef-24263]
  (primary source; retrieved 2026-07-27)
- [dotnet/efcore issue 29416][ef-29416]
  (primary source; retrieved 2026-07-27)

[mariadb-subquery-limitations]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/subqueries/subquery-limitations
[mariadb-join]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/joins/join-syntax
[mariadb-json-table]: https://mariadb.com/docs/server/reference/sql-functions/special-functions/json-functions/json_table
[mysql-spatial]: https://dev.mysql.com/doc/refman/8.4/en/spatial-function-reference.html
[mariadb-geometry]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors
[mariadb-buffer]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/geometry-constructors/st_buffer
[mariadb-collect]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/miscellaneous-gis-functions/st_collect
[mariadb-is-valid]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/miscellaneous-gis-functions/st_isvalid
[mariadb-relate]: https://mariadb.com/docs/server/reference/sql-statements/geometry-constructors/geometry-properties/st_relate
[mysql-limit]: https://dev.mysql.com/doc/refman/8.4/en/limit-optimization.html
[mariadb-order-by]: https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/order-by
[ef-31397]: https://github.com/dotnet/efcore/issues/31397
[ef-29287]: https://github.com/dotnet/efcore/issues/29287
[ef-28733]: https://github.com/dotnet/efcore/issues/28733
[ef-28645]: https://github.com/dotnet/efcore/issues/28645
[ef-24263]: https://github.com/dotnet/efcore/issues/24263
[ef-29416]: https://github.com/dotnet/efcore/issues/29416
