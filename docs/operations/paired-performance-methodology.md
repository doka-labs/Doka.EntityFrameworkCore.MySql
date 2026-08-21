# Paired Performance Methodology

This document owns the statistical design and evidence contract of automatic
paired scorecards. Use [Performance Evidence](performance-evidence.md) for
commands and triage, [Performance Evidence Reference](performance-evidence-reference.md)
for profiles and schemas, and
[Performance Baseline Operations](performance-baseline-operations.md) for
historical seeding and proposal automation.

The standalone benchmark workflow uses paired comparisons for automatic
engineering feedback. Release tags do not invoke this path, import an accepted
baseline, or consume its artifacts.

## What a Paired Run Measures

A reference and the candidate provider revision are measured alternately on
one allocated runner. The reference provider is packed from its reference
commit and bound into the candidate benchmark driver, so only the provider
differs between the two sides. Both sides share processor, runtime, engine
image, and database preparation, so those factors cancel from each ratio
instead of being matched across runs.

The order is counterbalanced: the side that measures first alternates between
blocks, so a warm-up advantage cannot accrue to one revision.

The shared cross-version driver contains only sources that can compile against
the accepted reference provider. Candidate-only BenchmarkDotNet probes remain
part of the ordinary benchmark build but do not enter either side of a paired
run. A packaged provider forces this compatible source set without depending
on a caller to repeat a second flag. Repository tests execute the production
build-only compatibility mode against the exact reference commit recorded by
the accepted baseline, so an additive provider API cannot first break the
driver after merge.

## What Decides the Run

| Check | What it answers | Failure means |
|---|---|---|
| Required latency endpoint | Does the complete matrix regress on normalized median for a supported target | The run-wide Holm decision rejects and the interval is above the practical budget |
| Observational latency endpoints | Where did normalized median, p95, or p99 move per workload | Diagnostic only; never a required endpoint |
| Resource families | Does the candidate allocate or collect more than its reference | The median block ratio exceeds a qualifying registered budget |
| Absolute ceilings | Is the candidate inside its family budgets at all | A pair that regressed together would otherwise qualify |
| Soak | Does sustained use leak | A resource invariant fails over sustained execution |

The required latency endpoint is the geometric mean, within each block, of the
complete workload matrix's normalized-median ratios. Per-workload median, p95,
and p99 intervals are observational secondary endpoints. A required regression
exists only when the run-wide Holm procedure rejects it and its interval is
above the practical budget. Statistical overlap remains visible but neither
asserts equivalence nor triggers a rerun. The ten-block population is fixed
before measurement, so repeatedly sampling until significance cannot bias the
decision.

The six required target endpoints form one family with a run-wide family-wise
error rate of `0.05`. Holm's step-down procedure runs once after all six target
artifacts exist; target jobs report only `pending-run-wide-adjustment`. Resource
ratios, absolute ceilings, and soak invariants remain hard local gates and do
not enter that statistical family.

Each target p-value comes from an exact one-sided sign-flip test over the ten
counterbalanced block log ratios centered on the practical budget. The
evaluator enumerates all 1,024 assignments, so Holm receives a calibrated
randomization p-value without Monte Carlo error. BCa supplies the effect
interval and remains separate from the hypothesis test.

## Registered Sensitivity

The block count is backed by a pre-registered sensitivity assurance. The
assurance runs the production BCa bootstrap and first Holm threshold over 200
deterministic planning experiments. The maximum log-ratio standard deviation
is `0.06048100249438095`: the one-sided 99 percent NIST upper confidence bound
over the noisiest of four digest-bound hosted characterization attempts, not a
point estimate.

At that bound the assurance requires at least 80 percent power, with a
one-sided 95 percent Wilson lower bound, to detect one aggregate regression at
`1.10` times its practical budget. It detects 180 of 200 experiments, with a
lower bound of approximately `0.8596`, and a minimum detectable
normalized-median ratio of `1.265`. A required target above the registered
dispersion is `measurement-inconclusive`; the evaluator cannot claim
sensitivity its blocks did not possess.

`uncertainResults` is an audit count, not a gate. Capping it would turn
statistical overlap into a result-dependent retry or failure condition. The
power assurance defines what the run is designed to detect; overlap below that
boundary is reported as `observed-overlap`, while absolute ceilings still
reject catastrophic latency.

The characterization is planning-only and cannot qualify a release. Its source
artifact identities and digest are contract-bound. Replay tests use the
production estimator against those hosted populations and must recover an
injected relevant regression before a contract change can land.

Every attempt emits `paired-dispersion-observation.json`. Monthly scorecards
retain these small files for thirty days and warn on `drift`; raw attempts
remain at seven days. When both attempts emit an observation, the selector
writes `paired-dispersion-confirmation.json` only after validating and binding
both receipts and projections. Two drift projections produce
`confirmed-drift`; one stable or absent projection does not. Confirmed drift
requires a D-026 amendment before the contract or target role can change. It
never blocks release, changes a role automatically, or starts another run.

## Reruns and Retries

An attempt receipt classifies the run and states whether retry is permitted.
Only `measurement-inconclusive` or an explicitly historical
`environment-not-comparable` state is retryable. Statistical overlap and a
paired sample-cap observation are fixed-population results, not attempt states.
A retry cannot select away a verdict about the code. The workflow reads the
receipt decision rather than reproducing the state list.

Exit code `1` is reserved for a conclusive evaluator-backed regression. The
paired runner accepts it only when `paired-evaluation.json` also records
`qualification: regression`. Driver compilation, startup, validation,
orchestration, and unexpected tooling failures leave as invalid evidence with
exit code `78`; they cannot be attributed to the provider under test.
The full output of a failed hosted attempt is retained for thirty days in a
target- and attempt-specific diagnostic artifact, including failures that
occur before the evaluator can write structured evidence.

## Paired Evidence Layout

```text
artifacts/benchmarks/<target>/reports/<run-id>/paired-evidence.json
artifacts/benchmarks/<target>/reports/<run-id>/paired-evaluation.json
artifacts/benchmarks/<target>/reports/<run-id>/paired-dispersion-observation.json
artifacts/benchmarks/<target>/reports/<run-id>/paired-soak.json
artifacts/benchmarks/<target>/reports/<run-id>/execution-order.json
artifacts/benchmarks/<target>/reports/<run-id>/blocks/
artifacts/dispersion-confirmation/<target>/paired-dispersion-confirmation.json
```

`execution-order.json` records one entry per block and `blocks/` holds every
per-side measurement and driver identity. The evaluator compares the recorded
order against the registered pattern. Build inputs remain under
`artifacts/paired/<target>/<run-id>/`. The standalone artifact digest-binds
every selected file; none is copied into a release candidate.

`paired-evaluation.json` is the target-local verdict document selected by the
attempt receipt and bound into paired evidence.

## What the Contract Controls

| Registered value | Enforced by |
|---|---|
| `blocks.profile`, `blocks.startingSamplesPerSidePerBlock` | Contract/profile equality and the valid-sample floor |
| `blocks.maximumSampleCountRatio` | Per-block population comparability; excess becomes retryable `measurement-inconclusive` |
| `resourceFamilies` | Qualifying allocation ratio, observational sparse Gen2 ratio, and hard candidate absolute ceiling |
| `primaryFamily`, `secondaryFamilies`, `targetRoles` | Pre-registered required and observational endpoints |
| `multipleComparison.*` | One run-wide Holm procedure over the exact required-target set |
| `executionOrder.blockPatterns` | Runner order, persisted execution order, and evaluator comparison |
| `retry.eligibleAttemptStates`, `retry.maximumRetries` | Receipt-owned retry decision and workflow condition |
| `durations.closingReserveSeconds` | Side watchdog and block forecast reserve for soak, assembly, and evaluation |
| `durations.finalizationReserveSeconds` | Additional reserve for evidence assembly and evaluation |
| `blocks.minimumValidSamples` | Per-side, per-block population floor |
| `blocks.maximumRelativeStandardError` | Per-side, per-block quality ceiling |
| `durations.maximumPairedRunSeconds` | Outer paired wall-clock bound |
| `durations.maximumWorkloadSeconds` | Per-workload bound |
| `sensitivity.*` | Characterization digest, NIST bound, production BCa/Holm replay, and registered power |

The canonical workload contract also rejects foreign schemas, impossible
termination, contradictory counts, and statistics that cannot be recomputed
from samples. The audit projection carries exactly the workload, family, and
five absolute-ceiling metrics; it cannot become an unchecked second copy of
the raw report.

Calibration origin is reconstructed from persisted pulses and each sample's
pulse index. Normalized, raw, and calibration populations must have the same
size and satisfy the recorded normalization identity. Absolute ceilings consume
raw nanosecond samples and contract-owned family assignment, never normalized
ratios or a workload's self-declared family.

A clean checkout records its Git tree identifier. A dirty local benchmark run
records a SHA-256 over commit tree, tracked diff, and every untracked source
file. The evaluator accepts only those defined identity shapes.

`evaluate-paired` is a separate trust boundary. It rechecks exact block count,
parallel record lengths, order, workload coverage, environment comparability,
candidate revision, contract digest, convergence, and sample cap. Types are
checked before values because a malformed field must be invalid evidence, not a
provider regression.

## Early Warning on the Default Branch

The `benchmark` workflow selects no measurement for unrelated changes, the
complete six-target smoke for ordinary provider source, or the complete paired
scorecard for measurement-defining inputs. Monthly and manual events always
select scorecard. Both lanes are early warning and neither qualifies nor blocks
a release. Historical scorecards exist only when `seed` creates a reviewed
replacement for a missing or incompatible accepted matrix.

## Timeouts and Interruption

Each hosted job has a bounded workflow timeout. The runner additionally
enforces the selected profile deadline and named workload timeout floor from
`timeoutPolicies`, using the stricter bound. Release qualification has no
benchmark bypass because it never runs a benchmark.

## Primary Sources

Retrieved 2026-08-21:

- [NIST/SEMATECH confidence limits for a standard deviation](https://www.itl.nist.gov/div898/handbook/eda/section3/eda358.htm)
- [Holm, A Simple Sequentially Rejective Multiple Test Procedure](https://www.jstor.org/stable/4615733)
- [Efron, Better Bootstrap Confidence Intervals](https://doi.org/10.1080/01621459.1987.10478410)
- [BenchmarkDotNet accuracy and precision](https://benchmarkdotnet.org/articles/guides/accuracy-and-precision.html)
