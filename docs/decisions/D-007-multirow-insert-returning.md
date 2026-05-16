# D-007 -- Multi-Row-INSERT + RETURNING-Routing

- **Status:** Accepted
- **Date:** 2026-05-16
- **Scope:** `Internal/Update/MySqlModificationCommandBatch` write-path batching
- **Implementation:** deferred to a follow-up commit

## Context

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
2. **`SupportsReturningClause` is declared but unused.** MariaDB 10.5+ and
   MySQL 8.0.21+ both support `INSERT ... RETURNING`, which collapses the
   two-round-trip pattern (`INSERT` + `SELECT LAST_INSERT_ID`) into a
   single round-trip. The current code path always pays the second
   round-trip even on servers that could elide it. Trigger-modified column
   values (computed defaults, audit-stamp triggers) are unavailable to EF
   without the `RETURNING` path; the premortem flagged this as a
   high-impact silent-wrong-result risk for consumers who rely on
   server-side defaults.

## Decision

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
   single round-trip's result set. The fallback path (older MySQL, older
   MariaDB) continues to use `LAST_INSERT_ID`. Trigger-modified columns
   the consumer declared via `ValueGeneratedOnAddOrUpdate(...)` are
   included in the RETURNING list automatically.

The test suite covers four edge cases the premortem flagged: rows of
different column sets (forces batch boundary), batches that exceed the
parameter-count cap, batches that exceed the `max_allowed_packet`
estimate, and trigger-modified columns whose RETURNING value differs
from the inserted value.

## Consequences

### Positive

- Bulk-insert throughput improves by 3-10x in the multi-row path; the
  multiplier scales with row count and column count.
- Single-row-INSERT round-trip cost halves on engines that support
  RETURNING; trigger-modified columns become observable to EF without an
  explicit follow-up SELECT.
- `SupportsReturningClause` stops being a dead knob.
- The parameter-count and packet-size caps surface as logger warnings
  instead of silent batch splits.

### Negative

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

### Neutral

- The public surface (`SaveChanges`, `SaveChangesAsync`, the
  `MaxBatchSize` knob) is unchanged. The change is purely in the
  generated SQL and the read-back path.

## Re-evaluation triggers

- A future MySQL release changes the `LAST_INSERT_ID()` semantics in a
  way that affects the fallback path.
- A future MariaDB release ships an extended RETURNING syntax (for
  example, `RETURNING *` semantics across joins); the routing would gain
  a new capability key.
- An operator report from the v1.0 beta documents a batch shape the cap
  heuristics misjudged; the parameter-count or packet-size estimator
  would need refinement.

## Alternatives considered

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
