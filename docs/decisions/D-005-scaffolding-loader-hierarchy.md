---
id: D-005
status: implemented
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Database-model reverse-engineering architecture"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-005 -- Split reverse engineering into aspect loaders

## Context and Problem Statement

`MySqlDatabaseModelFactory` is currently an 812-LOC monolith that handles
tables, columns, primary keys, unique constraints, indexes, foreign keys,
spatial columns and JSON check-constraints in one class. Two consequences:

1. **Unit-testability is structurally limited.** A unit test that wants to
   pin one loader (for example, the index loader's `SUB_PART` handling) has
   to spin up the full factory and route around the six other loaders. The
   integration-test path is the only practical way to exercise per-loader
   edge cases today.
2. **Seven INFORMATION_SCHEMA queries are issued without a table filter.**
   A `dotnet ef dbcontext scaffold --table T` invocation still pulls
   metadata for every table in the database, then discards the rest in
   memory. On large schemas (hundreds of tables) the over-fetch is
   measurable.

Additional observations from the review: `MySqlScaffoldingState` is a
mutable shared object passed across loaders, the index loader silently
drops the `SUB_PART` column (prefix-length on text indexes), and the
spatial-column loader's `SRID` handling has no MariaDB coverage.

## Decision Drivers

- Reverse-engineering responsibilities need bounded ownership.
- Metadata queries and state must remain operation-scoped.
- Engine-specific details need focused tests and maintainers.

## Considered Options

- Per-aspect loader pipeline
- One monolithic database model factory
- Generic reflection-driven loader framework

## Decision Outcome

Chosen option: "Per-aspect loader pipeline", because stable metadata aspects provide natural and testable component boundaries.

Split `MySqlDatabaseModelFactory` into a thin orchestrator and a hierarchy
of per-loader classes under
`Internal/Scaffolding/Loaders/{Table,Column,PrimaryKey,UniqueConstraint,Index,ForeignKey,SpatialColumn,JsonCheckConstraint}Loader.cs`.
Each loader receives `(DbConnection, TableFilter, IEngineProfile)` as
explicit dependencies. The factory itself shrinks to roughly 200 LOC.

Every loader issues its INFORMATION_SCHEMA query with
`WHERE TABLE_NAME IN (@t0, @t1, ...)` when the operator supplies a
table filter, eliminating the over-fetch path. `MySqlScaffoldingState` is
removed; each scaffolding run gets a per-call `ScaffoldingContext` that
the orchestrator constructs and passes through.

The test strategy splits into two tiers:

1. **Default tier:** SQLite-in-memory replay. Per-loader fixture loads a
   captured INFORMATION_SCHEMA dump and asserts the loader's translation
   against a frozen golden. Fast, deterministic, runs in every PR.
2. **Live-database tier:** integration tests against a real MySQL/MariaDB
   instance for the small set of pathways (spatial-column SRID, JSON-check
   detection, identifier-quoting edge cases) where the replay shape cannot
   capture engine-specific behavior.

### Consequences

- Good, because scaffolding changes stay local to one loader and shared state is explicit.
- Bad, because cross-aspect changes require coordination across the pipeline context.

#### Positive

- Per-loader unit coverage becomes structurally possible; the index
  loader's `SUB_PART` handling can be pinned in isolation.
- Over-fetch elimination cuts scaffold latency on filtered schemas by
  roughly one to two orders of magnitude.
- The `EngineProfile` dependency (per D-004) replaces ad-hoc
  `IsMariaDb` branches; each loader explicitly declares which syntax
  variants it consumes.
- Mutable shared state is gone; each scaffolding run is isolated by
  construction.

#### Negative

- The split is a wide-touching refactor across eight loader files plus the
  orchestrator. It must land in a single commit or short series, not as a
  long-running parallel-implementation branch.
- The SQLite-replay fixture requires a captured INFORMATION_SCHEMA dump
  per engine version under test. The dumps live under
  `tests/.../Fixtures/Scaffolding/`; they need to be regenerated when the
  supported engine matrix changes.

#### Neutral

- The public scaffolding surface (`OnConfiguring`,
  `MySqlDatabaseModelFactory.Create(...)`) stays unchanged; this is a pure
  internal refactor.

### Confirmation

- Run scaffolding unit tests and live schema round-trip tests.
- Review `MySqlDatabaseModelFactory` as orchestration-only code.

## Pros and Cons of the Options

### Per-aspect loader pipeline

- Good, because each metadata concern has one focused query and mapping boundary.
- Bad, because the orchestrator must preserve loader order and shared context invariants.

### One monolithic database model factory

- Good, because control flow is visible in a single file.
- Bad, because query, mapping, state, and engine concerns become difficult to review.

### Generic reflection-driven loader framework

- Good, because new loader types could share infrastructure.
- Bad, because the abstraction would hide SQL and introduce complexity without proven consumers.

## More Information

### Implementation Snapshot

- eight per-aspect loaders under `Internal/Scaffolding/Loaders/`, orchestrated by a 124-LOC `MySqlDatabaseModelFactory`.

### Implementation Notes

- `MySqlDatabaseModelFactory` shrank from 812 LOC to 124 LOC; the per-aspect loaders live as `TableLoader`, `ColumnLoader`, `PrimaryKeyLoader`, `UniqueConstraintLoader`, `IndexLoader`, `SpatialColumnLoader`, `ForeignKeyLoader`, `JsonCheckConstraintLoader`.
- `ScaffoldingPipelineContext` carries the per-call state (live connection, in-flight `DatabaseModel`, table-filter, engine capabilities, MariaDB JSON_VALID column set, lookup dictionaries).
- `ScaffoldingHelpers.AppendTableNameFilter` binds `WHERE TABLE_NAME IN (@t0, @t1, ...)` as SQL parameters; the loaders keep a client-side `tableFilter.Matches` belt-and-suspenders check so a test stub that ignores parameters still returns deterministic results.
- `IndexLoader` reads `SUB_PART` and emits `MySqlAnnotationNames.IndexPrefixLength` as an `int[]` (one entry per indexed column, `0` when the column has no prefix length). The previous monolith silently dropped `SUB_PART`.
- `MySqlScaffoldingState` renamed to `MySqlScaffoldingContext` with an explicit `Begin()` per-call reset method; the DI lifetime stayed Singleton because the EF Core `ProviderCodeGenerator.GenerateUseProvider(string, MethodCallCodeFragment?)` contract has no model parameter through which the cross-service `DetectedServerVersionText` + `UsesNetTopologySuiteScaffolding` flags could flow.
- Test-double strategy: per-loader unit tests use a hand-rolled stub `DbConnection` / `DbCommand` / `DbDataReader` triple (no SQLite replay needed); the integration tier (`MySqlScaffoldingFilterTests`) exercises the live server-side filter on a 20-table fixture against the MySQL 8.4 LTS container.

### Additional Alternative Rationale

- **Status quo (812-LOC monolith).** Rejected: every unit-test failure in
  this area requires the integration suite; over-fetch is a real
  performance cost on large schemas.
- **Mock the `DbConnection` via a hand-rolled `MockProvider`.** Rejected:
  brittle. SQLite-in-memory captures INFORMATION_SCHEMA semantics far more
  faithfully than a hand-rolled mock can, and the replay tier doubles as
  documentation of the expected query shape.
- **Generate the loader hierarchy via a source generator.** Rejected:
  premature for eight loaders. The structural seam is the right granularity
  for hand-written code; a generator becomes interesting only if a third
  consumer (for example, schema-diff) needs the same per-table per-loader
  composition.

### Re-evaluation Triggers

- A future MySQL or MariaDB release deprecates an INFORMATION_SCHEMA view
  the loaders depend on; the loader-per-view split makes the impact local.
- An EF Core change to `DatabaseModelFactory.Create(...)` that requires a
  different orchestration shape (for example, async-streaming for very
  large schemas); the orchestrator would need to be rewritten but the
  per-loader logic would survive.
- A user-reported scaffolding scenario where the SQLite-replay fixture
  diverges from real engine behavior in a way the integration tier did not
  catch; the test-tier boundary would shift.
- A loader needs state that cannot be represented by the operation-scoped context.
- Large-schema evidence shows loader query count or memory growth outside budgets.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Scaffolding/MySqlDatabaseModelFactory.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Scaffolding/Loaders/`

### Sources

- No external sources; repository evidence only.
