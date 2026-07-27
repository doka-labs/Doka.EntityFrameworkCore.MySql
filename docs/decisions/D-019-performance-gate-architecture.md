---
id: D-019
status: implemented
date: 2026-05-18
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Benchmark baselines, ratio assertions, and CI activation"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-019 -- Gate performance in the release-candidate path

## Context and Problem Statement

The v1.0 release was scoped with three concrete performance targets:

- Identifier-Quoting hot path >= 2x throughput vs. a naive reference implementation.
- BulkInsert of 1000 rows >= 3x throughput vs. per-row SaveChanges.
- JSON-ChangeTracking equality comparison >= 80% allocation reduction vs. a naive string round-trip.

These thresholds need a mechanically-asserted gate so a future code change that regresses any of them fails the pipeline rather than silently shipping. The plain BenchmarkDotNet (BDN) report flow already runs nightly via `.github/workflows/benchmark.yml` (currently disabled) and exists for ad-hoc operator runs via `eng/benchmark.sh`, but the report is informational -- a regression below threshold produces no automated failure.

## Decision Drivers

- Performance targets need mechanical pass or fail evidence.
- Measurements must compare on the same hardware and run.
- Continuous benchmark cost must fit the available CI budget.

## Considered Options

- Strict release-candidate gate with deferred per-PR benchmark CI
- Run the full benchmark gate on every pull request
- Keep benchmarks informational

## Decision Outcome

Chosen option: "Strict release-candidate gate with deferred per-PR benchmark CI", because the active release-candidate workflow is the enforceable boundary while per-PR benchmark CI remains trigger-driven.

The gate is composed of two layers:

### 1. In-benchmark baseline variants

Each gated scenario carries an `[Benchmark(Baseline = true)]`-marked naive reference implementation in the SAME benchmark class as the fast-path under test. BenchmarkDotNet's Ratio column then expresses `Mean[gated] / Mean[baseline]` directly, with both measurements taken on the same hardware in the same JIT pass.

| Scenario | Baseline method | Gated method | Metric | Threshold (max) |
|---|---|---|---|---|
| `IdentifierQuotingBenchmark` | `NaiveDelimitStringPlain` (per-char StringBuilder + manual backtick-escape loop) | `DelimitStringPlain` (Span-based fast-path via `ISqlGenerationHelper`) | mean ns | `0.5` |
| `BulkInsertBenchmark` | `PerRowSaveChanges` (1000x Add + SaveChangesAsync) | `MultiRowAddRangeSaveChanges` (AddRange + 1x SaveChangesAsync) | mean ns | `0.333` |
| `JsonComparerBenchmark` | `NaiveJsonElementEqualsLoop` (per-call `GetRawText` + Ordinal string-compare) | `JsonElementEqualsLoop` (.NET 10 `JsonElement.DeepEquals` DOM walk through `MySqlJsonValueComparers`) | bytes allocated per operation | `0.2` |

Within-run baseline comparison was preferred over "vs. a seeded earlier measurement" because shared CI runners (and even local laptops under varying thermal / background-load conditions) produce absolute timings with 10-30% noise. Ratios computed inside the same BDN pass absorb that noise -- both numerator and denominator see the same CPU state.

### 2. eng/check-benchmark-ratios.sh

The gate script walks every `*-report-full.json` under the benchmark artifacts root, locates the configured `(class, baseline-method, gated-method, metric, threshold)` tuples, computes the ratio per report, and emits one of three verdicts per scenario:

- `PASS`: ratio computed and within threshold.
- `FAIL`: ratio computed and exceeds threshold; exit `1`.
- `SKIP`: configured scenario produced no benchmark data in any report (typical when a Docker-dependent scenario like `BulkInsertBenchmark` is omitted from a no-DB run).

A non-strict default treats `SKIP` as advisory; `DOKA_BENCHMARK_GATE_STRICT=1` promotes missing data to a hard failure (`exit 2`), intended for release-candidate validation where every gate must report a verdict.

### 3. CI integration deferred

`.github/workflows/benchmark.yml` and `.github/workflows/release-candidate.yml` stay disabled. The gate script is operator-runnable today as part of a release-preparation checklist (see CONTRIBUTING.md). When budget conditions change, re-enabling `benchmark.yml` plus adding one `run: bash ./eng/check-benchmark-ratios.sh artifacts/benchmarks` step is the entire activation cost.

### Consequences

- Good, because tag eligibility now requires strict same-run ratio evidence on both engine families.
- Bad, because a regression can remain on main until the next scheduled or manual release-candidate run.

#### Positive

- Performance regression detection is **mechanically asserted** rather than reviewer-judgment. The script is deterministic.
- Operator can run `./eng/benchmark.sh --up-smoke-down && ./eng/check-benchmark-ratios.sh artifacts/benchmarks` locally before tagging a release; the gate fails loudly if any threshold is missed.
- CI integration is **single-step activation** when budget allows. No structural changes required at activation time.
- The gate is **scenario-extensible**: adding a fourth gated scenario is a one-line append to the `gates` array in the script plus a `[Benchmark(Baseline = true)]` marker on the corresponding naive variant.

#### Negative

- A regression that lands and ships before the next release-candidate validation goes undetected by the gate. Mitigation: the gate IS the release checklist gate; missing the step at release-prep is itself a process violation.
- Operator-triggered gates rely on human discipline. The trigger sits in CONTRIBUTING.md but cannot be enforced mechanically until CI activation.
- Naive-variant maintenance: when the fast-path changes substantially, the naive baseline must stay representative (still measures the "without our optimization" case). Diff-time discipline.

#### Neutral

- The existing `benchmarks/baselines/doka-benchmark-baseline.json` manifest (in `pending-seed` state) is unaffected by this ADR. It addresses a different question -- long-term seeded regression-tracking across releases -- and remains available for a future enhancement.

### Confirmation

- Run `eng/release-candidate.sh` without the development-only benchmark bypass.
- Verify the weekly `release-candidate` workflow executes the strict ratio gate.

## Pros and Cons of the Options

### Strict release-candidate gate with deferred per-PR benchmark CI

- Good, because every release candidate runs same-pass baselines without charging every pull request.
- Bad, because regressions are detected at release-candidate time rather than immediately after merge.

### Run the full benchmark gate on every pull request

- Good, because performance regressions surface before merge.
- Bad, because dual-engine benchmark time would consume a disproportionate shared CI budget.

### Keep benchmarks informational

- Good, because contributors can inspect reports without flaky gate failures.
- Bad, because a release can ship below an explicit performance target.

## More Information

### Current workflow state

The scheduled and manually dispatched `.github/workflows/release-candidate.yml`
workflow is active and invokes `eng/release-candidate.sh`, which runs both
engine benchmark smokes and the strict ratio gate. Both engine runs share the
release-candidate run ID, and the gate evaluates only reports produced under
that ID so stale local evidence cannot affect the verdict. Only the continuous
`.github/workflows/benchmark.yml` path remains deferred. Statements below that
describe both workflows as disabled are retained solely as historical context.

### Constraint

The project lives in an org plan with a shared `2000` CI-minute monthly budget across multiple repos. Per-PR or per-merge benchmark runs are not viable at this budget tier: a representative dual-engine smoke run consumes `15-30` minutes per matrix entry, which would saturate the budget within `4-6` weeks of normal PR cadence even before container-matrix and release-candidate workflows are re-enabled.

The performance-gate infrastructure must therefore be built so that:

- The gate is **executable** without CI -- an operator runs it locally before a release candidate.
- The gate is **structurally ready** for CI activation when the org plan changes or a project-specific budget allocation appears, with zero additional infrastructure work.
- The gate is **deterministic** -- the same inputs produce the same pass/fail verdict regardless of where it runs.

### Why this split

- **A gate failing only at release-candidate time is still a gate.** The DoD requirement is "Benchmark suite runs with baseline-snapshot" -- not "runs on every PR". An operator-triggered gate before tagging meets the substantive risk-management goal (no release with a known performance regression) at a fraction of the CI cost.
- **Padding the gate into CI now would burn budget on the wrong signal.** With both engines and three benchmark classes, even a single nightly run would consume 30-60 minutes per night = 900-1800 minutes per month = 45-90% of the entire org budget for one project's nightly benchmark sweep. Other projects on the same org plan would lose their share.
- **Within-run baseline comparison sidesteps the noise floor.** A seeded-baseline approach (commit reference numbers, compare later) would require either large regression-buffers (10-20%) to avoid false failure on runner-variability days, or a dedicated benchmark-runner outside GitHub Actions. The within-run pattern needs neither.

### Additional Alternative Rationale

- **Seeded-baseline + compare-against-stored-numbers approach.** Rejected for the v1.0 cycle: high noise floor on shared runners forces large regression-buffers; the seeded-baseline manifest stays available for a post-v1.0 enhancement when a dedicated runner removes the noise concern.
- **Pomelo-as-comparator approach.** Rejected: pulls Pomelo as a benchmark-only NuGet dependency plus parallel DI setup; the marketing value ("3x faster than Pomelo") does not justify the dependency-policy decision-brief overhead at v1.0 time.
- **Per-PR benchmark gate.** Rejected: budget-incompatible at the org's current `2000` CI-minute monthly tier. Would consume `45-90%` of the org-shared budget on this one repo's nightly sweep alone.
- **No gate, rely on reviewer attention.** Rejected: substantive risk -- the DoD targets are specific ratios, not vibes; without a mechanical asserter a regression > 50% on the IdentifierQuoting fast-path could ship unnoticed.

### References

- Performance-target source: the v1.0 release-substantive targets (IdQuote >= 2x, BulkInsert >= 3x, JSON-alloc >= 80% reduction).
- Baseline-variant commit: `14df9df368b2` (`feat(benchmarks): add baseline-marked naive variants for ratio gates`).
- Gate-script commit: `a51e22afdd65` (`feat(eng): add benchmark ratio gate script`).
- BulkInsertBenchmark already shipped `[Benchmark(Baseline = true)]` on `PerRowSaveChanges` via the multi-row-INSERT + RETURNING work; this ADR documents the now-uniform pattern across all three gated scenarios.

### Re-evaluation Triggers

The CI activation is re-evaluated under any of:

- **Trigger 1 -- org plan changes.** When the org's CI minute budget moves to a tier that comfortably accommodates a nightly benchmark sweep (estimate: budget >= 6000 min/month so a 30 min/night benchmark sweep consumes <= 15% of the budget while leaving headroom for other workflows), re-enable `benchmark.yml` + add the gate `run:` step. No code change to the gate script or baseline variants is needed.
- **Trigger 2 -- project-specific runner.** A self-hosted GitHub Actions runner dedicated to this repository changes the cost model entirely; runner time is no longer billable against the org plan. Same re-enable path as Trigger 1.
- **Trigger 3 -- a real regression slips past operator-triggered gate.** If a release ships with a measurable performance regression that the local gate did not catch (operator forgot the step, or the gate logic missed the case), the response is to enable the gate on at least a weekly cron + accept the budget cost.
- The organization receives enough CI capacity for continuous benchmark sweeps.
- A dedicated benchmark runner becomes available.
- A regression reaches a release because release-candidate evidence was bypassed.

### Decision History

- 2026-05-18: Decision recorded with status implemented.
- 2026-05-18: Gate infrastructure accepted with continuous CI activation deferred.
- 2026-07-27: Strict benchmark execution confirmed in the scheduled and manual release-candidate path.
- 2026-07-27: Replaced the serialization-based JSON equality path with the .NET 10 structural DOM comparison after the strict gate exposed a `1.4071` allocation ratio.
- 2026-07-27: Scoped strict ratio evaluation to the current release-candidate run ID so historical reports cannot create false failures.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the per-PR CI deferral.

### Implementation References

- `eng/check-benchmark-ratios.sh`
- `eng/release-candidate.sh`
- `.github/workflows/release-candidate.yml`
- `.github/workflows/benchmark.yml`

### Sources

- [.NET 10 `JsonElement.DeepEquals` API](https://learn.microsoft.com/dotnet/api/system.text.json.jsonelement.deepequals?view=net-10.0) (primary source; retrieved 2026-07-27)
- [.NET 10 `JsonElement.DeepEquals` source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Text.Json/src/System/Text/Json/Document/JsonElement.cs) (primary source; retrieved 2026-07-27)
- [EF Core value comparers](https://learn.microsoft.com/ef/core/modeling/value-comparers) (primary source; retrieved 2026-07-27)
