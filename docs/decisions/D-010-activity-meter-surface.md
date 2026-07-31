---
id: D-010
status: implemented
date: 2026-05-16
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Provider logging, tracing, and metrics surface"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-010 -- Emit a diagnostic triple for significant operations

## Context and Problem Statement

When this decision was recorded, `docs/host-integration-examples.md` promised
consumers an `ActivitySource` for distributed-trace correlation and a `Meter`
for SLO measurement, but the provider emitted neither:

- No provider-owned `ActivitySource` exists. Spans for migration-lock
  acquisition, retry attempts, soft/hard cancellation, and server-
  version resolution are not produced. Distributed-trace consumers see
  EF Core's spans but no provider-side context.
- No provider-owned `Meter` exists. Operational SLOs the provider
  promises (for example, "99% of migration locks acquired in under
  5 seconds", "retry rate stays under 0.1%") are not measurable from
  the standard OpenTelemetry collector.
- Logger events exist via `MySqlEventId` but cannot be correlated with
  trace context; the structured-logging stream lacks span/trace IDs.

The operability review classed this as an MAJOR gap: the provider
advertises observability capability it does not deliver. The premortem
amplified the risk: any production incident that needs to root-cause a
migration timeout or a retry-storm has no provider-side telemetry to
correlate against the rest of the application's traces.

## Decision Drivers

- Enterprise operations need correlated logs, traces, and metrics.
- No-listener hot paths must avoid avoidable allocations.
- Public telemetry names need one stable source of truth.
- Metric labels must have finite, documented value domains.
- Provider telemetry must not expose SQL, credentials, raw database names,
  connection strings, or exception messages.
- EF Core, provider, and driver signals need one trace context.

## Considered Options

- EventId, Activity, and Meter triple
- Structured logging only
- Activity tracing only

## Decision Outcome

Chosen option: "EventId, Activity, and Meter triple", because the three observability signals serve different operational consumers and must agree.

Establish a structured "diagnostic triple" pattern: every significant
provider operation emits a coordinated EventId + ActivitySource span +
Meter measurement.

```csharp
private static readonly ActivitySource s_activitySource = new(
    MySqlDiagnostics.SourceName,
    ProductVersion);

private static readonly Meter s_meter = new(
    MySqlDiagnostics.SourceName,
    ProductVersion);

private static readonly Histogram<double> s_migrationLockAcquireDuration =
    s_meter.CreateHistogram<double>(
        name: MySqlDiagnostics.MigrationLockAcquireDurationMetricName,
        unit: "s",
        description: "Wall time spent waiting for the migration advisory lock.");
```

The instrumented operations are:

- Server-version resolution:
  `ServerVersionResolved` / `UnsupportedServerVersion`,
  `db.serverversion.resolve`, and
  `doka_mysql_server_version_resolution_total{engine,support_status,compatibility_mode}`.
- Migration-lock acquisition:
  `MigrationLockAcquired` / `MigrationLockTimeout` /
  `MigrationLockAcquireFailed`, `db.migration.lock`, and
  `doka_mysql_migration_lock_acquire_duration_seconds{engine,outcome}`.
- Migration-lock release failure: `LockReleaseFailed`,
  `db.migration.lock.release_failed`, and
  `doka_mysql_migration_lock_release_failed_total{engine}`.
- Retry attempt: `RetryAttempt`, `db.retry.attempt`, and
  `doka_mysql_retry_attempts_total{engine,outcome}`.
- Retry exhaustion: `RetryLimitExceeded`, `db.retry.exhausted`, and
  `doka_mysql_retry_exhausted_total{engine}`.
- Soft or hard cancellation: `SoftCancellation` / `HardCancellation`,
  `db.operation.cancel`, and `doka_mysql_cancellation_total{engine,path}`.
- Command timeout: `CommandTimeoutExhausted`, `db.operation.timeout`, and
  `doka_mysql_command_timeout_total{engine}`.
- Commit with unknown outcome: `CommitUnknown`,
  `db.transaction.commit_unknown`, and
  `doka_mysql_commit_unknown_total{engine}`.

Every provider span carries the stable OpenTelemetry attributes
`db.system.name` and `db.operation.name`. `db.system.name` is selected from the
resolved engine profile and is therefore `mysql` for MySQL and `mariadb` for
MariaDB. Failure spans additionally carry `error.type`. The provider does not
add SQL, server, port, namespace, user, connection-string, raw lock-name, or
exception-message data.

Every provider metric carries the bounded `engine` tag (`mysql` / `mariadb`).
Every metric tag has a finite value domain recorded in
`docs/operations/observability-contract.json`. Failure logs record the
exception type as structured data but do not attach the exception object.

The hot-path cost is gated by `ActivitySource.HasListeners()` so the
no-listener case stays effectively zero-cost.

### Consequences

- Good, because operators can correlate failures, latency, and rates through stable names.
- Bad, because instrument names and tag cardinality become long-lived compatibility surfaces.

#### Positive

- Provider-side telemetry becomes correlatable with the consumer's
  trace context; root-causing a migration timeout no longer stops at
  the EF Core span boundary.
- Operational SLOs become measurable. The provider's documented
  promises ("99% migration locks under 5 seconds") gain a structural
  measurement path.
- The triple pattern is uniform across the surface; adding new
  instrumented operations follows one shape instead of three.
- The advertised observability matches the delivered observability.

#### Negative

- Adds a structural dependency on
  `System.Diagnostics.DiagnosticSource` (already present transitively
  via EF Core, but now a direct dependency).
- Hot-path cost is gated by `HasListeners()` but the gate check itself
  is a non-zero cost; the release gate requires a benchmark sweep that
  confirms no regression on the no-listener path.
- The metric and span names become part of the operational contract;
  renaming them later is a breaking change for dashboards and alert
  rules.

#### Neutral

- Logger events continue to fire alongside the new triple; consumers
  who only consume the logger stream see no behavior change.

### Confirmation

- Run `MySqlActivityAndMeterSmokeTests`.
- Run no-listener benchmark checks after telemetry hot-path changes.

## Pros and Cons of the Options

### EventId, Activity, and Meter triple

- Good, because each significant operation supports logs, traces, and aggregate metrics.
- Bad, because every new operation carries a larger instrumentation maintenance contract.

### Structured logging only

- Good, because the implementation surface stays small.
- Bad, because latency traces and aggregate counters require external log reconstruction.

### Activity tracing only

- Good, because distributed traces capture operation timing and relationships.
- Bad, because operators lose low-cost counters and conventional log diagnostics.

## More Information

### Implementation Snapshot

- `MySqlDiagnostics` exposes the stable provider, MySqlConnector, and EF Core
  source names plus every provider span and metric name.
- `MySqlActivitySource` uses `HasListeners()` before activity construction.
  `MySqlMeter` writes unconditionally because `Meter` short-circuits when no
  listener consumes an instrument.
- `MySqlDiagnosticTags` owns the provider vocabulary and bounded tag values.
- Provider-created connections receive a bounded default `ApplicationName` so
  MySqlConnector pool metrics do not derive their default pool name from the
  connection string.
- `MySqlObservabilityContractTests` reconcile public constants, operational
  EventIds, signal ownership, privacy, metric domains, alerts, and runbook
  anchors against the machine-readable contract.
- `MySqlCrossLayerObservabilityTests` execute live queries against MySQL and
  MariaDB and prove correlated EF Core, provider, and MySqlConnector signals.
- `MySqlNetworkFaultContractTests` prove the commit-unknown signal and
  reconciliation path at real TCP request/response boundaries.

### Implementation Notes

- `MySqlDiagnostics.SourceName` (`Doka.EntityFrameworkCore.MySql`) is the documented public surface consumers subscribe to via `builder.WithTracing(t => t.AddSource(MySqlDiagnostics.SourceName))` and `builder.WithMetrics(m => m.AddMeter(MySqlDiagnostics.SourceName))`. The span and instrument names are public constants on the same class so dashboard / alert configurations can reference them without string duplication.
- Migration-lock instrumentation records `acquired`, `timeout`, and `failed`
  outcomes through one completion path. Logs use an opaque hashed lock-scope
  identifier instead of the database-derived lock name.
- The cancellation counter carries a `path` tag (`soft` / `hard`) that mirrors the existing logger event split between `SoftCancellation` and `HardCancellation`.
- Per-operation activities use `ActivityKind.Client` (migration lock) or `ActivityKind.Internal` (retry, server-version-resolve); the provider is the originator of the span, not a network bridge.

### Additional Alternative Rationale

- **EventIds only, no `ActivitySource`/`Meter`.** Rejected: no span
  context for distributed traces; no histogram for SLO measurement.
- **Graft onto EF Core's `DiagnosticSource`.** Rejected: EF Core's
  `DiagnosticListener` is legacy compared to the OTel-aligned
  `ActivitySource`/`Meter` pattern. Consumers building on OTel today
  expect the modern shape.
- **Single `ActivitySource`, no per-operation spans (use events on a
  root span).** Rejected: distributed-trace UIs collapse event-only
  spans poorly; the per-operation span shape produces a usable trace
  view in standard tooling without extra configuration.

### Re-evaluation Triggers

- OpenTelemetry releases a new revision of the database semantic
  conventions that requires renaming or restructuring the existing
  tags; the contract would shift.
- A consumer-reported scenario where the gated `HasListeners()` check
  is measurably hot (for example, a tight inner loop that constructs
  many `DbContext` instances); the gate placement would move outward.
- A future EF Core change that introduces a first-party
  `ActivitySource`/`Meter` for provider hooks; the provider's surface
  would align with the upstream choice.
- OpenTelemetry database semantic conventions define a better stable provider mapping.
- A signal cannot remain allocation-safe or useful under production cardinality.

### Decision History

- 2026-05-16: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.
- 2026-07-31: Completed the diagnostic triple, privacy/cardinality contract,
  cross-layer correlation evidence, alert ownership, and real TCP fault proof.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/MySqlDiagnostics.cs`
- `docs/operations/observability-contract.json`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Diagnostics/MySqlActivityAndMeterSmokeTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Diagnostics/MySqlObservabilityContractTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Infrastructure/MySqlCrossLayerObservabilityTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Infrastructure/MySqlNetworkFaultContractTests.cs`

### Sources

- [OpenTelemetry database client span conventions][otel-database-spans]
  (primary source; retrieved 2026-07-31)
- [MySqlConnector tracing][mysqlconnector-tracing]
  (primary source; retrieved 2026-07-31)
- [MySqlConnector metrics][mysqlconnector-metrics]
  (primary source; retrieved 2026-07-31)
- [EF Core diagnostic listeners][efcore-diagnostic-listeners]
  (primary source; retrieved 2026-07-31)

[otel-database-spans]:
  https://opentelemetry.io/docs/specs/semconv/db/database-spans/
[mysqlconnector-tracing]: https://mysqlconnector.net/diagnostics/tracing/
[mysqlconnector-metrics]: https://mysqlconnector.net/diagnostics/metrics/
[efcore-diagnostic-listeners]:
  https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/diagnostic-listeners
