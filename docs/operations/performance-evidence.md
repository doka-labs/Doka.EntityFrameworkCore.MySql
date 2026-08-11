# Performance and memory evidence

This runbook describes the reproducible performance gate defined by
[D-019](../decisions/D-019-performance-gate-architecture.md).

Every enforced path has six independent controls:

1. A persisted interval host-CPU admission and processor-identity preflight.
2. BenchmarkDotNet same-run controls and allocation evidence.
3. A complete named workload matrix with raw and adjacent calibration samples.
4. Raw absolute ceilings plus either calibration-normalized historical budgets
   or a same-run paired practical budget.
5. Sustained resource invariants for caches, buffers, connections, locks,
   process memory, and concurrent throughput.
6. Hard deadlines and source-bound checkpoints for safe interruption and
   verified continuation.

No single control substitutes for another.

## Prerequisites

- the repository-pinned .NET SDK;
- Docker with Compose support;
- Python 3.10 or later;
- the exact MySQL 8.4 and MariaDB 11.8 images declared in
  `benchmarks/performance-contract.json`;
- a representative power and thermal state for accepted local measurements.

The wrapper samples aggregate operating-system CPU counters over one-second
intervals before measurement. Admission requires two consecutive utilization
samples at or below `0.90` within at most five attempts. This bounded window
absorbs short build or container runoff without accepting sustained host
contention. It does not require an idle workstation or gate on Unix load
average because load averages can include unrelated runnable work. Load
averages remain diagnostic evidence.
Every workload records an adjacent control pulse: CPU-only families use a
deterministic CPU control, while database families use a live `SELECT 1`
round-trip. Historical latency comparisons use workload/control ratios, so
ordinary contention affecting both paths does not appear as provider drift.
The adjustment is deliberately one-sided: a slower current control can
discount external contention, while a faster current control never amplifies
the provider metric into an artificial regression.

Do not compare historical latency from different runner classes. The scorecard
matches baselines by target, profile, and runner class. It additionally
requires an exact match for runtime, OS, architecture, concrete processor
model, processor count, and server image. BenchmarkDotNet must report that same
processor and process architecture. A matching runner label alone is not
sufficient. Release qualification instead requires environment equality
inside each reference-and-candidate pair and never compares processors from
different runs.

## Profiles

| Profile | Purpose | Workload samples | Soak | Baseline |
|---|---|---:|---|---|
| `smoke` | Fast harness and contract check | 1 to 3 | Optional | Not required |
| `paired-block` | One block of a paired comparison | starts at 16, extended on precision up to 64x | Required once per run | Not required |
| `scorecard` | Hosted historical evidence | 256; 128 expensive, adaptively extended up to 64x | Required | Required |
| `stress` | Extended investigation | 512; 256 expensive, adaptively extended up to 64x | Required | Required |

`paired-block` needs no baseline because a paired run carries its own
reference, and it observes rather than enforces the per-block error budget:
the run's precision comes from many blocks, not from one. A block that is so
noisy its ratio would be meaningless is still rejected, per side, against
`pairedPolicy.blocks.maximumRelativeStandardError`.

It measures the complete workload matrix -- the same fifty-five workloads the
scorecard measures -- and starts each workload at the smallest population the
profile accepts, extending only until the registered error budget is met. The
two sides of a block therefore need not reach the same population: extension
equalizes precision, not count, and a noisier side needs more samples to reach
the same budget. A real one-block run diverged on sixteen of fifty-five
workloads. A fixed larger population would spend the same wall clock on a
workload that converged in a quarter of it, and a paired run pays that cost
twice per block.
The achieved population travels in each workload report.

### Deadlines are error bounds, not budgets

Four watchdogs nest, and none of them reserves time for the next:

| Watchdog | Bounds |
|---|---|
| Workload | One hanging workload |
| Matrix | One complete side run |
| Paired run | The whole comparison, independent of the inner maxima |
| Job timeout | The forge's emergency stop, with room left for cleanup |

A side run stops at `min(side watchdog, remaining paired-run budget)`, so
staying inside the local watchdog does not by itself make a run valid. Before
each further block the runner forecasts from the blocks it has already measured
and stops early when the remaining budget cannot hold another one plus the
closing work: the sustained-use run, the evidence assembly, and the evaluation
all sit inside the same deadline. The outer watchdog is translated as well --
its own timeout code would otherwise be filed as invalid evidence, which no
retry can clear. Every deadline stop therefore reports
`measurement-inconclusive`: a measurement condition, retryable, and never a
verdict about the provider.

`paired-block`, `scorecard`, and `stress` execute the complete 55-cell
workload matrix; only `smoke` narrows it.
Expensive cells retain at least 100 observations for p99 while avoiding a
second full population of large writes. The scorecard accepts at most 25%
relative standard error; stress accepts at most 15%. An enforcing workload that
misses its error budget is extended in calibration-aligned blocks up to the
contract-owned multiplier. The workload and matrix deadlines bound that
extension, and they are the real ceiling: the multiplier only keeps a single
workload from consuming the matrix budget. Its value is dimensioned against
the accepted baseline of every required target, because engines differ in how
many samples the same workload needs:

| Target | `scorecard` | `stress` |
|---|---:|---:|
| MariaDB 11.8 | 26x | 33x |
| MySQL 8.4 | 27x | 34x |

Those are observations, not constants. The same workload measured twice on the
same commit has needed up to 1.9x the earlier population, because how many
samples fit inside the duration floor depends on how fast the host answers that
day. The multiplier therefore carries that spread on top of the largest
observed demand rather than sitting just above it: 34x observed, 1.9x spread,
64x configured.

A multiplier below that discards measurements whose precision is well inside
the target, because a workload stopped at the cap has by definition missed the
duration floor. A contract test asserts the cap against every required target's
baseline so the two cannot drift apart again. When that test fails after a
baseline update, the run-to-run spread has outgrown the headroom, and the
multiplier is what moves.
The runner never weakens the error budget or deletes observations;
evidence that remains unstable at the cap fails validation. Fast, idempotent
operations use fixed contract-owned batches so timer resolution and loop
overhead cannot dominate per-operation tail statistics. Tail outliers remain
in raw p95 and p99 and must pass their independent absolute budgets. Normalized
p95 must pass its matching historical point budget. A normalized p99 point
estimate above its historical threshold triggers two bounded confirmations.
The triggering population is excluded from the verdict. The two independent
confirmation populations fail only when an exact one-sided binomial tail test
establishes an exceedance rate above one percent at the one-percent
significance level. Smoke, scorecard, and stress have hard total deadlines of
10 minutes, 30 minutes, and two hours respectively.

The profile workload deadline is a hang detector, not a performance budget.
Every expensive workload references a named entry from `timeoutPolicies`.
The runner uses the larger of that policy's floor and the profile deadline,
while the contract validator rejects missing, unknown, unused, non-positive,
or matrix-breaking policies. This does not alter the sample population,
absolute budgets, normalized historical budgets, allocation limits, or GC
limits.

The fixed 10,000-row synchronous and asynchronous `SaveChanges` populations
use a 300-second floor. Their scorecard population still contains 128
independent observations. The large synchronous and asynchronous HiLo
populations use the `hilo-contention` policy and its 240-second floor. The
remaining expensive workloads share the 180-second `expensive-standard`
policy. These centralized declarations keep host scheduling and database
cleanup inside the hang deadline without turning the deadline into a latency
budget.

HiLo insert workloads track every entity before issuing one `SaveChanges` or
`SaveChangesAsync` call per context. EF Core assigns HiLo values while entities
enter the change tracker, so this transaction boundary preserves shared HiLo
allocation and provider batching while excluding artificial per-row commit
latency. The workload reports the number of rows actually persisted and fails
if it differs from the declared population.

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
3. captures and persists the contract-owned host-admission boundary;
4. executes the named workload matrix with adjacent calibration pulses;
5. runs only the BenchmarkDotNet methods referenced by the checked-in control
   contract;
6. rejects failed, incomplete, or host-mismatched BDN reports;
7. records the observed engine version from `SELECT VERSION()`;
8. executes soak scenarios when the profile requires them;
9. evaluates statistics and budgets;
10. writes a human-readable summary only after every gate passes.

Use a new `DOKA_BENCHMARK_RUN_ID` for every run. A non-empty current-run
directory fails instead of reusing old artifacts. To continue an interrupted
run, use the same identity with `DOKA_BENCHMARK_RESUME=1`. Completed workload
checkpoints are accepted only when contract version, target, profile, commit,
source hash, runner class, workload ID, and family all match. Incomplete
BenchmarkDotNet output is archived before that phase restarts.

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

artifacts/benchmarks/<target>/checkpoints/<run-id>/
`-- <workload-id-sha256>.json
```

`host-preflight.json` records the concrete processor, logical processor count,
one-, five-, and fifteen-minute load averages, operating-system counter source,
sampling interval, every interval utilization sample, required consecutive
passes, attempts, admitted utilization, and its admission ceiling.
`benchmarkdotnet-evidence.json` records a SHA-256 digest for every raw BDN
report. `performance-evaluation.json` hashes the host preflight, contract,
workload report, BDN evidence, and soak report.

Each workload row contains raw samples, calibration pulse samples, the pulse
index used by every workload sample, normalized samples, and recomputed raw and
normalized statistics. A calibration pulse reused for several adjacent
workload samples remains one observation for calibration-error calculations.
Raw retained-heap delta is persisted as a diagnostic, not used as a workload
budget: it is process-global and can include unrelated finalization or runtime
activity. Retained-memory correctness is enforced by the sustained working-set
and managed-heap soak invariant.

The workload report contains both the Git commit and a source hash. The source
hash covers `HEAD`, tracked modifications, and untracked source files while
excluding only the generated baseline file. It therefore identifies the exact
working tree measured during review.

The server image is evidence only after the wrapper compares the live
container's configured image or repository digest with the contract. The
reported database version is read from the server and must identify the
expected engine family and major/minor line.

## Measurement quality and termination

Each workload declares how its sampling ended. The runner never exceeds the
configured sample cap, so a run that hits the cap is a complete measurement of
a contract that does not fit the workload -- not a corrupt one.

| Field | Values | Meaning |
| --- | --- | --- |
| `terminationReason` | `precision_reached` | The relative standard error met the contract, and the minimum duration was satisfied. |
| | `sample_cap_reached` | The cap bound first while the duration or the precision target was still unmet. |
| `minimumDurationReached` | `true` / `false` | Whether the contract's `minimumMeasurementDurationMilliseconds` was satisfied. |

Both fields are required and were introduced with **schema version 4** of the
raw workload report (`kind: performance-workloads`). A version-3 document
predates them; the validator rejects it with an explicit unsupported-version
message rather than reporting it as incomplete. Re-measure with the current
benchmark build to produce a version-4 report. The accepted baseline and the
evaluation record keep their own schema versions.

The validator derives the allowed population from the contract as
`measurementSamples` (or the workload's own override) multiplied by
`maximumMeasurementSampleMultiplier`, then rejects every state the runner
cannot emit. Each of these is a hard `PerformanceEvidenceError`, not a quality
verdict:

- a sample count above the derived cap;
- `sample_cap_reached` with a population below the cap;
- `sample_cap_reached` although both the duration and the precision target
  were met;
- `precision_reached` with a relative standard error above the ceiling;
- an unreached minimum duration with any reason other than
  `sample_cap_reached`;
- a measurement shorter than the minimum duration without sitting at the cap;
- `minimumDurationReached` disagreeing with the duration computed from the
  samples and `operationsPerSample`;
- an unknown termination reason.

The comparisons against the error ceiling are tolerant, because the runner and
the validator compute the statistic independently; only a clear contradiction
counts as corrupt.

### What each policy does with a capped result

`measurementQualityPolicy` is a profile setting.

The trigger is the termination reason, not the duration flag. A run that met
its duration and still exhausted its budget chasing precision is exactly as
unusable, so it reaches the same verdict.

- **`enforce`** (scorecard, stress): a capped result exits with code 75. That
  code means "valid run, required measurement quality not achieved", which is
  what makes it retryable -- the scorecard workflow grants one bounded retry on
  a fresh hosted runner. A hard validation error is a different class and is
  never retried.
- **`observe`** (smoke): the evidence is published together with a diagnostic.
  The run does not fail.

Either way the outcome produces exactly one verdict, and the diagnostic carries
three pairs so no rerun is needed to decide what to change: samples achieved
against the derived cap, measured duration against the required minimum, and
achieved relative standard error against the allowed ceiling.

Under either policy a capped result is refused for baseline promotion. Seeding
rejects it by workload id and names the recalibration levers.

### Environmental noise or an unsuitable contract

A capped result on one run and a clean result on the retry is host noise; that
is what the bounded retry exists for. The same workload capping run after run
is not noise -- it says the contract cannot be satisfied on this hardware.

The diagnostic prints all three pairs listed above -- samples against the
derived cap, measured duration against the required minimum, and achieved
relative standard error against the allowed ceiling -- so the decision needs no
rerun. Read which pair is the outlier, then recalibrate exactly one of
`operationsPerSample` (more work per sample reaches the duration without more
samples), the minimum duration, or the cap.
Raising the cap alone treats the symptom; more work per sample is usually the
correct lever, and it is what BenchmarkDotNet's pilot stage and Go's `predictN`
solve for.

Recalibration is a reviewed contract change with its own version bump. It is
never applied automatically from a failing run.

## Accept an engine image update

Dependabot proposes engine images against the Compose stack, which is the one
place it edits. The same pin also lives in two workflows, the performance
contract, and a C# constant, so its pull request is incomplete by
construction and the pin gate rejects it until the other copies follow:

```bash
gh pr checkout <number>
python3 eng/quality/check-image-pins.py --fix
```

A new image is a new measurement environment, so the contract needs a new
`contractVersion` in the same change. The accepted baseline was taken against
the previous image and stays as it is; the next hosted run reseeds and opens
its own review.

Proposals arrive monthly rather than weekly, because these images are rebuilt
whenever their base picks up patches, without any change to the engine
version. Each accepted rebuild costs a benchmark run, so they are batched. A
published vulnerability is handled when it is published and does not wait for
that cadence.

An update that leaves one of the supported MySQL 8.4 / 9.7 or MariaDB 10.11 /
11.4 / 11.8 / 12.3 release lines is not an image update but a support decision,
with its own specification matrix and baseline work. The pin gate rejects it
even when every copy agrees.

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
python3 -m eng.performance.cli seed \
  --contract benchmarks/performance-contract.json \
  --baseline benchmarks/baselines/doka-benchmark-baseline.json \
  --version <reviewed-baseline-version> \
  --evidence \
    artifacts/benchmarks/mysql84/reports/local-seed-mysql84/evidence/performance-evaluation.json \
  --evidence \
    artifacts/benchmarks/mariadb118/reports/local-seed-mariadb118/evidence/performance-evaluation.json
```

Every evaluation handed to `seed` or `compare` must agree on profile, runner
class, commit, and source hash: promotion accepts one measured state of one
piece of software, not a set assembled from several. The run identifier is
deliberately not part of that agreement. It names a single measurement job, so
the two commands above give each target its own, and the hosted matrix does the
same with one job per engine. The contract's `evidenceMaximumAgeHours` keeps
the evaluations close together in time.

When adding a new runner class, retain existing accepted groups:

```bash
python3 -m eng.performance.cli seed \
  --contract benchmarks/performance-contract.json \
  --baseline artifacts/doka-benchmark-baseline.candidate.json \
  --version <reviewed-baseline-version> \
  --merge-existing benchmarks/baselines/doka-benchmark-baseline.json \
  --evidence <mysql-evaluation.json> \
  --evidence <mariadb-evaluation.json>
```

Validate the result before review:

```bash
python3 -m eng.performance.cli validate-baseline \
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
DOKA_BENCHMARK_GATE_RUN_ID=<run-id> \
bash eng/performance/check-benchmark-ratios.sh artifacts/benchmarks
```

The gate exits:

- `0` when both targets pass;
- `1` when current evidence or a budget fails;
- `2` when a required target has no current-run evidence.

Missing evidence fails by default, so no caller can inherit a permissive gate
by omitting a variable. A local run that deliberately measures one engine sets
`DOKA_BENCHMARK_GATE_ALLOW_MISSING=1`; even then the gate refuses to report
success when it evaluated no target at all.

Historical evidence outside the selected run ID cannot satisfy the gate.

## Hosted runner baseline

Any edit to the contract needs a new `contractVersion`. The resolver compares
that version first: a different one means the accepted baseline belongs to an
earlier contract and is reseeded, an equal one means it belongs to this
contract and is validated against its bytes. Editing the contract without
advancing the version therefore does not reuse the baseline, it fails the run
before any measurement starts, because the stored evidence no longer matches
the contract it claims.

Versions are dated. A second revision on the same day appends a counter, as in
`2026-08-09.2`, rather than borrowing a date the revision does not belong to.
The accepted baseline keeps the version it was measured under until a hosted
run produces a reviewed replacement.

The `benchmark` workflow resolves its baseline mode and required work before
starting services or either expensive matrix job:

- `.github/workflows/benchmark.yml` and
  `eng/performance/workflow_state.py` form the inexpensive control plane;
- control-plane-only changes run the resolver but do not allocate database
  services or benchmark runners;
- the shared release-evidence classifier treats provider source, benchmark
  source and corpora, database images, build and SDK inputs, the evaluator,
  the harness, and `.github/workflows/benchmark-scorecard.yml` as scorecard
  inputs;
- a `main` push that changes any scorecard input runs both the MySQL and
  MariaDB scorecards;
- changes confined to the parent workflow, resolver, documentation, tests, or
  accepted baseline output remain on the inexpensive resolver path;
- an exact current-contract `github-ubuntu-latest-x64` pair selects `compare`;
- a missing baseline, an older contract, or a missing runner pair selects
  `seed`;
- malformed or partial current-contract evidence fails before the matrix;
- monthly and manual runs always request fresh scorecard evidence;
- manual runs default to `auto`; select `seed` only for an intentional
  recalibration of the accepted baseline;
- `compare` evidence is immutable run evidence and never creates, updates, or
  synchronizes a baseline proposal;
- a current and up-to-date seed proposal is a no-op;
- a current proposal behind only unrelated `main` changes is synchronized
  without another scorecard; and
- proposal health cannot make an unrelated push allocate a scorecard, while a
  relevant push, monthly run, or manual run replaces invalid or stale proposal
  evidence on the same automation branch.

An automation branch that changes any path other than the canonical baseline
fails in the resolver before the scorecard matrix starts. Remove or review the
unexpected branch state explicitly; automation does not overwrite it. Later
`main` pushes queue behind a running scorecard instead of cancelling evidence
that is already being collected.

Each engine scorecard writes a typed attempt receipt. A successful attempt is
selected immediately. A measurement-quality result, reported with exit code
`75`, permits exactly one retry in a new hosted job with a fresh database
service. Any other non-zero exit is a hard failure and is never retried. A
second measurement-quality result also fails the scorecard; retrying cannot
turn a functional, budget, contract, or infrastructure failure into a pass.

The selector verifies the identity and digests of every attempt before it
copies the selected report tree into the stable engine artifact. Those
historical artifacts belong only to the `benchmark` workflow. Release
qualification invokes the same reusable workflow in `paired` mode, which
measures a reference and the candidate on the tagged commit's allocated runner
and produces a separate paired artifact. It neither imports nor reclassifies a
historical baseline.

Both engine jobs must succeed before a seed run can propose a baseline update.
A seed still enforces the complete workload, absolute budgets, statistical
integrity, allocation, GC, soak, environment, and host-admission contracts. It
omits only a historical comparison that cannot exist yet. A contract revision
deliberately does not carry older contract groups into the proposal.

After validation, the seed candidate is compared with the accepted baseline
through a canonical semantic projection. Run identifiers, timestamps, source
hashes, artifact hashes, and transient host-admission measurements remain in
the immutable evidence but cannot create or update a pull request by
themselves. Workloads, statistics, budgets, stable environment descriptors,
and enforcement controls remain part of the accepted contract. Only a change
to that contract writes the canonical baseline on the automation branch and
opens or updates its pull request. Proposal state is inspected and
synchronized only for seed work.

The workflow enables squash auto-merge for a semantic baseline proposal but
never approves it. The normal operator path is therefore:

1. Review the baseline diff and the linked benchmark run.
2. Confirm that `quality-gates`, `repo-tests`, and `integration-smoke` passed
   for the current proposal head.
3. Approve the current pull-request revision.
4. Let GitHub squash-merge the proposal after every protected check passes.

The proposal and linked evidence expose the following review inputs:

- source commit and source hash;
- exact server images;
- runtime, OS, CPU, architecture, and processor count;
- raw and normalized median, p95, p99, relative standard error, calibration
  stability, allocation, retained-byte diagnostics, and collection counts;
- absolute and soak verdicts;
- raw report SHA-256 hashes.

Branch updates made with `GITHUB_TOKEN` do not recursively start the normal
push or pull-request workflows. After creating or synchronizing the proposal,
the benchmark workflow therefore dispatches the trusted `baseline-proposal`
CI profile on the exact automation-branch head. That restricted profile runs
only the protected `quality-gates`, `repo-tests`, and `integration-smoke`
checks; all expensive scheduled and full-dispatch jobs are skipped. The
checks bind the merge decision to the reviewed revision without requiring a
manual workflow approval, baseline artifact download, or Run-ID handoff.

Repository administrators must enable **Allow GitHub Actions to create and
approve pull requests** under **Settings > Actions > General > Workflow
permissions**. The workflow consumes the pull-request creation and auto-merge
capabilities but contains no review, approval, or administrative-bypass
command. The active ruleset therefore continues to require an independent
maintainer approval and all protected checks. The restricted CI dispatch uses
only the workflow's ephemeral `GITHUB_TOKEN`; no PAT, repository secret, or
external application is required. This repository setting and its security
implications are documented by GitHub's
[Actions policy reference][github-actions-policy].

The historical workflow uses strict `compare` mode only when the accepted
runner pair is present. A missing or stale pair enters the reviewed seed path,
but that state is not a release precondition. Release qualification is decided
exclusively by the paired same-run evidence described below.

This design follows GitHub's documented `GITHUB_TOKEN` event behavior,
approval-gated workflow-run contract, and latest-commit status-check contract.
The primary sources were retrieved on 2026-08-07:

- [Automatic token authentication][github-token-authentication]
- [Triggering a workflow][github-workflow-events]
- [Troubleshooting required status checks][github-required-checks]
- [Skipping workflow runs][github-skipped-workflows]

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

The host did not produce two consecutive acceptable interval samples within
five attempts. Inspect every sample in the persisted preflight, then stop the
process that owns sustained contention or wait for it to finish. Do not
override the processor model, reduce the required passing window, or raise the
ceiling to admit a saturated run.

### Absolute budget failure

Look for a changed algorithm, accidental client evaluation, unexpectedly
materialized rows, disabled pooling, retry loops, or unbounded allocation.
Changing the absolute contract requires fresh evidence from both engines and a
decision review.

### Historical budget failure

Reproduce on the same runner class. Compare workload samples, environment
metadata, allocation, collections, source identity, and the persisted p99 tail
test. A p99 point estimate alone is not a failure: the runner automatically
collects two bounded confirmation populations before applying the exact test.
Fix a confirmed regression. Replace a baseline only when the new behavior is
intentionally accepted and documented.

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
gate evidence. A diagnostic result can explain a failure, but only a fresh
complete matrix can close the gate.

### Soak failure

Start with the named invariant. Verify cleanup in `finally` or disposal paths,
then add a regression test for the resource owner. Do not mask the result with
a larger limit unless a reviewed contract change establishes a new bounded
requirement.

## Release-candidate use

The release tag measures performance itself, once, as a paired comparison. It
does not import an accepted baseline and does not compare against a run from
another machine.

### What a paired run measures

A reference and the candidate provider revision are measured alternately on one
allocated runner. The reference provider is packed from its reference commit
and bound into the candidate benchmark driver, so only the provider differs
between the two sides. Because both sides share the processor, the runtime, the
engine image, and the database preparation, the machine cancels out of every
ratio instead of having to be matched.

The order is counterbalanced: the side that measures first alternates between
blocks, so any warm-up advantage cancels across the run rather than accruing to
one provider.

### What decides the run

| Check | What it answers | Failure means |
|---|---|---|
| Latency families | Does the candidate exceed its practical budget on median, p95, or p99 | The interval sits above the registered budget |
| Resource families | Does the candidate allocate or collect more than its reference | The median block ratio exceeds the registered budget |
| Absolute ceilings | Is the candidate inside its family budgets at all | A pair that regressed together would otherwise qualify |
| Soak | Does sustained use leak | A leak appears over thousands of iterations and never inside a block |

The latency verdict separates statistical detectability from practical impact:
a change the family procedure can detect is a regression only when the interval
also lies outside the reviewed budget. Between the two, the run is
`inconclusive`, which withholds qualification without asserting a regression.

Multiple comparison is controlled across the workload matrix with the
Benjamini-Hochberg procedure, so running one test per workload does not produce
false alarms in proportion to the matrix size.

### Reruns and retries

An attempt receipt classifies the run into one of six states and carries
whether a retry is permitted. Only `measurement-inconclusive` and
`environment-not-comparable` are retryable: a retry may not select away a
verdict about the code. The workflow reads that field rather than comparing
against a state name, so the retry policy has exactly one home.

### Evidence layout

A paired run writes into the report directory the attempt machinery already
collects. The recorder is told which comparison produced the verdict and holds
it to that contract: a paired attempt binds `paired-evaluation.json` together
with the measurements and the sustained-use report behind it, and selection
re-checks every one of those digests. Two attempts that measured the same
commit in different ways cannot be mixed.

```text
artifacts/benchmarks/<target>/reports/<run-id>/paired-evidence.json
artifacts/benchmarks/<target>/reports/<run-id>/paired-evaluation.json
artifacts/benchmarks/<target>/reports/<run-id>/paired-soak.json
artifacts/benchmarks/<target>/reports/<run-id>/execution-order.json
artifacts/benchmarks/<target>/reports/<run-id>/blocks/
```

`execution-order.json` records the order the run followed as it ran, one entry
per block, and `blocks/` holds every per-side measurement and the driver
identity behind it. The evaluator compares the recorded order against the
registered patterns and refuses a run that deviated, so the counterbalancing
is proven rather than documented.

Build inputs -- the reference worktree, the local package feed, and the two
published drivers -- stay under `artifacts/paired/<target>/<run-id>/` so no
consumer has to skip past them.

The release candidate copies this tree into
`artifacts/release-candidate/<run-id>/performance/<target>/` and binds every
file in it by digest. Benchmark artifacts expire in days; the candidate is
retained for ninety, and a performance claim nobody can re-derive after its
inputs expire is not evidence.

### What the contract controls

Every value the paired policy registers has something that reads it, and a
value nothing compares against would describe nothing:

| Registered value | What reads it |
|---|---|
| `blocks.profile`, `blocks.startingSamplesPerSidePerBlock` | The contract refuses a policy whose starting population differs from the profile that measures it, or falls below its valid-sample floor |
| `blocks.maximumSampleCountRatio` | Applied per block: the two sides may reach different populations, but not populations far enough apart to have measured different stretches of time |
| `interval.method`, `multipleComparison.procedure`, `retry.combination`, `primaryFamily.workloadScope` | Each admits only the procedure the evaluator performs; a policy naming another is refused |
| `executionOrder.blockPatterns` | The runner takes each block's order from the list, records what it executed, and the evaluator refuses an order that deviates or never alternates the starting side |
| `retry.eligibleAttemptStates`, `retry.maximumRetries` | The contract refuses a policy that disagrees with the states and bound the attempt recorder implements; the receipt carries the resulting decision, which the workflow condition reads |
| `durations.closingReserveSeconds` | Withheld from every side watchdog and from the block forecast, so the closing work always has room |
| `durations.finalizationReserveSeconds` | Withheld again from the sustained-use run, so assembling and evaluating the evidence keep their share of the closing reserve |
| `blocks.profile` (termination) | The runner's own convergence verdict travels per side per block; a workload that stopped at its sample cap is withheld from its metric family entirely, so its unusable p-value cannot move the false-discovery threshold for the workloads that did converge |
| The canonical workload contract | Every raw block report passes it before assembly: a foreign schema or kind, an impossible termination, a count that contradicts its samples, and any statistic that does not follow from the persisted samples are all invalid evidence |
| The audit projection's shape | The candidate summary carries exactly seven fields -- the workload, its family, and the five metrics the ceilings decide on -- and one function produces it for the runner and the fixtures alike. Anything more is refused: passing the workload report through put a second, unchecked copy of every sample, calibration and pulse into the document beside the canonical one, so a reader could find numbers there that no decision used |
| The calibration's origin | The divisor of each sample is not read from the evidence, it is rebuilt from the calibration pulses the run recorded and the pulse each sample was measured against, under the invariants the workload report is held to at measurement time: the train starts at the first pulse, advances one pulse at a time, uses every pulse it records, and reuses none beyond the registered interval. Without that, the arithmetic was provable and its origin was not -- a document could leave every raw latency untouched and rescale a real regression into a qualification by choosing a divisor |
| One measured population per block | A block records three views of the same operations: the calibration-normalized samples the pairing decides on, the raw nanosecond samples the absolute ceilings decide on, and the calibration pulse that divides one into the other. They must be equal in size, agree with the recorded sample count, and satisfy the identity the workload report proves at measurement time -- each normalized sample is its own latency over its own pulse. Without that, a document could pair sixteen observations while holding one unrelated observation to the budget |
| The absolute ceilings | Every input is the measurement the paired decision is formed from. The latencies are the recorded nanosecond samples of each block, not a per-block summary: the ratio decision uses calibration-normalized samples, which no budget in nanoseconds can be applied to, so the raw samples travel with the evidence and the ceilings read those. The workload family selects the budget, and the family comes from the contract, so a workload cannot re-declare itself into a more generous ceiling. The per-block summary remains in the document as an audit view and is checked against what those measurements produce |
| The driver identity | A clean checkout records the Git tree identifier; a working tree with uncommitted benchmark changes records a full SHA-256 over the commit tree, the diff, and the contents of every untracked file. Both shapes are what the evaluator accepts, so a local run cannot discard the evidence it just produced |
| The assembled-evidence contract | `evaluate-paired` is its own trust boundary and re-checks the finished document: a declared block count inside the registered range, every parallel record and the recorded order against it, candidate measurements covering every block and every registered workload, both environments complete and still comparable, provenance in digest form naming one candidate revision and the contract this evaluation actually loaded, a convergence claim against the duration floor, and a cap claim against the population the cap actually permits. Types are checked before values throughout, because reading a field first turns broken evidence into an ordinary failure -- and the attempt recorder reads that as a regression |
| `blocks.minimumValidSamples` (via `blocks.profile`) | Applied per side per block; a population below the profile floor is invalid evidence |
| `durations.maximumPairedRunSeconds` | The paired run's own wall clock, and an upper bound on the block profile's ceiling |
| `durations.maximumWorkloadSeconds` | An upper bound on the block profile's per-workload ceiling |
| `blocks.maximumRelativeStandardError` | Applied per side per block during assembly |

### Early warning on the default branch

The dedicated `benchmark` workflow keeps the hosted historical baseline current
on `main`. It is early warning: it never qualifies and never blocks a release.
The sections above on baselines, historical budgets, and the accepted runner
pair describe that path.

### Timeouts and interruption

Each hosted job has its own bounded workflow timeout. The performance runner
additionally enforces the selected profile deadline and the named workload
timeout floor from `timeoutPolicies`, using the stricter of the two. This
bounds expensive workloads without imposing one global deadline on unrelated
release stages.

Every finished stage writes a source-bound receipt outside the portable
candidate directory. Continue a safely interrupted run with the same
`DOKA_RELEASE_CANDIDATE_RUN_ID` and `DOKA_RELEASE_CANDIDATE_RESUME=1`. A stage
is skipped only after every artifact digest in its receipt is recomputed
successfully. Partial output from an unfinished stage is archived before that
stage restarts.

`DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS=1` is a development-loop bypass. Any
evidence produced with that bypass is not release eligible.

[github-actions-policy]:
  https://docs.github.com/en/organizations/managing-organization-settings/disabling-or-limiting-github-actions-for-your-organization
[github-required-checks]:
  https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks
[github-token-authentication]:
  https://docs.github.com/en/actions/concepts/security/github_token
[github-workflow-events]:
  https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow
[github-skipped-workflows]:
  https://docs.github.com/en/actions/how-tos/manage-workflow-runs/skip-workflow-runs
