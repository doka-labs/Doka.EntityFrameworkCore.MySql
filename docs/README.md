# Documentation

This index is the canonical entry point for provider documentation. Choose a
path by the task you need to complete. Architecture records explain why a
decision exists, while feature guides and runbooks explain supported behavior
and operational procedures.

## Use the Provider

- [Supported Databases](supported-databases.md) defines the active LTS matrix,
  exact qualified patches, test targets, unsupported-version behavior, and
  primary lifecycle evidence.
- [Host Integration](host-integration-examples.md) covers dependency injection,
  connection ownership, data sources, retry configuration, and telemetry.
- [Complex Types](complex-types.md) covers mapping, querying, updates, JSON,
  compiled models, and exact EF Core boundaries.
- [Temporal Tables](temporal-tables.md) covers portable system-versioned
  storage, query operators, migrations, reverse engineering,
  application-time periods, and bitemporal storage.
- [Common Table Expressions](ctes.md) covers recursive and non-recursive query
  roots, parameterization, composability, and data-modification boundaries.
- [External Limitations](limitations.md) inventories only engine-owned and
  EF Core-owned boundaries. Provider-owned gaps have a zero budget.

## Operate the Provider

- [Operations Runbook](operations-runbook.md) routes incidents, migrations,
  resilience, publication, and performance work to their owned procedures.
- [Release Publication](operations/release-publication.md) defines the ordered
  path from a green `main` commit through signed tagging, paired qualification,
  protected NuGet publication, and public readback.
- [Performance Evidence](operations/performance-evidence.md) defines benchmark,
  soak, baseline, and regression-budget evidence.
- [Repository Security Settings](operations/repository-security-settings.md)
  records the GitHub controls that cannot be verified from the repository tree.
- [Release Governance](release-governance.md) defines release gates, evidence,
  compatibility, servicing, and diagnostics policy.
- [Threat Model](security/threat-model.md) defines trust boundaries, assets,
  abuse cases, and required mitigations.

## Maintain the Provider

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
| Feature guide | Supported behavior and examples | Decision history |
| Limitations ledger | Engine and EF Core boundaries | Provider gaps |
| Architecture decision | Context, alternatives, and consequences | Procedures |
| Operations runbook | Diagnosis, recovery, and operations | Policy rationale |
| Release governance | Gates, evidence, and servicing | Incident commands |

Every public capability should have one canonical feature guide. Every
external boundary should appear in exactly one detailed limitations ledger.
Historical paths may remain as compatibility pages, but they must link to the
canonical owner instead of duplicating the contract.
