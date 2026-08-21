# Performance and Memory Evidence

This runbook is the stable operator entry point for the reproducible
performance-evidence system defined by
[D-019](../decisions/D-019-performance-gate-architecture.md).

Performance evidence is independent of release qualification. Neither the
qualification phase nor protected publication in `release-candidate.yml`
invokes it or consumes its artifacts. Failed, inconclusive, missing, or stale
benchmark evidence is engineering feedback and cannot block a release.

## Choose the Right Document

| Task | Canonical owner |
|---|---|
| Run a target or diagnose a failure | This runbook |
| Interpret profiles, schemas, termination, or soak | [Performance Evidence Reference](performance-evidence-reference.md) |
| Review paired statistics, sensitivity, retries, or contract controls | [Paired Performance Methodology](paired-performance-methodology.md) |
| Accept images, seed a baseline, or operate hosted proposals | [Performance Baseline Operations](performance-baseline-operations.md) |

Every enforced measurement path retains six independent controls:

1. Persisted interval CPU admission and processor identity.
2. BenchmarkDotNet same-run controls and allocation evidence.
3. A complete named workload matrix with raw and adjacent calibration samples.
4. Absolute ceilings plus normalized historical or same-run paired budgets.
5. Sustained cache, buffer, connection, lock, memory, and throughput invariants.
6. Hard deadlines and source-bound checkpoints for verified continuation.

No single control substitutes for another.

## Prerequisites

- the repository-pinned .NET SDK;
- Docker with Compose support;
- Python 3.10 or later;
- the exact images declared in `benchmarks/performance-contract.json`; and
- a representative power and thermal state for accepted local measurements.

The wrapper samples operating-system CPU counters over one-second intervals.
Admission requires two consecutive utilization samples at or below `0.90`
within five attempts. This absorbs short build or container runoff without
accepting sustained contention. Load averages remain diagnostic evidence and
are not an admission gate.

Every workload records an adjacent control pulse. CPU-only families use a
deterministic CPU control; database families use a live `SELECT 1` round trip.
Historical latency uses workload/control ratios. A slower current control may
discount external contention, while a faster current control never amplifies a
provider result into an artificial regression.

Historical latency from different runner classes is not comparable. Baselines
match target, profile, runner class, runtime, OS, architecture, processor model,
processor count, and server image. Paired scorecards require equality within
each reference/candidate pair and never compare processors from separate runs.

## Run One Target

Run against an already available Compose target:

```bash
DOKA_BENCHMARK_TARGET=mysql84 \
DOKA_BENCHMARK_PROFILE=scorecard \
DOKA_BENCHMARK_RUNNER_CLASS=local-darwin-arm64 \
./eng/benchmark.sh --test-only
```

Start and remove the selected service around the run:

```bash
DOKA_BENCHMARK_TARGET=mariadb118 \
DOKA_BENCHMARK_PROFILE=scorecard \
DOKA_BENCHMARK_RUNNER_CLASS=local-darwin-arm64 \
./eng/benchmark.sh --up-run-down
```

The lifecycle form is `--up-run-down`; `--test-only` requires an already
available target.

The scheduled smoke reads every key from
`performance-contract.json.requiredTargets`, fans the keys through a GitHub
matrix, and gives each job an isolated lifecycle. Smoke applies only absolute
contracts and produces no accepted scorecard evidence.

The wrapper:

1. resolves one container and verifies its digest-pinned image;
2. verifies and builds current source;
3. captures the contract-owned host-admission boundary;
4. executes the named workload matrix with calibration pulses;
5. runs only BenchmarkDotNet methods referenced by the contract;
6. rejects failed, incomplete, or host-mismatched reports;
7. records the observed engine version from `SELECT VERSION()`;
8. executes soak scenarios when required;
9. evaluates statistics and budgets; and
10. writes a human-readable summary after every gate passes.

Use a new `DOKA_BENCHMARK_RUN_ID` for each run. A non-empty current-run
directory fails instead of reusing artifacts. To continue an interrupted run,
reuse the identity with `DOKA_BENCHMARK_RESUME=1`. A checkpoint is reusable only
when contract version, target, profile, commit, source hash, runner class,
workload ID, and family all match.

## Failure Triage

### BenchmarkDotNet failure

Inspect the raw log and full JSON under the current run. A missing statistics
or memory object is a failed measurement, not an empty result to ignore.

### Relative standard error failure

Check competing processes, power state, thermal throttling, container activity,
and database readiness. Do not raise historical budgets to make a noisy sample
pass. Repeated cap or precision failures are interpreted through
[Measurement Quality and Termination](performance-evidence-reference.md#measurement-quality-and-termination).

### Host preflight failure

Inspect every persisted interval sample. Stop sustained contention or wait for
it to finish. Do not override processor identity, shorten the required passing
window, or raise the admission ceiling.

### Absolute budget failure

Look for a changed algorithm, client evaluation, unexpected materialization,
disabled pooling, retry loops, or unbounded allocation. An absolute-contract
change requires fresh evidence from every required target and decision review.

### Historical budget failure

Reproduce on the same runner class. Compare samples, environment metadata,
allocation, collections, source identity, and the p99 confirmation test. A p99
point estimate alone is not a failure: two bounded confirmation populations
feed the exact test. Fix a confirmed regression. Replace a baseline only when
new behavior is intentionally accepted and documented.

For root-cause analysis, measure one exact workload without rerunning the
matrix:

```bash
dotnet artifacts/bin/Doka.EntityFrameworkCore.MySql.Benchmarks/release/\
Doka.EntityFrameworkCore.MySql.Benchmarks.dll \
  --workload <workload-id> <diagnostic-output.json>
```

Set the same `DOKA_BENCHMARK_*` identity variables used by the scorecard. The
output kind is `performance-workload-diagnostic`; the evaluator rejects it as
gate evidence. Diagnostics can explain a failure, but only a fresh complete
matrix can close it.

### Soak failure

Start with the named invariant in
[Soak Interpretation](performance-evidence-reference.md#soak-interpretation).
Verify cleanup in `finally` or disposal paths, then add a regression test for
the resource owner. Do not enlarge a limit without a reviewed bounded contract.

## Compatibility Anchors

Published links to sections of the former combined runbook remain valid here
and route to the new canonical owner.

<a id="profiles"></a>

- [Profiles](performance-evidence-reference.md#profiles)

<a id="evidence-layout"></a>

- [Evidence layout](performance-evidence-reference.md#evidence-layout)

<a id="measurement-quality-and-termination"></a>

- [Measurement quality and termination](performance-evidence-reference.md#measurement-quality-and-termination)

<a id="accept-an-engine-image-update"></a>

- [Accept an engine image update](performance-baseline-operations.md#accept-an-engine-image-update)

<a id="seed-an-accepted-baseline"></a>

- [Seed an accepted baseline](performance-baseline-operations.md#seed-an-accepted-baseline)

<a id="compare-with-the-accepted-baseline"></a>

- [Compare with the accepted baseline](performance-baseline-operations.md#compare-with-the-accepted-baseline)

<a id="hosted-runner-baseline"></a>

- [Hosted runner baseline](performance-baseline-operations.md#hosted-runner-baseline)

<a id="soak-interpretation"></a>

- [Soak interpretation](performance-evidence-reference.md#soak-interpretation)

<a id="paired-scorecard-use"></a>

- [Paired scorecard methodology](paired-performance-methodology.md)

<a id="what-the-contract-controls"></a>

- [What the contract controls](paired-performance-methodology.md#what-the-contract-controls)
