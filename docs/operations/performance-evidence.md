# Performance and memory evidence

This runbook describes the reproducible performance gate defined by
[D-019](../decisions/D-019-performance-gate-architecture.md).

The release-qualified path has six independent controls:

1. A persisted interval host-CPU admission and processor-identity preflight.
2. BenchmarkDotNet same-run controls and allocation evidence.
3. A complete named workload matrix with raw and adjacent calibration samples.
4. Raw absolute and calibration-normalized historical budgets.
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

Do not compare latency from different runner classes. The gate matches
baselines by target, profile, and runner class. It additionally requires an
exact match for runtime, OS, architecture, concrete processor model, processor
count, and server image. BenchmarkDotNet must report that same processor and
process architecture. A matching runner label alone is not sufficient.

## Profiles

| Profile | Purpose | Workload samples | Soak | Baseline |
|---|---|---:|---|---|
| `smoke` | Fast harness and contract check | 1 to 3 | Optional | Not required |
| `scorecard` | Release evidence | 256; 128 expensive | Required | Required |
| `stress` | Extended investigation | 512; 256 expensive | Required | Required |

Only `scorecard` and `stress` execute the complete 55-cell workload matrix.
Expensive cells retain at least 100 observations for p99 while avoiding a
second full population of large writes. The scorecard accepts at most 25%
relative standard error; stress accepts at most 15%. Fast, idempotent
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

The `benchmark` workflow resolves its baseline mode and required work before
starting services or either expensive matrix job:

- an exact current-contract `github-ubuntu-latest-x64` pair selects `compare`;
- a missing baseline, an older contract, or a missing runner pair selects
  `seed`;
- malformed or partial current-contract evidence fails before the matrix;
- weekly and manual runs always request fresh scorecard evidence;
- a `main` push requests a scorecard only after a performance-contract,
  harness, evaluator, or benchmark-workflow input changes;
- a current and up-to-date seed proposal is a no-op;
- a current proposal behind only unrelated `main` changes is synchronized
  without another scorecard; and
- an invalid proposal or performance-input change after its source commit is
  replaced with fresh evidence on the same automation branch.

An automation branch that changes any path other than the canonical baseline
fails in the resolver before the scorecard matrix starts. Remove or review the
unexpected branch state explicitly; automation does not overwrite it. Later
`main` pushes queue behind a running scorecard instead of cancelling evidence
that is already being collected.

Both engine jobs must succeed before a seed run can propose a baseline update.
A seed still enforces the complete workload, absolute budgets, statistical
integrity, allocation, GC, soak, environment, and host-admission contracts. It
omits only a historical comparison that cannot exist yet. A contract revision
deliberately does not carry older contract groups into the proposal.

The workflow validates the combined MySQL and MariaDB evidence, writes the
canonical baseline on a stable automation branch, and opens or updates one
pull request. It never approves or merges that pull request. The normal
operator path is therefore:

1. Review the baseline diff and the linked benchmark run.
2. In the pull request's **Checks** tab, select **Approve workflows to run**.
3. Merge the proposal after its protected checks pass.

The proposal and linked evidence expose the following review inputs:

- source commit and source hash;
- exact server images;
- runtime, OS, CPU, architecture, and processor count;
- raw and normalized median, p95, p99, relative standard error, calibration
  stability, allocation, retained-byte diagnostics, and collection counts;
- absolute and soak verdicts;
- raw report SHA-256 hashes.

GitHub creates the normal `pull_request` workflow run when `GITHUB_TOKEN`
opens or synchronizes the proposal, but holds that run for approval by a user
with write access. After approval, the protected `quality-gates`, `repo-tests`,
and `integration-smoke` checks bind the merge decision to the current
pull-request revision and test merge. No baseline artifact download, Run-ID
handoff, or second workflow dispatch belongs to the operator path.

Repository administrators must enable **Allow GitHub Actions to create and
approve pull requests** under **Settings > Actions > General > Workflow
permissions**. The workflow consumes only the create capability and contains
no approval or merge command. It uses the ephemeral `GITHUB_TOKEN`; no PAT,
repository secret, or external application is required. This repository
setting and its security implications are documented by GitHub's
[Actions policy reference][github-actions-policy].

Release qualification always uses strict `compare` mode and fails closed when
the accepted current runner pair is absent. Its preflight tells the operator to
review and merge the automated proposal before creating a new release tag.

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

Each hosted release-candidate job has its own bounded workflow timeout. The
performance runner additionally enforces the selected profile deadline and the
named workload timeout floor from `timeoutPolicies`; it uses the stricter of
those two limits. This keeps expensive workloads bounded without imposing one
global deadline on unrelated release stages.

Every finished stage writes a source-bound receipt outside the portable
candidate directory.
Continue a safely interrupted run with the same
`DOKA_RELEASE_CANDIDATE_RUN_ID` and
`DOKA_RELEASE_CANDIDATE_RESUME=1`. A stage is skipped only after every artifact
digest in its receipt is recomputed successfully. Partial output from an
unfinished stage is archived before that stage restarts.

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
