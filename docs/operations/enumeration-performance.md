# Enumeration performance measurements

EF-owned descriptor scans use indexed access where complete-method measurements
show useful savings. This is not a general preference for `for`: concrete array,
string, JSON DOM, dictionary, and stack enumerators remain unchanged. Some
interface enumerations are already allocation-free under the .NET 10 JIT.

The retained optimizations are included in
[10.6.2](../../CHANGELOG.md#1062---2026-10-24). The measurements below retain
their original baseline, execution date and verification scope.

## Measurement conditions

The 2026-10-22 experiment compared baseline
`53c8fbd7ae8987b5c376250fe4d6296d729669cf` with the localized enumeration
changes on macOS 26.5.2, Apple M2 Max, 64 GiB RAM, Arm64, SDK 10.0.402,
.NET 10.0.13, EF Core 10.0.8, and BenchmarkDotNet 0.15.8.

Baseline and candidate ran in separate processes with identical benchmark
assemblies and inputs. Frozen provider outputs prevented BenchmarkDotNet from
building the baseline against candidate sources. Measurements ran without
concurrent builds or tests. The confirmation reversed provider execution order,
using six warmups and twelve 150 ms iterations with the in-process toolchain.

The initial screening covered 183 complete-method cases. Another 50 cases
covered targeted query branches and first/last affected-row output positions.
BenchmarkDotNet comparisons covered 35 initial cases, 18 confirmation cases,
and four additional batch/control cases per revision. Setup, reflection binding,
metadata initialization, and diagnostic fingerprints were outside timed work.

## Complete methods and collections

| Family | Actual inputs and measured boundary |
| --- | --- |
| Writes | Tracked EF entries and `IModificationCommandFactory`, with `List<IColumnModification>`, command lists, and physical key arrays; complete SQL generation, packet costing, and batch admission/finalization. |
| Write populations | 3/50 columns and 1/100/1000 commands; MySQL write-only, keyed readback, trigger fallback, and MariaDB `RETURNING`. Existing indexed shape comparisons are unchanged controls. |
| Stored procedures | Real mapped UPDATE commands with IN/OUT/INOUT parameters and an unmapped affected-row output first or last; complete SQL generation, output propagation, and affected-row scanning. |
| Generated result schema | Complete MySQL positional-readback validation through a setup-bound delegate; identical private-result boxing overhead on both revisions. MariaDB-labeled repetitions are not a distinct native readback protocol. |
| JSON SQL | Complete generator calls with finalized EF trees and list-backed static/dynamic paths, including a prepared 50-column nested-path stress case. |
| Query processing | Complete limit, APPLY, offset, decoding, and uncached compilation paths. Compilation uses EF's `IQueryCompiler.CreateCompiledQuery` so captured query roots are extracted without a compiled-cache hit. |
| Materialized sources | Selected member-initializer lineage, rehydrated collection Include, and the complete private UNION ALL physical-table-overlap guard with overlapping/disjoint branches. |
| Migrations and controls | Complete handler generation and scoped-command validation; SQL literal escaping, JSON structural hashing, and parameter-size processing as unchanged controls. |

The prepared JSON trees measure their stated collection shapes, not every
possible compiled JSON model. Stored-procedure metadata uses UPDATE because EF
does not allow affected-row output parameters on INSERT.

## Retained changes

These are representative confirmation results, not portable latency budgets.
KB below means 1,000 bytes. Allocation counts can change with JIT escape analysis;
they are not assertions in correctness tests.

| Complete operation | Baseline allocation | Candidate allocation | Local timing observation |
| --- | --- | --- | --- |
| Keyed-readback cost, 3 columns/1 command | 184 B | 72 B | 273 ns to 246 ns. Initial screening allocated 296 B on the baseline, demonstrating JIT-dependent enumeration elimination. |
| Keyed SQL, 50 columns/1000 commands | About 319 KB | About 127 KB | 1.942 ms to 1.510 ms in confirmation; both BenchmarkDotNet comparisons favored the candidate. |
| Stored-procedure output propagation, 4/50 columns | 160/1600 B | 120/1560 B | Narrow propagation improved; wide propagation showed no consistent timing advantage. |
| JSON SQL, prepared 50-column list paths | 35,201 B | 32,753 B | 12.16 us to 10.95 us. |
| Nested parameter-dependent `LEAST` | 3864 B | 3504 B | 2.15 us to 2.01 us in confirmation; the earlier comparison was timing-neutral. |
| Minimal scoped command | 720 B | 656 B | 166 ns to 142 ns. Existing snapshots remain intact; private array parameter types preserve allocation-free `foreach`. |
| Wide scoped command | 5864 B | 5800 B | 1.32 us to 1.17 us. |

Full batch timings were mixed across repetitions, including an apparent slowdown
that did not repeat in the focused comparison. The 100-command keyed fixture
saved about 30-35 KB, but it has no demonstrated consistent latency improvement.
An unchanged MariaDB SQL control also varied materially in time without an
allocation change. These measurements do not establish database, request, or
application throughput improvements.

## Changes not retained

- Generated-result schema scanning: no enumeration allocation saving, with a
  roughly doubled wide-scan time in both screening and BenchmarkDotNet.
- Affected-row scanning: no allocation saving at either output position; the
  wide late-output trial was slower in screening.
- APPLY, dynamic-offset, JSON decoding, string aggregation, materialized-source
  lineage/Include, and query identity scans: small or inconsistent complete-path
  gains did not justify extra indexing code. The isolated UNION guard saved
  80 B but is a small, compilation-only guard rather than a per-row operation.
- Handler command traversal: no compelling complete-generation benefit. The
  external operation sequence and defensive snapshots remain unchanged.
- Model building, conventions, scaffolding, startup, lazy/non-indexable
  enumerables, and error-only paths: no speculative materialization or loop
  conversion. Concrete array/string/DOM/dictionary/stack loops remain intact.

## Correctness verification

The reduced candidate preserved 50 complete SQL/result fingerprints. The
benchmark preflight passed all 53 scenarios. Regression tests cover tracked
narrow/wide SQL, exact keyed costs, duplicate physical keys, ordered shape
differences, static/dynamic/escaped JSON paths, invalid late path data, recursive
limits, invalid arguments, cache safety, and late scoped-payload overflow.
Each new test has one explicit Arrange/Act/Assert sequence.

Local verification passed:

- `./eng/test.sh`: 3,173 unit and 1,638 non-live functional tests; exact
  six-engine specification discovery and isolated publication-readiness checks.
- The final unit rerun: 3,173 passed after the declaration-only style fixes.
- Targeted generated-value integration: 157 passed across all six supported
  engines; engine-inapplicable cases skipped; owned-container cleanup completed.
- Stored-procedure specification tests: 56 passed under MySQL 8.4 and 56 under
  MariaDB 11.8, with no skips.
- Fast quality gates: style verification without rewriting, full solution
  build without warnings, repository contracts, shell lint, and ADR validation.

The local subset's coverage does not meet the global CI policy. That verdict
requires the full specification and integration producers, not only these
targeted live runs. No coverage floor, benchmark budget, workflow, dependency,
public API, or release version changes as part of this experiment.

## Reproduce a focused comparison

Build the same benchmark sources against each provider revision in isolated
output directories. Retain complete output directories, verify identical driver
hashes, and run each frozen assembly separately. For example:

```bash
dotnet <frozen-output>/Doka.EntityFrameworkCore.MySql.Benchmarks.dll \
  --inProcess --warmupCount 6 --iterationCount 12 --iterationTime 150 \
  --unrollFactor 1 --artifacts <fresh-results-directory> \
  --filter '*WritePathEnumerationBenchmark.GenerateSql*MySqlKeyedReadback*50cols*1000rows*'
```

Use the other enumeration benchmark classes for query, procedure, scope, and
control investigations. Inspect the effective jobs before comparing existing
classes with their own job attributes. Preserve raw reports outside Git; do not
change CI budgets from one local comparison. Re-measure when adopting a new EF
or .NET major, particularly when carrying changes into the .NET 11 line.

## Primary sources

- [.NET 10 enumeration and escape analysis](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/#enumeration)
- [EF Core 10.0.8 uncached query compilation](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/Query/Internal/QueryCompiler.cs)
- [BenchmarkDotNet jobs](https://benchmarkdotnet.org/articles/configs/jobs.html)
