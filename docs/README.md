# Documentation

This index is the canonical entry point for provider documentation. Choose a
path by the task you need to complete. Architecture records explain why a
decision exists, while feature guides and runbooks explain supported behavior
and operational procedures.

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
  context-option, model, reverse-engineering, and optional-package setup.
- [Migrating from Pomelo](migrating-from-pomelo.md) maps the known configuration,
  query, cache, migration, designer, and snapshot changes without claiming an
  exhaustive inventory of consumer applications.
- [Distributed Cache](distributed-cache.md) covers the standalone
  `Doka.Caching.MySql` package, explicit table deployment, expiration,
  low-allocation buffers, and bounded cleanup.
- [Query Functions](query-functions.md) defines every provider-specific
  `EF.Functions` translation, activation rule, and failure boundary.
- [Complex Types](complex-types.md) covers mapping, querying, updates, JSON,
  compiled models, and exact EF Core boundaries.
- [Temporal Tables](temporal-tables.md) covers portable system-versioned
  storage, query operators, migrations, reverse engineering,
  application-time periods, and bitemporal storage.
- [Common Table Expressions](ctes.md) covers recursive and non-recursive query
  roots, parameterization, composability, and data-modification boundaries.
- [Migration Operation Handlers](migration-operation-handlers.md) defines the
  additive custom-operation SPI, registration boundary, capability projection,
  failure contract, and package-author verification matrix.
- [External Limitations](limitations.md) inventories only engine-owned and
  EF Core-owned boundaries. Provider-owned gaps have a zero budget.

## Operate the Provider

- [Operations Runbook](operations-runbook.md) routes incidents, migrations,
  resilience, publication, and performance work to their owned procedures.
- [Release Publication](operations/release-publication.md) defines the ordered
  path from a green `main` commit through untagged qualification, signed
  tagging, protected NuGet publication, and public readback.
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
