# Performance Evidence Reference

This reference owns benchmark profiles, evidence layouts, measurement-quality
states, and soak invariants. Use
[Performance Evidence](performance-evidence.md) for commands and failure
triage, [Paired Performance Methodology](paired-performance-methodology.md) for
the statistical design, and
[Performance Baseline Operations](performance-baseline-operations.md) for
baseline and proposal procedures.

The versioned source of truth is
`benchmarks/performance-contract.json`; this page explains the fields that an
operator or reviewer must interpret.

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

The achieved population travels in each workload report. If the two
populations exceed the registered ratio, the evidence is complete but does not
describe comparable measurement windows. The evaluator reports
`measurement-inconclusive`, allowing the one independent retry, rather than
misclassifying runner noise as non-retryable malformed evidence.

After warmup, `paired-block` measures one configured operation batch and uses
that pilot to distribute 120 percent of the two-second duration floor over the
workload's starting population. A sixteen-sample workload therefore targets
150 milliseconds per sample, while an explicitly registered 8,192-sample
workload targets about 293 microseconds. The multiplier is rounded up and
capped at 1,024. Every workload uses the fastest of three pilot observations so
one scheduler stall cannot undersize it. This plans at least 2.4 seconds of
measured work without inflating high-population tail workloads or consuming the
separate sample-count cap. The reference and candidate sides pilot
independently because sample size is an execution property; the paired
statistic continues to compare normalized time per operation.

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

`paired-block`, `scorecard`, and `stress` execute the complete 55-cell workload
matrix; only `smoke` narrows it. Expensive cells retain at least 100 observations
for p99 while avoiding a second full population of large writes. The scorecard
accepts at most 25 percent relative standard error; stress accepts at most 15
percent. An enforcing workload that misses its error budget is extended in
calibration-aligned blocks up to the contract-owned multiplier. The workload
and matrix deadlines bound that extension, and they are the real ceiling: the
multiplier only keeps a single workload from consuming the matrix budget. The
current value retains headroom above the largest observation in the accepted
two-target baseline that preceded the LTS-matrix expansion:

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

The runner never weakens the error budget or deletes observations; evidence
that remains unstable at the cap fails validation. Fast, idempotent operations
use fixed contract-owned batches so timer resolution and loop overhead cannot
dominate per-operation tail statistics. Tail outliers remain in raw p95 and p99
and must pass their independent absolute budgets. Normalized p95 must pass its
matching historical point budget. A normalized p99 point estimate above its
historical threshold triggers two bounded confirmations. The triggering
population is excluded from the verdict. The two independent confirmation
populations fail only when an exact one-sided binomial tail test establishes an
exceedance rate above one percent at the one-percent significance level. Smoke,
scorecard, and stress have hard total deadlines of 10 minutes, 30 minutes, and
two hours respectively.

The profile workload deadline is a hang detector, not a performance budget.
Every expensive workload references a named entry from `timeoutPolicies`. The
runner uses the larger of that policy's floor and the profile deadline, while
the contract validator rejects missing, unknown, unused, non-positive, or
matrix-breaking policies. This does not alter the sample population, absolute
budgets, normalized historical budgets, allocation limits, or GC limits.

The fixed 10,000-row synchronous and asynchronous `SaveChanges` populations use
a 300-second floor. Their scorecard population still contains 128 independent
observations. The large synchronous and asynchronous HiLo populations use the
`hilo-contention` policy and its 240-second floor. The remaining expensive
workloads share the 180-second `expensive-standard` policy. These centralized
declarations keep host scheduling and database cleanup inside the hang deadline
without turning the deadline into a latency budget.

HiLo insert workloads track every entity before issuing one `SaveChanges` or
`SaveChangesAsync` call per context. EF Core assigns HiLo values while entities
enter the change tracker, so this transaction boundary preserves shared HiLo
allocation and provider batching while excluding artificial per-row commit
latency. The workload reports the number of rows actually persisted and fails
if it differs from the declared population.

## Evidence Layout

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

## Measurement Quality and Termination

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
- `sample_cap_reached` although both the duration and precision target were met;
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

`measurementQualityPolicy` is a profile setting. The trigger is the termination
reason, not the duration flag. A run that met its duration and still exhausted
its budget chasing precision is exactly as unusable, so it reaches the same
verdict.

- **`enforce`** (scorecard, stress): a capped result exits with code 75. That
  means valid evidence whose required measurement quality was not achieved and
  permits one bounded retry on a fresh hosted runner. A hard validation error is
  never retried.
- **`observe`** (smoke): the evidence is published with a diagnostic and the run
  does not fail.

Either outcome carries samples against the derived cap, measured duration
against the required minimum, and achieved relative standard error against the
allowed ceiling. Under either policy a capped historical result is refused for
baseline promotion. A paired block observes its cap state while retaining the
fixed ten-block population.

### Environmental noise or an unsuitable contract

A capped result on one run and a clean result on the retry is host noise. The
same workload capping repeatedly says the profile cannot satisfy its contract
within the registered limits. For fixed historical profiles, recalibrate
exactly one evidenced lever: `operationsPerSample`, the minimum duration, or the
cap. Raising the cap alone treats the symptom; more work per sample is usually
the correct lever.

The paired profile performs sample-size calibration automatically and records
every input needed to reproduce it. Duration determines the amount of work in
one sample, while the cap limits how many samples may be collected. A repeated
paired cap therefore points to the pilot multiplier ceiling, precision policy,
or time budget rather than processor speed alone. Those bounds remain reviewed
contract changes with a version bump.

This mirrors BenchmarkDotNet's pilot separation and Go benchmark `predictN`:
duration sizes the work performed by one sample, while a separate contract
limits the number of samples.

## Soak Interpretation

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

## Primary Sources

Retrieved 2026-08-21:

- [BenchmarkDotNet accuracy and precision](https://benchmarkdotnet.org/articles/guides/accuracy-and-precision.html)
- [BenchmarkDotNet configuration](https://benchmarkdotnet.org/articles/configs/configs.html)
- [Go benchmark sample-size prediction](https://go.dev/src/testing/benchmark.go)
- [GitHub Actions job timeouts](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idtimeout-minutes)
