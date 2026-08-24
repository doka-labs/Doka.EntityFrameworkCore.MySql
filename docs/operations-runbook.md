# Operations Runbook

This stable entry point routes operators to the procedure that owns each
operational responsibility. Keeping the procedures separate makes incident
response, migration safety, and release governance independently reviewable.

## Runbooks

- [Diagnostics and Observability](operations/diagnostics-and-observability.md)
  defines diagnostic identifiers, telemetry fields, alert anchors, and the
  first-response sequence.
- [Migration Operations](operations/migrations.md) covers migration-lock
  recovery, clustered pre-flight checks, and deployment modes.
- [Migration Operation Handlers](migration-operation-handlers.md) defines the
  custom-operation package contract and its fail-closed ownership boundary.
- [Resilience and Topology Operations](operations/resilience-and-topology.md)
  covers retries, commit-unknown handling, poolers, proxies, and load
  balancers.
- [Release Publication Operations](operations/release-publication.md) defines
  release-candidate qualification, trusted NuGet publication, readback, and
  evidence preservation.
- [Performance Evidence](operations/performance-evidence.md) owns benchmark
  execution and failure triage.
- [Performance Evidence Reference](operations/performance-evidence-reference.md)
  defines profiles, schemas, termination states, and soak invariants.
- [Paired Performance Methodology](operations/paired-performance-methodology.md)
  records the retired paired design and the current contract boundary.
- [Performance Baseline Operations](operations/performance-baseline-operations.md)
  owns engine-image acceptance and reviewed budget changes.

## Stable Alert Links

The following compatibility anchors preserve links published before the
runbook was separated by responsibility.

<a id="mysql-migration-lock-failure"></a>

- [Migration lock failure](operations/migrations.md#mysql-migration-lock-failure)

<a id="mysql-migration-operation-handler-failure"></a>

- [Migration-operation handler failure](operations/migrations.md#custom-migration-operation-handler-failure)

<a id="mysql-commit-unknown"></a>

- [Commit outcome unknown](operations/resilience-and-topology.md#mysql-commit-unknown)

<a id="mysql-retry-exhausted"></a>

- [Retry budget exhausted](operations/diagnostics-and-observability.md#mysql-retry-exhausted)

<a id="mysql-hard-cancellation"></a>

- [Hard cancellation](operations/diagnostics-and-observability.md#mysql-hard-cancellation)

<a id="mysql-command-timeout"></a>

- [Command timeout](operations/diagnostics-and-observability.md#mysql-command-timeout)
