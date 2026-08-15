# Performance and memory evidence

This runbook describes the reproducible performance-evidence system defined by
[D-019](../decisions/D-019-performance-gate-architecture.md).

This system is independent of release qualification. The release-candidate and
NuGet-publication workflows do not invoke it or consume its artifacts. A
failed, inconclusive, missing, or stale benchmark is engineering feedback and
cannot block a release.

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
- the exact images for every active LTS target declared in
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
sufficient. Paired scorecards require environment equality inside each
reference-and-candidate pair and never compare processors from different runs.

## Profiles

| Profile | Purpose | Workload samples | Soak | Baseline |
|---|---|---:|---|---|
| `smoke` | Fast harness and contract check | 1 to 3 | Optional | Not required |
| `paired-block` | One block of a paired comparison | starts at 16 with pilot-sized operation batches, extended on precision up to 64x | Required once per run | Not required |
| `scorecard` | Hosted baseline-seed evidence | 256; 128 expensive, adaptively extended up to 64x | Required | Required |
| `stress` | Extended investigation | 512; 256 expensive, adaptively extended up to 64x | Required | Required |

`paired-block` needs no baseline because a paired run carries its own
reference. Each target measures exactly ten alternating A/B blocks registered
before the run starts. The profile observes rather than enforces the per-block
sample cap: inference uses the complete fixed block population and never adds
blocks after seeing a statistical result. A block that is so noisy its ratio
would be meaningless is still rejected, per side, against
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

After warmup, `paired-block` measures one configured operation batch and uses
that pilot to distribute 120 percent of the two-second duration floor over the
workload's starting population. A sixteen-sample workload therefore targets
150 milliseconds per sample, while an explicitly registered 8,192-sample
workload targets about 293 microseconds. The multiplier is rounded up and
capped at 1,024. Every workload uses the fastest of three pilot observations so
one scheduler stall cannot undersize it. This plans at least 2.4 seconds of measured work without
inflating high-population tail workloads or consuming the separate sample-count
cap. The reference and candidate sides pilot independently because sample size
is an execution property; the paired statistic continues to compare normalized
time per operation.

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
workload from consuming the matrix budget. The current value retains headroom
above the largest observation in the accepted two-target baseline that
preceded the LTS-matrix expansion:

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
baseline once the baseline matches the current contract. The first six-target
seed closes that scorecard-evidence transition. It is not a release
precondition. When the current-contract test fails after a later baseline
update, the run-to-run spread has outgrown the headroom, and the multiplier is
what moves.
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

The scheduled smoke is not a hand-maintained representative subset. Its
internal reusable workflow reads every key from
`performance-contract.json.requiredTargets`, fans those keys out through a
GitHub matrix, and gives each job an isolated `--up-run-down` lifecycle. The
smoke profile applies only absolute contracts and produces no accepted
scorecard evidence. The complete target set exists to catch target-specific
harness or image drift before a full scorecard depends on that path.

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
| `configuredOperationsPerSample` | positive integer | The workload's checked-in operation batch before adaptive sizing. |
| `operationBatchingMode` | `fixed` / `pilot` | Whether the profile retained the configured batch or sized it from a pilot. |
| `pilotSamplesElapsedTicks` | positive integer array | Stopwatch ticks measured by the three registered pilot observations; empty for fixed batching. |
| `operationsPerSample` | positive integer | The actual operation batch used for warmup and measurement. |

The termination fields were introduced with schema version 4 of the raw
workload report (`kind: performance-workloads`). The operation-batching
provenance is required by the current **schema version 5**. The validator
rejects older documents with an explicit unsupported-version message rather
than reporting them as incomplete. Re-measure with the current benchmark build
to produce a version-5 report. The accepted baseline and the evaluation record
keep their own schema versions.

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
- an adaptive operation batch that cannot be derived exactly from the recorded
  fastest pilot, duration floor, workload population, headroom, and multiplier
  cap;
- pilot provenance on a fixed profile, or missing pilot provenance on an
  adaptive profile;
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

Under either policy a capped historical result is refused for baseline
promotion. Seeding rejects it by workload id and names the recalibration
levers. A paired block uses the `observe` policy: its cap state remains visible
in the audit record, while the fixed ten-block comparison still reports the
result it measured.

### Environmental noise or an unsuitable contract

A capped result on one run and a clean result on the retry is host noise; that
is what the bounded retry exists for. The same workload capping run after run
is not noise -- it says the profile cannot satisfy its contract within its
registered sizing limits.

The diagnostic prints all three pairs listed above -- samples against the
derived cap, measured duration against the required minimum, and achieved
relative standard error against the allowed ceiling -- so the decision needs no
rerun. For fixed historical profiles, read which pair is the outlier and
recalibrate exactly one of `operationsPerSample`, the minimum duration, or the
cap. Raising the cap alone treats the symptom; more work per sample is usually
the correct lever.

The paired profile performs that sample-size calibration automatically and
records every input needed to reproduce it. This follows the separation used
by BenchmarkDotNet's pilot stage and Go's `predictN`: duration determines the
amount of work in one sample, while the cap limits how many samples may be
collected. A repeated paired cap therefore points to the registered pilot
multiplier ceiling, precision policy, or time budget rather than to processor
speed alone. Changes to those bounds remain reviewed contract changes with a
version bump.

## Accept an engine image update

Dependabot proposes engine images against the Compose stack, which is the one
place it edits. The same pin also lives in the performance contract, the C#
test image catalog, and applicable workflow inputs, so its pull request is
incomplete by construction and the pin gate rejects it until every copy
follows:

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

Run every contract target in `seed` mode with the same profile and runner
class:

```bash
while read -r target; do
  DOKA_BENCHMARK_TARGET="${target}" \
  DOKA_BENCHMARK_PROFILE=scorecard \
  DOKA_BENCHMARK_BASELINE_MODE=seed \
  DOKA_BENCHMARK_RUNNER_CLASS=local-darwin-arm64 \
  DOKA_BENCHMARK_RUN_ID="local-seed-${target}" \
  ./eng/benchmark.sh --up-run-down
done < <(jq -r '.requiredTargets | keys[]' benchmarks/performance-contract.json)
```

Create the candidate only after reviewing every evaluation:

```bash
evidence=()
while read -r target; do
  evidence+=(
    --evidence
    "artifacts/benchmarks/${target}/reports/local-seed-${target}/evidence/performance-evaluation.json"
  )
done < <(jq -r '.requiredTargets | keys[]' benchmarks/performance-contract.json)

python3 -m eng.performance.cli seed \
  --contract benchmarks/performance-contract.json \
  --baseline benchmarks/baselines/doka-benchmark-baseline.json \
  --version <reviewed-baseline-version> \
  "${evidence[@]}"
```

Every evaluation handed to `seed` or `compare` must agree on profile, runner
class, commit, and source hash: promotion accepts one measured state of one
piece of software, not a set assembled from several. The run identifier is
deliberately not part of that agreement. It names a single measurement job, so
the commands above give each target its own, and the hosted matrix does the
same with one job per target. The contract's `evidenceMaximumAgeHours` keeps
the evaluations close together in time.

When adding a new runner class, retain existing accepted groups:

```bash
# Reuse the complete evidence array constructed above.
python3 -m eng.performance.cli seed \
  --contract benchmarks/performance-contract.json \
  --baseline artifacts/doka-benchmark-baseline.candidate.json \
  --version <reviewed-baseline-version> \
  --merge-existing benchmarks/baselines/doka-benchmark-baseline.json \
  "${evidence[@]}"
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

Run every required target with `DOKA_BENCHMARK_BASELINE_MODE=compare`, then
enforce the cross-target boundary:

```bash
DOKA_BENCHMARK_PROFILE=scorecard \
DOKA_BENCHMARK_GATE_RUN_ID=<run-id> \
bash eng/performance/check-benchmark-ratios.sh artifacts/benchmarks
```

The gate exits:

- `0` when every required target passes;
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
- the shared performance-input classifier treats provider source, benchmark
  source and corpora, database images, build and SDK inputs, the evaluator,
  the harness, the scorecard control plane, the target workflow that performs
  and uploads each measurement, and the executable sensitivity assurance as
  scorecard inputs;
- the evaluator binding includes the paired endpoint estimator and bounded
  attempt selector; changing either invalidates ancestor scorecard evidence
  before current family-level policy is applied to stored target statistics;
- the non-qualifying scheduled smoke workflow remains outside that reuse
  classifier because changing its orchestration cannot change accepted
  scorecard evidence;
- a `main` push that changes any measured provider or harness input runs the
  complete contract-derived LTS performance matrix;
- changes confined to the parent workflow, resolver, documentation, tests, or
  accepted baseline output remain on the inexpensive resolver path;
- an exact current-contract `github-ubuntu-latest-x64` matrix selects `compare`;
- a missing baseline, an older contract, or a missing runner matrix selects
  `seed`;
- `compare` always selects the paired same-run comparison, while `seed`
  selects the historical scorecard that creates reviewable baseline evidence;
- the reusable scorecard workflow accepts only that comparison selection and
  derives its baseline behavior, so a caller cannot submit a contradictory
  `paired`/`seed` or `historical`/`compare` combination;
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
copies the selected report tree into the stable engine artifact. Normal
`benchmark` comparisons invoke the reusable workflow in `paired` mode, which
measures a reference and the candidate on the allocated runner. Seed runs alone
produce historical artifacts, and those artifacts may only enter the reviewed
baseline proposal. Release qualification invokes neither mode and imports no
benchmark artifact.

The routing is the CPU-independence mechanism: every automatic comparison is
paired before a measurement job starts. The typed exit code `76` remains a
defense for an explicitly requested local historical comparison; no automatic
hosted comparison relies on that recovery path.

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

The workflow opens or updates a semantic baseline proposal but never approves
it. A private, repository-scoped GitHub App registers squash auto-merge without
bypassing protected-branch policy. The normal operator path is therefore:

1. Review the baseline diff and the linked benchmark run.
2. Confirm that `quality-gates`, `repo-tests`, and `integration-smoke` passed
   for the current proposal head.
3. Approve the current pull-request revision.
4. Let GitHub complete the App-owned squash merge after every requirement is
   satisfied.

The repository ruleset dismisses an approval when a later automation push
changes the reviewed diff and also requires approval of the most recent
reviewable push. A proposal that remains open after an earlier approval is
therefore not a failed auto-merge. Review the new canonical-baseline diff,
wait for `quality-gates`, `repo-tests`, and `integration-smoke` on the current
head, and approve that revision again. GitHub keeps the existing auto-merge
request active because the automation updating the branch has write permission,
and the controller ensures that a request exists; no workflow rerun, artifact
download, or manual merge is required. This behavior prevents a semantic
baseline update from inheriting an approval issued for different bytes.

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
permissions**. The workflow consumes only the pull-request creation capability
and contains no review, approval, or administrative-bypass command. The active
ruleset therefore continues to require an independent maintainer approval and
all protected checks. Branch synchronization, pull-request maintenance, and
the restricted CI dispatch retain the ephemeral `GITHUB_TOKEN`; this avoids a
second full PR workflow fan-out. Only the auto-merge request uses a short-lived
GitHub App installation token. The token is restricted to this repository and
the `contents` and `pull-requests` write permissions, and GitHub revokes it at
job completion. This keeps the final merge outside `GITHUB_TOKEN` recursion
suppression without introducing a user PAT or a ruleset bypass. The required
repository setting and App behavior are documented by GitHub's
[Actions policy reference][github-actions-policy],
[workflow-trigger reference][github-workflow-events], and the official
[`create-github-app-token` action][github-app-token-action].

GitHub exposes the Actions installation through the pull-request Actor login
`app/github-actions`, not through the commit-bot name
`github-actions[bot]`. The controller uses that real identity to migrate a
legacy auto-merge request, then reads the resulting state back from GitHub. An
App registration is accepted only when it remains an App-owned squash request
or has already completed as an App-owned merge.

The legacy migration is deliberately transitional. Remove its
`app/github-actions` branch and matching contract assertions after the first
dedicated-App baseline proposal has merged successfully and GitHub reports no
open baseline proposal owned by the legacy Actor. Record the qualifying pull
request and workflow run in D-019 before removal so the transition is closed
by observed repository evidence rather than elapsed time.

Before allocating scorecard runners, the cheap resolver also requests and
immediately revokes an unused token whenever proposal maintenance may be
required. This validates the Client ID, private key, installation, repository
selection, and requested permissions before an external configuration error
can waste the measured run. A fresh token is created after a proposal update;
tokens never cross job boundaries.

The hosted workflow uses paired `compare` mode when the accepted reference pair
is present. A missing or stale pair enters the reviewed historical seed path,
but that state is not a release precondition. Both paths produce independent
engineering evidence and have no release authority.

This design follows GitHub's documented `GITHUB_TOKEN` event behavior,
GitHub App attribution contract, approval-gated workflow-run contract, and
latest-commit status-check contract. The primary sources were retrieved on
2026-08-07 and revalidated for the App integration on 2026-08-14:

- [Automatic token authentication][github-token-authentication]
- [Triggering a workflow][github-workflow-events]
- [Troubleshooting required status checks][github-required-checks]
- [Skipping workflow runs][github-skipped-workflows]
- [Authenticating as a GitHub App installation][github-app-authentication]
- [`actions/create-github-app-token`][github-app-token-action]
- [Rules available for rulesets][github-ruleset-reviews]

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
Changing the absolute contract requires fresh evidence from every required
target and a decision review.

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

## Paired scorecard use

The standalone benchmark workflow uses paired comparisons for automatic
engineering feedback. Release tags do not invoke this path, import an accepted
baseline, or consume its artifacts.

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
| Required latency endpoint | Does the complete workload matrix regress on normalized median for a supported target | The run-wide Holm decision rejects and the interval sits above the practical budget |
| Observational latency endpoints | Where did normalized median, p95, or p99 move per workload | Reported for diagnosis; never a required benchmark endpoint |
| Resource families | Does the candidate allocate or collect more than its reference | The median block ratio exceeds the registered budget |
| Absolute ceilings | Is the candidate inside its family budgets at all | A pair that regressed together would otherwise qualify |
| Soak | Does sustained use leak | A leak appears over thousands of iterations and never inside a block |

The required latency endpoint is the geometric mean, within each block, of the
complete workload matrix's normalized-median ratios. Per-workload median, p95,
and p99 intervals are observational secondary endpoints. A change is a required
regression only when the run-wide Holm procedure rejects it and its interval is
above the practical budget. Statistical overlap remains visible but neither
asserts equivalence nor triggers a rerun. The ten-block population is fixed
before measurement, so repeatedly sampling until significance cannot bias the
decision.

The six required target endpoints form one family with a run-wide family-wise
error rate of `0.05`. Holm's step-down procedure operates once after all six
selected target artifacts exist; target jobs may only report
`pending-run-wide-adjustment`. A locally small p-value cannot bypass that
global decision. Resource ratios, absolute ceilings, and soak invariants remain
hard local gates and do not enter the statistical family.

Each target p-value comes from an exact one-sided sign-flip test over the ten
counterbalanced block log ratios centered on the practical budget. The
evaluator enumerates all 1,024 assignments, so Holm receives a calibrated
randomization p-value without Monte Carlo error. BCa supplies the effect
interval and remains separate from the hypothesis test.

The block count is backed by a pre-registered sensitivity assurance, not by a
round-number convention. The assurance runs the production BCa bootstrap and
the first Holm threshold over 200 deterministic planning experiments. The
maximum log-ratio standard deviation is `0.06048100249438095`: the one-sided
99 percent NIST upper confidence bound over the noisiest of four digest-bound
hosted characterization attempts, not their point estimate. At that bound the
assurance requires at least 80 percent power, with a one-sided 95 percent
Wilson lower bound, to detect one aggregate regression at `1.10` times its
practical budget. It detects 180 of 200 experiments, with a lower bound of
approximately `0.8596`, and a minimum detectable normalized-median ratio of
`1.265`. A required target above the registered dispersion is
`measurement-inconclusive`; the evaluation cannot claim sensitivity its blocks
did not possess.

`uncertainResults` remains an audit count, not a gate. Capping it would turn
statistical overlap back into a result-dependent retry or failure condition,
which is the optional-stopping path the fixed population removes. The power
assurance defines what the run is designed to detect; overlap below that
boundary is reported honestly as `observed-overlap`, while absolute
ceilings continue to reject catastrophic latency regardless of ratio power.

The characterization is planning-only and cannot qualify a release. Its source
artifact identities and the characterization file digest are contract-bound.
Replay tests run the production estimator against those hosted populations and
must recover an injected relevant regression before a contract change can
land.

Every attempt also emits `paired-dispersion-observation.json`. Monthly automatic
scorecards retain these small files for ninety days and report `drift` as a
workflow warning; raw attempts remain at seven days. The immutable observation
series records `stable` below the bound and produces the typed inconclusive
state for a required target on `drift`. If two separate complete scorecard runs
within thirty days each exhaust both attempts with drift on the same target,
D-026 requires an ADR amendment before the benchmark contract or target role is
changed. There is no automatic role downgrade and no additional
maintainer-triggered workflow.

### Reruns and retries

An attempt receipt classifies the run into one of six states and carries
whether a retry is permitted. Only an interrupted measurement that reports
`measurement-inconclusive`, or an incomparable historical environment that
reports `environment-not-comparable`, is retryable. Statistical overlap and a
paired sample-cap observation are fixed-population results, not attempt states.
A retry may not select away a verdict about the code. The workflow reads the
receipt field rather than comparing against a state name, so the retry policy
has exactly one home.

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
artifacts/benchmarks/<target>/reports/<run-id>/paired-dispersion-observation.json
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

The standalone benchmark artifact binds every selected file by digest and is
retained according to the benchmark workflow's artifact policy. No file from
this tree is copied into a release candidate.

### What the contract controls

Every value the paired policy registers has something that reads it, and a
value nothing compares against would describe nothing:

| Registered value | What reads it |
|---|---|
| `blocks.profile`, `blocks.startingSamplesPerSidePerBlock` | The contract refuses a policy whose starting population differs from the profile that measures it, or falls below its valid-sample floor |
| `blocks.maximumSampleCountRatio` | Applied per block: the two sides may reach different populations, but not populations far enough apart to have measured different stretches of time |
| `primaryFamily`, `secondaryFamilies`, `targetRoles` | The required aggregate and every observational endpoint and target role are fixed before measurement; no live result can promote or demote itself |
| `multipleComparison.*` | The scorecard finalizer applies one Holm procedure at the registered run-wide family-wise error rate over the exact required-target set |
| `executionOrder.blockPatterns` | The runner takes each block's order from the list, records what it executed, and the evaluator refuses an order that deviates or never alternates the starting side |
| `retry.eligibleAttemptStates`, `retry.maximumRetries` | The contract refuses a policy that disagrees with the states and bound the attempt recorder implements; the receipt carries the resulting decision, which the workflow condition reads |
| `durations.closingReserveSeconds` | Withheld from every side watchdog and from the block forecast, so the closing work always has room |
| `durations.finalizationReserveSeconds` | Withheld again from the sustained-use run, so assembling and evaluating the evidence keep their share of the closing reserve |
| `blocks.profile` (termination) | The runner's own termination verdict travels per side per block. A workload that stopped at its sample cap remains visible and participates in the pre-registered fixed-block comparison; the evaluator never chooses a replacement population after seeing its result |
| The canonical workload contract | Every raw block report passes it before assembly: a foreign schema or kind, an impossible termination, a count that contradicts its samples, and any statistic that does not follow from the persisted samples are all invalid evidence |
| The audit projection's shape | The candidate summary carries exactly seven fields -- the workload, its family, and the five metrics the ceilings decide on -- and one function produces it for the runner and the fixtures alike. Anything more is refused: passing the workload report through put a second, unchecked copy of every sample, calibration and pulse into the document beside the canonical one, so a reader could find numbers there that no decision used |
| The calibration's origin | The divisor of each sample is not read from the evidence, it is rebuilt from the calibration pulses the run recorded and the pulse each sample was measured against, under the invariants the workload report is held to at measurement time: the train starts at the first pulse, advances one pulse at a time, uses every pulse it records, and reuses none beyond the registered interval. Without that, the arithmetic was provable and its origin was not -- a document could leave every raw latency untouched and rescale a real regression into a qualification by choosing a divisor |
| One measured population per block | A block records three views of the same operations: the calibration-normalized samples the pairing decides on, the raw nanosecond samples the absolute ceilings decide on, and the calibration pulse that divides one into the other. They must be equal in size, agree with the recorded sample count, and satisfy the identity the workload report proves at measurement time -- each normalized sample is its own latency over its own pulse. Without that, a document could pair sixteen observations while holding one unrelated observation to the budget |
| The absolute ceilings | Every input is the measurement the paired decision is formed from. The latencies are the recorded nanosecond samples of each block, not a per-block summary: the ratio decision uses calibration-normalized samples, which no budget in nanoseconds can be applied to, so the raw samples travel with the evidence and the ceilings read those. The workload family selects the budget, and the family comes from the contract, so a workload cannot re-declare itself into a more generous ceiling. The per-block summary remains in the document as an audit view and is checked against what those measurements produce |
| The driver identity | A clean checkout records the Git tree identifier; a working tree with uncommitted benchmark changes records a full SHA-256 over the commit tree, the diff, and the contents of every untracked file. Both shapes are what the evaluator accepts, so a local run cannot discard the evidence it just produced |
| The assembled-evidence contract | `evaluate-paired` is its own trust boundary and re-checks the finished document: the exact registered block count, every parallel record and the recorded order against it, candidate measurements covering every block and every registered workload, both environments complete and still comparable, provenance in digest form naming one candidate revision and the contract this evaluation actually loaded, a convergence claim against the duration floor, and a cap claim against the population the cap actually permits. Types are checked before values throughout, because reading a field first turns broken evidence into an ordinary failure -- and the attempt recorder reads that as a regression |
| `blocks.minimumValidSamples` (via `blocks.profile`) | Applied per side per block; a population below the profile floor is invalid evidence |
| `durations.maximumPairedRunSeconds` | The paired run's own wall clock, and an upper bound on the block profile's ceiling |
| `durations.maximumWorkloadSeconds` | An upper bound on the block profile's per-workload ceiling |
| `blocks.maximumRelativeStandardError` | Applied per side per block during assembly |
| `sensitivity.*` | The Engineering suite verifies the digest-bound hosted characterization, recomputes its NIST upper confidence bound, replays the production BCa and Holm decision, and requires the registered power for the required aggregate endpoint |

### Early warning on the default branch

The dedicated `benchmark` workflow performs a paired same-run comparison for
performance-relevant `main` changes and its monthly refresh. It is early
warning: it never qualifies and never blocks a release. Because reference and
candidate share one allocated runner, a new hosted CPU model cannot turn that
comparison into a regression. Historical scorecards exist only when `seed`
produces a reviewed replacement for a missing or incompatible accepted
reference matrix.

### Timeouts and interruption

Each hosted job has its own bounded workflow timeout. The performance runner
additionally enforces the selected profile deadline and the named workload
timeout floor from `timeoutPolicies`, using the stricter of the two. This
bounds expensive workloads without imposing one global deadline on other
workflow jobs. Release qualification has no benchmark bypass because it never
runs a benchmark.

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
[github-app-authentication]:
  https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation
[github-app-token-action]:
  https://github.com/actions/create-github-app-token
[github-ruleset-reviews]:
  https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets
