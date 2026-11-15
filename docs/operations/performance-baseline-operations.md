# Performance Budget Operations

Status: historical baseline automation is retired. The filename remains stable
for published links.

## Accept an Engine Image Update

Update the Compose image, `benchmarks/performance-contract.json`, the test
image mirror, and any workflow mirror in one reviewed change. Then run:

```bash
./eng/quality-gates.sh
DOKA_BENCHMARK_TARGET=<target> \
DOKA_BENCHMARK_PROFILE=smoke \
DOKA_BENCHMARK_PORT=0 \
./eng/benchmark.sh --up-run-down
```

The compiled repository contract rejects mismatched image pins. It has no fix
mode because image changes must stay visible in review.

## Seed an Accepted Baseline

There is no accepted historical baseline and no seeding command. The current
gate compares raw BenchmarkDotNet measurements with explicit checked-in family
budgets and same-run controls. The selected evaluation mode determines which
thresholds affect the verdict, not which measurements must exist.

To change a budget:

1. Capture a fresh complete benchmark artifact for every affected target.
2. Explain whether the change is a provider regression, an intentional
   behavior change, or a corrected bound.
3. Modify `benchmarks/performance-contract.json` in the reviewed change.
4. Rerun the affected profiles.

No workflow writes or promotes the contract.

## Hosted Runner Baseline

Hosted runners do not own a baseline. The monthly `benchmark` workflow and a
manual dispatch run the current tree and upload raw artifacts. A workflow job
may be rerun after invalid infrastructure evidence, but the repository does
not select among attempts or convert a rerun into an accepted baseline.

GitHub Actions always enforces `DOKA_BENCHMARK_EVALUATION=allocations-only`.
Allocation ceilings/ratios and correctness/resource-ownership invariants remain
gates. Median/p95/p99 time, timing ratios, Gen2 frequency, working-set/managed-heap
soak growth and throughput retention remain validated diagnostics. Their
threshold violations do not make a slower or busier hosted CPU a performance
regression. CPU-headroom admission is local full-mode only.

Local runs default to `full` and can enforce the hardware-sensitive thresholds
on a controlled host. Use `DOKA_BENCHMARK_EVALUATION=allocations-only` locally to
reproduce hosted enforcement; `GITHUB_ACTIONS=true` cannot opt into full mode.
Unknown selector values are rejected. This is not `continue-on-error`: failed
execution, invalid/incomplete evidence, allocation regressions and explicit
resource leaks still fail, and genuine infrastructure outages/timeouts remain
possible. All produced raw reports and diagnostics are retained.

## Budget Review Rules

- Do not raise a budget to convert invalid evidence into a pass.
- Keep workload IDs and family ownership explicit.
- Treat exit `78` as an infrastructure or evidence defect and exit `1` as a
  measured violation of an enforced budget/invariant. A diagnostic timing or
  process-memory threshold is not an exit-`1` reason in hosted mode.
- Review the raw BenchmarkDotNet samples, allocation statistics, target image,
  and soak metrics with every budget change.
- Do not turn allocation/resource regression gates into diagnostics to hide
  failures, or tighten local timing limits from incomparable hosted hardware.

## Large Cache Write Allocation Samples

The single- and multi-segment large-write benchmarks measure a warm average
over 128 awaited writes per measurement window. The fixed invocation count
prevents CPU/time calibration from changing the denominator of the allocation
measurement. Their production methods, TLS, shared pools, GC sequence and
1,114,112-byte-per-operation ceilings remain unchanged.

This is not a per-request maximum or a cold-start guarantee. Shared pool
capacity can grow when async continuations return buffers on different threads;
those are real allocations and remain included in the raw measurement. Retain
cold-capacity diagnostics separately rather than describing their cost as fixed
or free. Do not adjust the invocation count after a failing result.

A copy allocated on every write grows with the operation count and cannot be
amortized away by the fixed batch. Review negative controls against the actual
budget: one extra 1 MiB copy exceeds the ceiling on the observed TLS profile,
but can fit on a lower-allocation transport. Two extra 1 MiB copies exceed the
ceiling independently of that transport difference. Preserve both controls and
the original failed samples when reviewing this measurement protocol.

## Primary Sources

- [BenchmarkDotNet documentation](https://benchmarkdotnet.org/articles/overview.html)
- [GitHub Actions rerunning workflows and jobs](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs)
