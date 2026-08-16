# EF Core specification contracts

This directory contains generated, version-bound evidence for the provider's
EF Core relational specification surface. The files are executable contracts,
not architecture decisions.

## Contract layers

- `SpecSuiteInventory.<version>.json` records every official core and
  relational `*TestBase`, its fixture contract, and its declared and inherited
  xUnit methods for the exact restored EF Core patch.
- `SpecSuiteBaseline.json` maps every upstream base to the current concrete
  provider test type, the official `NonSharedModelTestBase` exemption, or
  provider-owned suite debt. The debt count may only decrease.
- `SpecDiscovery.<version>.json` records the exact xUnit display IDs discovered
  for every active LTS target. It detects missing fixtures, missing Theory rows,
  duplicate IDs, and unexpected discovery growth.
- The discovery gate also compares the complete specification namespace with
  `Category=Spec`, so an adapter cannot silently fall out of the release matrix.
- `../SpecDispositions.json` records only executable engine, upstream-framework,
  and structurally not-applicable outcomes. Each disposition names its exact
  discovered test IDs. Provider debt is never a permitted disposition.

The `closurePhase` values in the baseline are internal delivery ownership.
They do not express architecture, compatibility, or a durable product
decision. Moving work between delivery phases therefore does not create or
amend an ADR.

## Current baseline

EF Core 10.0.8, 10.0.10, and 10.0.11 expose 327 official compliance bases. The
10.0.8 inventory contains 9,031 unique xUnit method definitions and 19,176
effective base-to-method assignments. The 10.0.10 inventory contains 9,039
definitions and 19,191 assignments. The 10.0.11 inventory contains 9,040
definitions and 19,192 assignments.

The baseline retrieved on 2026-07-27 recorded 9 implemented base mappings,
1 official compliance exemption, and 317 provider-owned gaps. Those 317 gaps
are now closed: the repository validator reports provider suite debt `0/317`
for every registered EF Core patch contract.

Discovery regenerated through 2026-08-16 records the complete concrete provider
surface:

| EF Core | Target | Discovered |
| --- | --- | ---: |
| 10.0.8 | MySQL 8.4 | 29,746 |
| 10.0.8 | MySQL 9.7 | 29,746 |
| 10.0.8 | MariaDB 10.11 | 29,412 |
| 10.0.8 | MariaDB 11.4 | 29,410 |
| 10.0.8 | MariaDB 11.8 | 29,411 |
| 10.0.8 | MariaDB 12.3 | 29,417 |
| 10.0.10 | MySQL 8.4 | 29,754 |
| 10.0.10 | MySQL 9.7 | 29,754 |
| 10.0.10 | MariaDB 10.11 | 29,420 |
| 10.0.10 | MariaDB 11.4 | 29,418 |
| 10.0.10 | MariaDB 11.8 | 29,419 |
| 10.0.10 | MariaDB 12.3 | 29,425 |
| 10.0.11 | MySQL 8.4 | 29,755 |
| 10.0.11 | MySQL 9.7 | 29,755 |
| 10.0.11 | MariaDB 10.11 | 29,421 |
| 10.0.11 | MariaDB 11.4 | 29,419 |
| 10.0.11 | MariaDB 11.8 | 29,420 |
| 10.0.11 | MariaDB 12.3 | 29,426 |

The complete six-target matrix was executed in full against EF Core 10.0.10
on 2026-08-11:

| EF Core | Target | Passed | Skipped | Failed | Total |
| --- | --- | ---: | ---: | ---: | ---: |
| 10.0.10 | MySQL 8.4 | 29,427 | 327 | 0 | 29,754 |
| 10.0.10 | MySQL 9.7 | 29,427 | 327 | 0 | 29,754 |
| 10.0.10 | MariaDB 10.11 | 28,720 | 700 | 0 | 29,420 |
| 10.0.10 | MariaDB 11.4 | 28,716 | 702 | 0 | 29,418 |
| 10.0.10 | MariaDB 11.8 | 28,718 | 701 | 0 | 29,419 |
| 10.0.10 | MariaDB 12.3 | 28,730 | 695 | 0 | 29,425 |

The complete six-target matrix was executed in full against EF Core 10.0.11
on 2026-08-16:

| EF Core | Target | Passed | Skipped | Failed | Total |
| --- | --- | ---: | ---: | ---: | ---: |
| 10.0.11 | MySQL 8.4 | 29,428 | 327 | 0 | 29,755 |
| 10.0.11 | MySQL 9.7 | 29,428 | 327 | 0 | 29,755 |
| 10.0.11 | MariaDB 10.11 | 28,721 | 700 | 0 | 29,421 |
| 10.0.11 | MariaDB 11.4 | 28,717 | 702 | 0 | 29,419 |
| 10.0.11 | MariaDB 11.8 | 28,719 | 701 | 0 | 29,420 |
| 10.0.11 | MariaDB 12.3 | 28,731 | 695 | 0 | 29,426 |

Each raw 10.0.11 run also passed two provider-owned `Category=Live` checks
that sit outside the version-bound upstream discovery inventory.

The TRX totals and display IDs matched the discovery contracts regenerated
through 2026-08-16. Every skip matched its ledger ID, method, and target; every other
discovered test passed. The three newly admitted targets were also executed
against the minimum EF Core 10.0.8 patch on the same date. The scheduled patch
matrix continues to execute both supported dependency endpoints in full.
Release qualification re-resolves and records the deterministic floor graph,
then fully executes the latest compatible patch; the commit-exact
`repository-qualification` check already owns full floor behavior across all
six active LTS targets.
The source contract also rejects inherited upstream skips unless the provider
activates the assertion or records an executable framework disposition.

The publication gate still calculates this state from the provider assembly.
These figures are evidence, not a substitute for the zero-debt check.

## Verification

Build the functional-test assembly and contract tool in Release mode, then run:

```bash
bash eng/testing/check-spec-version-contract.sh <exact_ef_core_version>
bash eng/testing/check-spec-contract.sh
bash eng/testing/check-spec-discovery.sh
```

The floor/latest patch runner invokes the exact-version preflight immediately
after reading back NuGet's resolved graph and before starting the repository or
live-engine suites.

After a live specification run, reconcile its TRX results with the exact
discovery and disposition contracts:

```bash
bash eng/testing/check-spec-results.sh mysql84 artifacts/spec-tests/mysql84
```

Before publication, run the stricter official compliance and zero-debt gate:

```bash
bash eng/check-publication-readiness.sh
```

Inventories are generated by the in-repository
`Doka.EntityFrameworkCore.MySql.SpecificationContract` tool. Regeneration must
use an explicit EF Core version and record the retrieval date. Review the JSON
diff before accepting an upstream patch.

## Primary sources

Retrieved on 2026-08-16:

- NuGet package versions:
  <https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.relational.specification.tests/index.json>
- EF Core `ComplianceTestBase` 10.0.11:
  <https://github.com/dotnet/efcore/blob/v10.0.11/test/EFCore.Specification.Tests/ComplianceTestBase.cs>
- EF Core `RelationalComplianceTestBase` 10.0.11:
  <https://github.com/dotnet/efcore/blob/v10.0.11/test/EFCore.Relational.Specification.Tests/RelationalComplianceTestBase.cs>
