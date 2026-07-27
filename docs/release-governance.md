# Release Governance

`Doka.EntityFrameworkCore.MySql` treats Phase 4 as repo-local release hardening, not as external launch closure.

This document freezes the reviewable governance baseline for:

- regression expectations
- diagnostics categories and `MySqlEventId` ranges
- repo-local evidence paths
- PR review obligations tied to the planning contract

## Repo-Local Evidence Paths

The Phase 4 evidence model is intentionally explicit and repeatable:

- PR workflow:
  - workflow: `.github/workflows/ci.yml`
  - local path: `./eng/test.sh`
  - representative live DB path: `DOKA_INTEGRATION_TARGETS=mysql84,mariadb118 ./eng/test-integration.sh`
  - runtime smoke: `./eng/test-runtime-posture.sh --test-only`
  - benchmark smoke:
    - `DOKA_BENCHMARK_TARGET=mysql84 ./eng/benchmark.sh --test-only`
    - `DOKA_BENCHMARK_TARGET=mariadb118 ./eng/benchmark.sh --test-only`
- Scheduled container matrix:
  - workflow: `.github/workflows/container-matrix.yml`
  - local path: `./eng/test-integration.sh`
  - retained evidence:
    - `artifacts/integration/<run-id>/compatibility-matrix-summary.md`
    - `artifacts/integration/<run-id>/compatibility-matrix-evidence.json`
    - `artifacts/integration/<run-id>/test-database-evidence.json`
- Dedicated benchmark scorecard:
  - workflow: `.github/workflows/benchmark.yml`
  - local path:
    - `DOKA_BENCHMARK_TARGET=mysql84 DOKA_BENCHMARK_PROFILE=scorecard ./eng/benchmark.sh --up-smoke-down`
    - `DOKA_BENCHMARK_TARGET=mariadb118 DOKA_BENCHMARK_PROFILE=scorecard ./eng/benchmark.sh --up-smoke-down`
  - retained evidence:
    - `artifacts/benchmarks/<target>/benchmark-summary.md`
    - `artifacts/benchmarks/<target>/benchmark-evidence.json`
    - `artifacts/benchmarks/<target>/reports/<run-id>/...`
- Repo-local release candidate:
  - workflow: `.github/workflows/release-candidate.yml`
  - local path: `./eng/release-candidate.sh`
  - retained evidence:
    - `artifacts/release-candidate/<run-id>/release-candidate-changelog.md`
    - `artifacts/release-candidate/<run-id>/release-candidate-summary.md`
    - `artifacts/release-candidate/<run-id>/release-candidate-evidence.json`
    - `artifacts/release-candidate/<run-id>/audit/...`
    - `artifacts/release-candidate/<run-id>/sbom/...`

## Diagnostics Categories

The stable provider logging taxonomy for the `10.0.x` line is:

- `Doka.EntityFrameworkCore.MySql.Configuration`
- `Doka.EntityFrameworkCore.MySql.Query`
- `Doka.EntityFrameworkCore.MySql.Update`
- `Doka.EntityFrameworkCore.MySql.Migrations`
- `Doka.EntityFrameworkCore.MySql.Scaffolding`
- `Doka.EntityFrameworkCore.MySql.Resilience`
- `Doka.EntityFrameworkCore.MySql.Spatial`

These category names are documentation-safe and test-backed. Renaming or repurposing them requires:

1. a deliberate planning update
2. matching test updates
3. an explicit PR justification

## Stable `MySqlEventId` Ranges

`MySqlEventId` values remain allocated by subsystem:

- `1000-1099`: configuration
- `1100-1199`: query and translation
- `1200-1299`: update and value generation
- `1300-1399`: migrations
- `1400-1499`: scaffolding
- `1500-1599`: resilience
- `1600-1699`: spatial

The current Phase 4 baseline uses these exact IDs:

- Configuration:
  - `1000` `ServerVersionResolved`
  - `1001` `InvalidConfiguration`
  - `1002` `SchemaUnsupported`
  - `1003` `KeyOrIndexMaxLengthRequired`
  - `1004` `ImplicitDecimalPrecisionDefaulted`
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

New provider events must stay inside an approved subsystem range, update this document, and add or adjust coverage in the diagnostics-governance tests in the same change.

## Review Expectations

Phase 4 keeps review obligations explicit:

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

## Phase Boundaries

This governance baseline is Phase 4-only:

- it supports repo-local hardening and reviewability
- it does not imply signing, provenance, publication, or hosted managed-service launch evidence
- Azure Database for MySQL live validation (when credentials become available), signing, provenance, and publication remain Phase 6 work; Aurora MySQL is out of scope
