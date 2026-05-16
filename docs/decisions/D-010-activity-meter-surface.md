# D-010 -- Activity/Meter-Surface (Diagnostic-Triple)

- **Status:** Accepted
- **Date:** 2026-05-16
- **Scope:** provider-wide diagnostic surface (`Internal/Diagnostics/`)
- **Implementation:** deferred to a follow-up commit

## Context

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

## Decision

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

## Consequences

### Positive

- Provider-side telemetry becomes correlatable with the consumer's
  trace context; root-causing a migration timeout no longer stops at
  the EF Core span boundary.
- Operational SLOs become measurable. The provider's documented
  promises ("99% migration locks under 5 seconds") gain a structural
  measurement path.
- The triple pattern is uniform across the surface; adding new
  instrumented operations follows one shape instead of three.
- The advertised observability matches the delivered observability.

### Negative

- Adds a structural dependency on
  `System.Diagnostics.DiagnosticSource` (already present transitively
  via EF Core, but now a direct dependency).
- Hot-path cost is gated by `HasListeners()` but the gate check itself
  is a non-zero cost; a benchmark sweep is required as part of the
  follow-up commit to confirm no regression on the no-listener path.
- The metric and span names become part of the operational contract;
  renaming them later is a breaking change for dashboards and alert
  rules.

### Neutral

- Logger events continue to fire alongside the new triple; consumers
  who only consume the logger stream see no behavior change.

## Re-evaluation triggers

- OpenTelemetry releases a new revision of the database semantic
  conventions that requires renaming or restructuring the existing
  tags; the contract would shift.
- A consumer-reported scenario where the gated `HasListeners()` check
  is measurably hot (for example, a tight inner loop that constructs
  many `DbContext` instances); the gate placement would move outward.
- A future EF Core change that introduces a first-party
  `ActivitySource`/`Meter` for provider hooks; the provider's surface
  would align with the upstream choice.

## Alternatives considered

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
