---
id: D-027
status: implemented
date: 2026-08-11
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "How external packages add exact custom migration operations without replacing the provider generator"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-027 -- Expose exact-type migration operation handlers

## Context and Problem Statement

EF Core permits applications and libraries to introduce custom
`MigrationOperation` types, but the relational generator fails when no SQL
generator owns the exact runtime type. The documented extension model replaces
`IMigrationsSqlGenerator` with a derived generator. That model is suitable for
one application-owned customization, but it is not composable for independent
packages: each replacement must inherit provider internals, reproduce the
provider dispatch boundary, and become the only selected generator.

The provider must remain the sole authority for built-in MySQL and MariaDB DDL.
At the same time, independent packages need a public, additive way to own their
own exact operation types, delegate standard DDL back to the provider, preserve
command boundaries and transaction suppression, and fail closed when ownership
is absent or conflicting.

The decision question is which public contract adds that capability without
making the internal generator, its command builder, or its implementation
details part of the compatibility surface.

## Decision Drivers

- Multiple independent packages must compose in one scoped EF service graph.
- Built-in EF Core and provider operation types must remain provider-owned.
- Unknown, duplicate, invalid, and failed custom operations must fail closed.
- A handler must not partially mutate the shared command builder before its
  result is validated.
- Provider quoting, command boundaries, generation options, and
  `TransactionSuppressed` semantics must remain authoritative.
- Dispatch must be deterministic, exact-type, constant-time, and independent
  of registration order.
- The public contract must contain no provider-internal or SafeMigrations-
  specific type.
- Diagnostics must be stable, bounded, low-cardinality, and free of SQL,
  credentials, object names, and plugin exception messages.
- Runtime, design-time tools, scripts, and bundles must resolve the same
  options-owned EF service graph.

## Considered Options

- Add an exact-type migration operation handler SPI
- Let each package replace the migrations SQL generator
- Make the provider generator public and derivable
- Add an ordered migration middleware chain
- Require custom packages to emit SQL operations directly

## Decision Outcome

Chosen option: "Add an exact-type migration operation handler SPI", because it
adds one narrow, composable ownership boundary while keeping standard DDL and
the mutable command builder inside the provider.

The runtime package exposes an immutable context, an immutable command
specification, a validated generated result, a canonical migration-feature
projection, stable failure codes, and
`IMySqlMigrationOperationHandler`. A package registers each handler as scoped
through a package-owned `IDbContextOptionsExtension`; its `ApplyServices`
method uses `TryAddEnumerable` so independent implementations compose in EF
Core's internal service provider.

The scoped generator snapshots every handler identifier and operation type
exactly once. It rejects invalid identifiers, duplicate identifiers, duplicate
exact operation ownership, open or abstract operation types, and every
reserved EF Core operation type before generation. Object identity is not a
registry key; registering one instance twice still fails through the same
public identifier and operation-ownership constraints as two equivalent
instances. Registry lookup uses exact runtime-type equality in an immutable
dictionary. Registration order never selects a winner.

A selected handler receives the current operation, target model, generation
options, server-version descriptor, zero-based operation ordinal, and a
capability projection derived from the provider's canonical profile. It may
render one reserved standard operation through a provider baseline renderer.
That renderer uses a fresh command builder and explicitly bypasses plugin
dispatch while still reaching the provider's typed standard-operation
overrides. Custom operations, re-entrant rendering, concurrent rendering, and
use after the handler returns are rejected. Context deactivation closes the
render lease before it waits for an already admitted render to finish, so no
new provider callback can begin or outlive the invocation boundary.

Use after return is reported directly as `ContextExpired`. It occurs after the
handler invocation has completed and therefore is not attributed retroactively
to that invocation's activity, metrics, or log event.

The handler returns one complete result containing at least one immutable
command and one bounded outcome code. The provider enumerates the foreign
command collection exactly once into a private snapshot, validates that
snapshot, and appends only the validated copy to the outer builder. Handler
failure or contract failure never falls back to built-in generation. An
unregistered custom operation also fails closed.

### Consequences

- Good, because independent handler packages compose without replacing the
  provider generator or knowing one another.
- Good, because exact ownership, immutable staging, and no fallback prevent
  ambiguous or partially generated custom DDL.
- Good, because the feature set centralizes engine differences instead of
  forcing packages to maintain private version tables.
- Good, because the baseline renderer preserves provider SQL and command
  boundaries without exposing the mutable builder.
- Bad, because package authors must provide an EF options extension rather
  than relying on ordinary application-container registration.
- Bad, because every public feature and failure value becomes a versioned API
  contract that requires compatibility review.
- Bad, because one dictionary lookup, context allocation, result snapshot, and
  bounded diagnostic envelope are added for each custom operation.

### Confirmation

- Run `./eng/test.sh` to validate registry, capability, diagnostics, a local
  package-only consumer dispatch, and functional service-graph contracts.
- Run `dotnet test tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Doka.EntityFrameworkCore.MySql.FunctionalTests.csproj --filter FullyQualifiedName~MySqlMigrationOperationHandlerTests` to validate exact dispatch, every generation mode, baseline rendering, and independent options extensions.
- Run `dotnet build tests/Doka.EntityFrameworkCore.MySql.RuntimeSmoke/Doka.EntityFrameworkCore.MySql.RuntimeSmoke.csproj --configuration Release` to compile the public SPI in the trim and NativeAOT smoke consumer.
- Run `./eng/test-migration-deployment.sh` to prove the same options-owned
  handler through design-time normal and idempotent scripts, runtime migration,
  bundle generation, rollback, and reapply on all six active LTS targets.
- Run the release-candidate package stage to restore an isolated consumer from
  the exact local `.nupkg` bytes and dispatch a custom operation through the
  provider-owned baseline renderer before publication.
- Run `dotnet build benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks.csproj --configuration Release` to keep the dispatch benchmark buildable.
- Run `./eng/validate-adrs.sh` and the public-API analyzer gate.

## Pros and Cons of the Options

### Add an exact-type migration operation handler SPI

- Good, because ownership is additive, deterministic, conflict-detecting, and
  independent of registration order.
- Good, because the provider retains standard DDL and command-builder
  authority.
- Bad, because the provider owns an additional public compatibility surface.

### Let each package replace the migrations SQL generator

- Good, because this follows EF Core's documented application-level example.
- Bad, because multiple replacements do not compose and each package must
  depend on provider implementation details.
- Bad, because replacement order silently decides which package works.

### Make the provider generator public and derivable

- Good, because a derived class could reuse protected provider generation
  methods.
- Bad, because protected internals become a permanent public API and two
  independent derived generators still cannot compose.

### Add an ordered migration middleware chain

- Good, because middleware could inspect or transform every operation.
- Bad, because priority and order become hidden semantic inputs for DDL.
- Bad, because plugins could intercept provider-owned standard operations and
  expand the security and compatibility surface.

### Require custom packages to emit SQL operations directly

- Good, because it needs no provider extension point.
- Bad, because the package must reproduce provider quoting, capability,
  command-boundary, and engine-difference logic.
- Bad, because a missing package registration cannot make the custom intent
  fail closed structurally.

## More Information

The SPI is synchronous because EF Core's migrations SQL generator is
synchronous. Handler code must be deterministic and perform no database,
network, file, clock, random, or service-locator I/O. Runtime catalog checks
belong in generated SQL or in a separate preflight service, not in generation.

`AtomicDdl` means crash-safe atomicity only for the statement and storage-engine
shapes the configured engine documents. It does not mean transactional DDL.
MySQL and MariaDB DDL can implicitly commit, so a multi-command handler must
design its execution and recovery semantics accordingly.

Handler package registrations belong to a package-owned
`IDbContextOptionsExtension.ApplyServices` boundary. Registering the handler
only in an application's ordinary service collection is insufficient when EF
Core maintains its own internal service provider. An explicitly supplied
internal provider remains supported and follows the same scoped enumerable
contract.

### Re-evaluation Triggers

- Re-evaluate if EF Core adds a public, additive, multi-provider custom
  migration-operation plugin contract with equivalent ownership and command
  semantics.
- Re-evaluate if EF Core changes exact-type dispatch, migrations SQL generator
  lifetime, generation options, or command-boundary behavior in a supported
  patch or major line.
- Re-evaluate if a real handler requires asynchronous generation or database
  I/O; do not add either capability without a separate decision.
- Re-evaluate if measured dispatch or allocation cost exceeds the registered
  benchmark budget.

### Decision History

- 2026-08-11: Decision recorded with status proposed.
- 2026-08-11: Status changed from proposed to accepted.
- 2026-08-11: Status changed from accepted to implemented.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Migrations/`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations/MySqlMigrationOperationHandlerRegistry.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations/MySqlMigrationsSqlGenerator.Handlers.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations/MySqlStandardMigrationOperations.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Migrations/`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Migrations/MySqlMigrationOperationHandlerTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.RuntimeSmoke/Program.cs`
- `eng/tests/test_paired_runtime_guards.py`
- `benchmarks/Doka.EntityFrameworkCore.MySql.Benchmarks/MigrationOperationHandlerDispatchBenchmark.cs`
- `docs/migration-operation-handlers.md`
- `docs/operations/observability-contract.json`

### Sources

- [EF Core custom migrations operations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/operations) (primary source; retrieved 2026-08-11)
- [EF Core 10.0.8 migrations SQL generator source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/MigrationsSqlGenerator.cs) (primary source; retrieved 2026-08-11)
- [EF Core 10.0.10 migrations SQL generator source](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Migrations/MigrationsSqlGenerator.cs) (primary source; retrieved 2026-08-11)
- [EF Core 10.0.8 migrations SQL generator interface source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/IMigrationsSqlGenerator.cs) (primary source; retrieved 2026-08-11)
- [EF Core 10.0.10 migrations SQL generator interface source](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Migrations/IMigrationsSqlGenerator.cs) (primary source; retrieved 2026-08-11)
- [EF Core 10.0.8 migration command source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/MigrationCommand.cs) (primary source; retrieved 2026-08-11)
- [EF Core 10.0.10 migration command source](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Migrations/MigrationCommand.cs) (primary source; retrieved 2026-08-11)
- [EF Core 10.0.8 migration command-list builder source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/MigrationCommandListBuilder.cs) (primary source; retrieved 2026-08-11)
- [EF Core 10.0.10 migration command-list builder source](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Migrations/MigrationCommandListBuilder.cs) (primary source; retrieved 2026-08-11)
- [.NET dependency-injection service registration](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-registration) (primary source; retrieved 2026-08-11)
- [EF Core IDbContextOptionsExtension API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.infrastructure.idbcontextoptionsextension?view=efcore-10.0) (primary source; retrieved 2026-08-11)
- [.NET distributed tracing instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs) (primary source; retrieved 2026-08-11)
- [.NET metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation) (primary source; retrieved 2026-08-11)
- [MySQL atomic DDL](https://dev.mysql.com/doc/refman/8.4/en/atomic-ddl.html) (primary source; retrieved 2026-08-11)
- [MariaDB atomic DDL](https://mariadb.com/docs/server/reference/sql-statements/data-definition/atomic-ddl) (primary source; retrieved 2026-08-11)
- [MariaDB PREPARE statement](https://mariadb.com/docs/server/reference/sql-statements/prepared-statements/prepare-statement) (primary source; retrieved 2026-08-11)
