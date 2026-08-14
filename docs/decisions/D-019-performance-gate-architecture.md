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
amended-by: [D-026]
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
- ordinary desktop activity made raw historical latency too sensitive, while
  requiring an artificially idle workstation made the gate operationally
  brittle;
- a hung build, benchmark child, or later release stage could consume an
  unbounded amount of time and discard already completed evidence;
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
- Current aggregate host CPU utilization and concrete processor identity must
  be accepted before timing starts, and every measurement layer must agree on
  that host.
- Historical latency must distinguish provider drift from contention that
  equally affects a directly adjacent control path.
- Every long-running boundary needs a hard deadline, complete process-tree
  cleanup, and source-bound continuation evidence.
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
`eng/performance/cli.py` independently validates and evaluates their
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
controls. The release gate selects only the methods named by the checked-in
control contract; the complete benchmark suite remains available for manual
investigation. It uses the full JSON exporter, the memory diagnoser, Release
builds, and stop-on-first-error behavior. The host process also returns
non-zero for:

- no benchmark summaries;
- critical validation errors;
- any unsuccessful benchmark report;
- an exception in a workload or soak run.

The named-workload runner persists every raw sample and an adjacent calibration
pulse. CPU-only families use a deterministic CPU control; database families
use a live `SELECT 1` round-trip. It derives raw and normalized median, p95,
p99, and standard error using documented linear interpolation. The Python gate
recomputes each statistic from the samples and rejects a mismatch, a non-finite
number, a missing matrix cell, excessive normalized relative standard error,
or unstable calibration. A pulse reused for several nearby workload samples is
counted once when calibration error is calculated.

The paired release gate also binds its fixed block count to an executable
power assurance. The assurance drives the production BCa bootstrap and one
run-wide Holm decision over the required target endpoints, with a
pre-registered detectable effect, a characterization-backed upper confidence
bound for log-ratio dispersion, a simulation seed, a trial count, and
confidence-bounded minimum power. Per-workload latency endpoints are
observational; resource, absolute-ceiling, and soak gates remain hard. A live
required endpoint whose cross-block dispersion exceeds the registered bound is
a retryable measurement condition, not a qualified result with an unsupported
sensitivity claim. Each attempt persists the realized dispersion so monthly
automatic scorecards expose drift over time.

Historical profiles use a fixed, checked-in operations-per-sample value. The
paired profile instead derives its operation batch from a recorded pilot so
the duration floor cannot consume the independent sample-count cap. The runner
times the complete batch and normalizes latency, allocation, and collection
counts per operation. This keeps timer resolution and loop overhead from
dominating sub-microsecond paths while preserving deterministic workload
identity. Tail statistics for these paths describe the distribution of
per-operation-normalized batch samples, not individual OS scheduling pauses.
Scorecard evidence starts with 256 samples for ordinary cells and 128 for
expensive cells, with at most 25% relative standard error. Stress starts with
512 and 256 respectively, with at most 15% relative standard error. If a
population misses that statistical budget, the runner retains every
observation and extends measurement in calibration-aligned blocks, up to the
contract-owned multiplier. Existing workload and matrix deadlines bound the
extension. A population that remains unstable at the cap still fails. Every
release profile therefore retains at least 100 individual observations for
p99 without forcing stable large database writes to repeat at the same
population as cheap in-process work.

Managed allocation uses the precise process allocation counter around the
measured operation. Preparation and cleanup are outside that window. Garbage
collection counts cover the same operation window. Retained bytes are measured
after forced full collections before and after the workload series. That
process-global heap delta is retained as a diagnostic, not a per-workload
budget, because unrelated finalization and runtime activity can change it. The
sustained managed-heap and working-set soak invariant is the hard
retained-memory gate. These metrics describe managed process behavior; they do
not claim to measure native driver or server allocation.

Every evaluation records:

- run ID, target, profile, Git commit, and exact working-tree source hash;
- stable runner class and concrete processor model;
- .NET runtime, OS, architecture, processor, processor count, and exact server
  image;
- one-, five-, and fifteen-minute load averages as diagnostics, plus every
  interval host-CPU sample, its operating-system counter source, bounded
  admission decision, and ceiling;
- engine family and the server-observed `SELECT VERSION()` value;
- raw BenchmarkDotNet report paths and SHA-256 hashes;
- workload, soak, contract, and derived-evidence hashes;
- all absolute and historical verdicts.

The wrapper builds first, then samples aggregate host CPU counters over
one-second intervals. Linux uses `/proc/stat`; macOS uses Mach
`host_statistics64`. Admission requires two consecutive samples at or below
`0.90` within five attempts. The bounded retry absorbs short build or container
runoff, while persistent saturation still fails before timing begins. A
lifetime process average is not used as a proxy for current host utilization.
Unix load average remains diagnostic because it can include runnable work that
does not represent provider contention. The wrapper persists every interval
sample and exports the admitted values into the workload process. Adjacent
calibration then removes ordinary current-run CPU or local-database contention
from historical latency comparisons. Calibration is a one-sided nuisance
adjustment: a slower current control can discount contention, but a faster
control cannot make an unchanged or faster provider path appear slower. The
evaluator binds both artifacts and also requires BenchmarkDotNet to report the
same processor and process architecture in a Release build. A targeted
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
catastrophes. They are calibrated above the worst required-target scorecard,
with larger latency headroom than memory headroom because runner timing varies
more than managed allocation.

Historical budgets are stricter. They compare only the same target, profile,
and runner class. The current runtime, OS, architecture, processor, processor
count, and server image must also exactly match the accepted environment. A
reused runner label cannot hide hardware or runtime drift.

| Metric | Maximum relative to accepted baseline |
|---|---:|
| Normalized median | 1.15x |
| Normalized p95 | 1.25x |
| Normalized p99 exceedance threshold | 1.40x |
| Allocated bytes per operation | 1.10x |

Raw median, p95, and p99 retain broad absolute disaster ceilings. Managed
allocation has both an absolute and a historical ceiling. Collection counts
and raw retained-heap delta remain persisted diagnostics; the sustained soak
invariants enforce actual resource ownership. The historical p99 verdict
persists its sample count, exceedance count and rate, exact p-value, expected
one-percent exceedance probability, and one-percent significance level. Both
latency layers must pass. An absolute budget cannot replace normalized
historical comparison, and a favorable historical comparison cannot excuse an
absolute safety violation.

Normalized median and p95 remain direct point-estimate budgets. A normalized
p99 above 1.40 times its accepted baseline triggers two bounded independent
confirmations. The triggering population is excluded from the verdict to avoid
selection bias. The combined confirmation population is rejected only when
the exact one-sided binomial probability of its threshold exceedance count is
below 0.01 under the expected p99 exceedance probability of 0.01. This keeps
the tail gate sensitive to sustained regressions without treating the ordinary
one-percent tail as a deterministic failure.

The accepted baseline contains one complete contract-derived LTS target matrix
for each runner class. Replacing a matrix requires successful seed evaluations
for every required target. Existing complete runner groups are retained.
Duplicate or partial target/profile/runner tuples are rejected.

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

The benchmark workflow resolves baseline compatibility and event relevance
before starting services or any scorecard matrix job. Monthly and manual
runs always request fresh evidence. A `main` push requests fresh evidence when
the shared release-evidence classifier detects a provider, benchmark, corpus,
database-image, build, SDK, harness, evaluator, or reusable scorecard-workflow
input. The contract-derived six-target scorecard then compares against the
candidate in paired mode, or produces historical evidence in reviewed seed
mode. The parent workflow, resolver, documentation, tests, and accepted
baseline output remain on the inexpensive control-plane path. Provider source
changes never normalize their own regression threshold.

Each engine execution emits a typed attempt receipt. Success selects the first
attempt. Only an interrupted measurement, or a historical environment that is
not comparable, can start one retry, and that retry runs in a new hosted job
with a fresh database service. A hard functional, budget, contract, or
infrastructure failure stops immediately and cannot be masked by another
attempt. Statistical overlap and a paired sample-cap observation are results,
not retry states. Two retryable attempts fail closed as inconclusive evidence.

The resolver compares only when the accepted baseline contains a complete
current-contract matrix for the hosted runner. A missing baseline, older
contract, or absent runner matrix selects `seed`. Malformed or partial
current-contract evidence fails before the matrix. In seed mode, the resolver
validates the stable open proposal before deciding whether to allocate the
matrix. Current proposal evidence is reused. If only unrelated `main` changes
are missing, the proposal branch is synchronized without another scorecard.
Proposal state cannot override event relevance: absent, invalid, or stale
proposal evidence does not make an unrelated push allocate the matrix. A
relevant push, scheduled run, or manual run replaces that evidence. A proposal
branch that changes any path other than the canonical baseline fails in the
resolver before the matrix starts; automation never overwrites or normalizes
that unexpected branch state.

Every seed job must pass before automation combines and validates the complete
LTS target matrix. The workflow writes that candidate to a stable
automation branch and opens or updates one pull request. It enables squash
auto-merge through a private, repository-scoped GitHub App but never approves
its proposal. Only the proposal-update jobs receive `GITHUB_TOKEN` contents
write authority, and only the pull-request writer receives `GITHUB_TOKEN`
pull-request write authority. Every measurement job remains read-only.
Tree-diff guards confine both proposal creation and synchronization to the
canonical baseline file.

Branch updates made with `GITHUB_TOKEN` do not recursively start normal push
or pull-request workflows. After creating or synchronizing the proposal, the
controller therefore dispatches a restricted CI profile on the exact proposal
head. The profile runs only the three protected repository checks and skips
the expensive scheduled and full-dispatch jobs. The built-in token retains
branch and pull-request updates, which prevents an additional full PR workflow
fan-out. A short-lived installation token is minted only when a semantic
baseline proposal may need maintenance. The resolver immediately revokes that
unused preflight token before allocating scorecard runners. After an actual
proposal update, a fresh token is restricted to this repository and the
`contents` and `pull-requests` write permissions and is used only to register
squash auto-merge. GitHub attributes that API request to the App rather than
`GITHUB_TOKEN`, so the eventual `main` push starts the normal repository
workflows. An independent maintainer approval and every protected check remain
mandatory. Protected-branch review remains the acceptance boundary. No PAT,
automatic approval, administrative bypass, or downloaded handoff artifact is
involved.

The controller distinguishes GitHub's commit-bot identity from its
pull-request Actor identity. An auto-merge request created with `GITHUB_TOKEN`
reports `app/github-actions`, not `github-actions[bot]`. The controller rebinds
that legacy request to the dedicated App and reads the resulting actor back
before it accepts either an open auto-merge request or an immediately completed
merge. The legacy Actor representation was verified against repository
baseline PR 42 on 2026-08-14 instead of being inferred from the commit-bot
display name.

The legacy rebind is a transition mechanism, not a permanent compatibility
surface. Remove it, together with its contract assertions, after the first
baseline proposal registered by the dedicated App has merged successfully and
the repository has no open baseline proposal whose Actor is
`app/github-actions`. The removal change must cite that pull request and its
workflow run so the criterion is satisfied by observed repository state.

The App private key remains an organization secret restricted to this one
repository. An unprotected Actions environment would not add an authorization
boundary because a reviewed workflow can reference it, while a required-review
environment would add a second human gate to every automated baseline cycle.
The accepted boundary is therefore protected workflow review, a repository-only
App installation, and job-level token permission narrowing. This decision is
revisited if GitHub offers a non-interactive environment protection rule that
can bind only this maintenance transition without adding an operator handoff.

Every `main` push reaches the resolver so required-check coverage cannot be
skipped by a path filter. The resolver delegates its common input policy to the
release-evidence classifier, preventing `main` and release-candidate relevance
from drifting apart. Workflow concurrency queues later pushes instead of
cancelling a scorecard that already started; an unrelated queued push then
reduces to the inexpensive no-op or synchronization path.

Every release-candidate run remains strict: no matching accepted runner matrix
is a hard failure during the inexpensive preflight. Seed mode still enforces
the complete workload, absolute budgets, statistics, allocation, GC, soak,
environment identity, and host admission; it omits only the unavailable
historical comparison. Contract changes do not merge older-contract runner
groups into the new candidate. Merging the accepted baseline does not rerun
the scorecard because the baseline file is deliberately excluded from the
relevant-input classifier.

The release-candidate workflow calls the reusable scorecard once for the exact
release commit. Its import stages verify each selected attempt against the
engine target, commit, selection receipt, and evaluation digest before copying
the complete raw evidence into the release evidence directory. The release
candidate verifies the imported target-matrix selections and does not repeat the
measurement or classify the same reports under a second run identity.

Smoke, scorecard, and stress runs have hard total deadlines of 10 minutes,
30 minutes, and two hours. The deadline helper owns a new process group,
forwards operator termination, and escalates from cooperative termination to a
forced stop so BenchmarkDotNet or shell descendants cannot survive the run.
Each completed workload is atomically checkpointed against contract version,
run ID, target, profile, commit, source hash, runner class, workload ID, and
family. A resumed run can therefore lose at most the workload that was active
at interruption. Expensive workloads use reviewed, named timeout floors from
the performance contract, while every hosted release stage has its own bounded
workflow timeout. The release candidate uses digest-verified, source-bound
receipts at every major stage instead of one global deadline.

### Consequences

- Good, because a release must prove complete named behavior rather than the
  presence of benchmark files.
- Good, because the gate preserves samples and independently recomputes tail
  statistics.
- Good, because failures, missing targets, stale runs, noisy measurements, and
  weakened report budgets fail closed.
- Good, because a saturated or ambiguously identified host fails before it can
  produce misleading evidence, while ordinary workstation activity is
  normalized beside each workload.
- Good, because hard deadlines clean up complete process trees and verified
  checkpoints retain completed work.
- Good, because absolute, historical, and sustained-resource regressions are
  separately diagnosable.
- Bad, because a new runner class or evidence contract requires review and
  acceptance of one complete LTS-matrix seed candidate before strict
  comparisons and release qualification can pass.
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
  eng.tests.test_performance_contract \
  eng.tests.test_performance_confirmation \
  eng.tests.test_performance_host \
  eng.tests.test_performance_reports \
  eng.tests.test_performance_baseline \
  eng.tests.test_benchmark_ratio_gate
```

- Run a scorecard and soak pass for each engine with one stable runner class.
- Confirm each run persists a successful host preflight and matching
  BenchmarkDotNet/workload processor identity, adjacent calibration samples,
  and source-bound workload checkpoints.
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
- 2026-08-03: Replaced idle-host dependence with workload-local CPU/database
  calibration, normalized historical latency, contract-selected
  BenchmarkDotNet controls, hard process-tree deadlines, atomic workload
  checkpoints, digest-verified release-stage continuation, and exact
  confirmation testing for historical p99 exceedances.
- 2026-08-04: Added contract-validated workload timeout floors for fixed large
  I/O populations and explicit matrix-versus-workload timeout diagnostics.
  Sampling, statistical, allocation, and historical regression budgets remain
  unchanged, so existing accepted baselines remain compatible.
- 2026-08-06: Replaced lifetime process CPU admission with bounded interval
  host-CPU sampling from Linux and macOS operating-system counters. Added a
  pre-matrix baseline-mode resolver so scheduled hosted runs produce a
  reviewable seed candidate for new evidence contracts while explicit compare
  and release-candidate paths remain fail-closed.
- 2026-08-07: Replaced manual artifact download, baseline installation, and
  repeated benchmark dispatch with one stable validated baseline pull request.
  Added a locally tested event and proposal resolver, current-proposal reuse,
  cheap synchronization after unrelated changes, least-privilege proposal
  authority, normal approval-gated pull-request checks, and a strict release
  preflight.
- 2026-08-07: Enabled squash auto-merge for the validated baseline proposal.
  Independent maintainer approval and every protected check remain mandatory;
  the workflow has no review or administrative-bypass command.
- 2026-08-14: Moved squash auto-merge authority from `GITHUB_TOKEN` to a
  private, repository-scoped GitHub App. The App token is short-lived and
  permission-restricted, never approves or bypasses the proposal, and lets the
  resulting `main` push produce commit-exact release qualification evidence.
- 2026-08-07: Isolated the measured scorecard in a reusable workflow. The
  parent benchmark workflow and its event resolver are now an explicitly cheap
  control plane, so orchestration-only changes cannot allocate the hosted
  contract-derived LTS benchmark matrix.
- 2026-08-07: Unified hosted-push and release-candidate performance relevance.
  Provider, benchmark, database-image, build, SDK, harness, evaluator, and
  reusable scorecard changes now run every hosted LTS scorecard on `main`.
  Proposal health cannot independently allocate that work after an unrelated
  push.
- 2026-08-08: Added typed scorecard attempt receipts and one fresh-runner retry
  exclusively for inconclusive measurement quality. Hard failures remain
  terminal. The release-candidate workflow now imports target-, commit-, and
  digest-bound evidence from the single reusable scorecard instead of running
  or evaluating the performance matrix twice.
- 2026-08-13: Expanded accepted baselines, automated scorecards, and release
  qualification from two representative engines to the complete active LTS
  target matrix derived from the performance contract. Registered a fixed
  ten-block paired population with executable power assurance and removed
  result-driven statistical retries.

### Implementation References

- `benchmarks/performance-contract.json`
- `benchmarks/baselines/doka-benchmark-baseline.json`
- `benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks`
- `eng/benchmark.sh`
- `eng/performance/workflow_state.py`
- `eng/performance/sensitivity.py`
- `eng/performance/cli.py`
- `eng/performance/check-benchmark-ratios.sh`
- `eng/tests/test_performance_contract.py`
- `eng/tests/test_performance_confirmation.py`
- `eng/tests/test_performance_host.py`
- `eng/tests/test_performance_reports.py`
- `eng/tests/test_performance_baseline.py`
- `eng/tests/test_benchmark_ratio_gate.py`
- `eng/tests/test_benchmark_workflow_state.py`
- `.github/workflows/benchmark.yml`
- `.github/workflows/benchmark-smoke.yml`
- `.github/workflows/benchmark-scorecard.yml`
- `.github/workflows/benchmark-target.yml`
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
- [Linux `ps(1)` CPU semantics][linux-ps]
  (primary source; retrieved 2026-08-06)
- [Linux `/proc/stat` CPU counters][linux-proc]
  (primary source; retrieved 2026-08-06)
- [Linux CPU-load accounting][linux-cpu-load]
  (primary source; retrieved 2026-08-06)
- [Apple `host_statistics64`][apple-host-statistics64]
  (primary source; retrieved 2026-08-06)
- [Apple `host_cpu_load_info_t`][apple-host-cpu-load]
  (primary source; retrieved 2026-08-06)
- [GitHub automatic token authentication][github-token-authentication]
  (primary source; retrieved 2026-08-07)
- [GitHub workflow-trigger behavior][github-workflow-events]
  (primary source; retrieved 2026-08-07)
- [GitHub required status checks][github-required-checks]
  (primary source; retrieved 2026-08-07)
- [GitHub skipped-workflow status behavior][github-skipped-workflows]
  (primary source; retrieved 2026-08-07)
- [GitHub Actions policy settings][github-actions-policy]
  (primary source; retrieved 2026-08-07)
- [GitHub pull-request auto-merge][github-auto-merge]
  (primary source; retrieved 2026-08-07)
- [GitHub CLI `pr merge`][github-cli-pr-merge]
  (primary source; retrieved 2026-08-07)
- [GitHub App installation authentication][github-app-authentication]
  (primary source; retrieved 2026-08-14)
- [GitHub `create-github-app-token` action][github-app-token-action]
  (primary source; retrieved 2026-08-14)
- [GitHub ruleset pull-request review rules][github-ruleset-reviews]
  (primary source; retrieved 2026-08-14)
- [GitHub deployment environments][github-deployment-environments]
  (primary source; retrieved 2026-08-14)
- [Repository baseline PR 42 Actor evidence][baseline-pr-42]
  (primary source; retrieved 2026-08-14)

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
[linux-ps]: https://www.man7.org/linux/man-pages/man1/ps.1.html
[linux-proc]: https://docs.kernel.org/filesystems/proc.html
[linux-cpu-load]: https://docs.kernel.org/admin-guide/cpu-load.html
[apple-host-statistics64]:
  https://developer.apple.com/documentation/kernel/1502863-host_statistics64
[apple-host-cpu-load]:
  https://developer.apple.com/documentation/kernel/host_cpu_load_info_t
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
[github-auto-merge]:
  https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-auto-merge-for-pull-requests-in-your-repository
[github-cli-pr-merge]: https://cli.github.com/manual/gh_pr_merge
[github-app-authentication]:
  https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation
[github-app-token-action]:
  https://github.com/actions/create-github-app-token
[github-ruleset-reviews]:
  https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets
[github-deployment-environments]:
  https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments
[baseline-pr-42]:
  https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/pull/42
