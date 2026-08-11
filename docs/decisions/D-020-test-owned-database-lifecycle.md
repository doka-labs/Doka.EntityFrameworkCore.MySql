---
id: D-020
status: accepted
date: 2026-07-27
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Integration and specification database lifecycle"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-020 -- Make live tests own their database lifecycle

## Context and Problem Statement

The live test suites previously depended on database containers that an operator or CI workflow had to start before `dotnet test`. The repository supplied a Compose wrapper, but direct CLI and IDE execution still used fixed localhost ports and silently skipped unreachable integration targets. Persistent Compose volumes could also retain state between runs, and fixed names and ports prevented independent worktrees from executing concurrently.

The database lifecycle must be part of the test contract:

- a selected target is ready before its first test executes
- the endpoint is allocated dynamically
- exact engine artifacts are reproducible
- setup failure is visible as an infrastructure failure
- cleanup runs after successful and failed suites
- local and CI execution use the same ownership path
- hosted or externally managed databases remain explicit overrides

## Decision Drivers

- Direct IDE and CLI test runs must not require pre-started services.
- Parallel worktrees need isolated endpoints and state.
- Unavailable selected targets must fail rather than appear as skipped coverage.

## Considered Options

- Assembly-scoped test-owned containers
- Pre-started fixed-port Compose stack
- Shared external database only

## Decision Outcome

Chosen option: "Assembly-scoped test-owned containers", because the test contract is reliable only when selected target lifecycle is owned by the test run.

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

### Consequences

- Good, because IDE, CLI, CI, and parallel worktrees execute the same isolated live path.
- Bad, because live suites require a functioning container runtime unless an explicit external override is supplied.

#### Positive

- `dotnet test` and IDE test execution no longer depend on pre-running repository containers.
- Dynamic ports and names allow parallel worktrees and CI jobs.
- The test process owns readiness, configuration, evidence, and cleanup.
- Exact image manifests make release evidence reproducible across supported architectures.
- Missing Docker or an unreachable external target fails visibly instead of converting infrastructure failure into skipped coverage.

#### Negative

- Docker remains a prerequisite for live local tests.
- The first run must pull the pinned images.
- The test projects gain two test-only NuGet dependencies and their transitive Docker client.

#### Neutral

- Benchmarks and runtime-posture checks keep their explicit long-lived database paths because their lifecycle and measurement needs differ from correctness tests.
- Provider production packages and their transitive dependency graph are unchanged.

### Confirmation

- Run live tests directly without starting Compose.
- Inspect persisted test-database evidence for image, endpoint, ownership, and cleanup state.

## Pros and Cons of the Options

### Assembly-scoped test-owned containers

- Good, because tests own readiness, dynamic endpoints, evidence, and cleanup.
- Bad, because the test process needs Docker access and container startup time.

### Pre-started fixed-port Compose stack

- Good, because operators can inspect long-lived services during debugging.
- Bad, because direct tests and parallel worktrees depend on external mutable state.

### Shared external database only

- Good, because tests avoid local container startup.
- Bad, because credentials, availability, cleanup, and test isolation become external dependencies.

## More Information

### CI and Compose Boundaries

GitHub Actions no longer duplicates MySQL or MariaDB service definitions for
integration and specification tests. Those jobs invoke the same fixture-owned
lifecycle used locally. The specification matrix is binding for all six active
LTS targets; failures are not allowed to continue silently.

Compose remains an operator-selected debugging and benchmark mechanism. `eng/test-integration.sh --up-test-down` is the compatibility path for an explicit Compose run and removes its volumes afterwards. The default `eng/test-integration.sh` path uses Testcontainers.

### Re-evaluation Triggers

- xUnit is upgraded to a version with a materially better assembly-fixture lifecycle.
- Testcontainers removes or replaces either database module.
- Hosted-database validation needs a different authentication or readiness contract.
- The supported MySQL or MariaDB LTS matrix changes.
- The repository adopts a non-container local database isolation mechanism with equivalent evidence.
- A hosted target is added and needs an external-validation-pending contract.

### Decision History

- 2026-07-27: Decision recorded with status accepted.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.
- 2026-08-11: Extended the test-owned lifecycle and exact image evidence to
  MySQL 9.7 and MariaDB 10.11 / 12.3 as part of the six-line active-LTS matrix.

### Implementation References

- `tests/Doka.EntityFrameworkCore.MySql.TestUtilities/IntegrationTestEnvironment.cs`
- `eng/test-integration.sh`
- `docker/compose.yml`

### Sources

- No external sources; repository evidence only.
