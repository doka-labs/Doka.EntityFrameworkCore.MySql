# D-020 -- Test-Owned Database Lifecycle

- **Status:** Accepted
- **Date:** 2026-07-27
- **Scope:** Integration tests, EF Core specification tests, compatibility CI, and local database-test evidence

## Context

The live test suites previously depended on database containers that an operator or CI workflow had to start before `dotnet test`. The repository supplied a Compose wrapper, but direct CLI and IDE execution still used fixed localhost ports and silently skipped unreachable integration targets. Persistent Compose volumes could also retain state between runs, and fixed names and ports prevented independent worktrees from executing concurrently.

The database lifecycle must be part of the test contract:

- a selected target is ready before its first test executes
- the endpoint is allocated dynamically
- exact engine artifacts are reproducible
- setup failure is visible as an infrastructure failure
- cleanup runs after successful and failed suites
- local and CI execution use the same ownership path
- hosted or externally managed databases remain explicit overrides

## Decision

Use Testcontainers for .NET as the canonical lifecycle owner for live integration and specification tests.

- `Testcontainers.MySql` 4.13.0 provisions MySQL.
- `Testcontainers.MariaDb` 4.12.0 provisions MariaDB; it consumes the compatible Testcontainers core selected by the MySQL module.
- One container per selected engine is shared by an xUnit collection fixture for the lifetime of one test assembly.
- Tests isolate mutable state with per-test databases and existing cleanup helpers; they do not create one container per test.
- Supported images are pinned by patch version and multi-platform manifest digest in one shared source file.
- Host ports and container names are allocated dynamically.
- Testcontainers resource cleanup remains enabled. Experimental resource reuse is not used.
- External connection strings are verified before tests execute and bypass local provisioning only for the named target.
- MySQL 8.0 remains an explicit external-only legacy test target and is not part of the default release matrix.
- Test database evidence contains no credentials and records target, exact image, endpoint metadata, ownership source, container id, and final cleanup state.

The dependency addition was proposed explicitly and approved by the project owner in the implementation request on 2026-07-27.

## CI and Compose Boundaries

GitHub Actions no longer duplicates MySQL or MariaDB service definitions for integration and specification tests. Those jobs invoke the same fixture-owned lifecycle used locally. The specification matrix is binding for MySQL 8.4, MariaDB 11.4, and MariaDB 11.8; failures are not allowed to continue silently.

Compose remains an operator-selected debugging and benchmark mechanism. `eng/test-integration.sh --up-test-down` is the compatibility path for an explicit Compose run and removes its volumes afterwards. The default `eng/test-integration.sh` path uses Testcontainers.

## Consequences

### Positive

- `dotnet test` and IDE test execution no longer depend on pre-running repository containers.
- Dynamic ports and names allow parallel worktrees and CI jobs.
- The test process owns readiness, configuration, evidence, and cleanup.
- Exact image manifests make release evidence reproducible across supported architectures.
- Missing Docker or an unreachable external target fails visibly instead of converting infrastructure failure into skipped coverage.

### Negative

- Docker remains a prerequisite for live local tests.
- The first run must pull the pinned images.
- The test projects gain two test-only NuGet dependencies and their transitive Docker client.

### Neutral

- Benchmarks and runtime-posture checks keep their explicit long-lived database paths because their lifecycle and measurement needs differ from correctness tests.
- Provider production packages and their transitive dependency graph are unchanged.

## Re-evaluation Triggers

- xUnit is upgraded to a version with a materially better assembly-fixture lifecycle.
- Testcontainers removes or replaces either database module.
- Hosted-database validation needs a different authentication or readiness contract.
- The supported MySQL or MariaDB LTS matrix changes.
