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
  - representative live DB path:

    ```bash
    DOKA_INTEGRATION_TARGETS=mysql84,mariadb118 \
    DOKA_INTEGRATION_TEST_FILTER='VerificationLane!=FullIntegration' \
    ./eng/test-integration.sh
    ```

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
  - targets: `mysql84`, `mariadb114`, and `mariadb118`
  - includes the complete configuration, security, and failure categories
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
  - toolchain gate: every hosted job installs the exact stable SDK declared in
    `global.json`; roll-forward is disabled, release evidence binds the approved
    and observed identities, and Dependabot proposes SDK changes as reviewed
    pull requests
  - repository quality gate: the complete shared `quality-gates.sh` contract,
    including formatting, analyzers, public examples, README compilation,
    dependency audits, and migration-model verification
  - performance gate: isolated run-owned Compose projects and dynamic ports;
    scorecards run before the repository build and database-heavy verification
    so their initial host snapshot is not contaminated by the release workflow
    itself; host admission uses active-process CPU instead of Unix
    load average, which can count runnable desktop and video-decoding threads;
    adjacent deterministic CPU or live database calibration pulses normalize
    historical latency per workload; an isolated normalized historical p99
    failure is confirmed by two targeted calibrated measurements before the
    combined population is gated; raw latency and managed allocation remain
    hard workload gates, while process-global retained-heap delta and
    Gen0/Gen1/Gen2 collection counts are retained as diagnostics; sustained
    retained-memory behavior remains a hard soak invariant
  - bounded execution and recovery: engine scorecards have contract-owned hard
    deadlines and source-bound per-workload checkpoints; the complete release
    candidate has a two-hour default deadline and source-bound per-stage
    receipts; resumed stages are reused only after every retained artifact
    digest passes readback, and incomplete outputs are archived before retry
  - performance recovery: a failed later gate may reuse an earlier candidate's
    complete scorecards by setting
    `DOKA_RELEASE_CANDIDATE_REUSE_PERFORMANCE_FROM` to its evidence root; reuse
    is accepted only when both engine evaluations and their retained artifacts
    pass integrity validation, the measured commit is an ancestor of the new
    candidate, and Git proves that no provider, benchmark, dependency, build,
    or container input changed; the new candidate retains an exhaustive source
    delta and per-target evaluation hashes in `performance/reuse-evidence.json`
  - integration gate: unfiltered configuration and failure matrix across
    `mysql84`, `mariadb114`, and `mariadb118`
  - executable-documentation gate: all thirteen live-matrix examples execute
    their own scenario invariants against every supported engine in an isolated
    Compose project with dynamic ports, protected sentinel-catalog readback,
    and verified cleanup
  - functional live gate: specification and standalone `Category=Live`
    contracts on all three supported engines
  - runtime posture gate: ordinary execution plus an executed self-contained
    binary published with `PublishTrimmed=true` and `TrimMode=full`
  - reconciliation gate: every named release contract must be present and
    passing before the immutable manifest can be generated
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
    - `artifacts/release-candidate/<run-id>/integration/examples/live-example-matrix-evidence.json`
    - `artifacts/release-candidate/<run-id>/migration-deployment/...`
    - `artifacts/release-candidate/<run-id>/runtime/...`
    - `artifacts/release-candidate/<run-id>/release-candidate-reconciliation.json`
    - `artifacts/release-candidate/<run-id>/sbom/...`
- Manual NuGet publication and public readback:
  - workflow: `.github/workflows/nuget-publish.yml`
  - cadence: manually dispatched from trusted `main` after one successful
    release-candidate run for the exact current commit and release tag
  - explicit inputs: candidate workflow run ID, semantic release tag, and the
    literal confirmation `publish <release-tag>`
  - environment: `nuget`, restricted to the `main` branch
  - credential: a NuGet.org short-lived API key exchanged from GitHub OIDC
    immediately before the first push; no persistent NuGet API key is stored
  - candidate binding: completed successful `release-candidate.yml` run,
    immutable artifact readback, canonical manifest verification, exact
    current `main` commit, exactly one matching semantic tag, matching hosted
    workflow identity, and GitHub artifact-attestation verification constrained
    to the candidate workflow, tagged source ref, source and signer digests,
    and a GitHub-hosted runner
  - package binding: exact IDs and versions inside each nuspec, source commit
    and repository metadata, exact-version spatial dependency, and both symbol
    packages
  - safe retry: an absent package may be published; an existing package may be
    resumed only when its canonical ZIP payload matches the candidate after
    excluding NuGet.org's repository-owned `.signature.p7s` entry; conflicting
    same-version content fails before login
  - publication order: provider, provider symbols, spatial extension, spatial
    symbols; primary packages never use `--skip-duplicate`, while symbol-only
    uploads accept the endpoint's documented HTTP 409 pending state
  - public readback: bounded NuGet V3 and symbol-server polling, canonical
    package comparison, Portable PDB retrieval using the candidate DLL's SSQP
    key and SHA-256 checksum, empty-cache restore from NuGet.org only, and
    execution of the basic and spatial compiled-model runtime contract against
    the candidate's pinned MySQL 8.4 image
  - retained evidence:
    - `validated-candidate.json`
    - `publication-preflight.json`
    - `symbol-readback-manifest.json`
    - `nuget-publication-readback.json`
    - `consumer-runtime-readback.json`
    - downloaded public package and Portable PDB payloads
- Migration deployment:
  - workflow: `.github/workflows/ci.yml`
  - local path: `./eng/test-migration-deployment.sh`
  - retained evidence:
    - `artifacts/migration-deployment/<run-id>/migration-deployment-summary.md`
    - `artifacts/migration-deployment/<run-id>/migration-deployment-evidence.json`

## Integration Configuration and Failure Contract

The release-candidate integration gate covers configuration and failure modes
that unit tests or a healthy default connection cannot prove:

- provider-generated text literals are executed with the default SQL mode,
  `NO_BACKSLASH_ESCAPES`, `ANSI_QUOTES`, and their strict combined form
- static and dynamically indexed JSON paths apply JSON-path escaping first and
  then use the same mode-independent SQL literal generator as other text
- the reusable driver, lifecycle, network-fault, operability, transaction, and
  cross-layer observability contracts run on MariaDB 11.4 as well as MySQL 8.4
  and MariaDB 11.8
- test-owned certificate authorities and server/client certificates prove
  verified TLS, rejected plaintext, rejected untrusted and name-mismatched
  certificates, password success/failure, engine-default authentication
  plugins, and `REQUIRE X509` client authentication
- bounded pools prove saturation timeout, cancellation, recovery, physical
  connection reuse, session reset, and broken-connection eviction
- a deliberately unreachable first address followed by the live test proxy
  proves ordered multi-host failover through an actual provider query

The fast push lane excludes the three dedicated categories. The
release-candidate runner sets `DOKA_REQUIRE_FULL_CONFIGURATION_MATRIX=1`; the
shared runner then rejects a filtered selection or any target set other than
MySQL 8.4, MariaDB 11.4, and MariaDB 11.8. The immutable evidence generator
independently reads the persisted matrix result and rejects a non-zero exit
code, a filter, a partial target set, or a run not marked as required.

MySQL-family DDL accepts only its quoted comment-literal grammar, so the
provider cannot use the general `_utf8mb4 X'...'` expression form directly
after `COMMENT`. For comment statements containing a backslash, generated SQL
uses a server-executable comment to save the session mode, adds
`NO_BACKSLASH_ESCAPES` without removing existing modes, emits the quoted DDL,
and restores the exact previous mode. MySqlConnector treats the wrapper as a
comment while MySQL and MariaDB execute its contents, so runtime migrations do
not require `Allow User Variables=true`. Live readback verifies comment bytes,
data-operation values, and exact mode restoration.

JSON member names that are not identifiers are quoted and escaped at the JSON
path layer. The complete static path, or each literal chunk of a dynamic path,
then flows through the central SQL literal generator. Backslashes used by JSON
path syntax therefore reach the JSON parser as UTF-8 hexadecimal text instead
of being reinterpreted by the SQL parser. The live matrix covers quote,
backslash, and apostrophe member names under every supported SQL mode.

MariaDB certificate-negative tests use a test-owned account with an empty
password. MySqlConnector 2.6.1 contains a narrowly scoped MariaDB compatibility
path that can accept an otherwise invalid server certificate when no TLS
verification option is configured and the certificate fingerprint proves the
password exchange. Removing the password from this isolated negative-test
identity prevents that separate compatibility path from masking certificate
validation. Normal password and mutual-TLS identities are exercised
independently.

### Primary sources

- MySQL, [Hexadecimal Literals][mysql-hex-literals], retrieved 2026-07-31.
- MariaDB, [Hexadecimal Literals][mariadb-hex-literals], retrieved 2026-07-31.
- MySQL, [Comments][mysql-comments], retrieved 2026-07-31.
- MariaDB, [Comment Syntax][mariadb-comments], retrieved 2026-07-31.
- MySQL, [Functions That Search JSON Values][mysql-json-search], retrieved
  2026-07-31.
- MariaDB, [JSONPath Expressions][mariadb-json-path], retrieved 2026-07-31.
- MySQL, [Using Encrypted Connections][mysql-encrypted-connections], retrieved
  2026-07-31.
- MariaDB, [Securing Connections for Client and Server][mariadb-tls], retrieved
  2026-07-31.
- MySqlConnector, [Connection Options][mysqlconnector-options], retrieved
  2026-07-31.
- MySqlConnector 2.6.1, [`ServerSession` certificate validation source][mysqlconnector-server-session],
  retrieved 2026-07-31.

[mysql-hex-literals]: https://dev.mysql.com/doc/refman/8.4/en/hexadecimal-literals.html
[mariadb-hex-literals]: https://mariadb.com/docs/server/reference/sql-structure/sql-language-structure/hexadecimal-literals
[mysql-comments]: https://dev.mysql.com/doc/refman/8.4/en/comments.html
[mariadb-comments]: https://mariadb.com/docs/server/reference/sql-statements/comment-syntax
[mysql-json-search]: https://dev.mysql.com/doc/refman/8.4/en/json-search-functions.html
[mariadb-json-path]: https://mariadb.com/docs/server/reference/sql-functions/special-functions/json-functions/jsonpath-expressions
[mysql-encrypted-connections]: https://dev.mysql.com/doc/refman/8.4/en/using-encrypted-connections.html
[mariadb-tls]: https://mariadb.com/docs/server/security/securing-mariadb/securing-mariadb-encryption/encryption-data-in-transit
[mysqlconnector-options]: https://mysqlconnector.net/connection-options/
[mysqlconnector-server-session]: https://github.com/mysql-net/MySqlConnector/blob/2.6.1/src/MySqlConnector/Core/ServerSession.cs

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
- NuGet publication and post-publication install/readback are explicit,
  manually authorized release operations; Aurora MySQL is outside the
  advertised matrix

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
- GitHub, [`actions/setup-dotnet`](https://github.com/actions/setup-dotnet),
  retrieved 2026-08-03.
- GitHub,
  [Dependabot supported ecosystems and repositories](https://docs.github.com/en/code-security/reference/supply-chain-security/supported-ecosystems-and-repositories),
  retrieved 2026-08-03.
- NuGet, [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),
  retrieved 2026-08-03.
- NuGet, [`NuGet/login`](https://github.com/NuGet/login), retrieved
  2026-08-03.
- NuGet, [`dotnet nuget push`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push),
  retrieved 2026-08-03.
- NuGet,
  [Symbol packages](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg),
  retrieved 2026-08-03.
- NuGet,
  [Symbol package publish resource](https://learn.microsoft.com/en-us/nuget/api/symbol-package-publish-resource),
  retrieved 2026-08-03.
- .NET,
  [SSQP key conventions](https://github.com/dotnet/symstore/blob/main/docs/specs/SSQP_Key_Conventions.md),
  retrieved 2026-08-03.
