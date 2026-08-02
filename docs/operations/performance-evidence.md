# Performance and memory evidence

This runbook describes the reproducible performance gate defined by
[D-019](../decisions/D-019-performance-gate-architecture.md).

The release-qualified path has five independent controls:

1. A persisted host-quiescence and processor-identity preflight.
2. BenchmarkDotNet same-run controls and allocation evidence.
3. A complete named workload matrix with raw samples and tail statistics.
4. Absolute and runner-specific historical budgets.
5. Sustained resource invariants for caches, buffers, connections, locks,
   process memory, and concurrent throughput.

No single control substitutes for another.

## Prerequisites

- the repository-pinned .NET SDK;
- Docker with Compose support;
- Python 3.10 or later;
- the exact MySQL 8.4 and MariaDB 11.8 images declared in
  `benchmarks/performance-contract.json`;
- a stable power and thermal state for accepted local measurements.

The wrapper mechanically requires a one-minute load average no greater than
`0.40` per logical processor. After a build, it waits for at most five minutes
for this boundary. A still-busy host fails before any benchmark starts. This
preflight supplements rather than infers power and thermal stability.

Do not compare latency from different runner classes. The gate matches
baselines by target, profile, and runner class. It additionally requires an
exact match for runtime, OS, architecture, concrete processor model, processor
count, and server image. BenchmarkDotNet must report that same processor and
process architecture. A matching runner label alone is not sufficient.

## Profiles

| Profile | Purpose | Workload samples | Soak | Baseline |
|---|---|---:|---|---|
| `smoke` | Fast harness and contract check | 1 to 3 | Optional | Not required |
| `scorecard` | Release evidence | 256 | Required | Required |
| `stress` | Extended investigation | 512 | Required | Required |

Only `scorecard` and `stress` execute the complete 55-cell workload matrix.
The scorecard accepts at most 25% relative standard error; stress accepts at
most 15%. Fast, idempotent operations use fixed contract-owned batches so
timer resolution and loop overhead cannot dominate per-operation tail
statistics. Tail outliers remain in p95 and p99 and must pass their independent
absolute and historical budgets.

## Run one target

Run against an already available Compose target:

```bash
DOKA_BENCHMARK_TARGET=mysql84 \
DOKA_BENCHMARK_PROFILE=scorecard \
DOKA_BENCHMARK_RUNNER_CLASS=local-darwin-arm64 \
./eng/benchmark.sh --test-only
```

Start and remove the selected Compose service around the run:

```bash
DOKA_BENCHMARK_TARGET=mariadb118 \
DOKA_BENCHMARK_PROFILE=scorecard \
DOKA_BENCHMARK_RUNNER_CLASS=local-darwin-arm64 \
./eng/benchmark.sh --up-run-down
```

The wrapper:

1. resolves exactly one container on the target port and verifies its
   digest-pinned image;
2. verifies and builds the current source;
3. waits for and persists the contract-owned host-quiescence boundary;
4. runs all BenchmarkDotNet benchmarks;
5. rejects failed, incomplete, or host-mismatched BDN reports;
6. executes the named workload matrix and records `SELECT VERSION()`;
7. executes soak scenarios when the profile requires them;
8. evaluates statistics and budgets;
9. writes a human-readable summary only after every gate passes.

Use a new `DOKA_BENCHMARK_RUN_ID` for every run. A non-empty current-run
directory fails instead of reusing old artifacts.

## Evidence layout

Each target writes:

```text
artifacts/benchmarks/<target>/reports/<run-id>/
|-- evidence/
|   |-- benchmarkdotnet-evidence.json
|   |-- host-preflight.json
|   |-- performance-evaluation.json
|   |-- performance-summary.md
|   |-- soak-evidence.json
|   `-- workload-evidence.json
`-- results/
    `-- *-report-full.json
```

`host-preflight.json` records the concrete processor, logical processor count,
one-, five-, and fifteen-minute load averages, the normalized one-minute load,
and its contract ceiling. `benchmarkdotnet-evidence.json` records a SHA-256
digest for every raw BDN report. `performance-evaluation.json` hashes the host
preflight, contract, workload report, BDN evidence, and soak report.

The workload report contains both the Git commit and a source hash. The source
hash covers `HEAD`, tracked modifications, and untracked source files while
excluding only the generated baseline file. It therefore identifies the exact
working tree measured during review.

The server image is evidence only after the wrapper compares the live
container's configured image or repository digest with the contract. The
reported database version is read from the server and must identify the
expected engine family and major/minor line.

## Seed an accepted baseline

Seeding is a review action, not a regression-recovery shortcut.

Run both required targets in `seed` mode with the same profile and runner
class:

```bash
DOKA_BENCHMARK_TARGET=mysql84 \
DOKA_BENCHMARK_PROFILE=scorecard \
DOKA_BENCHMARK_BASELINE_MODE=seed \
DOKA_BENCHMARK_RUNNER_CLASS=local-darwin-arm64 \
DOKA_BENCHMARK_RUN_ID=local-seed-mysql84 \
./eng/benchmark.sh --test-only

DOKA_BENCHMARK_TARGET=mariadb118 \
DOKA_BENCHMARK_PROFILE=scorecard \
DOKA_BENCHMARK_BASELINE_MODE=seed \
DOKA_BENCHMARK_RUNNER_CLASS=local-darwin-arm64 \
DOKA_BENCHMARK_RUN_ID=local-seed-mariadb118 \
./eng/benchmark.sh --test-only
```

Create the candidate only after reviewing both evaluations:

```bash
python3 eng/performance_evidence.py seed \
  --contract benchmarks/performance-contract.json \
  --baseline benchmarks/baselines/doka-benchmark-baseline.json \
  --version <reviewed-baseline-version> \
  --evidence \
    artifacts/benchmarks/mysql84/reports/local-seed-mysql84/evidence/performance-evaluation.json \
  --evidence \
    artifacts/benchmarks/mariadb118/reports/local-seed-mariadb118/evidence/performance-evaluation.json
```

When adding a new runner class, retain existing accepted groups:

```bash
python3 eng/performance_evidence.py seed \
  --contract benchmarks/performance-contract.json \
  --baseline artifacts/doka-benchmark-baseline.candidate.json \
  --version <reviewed-baseline-version> \
  --merge-existing benchmarks/baselines/doka-benchmark-baseline.json \
  --evidence <mysql-evaluation.json> \
  --evidence <mariadb-evaluation.json>
```

Validate the result before review:

```bash
python3 eng/performance_evidence.py validate-baseline \
  --contract benchmarks/performance-contract.json \
  --baseline benchmarks/baselines/doka-benchmark-baseline.json \
  --output artifacts/baseline-validation.json
```

The seed command rejects a missing target, duplicate target, incomplete
workload matrix, failed evaluation, wrong contract version, or malformed
existing baseline. It replaces only matching target/profile/runner tuples.

## Compare with the accepted baseline

Run both targets with `DOKA_BENCHMARK_BASELINE_MODE=compare`, then enforce the
cross-target boundary:

```bash
DOKA_BENCHMARK_PROFILE=scorecard \
DOKA_BENCHMARK_GATE_STRICT=1 \
DOKA_BENCHMARK_GATE_RUN_ID=<run-id> \
bash eng/check-benchmark-ratios.sh artifacts/benchmarks
```

The strict gate exits:

- `0` when both targets pass;
- `1` when current evidence or a budget fails;
- `2` when a required target has no current-run evidence.

Historical evidence outside the selected run ID cannot satisfy the gate.

## Hosted runner baseline

The manual `benchmark` workflow accepts `baseline_mode=seed`. Both engine jobs
must succeed before a final job packages
`benchmark-baseline-candidate`. The candidate contains the existing accepted
runner groups plus the new `github-ubuntu-latest-x64` pair.

Download and review:

- source commit and source hash;
- exact server images;
- runtime, OS, CPU, architecture, and processor count;
- median, p95, p99, relative standard error, allocation, retained bytes, and
  collection counts;
- absolute and soak verdicts;
- raw report SHA-256 hashes.

The workflow never commits the candidate. Until its runner pair is explicitly
accepted in the repository baseline, scheduled scorecards and
release-candidate runs fail closed for that runner class.

## Soak interpretation

The soak report is accepted only when all six scenario rows exist and the gate
can recompute their verdicts from exact metric names:

- `soak.hilo-cache-bound`: the provider cache stays within capacity;
- `soak.pooled-buffer-return`: every array rent has a matching return;
- `soak.connection-cleanup`: physical connection count returns to its allowed
  delta;
- `soak.migration-lock-cleanup`: the provider advisory lock has no owner;
- `soak.working-set-stabilization`: working set and managed heap stay within
  growth limits;
- `soak.concurrent-throughput-retention`: the final window retains the
  contracted fraction of initial throughput.

A report cannot weaken its own limit. Reported budget fields must equal the
checked-in contract before the success flag is considered.

## Failure triage

### BenchmarkDotNet failure

Inspect the raw log and full JSON under the current run. A missing statistics
or memory object is a failed measurement, not an empty result to ignore.

### Relative standard error failure

Treat excessive noise as invalid evidence. Check competing processes, power
state, thermal throttling, container activity, and database readiness. Do not
raise historical budgets to make a noisy sample pass.

### Host preflight failure

Stop CPU-heavy applications or wait for unrelated work to finish. The failed
preflight remains in the run directory with the observed load values. Do not
override the processor model or raise the load ceiling to admit a busy run.

### Absolute budget failure

Look for a changed algorithm, accidental client evaluation, unexpectedly
materialized rows, disabled pooling, retry loops, or unbounded allocation.
Changing the absolute contract requires fresh evidence from both engines and a
decision review.

### Historical budget failure

Reproduce on the same runner class. Compare workload samples, environment
metadata, allocation, collections, and source identity. Fix a confirmed
regression. Replace a baseline only when the new behavior is intentionally
accepted and documented.

For root-cause analysis, the benchmark executable can measure one exact
contract workload without rerunning the complete matrix:

```bash
dotnet artifacts/bin/Doka.EntityFrameworkCore.MySql.Benchmarks/release/\
Doka.EntityFrameworkCore.MySql.Benchmarks.dll \
  --workload <workload-id> <diagnostic-output.json>
```

Set the same `DOKA_BENCHMARK_*` identity variables used by the scorecard before
running the command. The output kind is
`performance-workload-diagnostic`; the evaluator deliberately rejects it as
release evidence. A diagnostic result can explain a failure, but only a fresh
complete matrix can close the gate.

### Soak failure

Start with the named invariant. Verify cleanup in `finally` or disposal paths,
then add a regression test for the resource owner. Do not mask the result with
a larger limit unless a reviewed contract change establishes a new bounded
requirement.

## Release-candidate use

`eng/release-candidate.sh` runs both engine scorecards, re-evaluates the strict
cross-target gate, and copies the raw report trees into:

```text
artifacts/release-candidate/<run-id>/performance/
```

`DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS=1` is a development-loop bypass. Any
evidence produced with that bypass is not release eligible.
