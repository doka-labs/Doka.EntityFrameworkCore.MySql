# Release Governance

`Doka.EntityFrameworkCore.MySql` treats release hardening as a reproducible
engineering contract. Local gates create the evidence; the hosted release
workflow binds its manifest to GitHub's artifact-attestation identity.

This document freezes the reviewable governance baseline for:

- regression expectations
- diagnostics categories and `MySqlEventId` ranges
- repo-local evidence paths
- PR review obligations tied to the planning contract

## Repo-Local Evidence Paths

The release-hardening evidence model is intentionally explicit and repeatable:

- PR workflow:
  - workflow: `.github/workflows/ci.yml`
  - shared quality path: `./eng/quality-gates.sh`
  - local commit subset: `./eng/quality-gates.sh --fast`
  - local path: `./eng/test.sh`
  - representative live DB path: `DOKA_INTEGRATION_TARGETS=mysql84,mariadb118 ./eng/test-integration.sh`
  - migration model drift gate: `./eng/check-migration-model.sh`
- Scheduled and manually dispatched exhaustive workflow:
  - workflow: `.github/workflows/ci.yml`
  - cadence: weekly and on demand
  - EF Core floor/latest matrix: `efcore-patch-matrix`
  - MySqlConnector floor/latest matrix: `mysqlconnector-patch-matrix`
  - supported MySqlConnector range: `[2.5.0, 3.0.0)`
  - driver evidence:
    - `artifacts/mysqlconnector-patch-matrix/<matrix-entry>/resolved-packages.json`
    - `artifacts/mysqlconnector-patch-matrix/<matrix-entry>/driver-contract-evidence.json`
    - `artifacts/mysqlconnector-patch-matrix/<matrix-entry>/test-database-evidence.json`
    - `artifacts/mysqlconnector-patch-matrix/<matrix-entry>/unit/...`
    - `artifacts/mysqlconnector-patch-matrix/<matrix-entry>/live/...`
  - specification targets: `mysql84`, `mariadb114`, and `mariadb118`
  - merged source-coverage gate: `coverage-gate`
  - migration deployment lifecycle: `./eng/test-migration-deployment.sh`
  - runtime smoke: `./eng/test-runtime-posture.sh --test-only`
  - benchmark smoke:
    - `DOKA_BENCHMARK_TARGET=mysql84 ./eng/benchmark.sh --test-only`
    - `DOKA_BENCHMARK_TARGET=mariadb118 ./eng/benchmark.sh --test-only`
- Scheduled container matrix:
  - workflow: `.github/workflows/container-matrix.yml`
  - cadence: weekly and on demand
  - local path: `./eng/test-integration.sh`
  - retained evidence:
    - `artifacts/integration/<run-id>/compatibility-matrix-summary.md`
    - `artifacts/integration/<run-id>/compatibility-matrix-evidence.json`
    - `artifacts/integration/<run-id>/test-database-evidence.json`
- Dedicated benchmark scorecard:
  - workflow: `.github/workflows/benchmark.yml`
  - local paths:

    ```bash
    DOKA_BENCHMARK_TARGET=mysql84 \
    DOKA_BENCHMARK_PROFILE=scorecard \
    ./eng/benchmark.sh --up-run-down

    DOKA_BENCHMARK_TARGET=mariadb118 \
    DOKA_BENCHMARK_PROFILE=scorecard \
    ./eng/benchmark.sh --up-run-down
    ```

  - retained evidence:
    - `artifacts/benchmarks/<target>/benchmark-summary.md`
    - `artifacts/benchmarks/<target>/benchmark-evidence.json`
    - `artifacts/benchmarks/<target>/reports/<run-id>/...`
- Repo-local release candidate:
  - workflow: `.github/workflows/release-candidate.yml`
  - cadence: manually dispatched from the exact semantic release tag
  - local path: `./eng/release-candidate.sh`
  - source gates: clean worktree, exact commit/ref, and exactly one matching
    `v<package-version>` tag
  - hosted proof: GitHub artifact attestation for packages and the canonical
    evidence manifest, followed by hosted verification readback
  - retained evidence:
    - `artifacts/release-candidate/<run-id>/release-candidate-changelog.md`
    - `artifacts/release-candidate/<run-id>/release-candidate-summary.md`
    - `artifacts/release-candidate/<run-id>/release-candidate-evidence.json`
    - `artifacts/release-candidate/<run-id>/release-candidate-evidence.sha256`
    - `artifacts/release-candidate/<run-id>/resolved-packages.json`
    - `artifacts/release-candidate/<run-id>/packages/...`
    - `artifacts/release-candidate/<run-id>/audit/...`
    - `artifacts/release-candidate/<run-id>/integration/...`
    - `artifacts/release-candidate/<run-id>/migration-deployment/...`
    - `artifacts/release-candidate/<run-id>/sbom/...`
- Migration deployment:
  - workflow: `.github/workflows/ci.yml`
  - local path: `./eng/test-migration-deployment.sh`
  - retained evidence:
    - `artifacts/migration-deployment/<run-id>/migration-deployment-summary.md`
    - `artifacts/migration-deployment/<run-id>/migration-deployment-evidence.json`

## Diagnostics Categories

The stable provider logging taxonomy for the `10.0.x` line is:

- `Doka.EntityFrameworkCore.MySql.Configuration`
- `Doka.EntityFrameworkCore.MySql.Query`
- `Doka.EntityFrameworkCore.MySql.Update`
- `Doka.EntityFrameworkCore.MySql.Migrations`
- `Doka.EntityFrameworkCore.MySql.Scaffolding`
- `Doka.EntityFrameworkCore.MySql.Resilience`
- `Doka.EntityFrameworkCore.MySql.Spatial`

Provider events emitted from EF Core model validation use
`Microsoft.EntityFrameworkCore.Model.Validation` instead of retaining a
context-specific logger inside the singleton model validator.

These category names are documentation-safe and test-backed. Renaming or repurposing them requires:

1. a deliberate planning update
2. matching test updates
3. an explicit PR justification

## Stable `MySqlEventId` Ranges

`MySqlEventId` values remain allocated by subsystem:

- `1000-1099`: configuration
- `1100-1199`: migrations and advisory locks
- `1400-1499`: scaffolding
- `1500-1599`: resilience
- `1600-1699`: spatial
- `1700-1799`: update and batch sizing

The current baseline uses these exact IDs:

- Configuration:
  - `1000` `ServerVersionResolved`
  - `1001` `InvalidConfiguration`
  - `1002` `SchemaUnsupported`
  - `1003` `KeyOrIndexMaxLengthRequired`
  - `1004` `ImplicitDecimalPrecisionDefaulted`
  - `1005` `UnsupportedServerVersion`
- Migrations:
  - `1100` `MigrationLockAcquired`
  - `1101` `MigrationLockTimeout`
  - `1102` `LockReleaseFailed`
  - `1103` `MigrationLockAcquireFailed`
- Resilience:
  - `1500` `RetryAttempt`
  - `1501` `RetryLimitExceeded`
  - `1502` `SoftCancellation`
  - `1503` `HardCancellation`
  - `1504` `CommandTimeoutExhausted`
  - `1505` `CommitUnknown`
- Scaffolding:
  - `1403` `ForeignKeyPrincipalTableNotScaffolded`
- Spatial:
  - `1600` `MissingSpatialPackageDuringScaffolding`
  - `1601` `InvalidSpatialIndexConfiguration`
  - `1602` `MissingSpatialTranslation`
  - `1603` `SpatialSridMismatchDetected`
- Update:
  - `1700` `BulkInsertParameterCountCapped`
  - `1701` `BulkInsertPacketSizeCapped`

New provider events must stay inside an approved subsystem range, update this document, and add or adjust coverage in the diagnostics-governance tests in the same change.

## Review Expectations

Release hardening keeps review obligations explicit:

- every PR that changes provider options, public API shape, engine-difference handling, or supported-engine policy must describe the contract impact in the PR summary
- diagnostics changes must call out:
  - affected categories
  - affected `MySqlEventId` values or ranges
  - affected benchmark, compatibility, or release-candidate evidence paths when applicable
- benchmark-impacting or compatibility-impacting changes must point to the relevant evidence workflow or explain why no evidence path changed

The repository PR template is the review seam for these obligations.

## Upstream Cadence and Servicing SLA

The repo-local servicing model is operational rather than informal:

- weekly upstream triage
- monthly compatibility review
- supported-engine lifecycle review whenever an upstream engine enters or leaves vendor-supported maintenance

The project-level servicing SLA stays binding:

- preview or RC analysis plus an adaptation plan within 7 calendar days of the upstream drop
- GA-compatible package or clearly labeled RC package within 14 calendar days of EF/.NET GA
- critical servicing fixes within 7 calendar days after a confirmed regression bug

These targets are release-governance gates, not marketing copy.

## Expected Repo-Reviewable Outputs

The cadence above is considered operational only when the repository captures explicit review outputs:

- Weekly upstream triage issue:
  - source link or release reference
  - reviewed date
  - owner
  - impact classification:
    - code change required
    - reviewed no-op
    - backlog item with target release
  - supported-engine or provider-surface impact notes
- Monthly compatibility review issue:
  - review month
  - owner
  - repo-local matrix status for MySQL `8.4`, MariaDB `11.4`, and MariaDB `11.8`
  - lifecycle change notes for supported engines
  - SLA risk notes
  - resulting actions or explicit no-op

The repository issue templates are the review seam for these outputs. They intentionally stay repo-local and do not rely on GitHub organization labels, automations, or protected metadata outside the repository itself.

## Scope Boundaries

This governance baseline:

- it supports repo-local hardening, immutable evidence inventory, and hosted
  artifact provenance
- GitHub artifact attestation is not NuGet repository signing and does not
  publish a package
- Azure Database for MySQL live validation remains an external canary when
  credentials become available; the provider contract does not depend on that
  account existing
- NuGet publication and post-publication package install/readback remain
  explicit release operations; Aurora MySQL is outside the advertised matrix

## Immutable Evidence Contract

`eng/release_evidence.py` generates the canonical manifest only after every
release gate has completed. It rejects dirty or mismatched source, mutable
engine image tags, incomplete engine coverage, stale or unexpected packages,
package/symbol version drift, missing SBOM output, and ambiguous dependency
versions. Every retained regular file receives a portable relative path,
SHA-256 digest, byte count, and role. A detached checksum protects the manifest
before the hosted workflow attests it.

Verification enumerates the directory again and fails on changed, missing, or
additional files. The release directory must be new and empty, so reruns cannot
inherit stale evidence from an earlier candidate.

### Primary sources

- GitHub, "Use artifact attestations", retrieved 2026-07-31:
  <https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations>
- GitHub, [`actions/attest`](https://github.com/actions/attest), retrieved
  2026-07-31.
