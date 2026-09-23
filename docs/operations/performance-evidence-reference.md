# Performance Evidence Reference

The source of truth is `benchmarks/performance-contract.json`. BenchmarkDotNet
produces the raw measurements; `PerformanceGate` reads those reports once and
returns one of three outcomes.

## Profiles

| Profile | Provider workloads | Soak |
| --- | --- | --- |
| `smoke` | Contract rows marked `smoke` | Not required |
| `scorecard` | Complete workload catalog | 2,048 iterations at concurrency 16 |
| `stress` | Complete workload catalog | 10,000 iterations at concurrency 32 |

All provider workload measurements use the repository-owned BenchmarkDotNet
job. BenchmarkDotNet owns warmup, iterations, statistical summaries, and
memory diagnostics. Profiles select workload breadth and soak depth; they do
not introduce another sampler.

## Evidence Layout

Each target and run writes only raw BenchmarkDotNet artifacts plus an optional
soak report:

```text
artifacts/benchmarks/<target>/reports/<run-id>/
|-- results/
|   `-- *-report-full.json
`-- soak.json
```

BenchmarkDotNet may add its normal logs and exports below the same artifact
root. The gate recursively selects `*-report-full.json`, validates that every
report describes the same host, and identifies provider workloads through the
`Target` and `WorkloadId` parameters.

No derived performance receipt, attempt record, confirmation artifact,
checkpoint, or promoted baseline exists. GitHub uploads the raw target
directory as the diagnostic artifact.

## Measurement Quality and Termination

The performance executable uses these exit codes:

| Exit | Meaning |
| ---: | --- |
| `0` | Complete evidence satisfies every budget. |
| `1` | Complete evidence violates at least one budget. |
| `78` | Evidence or infrastructure is invalid, so no performance verdict is possible. |

Provider evidence is invalid when any expected workload is missing, duplicated,
or undeclared; the target parameter differs; raw samples are empty, non-finite,
or non-positive; allocation or collection values are invalid; reports came
from different host identities; a debugger was attached; or a required
same-run control cannot be resolved exactly once.

For every workload, the gate recomputes median, p95, and p99 from
BenchmarkDotNet `OriginalValues`. It divides time and allocation by the
contract-owned operation batch and compares the result with the workload
family's absolute budgets. It also evaluates the named BenchmarkDotNet
allocation and same-run ratio controls.

Each control declares either one `maximum` for every engine target or a
`maximumByTarget` map covering exactly the contract's `requiredTargets`.
Ambiguous, incomplete, unknown-target, duplicate-target, negative, or non-finite
limits are invalid evidence; the gate never falls back to another target's
limit. `meanRatio` divides the measured method's mean by its named baseline
method's mean from the same run. Both methods must be reported exactly once.

The parallel sliding-cache control compares sixteen reads of one sliding key
with sixteen absolute-only reads of the same value size. It guards relative
end-to-end refresh overhead, including database transactions and round trips,
not isolated lock duration. Engine-specific limits reflect different costs;
relative comparison reduces some shared host effects but is not independent
of hardware, network, engine configuration, or measurement noise. Both paths
slowing together can leave the ratio unchanged.

There is no historical host-to-host comparison. This deliberately removes the
false precision and control-plane state required to match, retry, confirm, and
promote historical evidence across hosted runners.

## Parameterized-Collection Ratio Calibration

`ParameterizedCollectionBenchmark` measures indexed membership over 14,000
rows with a 10,000-value GUID collection. It runs explicit `EF.Parameter(...)`
(JSON), `EF.MultipleParameters(...)`, and `EF.Constant(...)` for both `Binary16`
and `Char36`. Setup and corpus creation occur outside the measured methods.

The controls compare methods from the same BenchmarkDotNet run. Throughput is
guarded only where both calibrated engine families show a material advantage;
allocation ratios also guard the JSON opt-in against constant expansion. No
absolute elapsed-time budget is used, because host CPU capacity is not a
portable performance contract.

Each ratio below divides the explicit JSON method by the named baseline.

Initial calibration on 2026-09-22 covered every required target with the
digest-pinned contract images, .NET 10.0.12, SDK 10.0.401, and BenchmarkDotNet
0.15.8 on Apple M2 Max Arm64. Each target used one launch, one warmup, and three
measurement iterations per method.

| Target | B16 JSON/multi time | B16 JSON/multi alloc | B16 JSON/constant alloc | Char36 JSON/multi time | Char36 JSON/multi alloc | Char36 JSON/constant time | Char36 JSON/constant alloc |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| MySQL 8.4 | 0.576 | 0.174 | 0.491 | 0.482 | 0.240 | 0.592 | 0.738 |
| MySQL 9.7 | 0.549 | 0.174 | 0.491 | 0.482 | 0.242 | 0.585 | 0.738 |
| MariaDB 10.11 | 0.619 | 0.153 | 0.449 | 0.221 | 0.224 | 0.269 | 0.719 |
| MariaDB 11.4 | 0.619 | 0.174 | 0.491 | 0.199 | 0.242 | 0.231 | 0.738 |
| MariaDB 11.8 | 0.611 | 0.174 | 0.491 | 0.244 | 0.242 | 0.244 | 0.714 |
| MariaDB 12.3 | 0.564 | 0.174 | 0.491 | 0.243 | 0.242 | 0.258 | 0.738 |
| Contract ceiling | 0.80 | 0.30 | 0.65 | 0.75 | 0.35 | 0.85 | 0.85 |

Ratios are rounded to three decimals for documentation; the contract ceilings
are reviewed values with headroom, not a statistical confidence bound. A red
ratio requires diagnosis of the query shape, engine, runtime, and host before
any ceiling changes.

### Selective Membership Check

The 10,000-value benchmark does not establish the right default for small
collections. A separate 2026-09-22 diagnostic used 1,000,000 rows with unique
indexes on both GUID columns and 1, 5, 50, or 500 matching keys. The same
`CountAsync` LINQ predicate was executed with the explicit `EF.Parameter(...)`
JSON strategy and with `EF.MultipleParameters(...)` for `Binary16` and `Char36`.
Each case had five warmup calls followed by five batches of twenty calls. The
table reports the range of the median batch-time ratios from two repeated runs
on the populated table, with JSON time divided by multiple-parameter time.
Values above 1 mean that JSON was slower. The isolated, digest-pinned MySQL 8.4
and MariaDB 11.8 containers ran on the Apple M2 Max Arm64 host.

| Target and GUID format | 1 key | 5 keys | 50 keys | 500 keys |
| --- | ---: | ---: | ---: | ---: |
| MySQL 8.4 `Binary16` | 0.98-1.04 | 1.00-1.10 | 0.74-0.75 | 0.40-0.44 |
| MySQL 8.4 `Char36` | 0.95-1.04 | 0.89-1.06 | 0.85-0.90 | 0.54-0.55 |
| MariaDB 11.8 `Binary16` | 0.89-0.97 | 0.90-1.07 | 0.70-0.89 | 0.33-0.35 |
| MariaDB 11.8 `Char36` | 0.96-1.14 | 1.00-1.02 | 0.88-0.91 | 0.51-0.63 |

`EXPLAIN` of the executed SQL at 1 and 500 keys showed indexed lookups for
both formats and both modes. The JSON form read `JSON_TABLE` first and joined
to the entity column through its unique index with `eq_ref`; the multiple
parameter form used `const` at one key and indexed `range` at 500 keys.
Neither engine used a full entity-table scan in these cases. The initial
MySQL `Binary16` 50-key batch immediately after seeding was noisy (JSON
median 3.674 ms versus 1.518 ms); subsequent runs on the same populated
table gave 0.983 versus 1.317 ms and 0.913 versus 1.230 ms. The table uses
those repeated runs and makes no claim that small collections always favor
JSON. At one to five keys, observed differences were small and changed sign
between runs.

This was a targeted diagnostic, not BenchmarkDotNet evidence or an automated
gate. It confirms that the observed large-collection gain does not require a
table scan for the tested selective shapes. It did not cover relationship
includes or split queries; a selective relationship query timed out under the
JSON default in 10.4.3. No incorrect results were reported. Query plans and
latency can change with other predicates, data distributions, engine versions,
and hosts. EF Core's [collection translation guidance][ef-collections] also
calls out this workload dependence. Keep the per-query mode override for
measured exceptions; do not infer a universal optimal mode from these ratios.

## Sliding-Cache Ratio Calibration

The `cache-parallel-sliding-buffer-throughput` control divides the mean of
`ParallelSlidingBufferReadsAsync` by the same-run mean of
`ParallelBufferReadsAsync`. Both methods read one 1 KiB value with sixteen
parallel readers. The initial target ceilings were calibrated on 2026-08-26
from eighteen independent BenchmarkDotNet runs: three per target, with both
methods in each run. Each run used one launch, three warmups, and seven
requested measurement iterations per method. BenchmarkDotNet's normal outlier
handling determines the retained `OriginalValues` count.

The host was an Apple M2 Max running macOS Tahoe 26.5.2, Arm64, .NET 10.0.11
(SDK 10.0.400), and BenchmarkDotNet 0.15.8, in Release without an attached
debugger. No builds or tests ran concurrently with calibration. Containers
used the exact digest-pinned images from the 2026-08-26 performance contract;
each retained run's `image.json` records its full immutable image reference.
The image tags below identify those pins, not mutable-tag-only evidence.

| Target | Image tag | Minimum ratio | Maximum ratio | Unchanged ceiling |
| --- | --- | ---: | ---: | ---: |
| `mysql84` | `mysql:8.4.11` | 11.701880 | 12.884331 | 17 |
| `mysql97` | `mysql:9.7.2` | 11.927741 | 14.481168 | 19 |
| `mariadb1011` | `mariadb:10.11.18` | 5.723713 | 8.628951 | 11 |
| `mariadb114` | `mariadb:11.4.12` | 6.649563 | 7.054340 | 9 |
| `mariadb118` | `mariadb:11.8.8` | 7.346698 | 8.135599 | 11 |
| `mariadb123` | `mariadb:12.3.2` | 5.825890 | 6.510264 | 9 |

Each ceiling is `ceil(1.25 * maxRatio)`, using the unrounded maximum of the
three measured ratios for that target. This adds 25 percent headroom before
rounding upward to an integer; rounding adds less than one further ratio
unit. The displayed ratios are rounded to six decimal places. These are
reviewed contract values, not a statistical confidence bound or an automatic
budget-update rule.

All eighteen retained normal ratios pass. Doubling only the sliding mean,
while leaving the absolute-read mean and allocation unchanged, exceeds the
target ceiling in every retained run. This is an arithmetic sensitivity
analysis of measured data, not an injected lock-delay experiment or proof
that every real slowdown will be detected. The ratio measures end-to-end
refresh overhead, not pure lock duration; shared slowdowns can cancel out.
It is not hardware independent. Hosted Ubuntu/x64 evidence remains a separate
validation surface from these local Arm64 runs.

The original evidence is retained under
`artifacts/benchmarks/cache-followup-20260826/<target>/run-<1..3>/`.
Each directory contains `image.json`, `benchmark.log`, and the full
BenchmarkDotNet JSON under `reports/results/`. The measured benchmark source
SHA-256 was
`f0322a027b5b701254e3220c23de97657017e4808a058f49a4687a9a9cf647f8`;
the refresh source SHA-256, including its comment correction, was
`c842f600e998affd757ad087c6e3c674cc0435bd3bd2fa05a4ca97485040f508`.

To repeat a run, use a healthy isolated container with its recorded image pin
and resolve its dynamic port, then run both methods into a fresh directory:

```sh
DOKA_BENCHMARK_TARGET=<target> \
DOKA_BENCHMARK_DATABASE_PORT=<isolated-port> \
DOKA_BENCHMARK_PROFILE=smoke \
dotnet \
  artifacts/bin/Doka.EntityFrameworkCore.MySql.Benchmarks/release/Doka.EntityFrameworkCore.MySql.Benchmarks.dll \
  --filter '*DistributedCacheBenchmark.ParallelBufferReadsAsync' \
           '*DistributedCacheBenchmark.ParallelSlidingBufferReadsAsync' \
  --iterationCount 7 --artifacts <fresh-directory>
```

### Manual Re-evaluation and Tightening

Review the ceilings when the first comparable hosted Ubuntu/x64 evidence is
available, or when the workload, engine image or configuration, .NET runtime
or SDK, or BenchmarkDotNet baseline changes. Record the affected targets and
the changed conditions; compare runs with the same workload, payload,
parallelism, job settings, engine configuration, runtime, and controlled host
conditions. Do not pool local Arm64 and hosted x64 runs into one calibration
range.

A reviewer may lower a target's ceiling to `ceil(1.25 * newMaxRatio)` after at
least three clean, independently started comparable runs confirm a sustained
decrease. The lower ceiling must pass every normal run in the newly accepted
calibration set, and doubling only the sliding mean must exceed it for every
run in that set. Preserve older reports as historical evidence with their
original conditions and ceilings. Retain raw reports as run artifacts and
document the reviewed calculation with the contract change; neither the
runner nor the gate updates limits automatically.

Never automatically relax a ceiling after a red run. Diagnose the measured
regression or invalid evidence first, including workload, engine, runtime,
host, and infrastructure changes. A failing run alone does not authorize a
larger budget. Any later ceiling change requires an explicit review of
comparable evidence and the same normal-pass and sensitivity checks.

## Generic LIKE Measurement Boundaries

`GenericLikeBenchmark` has exactly two measured paths over the same numeric,
`DateTime`, and GUID predicate:

| Method | Measured work | Excluded setup |
| --- | --- | --- |
| `CachedScalarQueryToQueryString` | Expression construction, parameter extraction, warmed query-cache lookup, and debug SQL preparation | Context, model, service initialization, and first compilation |
| `CompileScalarQueryToQueryString` | Expression construction, parameter extraction, cache-miss processing, query compilation, and debug SQL preparation | Context, model, and service initialization |

Both contexts are initialized and both methods are called in `GlobalSetup`.
The compilation context uses the same prebuilt model and a benchmark-local
non-caching `IMemoryCache`, following the
[EF Core 10.0.8 compilation benchmark][ef-compilation-benchmark]. The cache
instance is reused; it never stores compiled queries and never evicts entries
from the normal context's cache. EF services and contexts are not recreated
inside either measured method. These methods only generate SQL; neither
opens a database connection or measures database execution.

Compilation includes EF preprocessing, SQL translation, shaper and delegate
compilation, and `ToQueryString` preparation. It is not an isolated measurement
of the LIKE translator. The non-caching entry bookkeeping is also part of the
measured cache-miss path. The warmed path does not claim to compile a query
again: EF's [query compiler][ef-query-compiler] reuses the cached delegate.

The previous `CompileScalarQuery` measurement disabled service-provider
caching. Its allocation budget included context/service construction despite
sharing a model; it cannot be compared directly with the corrected boundary
as a provider performance improvement. The former `TranslateScalarCorpus`
name also overstated what the warmed path measured. Contract method names and
the warmed control ID now describe the actual paths.

Behavioral tests observe EF compilation events: each uncached call compiles
once, cached calls do not compile, both return the same complete SQL, and
context/model/service identities remain stable. Gate tests separately check
the actual allocation ceilings, one byte above them, and missing reports.

### LIKE Allocation Calibration

On 2026-08-26, three independently started runs per engine family measured
both corrected methods. Each run used one launch, three warmups, and seven
requested iterations. Host admission passed before each run; no builds or
tests ran concurrently. The host was the same Apple M2 Max/macOS 26.5.2 Arm64
environment described above, using SDK 10.0.400, runtime 10.0.11,
BenchmarkDotNet 0.15.8, and resolved EF Core 10.0.8.

| Target profile | Cached path bytes, min-max | Compiling path bytes, min-max |
| --- | ---: | ---: |
| `mysql84` | 18,257-18,385 | 201,182-202,641 |
| `mariadb114` | 18,257-18,337 | 202,600-204,180 |

The scalar ceiling for each method is
`ceil(1.25 * maximumMeasuredBytes / 1024) * 1024`, taking the largest value
across both profiles. This is 25 percent headroom rounded up to the next
1 KiB: **23,552 bytes** for the cached path and **256,000 bytes** for the
compiling path. These replace the earlier 65,536-byte and 2,097,152-byte
ceilings. All six normal observations per method pass the new limits;
doubling any retained allocation exceeds its limit. This is a sensitivity
check on retained measurements, not an injected provider-regression test.

The raw reports and logs are retained under
`artifacts/benchmarks/like-measurement-20260826/<target>/run-<1..3>/`.
The measured `GenericLikeBenchmark.cs` SHA-256 is
`c3c16a6fd7b25300ca6609f4e14ee75553cc06b52ac2753c0410e7e2ca9fd8f8`.
Run the Release benchmark assembly with `--filter '*GenericLikeBenchmark*'
and `--iterationCount 7`, setting `DOKA_BENCHMARK_TARGET` to each profile and
using a fresh `--artifacts` directory for every run. These are SQL-generation
profiles, not a claim of database execution or six-engine live validation.

The limits are reviewed data, not exact allocation assertions in unit tests.
Re-evaluate them after query-shape, EF, runtime, SDK, or benchmark changes and
when comparable hosted x64 evidence first becomes available. Any tightening
requires at least three comparable runs per affected profile; retain their
raw reports and document the calculation. Diagnose a failing run before
considering a change to its limit. Neither the runner nor the gate adjusts
these budgets automatically.

## Soak Interpretation

`scorecard` and `stress` require exactly these six scenario identities:

- `soak.hilo-cache-bound`;
- `soak.pooled-buffer-return`;
- `soak.connection-cleanup`;
- `soak.migration-lock-cleanup`;
- `soak.working-set-stabilization`; and
- `soak.concurrent-throughput-retention`.

The gate validates the report identity and recomputes every maximum or minimum
from the scenario metrics and the current contract. The working-set scenario
checks both working-set growth and managed-heap growth. An omitted or non-finite
metric is invalid evidence; a finite budget violation is a regression.

## Primary Sources

- [BenchmarkDotNet documentation](https://benchmarkdotnet.org/articles/overview.html)
- [BenchmarkDotNet memory diagnostics](https://benchmarkdotnet.org/articles/configs/diagnosers.html)
- [GitHub Actions workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)

[ef-compilation-benchmark]: https://github.com/dotnet/efcore/blob/v10.0.8/benchmark/EFCore.Benchmarks/Query/QueryCompilationTests.cs
[ef-query-compiler]: https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/Query/Internal/QueryCompiler.cs
[ef-collections]: https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/breaking-changes#parameterized-collections-now-use-multiple-parameters-by-default
