---
id: D-011
status: implemented
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "EF Core relational specification test adoption"
supersedes: []
superseded-by: []
amends: []
amended-by: [D-021]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-011 -- Adopt the official EF Core relational specification corpus

## Context and Problem Statement

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

## Decision Drivers

- Provider behavior should be measured against upstream relational contracts.
- Inherited tests must run through provider-specific fixtures.
- Exceptions need reviewable and enforceable disposition rules.

## Considered Options

- Provider subclasses of official specification suites
- Repository-authored tests only
- Fork the complete upstream specification project

## Decision Outcome

Chosen option: "Provider subclasses of official specification suites", because official inherited suites provide the strongest shared contract for an EF Core provider.

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

### Consequences

- Good, because upstream behavior and regressions become part of provider verification.
- Bad, because the suite requires target-aware fixtures and explicit disposition governance.

#### Positive

- The provider's quality claim moves from self-graded to
  Microsoft-contract-checked.
- Acceptance regressions surface in the standard suite rather than as
  consumer bug reports months after a release.
- The triage discipline keeps the suite manageable: every red test has
  a documented disposition rather than being silently ignored.
- The shared `MySqlTestStore` infrastructure becomes the canonical
  test-database harness for future test suites.

#### Negative

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

#### Neutral

- The specification subclasses live in the functional-tests project so
  they remain separable from the unit-test suite that runs on every
  build.

### Confirmation

- Run `eng/check-spec-discovery.sh` and every supported-engine specification matrix.
- Apply the executable disposition contract defined by D-021.

## Pros and Cons of the Options

### Provider subclasses of official specification suites

- Good, because upstream behavior additions become visible through inherited tests.
- Bad, because fixture adaptation and engine-specific expected SQL require maintenance.

### Repository-authored tests only

- Good, because all tests can be tailored to the provider implementation.
- Bad, because upstream relational behaviors can remain silently uncovered.

### Fork the complete upstream specification project

- Good, because the provider controls every fixture and test source line.
- Bad, because the fork would drift from each EF Core patch and obscure upstream changes.

## More Information

### Implementation Snapshot

- infrastructure (`MySqlTestEnvironment`, `MySqlTestStore`, factories) plus the first two subclasses (`NorthwindWhereQueryMySqlTest`, `BuiltInDataTypesMySqlTest`) ship in the same commit as this status update. The remaining Northwind variants (Aggregate, GroupBy, Join, ...) follow incrementally. Test classes carry `[Trait("Category", "Spec")]` so the repo-tests path excludes them and the dedicated CI `spec-test-suite` job runs them against MySQL 8.4 + MariaDB 11.8.

### Additional Alternative Rationale

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

### Re-evaluation Triggers

- Microsoft releases a new specification fixture (new EF Core feature
  with a corresponding suite); the first-wave list expands to cover
  it before the next release.
- A persistent quarantine grows beyond a small handful of suites;
  the structural reason for the quarantine surfaces as a separate
  ADR.
- The triage discipline drifts (skips without recorded reason
  appear); the discipline rule shifts to a CI gate that enforces the
  `SkipList.md` correspondence.
- EF Core adds, removes, or renames a relational specification base family.
- A fixture cannot preserve the inherited assertion on a supported engine.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.

### Implementation References

- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/`
- `eng/check-spec-discovery.sh`

### Sources

- No external sources; repository evidence only.
