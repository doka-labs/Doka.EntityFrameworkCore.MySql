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

# D-007 -- Use shape-aware multi-row INSERT and generated-value readback

## Context and Problem Statement

At the original decision on 2026-05-16,
`MySqlModificationCommandBatch` inherited from
`AffectedCountModificationCommandBatch` and capped `MaxBatchSize` at 1000,
overriding higher user-configured values. That implementation
issued one INSERT statement per row plus a follow-up `SELECT LAST_INSERT_ID()`
(or `SELECT ROW_COUNT()` for affected-count tracking). Two consequences:

1. **Multi-row writes.** MySQL and MariaDB both support multi-row VALUES
   (`INSERT INTO t(a,b) VALUES (...),(...),(...)`). The original rationale
   reported a 3-10x throughput difference for 10000-row bulk inserts. That
   historical observation is not a measurement or promise for the later
   known-key readback path.
2. **Generated values.** MariaDB 10.5+ supports `INSERT ... RETURNING`;
   MySQL 8.4 has no corresponding clause. Separate insert and readback
   statements introduce SQL execution and result-processing overhead. The
   number of SQL statements alone does not establish network round trips.

The 2026-10-19 amendment addresses MySQL groups whose complete physical
primary keys are already available. Computed or defaulted non-key columns
previously forced those groups through per-row insert/readback despite their
stable keys. A safe grouped path must preserve native key comparisons and
must not assume that result rows arrive in input order.

## Decision Drivers

- Bulk inserts need fewer round trips without weakening correctness.
- Generated and trigger-modified values must map to the right entity.
- MySQL and MariaDB syntax differences must remain explicit.
- Result correlation must respect physical keys, transactions, and declared triggers.
- Readback memory and SQL size must remain bounded by the modification batch.

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
   same column set. The batch size is capped by the
   configured maximum, the provider's 1000-command cap, the 65535-parameter
   ceiling, and a `max_allowed_packet` heuristic. The estimate includes
   additional readback SQL and repeated key values before submitting. When the
   user-supplied `MaxBatchSize` exceeds the parameter-count cap, a
   logger warning fires once per batch shape so the consumer learns the
   effective cap.
2. **RETURNING routing.** When the active `EngineProfile` (per D-004)
   declares `SupportsReturningClause`, the INSERT command appends
   `RETURNING <id-column>` and reads the generated value from the
   single round-trip's result set. The fallback for unavailable server-generated identities continues to use
   `LAST_INSERT_ID` on engines without RETURNING. Trigger-modified
   columns the consumer declared via
   `ValueGeneratedOnAddOrUpdate(...)` are included in the RETURNING list
   automatically.
3. **MySQL known-key readback.** For more than one compatible row with
   complete, written, non-temporary physical primary keys, emit one
   multi-row insert followed by one compound `SELECT ... UNION ALL ...`.
   Each branch compares the real table's key to the original parameters
   and returns the requested generated columns plus a group-relative
   ordinal. The result consumer checks column count, ordinal range,
   uniqueness, exact cardinality, and EOF while streaming values to the
   corresponding commands. No CLR key dictionary or full output buffer
   is required.

The known-key path requires a complete EF trigger model. Applications must
register every existing table trigger with `HasTrigger(...)`, including
triggers maintained outside EF migrations and in compiled models. A declared
trigger on any physical table mapping, Doka's MySQL temporal triggers, an
explicitly configured non-InnoDB engine, or an unavailable key retains the
prior per-row path. The provider does not discover physical triggers through catalog I/O on
each save. This complete-model prerequisite was explicitly approved for the
2026-10-19 amendment.

The physical inserted table must use InnoDB. An absent storage-engine
annotation is not physical discovery: Doka omits `ENGINE` in CREATE TABLE,
so this case assumes an InnoDB server default and model/schema consistency.
Applications can make the intended DDL independent of that default with
`modelBuilder.Entity<Document>().UseStorageEngine("InnoDB")`. Doka does not
query the catalog to detect a different default or a manual engine change.
A table created under a MyISAM default, or manually changed to MyISAM, cannot
be rolled back by opening an EF transaction. The metadata-free path therefore
requires actual InnoDB; an explicit non-InnoDB annotation retains the fallback.

The grouped insert/readback requires a transaction even when it is the only
batch. EF creates or uses that transaction through its existing execution
path. With `AutoTransactionBehavior.Never`, it is eligible only when a current,
enlisted, or ambient transaction already exists. Failure and cancellation use
EF's rollback/savepoint behavior; failed saves discard generated-value
sidecars. The fallback retains the application's existing transaction opt-out
semantics.

The test suite covers four edge cases the premortem flagged: rows of
different column sets (forces batch boundary), batches that exceed the
parameter-count cap, batches that exceed the `max_allowed_packet`
estimate, and trigger-modified columns whose RETURNING value differs
from the inserted value. The inherited non-shared update suite also
verifies exception-entry attribution for a failed multi-row insert.

### Consequences

- Good, because supported bulk inserts use fewer SQL statements and MariaDB can return generated values.
- Bad, because batch splitting and result mapping add a larger correctness surface.

#### Positive

- Multi-row SQL reduces insert-statement amplification for qualifying
  groups. MariaDB returns generated values without a follow-up SELECT;
  qualifying MySQL groups share one compound readback instead of one
  readback statement per row. Workload-specific elapsed-time and allocation
  evidence must be measured separately from these statement counts.
- Trigger-modified values remain observable through the existing supported
  engine route; MySQL tables with declared triggers retain their fallback.
- `SupportsReturningClause` stops being a dead knob.
- The parameter-count and packet-size caps surface as logger warnings
  instead of silent batch splits.

#### Negative

- The implementation is wide: it touches the modification-command-batch
  hierarchy, the SQL-generator surface, and the value-bookkeeping code
  path that previously assumed one-row-one-statement.
- MySQL known-key correlation relies on a complete trigger declaration in
  the EF model. An undeclared key-rewriting trigger can invalidate that
  prerequisite even if all expected keys remain present.
- Without explicit engine metadata, eligibility assumes an InnoDB server
  default and an unchanged physical engine. A nontransactional physical
  table cannot provide the rollback guarantee of the InnoDB contract.
- RETURNING's interaction with server-side AUTO_INCREMENT differs from
  `LAST_INSERT_ID`'s: `LAST_INSERT_ID()` returns the
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
- Run `MySqlGeneratedValueSqlTests` for eligible shapes, key mappings,
  fallback routing, and batch-cost accounting.
- Run `MySqlGeneratedValueReadbackTests` in the unit project for permuted
  and malformed positional results, cancellation, and exact cardinality.
- Run the integration `MySqlGeneratedValueReadbackTests` on MySQL and
  MariaDB for native mappings, trigger fallback, caller transactions,
  rollback, cancellation, retry, and the 1000/1001 batch boundary.
- Measure fixed before/after generated-value and write-only controls on
  both engine families. Keep SQL/ADO counts, elapsed time, and managed
  allocations separate; those observations do not create a new release
  or benchmark-ratio gate.

## Pros and Cons of the Options

### Shape-aware batching with engine-routed RETURNING

- Good, because it combines throughput with correct generated-value handling.
- Bad, because the batcher must split on shape, parameter, packet, and engine boundaries.

### Always emit one INSERT per row

- Good, because generated-value mapping is straightforward on every engine.
- Bad, because per-row SQL and result-processing overhead remain.

### Always emit one multi-row INSERT

- Good, because write-only throughput is maximized.
- Bad, because MySQL cannot safely map every generated value through one LAST_INSERT_ID result.

## More Information

### Implementation Snapshot

- `MySqlUpdateSqlGenerator.AppendBulkInsertOperation` routes between
  single-row, multi-row write-only, MariaDB `RETURNING`, and MySQL
  known-key readback paths. MariaDB 10.5+ retains
  `INSERT ... VALUES (..),(..) RETURNING ...`. MySQL uses one multi-row
  insert plus one ordinal-correlated compound readback only when every
  command satisfies the physical-key and trigger prerequisites.
  Other generated-value shapes retain per-row inserts/readback because
  `LAST_INSERT_ID()` cannot identify every generated identity.
- `MySqlModificationCommandBatch` buffers consecutive added commands
  when table, schema, write columns, and read columns have the same
  shape. Shape changes, non-insert commands, and `Complete` flush the
  buffer.
- `MySqlBulkInsertReturningTests` covers auto-increment values,
  trigger-modified columns, write-only inserts, the MySQL fallback, and
  shape splits. `BulkInsertBenchmark` measures throughput against the
  per-row baseline. The additional unit and integration
  `MySqlGeneratedValueReadbackTests` cover the MySQL known-key path without
  changing the existing MariaDB route.

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
- The MySQL known-key SELECT immediately follows its insert and checks
  `ROW_COUNT()` against the complete group in every branch. Original key
  parameters retain the real key columns' native comparison semantics;
  result ordinals carry correlation independently of result order.
- The positional reader keeps only bounded ordinal bookkeeping in
  addition to EF's existing modification commands. Invalid columns,
  repeated or out-of-range ordinals, missing rows, and extra rows fail
  instead of silently updating the wrong entry. Async consumption passes
  the cancellation token through the final EOF read.
- Repeated readback key parameters and generated SQL contribute to the
  existing batch-size estimates. The server's actual packet limit remains
  authoritative; the estimate is not a discovery query or an exact packet
  measurement.
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
  SQL overhead; RETURNING capability remains unused.
- **Multi-row INSERT only behind an opt-in knob.** Rejected: routing can
  determine eligibility from existing model and command metadata, so a
  second public switch would duplicate that contract.
- **Use `LOAD DATA INFILE` for very large bulk inserts.** Rejected as
  default: requires filesystem access, is not EF-idiomatic, and the
  multi-row VALUES path already delivers the order-of-magnitude
  improvement without those caveats. The path may make sense as an
  explicit bulk-insert extension later.
- **Derive identities arithmetically from `LAST_INSERT_ID()`.** Rejected:
  the first generated identity is not a contract for every input row's
  resulting key. Unknown server-generated identities retain the fallback.
- **Join a derived request-key table to the inserted table.** Rejected for
  this implementation: derived columns can introduce type and collation
  coercion different from comparing the physical key to its parameter.
  Direct-key UNION ALL branches retain native column comparison.
- **Map returned rows by result order.** Rejected: row order is not the
  correlation contract. An explicit validated ordinal selects the command.
- **Discover triggers from the database catalog during every save.**
  Rejected for this amendment: it adds I/O and mutable-schema observations
  to the modification path. The approved contract requires a complete EF
  trigger model and retains the fallback for declared triggers, including
  triggers on other mappings of the same physical table.

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
- An application requires undeclared physical triggers or physical storage
  engines to be discovered instead of maintaining model/schema consistency.
- EF changes positional result mapping, transaction creation, or failed-save
  generated-value discard behavior within the supported dependency range.
- Native measurements show a new key shape for which the compound
  readback does not retain its statement, allocation, or correctness contract.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.
- 2026-07-27: Corrected MySQL RETURNING support and documented MariaDB
  multi-row error attribution.
- 2026-10-19: Added transactional MySQL known-key generated-value readback,
  explicit validated ordinals, bounded cost accounting, and the approved
  complete-trigger-model prerequisite. Recorded the physical InnoDB and
  model/schema-consistency prerequisites, including explicit engine DDL and
  the nontransactional-table counterexample. Preserved MariaDB RETURNING and
  unsupported-shape fallback behavior.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Update/MySqlModificationCommandBatch.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Update/MySqlUpdateSqlGenerator.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Update/MySqlUpdateSqlGenerator.GeneratedValues.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Update/MySqlModificationCommandBatch.GeneratedValues.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Update/MySqlGeneratedValueSqlTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Update/MySqlGeneratedValueReadbackTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Update/MySqlGeneratedValueReadbackTests.cs`

### Sources

- [MySQL 8.4 INSERT Statement](https://dev.mysql.com/doc/refman/8.4/en/insert.html)
  (primary source; retrieved 2026-07-27)
- [MySQL 8.4 Information Functions](https://dev.mysql.com/doc/refman/8.4/en/information-functions.html)
  (primary source; retrieved 2026-10-19)
- [MariaDB INSERT...RETURNING][mariadb-insert-returning]
  (primary source; retrieved 2026-10-19)
- [MariaDB error 1062](https://mariadb.com/docs/server/reference/error-codes/mariadb-error-codes-1000-to-1099/e1062)
  (primary source; retrieved 2026-07-27)
- [MySQL 8.4 Set Operations](https://dev.mysql.com/doc/refman/8.4/en/set-operations.html)
  (primary source; retrieved 2026-10-19)
- [MySQL 8.4 Trigger Syntax](https://dev.mysql.com/doc/refman/8.4/en/trigger-syntax.html)
  (primary source; retrieved 2026-10-19)
- [MySQL 8.4 Collation Coercibility](https://dev.mysql.com/doc/refman/8.4/en/charset-collation-coercibility.html)
  (primary source; retrieved 2026-10-19)
- [EF Core 10.0.8 positional result mapping](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Update/AffectedCountModificationCommandBatch.cs)
  (primary source; retrieved 2026-10-19)
- [EF Core 10.0.8 transaction execution](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Update/Internal/BatchExecutor.cs)
  (primary source; retrieved 2026-10-19)
- [EF Core 10.0.8 failed-save generated-value discard](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore/ChangeTracking/Internal/StateManager.cs)
  (primary source; retrieved 2026-10-19)
- [MySQL 8.4 CREATE TABLE storage-engine selection](https://dev.mysql.com/doc/refman/8.4/en/create-table.html)
  (primary source; retrieved 2026-10-19)
- [MySQL 8.4 rollback failure for nontransactional tables](https://dev.mysql.com/doc/refman/8.4/en/nontransactional-tables.html)
  (primary source; retrieved 2026-10-19)

[mariadb-insert-returning]:
  https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/inserting-loading-data/insertreturning
