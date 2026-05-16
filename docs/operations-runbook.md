# Operations Runbook

Operational reference for `Doka.EntityFrameworkCore.MySql` in production. Covers the diagnostic event catalog, recovery procedures for advisory-lock and commit-unknown incidents, topology-specific retry guidance, and pre-flight checks for Galera / MaxScale fronted clusters.

The runbook is provider-agnostic at the EF Core layer; every code reference points to a public type or member that is part of the supported provider surface.

## 1. EventId Reference

Every diagnostic event the provider emits carries a numeric `EventId` plus a stable string name. The name is the public API surface; the number is stable too and is the documented retrieval key for log aggregators that filter on integers.

Subsystem ranges:

| Range | Subsystem |
|-------|-----------|
| 1000-1099 | Configuration + model validation |
| 1100-1199 | Migrations + advisory locks |
| 1400-1499 | Scaffolding |
| 1500-1599 | Resilience + execution strategy + transactions |
| 1600-1699 | Spatial |
| 1700-1799 | Bulk insert + batch sizing |

Full catalog (source of truth: `src/Doka.EntityFrameworkCore.MySql/MySqlEventId.cs`):

| EventId | Name | Level | Subsystem | When it fires |
|--------:|------|-------|-----------|---------------|
| 1000 | `ServerVersionResolved` | Information | Configuration | Server-version resolution and capability caching succeed at first connect. |
| 1001 | `InvalidConfiguration` | Error | Configuration | Provider configuration is invalid (missing server version, malformed connection string, conflicting options). |
| 1002 | `SchemaUnsupported` | Error | Configuration | Unsupported MySQL schema configuration is detected; MySQL treats schema and database as synonyms. |
| 1003 | `KeyOrIndexMaxLengthRequired` | Error | Configuration | A keyed or indexed text or binary property omits the required explicit max length. |
| 1004 | `ImplicitDecimalPrecisionDefaulted` | Warning | Configuration | A decimal property falls back to the provider default precision and scale contract (18, 2). |
| 1102 | `LockReleaseFailed` | Warning | Migrations | The migration advisory lock could not be released cleanly via `RELEASE_LOCK`. Disposing the dedicated connection still releases the session-scoped lock implicitly; the warning surfaces an unusual server-side state worth investigating. |
| 1403 | `ForeignKeyPrincipalTableNotScaffolded` | Warning | Scaffolding | A foreign key is skipped during scaffolding because its principal table is excluded by the scaffolding filter. |
| 1500 | `RetryAttempt` | Warning | Resilience | The execution strategy retries a transient failure; attempt counter + previous-exception attached to the log scope. |
| 1501 | `RetryLimitExceeded` | Error | Resilience | The configured retry budget for a transient failure is exhausted. |
| 1502 | `SoftCancellation` | Information | Resilience | The driver completes a command cancellation through the soft-cancel path. |
| 1503 | `HardCancellation` | Warning | Resilience | The driver had to fall back to the hard-cancel path to finish command cancellation. |
| 1504 | `CommandTimeoutExhausted` | Warning | Resilience | A relational command exhausted its configured timeout budget. |
| 1505 | `CommitUnknown` | Warning | Resilience | A transaction commit failed transiently and the commit outcome may be unknown (see section 4). |
| 1600 | `MissingSpatialPackageDuringScaffolding` | Warning | Spatial | Spatial reverse engineering detects spatial artifacts but the optional NetTopologySuite package is not installed. |
| 1601 | `InvalidSpatialIndexConfiguration` | Error | Spatial | Spatial index configuration violates the supported provider contract. |
| 1602 | `MissingSpatialTranslation` | Warning | Spatial | A spatial member or method is detected but no supported server translation exists. |
| 1603 | `SpatialSridMismatchDetected` | Warning | Spatial | The translator observed two `ST_Distance` arguments with different SRIDs. MySQL rejects the mismatch with a hard error; MariaDB silently treats both inputs as Cartesian and returns a meaningless result. |
| 1700 | `BulkInsertParameterCountCapped` | Warning | Update | A `SaveChanges` batch would exceed MySQL's 65535-placeholder hard limit; the batch is split at the command that would have crossed the cap. |
| 1701 | `BulkInsertPacketSizeCapped` | Warning | Update | A `SaveChanges` batch would exceed the conservative `max_allowed_packet` budget; the batch is split at the command that would have crossed the cap. |

All emissions go through `MySqlLoggerCategory.*` categories (Configuration, Query, Update, Migrations, Scaffolding, Resilience, Spatial) so log shippers can route by category without parsing event numbers.

## 2. Migration Lock Stuck Procedure

The provider serializes migrations through a database-scoped MySQL advisory lock named `__ef_migrations_lock:<database>` (truncated to a SHA-256 suffix when the combined length exceeds MySQL's 64-character `GET_LOCK` limit). The lock is held on a dedicated connection for the full duration of the migration and released through `RELEASE_LOCK` plus dedicated-connection dispose; the timeout for `GET_LOCK` is 60 seconds.

Stuck conditions and their recovery:

### Symptom: migration startup times out with `TimeoutException`

```
Could not acquire the MySQL advisory lock '__ef_migrations_lock:<database>'
within 60 seconds. Another migration process may be running concurrently.
```

Root cause: another connection on the same server still holds the named lock. Two paths:

1. **Legitimate concurrent migration runner** -- a second instance of the application is also starting up and is mid-migration. Wait, retry, or coordinate the startup order.
2. **Orphaned lock from a crashed migrator** -- the connection that held the lock did not dispose cleanly. The lock is session-scoped, so a TCP-dead session is reaped by the server within `wait_timeout` (default 28800 seconds = 8 hours; on most CI containers shorter), but in a pinch the operator can force-release.

### Recovery: identify and kill the lock holder

```sql
-- Who owns the lock right now? IS_USED_LOCK returns the connection_id of the
-- owner, or NULL when the lock is free.
SELECT IS_USED_LOCK('__ef_migrations_lock:<database>') AS owner_connection_id;

-- Inspect the owning session before killing it; confirm it is the orphan and
-- not a live migration.
SELECT id, user, host, db, command, time, state, info
FROM information_schema.processlist
WHERE id = <owner_connection_id>;

-- Kill the orphan session; the server releases all session-scoped locks
-- automatically on disconnect.
KILL <owner_connection_id>;
```

### Recovery: manual lock release without killing the session

Only when the owning session is genuinely the operator's own diagnostic session and `KILL` is overkill:

```sql
-- RELEASE_LOCK is session-scoped and only succeeds when issued from the
-- session that holds the lock. Returns 1 on release, 0 when the lock was
-- not held by this session, NULL when the lock does not exist.
SELECT RELEASE_LOCK('__ef_migrations_lock:<database>');
```

### After recovery

Restart the migrator. The provider logs `LockReleaseFailed` (EventId 1102) only when the provider's own normal release path threw; an operator-side `KILL` does not produce this event.

## 3. Galera / MariaDB Cluster Retry Configuration

Galera clusters introduce additional transient-failure modes that benefit from a higher retry budget than a single-node MySQL deployment. The provider's `EnableRetryOnFailure(...)` knob is the single tuning surface:

```csharp
options.UseMySql(
    connectionString,
    MySqlServerVersion.MariaDb(new Version(11, 8, 0)),
    mysql => mysql.EnableRetryOnFailure(
        maxRetryCount: 6,
        maxRetryDelay: TimeSpan.FromSeconds(30)));
```

Defaults: `maxRetryCount` is 6, `maxRetryDelay` is the EF Core convention (30 seconds). The detector treats the following MySQL error codes as retryable:

- `ConnectionCountError`, `TooManyUserConnections` -- pool saturation upstream.
- `UnableToConnectToHost`, `ServerShutdown` -- node restart, rolling-cluster maintenance.
- `LockWaitTimeout`, `LockDeadlock`, `XARBDeadlock`, `UserLockDeadlock` -- intra-cluster contention.
- Wrapped `SocketException` or `IOException` -- transport-level disconnect.
- `MySqlException.IsTransient == true` -- driver-level classification.

`OperationCanceledException` and `CommandTimeoutExpired` are never retried -- the consumer's cancellation token wins, and a command-timeout is treated as a non-transient capacity signal.

For Galera specifically, the retry budget should account for the worst-case re-routing latency after a node-evict event. Six attempts with linear-then-randomized backoff up to 30 seconds cover the majority of `wsrep_provider`-driven evict + re-elect cycles on Galera 4 and MariaDB 11.x.

Read-only failovers via MaxScale or ProxySQL stay invisible to the provider; the proxy decides transparently. See section 6 for the pre-flight checks the operator should run before letting the provider migrate against a fronted cluster.

## 4. Commit-Unknown Response

When a transaction commit fails transiently, the commit outcome is genuinely unknown -- the server may have applied the commit before the connection dropped, or the connection may have died before the commit landed. The provider emits `CommitUnknown` (EventId 1505) on that exact path and the application code must decide how to disambiguate.

### Pattern: retry with idempotent verification

EF Core ships `ExecuteInTransactionAsync` exactly for this case. The pattern wraps the transactional work with a `verifySucceeded` callback that probes the database for the commit's observable side-effect:

```csharp
public async Task TransferAsync(
    Guid transferId,
    Guid fromAccount,
    Guid toAccount,
    decimal amount,
    CancellationToken cancellationToken)
{
    var strategy = _context.Database.CreateExecutionStrategy();

    await strategy.ExecuteInTransactionAsync(
        operation: async () =>
        {
            // Commit-the-side-effect: insert a unique transfer record first.
            _context.Transfers.Add(new Transfer
            {
                Id = transferId,
                FromAccount = fromAccount,
                ToAccount = toAccount,
                Amount = amount,
            });

            await _context.SaveChangesAsync(cancellationToken);

            // Followed by the side-effects that depend on it.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE accounts SET balance = balance - {amount} WHERE id = {fromAccount}",
                cancellationToken);

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE accounts SET balance = balance + {amount} WHERE id = {toAccount}",
                cancellationToken);
        },
        verifySucceeded: async () =>
        {
            // After a retried commit, ask the database whether the transfer
            // record exists. If yes, the previous attempt's commit landed and
            // the operation must NOT run again.
            return await _context.Transfers
                .AnyAsync(t => t.Id == transferId, cancellationToken);
        });
}
```

`ExecuteInTransactionAsync` calls `verifySucceeded` after a retry only when the previous attempt's commit raised a transient failure. When `verifySucceeded` returns `true`, the strategy treats the previous attempt as successful and stops retrying; when it returns `false`, the operation re-runs in a fresh transaction.

### Pre-conditions for the pattern to work

- The operation must own a stable idempotency key (`transferId` above) the consumer generates BEFORE the transaction opens.
- The key must be checkable through the same database the transaction targets; reaching a separate read-replica risks reading stale state.
- The operation must be safe to re-run when `verifySucceeded` returns `false` (re-issuing the transfer never double-applies because the conflicting record is the verification proxy).

### When the pattern is NOT applicable

- Multi-statement transactions that touch independent rows without a single canonical side-effect to probe -- those need application-level reconciliation or a saga.
- Operations against a database the application cannot query later (one-shot write to a remote system).
- Transactions whose only side-effect is the SELECT-FOR-UPDATE row-lock the commit released; without a write to probe, there is nothing to verify.

For those cases, the application must reconcile through a separate idempotency table managed outside the transaction, or accept the rare double-execution and design downstream consumers to be idempotent themselves.

## 5. Connection Pooler / Load Balancer Compatibility

The migration advisory lock and the commit-unknown verification both depend on **session stickiness**. The same connection that ran `GET_LOCK` must run `RELEASE_LOCK`; the same connection that committed the transaction must observe the side-effect of `verifySucceeded`. Connection poolers and load balancers that re-route mid-session break both contracts.

### Supported topologies

- **Direct connection** to MySQL or MariaDB. Standard; no special configuration.
- **MySqlConnector's built-in connection pool** (default). Per-application pool; sessions stay sticky for the lifetime of the leased connection. Lock + commit semantics unaffected.
- **ProxySQL with session pinning**: configure `mysql-multiplexing=0` on the connection group fronted by the provider, OR set `default_session_track_gtids=OWN_GTID` and rely on session-pinning on transaction boundaries. Without one of these, ProxySQL multiplexes statements across backend sessions and the migration lock + commit-unknown verification both break silently.
- **MaxScale (read-write splitter)** with `transaction_replication=true` and `causal_reads=true`: transaction routing pins the session; reads inside a transaction stay on the master. The migration advisory lock works on the master endpoint; commit-unknown verification works when the verifying `SELECT` is inside a `TransactionScope` or `ExecuteInTransactionAsync` callback (both pin to the master).

### NOT supported topologies

- **PgBouncer-style transaction-pooling** -- semantically equivalent solutions for MySQL (e.g. ProxySQL with multiplexing on) recycle the backend session between statements. The advisory lock and the dedicated-connection contract both break. The provider does not detect this misconfiguration; the symptom is migrations either deadlocking or silently running concurrently.
- **L4 load balancers** (HAProxy in TCP mode without `option mysql-check` source-IP-hash) -- without hash-based pinning, a re-connect routes to a different backend and the advisory lock is held against the wrong node.

### Operator pre-deployment checklist

1. Identify the topology between the application host and the database (direct, pooler, proxy, L4 balancer).
2. For each layer, verify session stickiness during a single connection lifetime.
3. Run a smoke test:
   ```sql
   -- Open one session, do not close it.
   SELECT GET_LOCK('doka_smoke_test', 5) AS acquired;
   -- Wait a second; on the same session:
   SELECT IS_USED_LOCK('doka_smoke_test') AS still_held_same_session;
   -- Open a SECOND session against the proxy:
   SELECT IS_USED_LOCK('doka_smoke_test') AS still_held_other_session;
   ```
   The first `IS_USED_LOCK` must return the original session's connection_id. The second must return the same connection_id from the other session's vantage point -- proving the lock is observable across sessions and pinned to a single backend. If either probe returns NULL, the topology multiplexes and migrations will misbehave.
4. Document the verified topology in the runbook for the next operator on call.

## 6. Galera / MaxScale Migration Pre-Flight

MariaDB Galera clusters and MaxScale-fronted topologies route writes through the current primary. Migration runs that target a read-only node end in unhelpful errors deep inside the migration pipeline; the pre-flight catches the misconfiguration before any DDL ships.

### Pre-flight script

```sql
-- 1. Confirm the connection lands on a writeable node.
SELECT @@hostname AS node, @@read_only AS is_read_only,
       @@super_read_only AS is_super_read_only;
-- Expected: is_read_only = 0, is_super_read_only = 0.

-- 2. Confirm Galera health (skip on non-Galera MariaDB / on MySQL).
SHOW STATUS LIKE 'wsrep_ready';
SHOW STATUS LIKE 'wsrep_local_state_comment';
SHOW STATUS LIKE 'wsrep_cluster_size';
-- Expected: wsrep_ready = ON, wsrep_local_state_comment = Synced,
-- wsrep_cluster_size >= 2 (single-node Galera is a degraded state).

-- 3. Confirm the advisory lock surface works on this node.
SELECT GET_LOCK('__ef_migrations_pre_flight', 5) AS acquired;
SELECT RELEASE_LOCK('__ef_migrations_pre_flight') AS released;
-- Expected: acquired = 1, released = 1. A 0 / NULL on either step
-- means another concurrent runner is on the same node or the proxy
-- re-routed mid-pre-flight -- do not start the migration.

-- 4. Confirm DDL can begin (smoke a no-op transaction).
START TRANSACTION;
SELECT 1;
COMMIT;
-- Expected: completes without `1290 (HY000): The MySQL server is
-- running with the --read-only option` or Galera flow-control rejection.
```

### Galera-specific options the migrator should set

Galera serializes DDL through the Total Order Isolation protocol; long-running migrations can stall flow control. Configure the migration session with:

```sql
-- Allow up to 1 hour for a single DDL statement before Galera evicts
-- the node for flow-control violation. Tune downward when DDL is
-- known-small; tune upward when the migration includes a known-slow
-- ALTER on a large table.
SET SESSION wsrep_sync_wait = 0;
SET SESSION wsrep_OSU_method = TOI;
```

`wsrep_sync_wait = 0` keeps the migrator from blocking on every other node's apply queue; the advisory lock already serializes against concurrent migrators. `TOI` is the safer of the two OSU methods for typical EF Core migrations (RSU requires explicit per-node DDL replay).

### MaxScale-specific routing

Route the migration session explicitly at the master through MaxScale's hint syntax or by connecting to the read-write splitter on a connection-string-pinned endpoint:

```
Server=maxscale.internal;Port=4006;Database=doka;User ID=migrator;Password=...;
ConnectionAttributes=program_name=ef-core-migrator
```

The `program_name` attribute lets the MaxScale operator route migrations explicitly through a dedicated service group with `master_accept_reads=false` and `slave_selection_criteria=NONE`, isolating the migration session from the read-write splitter's regular traffic.

### After the pre-flight passes

Run the migration normally:

```bash
dotnet ef database update --project src/MyApp --startup-project src/MyApp
```

Watch for `RetryAttempt` (EventId 1500) emissions during the run; on Galera, occasional `LockWaitTimeout` retries are normal as flow control pauses commit groups. Persistent `RetryLimitExceeded` (EventId 1501) or any `LockReleaseFailed` (EventId 1102) is a stop-the-line signal -- consult section 2 before retrying.
