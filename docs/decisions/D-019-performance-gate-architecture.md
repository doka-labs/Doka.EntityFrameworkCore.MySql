---
id: D-019
status: implemented
date: 2026-05-18
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Benchmark baselines, ratio and allocation assertions, and CI activation"
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

- Identifier-Quoting hot path >= 2x throughput vs. a naive reference
  implementation.
- BulkInsert of 1000 rows >= 3x throughput vs. per-row SaveChanges.
- JSON-ChangeTracking equality comparison >= 80% allocation reduction vs. a
  naive string round-trip.

These thresholds need a mechanically asserted gate so a future code change
that regresses any of them fails the release path rather than silently
shipping. BenchmarkDotNet (BDN) already produces structured reports for
operator runs through `eng/benchmark.sh`; the reports need deterministic
evaluation.

Query translation is also a provider hot path and a broad allocation surface.
Its original benchmark corpus covered only string length, date year, JSON
contains, and spatial distance. It did not exercise the split GUID, byte-array,
numeric conversion, math, string, temporal, or signed-bitwise translators.
Those paths need a representative corpus and a deterministic memory ceiling.

## Decision Drivers

- Performance targets need mechanical pass or fail evidence.
- Measurements must compare on the same hardware and run.
- Allocation-sensitive translation paths need a stable absolute ceiling.
- Continuous benchmark cost must fit the available CI budget.

## Considered Options

- Strict release-candidate gate with deferred per-PR benchmark CI
- Run the full benchmark gate on every pull request
- Keep benchmarks informational

## Decision Outcome

Chosen option: "Strict release-candidate gate with deferred per-PR benchmark
CI", because the active release-candidate workflow is the enforceable boundary
while per-PR benchmark CI remains trigger-driven.

The gate is composed of two layers:

### 1. In-benchmark controls and allocation budgets

Each ratio-gated scenario carries a
`[Benchmark(Baseline = true)]`-marked naive reference implementation in the
same benchmark class as the fast path. BenchmarkDotNet's ratio then expresses
`Mean[gated] / Mean[baseline]`, with both measurements taken on the same
hardware in the same JIT pass.

| Scenario | Baseline | Gated method | Metric | Maximum |
|---|---|---|---|---:|
| `IdentifierQuotingBenchmark` | `NaiveDelimitStringPlain` | `DelimitStringPlain` | mean ratio | `0.5` |
| `BulkInsertBenchmark` | `PerRowSaveChanges` | `MultiRowAddRangeSaveChanges` | mean ratio | `0.333` |
| `JsonComparerBenchmark` | `NaiveJsonElementEqualsLoop` | `JsonElementEqualsLoop` | allocation ratio | `0.2` |
| `QueryTranslationBenchmarks` | absolute budget | `TranslateRepresentativeCorpus` | allocated bytes | `163840` |

Within-run baseline comparison was preferred over a seeded earlier
measurement because shared CI runners and local machines produce noisy
absolute timings. Ratios computed inside the same BDN pass absorb much of that
noise because numerator and denominator see the same CPU state.

The query-translation benchmark deliberately uses an absolute allocation
ceiling rather than a synthetic slow baseline. A deliberately inefficient
translator would not represent a useful production comparison. Managed bytes
per operation are deterministic enough for this gate, while latency remains
report evidence because the smoke job's five iterations are too noisy for an
absolute time limit.

Corpus version 2.0.0 contains twelve scenarios. The accepted 2026-07-29
calibration measured 140,000 bytes on MySQL 8.4 and 139,968 bytes on MariaDB
11.8. The 163,840-byte ceiling leaves 17.0% headroom above the larger result
for runtime and instrumentation variance while still failing a material
allocation regression.

### 2. eng/check-benchmark-ratios.sh

The gate script walks every `*-report-full.json` under the benchmark artifacts
root. It evaluates both relative tuples and absolute allocation tuples, then
emits one of three verdicts per scenario:

- `PASS`: ratio computed and within threshold.
- `FAIL`: a ratio or absolute value exceeds its threshold; exit `1`.
- `SKIP`: one required engine target lacks the configured scenario.

A non-strict default treats `SKIP` as advisory.
`DOKA_BENCHMARK_GATE_STRICT=1` promotes missing data to a hard failure
(`exit 2`) so every release-candidate gate must report a verdict for both
`mysql84` and `mariadb118`.

### 3. Release-candidate integration

The scheduled and manually dispatched
`.github/workflows/release-candidate.yml` workflow is active. It runs both
engine benchmark smokes with one release-candidate run ID, then invokes the
strict gate against only that run's reports. Historical or local reports
cannot satisfy the gate.

Continuous `.github/workflows/benchmark.yml` execution remains deferred.
Operator runs remain available through `eng/benchmark.sh`.

### Consequences

- Good, because tag eligibility requires strict same-run evidence on both
  engine families.
- Good, because translation allocation growth now fails at a fixed,
  reviewable boundary.
- Bad, because a regression can remain on main until the next scheduled or
  manual release-candidate run.

#### Positive

- Performance regression detection is **mechanically asserted**. The script
  is deterministic for a fixed report set.
- Operators can run the benchmark and gate locally before tagging; any missed
  threshold fails loudly.
- Continuous CI activation requires no new benchmark or gate architecture.
- Ratio and absolute gates can be extended independently without inventing a
  synthetic control benchmark.
- Query translation exercises twelve representative translator families on
  both supported engine families.

#### Negative

- A regression that lands before release-candidate validation is detected
  later than a per-change gate would detect it.
- Operator-triggered local gates still rely on contributor discipline.
- Naive variants must remain representative when their fast path changes.

#### Neutral

- The `pending-seed` long-term baseline manifest addresses cross-release
  trends and remains independent from these release gates.

### Confirmation

- Run `eng/release-candidate.sh` without the development-only benchmark bypass.
- Verify the weekly `release-candidate` workflow executes the strict benchmark
  gate.
- Run the benchmark-gate regression tests:

```bash
python3 -m unittest eng.tests.test_benchmark_ratio_gate
```

## Pros and Cons of the Options

### Strict release-candidate gate with deferred per-PR benchmark CI

- Good, because every release candidate runs same-pass baselines without
  charging every pull request.
- Bad, because regressions are detected at release-candidate time rather than
  immediately after merge.

### Run the full benchmark gate on every pull request

- Good, because performance regressions surface before merge.
- Bad, because dual-engine benchmark time would consume a disproportionate
  shared CI budget.

### Keep benchmarks informational

- Good, because contributors can inspect reports without flaky gate failures.
- Bad, because a release can ship below an explicit performance target.

## More Information

### Constraint

The project lives in an organization plan with a shared `2000` CI-minute
monthly budget across multiple repositories. A representative dual-engine
smoke consumes `15-30` minutes per matrix entry. Per-change execution would
consume a disproportionate share of that budget.

The performance-gate infrastructure must therefore be built so that:

- The gate is **executable** without CI.
- The gate is **ready** for continuous CI when sufficient capacity exists.
- The gate is **deterministic** for a fixed set of structured reports.

### Why this split

- **Release-candidate gating protects the publication boundary.** A tag cannot
  use incomplete or failing benchmark evidence.
- **Continuous execution has a material budget cost.** The current dual-engine
  smoke would consume a large share of the organization-wide allowance.
- **Within-run comparison reduces timing noise.** Ratio controls see the same
  machine and runtime conditions in one BDN pass.
- **Allocation ceilings complement ratios.** A representative query-translation
  corpus has no honest naive implementation. Its managed allocation count is
  stable across repeated runs and therefore supports a direct upper bound.

### Additional Alternative Rationale

- **Seeded baseline against stored timings.** Rejected because shared-runner
  timing noise would require a wide regression buffer. The manifest remains
  available for a dedicated runner.
- **Pomelo as comparator.** Rejected because it adds a benchmark dependency
  without improving the provider's release invariant.
- **Per-PR benchmark gate.** Rejected under the current shared CI allowance.
- **Informational reports only.** Rejected because explicit performance
  targets require machine-enforced verdicts.

### References

- Performance-target source: the v1.0 release targets.
- Baseline-variant commit: `14df9df368b2`
  (`feat(benchmarks): add baseline-marked naive variants for ratio gates`).
- Gate-script commit: `a51e22afdd65` (`feat(eng): add benchmark ratio gate script`).
- The translation-corpus calibration is retained in the 2026-07-29 decision
  history and reproducible from the implementation references.

### Re-evaluation Triggers

The CI activation is re-evaluated under any of:

- The organization receives enough CI capacity for continuous benchmark
  sweeps; enable `benchmark.yml` and its strict gate.
- A dedicated benchmark runner becomes available; enable continuous execution.
- A regression reaches a release despite release-candidate evidence; tighten
  the gate or its invocation before the next release.
- The translation corpus changes; recalibrate the absolute allocation ceiling
  on both engine families in the same change.

### Decision History

- 2026-05-18: Decision recorded with status implemented.
- 2026-05-18: Gate infrastructure accepted with continuous CI activation deferred.
- 2026-07-27: Strict benchmark execution confirmed in the scheduled and manual
  release-candidate path.
- 2026-07-27: Replaced serialization-based JSON equality with .NET 10
  structural DOM comparison after the strict gate exposed a `1.4071` ratio.
- 2026-07-27: Scoped strict evaluation to the current release-candidate run ID.
- 2026-07-27: Migrated to Doka MADR profile 1.0.
- 2026-07-29: Expanded the translation corpus from four to twelve scenarios
  and added the 163,840-byte dual-engine allocation ceiling.
- 2026-07-29: Required every strict gate tuple on both benchmark targets.

### Implementation References

- `eng/check-benchmark-ratios.sh`
- `eng/tests/test_benchmark_ratio_gate.py`
- `benchmarks/corpora/translation-corpus.json`
- `benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks/ProviderBenchmarks.cs`
- `eng/release-candidate.sh`
- `.github/workflows/release-candidate.yml`
- `.github/workflows/benchmark.yml`

### Sources

- [.NET 10 `JsonElement.DeepEquals`
  API](https://learn.microsoft.com/dotnet/api/system.text.json.jsonelement.deepequals?view=net-10.0)
  (primary source; retrieved 2026-07-27)
- [.NET 10 `JsonElement.DeepEquals`
  source](https://source.dot.net/System.Text.Json/System/Text/Json/Document/JsonElement.cs.html)
  (primary source; retrieved 2026-07-29)
- [EF Core value
  comparers](https://learn.microsoft.com/ef/core/modeling/value-comparers)
  (primary source; retrieved 2026-07-27)
