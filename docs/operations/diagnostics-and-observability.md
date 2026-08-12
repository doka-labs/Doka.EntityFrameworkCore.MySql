# Diagnostics and Observability

This runbook defines the stable diagnostic identifiers, telemetry fields, and
operator responses for provider incidents. Alert definitions use the explicit
anchors in this document and the related migration and resilience runbooks.

## EventId Reference

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
| 1110 | `MigrationOperationHandlerSelected` | Information | Migrations | Exact-type dispatch selected one validated custom operation handler. |
| 1111 | `InvalidMigrationOperationHandlerRegistration` | Error | Migrations | The scoped handler registry contains an invalid or conflicting registration. |
| 1112 | `MigrationOperationHandlerFailed` | Error | Migrations | The selected custom operation handler threw while generating its staged result. |
| 1113 | `MigrationOperationHandlerContractViolation` | Error | Migrations | The selected handler violated its result, operation-ownership, or baseline-rendering contract. |
| 1114 | `UnknownMigrationOperation` | Error | Migrations | Neither the provider nor a registered exact-type handler owns the custom operation. |
| 1403 | `ForeignKeyPrincipalTableNotScaffolded` | Warning | Scaffolding | A foreign key is skipped during scaffolding because its principal table is excluded by the scaffolding filter. |
| 1500 | `RetryAttempt` | Warning | Resilience | A transient operation will be retried. |
| 1501 | `RetryLimitExceeded` | Error | Resilience | The configured retry budget for a transient failure is exhausted. |
| 1502 | `SoftCancellation` | Information | Resilience | The driver completes a command cancellation through the soft-cancel path. |
| 1503 | `HardCancellation` | Warning | Resilience | The driver had to fall back to the hard-cancel path to finish command cancellation. |
| 1504 | `CommandTimeoutExhausted` | Warning | Resilience | A relational command exhausted its configured timeout budget. |
| 1505 | `CommitUnknown` | Warning | Resilience | Commit threw; server outcome unproven. Follow the [commit-unknown response](resilience-and-topology.md#commit-unknown-response). |
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

## Observability and Alert Response

The machine-readable contract is
`docs/operations/observability-contract.json`. Dashboard and alert automation
must consume its stable event, span, metric, tag-domain, and runbook mappings.
The provider source is `Doka.EntityFrameworkCore.MySql`; EF Core diagnostic
events use `Microsoft.EntityFrameworkCore`; driver spans and metrics use
`MySqlConnector`. A single root activity must correlate all three layers.
Every provider metric carries the bounded `engine` tag (`mysql` or `mariadb`),
so dashboards and alerts can separate the two supported engine families.

Custom migration-operation generation emits the internal span
`db.migration.operation_handler.generate`, three counters, and one duration
histogram:

- `doka_mysql_migration_operation_handler_calls_total`;
- `doka_mysql_migration_operation_handler_failures_total`;
- `doka_mysql_migration_operation_handler_contract_violations_total`;
- `doka_mysql_migration_operation_handler_duration_seconds`.

Handler telemetry uses the bounded handler ID, exact CLR operation type,
generation mode, outcome, engine family, and error type. It never records SQL,
connection strings, database or object names, migration identifiers, plugin
exception messages, stack traces, or exception data. The immediate caller still
receives the original exception as `InnerException` and must apply its own
redaction policy before logging it.

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

<a id="mysql-migration-operation-handler-failure"></a>

### Migration-operation handler failure

Alert on any increase of
`doka_mysql_migration_operation_handler_failures_total` or
`doka_mysql_migration_operation_handler_contract_violations_total`. Custom DDL
generation is fail closed; retrying the same migration artifact cannot correct
an invalid registration, deterministic handler exception, unknown operation,
or invalid staged result. Follow the
[migration handler failure procedure](migrations.md#custom-migration-operation-handler-failure)
before generating or applying another artifact.
