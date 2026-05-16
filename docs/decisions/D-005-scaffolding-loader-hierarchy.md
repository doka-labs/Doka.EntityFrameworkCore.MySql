# D-005 -- Scaffolding-Loader-Hierarchie

- **Status:** Accepted
- **Date:** 2026-05-16
- **Scope:** `Internal/Scaffolding/` reverse-engineering surface
- **Implementation:** deferred to a follow-up commit

## Context

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

## Decision

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

## Consequences

### Positive

- Per-loader unit coverage becomes structurally possible; the index
  loader's `SUB_PART` handling can be pinned in isolation.
- Over-fetch elimination cuts scaffold latency on filtered schemas by
  roughly one to two orders of magnitude.
- The `EngineProfile` dependency (per D-004) replaces ad-hoc
  `IsMariaDb` branches; each loader explicitly declares which syntax
  variants it consumes.
- Mutable shared state is gone; each scaffolding run is isolated by
  construction.

### Negative

- The split is a wide-touching refactor across eight loader files plus the
  orchestrator. It must land in a single commit or short series, not as a
  long-running parallel-implementation branch.
- The SQLite-replay fixture requires a captured INFORMATION_SCHEMA dump
  per engine version under test. The dumps live under
  `tests/.../Fixtures/Scaffolding/`; they need to be regenerated when the
  supported engine matrix changes.

### Neutral

- The public scaffolding surface (`OnConfiguring`,
  `MySqlDatabaseModelFactory.Create(...)`) stays unchanged; this is a pure
  internal refactor.

## Re-evaluation triggers

- A future MySQL or MariaDB release deprecates an INFORMATION_SCHEMA view
  the loaders depend on; the loader-per-view split makes the impact local.
- An EF Core change to `DatabaseModelFactory.Create(...)` that requires a
  different orchestration shape (for example, async-streaming for very
  large schemas); the orchestrator would need to be rewritten but the
  per-loader logic would survive.
- A user-reported scaffolding scenario where the SQLite-replay fixture
  diverges from real engine behavior in a way the integration tier did not
  catch; the test-tier boundary would shift.

## Alternatives considered

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
