---
id: D-018
status: implemented
date: 2026-05-18
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Source coverage union, assembly floors, and risk-critical class floors"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-018 -- Enforce coverage by shipped assembly and risk-critical class

## Context and Problem Statement

An aggregate source-coverage percentage can stay green while an entire shipped
assembly or a high-risk class regresses. That happened structurally in the
previous gate:

- only `Doka.EntityFrameworkCore.MySql` contributed to the enforced package
  filter;
- `Doka.EntityFrameworkCore.MySql.NetTopologySuite` had no independent floor;
- live integration coverage was produced but not part of the CI union;
- a class with weak coverage could hide behind unrelated, highly covered
  classes;
- an old merged report could be accepted as current evidence.

Coverage must remain a regression detector rather than a vanity score, but it
must measure the surfaces that are actually shipped and the paths whose failure
would have disproportionate production impact.

The fresh merged union measured on 2026-07-27 combines:

- 515 unit tests;
- 298 non-live functional tests;
- 945 MySQL 8.4 specification results;
- 924 MariaDB 11.4 specification results;
- 924 MariaDB 11.8 specification results;
- 112 dual-engine integration results, of which 97 ran and 15 were excluded
  because their engine targets were not selected.

Every executed test passed. Exact specification skips were reconciled against
D-021 before the coverage evidence was accepted.

## Decision Drivers

- Every shipped assembly needs an independent regression floor.
- Security-, correctness-, translation-, migration-, retry-, and serialization-
  sensitive classes need explicit branch protection.
- Multiple test reports must be merged with per-line deduplication.
- Coverage evidence must belong to the current run.
- Floors need a small measurement buffer without concealing meaningful drift.
- The policy must be versioned, machine-readable, and locally reproducible.

## Considered Options

- Assembly and risk-critical class policy
- Aggregate provider threshold only
- Exact measured percentages as hard floors

## Decision Outcome

Chosen option: "Assembly and risk-critical class policy", because it prevents
large or well-tested surfaces from masking regression in a second package or a
critical class.

`eng/coverage-policy.json` is the authoritative machine-readable policy.
`eng/check-coverage-threshold.sh` accepts exactly one ReportGenerator-merged
Cobertura union and delegates validation to `eng/coverage_policy.py`.

The merged evidence must be no more than six hours old. The gate recomputes
line and branch counters from Cobertura line records instead of trusting
package summary attributes.

### Shipped assembly floors

| Assembly | Measured lines | Line floor | Measured branches | Branch floor |
|---|---:|---:|---:|---:|
| `Doka.EntityFrameworkCore.MySql` | 86.50% | 84% | 72.65% | 70% |
| `Doka.EntityFrameworkCore.MySql.NetTopologySuite` | 79.11% | 77% | 60.42% | 58% |

### Risk-critical class floors

| Class | Line floor | Branch floor |
|---|---:|---:|
| `MySqlConnectionStringRedactor` | 98% | 98% |
| `MySqlDatabaseModelFactory` | 94% | 78% |
| `MySqlExecutionStrategy` | 98% | 73% |
| `MySqlJsonValueComparers` | 86% | 85% |
| `MySqlMigrationsSqlGenerator` | 89% | 72% |
| `MySqlQuerySqlGenerator` | 95% | 68% |
| `MySqlSequenceValueGenerator` | 89% | 68% |
| `MySqlTransientExceptionDetector` | 98% | 90% |
| `MySqlUpdateSqlGenerator` | 98% | 93% |
| `MySqlNetTopologySuiteMethodCallTranslator` | 81% | 73% |
| `MySqlNetTopologySuiteTypeMappingSourcePlugin` | 74% | 52% |

The assembly floors retain roughly two percentage points of measurement
headroom. Critical-class floors are similarly below the fresh measured values
but above any previous blind spot.

### Consequences

- Good, because both NuGet assemblies must now appear in every accepted report.
- Good, because critical branch loss fails even when aggregate coverage rises.
- Good, because unit, functional, specification, and integration coverage all
  contribute to the same deduplicated union.
- Good, because stale or multiply merged evidence is rejected.
- Bad, because CI must run the dual-engine integration suite before the
  coverage job can finish.
- Bad, because adding a critical class requires a deliberate policy update and
  measured baseline.

#### Positive

- `MySqlSequenceValueGenerator` can no longer appear as 0% while integration
  evidence exists elsewhere.
- NetTopologySuite regressions cannot hide behind the larger core assembly.
- The pinned ReportGenerator tool makes local, CI, and release-candidate union
  construction consistent.

#### Negative

- ReportGenerator and coverlet can attribute compiler-generated branches
  differently after an SDK update. The floor buffer absorbs small drift, while
  the re-evaluation process handles material changes.
- Full release-candidate validation takes longer because it produces the same
  coverage corpus as CI.

#### Neutral

- Coverage is evidence of exercised code, not proof of correctness. Official
  specification contracts, property tests, integration tests, analyzers,
  benchmarks, and review gates remain independent controls.

### Confirmation

- Produce a fresh merged union and evaluate it:

```bash
bash eng/merge-coverage.sh artifacts/coverage artifacts/coverage-merged
bash eng/check-coverage-threshold.sh artifacts/coverage-merged
```

- Confirm that the command reports both shipped assemblies and every critical
  class, then exits zero.
- Run the Python regression tests:

```bash
python3 -m unittest discover --start-directory eng/tests --pattern "test_*.py"
```

## Pros and Cons of the Options

### Assembly and risk-critical class policy

- Good, because thresholds follow release boundaries and production risk.
- Good, because a missing assembly or class is a hard error.
- Bad, because the policy contains more reviewed entries than one aggregate
  percentage.

### Aggregate provider threshold only

- Good, because it is simple to explain.
- Bad, because it previously omitted the spatial package and concealed
  class-level gaps.

### Exact measured percentages as hard floors

- Good, because any numeric decrease fails immediately.
- Bad, because harmless SDK or instrumentation drift would create false
  failures with no regression buffer.

## More Information

### Measurement method

Coverlet produces one Cobertura report for each test run. The pinned
ReportGenerator tool merges those reports into one union and deduplicates
source lines reached by multiple test assemblies or engine targets.

The policy reader:

1. rejects zero, future, or stale timestamps;
2. rejects duplicate or missing assembly records;
3. recomputes exact line and condition-branch counters;
4. checks every shipped assembly floor;
5. requires every critical class exactly once in its declared assembly;
6. checks every critical class floor.

The 2026-07-27 accepted union measured:

- core: 11,194/12,941 lines and 4,231/5,824 branches;
- NetTopologySuite: 1,000/1,264 lines and 290/480 branches.

### Additional Alternative Rationale

- A threshold configured only through environment variables is rejected
  because it is easy for local, CI, and release paths to drift.
- Summing raw Cobertura reports is rejected because it double-counts common
  source lines and weights frequently repeated targets more heavily.
- Omitting integration coverage is rejected because database lifecycle,
  sequence, migration, and spatial paths require live engines.

### Re-evaluation Triggers

- Two consecutive main-branch runs exceed an assembly floor by at least four
  percentage points in both dimensions; raise that assembly floor by two
  points.
- A critical class exceeds both floors by at least five points for two
  consecutive main-branch runs; raise its floors while retaining at least a
  two-point buffer.
- A new shipped assembly or production-critical class is added; add it to the
  policy in the same change.
- The .NET SDK, coverlet, or ReportGenerator changes line or branch attribution;
  produce a fresh full union and review the diff before changing a floor.
- Coverage falls below a floor; diagnose the uncovered behavior and add or
  correct tests before changing policy.

### Decision History

- 2026-05-18: Decision recorded with status implemented.
- 2026-05-18: Aggregate merged-union calibration implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0.
- 2026-07-27: Replaced the aggregate-only gate with shipped-assembly,
  critical-class, integration-union, and freshness enforcement.

### Implementation References

- `eng/coverage-policy.json`
- `eng/coverage_policy.py`
- `eng/check-coverage-threshold.sh`
- `eng/merge-coverage.sh`
- `eng/tests/test_coverage_policy.py`
- `.github/workflows/ci.yml`
- `eng/release-candidate.sh`

### Sources

- No external sources; repository evidence only.
