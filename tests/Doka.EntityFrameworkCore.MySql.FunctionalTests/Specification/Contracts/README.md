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
- The release matrix selects the complete provider `Specification` namespace
  and provider adapters marked `Category=Spec`. The latter includes the runtime
  migration adapter that upstream source generation requires in EF Core's
  namespace. The discovery parser admits only that adapter's exact external
  prefix, and the gate reconciles the resulting exact test IDs.
- `../SpecDispositions.json` records only executable engine, upstream-framework,
  and structurally not-applicable outcomes. Each disposition names its exact
  discovered test IDs. Provider debt is never a permitted disposition.

The `closurePhase` values in the baseline are internal delivery ownership.
They do not express architecture, compatibility, or a durable product
decision. Moving work between delivery phases therefore does not create or
amend an ADR.

## Current baseline

EF Core `11.0.0-rc.1.26425.128` exposes 329 official compliance bases, 9,299
unique xUnit method definitions, and 19,659 effective base-to-method
assignments. Every base is implemented or covered by the upstream-defined
`NonSharedModelTestBase` exemption; the provider suite debt is `0/0`.

Discovery regenerated on 2026-09-19 records 30,422 exact specification test
IDs for each supported target:

| EF Core | Target | Discovered |
| --- | --- | ---: |
| 11.0.0-rc.1.26425.128 | MySQL 8.4 | 30,422 |
| 11.0.0-rc.1.26425.128 | MySQL 9.7 | 30,422 |
| 11.0.0-rc.1.26425.128 | MariaDB 10.11 | 30,422 |
| 11.0.0-rc.1.26425.128 | MariaDB 11.4 | 30,422 |
| 11.0.0-rc.1.26425.128 | MariaDB 11.8 | 30,422 |
| 11.0.0-rc.1.26425.128 | MariaDB 12.3 | 30,422 |

## Historical 10.x evidence

The stable 10.x line recorded the following exact discovery counts before the
11.x contract replaced its floor/latest patch matrix:

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
| 10.0.12 | MySQL 8.4 | 29,755 |
| 10.0.12 | MySQL 9.7 | 29,755 |
| 10.0.12 | MariaDB 10.11 | 29,421 |
| 10.0.12 | MariaDB 11.4 | 29,419 |
| 10.0.12 | MariaDB 11.8 | 29,420 |
| 10.0.12 | MariaDB 12.3 | 29,426 |

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
outside its version-bound upstream discovery inventory. The TRX totals and
display IDs matched the historical discovery contracts, and every skip matched
its ledger ID, method, and target.

The publication gate still calculates this state from the provider assembly.
These figures are evidence, not a substitute for the zero-debt check.

## Verification

Build the functional-test assembly and contract tool in Release mode, then run:

```bash
bash eng/testing/check-spec-version-contract.sh <exact_ef_core_version>
bash eng/testing/check-spec-contract.sh
bash eng/testing/check-spec-discovery.sh
```

The repository and release workflows validate the exact restored EF Core RC.1
graph before starting the live-engine suites.

After a live specification run, reconcile its TRX results with the exact
discovery and disposition contracts:

```bash
bash eng/testing/check-spec-results.sh mysql84 artifacts/spec-tests/mysql84
```

Before publication, run the stricter official compliance and zero-debt gate:

```bash
bash eng/release/check-publication-readiness.sh \
  --mysqlconnector-version 2.5.0
```

Inventories are generated by the in-repository
`Doka.EntityFrameworkCore.MySql.SpecificationContract` tool. Regeneration
records the exact restored EF Core version and retrieval date. Review the JSON
diff before accepting an upstream prerelease update.

## Primary sources

Retrieved on 2026-09-19:

- NuGet package versions:
  <https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.relational.specification.tests/index.json>
- EF Core `ComplianceTestBase` at the RC.1 source commit:
  <https://github.com/dotnet/efcore/blob/c22dd77aa7f7392f997cf779f0c23e0b9aab1988/test/EFCore.Specification.Tests/ComplianceTestBase.cs>
- EF Core `RelationalComplianceTestBase` at the RC.1 source commit:
  <https://github.com/dotnet/efcore/blob/c22dd77aa7f7392f997cf779f0c23e0b9aab1988/test/EFCore.Relational.Specification.Tests/RelationalComplianceTestBase.cs>
