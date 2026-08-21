# Migration Operations

This runbook covers migration-lock recovery, clustered pre-flight checks, and
safe migration deployment modes. Use it whenever schema changes are applied to
a live MySQL or MariaDB estate. Package authors implementing custom operations
should begin with the
[migration-operation handler contract](../migration-operation-handlers.md).

<a id="mysql-migration-operation-handler-failure"></a>

## Custom Migration-Operation Handler Failure

Any increase in `doka_mysql_migration_operation_handler_failures_total` or
`doka_mysql_migration_operation_handler_contract_violations_total` is a
stop-the-line migration signal. Do not retry the same artifact blindly.

1. Identify the bounded `handler.id`, `operation.type`, `generation.mode`, and
   `error.type` tags. Provider telemetry deliberately excludes SQL, object
   names, connection strings, and plugin exception messages.
2. Reproduce generation offline with the same migration assembly, provider
   package, server-version descriptor, and generation options.
3. Correct the handler registration, exception, or invalid result. A handler
   must return at least one immutable command, a bounded outcome code, and the
   intended transaction-suppression boundary for every command.
4. Regenerate and review the complete migration script before deployment.

`UnknownMigrationOperation` (1114) means no built-in or registered exact-type
handler owns the custom operation. `InvalidMigrationOperationHandlerRegistration`
(1111) means the scoped service graph is invalid and must be corrected before
any SQL generation can proceed.

For invocation-time handler contract violations, inspect `error.type`:
`UnknownOperationType` requires passing a provider-owned standard operation to
the baseline renderer,
`RecursiveProviderRendering` requires removing recursive or concurrent baseline
rendering, and `InvalidHandlerResult` requires correcting the returned command
or outcome metadata.

`ContextExpired` is raised directly to code that invokes a retained context
after its handler has returned. Because the handler invocation and its span have
already completed, this post-lifetime misuse is not attributed to the earlier
invocation's log, metric, or activity.

### Scoped command recovery

Provider-rendered DDL comments containing a backslash require a temporary
`NO_BACKSLASH_ESCAPES` session mode. During `Database.Migrate`, direct
`IMigrator`, `dotnet ef`, and bundle runtime execution, Doka owns that scope in
one specialized migration command:

1. open and retain one physical connection when the caller did not already
   open it;
2. capture the exact original session mode client-side;
3. enable the required mode without removing existing modes;
4. execute the provider body;
5. restore the captured value even after server failure or cancellation; and
6. close only a connection opened by the scoped command.

Async cleanup does not reuse the canceled caller token. If restoration fails,
the provider forcibly closes the physical connection even when the caller
opened it, clears the MySqlConnector pool, and keeps the cleanup failure
visible. A connection-close failure is preserved through the same error path.
If both the body and cleanup fail, both exceptions are retained below one
terminal provider cleanup exception; investigate the body error first. EF's
execution strategy does not retry this ambiguous DDL outcome.

The same generated command exposes exact `Setup`, `Body`, and `Cleanup`
fragments to migration-operation handlers. These roles remove SQL-parser
coupling but do not turn a SQL script into a try/finally construct. If a normal
or idempotent script fails inside such a scope, terminate its client session
before retrying. Do not continue unrelated work on that session.

Handlers that acquire their own session state use
`MySqlMigrationCommandSpec.CreateScoped`. Runtime setup executes in declaration
order, the body executes once, and cleanup is attempted in reverse declaration
order after setup failure, body failure, success, or cancellation. Cleanup uses
an independent cancellation token. It must be idempotent and should use
server-side `IF EXISTS` forms where available. A cleanup failure closes the
physical connection and clears the affected pool, including when the caller
owned the open connection. The provider retains simultaneous body and cleanup
failures rather than hiding either one.

The repository deployment gate injects a deterministic failing scoped script
on every supported target. It requires the process-owned database client to
return failure and proves that a statement after that failure was not executed,
so the documented stop-and-discard rule is executable rather than advisory.

### Column default grammar

Migration default rendering resolves the effective relational type mapping even
when an operation omits `ColumnType`. DateOnly and TimeOnly typed literals are
expressions and are therefore emitted as parenthesized column defaults. The
same policy covers expression-default forms for text, binary, JSON, and every
spatial store type, including store types with modifiers.

TimeOnly and TimeSpan literals are truncated to the declared fractional-second
precision before SQL is generated. Precision is limited to the engine range of
zero through six; an explicit `time` store type therefore emits no fractional
digits, while the provider default `time(6)` emits microseconds. This avoids a
result that depends on an engine's rounding mode. CLR `char` literals use the
same SQL-mode-independent text encoding as provider string mappings, including
backslash and NUL values.

MariaDB JSON aliases and spatial column definitions retain literal or SQL
defaults, comments, visibility, computed expressions, storage, and SRID
metadata through their provider-specific rendering paths. The generator rejects
a backslash comment combined with raw SQL whose interpretation would change
under `NO_BACKSLASH_ESCAPES`; callers must rewrite that raw expression into a
mode-independent form before generating the migration.

Changing any nullable column to required is a data migration. The provider
repairs existing null rows only when the `AlterColumnOperation` declares an
explicit `DefaultValue` or nonblank `DefaultValueSql`; otherwise SQL generation
fails before an `UPDATE` or `ALTER TABLE` is emitted. This applies equally to
ordinary scalar columns and to JSON, spatial, `ENUM`, and constrained columns.
A CLR default cannot express arbitrary store constraints or application
meaning.

A nullable-to-required `timestamp` repair must use `DefaultValueSql`, not a CLR
`DateTime` value. Both engine families interpret `TIMESTAMP` input through the
executing session time zone before storing UTC, so validating or rendering an
unspecified CLR wall-clock value in the design-time generator would not define
a portable instant. Use an intentional server expression such as
`CURRENT_TIMESTAMP(6)`, or run a separate application-owned data migration
before changing nullability. The target server remains authoritative for the
expression, timestamp range, fractional precision, and every table constraint.

The executable migration example proves create, add-on-populated-table, alter,
existing-row preservation, and new-row defaults through every EF deployment
path on all six active targets. Script generation additionally rejects any
unparenthesized `DEFAULT DATE '...'` or `DEFAULT TIME '...'` form.

<a id="mysql-migration-lock-failure"></a>

## Migration Lock Stuck Procedure

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

<a id="galera-maxscale-migration-pre-flight"></a>

## Galera / MaxScale Migration Pre-Flight

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

Watch for `RetryAttempt` (EventId 1500) emissions during the run; on Galera,
occasional `LockWaitTimeout` retries are normal as flow control pauses commit
groups. Persistent `RetryLimitExceeded` (EventId 1501) or any
`LockReleaseFailed` (EventId 1102) is a stop-the-line signal -- consult the
[migration lock recovery procedure](#migration-lock-stuck-procedure) before
retrying.

## Migration Deployment Modes and Safety Gates

The provider supports every EF Core migration application path. The deployment
owner chooses the path based on operational control, credential boundaries, and
artifact policy; the provider must not silently redirect one path to a different
database.

### Repository gates

Run the model-drift gate whenever the model or migration assembly changes:

```bash
./eng/quality/check-migration-model.sh
```

The gate builds the executable migration example and runs
`dotnet ef migrations has-pending-model-changes`. A non-zero result means the
model snapshot and runtime model differ; release work stops until a deliberate
migration is added or the unintended model change is reverted.

Run the deployment lifecycle before a release candidate:

```bash
./eng/test-migration-deployment.sh
```

This gate uses isolated, dynamically published containers for all six active
LTS targets. For each target it:

1. applies and reapplies through `Database.MigrateAsync()`
2. applies and reapplies through `IMigrator.MigrateAsync()`
3. applies, reapplies, and rolls back through `dotnet ef database update`
4. generates and executes normal and idempotent SQL scripts
5. generates an EF Core migration bundle
6. applies, reapplies, and rolls every path back to migration `0`
7. independently reads back both the latest and absent application schema
8. reapplies the bundle and verifies the latest schema and seed data

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

Idempotent scripts frame their stored-procedure guards with the native MySQL
and MariaDB `DELIMITER` client command. Apply the complete generated file with
the `mysql` or `mariadb` client, or with deployment tooling that implements the
same delimiter semantics. Do not submit `DELIMITER` as server SQL through an
ADO.NET command.

### Primary sources

- Microsoft, [Applying Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying),
  retrieved 2026-07-28.
- Microsoft, [Managing Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing),
  retrieved 2026-07-28.
- Microsoft, [EF Core 10.0.8 `MigrationCommand` source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/MigrationCommand.cs),
  retrieved 2026-08-20.
- Microsoft, [EF Core 10.0.8 migration command executor source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/Internal/MigrationCommandExecutor.cs),
  retrieved 2026-08-20.
- Microsoft, [EF Core 10.0.10 migration command executor source](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Migrations/Internal/MigrationCommandExecutor.cs),
  retrieved 2026-08-21.
- Microsoft, [EF Core 10.0.11 SQL Server migrations SQL generator source](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.SqlServer/Migrations/SqlServerMigrationsSqlGenerator.cs),
  retrieved 2026-08-21.
- MySQL, [Data Type Default Values](https://dev.mysql.com/doc/refman/8.4/en/data-type-defaults.html),
  retrieved 2026-08-20.
- MySQL, [Fractional Seconds in Time Values](https://dev.mysql.com/doc/refman/8.4/en/fractional-seconds.html),
  retrieved 2026-08-20.
- MySQL, [Date and Time Data Type Syntax](https://dev.mysql.com/doc/refman/8.4/en/date-and-time-type-syntax.html),
  retrieved 2026-08-21.
- MySQL, [Geometry Well-Formedness and Validity](https://dev.mysql.com/doc/refman/8.4/en/geometry-well-formedness-validity.html),
  retrieved 2026-08-21.
- MySQL, [`CHECK` Constraints](https://dev.mysql.com/doc/refman/8.4/en/create-table-check-constraints.html),
  retrieved 2026-08-21.
- MySQL, [`CREATE TABLE` Statement](https://dev.mysql.com/doc/refman/8.4/en/create-table.html),
  retrieved 2026-08-20.
- MySQL, [`mysql` client options](https://dev.mysql.com/doc/refman/8.4/en/mysql-command-options.html),
  retrieved 2026-08-21.
- MySQL, [CREATE PROCEDURE and CREATE FUNCTION Statements](https://dev.mysql.com/doc/refman/8.4/en/create-procedure.html),
  retrieved 2026-08-11.
- MariaDB, [mariadb Command-Line Client](https://mariadb.com/docs/server/clients-and-utilities/mariadb-client/mariadb-command-line-client),
  retrieved 2026-08-11.
- MariaDB, [`CREATE TABLE`](https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-table),
  retrieved 2026-08-20.
- MariaDB, [`TIME`](https://mariadb.com/docs/server/reference/data-types/date-and-time-data-types/time),
  retrieved 2026-08-20.
- MariaDB, [`TIMESTAMP`](https://mariadb.com/docs/server/reference/data-types/date-and-time-data-types/timestamp),
  retrieved 2026-08-21.
- MariaDB, [Constraints](https://mariadb.com/docs/server/reference/sql-statements/data-definition/constraint),
  retrieved 2026-08-21.
- MySqlConnector, [connection options and pool reset behavior](https://mysqlconnector.net/connection-options/),
  retrieved 2026-08-21.
- MySqlConnector, [`MySqlConnection.ClearPoolAsync`](https://mysqlconnector.net/api/mysqlconnector/mysqlconnection/clearpoolasync/),
  retrieved 2026-08-21.
