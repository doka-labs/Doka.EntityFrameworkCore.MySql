# Resilience and Topology Operations

This runbook covers transient retry configuration, commit-unknown incidents,
and connection behavior behind poolers, proxies, and load balancers.

## Galera / MariaDB Cluster Retry Configuration

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

Read-only failovers via MaxScale or ProxySQL stay invisible to the provider; the
proxy decides transparently. Run the
[Galera / MaxScale migration pre-flight](migrations.md#galera-maxscale-migration-pre-flight)
before letting the provider migrate against a fronted cluster.

<a id="mysql-commit-unknown"></a>

## Commit-Unknown Response

When a transaction commit throws after the driver commit API was invoked, its outcome is
genuinely unknown: the server may have applied it before the acknowledgement was lost, or
the request may not have landed. The provider conservatively emits `CommitUnknown`
(EventId 1505) for every such exception because retry classification cannot prove the
server-side outcome. Application code must decide how to disambiguate it.

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

## Connection Pooler / Load Balancer Compatibility

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
