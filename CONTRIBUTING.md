# Contributing

Contributions are welcome. Please read this document before opening a pull request.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (version `10.0.300` or later -- pinned in `global.json`)
- [Docker](https://docs.docker.com/get-docker/) -- required for the MySQL / MariaDB integration test suites

## Building

```bash
dotnet build Doka.EntityFrameworkCore.MySql.slnx
```

The build must remain warning-free, including analyzer, formatting, trim-analysis, and AOT-analysis diagnostics.

## Running Tests

**Unit tests only** (no Docker required):

```bash
./eng/test.sh
```

This runs unit and functional tests with `--no-build --no-restore` after a single restore and build pass.

**Integration tests** (requires Docker):

```bash
./eng/test-integration.sh
```

The test assembly starts isolated containers on dynamic ports for MySQL 8.4, MariaDB 11.4, and MariaDB 11.8, waits for database readiness, runs the live tests, and removes every owned container. No database service needs to be running before the command starts.

For a representative subset, scope the target selection:

```bash
DOKA_INTEGRATION_TARGETS=mysql84,mariadb118 ./eng/test-integration.sh
```

Use `./eng/test-integration.sh --up-test-down` only when an explicit Compose stack is useful for debugging. That mode exposes the documented host ports and removes the stack plus its volumes after the run. External targets remain available through the `DOKA_<TARGET>_CONNECTION_STRING` variables. MySQL 8.0 is not part of the supported release matrix; its retained legacy tests require both an explicit `mysql80` selection and an external connection string.

Specification tests use the same lifecycle:

```bash
DOKA_SPEC_TEST_TARGET=mysql84 dotnet test \
  tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj \
  --filter "Category=Spec"
```

Accepted specification targets are `mysql84`, `mariadb114`, and `mariadb118`. Set `DOKA_SPEC_TEST_CONNECTION_STRING` together with `DOKA_SPEC_TEST_SERVER_VERSION` only when validating an external database.

The specification contract is stricter than a passing aggregate test count:

```bash
bash eng/check-spec-contract.sh
bash eng/check-spec-discovery.sh
bash eng/check-spec-results.sh mysql84 artifacts/spec-tests/mysql84
```

The first two commands validate the exact EF Core patch inventory, monotonic
provider mapping baseline, fixtures, and discovered test IDs. The TRX command
rejects missing, duplicate, failed, unexpected, or undeclared skipped results.

Coverage is measured across unit, functional, specification, and integration
tests. Both shipped assemblies and risk-critical classes have independent
line and branch floors:

```bash
bash eng/merge-coverage.sh artifacts/coverage artifacts/coverage-merged
bash eng/check-coverage-threshold.sh artifacts/coverage-merged
```

**Runtime-posture smoke** (JIT + trim smoke tests; the NativeAOT pass is deferred per ADR D-017 while upstream EF Core NativeAOT support remains experimental):

```bash
./eng/test-runtime-posture.sh --up-test-down
```

**Benchmark smoke** (representative benchmark run, MySQL 8.4 or MariaDB 11.8):

```bash
DOKA_BENCHMARK_TARGET=mysql84 ./eng/benchmark.sh --up-smoke-down
```

**Release-candidate evidence path** (repository, specification, integration,
coverage, package, vulnerability, SBOM, benchmark, performance, and
publication-readiness gates):

```bash
./eng/release-candidate.sh
```

The script is the single deterministic pre-tag checkpoint while the CI
benchmark workflow is disabled per ADR D-019. Exit `0` signals "safe to tag";
exit non-zero stops the release. The final publication-readiness check invokes
the official EF Core relational compliance assertions and requires zero
provider-owned specification debt. The benchmark + ratio gate asserts
IdentifierQuoting >= 2x throughput vs. the naive baseline,
BulkInsert1000Rows >= 3x throughput vs. per-row SaveChanges, and JsonComparer
>= 80% allocation reduction vs. a string-round-trip baseline; these run
against both `mysql84` and `mariadb118` via `--up-smoke-down`, so Docker must
be available.

Dev-loop bypass (only for iteration that does not aim to ship): `DOKA_RELEASE_CANDIDATE_SKIP_BENCHMARKS=1 ./eng/release-candidate.sh` skips the benchmark + gate step. The resulting evidence is explicitly not release-eligible.

Once the script returns `0`, tag manually:

```bash
git tag v<version>
git push --tags
```

## Code Style

- Follow the existing naming, formatting, and layout conventions in the codebase.
- All code comments must be in English.
- `<Nullable>enable</Nullable>`, `TreatWarningsAsErrors`, trim analysis, AOT analysis, and build-enforced code-style analyzers are configured solution-wide via `Directory.Build.props`.
- Do not add third-party library dependencies without first opening an issue to discuss the rationale.
- Engine differences must flow through the internal `EngineProfile` model; avoid ad-hoc version checks across unrelated subsystems.

## Architecture Decisions

Architecture decisions follow MADR 4.0.0 with the Doka enterprise profile
defined in `docs/decisions/MADR-PROFILE.md`.

Workflow for a new or changed decision:

1. Copy `docs/decisions/adr-template.md` to the next contiguous
   `D-NNN-lowercase-slug.md` filename.
2. Complete every metadata field and required section. Each considered option
   needs at least one `Good, because` and one `Bad, because` entry.
3. Put external URLs only under Sources and record authoritative primary
   sources with retrieval dates.
4. Record both sides of every supersedes or amends relationship.
5. Run `./eng/validate-adrs.sh --write-index` to regenerate the decision index
   and relationship graph.
6. Run `./eng/validate-adrs.sh` before requesting review.

The validator has no third-party package dependency and runs from the local
build, repository tests, CI quality gate, and release-candidate path. Manual
changes to `docs/decisions/README.md` or `decision-index.json` are rejected.

## Test Conventions

- Every integration test class that uses a live database MUST belong to `IntegrationDatabaseTestGroup`. Every target-specific method MUST use `[RequiresDatabaseTargetFact(IntegrationDatabaseTarget.X)]`, not `[Fact]`, so a scoped matrix run only executes tests for provisioned targets. Method-name suffix `_on_<target>` is the usual but not mandatory cue; the attribute is the source of truth.
- Any raw-SQL helper that interpolates an identifier into backticked DDL (e.g. `` $"DROP TABLE IF EXISTS `{name}`;" ``) must backtick-escape the interpolated value with `name.Replace("`", "``", StringComparison.Ordinal)` so the helper is safe-by-construction, regardless of caller-supplied input. The `EF1002` analyzer is enforced in unit and functional tests; it is suppressed only under `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/**` where fixture-controlled DDL is expected.
- Culture-sensitive string comparison analyzers (`CA1304/1307/1309/1311`) are enforced in all test projects. When a test deliberately uses `string.ToUpper()` / `ToLower()` / `Equals` inside an `IQueryable` expression tree (where EF translates the call server-side and the CLR culture never applies), suppress the warnings per-line with `#pragma warning disable CA1304, CA1311` + `#pragma warning restore CA1304, CA1311` plus a short rationale comment.

## Pull Requests

- Target the `main` branch.
- Keep each PR focused on a single concern.
- New translation, type-mapping, migration DDL, or scaffolding paths must include both unit / functional tests and -- where behavior is engine-specific -- live integration tests against the relevant MySQL and MariaDB containers.
- The build and all test suites must be green before requesting review.
- Summarize the motivation and approach in the PR description.

## Public-API Changes

Public-API drift is tracked mechanically via `Microsoft.CodeAnalysis.PublicApiAnalyzers` (see ADR D-008). Two text files per src project carry the current contract:

- `PublicAPI.Shipped.txt` -- immutable snapshot of the public API as of the most recent release. Edited only at release time by merging the unshipped delta.
- `PublicAPI.Unshipped.txt` -- the working set of public-API additions since the last release.

Workflow for changes that add or remove public API:

1. Make the source change. The build will fail with `RS0016` (declared API not in shipped or unshipped) or `RS0017` (shipped API removed from source).
2. Apply the analyzer code-fix in your IDE or run `dotnet format analyzers <csproj> --diagnostics RS0016 --severity info` from the repository root to populate `PublicAPI.Unshipped.txt` automatically.
3. Removals require an explicit `*REMOVED*` line in `PublicAPI.Unshipped.txt` plus removal of the symbol from `PublicAPI.Shipped.txt`. The diff makes the SemVer-breaking nature of the change visible in PR review.
4. At release time, the contents of `Unshipped.txt` move to `Shipped.txt` (and the unshipped file is reset to `#nullable enable`) as part of the tag commit.

`RS0026` ("Do not add multiple overloads with optional parameters") fires on the `UseMySql`, `UseHiLo`, and `IsInvisible` extension overloads because each carries an optional default (`mySqlOptionsAction = null`, `name = null`, `invisible = true`). The optional pattern is the EF Core community standard and part of the documented public surface. The suppression is scoped per-method via `[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "...")]` on each affected declaration; the project-wide `TreatWarningsAsErrors=true` still applies so any **new** overload that introduces the same pattern fails the build until the author adds the explicit `SuppressMessage` attribute. The added optional parameter is still a SemVer break and demands reviewer attention.

Post-v1.0 the project will additionally adopt `PackageValidation` (built-in to .NET 8+) so the released NuGet packages are compared against the previous baseline at `dotnet pack` time -- see ADR D-008 for the deferred-activation trigger.

## Reporting Issues

Use [GitHub Issues](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/issues) for bug reports and feature requests. Include:

- the engine and version you are running against (MySQL 8.x or MariaDB 11.x)
- a minimal reproduction (connection configuration, model snippet, query)
- the generated SQL if applicable (`query.ToQueryString()` works for `IQueryable`)

For security vulnerabilities, see [SECURITY.md](.github/SECURITY.md).
