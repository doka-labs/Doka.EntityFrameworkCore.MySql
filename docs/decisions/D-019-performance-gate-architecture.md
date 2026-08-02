---
id: D-019
status: implemented
date: 2026-05-18
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Named performance workloads, reproducible baselines, budgets, and soak gates"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-019 -- Gate performance and resource behavior at the publication boundary

## Context and Problem Statement

The provider had useful BenchmarkDotNet controls, but they did not constitute
complete release evidence:

- several benchmarks measured a terminal aggregate instead of the named
  materialization path;
- the corpus did not cover sync and async execution, compiled and dynamic
  queries, retry, diagnostics, context and connection pooling, concurrency, or
  representative size boundaries as explicit matrix dimensions;
- historical reports could be mistaken for the current run;
- a benchmark process could return success after an individual benchmark
  failed;
- the repository retained no accepted median, p95, p99, standard-error,
  allocation, collection, or retained-memory baseline;
- cache bounds, pooled-buffer ownership, connection cleanup, advisory-lock
  cleanup, working-set stabilization, and sustained concurrent throughput were
  not release gates;
- an overloaded host could complete most of a scorecard before historical
  latency comparison exposed that its measurements were not comparable;
- macOS workload evidence identified every Apple Silicon processor only as
  `Arm64`, and same-run BenchmarkDotNet host identity was not cross-checked
  against the workload process.

Performance evidence must prove that the provider is not the limiting factor
for its supported engine families. It must remain reviewable, reproducible, and
hard-failing without turning shared-runner noise into arbitrary threshold
changes.

## Decision Drivers

- Every result must correspond to a named provider behavior.
- MySQL and MariaDB need independent evidence on comparable runner classes.
- Tail latency, managed allocation, garbage collection, and retained memory
  need persisted raw evidence.
- Missing, stale, malformed, noisy, or failing measurements must stop the gate.
- Host load and concrete processor identity must be accepted before timing
  starts, and every measurement layer must agree on that host.
- Absolute limits and historical regression limits serve different purposes
  and must both be enforced.
- Sustained resource ownership needs explicit invariants outside
  microbenchmarks.
- Baseline replacement must be an operator-reviewed action, never an automatic
  response to a regression.

## Considered Options

- Versioned workload contract with dual budget layers and soak gates
- BenchmarkDotNet ratios without historical baselines
- Informational benchmark reports

## Decision Outcome

Chosen option: "Versioned workload contract with dual budget layers and soak
gates", because it separates workload completeness, measurement integrity,
hardware-independent safety ceilings, comparable-runner regressions, and
sustained resource ownership into independently reviewable controls.

`benchmarks/performance-contract.json` is the authoritative machine-readable
contract. The C# harness executes the provider paths, and
`eng/performance_evidence.py` independently validates and evaluates their
evidence.

### Named workload matrix

The scorecard requires every declared workload exactly once. Its 55 cells
cover:

- cold and warm model access, including a 256-entity model;
- sync and async queries;
- compiled and dynamic queries;
- context pooling, connection pooling, both pools, and neither pool;
- retry and diagnostic-listener combinations;
- one, four, and sixteen concurrent contexts;
- one, ten, one hundred, one thousand, and ten thousand rows where applicable;
- full-entity and concrete projection materialization;
- equal, early-mismatch, and late-mismatch JSON values at 1 KiB, 64 KiB, and
  1 MiB;
- JSON and spatial materialization;
- default, 32-row, and 256-row write batches;
- translation and migration corpora;
- sync and async HiLo writes across one and ten contexts.

Benchmark names describe the work actually performed. Materialization
benchmarks materialize rows; they do not substitute `Count()` or another
aggregate. The HiLo fixture follows the provider capability contract: MariaDB
uses native sequences and MySQL uses the provider's table emulation.

The `smoke`, `scorecard`, and `stress` profiles define warmup, sample, noise,
and soak requirements. Scorecard and stress runs require the complete matrix,
an accepted baseline, and all soak scenarios. Smoke is a fast structural check
and cannot qualify a release.

### Measurement and evidence integrity

BenchmarkDotNet remains the isolated microbenchmark layer for same-run
controls. It uses the full JSON exporter, the memory diagnoser, Release builds,
and stop-on-first-error behavior. The host process also returns non-zero for:

- no benchmark summaries;
- critical validation errors;
- any unsuccessful benchmark report;
- an exception in a workload or soak run.

The named-workload runner persists every raw sample and derives median, p95,
p99, and standard error using documented linear interpolation. The Python gate
recomputes each statistic from the samples and rejects a mismatch, a non-finite
number, a missing matrix cell, or excessive relative standard error.

Fast, idempotent work uses a fixed, checked-in operations-per-sample value. The
runner times the complete batch and normalizes latency, allocation, and
collection counts per operation. This keeps timer resolution and loop overhead
from dominating sub-microsecond paths while preserving deterministic workload
identity. Tail statistics for these paths describe the distribution of
per-operation-normalized batch samples, not individual OS scheduling pauses.
Scorecard evidence requires 256 samples with at most 25% relative standard
error; stress requires 512 samples with at most 15%. These counts provide
enough individual observations to retain several measurements in the p99 tail
instead of deriving it from an undersized expensive-workload sample.

Managed allocation uses the precise process allocation counter around the
measured operation. Preparation and cleanup are outside that window. Garbage
collection counts cover the same operation window. Retained bytes are measured
after forced full collections before and after the workload series. These
metrics describe managed process behavior; they do not claim to measure native
driver or server allocation.

Every evaluation records:

- run ID, target, profile, Git commit, and exact working-tree source hash;
- stable runner class and concrete processor model;
- .NET runtime, OS, architecture, processor, processor count, and exact server
  image;
- one-, five-, and fifteen-minute pre-measurement load averages, the
  one-minute load per logical processor, and its contract ceiling;
- engine family and the server-observed `SELECT VERSION()` value;
- raw BenchmarkDotNet report paths and SHA-256 hashes;
- workload, soak, contract, and derived-evidence hashes;
- all absolute and historical verdicts.

The wrapper builds first, then waits up to five minutes for the one-minute load
average to fall to at most `0.40` per logical processor. This ceiling remains
below the `0.50` to `0.75` range in which the same code produced materially
different latency during controlled reproduction. The wrapper persists that
preflight and exports the exact values into the workload process. The evaluator
binds both artifacts and also requires BenchmarkDotNet to report the same
processor and process architecture in a Release build. A targeted
single-workload diagnostic report uses a distinct kind that the release
evaluator rejects; it supports root-cause analysis without weakening matrix
completeness.

The source hash excludes only the generated baseline output. This avoids a
self-referential digest while binding measurements made during code review to
the exact uncommitted source that produced them.

Before measurement, the wrapper resolves exactly one live container on the
target port. Its configured image or repository digest must match the
digest-pinned contract. The workload runner then obtains the database version
from the server; target labels cannot substitute for observed engine identity.

### Budget model

Absolute budgets are broad, runner-tolerant failure ceilings. They detect
runaway complexity, accidental client work, unbounded allocation, and resource
catastrophes. They are calibrated above the worst dual-engine local scorecard,
with larger latency headroom than memory headroom because runner timing varies
more than managed allocation.

Historical budgets are stricter. They compare only the same target, profile,
and runner class. The current runtime, OS, architecture, processor, processor
count, and server image must also exactly match the accepted environment. A
reused runner label cannot hide hardware or runtime drift.

| Metric | Maximum relative to accepted baseline |
|---|---:|
| Median | 1.25x |
| p95 | 1.35x |
| p99 | 1.50x |
| Allocated bytes per operation | 1.15x |
| Retained bytes | 1.25x plus 8 MiB noise allowance |
| Gen 0, Gen 1, and Gen 2 collections | 1.25x plus 250 per 1,000 allowance |

Both layers must pass. An absolute budget cannot replace historical
comparison, and a favorable historical comparison cannot excuse an absolute
safety violation.

The accepted baseline contains one complete MySQL 8.4 and MariaDB 11.8 pair
for each runner class. Replacing a pair requires successful seed evaluations
for both targets. Existing complete runner groups are retained. Duplicate or
partial target/profile/runner tuples are rejected.

### Sustained resource gates

Scorecard and stress runs execute six independent soak invariants:

| Scenario | Enforced invariant |
|---|---|
| HiLo state cache | No more than 1,024 retained entries |
| Pooled JSON buffer | Every rent is returned and no buffer remains outstanding |
| Connections | Physical connected-thread delta is at most one |
| Migration lock | No provider advisory lock remains held |
| Process memory | Working-set growth <= 64 MiB and managed-heap growth <= 32 MiB |
| Concurrency | Final throughput retains at least 70% of initial throughput |

The evaluator does not trust the report's success flag. It checks exact metric
and budget fields, verifies that reported budgets equal the checked-in
contract, and recomputes every verdict.

### Automation and baseline acceptance

The weekly benchmark workflow and the release-candidate workflow run the
scorecard against both required engines. A scorecard with no matching accepted
runner baseline fails.

A manual benchmark workflow can run in `seed` mode. It packages both engine
evaluations into a combined baseline candidate while retaining already
accepted runner groups. The candidate is an artifact for review; the workflow
does not commit or accept it.

The release-candidate path copies the complete raw performance evidence into
its release evidence directory and re-evaluates both targets before reporting
success.

### Consequences

- Good, because a release must prove complete named behavior rather than the
  presence of benchmark files.
- Good, because the gate preserves samples and independently recomputes tail
  statistics.
- Good, because failures, missing targets, stale runs, noisy measurements, and
  weakened report budgets fail closed.
- Good, because a busy or ambiguously identified host fails before it can
  produce misleading latency evidence.
- Good, because absolute, historical, and sustained-resource regressions are
  separately diagnosable.
- Bad, because accepting a new runner class requires one reviewed dual-engine
  seed run before scheduled comparisons can pass.
- Bad, because the full scorecard and soak corpus is intentionally unsuitable
  for every pull request.

#### Positive

- Sync, async, compiled, retry, pooling, concurrency, size, JSON, spatial,
  migration, write, and HiLo paths share one exhaustive contract.
- Raw report hashes and source identity make accidental evidence reuse visible.
- Local and hosted runner records can coexist without pretending their latency
  distributions are directly comparable.

#### Negative

- Shared hosted runners can still produce timing variance. Relative standard
  error and runner-specific baselines detect that condition instead of hiding
  it with wider historical budgets.
- A source, runtime, engine image, or workload-contract change can require
  reviewed recalibration.

#### Neutral

- Benchmark evidence proves performance and resource contracts for the named
  workloads. It does not replace correctness, compatibility, security,
  coverage, or production telemetry evidence.

### Confirmation

- Run the Python evidence regression suite:

```bash
python3 -m unittest \
  eng.tests.test_performance_evidence \
  eng.tests.test_benchmark_ratio_gate
```

- Run a scorecard and soak pass for each engine with one stable runner class.
- Confirm each run persists a successful host preflight and matching
  BenchmarkDotNet/workload processor identity.
- Validate the accepted baseline and re-evaluate the same current run in
  compare mode.
- Run the strict cross-target gate and confirm two passes with no skipped
  target.
- Run the release-candidate path without the development-only benchmark
  bypass.

## Pros and Cons of the Options

### Versioned workload contract with dual budget layers and soak gates

- Good, because completeness, statistics, resource use, and historical drift
  are mechanically enforced.
- Bad, because the contract and accepted baselines require deliberate
  maintenance.

### BenchmarkDotNet ratios without historical baselines

- Good, because same-process ratios reduce hardware noise.
- Bad, because they cover only synthetic comparator pairs and cannot detect
  drift across the full provider workload matrix.

### Informational benchmark reports

- Good, because reports never block a workflow.
- Bad, because missing or regressed evidence can still reach publication.

## More Information

### Calibration policy

Absolute ceilings are reviewed against fresh scorecard evidence from both
required engine families. They remain broad enough for supported runner
classes but narrow enough to fail a material order-of-magnitude regression.
Historical thresholds are not widened in response to a failing run.

A baseline update requires:

1. the active contract and exact engine images;
2. one scorecard and soak evaluation per required target;
3. identical profile and runner class across the pair;
4. complete raw report hashes and source identity;
5. an explicit review of metric changes and environment metadata;
6. baseline validation after the candidate is written.

### Additional Alternative Rationale

- One cross-platform timing baseline is rejected because CPU, operating system,
  virtualization, and runtime configuration change latency distributions.
- Automatically accepting the latest successful run is rejected because it
  converts a regression into the next expected value.
- Allocation-only gates are rejected because tail latency and sustained
  resource ownership are independent failure modes.
- A long-running microbenchmark alone is rejected because cache, buffer,
  connection, and lock ownership need explicit postconditions.

### Re-evaluation Triggers

- A supported engine image, .NET runtime, BenchmarkDotNet version, or runner
  class changes.
- A workload is added, removed, renamed, or changes the provider path it
  executes.
- Two accepted runs exceed an absolute ceiling without historical regression;
  review whether the absolute runner headroom is still representative.
- A historical budget fails; diagnose the code and measurement stability
  before proposing a baseline change.
- Production telemetry identifies an important workload or resource invariant
  absent from the contract.
- A new cache, pool, lock, or process-wide retained resource is introduced.

### Decision History

- 2026-05-18: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0.
- 2026-07-29: Expanded the translation corpus and enforced its allocation
  ceiling on both engine families.
- 2026-07-30: Replaced the ratio-only gate with named workload, raw evidence,
  runner-specific baseline, absolute and historical budget, source identity,
  hard-failure, and sustained-resource contracts.
- 2026-08-02: Added fail-closed host quiescence, concrete processor identity,
  BenchmarkDotNet/workload host binding, and diagnostic-only single-workload
  reproduction.

### Implementation References

- `benchmarks/performance-contract.json`
- `benchmarks/baselines/doka-benchmark-baseline.json`
- `benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks`
- `eng/benchmark.sh`
- `eng/performance_evidence.py`
- `eng/check-benchmark-ratios.sh`
- `eng/tests/test_performance_evidence.py`
- `eng/tests/test_benchmark_ratio_gate.py`
- `.github/workflows/benchmark.yml`
- `.github/workflows/release-candidate.yml`
- `eng/release-candidate.sh`

### Sources

- [BenchmarkDotNet config options][bdn-config]
  (primary source; retrieved 2026-07-30)
- [BenchmarkDotNet diagnosers][bdn-diagnosers]
  (primary source; retrieved 2026-07-30)
- [BenchmarkDotNet JsonExporter][bdn-json]
  (primary source; retrieved 2026-07-30)
- [.NET `GC.GetTotalAllocatedBytes(Boolean)`][dotnet-allocated]
  (primary source; retrieved 2026-07-30)
- [.NET `Stopwatch.GetTimestamp()`][dotnet-stopwatch]
  (primary source; retrieved 2026-07-30)
- [MariaDB CREATE SEQUENCE][mariadb-create-sequence]
  (primary source; retrieved 2026-07-30)
- [MariaDB ALTER SEQUENCE][mariadb-alter-sequence]
  (primary source; retrieved 2026-07-30)

[bdn-config]: https://benchmarkdotnet.org/articles/configs/configoptions.html
[bdn-diagnosers]: https://benchmarkdotnet.org/articles/configs/diagnosers.html
[bdn-json]: https://benchmarkdotnet.org/api/BenchmarkDotNet.Exporters.Json.JsonExporter.html
[dotnet-allocated]:
  https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes?view=net-10.0
[dotnet-stopwatch]:
  https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch.gettimestamp?view=net-10.0
[mariadb-create-sequence]:
  https://mariadb.com/docs/server/reference/sql-structure/sequences/create-sequence
[mariadb-alter-sequence]:
  https://mariadb.com/docs/server/reference/sql-structure/sequences/alter-sequence
