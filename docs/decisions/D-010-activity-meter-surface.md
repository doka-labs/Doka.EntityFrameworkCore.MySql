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

`docs/host-integration-examples.md` promises consumers an
`ActivitySource` for distributed-trace correlation and a `Meter` for SLO
measurement. The provider currently emits neither:

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
    "Doka.EntityFrameworkCore.MySql",
    ProductVersion);

private static readonly Meter s_meter = new(
    "Doka.EntityFrameworkCore.MySql",
    ProductVersion);

private static readonly Histogram<double> s_migrationLockAcquireDuration =
    s_meter.CreateHistogram<double>(
        name: "migration_lock_acquire_duration_seconds",
        unit: "s",
        description: "Wall time spent waiting for the migration advisory lock.");
```

The instrumented operations (initial set):

| Operation | EventId | Span | Meter |
|---|---|---|---|
| Migration lock acquisition | `MigrationLockAcquired` | `db.migration.lock` | `migration_lock_acquire_duration_seconds` (Histogram) |
| Retry attempt | `RetryAttempted` | `db.retry.attempt` | `retry_attempts_total{outcome}` (Counter) |
| Soft / hard cancellation | `OperationCancelled` | included as event in active span | `cancellation_total{path}` (Counter) |
| Command timeout exceeded | `CommandTimedOut` | included as event in active span | `command_timeout_total` (Counter) |
| Commit-with-unknown-outcome | `CommitOutcomeUnknown` | included as event in active span | `commit_unknown_total` (Counter) |
| Server-version resolution | `ServerVersionResolved` | `db.serverversion.resolve` | (no histogram; one-shot) |

OpenTelemetry semantic-convention tags (`db.system`,
`server.address`, `db.namespace`) are attached to every span.

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
  is a non-zero cost; a benchmark sweep is required as part of the
  follow-up commit to confirm no regression on the no-listener path.
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

- Backbone-4 diagnostic-triple shipped in PR 4.2 (Phase 29). The provider exposes an `ActivitySource` and a `Meter` named `Doka.EntityFrameworkCore.MySql` (the canonical `MySqlDiagnostics.SourceName` constant); three spans (`db.migration.lock`, `db.retry.attempt`, `db.serverversion.resolve`) and five instruments (`doka_mysql_migration_lock_acquire_duration_seconds` histogram + `doka_mysql_retry_attempts_total` / `doka_mysql_cancellation_total` / `doka_mysql_command_timeout_total` / `doka_mysql_commit_unknown_total` counters) are wired across the migration-lock, execution-strategy, logging-execution-strategy, server-version-resolution, and commit paths. The hot-path `HasListeners()` guard inside `MySqlActivitySource.Start*` keeps the no-subscriber case allocation-free; the `Meter` counter and histogram writes are unconditional because `System.Diagnostics.Metrics.Meter` short-circuits internally on no-listener. In-process smoke coverage (`MySqlActivityAndMeterSmokeTests`) pins each span + counter / histogram via the dotnet `ActivityListener` and `MeterListener` surfaces, asserting both that emission happens when a listener is subscribed and that `Start*` returns `null` when no listener is attached.

### Implementation Notes

- `MySqlDiagnostics.SourceName` (`Doka.EntityFrameworkCore.MySql`) is the documented public surface consumers subscribe to via `builder.WithTracing(t => t.AddSource(MySqlDiagnostics.SourceName))` and `builder.WithMetrics(m => m.AddMeter(MySqlDiagnostics.SourceName))`. The span and instrument names are public constants on the same class so dashboard / alert configurations can reference them without string duplication.
- The migration-lock instrumentation records on both success and timeout paths via a `try`/`finally` around `Stopwatch.StartNew()`; the `outcome` tag (`acquired` / `timeout`) lets SLO dashboards compare the two without re-deriving the threshold from histogram buckets.
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

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Diagnostics/MySqlDiagnostics.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/MySqlActivityAndMeterSmokeTests.cs`

### Sources

- No external sources; repository evidence only.
