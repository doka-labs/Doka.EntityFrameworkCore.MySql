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
amended-by: [D-023]
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-018 -- Enforce coverage by shipped assembly and risk-critical class

## 2026-08-23 Amendment: Move coverage evaluation to the compiled owner

Coverage union, freshness, assembly floors, critical-class floors, partial
class aggregation, and branch handling are now evaluated by the dependency-free
C# `CoverageGate`. The Python coverage module and its self-tests are retired.
The coverage policy and thresholds are unchanged; only their implementation
owner moved into the existing .NET test and toolchain boundary.

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

The fresh merged union measured on 2026-07-29 combines:

- 536 unit tests;
- 378 non-live functional tests;
- 8,811 executed MySQL 8.4 specification tests and 175 dispositions;
- 8,647 executed MariaDB 11.4 specification tests and 257 dispositions;
- 8,649 executed MariaDB 11.8 specification tests and 256 dispositions;
- 128 executed integration tests and five MySQL 8.0 matrix exclusions.

Every executed test passed. Exact specification dispositions were reconciled
against D-021 before the coverage evidence was accepted.

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
`eng/quality/check-coverage-threshold.sh` accepts exactly one
ReportGenerator-merged Cobertura union and delegates validation to
the compiled `Doka.EntityFrameworkCore.MySql.CoverageGate` tool.

The merged evidence must be no more than six hours old. The gate recomputes
line and branch counters from Cobertura line records instead of trusting
package summary attributes.

### Shipped assembly floors

| Assembly | Measured lines | Line floor | Measured branches | Branch floor |
|---|---:|---:|---:|---:|
| `Doka.EntityFrameworkCore.MySql` | 91.69% | 84% | 79.17% | 70% |
| `Doka.EntityFrameworkCore.MySql.NetTopologySuite` | 81.14% | 77% | 65.13% | 58% |

### Risk-critical class floors

| Class | Lines | Floor | Branches | Floor |
|---|---:|---:|---:|---:|
| `MySqlByteArrayMethodTranslator` | 100.00% | 98% | 100.00% | 98% |
| `MySqlConvertMethodTranslator` | 100.00% | 98% | 91.67% | 89% |
| `MySqlDatabaseModelFactory` | 100.00% | 94% | 92.31% | 78% |
| `MySqlDiagnosticScopeId` | 100.00% | 98% | N/A | N/A |
| `MySqlExecutionStrategy` | 100.00% | 98% | 75.00% | 73% |
| `MySqlGuidMethodTranslator` | 100.00% | 98% | 100.00% | 98% |
| `MySqlJsonValueComparers` | 88.10% | 86% | 88.37% | 85% |
| `MySqlMathMethodTranslator` | 97.41% | 95% | 66.91% | 64% |
| `MySqlMigrationsSqlGenerator` | 92.02% | 89% | 74.94% | 72% |
| `MySqlQuerySqlGenerator` | 96.13% | 95% | 87.00% | 68% |
| `MySqlSequenceValueGenerator` | 91.67% | 89% | 70.00% | 68% |
| `MySqlSqlTokenValidator` | 100.00% | 98% | 100.00% | 98% |
| `MySqlSqlTranslatingExpressionVisitor` | 93.27% | 91% | 84.17% | 82% |
| `MySqlStringMethodTranslator` | 98.28% | 96% | 87.84% | 85% |
| `MySqlTemporalMemberTranslator` | 98.31% | 96% | 82.70% | 80% |
| `MySqlTemporalMethodCallTranslator` | 100.00% | 98% | 100.00% | 98% |
| `MySqlTransientExceptionDetector` | 100.00% | 98% | 92.00% | 90% |
| `MySqlUpdateSqlGenerator` | 98.89% | 98% | 93.75% | 93% |
| `MySqlNetTopologySuiteMethodCallTranslator` | 83.52% | 81% | 75.68% | 73% |
| `MySqlNetTopologySuiteTypeMappingSourcePlugin` | 82.35% | 74% | 72.50% | 52% |

The assembly floors retain roughly two percentage points of measurement
headroom from their original calibration. Critical-class floors retain a
smaller, deliberate buffer below the fresh measured values while preventing
each split query translator from hiding behind aggregate provider coverage.
Branch-free critical classes declare `minimumBranchPercent` as `null`. The
checker renders that state as `N/A`, rejects numeric zero-percent floors, and
fails if instrumentation later discovers a branch without a positive budget.

### Consequences

- Good, because both NuGet assemblies must now appear in every accepted report.
- Good, because critical branch loss fails even when aggregate coverage rises.
- Good, because unit, functional, specification, and integration coverage all
  contribute to the same deduplicated union.
- Good, because stale or multiply merged evidence is rejected.
- Bad, because the exhaustive CI lane must run the dual-engine integration
  suite before the coverage job can finish.
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
bash eng/quality/merge-coverage.sh artifacts/coverage artifacts/coverage-merged
bash eng/quality/check-coverage-threshold.sh artifacts/coverage-merged
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

The 2026-07-29 accepted union measured:

- core: 19,428/21,189 lines and 8,639/10,912 branches;
- NetTopologySuite: 1,024/1,262 lines and 310/476 branches.

### Additional Alternative Rationale

- A threshold configured only through environment variables is rejected
  because it is easy for local, CI, and release paths to drift.
- Summing raw Cobertura reports is rejected because it double-counts common
  source lines and weights frequently repeated targets more heavily.
- Omitting integration coverage is rejected because database lifecycle,
  sequence, migration, and spatial paths require live engines.

### Re-evaluation Triggers

- Two consecutive exhaustive verification runs exceed an assembly floor by at
  least four percentage points in both dimensions; raise that assembly floor
  by two points.
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
- 2026-07-29: Added independent floors for the SQL translation visitor and
  seven split method/member translators using a fresh full test-run union.
- 2026-07-31: Amended by D-023 to enforce the complete coverage union in the
  weekly and manually dispatched exhaustive CI lane.
- 2026-08-01: Removed the `MySqlConnectionStringRedactor` policy entry after
  invalid-configuration diagnostics stopped serializing any connection-string
  representation and the now-consumerless component was deleted.
- 2026-08-01: Added a 98% line floor for the branch-free
  `MySqlDiagnosticScopeId` and 98% line and branch floors for
  `MySqlSqlTokenValidator` after a fresh unit-test coverage run measured every
  instrumentable dimension at 100%.
- 2026-08-23: Moved coverage evaluation from Python to the compiled C# owner
  without changing the policy, thresholds, or evidence contract.

### Implementation References

- `eng/coverage-policy.json`
- `eng/quality/check-coverage-threshold.sh`
- `eng/quality/merge-coverage.sh`
- `eng/tools/Doka.EntityFrameworkCore.MySql.CoverageGate/`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Contracts/CoverageContractTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Contracts/EngineeringToolContractTests.cs`
- `.github/workflows/ci.yml`
- `eng/release-candidate.sh`

### Sources

- No external sources; repository evidence only.
