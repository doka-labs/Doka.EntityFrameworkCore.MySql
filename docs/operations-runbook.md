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
| 1005 | `UnsupportedServerVersion` | Warning | Configuration | An explicit opt-in uses an unsupported release line. |
| 1100 | `MigrationLockAcquired` | Information | Migrations | The database-scoped advisory lock was acquired. |
| 1101 | `MigrationLockTimeout` | Warning | Migrations | Advisory-lock acquisition exhausted its timeout budget. |
| 1102 | `LockReleaseFailed` | Warning | Migrations | The migration advisory lock could not be released cleanly via `RELEASE_LOCK`. Disposing the dedicated connection still releases the session-scoped lock implicitly; the warning surfaces an unusual server-side state worth investigating. |
| 1103 | `MigrationLockAcquireFailed` | Error | Migrations | Non-timeout acquisition failure. |
| 1403 | `ForeignKeyPrincipalTableNotScaffolded` | Warning | Scaffolding | A foreign key is skipped during scaffolding because its principal table is excluded by the scaffolding filter. |
| 1500 | `RetryAttempt` | Warning | Resilience | A transient operation will be retried. |
| 1501 | `RetryLimitExceeded` | Error | Resilience | The configured retry budget for a transient failure is exhausted. |
| 1502 | `SoftCancellation` | Information | Resilience | The driver completes a command cancellation through the soft-cancel path. |
| 1503 | `HardCancellation` | Warning | Resilience | The driver had to fall back to the hard-cancel path to finish command cancellation. |
| 1504 | `CommandTimeoutExhausted` | Warning | Resilience | A relational command exhausted its configured timeout budget. |
| 1505 | `CommitUnknown` | Warning | Resilience | Commit threw; server outcome unproven (see section 4). |
| 1600 | `MissingSpatialPackageDuringScaffolding` | Warning | Spatial | Spatial reverse engineering detects spatial artifacts but the optional NetTopologySuite package is not installed. |
| 1601 | `InvalidSpatialIndexConfiguration` | Error | Spatial | Spatial index configuration violates the supported provider contract. |
| 1602 | `MissingSpatialTranslation` | Warning | Spatial | A spatial member or method is detected but no supported server translation exists. |
| 1603 | `SpatialSridMismatchDetected` | Warning | Spatial | The translator observed two `ST_Distance` arguments with different SRIDs. MySQL rejects the mismatch with a hard error; MariaDB silently treats both inputs as Cartesian and returns a meaningless result. |
| 1700 | `BulkInsertParameterCountCapped` | Warning | Update | A `SaveChanges` batch would exceed MySQL's 65535-placeholder hard limit; the batch is split at the command that would have crossed the cap. |
| 1701 | `BulkInsertPacketSizeCapped` | Warning | Update | A `SaveChanges` batch would exceed the conservative `max_allowed_packet` budget; the batch is split at the command that would have crossed the cap. |

Provider runtime emissions use the stable `MySqlLoggerCategory.*` taxonomy
(Configuration, Query, Update, Migrations, Scaffolding, Resilience, Spatial).
Events raised during EF Core model validation intentionally use
`Microsoft.EntityFrameworkCore.Model.Validation`, so application warning
configuration and category filters remain effective. The stable `EventId`
continues to identify the provider subsystem independently of category.

Events `1003`, `1004`, `1403`, `1600`, and `1601` correlate affected model or
database objects through a stable 16-character `ObjectScopeId`. They never emit
the raw entity, property, constraint, table, column, or index name. Event `1001`
uses the bounded `Reason` vocabulary from
`MySqlConfigurationFailureReason` and a bounded `ConnectionPath`; it does not
emit caller-provided messages or any connection-string representation. The
exception thrown to the calling application retains the detailed validation
message needed to correct the configuration.

<a id="mysql-migration-lock-failure"></a>

## 2. Migration Lock Stuck Procedure

The provider serializes migrations through a database-scoped MySQL advisory lock named `__ef_migrations_lock:<database>` (truncated to a SHA-256 suffix when the combined length exceeds MySQL's 64-character `GET_LOCK` limit). The lock is held on a dedicated connection for the full duration of the migration and released through `RELEASE_LOCK` plus dedicated-connection dispose; the timeout for `GET_LOCK` is 60 seconds.

Stuck conditions and their recovery:

### Symptom: migration startup times out with `TimeoutException`

```
Could not acquire the MySQL advisory lock scope '<opaque-lock-scope-id>'
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

<a id="mysql-commit-unknown"></a>

## 4. Commit-Unknown Response

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

## 7. Migration Deployment Modes and Safety Gates

The provider supports every EF Core migration application path. The deployment
owner chooses the path based on operational control, credential boundaries, and
artifact policy; the provider must not silently redirect one path to a different
database.

### Repository gates

Run the model-drift gate whenever the model or migration assembly changes:

```bash
./eng/check-migration-model.sh
```

The gate builds the executable migration example and runs
`dotnet ef migrations has-pending-model-changes`. A non-zero result means the
model snapshot and runtime model differ; release work stops until a deliberate
migration is added or the unintended model change is reverted.

Run the deployment lifecycle before a release candidate:

```bash
./eng/test-migration-deployment.sh
```

This gate uses isolated, dynamically published MySQL 8.4, MariaDB 11.4, and
MariaDB 11.8 containers. For each engine it:

1. generates an EF Core migration bundle
2. applies the latest migration
3. reapplies it to prove idempotence
4. rolls back to migration `0`
5. verifies that the application schema is absent
6. reapplies and reads back the latest schema and seed data

Evidence is retained under
`artifacts/migration-deployment/<run-id>/migration-deployment-evidence.json`.
The regular integration suite separately kills a real migrator process while
the provider owns the advisory lock, then proves lock release and recovery.

### Runtime `MigrateAsync`

`Database.MigrateAsync()` is supported and uses the provider's database-scoped
advisory lock. It is appropriate for controlled, single-purpose migrator jobs
whose identity owns schema-change permissions. Do not grant schema-change
permissions to every application replica merely to migrate during startup.
Application startup also provides a weaker review and rollback boundary than a
versioned deployment artifact.

### Migration bundle

The preferred automated production path is a bundle generated from the exact
release source and dependency graph:

```bash
dotnet tool restore
dotnet tool run dotnet-ef -- migrations bundle \
    --project examples/MigrationsWorkflow/MigrationsWorkflow.csproj \
    --startup-project examples/MigrationsWorkflow/MigrationsWorkflow.csproj \
    --context Doka.EntityFrameworkCore.MySql.Examples.MigrationsWorkflow.MigrationWorkflowContext \
    --configuration Release \
    --output artifacts/migration-deployment/efbundle

./artifacts/migration-deployment/efbundle \
    --connection "<deployment-secret>"
```

The bundle honors `--connection` for database creation, migration history,
advisory-lock naming, and DDL execution. Keep the connection string in the
deployment platform's secret mechanism; do not persist it in logs or evidence.
Retain the bundle, its checksum, the release identifier, and the gate evidence
as one deployment record.

To roll back deliberately, pass the exact previous migration identifier.
Migration `0` removes every migration and is reserved for isolated rehearsal or
full decommissioning:

```bash
./artifacts/migration-deployment/efbundle 0 \
    --connection "<isolated-rehearsal-secret>"
```

Take and verify a restorable backup before production rollback. MySQL and
MariaDB DDL behavior can make a multi-command rollback only partially
transactional; a bundle exit code alone is not proof that application data is
recoverable.

### `dotnet ef database update`

The CLI uses the same EF migrator and provider lock as runtime migration and
bundles. It is suitable for an operator workstation or build agent that has the
correct SDK, tool manifest, source tree, and credentials. It is less portable
than a bundle because those inputs must be reconstructed at execution time.
Always pass the intended context and release configuration explicitly.

### SQL scripts

Generated SQL scripts are reviewable and work with database-native deployment
systems. They do not execute through EF Core's runtime migrator, so the
provider's advisory lock does not protect script execution. The deployment
orchestrator must enforce a single writer and retain script output, target
identity, execution result, and post-deployment readback. An idempotent script
reduces repeat-application risk; it does not make concurrent script runners
safe.

### Primary sources

- Microsoft, [Applying Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying),
  retrieved 2026-07-28.
- Microsoft, [Managing Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing),
  retrieved 2026-07-28.

## 8. NuGet and GitHub Release-Candidate Publication

Package publication is intentionally separate from qualification. Never upload
one of the locally generated packages manually: only the hosted candidate has
the workflow identity and attestations accepted by the publication boundary.

### One-time configuration

The repository environment is named `nuget`. It contains only the environment
secret `NUGET_USER`, whose value is the personal NuGet.org username that belongs
to the `doka-labs` organization. It is restricted to the `main` branch because
the publication workflow executes reviewed tooling from trusted `main`; the
selected candidate separately proves that its release tag identifies that same
commit.

Create one NuGet.org Trusted Publishing policy after
`.github/workflows/nuget-publish.yml` is present on `main`:

| Field | Value |
|---|---|
| Policy owner | `doka-labs` |
| Repository owner | `doka-labs` |
| Repository | `Doka.EntityFrameworkCore.MySql` |
| Workflow file | `nuget-publish.yml` |
| Environment | `nuget` |

Do not create or store a long-lived NuGet API key. `NuGet/login` exchanges the
workflow's GitHub OIDC token for a one-hour key only after candidate, manifest,
package, and remote-state verification have passed.

GitHub artifact attestations and environment reviewers require a public
repository on GitHub Free, Pro, or Team. A private repository needs GitHub
Enterprise Cloud for attestations; its plan must also expose the selected
environment protections. Before the first candidate, confirm that the hosted
`release-candidate` run can create and verify attestations. When required
reviewers are available, add the maintainer who authorizes publication and
disable administrator bypass. Do not disable self-review when that maintainer
is the repository's only release operator.

### Qualification and publication procedure

This is the canonical operator sequence for every prerelease and stable
publication. In the examples below, `release_version` has no leading `v`, while
`release_tag` is the corresponding Git tag. Always select the next unused
semantic version; do not copy the example version without checking the remote
repository and NuGet.org.

#### 1. Establish the release source

1. Complete the version, dated `CHANGELOG.md` section, public API, package
   metadata, and release-note changes before selecting the reviewed release
   commit.
2. Merge the release commit into protected `main`. Independent maintainer
   approval is the normal path. A documented bootstrap or emergency bypass is
   an exceptional recovery mechanism, not a routine substitute for review.
3. Update the local `main`, confirm that the worktree is clean, and record the
   exact source commit:

   ```bash
   git fetch origin main --tags
   git switch main
   git merge --ff-only origin/main
   git status --short

   release_commit="$(git rev-parse HEAD)"
   test "${release_commit}" = "$(git rev-parse origin/main)"
   ```

   `git status --short` must produce no output, and the final comparison must
   exit successfully.

#### 2. Qualify and freeze `main`

Wait for the following checks on `release_commit` to complete successfully:

- `quality-gates`
- `repo-tests`
- `integration-smoke`
- CodeQL and every other code-scanning check required by the active `main`
  ruleset

Resolve every release blocker before continuing. Once the exact commit is
green, freeze `main` operationally until publication completes. Any later
commit makes the candidate stale, even if that commit changes only
documentation or automation.

#### 3. Create the release tag

Create one signed, annotated tag at `release_commit`. The package version,
dated changelog heading, tag, and tag message must identify the same version.
For example, after replacing the version with the next unused value:

```bash
release_version="10.0.0-rc.2"
release_tag="v${release_version}"

git tag -s "${release_tag}" "${release_commit}" \
  -m "Doka.EntityFrameworkCore.MySql ${release_version}"
git tag -v "${release_tag}"
test "$(git rev-list -n 1 "${release_tag}")" = "${release_commit}"
git push origin "refs/tags/${release_tag}"
```

Verify the signature and target before pushing. Push only the intended tag;
never use `git push --tags` for a release. A tag is immutable release identity:
never move, replace, or reuse it after it reaches the remote repository.

#### 4. Produce the hosted candidate

1. Open GitHub Actions and select the `release-candidate` workflow.
2. Choose `Run workflow`, then select the exact value of `release_tag` in the
   branch/tag field.
3. Wait for the complete workflow to succeed. A failed candidate has no
   publication authority.
4. Inspect the workflow summary and retained evidence. Record the numeric run
   ID from the successful run URL:

   ```text
   https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/runs/<candidate-run-id>
   ```

The hosted workflow reruns the complete release-candidate contract, binds the
result to the tagged source commit, attests the packages and canonical
manifest, verifies the attestation, and uploads the immutable candidate
artifact.

#### 5. Publish from trusted `main`

Keep `main` frozen. In GitHub Actions, manually run `nuget-publish` from
`main` with these exact inputs:

- `candidate_run_id`: the numeric ID of the successful candidate run
- `release_tag`: the exact `release_tag` selected for that run
- `confirmation`: `publish <release-tag>`

The workflow must run from `main`; selecting the release tag for this second
workflow is invalid. Approve the `nuget` environment deployment only after the
displayed candidate run ID and tag match the reviewed release.

#### 6. Verify public readback and finalize the release

Wait for both publication jobs to succeed. `publish-and-read-back` publishes
the provider and spatial packages, validates their symbols, restores them into
an empty isolated consumer, and executes the runtime contract. Only then may
`finalize-github-release` create or resume the matching draft, verify every
asset by readback, and publish the immutable GitHub release.

Before unfreezing `main`, confirm all of the following:

- Both primary packages and both symbol packages passed public readback.
- The isolated basic and spatial consumer contracts passed.
- The GitHub release points to `release_tag` and contains the expected assets.
- A prerelease is marked as a prerelease and is not `latest`; a stable release
  is not marked as a prerelease and is `latest`.
- `nuget-publication-evidence-<release-tag>` and
  `github-release-evidence-<release-tag>` are retained. The latter contains the
  deterministic release plan and verified public release receipt.

#### 7. Recover without changing release identity

- If the candidate fails because of transient hosted infrastructure and no
  candidate input must change, rerun `release-candidate` on the same tag and
  use only the new successful run ID.
- If any source, package, documentation, configuration, dependency, or release
  automation change is required, prepare a new release commit and version,
  repeat the green-`main` gate, and create a new signed tag. Do not repair the
  old candidate by moving its tag.
- If `main` advances before publication, discard the stale candidate and
  produce a new version and candidate from the new green `main` commit.
- If NuGet or GitHub finalization fails after a partial public write, preserve
  the workflow evidence and follow the conflict-safe retry procedures below.
  Do not publish local packages or alter remote assets to make the retry pass.

The workflow rejects a candidate from another repository, commit, tag,
workflow, attempt, or failed run. It also rejects a candidate once `main` has
advanced. Produce a new candidate version instead of publishing stale evidence.

### GitHub release finalization and recovery

The finalization job has the workflow's only `contents: write` permission. It
does not receive the NuGet OIDC permission. Before any release mutation, it
requires the local and remote tag to be annotated and to resolve to the exact
published source commit. It never creates, moves, or replaces a tag.

Release notes are the exact dated version section from `CHANGELOG.md`. Release
assets are the checksum-bound packages and symbols, candidate manifest and
checksum, candidate summary and reconciliation, resolved package inventory,
all SBOMs, and the five successful NuGet publication receipts. A prerelease
version is published as a GitHub prerelease and never becomes `latest`; a
stable version is not a prerelease and must become `latest`.

Retries are conflict safe. An absent release becomes a draft. A matching
partial draft receives only missing assets. An already published release is an
idempotent success only when its metadata, notes, asset names, sizes, payload
hashes, immutability state, and latest-release classification all match. Any
unexpected asset, changed payload, changed notes, moved or lightweight tag, or
other metadata conflict stops the job. The helper neither deletes assets nor
uses a clobber operation.

If finalization stops on a conflict, preserve the draft and both evidence
artifacts. Diagnose the conflicting remote state before making a manual
change. Rerun the same dispatch only after the remote draft matches the sealed
candidate; otherwise create a new release-candidate version.

### NuGet retry and partial-publication recovery

NuGet package versions are immutable and the two package pushes are not an
atomic transaction. If a network or symbol-server error interrupts the run,
dispatch the same publication request again. The preflight downloads any
existing primary package and compares a canonical content digest with the
candidate. A matching provider package allows the spatial step to resume; any
same-version payload conflict stops before a new key is requested.

Only symbol uploads use `--skip-duplicate`. The NuGet symbol endpoint documents
HTTP 409 while the same ID and version are still pending, and permits another
submission after publication. Treating that pending response as idempotent does
not weaken the immutable primary-package comparison.

Symbol validation and indexing are asynchronous. NuGet documents completion as
normally taking less than 15 minutes and directs publishers to investigate a
symbol package still pending after one hour. The workflow therefore polls for
at most one hour. It derives each public symbol URL and SHA-256 header from the
candidate DLL, then requires the downloaded Portable PDB to match the checksum
sealed into that assembly. A primary package becoming visible is not sufficient
publication evidence when its symbols remain unavailable.

Never use `--skip-duplicate` to bypass a primary-package conflict. If NuGet.org
contains different bytes, preserve the failed workflow evidence, stop the
release, and select a new prerelease version after root-cause review.

### Primary sources

- NuGet, [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),
  retrieved 2026-08-03.
- NuGet, [Publish packages](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package),
  retrieved 2026-08-03.
- NuGet,
  [Symbol packages](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg),
  retrieved 2026-08-03.
- NuGet,
  [Symbol package publish resource](https://learn.microsoft.com/en-us/nuget/api/symbol-package-publish-resource),
  retrieved 2026-08-03.
- .NET,
  [SSQP key conventions](https://github.com/dotnet/symstore/blob/main/docs/specs/SSQP_Key_Conventions.md),
  retrieved 2026-08-03.
- GitHub,
  [Artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations),
  retrieved 2026-08-03.
- GitHub,
  [Deployments and environments](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments),
  retrieved 2026-08-03.
- GitHub,
  [Immutable releases](https://docs.github.com/en/repositories/releasing-projects-on-github/immutable-releases),
  retrieved 2026-08-04.
- GitHub CLI,
  [`gh release create`](https://cli.github.com/manual/gh_release_create),
  retrieved 2026-08-04.

## 9. Observability and Alert Response

The machine-readable contract is
`docs/operations/observability-contract.json`. Dashboard and alert automation
must consume its stable event, span, metric, tag-domain, and runbook mappings.
The provider source is `Doka.EntityFrameworkCore.MySql`; EF Core diagnostic
events use `Microsoft.EntityFrameworkCore`; driver spans and metrics use
`MySqlConnector`. A single root activity must correlate all three layers.
Every provider metric carries the bounded `engine` tag (`mysql` or `mariadb`),
so dashboards and alerts can separate the two supported engine families.

Provider telemetry deliberately excludes SQL, connection strings, raw database
names, usernames, exception messages, and exception stack traces. Failure logs
carry the exception type only. Provider-created connection strings receive the
bounded driver pool name `Doka.EntityFrameworkCore.MySql` when the application
does not explicitly configure `ApplicationName`. An explicit application name
is an operator-owned cardinality decision and should come from a small service
name vocabulary, never from request, tenant, or user data.

<a id="mysql-retry-exhausted"></a>

### Retry budget exhausted

Alert on any five-minute increase of
`doka_mysql_retry_exhausted_total`. Correlate the provider
`db.retry.exhausted` span with preceding `db.retry.attempt` spans and the
driver command span. Stop automatic retries when the error remains persistent;
inspect database health, pool saturation, and network reachability first.

<a id="mysql-hard-cancellation"></a>

### Hard cancellation rate elevated

Alert when `doka_mysql_cancellation_total{path=hard}` exceeds the established
service baseline for fifteen minutes. Hard cancellation means cooperative
command cancellation did not complete before the driver closed the connection.
Inspect long-running queries, server load, and network latency, then verify that
the pool replaces the broken physical connection.

<a id="mysql-command-timeout"></a>

### Command timeout rate elevated

Alert when `doka_mysql_command_timeout_total` consumes the service's timeout
SLO budget. Correlate `db.operation.timeout` with the MySqlConnector command
span and EF Core command event. Do not blindly raise the timeout: determine
whether the cause is query-plan regression, blocking, capacity, or transport
latency and correct that cause first.
