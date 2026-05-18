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
./eng/test-integration.sh --up-test-down
```

This starts the bundled Docker Compose stack, runs live integration tests against MySQL 8.0, MySQL 8.4, MariaDB 11.4, and MariaDB 11.8, and stops the stack.

For a representative subset, scope the target selection:

```bash
DOKA_INTEGRATION_TARGETS=mysql84,mariadb118 ./eng/test-integration.sh --up-test-down
```

**Runtime-posture smoke** (JIT + trim smoke tests; the NativeAOT pass is deferred per ADR D-017 while upstream EF Core NativeAOT support remains experimental):

```bash
./eng/test-runtime-posture.sh --up-test-down
```

**Benchmark smoke** (representative benchmark run, MySQL 8.4 or MariaDB 11.8):

```bash
DOKA_BENCHMARK_TARGET=mysql84 ./eng/benchmark.sh --up-smoke-down
```

**Release-candidate evidence path** (pack + vulnerability audit + SBOM):

```bash
./eng/release-candidate.sh
```

## Code Style

- Follow the existing naming, formatting, and layout conventions in the codebase.
- All code comments must be in English.
- `<Nullable>enable</Nullable>`, `TreatWarningsAsErrors`, trim analysis, AOT analysis, and build-enforced code-style analyzers are configured solution-wide via `Directory.Build.props`.
- Do not add third-party library dependencies without first opening an issue to discuss the rationale.
- Engine differences must flow through the internal `ServerCapabilities` model; avoid ad-hoc version checks across unrelated subsystems.

## Test Conventions

- Every test method in `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/**` that hits a live database MUST use `[RequiresDatabaseTargetFact(IntegrationDatabaseTarget.X)]`, not `[Fact]`. Plain `[Fact]` in this project silently runs the test even when the required database is unavailable, producing a hard failure instead of a graceful skip. Method-name suffix `_on_<target>` is the usual but not mandatory cue; the attribute is the source of truth.
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
