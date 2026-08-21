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

The migration advisory lock depends on backend-session identity: the backend
session that ran `GET_LOCK` must remain available until the provider runs
`RELEASE_LOCK`. Commit-unknown verification has a separate routing requirement:
the verification query must observe the same logical writer and must not be
sent to a stale replica. A topology must satisfy both properties; generic
claims such as "sticky" or "causal" are not sufficient evidence.

### Supported topologies

- **Direct connection** to MySQL or MariaDB. Standard; no special configuration.
- **MySqlConnector's built-in connection pool** (default). Per-application pool; sessions stay sticky for the lifetime of the leased connection. Lock + commit semantics unaffected.
- **ProxySQL with verified writer routing.** ProxySQL documents that executing
  `GET_LOCK()` disables multiplexing permanently for that frontend session. It
  does not disable routing, so query rules must still route `GET_LOCK`,
  `IS_USED_LOCK`, `RELEASE_LOCK`, migration DDL, and commit verification to the
  same writer hostgroup. Disabling the global `mysql-multiplexing` option is a
  conservative alternative, but affects every session. GTID tracking is not a
  substitute for advisory-lock session identity.
- **MaxScale `readwritesplit` with verified primary routing.** Current MaxScale
  routes `GET_LOCK()`, `RELEASE_LOCK()`, and the related lock functions to the
  primary, and routes every statement in an open read-write transaction to the
  primary. `ExecuteInTransactionAsync` therefore keeps the operation and its
  transactional verification on the writer. `causal_reads` controls visibility
  for reads routed to replicas; it is an enum and is not the advisory-lock
  pinning mechanism. `transaction_replay` is likewise a failure-recovery option,
  not a lock-ownership setting.
- **L4 TCP load balancer to one logical writer.** A TCP proxy keeps an
  established frontend connection on its selected backend. Health checks such
  as HAProxy's `option mysql-check` affect backend eligibility, not persistence.
  This topology is supported only when every application instance reaches the
  same logical writer for advisory-lock operations and verification reads.

### NOT supported topologies

- **Transaction-level backend pooling** that can recycle the backend session
  between `GET_LOCK` and `RELEASE_LOCK`. The advisory lock and dedicated-session
  contract break even if transactions themselves are pinned.
- **L4 balancing across independent writable servers.** TCP connection
  persistence and source hashing do not create a cluster-wide advisory lock.
  Different application hosts can acquire the same lock name on different
  servers, and a reconnect can select another server.
- **Read/write splitting that can send verification to a stale replica.** A
  successful commit can then look absent and cause unsafe replay.

### Operator pre-deployment checklist

1. Identify the topology between the application host and the database (direct, pooler, proxy, L4 balancer).
2. For each layer, verify backend-session identity for the full advisory-lock
   lifetime and writer visibility for commit verification.
3. Run the smoke test from every distinct application source or proxy route:
   ```sql
   -- Open one session, do not close it.
   SELECT GET_LOCK('doka_smoke_test', 5) AS acquired;
   -- Wait a second; on the same session:
   SELECT IS_USED_LOCK('doka_smoke_test') AS still_held_same_session;
   -- Open a SECOND session against the proxy:
   SELECT IS_USED_LOCK('doka_smoke_test') AS still_held_other_session;
   -- Back on the first session:
   SELECT RELEASE_LOCK('doka_smoke_test') AS released;
   ```
   Both `IS_USED_LOCK` calls must return the first backend session's
   `CONNECTION_ID()`, and `RELEASE_LOCK` must return `1`. A `NULL`, a different
   owner, or simultaneous acquisition from another application source means the
   topology does not provide the provider's migration-lock contract.
4. In a non-production database, interrupt one post-commit acknowledgement and
   verify that the idempotency probe reads the writer result before retrying.
5. Document the proxy version, routing rules, writer topology, and verification
   evidence for the next operator on call.

## Primary Sources

Retrieved 2026-08-21:

- [ProxySQL multiplexing and `GET_LOCK()` behavior](https://proxysql.com/documentation/multiplexing/)
- [ProxySQL MySQL variables](https://proxysql.com/documentation/global-variables/mysql-variables/)
- [MariaDB MaxScale `readwritesplit` routing and options](https://mariadb.com/docs/maxscale/reference/maxscale-routers/maxscale-readwritesplit)
- [HAProxy active health checks](https://www.haproxy.com/documentation/haproxy-configuration-tutorials/reliability/health-checks/)
- [MySQL locking functions](https://dev.mysql.com/doc/refman/8.4/en/locking-functions.html)
- [MariaDB `GET_LOCK`](https://mariadb.com/docs/server/reference/sql-functions/secondary-functions/miscellaneous-functions/get_lock)
