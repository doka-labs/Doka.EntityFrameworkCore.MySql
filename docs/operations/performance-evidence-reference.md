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

There is no historical host-to-host comparison. This deliberately removes the
false precision and control-plane state required to match, retry, confirm, and
promote historical evidence across hosted runners.

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
