# Project Roadmap

This roadmap records what `Doka.EntityFrameworkCore.MySql` intends to do and
not do from August 2026 through July 2027. It describes direction rather than a
promise that every item will ship on a fixed date. Supported behavior remains
defined by released packages, the support matrix, and release notes.

## Direction Through July 2027

### Maintain the stable EF Core 10 release line

- Maintain the released .NET 10 / EF Core 10 package line and qualify each
  patch or additive minor release through the shared release contract.
- Treat correctness, data integrity, security, migrations, query semantics,
  type mapping, and cross-engine compatibility defects as the primary work on
  the `10.x` line.
- Keep the public API small. Add surface only when a supported scenario cannot
  be expressed safely through EF Core or an existing provider contract.
- Maintain package, trimming, runtime, migration, scaffolding, specification,
  and live-engine verification for all three coordinated packages.

### Maintain supported dependencies and engines

- Keep the supported EF Core 10 maintenance range, the reviewed EF Core 11 GA
  patch range, and the qualified MySqlConnector 2.x/3.x range under
  compatibility and vulnerability review.
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

<a id="deliver-the-1070-feature-set-on-ef-core-10"></a>

### Maintain the published 10.7.0 feature set

- Version 10.7.0 published application character-set/collation fallbacks,
  explicit locking reads, typed ordinary/recursive CTEs, window functions,
  typed INSERT SELECT and native single/range UPSERT in the .NET 10 line.
- Maintain these opt-in contracts and qualify affected feature combinations on
  all supported MySQL and MariaDB profiles for subsequent releases.
- Keep database-native timing measurements diagnostic and retain deterministic
  semantic controls and existing allocation ceilings.

### Qualify the next platform major deliberately

- Keep the separate .NET 11 / EF Core 11 GA preparation isolated until the
  approved platform cutover. It must retain the applicable feature contracts
  and their independent compatibility evidence.
- The maintainer-authorized trigger in ADR D-013 has fired; `feature/dotnet-11`
  owns SDK 11.0.100 and the reviewed EF Core 11 GA graph.
- Treat that transition as a new provider major with explicit validation of
  EF Core internal services, public API compatibility, trimming/AOT posture,
  migrations and specification tests.
- Keep publication, hosted authorization and the maintenance/main cutover as
  separate operator decisions. The maintenance branch name does not establish
  an automatic feature freeze for the supported .NET 10 line.

<a id="complete-the-first-1100-feature-set"></a>

### Retain the implemented 11.0.0 feature set

- The 11.0.0 source implements the same six contracts already published in
  10.7.0, adapted to .NET 11 / EF Core 11. Their implementation is complete;
  they are not pending feature work.
- Preserve these contracts and require source-bound qualification of the
  supported combinations on the qualified MySQL and MariaDB profiles before
  the first 11.0.0 publication. Implementation does not establish publication
  or completed hosted qualification for a later source revision.
- Keep publication, hosted authorization and the maintenance/main cutover as
  separate operator decisions. Review future changes for each supported line;
  no automatic backport is implied.

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
