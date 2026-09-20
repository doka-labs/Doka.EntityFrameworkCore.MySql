# Project Roadmap

This roadmap records what `Doka.EntityFrameworkCore.MySql` intends to do and
not do from August 2026 through July 2027. It describes direction rather than a
promise that every item will ship on a fixed date. Supported behavior remains
defined by released packages, the support matrix, and release notes.

## Direction Through July 2027

### Maintain the stable EF Core 10 release line

- Maintain the published 10.x packages as the .NET 10 consumer line.
- Treat correctness, data integrity, security, migrations, query semantics,
  type mapping, and cross-engine compatibility defects as the primary work on
  the `10.x` line.
- Keep the public API small. Add surface only when a supported scenario cannot
  be expressed safely through EF Core or an existing provider contract.
- Maintain package, trimming, runtime, migration, scaffolding, specification,
  and live-engine verification for all three shipped packages.

### Maintain supported dependencies and engines

- Keep the supported EF Core 10 maintenance range, the exact EF Core 11 RC.1
  graph, and the MySqlConnector 2.x range under compatibility and
  vulnerability review.
- Prioritize dependency updates that remediate a relevant vulnerability.
  Routine version churn without compatibility, security, or support value is
  not a roadmap goal.
- Requalify the current patch of each advertised MySQL and MariaDB LTS line
  before moving a database-image pin.
- Consider a new engine LTS line through an explicit support decision that
  includes lifecycle evidence, capabilities, documentation, and the complete
  live qualification matrix.

### Preserve operational evidence without release coupling

- Keep release qualification deterministic, commit-bound, and independent of
  benchmark availability.
- Keep benchmarks as engineering evidence for performance investigation and
  reviewed baseline work. Optimize their execution only when the statistical
  and evidence contracts remain intact.
- Continue to publish checksums, SBOMs, signed tags, immutable releases, NuGet
  repository signatures, and portable SLSA provenance for releases.

### Qualify the next platform major deliberately

- Keep EF Core 11 release-candidate packages isolated from the stable 10.x
  provider line.
- The maintainer-authorized trigger in ADR D-013 has fired; `feature/dotnet-11`
  owns the exact RC.1 graph.
- Treat that transition as a new provider major with explicit validation of
  EF Core internal-service dependencies, public API compatibility, trim and
  AOT posture, migrations, and specification tests.

## Explicit Non-Goals Through July 2027

The project does not intend to:

- add support for end-of-life MySQL or MariaDB release lines;
- advertise Azure Database for MySQL or Amazon Aurora compatibility before a
  maintained live qualification path exists;
- support an ADO.NET driver other than MySqlConnector;
- pursue API or feature-count parity with another MySQL provider;
- hide unsupported query behavior through client evaluation;
- move application authentication, authorization, tenant isolation, network
  policy, or database privilege ownership into the provider;
- make benchmarks a release-publication authority; or
- merge the EF Core 11 line before its exact SDK, package graph, specification
  contracts, runtime posture, and live-engine matrix are reproducible.

## Review and Change Process

The lead maintainer reviews this roadmap at least quarterly and whenever one
of these events occurs:

- a supported platform or engine publishes a new major or LTS line;
- a vulnerability requires a dependency or support-policy change;
- a confirmed consumer requirement changes release priorities; or
- an accepted ADR changes a roadmap commitment or non-goal.

A roadmap change uses a pull request and links the issue, consumer evidence,
or ADR that caused it. Completed behavior is recorded in `CHANGELOG.md`; the
roadmap is not a substitute for release notes.

## Related Contracts

- [Governance](GOVERNANCE.md)
- [Supported database matrix](docs/supported-databases.md)
- [External limitations](docs/limitations.md)
- [EF Core 11 upgrade decision](docs/decisions/D-013-ef-core-11-upgrade.md)
- [Release governance](docs/release-governance.md)

## Primary Source

- OpenSSF Best Practices, [Silver roadmap criterion](https://www.bestpractices.dev/en/criteria/1),
  retrieved 2026-08-21. The criterion requires both intended and deliberately
  excluded work for at least the next year.
