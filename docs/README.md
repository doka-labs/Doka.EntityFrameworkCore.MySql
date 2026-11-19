# Documentation

This index is the canonical entry point for provider documentation. Choose a
path by the task you need to complete. Architecture records explain why a
decision exists, while feature guides and runbooks explain supported behavior
and operational procedures.

For the 10.7.0 stable preparation, see the [changelog](../CHANGELOG.md#1070---2026-11-18)
and [shared qualification overview](operations/workflow-overview.md). This release
adds application text defaults, locking reads, scoped ordinary/recursive CTEs,
window calculations, typed INSERT SELECT and native UPSERT on .NET 10 / EF Core 10.
Each feature guide defines its opt-in behavior, examples and failure boundaries.

The EF Core 10.0.8 minimum, supported engines, existing GUID/collection defaults,
cache schema and MySqlConnector range remain unchanged. The APIs are additive;
existing applications retain their behavior without opting in. Text defaults
participate in the normal model, migration and snapshot workflow. Direct writes
have explicit transaction, affected-count and change-tracker boundaries.

Coverage floors and allocation ceilings remain unchanged. Timing observations
remain diagnostic. This preparation does not activate `maintenance/10.x` or
`preview/11.x`, authorize publication, or assert hosted qualification or NuGet
availability. When upgrading from a version before 10.6.4, regenerate existing
compiled models to adopt the corrected
[fixed-binary byte-array reader](provider-configuration.md#fixed-binary-byte-arrays).
The [MySqlConnector security floor](security/dependency-audits.md#mysqlconnector-security-floor)
remains 2.6.1 with upper boundary `< 4.0.0`. Review the
[driver compatibility notes](operations/resilience-and-topology.md#mysqlconnector-3)
before selecting 3.x explicitly.

## Use the Provider

- [Provider Architecture](architecture.md) explains the runtime, design-time,
  migration, scaffolding, spatial, security, and verification boundaries.
- [Supported Databases](supported-databases.md) defines the active LTS matrix,
  exact qualified patches, test targets, unsupported-version behavior, and
  primary lifecycle evidence.
- [Host Integration](host-integration-examples.md) covers dependency injection,
  connection ownership, data sources, retry configuration, and telemetry.
- [IDE Integration](ide-integration.md) records provider-aware Rider and
  ReSharper inspection behavior and scoped consumer-project configuration.
- [Provider Configuration](provider-configuration.md) maps connection,
  context-option, model, reverse-engineering, and optional-package setup,
  including index-key byte budgets and generated-value readback.
- [Application Text Defaults](default-text-options.md) describes the 10.7.0
  charset/collation fallbacks, explicit overrides and normal migration route.
- [Migrating from Pomelo](migrating-from-pomelo.md) maps the known configuration,
  query, cache, migration, designer, and snapshot changes without claiming an
  exhaustive inventory of consumer applications.
- [Distributed Cache](distributed-cache.md) covers the standalone
  `Doka.Caching.MySql` package, explicit table deployment, expiration,
  low-allocation buffers, and bounded cleanup.
- [Query Functions](query-functions.md) defines provider-specific scalar and
  aggregate translations, activation rules and failure boundaries.
- [Window Functions](window-functions.md) describes the 10.7.0
  fluent calculations, frame and indexed-LINQ composition contracts.
- [Locking Reads](locking-reads.md) defines explicit read policies, final-query
  scope, transaction ownership, contention behavior and initial shape limits.
- [Complex Types](complex-types.md) covers mapping, querying, updates, JSON,
  compiled models, and exact EF Core boundaries.
- [Temporal Tables](temporal-tables.md) covers portable system-versioned
  storage, query operators, migrations, reverse engineering,
  application-time periods, and bitemporal storage.
- [CTEs and Query Composition](ctes.md) covers scoped ordinary and bounded
  recursive LINQ sources, raw query roots, materialized sources, directed
  `StraightJoin` edges, parameterization, composability, and data-modification
  boundaries.
- [INSERT SELECT](insert-select.md) describes the 10.7.0 typed
  assignment language, direct writes and transaction ownership.
- [UPSERT](upsert.md) describes the 10.7.0 native conflict handling,
  bounded batches, affected counts and explicit write boundaries.
- [Migration Operation Handlers](migration-operation-handlers.md) defines the
  additive custom-operation SPI, registration boundary, capability projection,
  failure contract, and package-author verification matrix.
- [External Limitations](limitations.md) inventories only engine-owned and
  EF Core-owned boundaries. Provider-owned gaps have a zero budget.

## Operate the Provider

- [Workflow Overview](operations/workflow-overview.md) maps shared PR/RC
  qualification, separate publication and scheduled checks, including hosted
  benchmark enforcement without hardware-dependent time gates.
- [Operations Runbook](operations-runbook.md) routes incidents, migrations,
  resilience, publication, and performance work to their owned procedures.
- [Release Publication](operations/release-publication.md) defines the ordered
  single-entry path from a qualified exact line through untagged qualification,
  personal signed tagging, protected publication, and public readback; its
  concrete main/10 examples remain the currently active route.
- [Release Lines](operations/release-lines.md) defines exact main/maintenance/
  preview roles, prepared 10.x maintenance, inactive preview and protected activation,
  monotonic Latest, framework servicing and exact public RC consumption. It
  retains the existing single-maintainer/admin-bypass arrangement and `nuget`
  environment, with no publisher migration or transition-report subsystem.
- [Performance Evidence](operations/performance-evidence.md) routes benchmark
  execution and failure triage to the profile/schema
  [reference](operations/performance-evidence-reference.md), retired paired
  [methodology](operations/paired-performance-methodology.md), and budget
  [operations](operations/performance-baseline-operations.md).
- [Repository Security Settings](operations/repository-security-settings.md)
  records the GitHub controls that cannot be verified from the repository tree.
- [Release Governance](release-governance.md) defines release gates, evidence,
  compatibility, servicing, and diagnostics policy.
- [Threat Model](security/threat-model.md) defines trust boundaries, assets,
  abuse cases, and required mitigations.
- [Security Assurance Case](security/assurance-case.md) maps every published
  security claim to its argument, controls, weaknesses, and evidence.
- [Dependency Security Audits](security/dependency-audits.md) defines daily
  active-line audits, fresh advisory checks, failure evidence and security backports.
- [Release Verification](security/release-verification.md) shows consumers how
  to verify signed tags, SLSA provenance, and NuGet repository signatures.

## Maintain the Provider

- [OpenSSF Best Practices Evidence](openssf-best-practices.md) maps current
  Silver and Gold documentation evidence without masking people or settings
  that the repository cannot provide.
- [Governance](../GOVERNANCE.md) defines decision authority, roles,
  responsibilities, conflicts, and the current continuity limit.
- [Roadmap](../ROADMAP.md) records intended and explicitly excluded work through
  July 2027.
- [Architecture Decisions](decisions/README.md) indexes the validated MADR
  decision corpus.
- [Contributing](../CONTRIBUTING.md) covers local setup, test tiers, code style,
  pull requests, and public API changes.
- [Engineering Standards](engineering-standards.md) records the adopted shared
  rules, source snapshot identities and explicit .NET 10 project baselines.
- [Support](../SUPPORT.md) defines supported requests and issue routing.
- [Security Policy](../SECURITY.md) defines private vulnerability reporting and
  coordinated disclosure.

## Document Ownership

| Type | Owns | Excludes |
| --- | --- | --- |
| Root README | Summary, installation, and first use | Full contracts |
| Feature guide | Supported behavior, examples, and failure boundaries | Decision history |
| API reference | Exact public entry points, translations, and precedence | Tutorials and decision history |
| Limitations ledger | Engine and EF Core boundaries | Provider gaps |
| Architecture decision | Context, alternatives, and consequences | Procedures |
| Operations runbook | Diagnosis, recovery, and operations | Policy rationale |
| Methodology | Evidence design, inference, and acceptance rules | Operator procedures |
| Compliance evidence | Criterion-to-proof mapping and honest readiness state | The external criterion itself |
| Release governance | Gates, evidence, and servicing | Incident commands |

Every public capability should have one canonical feature guide. Every
external boundary should appear in exactly one detailed limitations ledger.
When content moves inside a published canonical document, retain its previous
section anchor and route that anchor to the new owner. Do not keep a standalone
compatibility page after every repository-owned reference has migrated.

## Documentation Contract

Repository documentation is executable evidence rather than an unverified
catalog. Every change must preserve:

- one canonical owner for each public capability and operational procedure;
- task-oriented navigation from this index or the operations runbook;
- runnable examples or repository commands for behavior claims;
- dated primary sources for engine-, framework-, tool-, and host-specific
  claims; and
- compatibility anchors when a published section moves to a new owner.

Run the same dependency-free contract used by the quality gate:

```bash
./eng/quality-gates.sh --fast
```

The dependency-free compiled validator checks local links and anchors, packaged-README portability,
required evidence sections, performance-runbook routing, and canonical
documentation of public query and configuration methods.
