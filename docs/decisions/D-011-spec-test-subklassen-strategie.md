# D-011 -- Spec-Test-Subklassen-Strategie

- **Status:** Accepted
- **Date:** 2026-05-16
- **Scope:** `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/`
- **Implementation:** deferred to a follow-up commit

## Context

`Microsoft.EntityFrameworkCore.Relational.Specification.Tests` is the
official acceptance-test corpus that Microsoft ships for EF Core
provider authors. The provider's csproj already carries a reference to
the package, but zero specification-test subclasses exist. The
acceptance suite covers:

- `NorthwindQuery*` (query translation against a known fixture schema),
- `BuiltInDataTypes*` (full CLR-type round-trip matrix),
- `Migrations*` (DDL emission for the standard EF Core operations),
- `Update*` (modification-command-batch behavior end-to-end),
- `OwnedQuery*` / `ComplexType*` (relational mapping for non-entity
  types),
- `JsonQuery*` (JSON-column query translation),
- `TPH*` / `TPT*` / `TPC*` (inheritance strategies).

Without a subclassed run of these suites the provider's v1.0 quality
claim is self-graded. The premortem flagged this as the
highest-impact gap of the release: the corpus exists, the
infrastructure exists, but nothing connects them.

## Decision

Add a `Specification/` directory under
`tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/` containing
provider-specific subclasses of the upstream specification fixtures.

First wave (the minimum for v1.0):

- `NorthwindQueryMySqlTest` plus the seven other Northwind variants.
- `BuiltInDataTypesMySqlTest` plus the standard date/time variants.
- `MigrationsMySqlTest`.
- `UpdatesMySqlTest`.
- `JsonQueryMySqlTest`.

Shared infrastructure:

- A central `MySqlTestStore` abstraction handles per-engine connection-
  string resolution, test-database creation/teardown, and isolation
  across parallel test runs.
- Engine routing exercises both supported engines (MySQL 8.4 LTS and
  the MariaDB 11.x LTS line) via an xUnit theory data source; failures
  are reported per-engine.

Triage discipline:

- A failing spec test belongs in one of three buckets: (1) fixable in
  the current PR, (2) fixable in a follow-up commit (with a referenced
  issue), or (3) a permanent skip with an explicit reason. A central
  `tests/.../Specification/SkipList.md` records every skip, the engine
  it applies to, and the structural reason.
- Bulk-failure handling: if more than 10% of a freshly subclassed
  suite fails, the suite enters quarantine (excluded from the gating
  CI run) until the failure is triaged. Quarantine is logged in the
  same `SkipList.md`.

## Consequences

### Positive

- The provider's quality claim moves from self-graded to
  Microsoft-contract-checked.
- Acceptance regressions surface in the standard suite rather than as
  consumer bug reports months after a release.
- The triage discipline keeps the suite manageable: every red test has
  a documented disposition rather than being silently ignored.
- The shared `MySqlTestStore` infrastructure becomes the canonical
  test-database harness for future test suites.

### Negative

- The first run of any specification suite will surface a set of red
  tests that need triage. The triage cost is bounded by the suite
  size but is non-trivial; the disposition discipline keeps it from
  ballooning.
- CI runtime increases substantially. The specification suites are
  large; the matrix multiplier across two engines doubles the cost.
  The release-gating CI lane runs the full suite; the PR-gating lane
  runs a smoke subset.
- Specification-test fixtures depend on a real MySQL/MariaDB instance.
  The integration-test infrastructure (Docker Compose for local,
  containerized services for CI) becomes a hard requirement for any
  PR that touches translation, scaffolding, or DDL.

### Neutral

- The specification subclasses live in the functional-tests project so
  they remain separable from the unit-test suite that runs on every
  build.

## Re-evaluation triggers

- Microsoft releases a new specification fixture (new EF Core feature
  with a corresponding suite); the first-wave list expands to cover
  it before the next release.
- A persistent quarantine grows beyond a small handful of suites;
  the structural reason for the quarantine surfaces as a separate
  ADR.
- The triage discipline drifts (skips without recorded reason
  appear); the discipline rule shifts to a CI gate that enforces the
  `SkipList.md` correspondence.

## Alternatives considered

- **Custom test set without the Microsoft specifications.** Rejected:
  self-graded; consumers cannot map their EF Core knowledge onto the
  provider's test patterns.
- **Specification tests as an optional later release.** Rejected:
  v1.0 is the acceptance moment; deferring the specification run to
  v1.1 ships v1.0 without the structural quality claim the release
  notes are about to make.
- **Run specifications only against MySQL, skip MariaDB.** Rejected:
  MariaDB is a first-class supported engine; engine-specific
  divergence is exactly what the specification suite catches.
