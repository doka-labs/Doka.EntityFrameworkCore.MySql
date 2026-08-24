# Engineering System

The `eng` tree is an execution boundary, not a second product framework.

## Ownership

| Owner | Responsibility |
| --- | --- |
| .NET test projects | Product behavior, repository contracts, coverage evaluation, and performance evaluation |
| BenchmarkDotNet | Warmup, measurement iterations, statistics, and allocation data |
| Shell | Thin process, container, environment, and version-matrix orchestration |
| GitHub Actions | Triggers, jobs, permissions, concurrency, and artifact transport |
| `eng/release` Python | Release trust, immutable evidence, provenance, publication, and public readback |

Python is intentionally absent from `eng/quality` and `eng/performance`.
`eng/common/deadline.py` and `eng/testing/spec_matrix.py` remain small bounded
helpers. Python release tests run only through `eng/release/test-tools.sh`;
the general provider test path runs no Python self-test suite.

## Layout

| Path | Responsibility |
| --- | --- |
| `eng/common/` | SDK and process deadline primitives |
| `eng/development/` | Local contributor setup |
| `eng/performance/` | Thin BenchmarkDotNet and container orchestration |
| `eng/quality/` | Shell entry points for compiled and external quality tools |
| `eng/release/` | Release candidate, trust, publication, and release-only tests |
| `eng/testing/` | Product, integration, specification, and runtime orchestration |
| `eng/tools/` | Dependency-free compiled repository and coverage contracts |
| `eng/tests/` | Tests for retained Python release logic |

`eng/architecture.json` inventories every stable root command, its domain
owner, cost, side effects, and consumers. Root commands remain short Bash
facades that delegate directly to one domain implementation.

## Normal Verification

```bash
./eng/quality-gates.sh --fast
./eng/test.sh
```

The quality gate compiles and runs the repository contract. It validates
documentation, examples, image pins, workflow boundaries, engineering
ownership, dependency shape, Shell portability, and retained tool contracts.
The provider test runner builds and executes the .NET test projects.

Full quality validation additionally restores the solution, runs analyzers,
audits dependencies, builds examples, compiles README snippets, and checks
migration-model drift:

```bash
./eng/quality-gates.sh
```

Release-only Python contracts run from the release owner:

```bash
./eng/release/test-tools.sh
```

Benchmarks are explicit high-cost operations:

```bash
DOKA_BENCHMARK_TARGET=mysql84 \
DOKA_BENCHMARK_PROFILE=smoke \
DOKA_BENCHMARK_PORT=0 \
./eng/benchmark.sh --up-run-down
```

## Change Rules

1. Put product and repository decisions in compiled .NET owners.
2. Keep Shell limited to lifecycle and process composition.
3. Do not add workflow, attempt, retry, promotion, or general repository policy
   to Python.
4. Keep release Python limited to the irreversible release trust boundary.
5. Add no universal engineering CLI or new policy layer.
6. Add a root command only when operators need a stable end-to-end entry point,
   then declare it in `eng/architecture.json`.
