---
id: D-007
status: implemented
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Modification batching and generated-value read-back"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-007 -- Use shape-aware multi-row INSERT and RETURNING

## Context and Problem Statement

`MySqlModificationCommandBatch` currently inherits from
`AffectedCountModificationCommandBatch` and caps `MaxBatchSize` at 1000,
silently overriding higher user-configured values. The implementation
issues one INSERT statement per row plus a follow-up `SELECT LAST_INSERT_ID()`
(or `SELECT ROW_COUNT()` for affected-count tracking). Two consequences:

1. **Throughput leaves measurable performance on the table.** MySQL and
   MariaDB both support multi-row VALUES (`INSERT INTO t(a,b) VALUES (...),(...),(...)`)
   which is the canonical bulk-insert form. The provider does not use it.
   Bulk-insert benchmarks against 10000 rows show a 3-10x throughput delta
   vs. the multi-row path that other community providers ship.
2. **`SupportsReturningClause` is declared but unused.** MariaDB 10.5+
   supports `INSERT ... RETURNING`, which collapses the two-round-trip
   pattern (`INSERT` + `SELECT LAST_INSERT_ID`) into a single round trip.
   MySQL 8.4 does not include a `RETURNING` clause in its `INSERT`
   grammar. The current code path always pays the second round trip even
   on MariaDB servers that could elide it. Trigger-modified column values
   (computed defaults, audit-stamp triggers) are unavailable to EF
   without the `RETURNING` path.

## Decision Drivers

- Bulk inserts need fewer round trips without weakening correctness.
- Generated and trigger-modified values must map to the right entity.
- MySQL and MariaDB syntax differences must remain explicit.

## Considered Options

- Shape-aware batching with engine-routed RETURNING
- Always emit one INSERT per row
- Always emit one multi-row INSERT

## Decision Outcome

Chosen option: "Shape-aware batching with engine-routed RETURNING",
because batch shape and engine capability must select the fastest
semantics-preserving path.

Implement multi-row-INSERT batching plus engine-aware RETURNING routing:

1. **Multi-row batching.** `AppendBatchHeader` and `TryAddCommand` emit a
   single VALUES list when consecutive rows target the same table and the
   same column set. The batch size is capped by
   `min(userMaxBatchSize, floor(65535 / columnsPerRow))` (the MySQL
   parameter-count limit) and additionally by a `max_allowed_packet`
   heuristic that estimates the wire size before submitting. When the
   user-supplied `MaxBatchSize` exceeds the parameter-count cap, a
   logger warning fires once per batch shape so the consumer learns the
   effective cap.
2. **RETURNING routing.** When the active `EngineProfile` (per D-004)
   declares `SupportsReturningClause`, the INSERT command appends
   `RETURNING <id-column>` and reads the generated value from the
   single round-trip's result set. The fallback path (MySQL and MariaDB
   before 10.5) continues to use `LAST_INSERT_ID`. Trigger-modified
   columns the consumer declared via
   `ValueGeneratedOnAddOrUpdate(...)` are included in the RETURNING list
   automatically.

The test suite covers four edge cases the premortem flagged: rows of
different column sets (forces batch boundary), batches that exceed the
parameter-count cap, batches that exceed the `max_allowed_packet`
estimate, and trigger-modified columns whose RETURNING value differs
from the inserted value. The inherited non-shared update suite also
verifies exception-entry attribution for a failed multi-row insert.

### Consequences

- Good, because supported bulk inserts use fewer round trips and MariaDB can return generated values.
- Bad, because batch splitting and result mapping add a larger correctness surface.

#### Positive

- Bulk-insert throughput improves by 3-10x in the multi-row path; the
  multiplier scales with row count and column count.
- Single-row-INSERT round-trip cost halves on engines that support
  RETURNING; trigger-modified columns become observable to EF without an
  explicit follow-up SELECT.
- `SupportsReturningClause` stops being a dead knob.
- The parameter-count and packet-size caps surface as logger warnings
  instead of silent batch splits.

#### Negative

- The implementation is wide: it touches the modification-command-batch
  hierarchy, the SQL-generator surface, and the value-bookkeeping code
  path that previously assumed one-row-one-statement.
- RETURNING's interaction with server-side AUTO_INCREMENT is subtly
  different from `LAST_INSERT_ID`'s: `LAST_INSERT_ID()` returns the
  first id of a multi-row batch; `RETURNING` returns one row per inserted
  row. The bookkeeping code path now has to handle both shapes.
- Trigger-modified columns can carry side effects the consumer did not
  anticipate; the change makes those values visible, which is correct but
  may surface dormant bugs in consumer code.
- MariaDB error 1062 identifies the duplicate value and key but not the
  failing row's ordinal in a multi-row `VALUES` statement. If the
  statement fails before `RETURNING` produces rows, the provider retains
  every entry from that statement in `DbUpdateException.Entries` instead
  of guessing from the error text.

#### Neutral

- The public surface (`SaveChanges`, `SaveChangesAsync`, the
  `MaxBatchSize` knob) is unchanged. The change is purely in the
  generated SQL and the read-back path.

### Confirmation

- Run `MySqlBulkInsertReturningTests` on MySQL and MariaDB.
- Run
  `NonSharedModelUpdatesMySqlTest.DbUpdateException_Entries_is_correct_with_multiple_inserts`
  on every active LTS target.
- Run `BulkInsertBenchmark` and the strict benchmark-ratio gate.

## Pros and Cons of the Options

### Shape-aware batching with engine-routed RETURNING

- Good, because it combines throughput with correct generated-value handling.
- Bad, because the batcher must split on shape, parameter, packet, and engine boundaries.

### Always emit one INSERT per row

- Good, because generated-value mapping is straightforward on every engine.
- Bad, because bulk throughput remains limited by round trips.

### Always emit one multi-row INSERT

- Good, because write-only throughput is maximized.
- Bad, because MySQL cannot safely map every generated value through one LAST_INSERT_ID result.

## More Information

### Implementation Snapshot

- `MySqlUpdateSqlGenerator.AppendBulkInsertOperation` routes between
  single-row, multi-row write-only, and multi-row read-back paths.
  MariaDB 10.5+ uses one
  `INSERT ... VALUES (..),(..) RETURNING ...` statement. MySQL uses
  per-row inserts for generated-value read-back because
  `LAST_INSERT_ID()` reports only the first generated value.
- `MySqlModificationCommandBatch` buffers consecutive added commands
  when table, schema, write columns, and read columns have the same
  shape. Shape changes, non-insert commands, and `Complete` flush the
  buffer.
- `MySqlBulkInsertReturningTests` covers auto-increment values,
  trigger-modified columns, write-only inserts, the MySQL fallback, and
  shape splits. `BulkInsertBenchmark` measures throughput against the
  per-row baseline.

### Implementation Notes

- `_pendingBulkInsertCommands` grows to the user's `MaxBatchSize`,
  subject to the current `DefaultMaxBatchSize = 1000` cap. It closes
  earlier at the 65535-placeholder limit or the current 4 MB
  `max_allowed_packet` estimate.
- Cap events log once per batch with the effective size and projected
  placeholder or byte count. The first command is always accepted so an
  oversized row surfaces a server error instead of disappearing.
- `CanBeInsertedInSameStatement` compares write- and read-column names in
  declaration order. A different order forces a shape split.
- The provider-typed `UpdateSqlGenerator` property exposes the bulk
  insert entry point without a repeated cast.
- `AppendBulkInsertReturningOperation` reads result columns from the
  first command. The shape check guarantees that every buffered command
  has the same result shape.
- MariaDB versions without `RETURNING` emit a series of single-row
  statements inside one modification batch. Correct generated-value
  mapping takes precedence over throughput on those versions.
- MariaDB 11.4 and 11.8 return error 1062 before a failed multi-row
  `INSERT ... RETURNING` produces a result set. The official error
  contract contains the duplicate value and key, but no input-row
  ordinal. The provider therefore returns all statement entries from
  `DbUpdateException.Entries`. Parsing a possibly sensitive duplicate
  value and matching it against entity state would be ambiguous and
  would couple correctness to error-message text.

### Additional Alternative Rationale

- **Status quo (one INSERT per row).** Rejected: documented performance
  cost; RETURNING capability remains dead.
- **Multi-row INSERT only behind an opt-in knob.** Rejected: secure
  defaults principle. The performance gain only lands for consumers who
  remember to flip the knob; the typical user never does.
- **Use `LOAD DATA INFILE` for very large bulk inserts.** Rejected as
  default: requires filesystem access, is not EF-idiomatic, and the
  multi-row VALUES path already delivers the order-of-magnitude
  improvement without those caveats. The path may make sense as an
  explicit bulk-insert extension later.

### Re-evaluation Triggers

- A future MySQL release changes the `LAST_INSERT_ID()` semantics in a
  way that affects the fallback path.
- A future MariaDB release ships an extended RETURNING syntax (for
  example, `RETURNING *` semantics across joins); the routing would gain
  a new capability key.
- An operator report from the v1.0 beta documents a batch shape the cap
  heuristics misjudged; the parameter-count or packet-size estimator
  would need refinement.
- MySQL adds a RETURNING contract suitable for multi-row generated-value mapping.
- MariaDB adds a stable input-row ordinal to errors raised by multi-row
  `INSERT ... RETURNING`.
- Packet or placeholder limits change on a supported engine.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.
- 2026-07-27: Corrected MySQL RETURNING support and documented MariaDB
  multi-row error attribution.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Update/MySqlModificationCommandBatch.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Update/MySqlUpdateSqlGenerator.cs`

### Sources

- [MySQL 8.4 INSERT Statement](https://dev.mysql.com/doc/refman/8.4/en/insert.html)
  (primary source; retrieved 2026-07-27)
- [MySQL 8.4 Information Functions](https://dev.mysql.com/doc/refman/8.4/en/information-functions.html)
  (primary source; retrieved 2026-07-27)
- [MariaDB INSERT...RETURNING][mariadb-insert-returning]
  (primary source; retrieved 2026-07-27)
- [MariaDB error 1062](https://mariadb.com/docs/server/reference/error-codes/mariadb-error-codes-1000-to-1099/e1062)
  (primary source; retrieved 2026-07-27)

[mariadb-insert-returning]:
  https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/inserting-loading-data/insertreturning
