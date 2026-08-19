# Release Governance

`Doka.EntityFrameworkCore.MySql` treats release hardening as a reproducible
engineering contract. Commit-exact CI and one manually dispatched, untagged
qualification workflow create the evidence. Hosted assembly freezes the
selected identities and payload digests before GitHub attests the candidate.

This document freezes the reviewable governance baseline for:

- regression expectations
- diagnostics categories and `MySqlEventId` ranges
- verification and retained-evidence paths
- PR review obligations tied to the planning contract

The canonical operator sequence from a reviewed, green `main` commit through
hosted qualification, signed tagging, NuGet publication, and immutable GitHub
publication is maintained in the
[release publication runbook](operations/release-publication.md#qualification-and-publication-procedure).
This document defines the underlying evidence and policy contracts; it does not
replace that ordered release procedure.

## Verification and Evidence Paths

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

  - migration model drift gate: `./eng/quality/check-migration-model.sh`
  - protected-branch aggregator: `repository-qualification`, which fails closed
    over quality, repository tests, specification conformance, integration
    smoke, and merged coverage for the exact commit
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
  - specification targets: `mysql84`, `mysql97`, `mariadb1011`, `mariadb114`,
    `mariadb118`, and `mariadb123`
  - merged source-coverage gate: `coverage-gate`
  - migration deployment lifecycle: `./eng/test-migration-deployment.sh`
  - runtime smoke: `./eng/test-runtime-posture.sh --test-only`
  - benchmark smoke:
    - targets: every key in `performance-contract.json.requiredTargets`
    - execution: one isolated, target-owned Compose lifecycle per matrix job
    - comparison: explicit single-revision `historical` orchestration with the
      non-baselined `smoke` profile; this checks absolute contracts and creates
      no release evidence
- Scheduled container matrix:
  - workflow: `.github/workflows/container-matrix.yml`
  - cadence: weekly and on demand
  - local path: `./eng/test-integration.sh`
  - targets: `mysql84`, `mysql97`, `mariadb1011`, `mariadb114`, `mariadb118`,
    and `mariadb123`
  - includes the complete configuration, security, and failure categories
  - retained evidence:
    - `artifacts/integration/<run-id>/compatibility-matrix-summary.md`
    - `artifacts/integration/<run-id>/compatibility-matrix-evidence.json`
    - `artifacts/integration/<run-id>/test-database-evidence.json`
- Dedicated benchmark scorecard:
  - workflow: `.github/workflows/benchmark.yml`
  - cadence: monthly, on demand, and after relevant performance-input changes
  - targets: every key in `performance-contract.json.requiredTargets`; the
    workflow derives its matrix from that contract instead of duplicating it
  - local path for one selected target:

    ```bash
    DOKA_BENCHMARK_TARGET=<target> \
    DOKA_BENCHMARK_PROFILE=scorecard \
    ./eng/benchmark.sh --up-run-down
    ```

  - retained evidence:
    - `artifacts/benchmarks/<target>/benchmark-summary.md`
    - `artifacts/benchmarks/<target>/benchmark-evidence.json`
    - `artifacts/benchmarks/<target>/reports/<run-id>/...`
  - authority: independent engineering evidence only; benchmark outcomes and
    artifacts are not release gates and are absent from candidate manifests
- Hosted candidate and protected publication:
  - workflow: `.github/workflows/release-candidate.yml`
  - trigger: one manual dispatch from exact current `main`, with the package
    version as its only input; tag pushes do not start another run
  - pre-tag lookup: `./eng/pre-tag-check.sh` verifies the clean commit,
    protected-main reachability, signing material, and successful
    `repository-qualification` without allocating a runner or creating a tag
  - imported branch gate: `repository-qualification` aggregates quality,
    repository tests, specification, integration smoke, and coverage for the
    candidate commit
  - candidate-produced gates: migration deployment, runtime posture, both
    patch matrices, package, and SBOM produce exactly six stage receipts while
    the source is still untagged
  - EF Core patch scope: the candidate re-resolves and records the exact floor
    graph already behavior-qualified by commit-exact
    `repository-qualification`, then fully executes the latest compatible
    patch; both scopes, resolved package graphs, per-target TRX and engine
    evidence, and the full-row integration result are independently verified
    during evidence assembly
  - EF Core version preflight: exact inventory, complete baseline membership,
    and six-target discovery contracts are required immediately after restore,
    before the full repository and live latest-patch matrix begins
  - local consumer boundary: before publication, an isolated project restores
    the exact local provider and spatial packages into an empty cache, binds
    their SHA-256 digests, compiles generated models, and executes basic and
    spatial runtime contracts against the pinned MySQL 8.4 image
  - performance boundary: candidate and publication code cannot invoke or read
    benchmark workflows, contracts, baselines, artifacts, or verdicts
  - candidate identity: `github.run_id` owns all stage artifacts; reruns may
    select only checksum-verified state from the same run and no future attempt
  - attestation boundary: the `attest` job alone receives attestation and
    artifact-metadata write permissions, and binds the untagged
    `refs/heads/main` source plus candidate bytes; its exact Sigstore bundle is
    materialized as attempt-qualified portable SLSA provenance
  - manual identity transition: after reversible qualification succeeds, the
    operator pushes one signed annotated `v<version>` tag on the candidate SHA
  - publication boundary: the waiting `publish` job alone enters the protected
    `nuget` environment and receives NuGet OIDC plus repository-write authority
  - same-run binding: publication revalidates the exact qualified checkout and
    its continued reachability from current `main`, the signed tag, frozen
    branch qualification, candidate receipt, package bytes, and same-run
    attestations; the exact portable bundle is selected through a job output,
    verified offline before draft creation, and no workflow run ID or artifact
    handoff is operator-selected
  - GitHub release staging: a matching draft and all pre-publication identity
    assets, including `release-provenance.intoto.jsonl`, are uploaded and read
    back before the first NuGet push
  - safe retry: existing primary packages are accepted only when canonical
    content matches after excluding NuGet.org's repository-owned
    `.signature.p7s`; provider, provider symbols, spatial, and spatial symbols
    publish in dependency order; every push tolerates a duplicate response so
    an accepted but not yet indexed package can resume, while the later public
    readback rejects conflicting bytes
  - immutable identity: after all four NuGet submissions return successfully,
    the already complete draft is published and independently read back before
    availability probes begin
  - public completion: independently ordered package and symbol visibility is
    polled until the bounded deadline; exact bytes that are visible before
    their repository signature remain pending rather than becoming a false
    terminal verdict; canonical byte comparison and cryptographic
    repository-signature verification are retained as retry-varying workflow
    evidence, never release assets
  - protocol discovery: package content is resolved from the configured V3
    service index's stable `PackageBaseAddress/3.0.0` capability and only a
    canonical lowercase NuGet release version can form a public readback URL
  - immutable recovery: matching drafts, partial NuGet publication, and
    matching immutable releases resume without replacement;
    unexpected metadata, assets, bytes, tags, or classification fail closed
  - environment: `nuget`, restricted to `main` and maintainer approval
  - credential: `NuGet/login` exchanges GitHub OIDC for a short-lived key only
    after the authoritative preflight; no persistent NuGet API key is stored
  - retained candidate evidence:
    - `artifacts/release-candidate/<run-id>/release-qualification-manifest.json`
    - `artifacts/release-candidate/<run-id>/release-gate-results.json`
    - `artifacts/release-candidate/<run-id>/release-candidate-summary.md`
    - `artifacts/release-candidate/<run-id>/release-candidate-reconciliation.json`
    - `artifacts/release-candidate/<run-id>/release-candidate-evidence.json`
    - `artifacts/release-candidate/<run-id>/release-candidate-evidence.sha256`
    - `artifacts/release-candidate/<run-id>/packages/...`
    - `artifacts/release-candidate/<run-id>/local-package-consumer/...`
    - `artifacts/release-candidate/<run-id>/sbom/...`
    - `artifacts/release-candidate/<run-id>/artifact-selections/...`
  - protected-check binding: publication reads the exact check-run ID and
    workflow-run attempt frozen in `release-qualification-manifest.json`, then
    recomputes its canonical response digest instead of selecting current CI
    state
  - retained publication evidence:
    - `candidate-receipt.json`
    - `release-publication-receipt.json`
    - `release-tag-trust-root.json`
    - `candidate-publication-preflight.json`
    - `publication-preflight.json`
    - `symbol-readback-manifest.json`
    - `release-provenance.intoto.jsonl`
    - `nuget-publication-readback.json`
    - `nuget-signature-verification.txt`
    - `github-release-staged-plan.json`
    - `github-release-readback.json`
    - `release-publication-completion.json`
    - downloaded public package and Portable PDB payloads
  - retained hosted artifact:
    `release-publication-<version>-attempt-<attempt>`
- Migration deployment:
  - workflow: `.github/workflows/ci.yml`
  - local path: `./eng/test-migration-deployment.sh`
  - retained evidence:
    - `artifacts/migration-deployment/<run-id>/migration-deployment-summary.md`
    - `artifacts/migration-deployment/<run-id>/migration-deployment-evidence.json`

## Integration Configuration and Failure Contract

The scheduled container matrix and the explicit local full-matrix command cover
configuration and failure modes that unit tests or a healthy default connection
cannot prove:

- provider-generated text literals are executed with the default SQL mode,
  `NO_BACKSLASH_ESCAPES`, `ANSI_QUOTES`, and their strict combined form
- static and dynamically indexed JSON paths apply JSON-path escaping first and
  then use the same mode-independent SQL literal generator as other text
- the reusable driver, lifecycle, network-fault, operability, transaction, and
  cross-layer observability contracts run on all six active LTS targets
- test-owned certificate authorities and server/client certificates prove
  verified TLS, rejected plaintext, rejected untrusted and name-mismatched
  certificates, password success/failure, engine-default authentication
  plugins, and `REQUIRE X509` client authentication
- bounded pools prove saturation timeout, cancellation, recovery, physical
  connection reuse, session reset, and broken-connection eviction
- a deliberately unreachable first address followed by the live test proxy
  proves ordered multi-host failover through an actual provider query

The fast push lane excludes the three dedicated categories. The scheduled
`container-matrix` workflow selects every active MySQL and MariaDB LTS target
explicitly and runs without a filter. The shared runner persists its selected
targets, filter, exit code, and full-matrix marker so the result remains
auditable instead of being inferred from a green job alone. This scheduled
lane is compatibility evidence; the release manifest imports only the
commit-exact `integration-smoke` result through `repository-qualification`.

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
  - `1110` `MigrationOperationHandlerSelected`
  - `1111` `InvalidMigrationOperationHandlerRegistration`
  - `1112` `MigrationOperationHandlerFailed`
  - `1113` `MigrationOperationHandlerContractViolation`
  - `1114` `UnknownMigrationOperation`
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
- migration-operation handler SPI changes must reconcile the exact-type
  registry, public API, feature projection, package boundary, diagnostics,
  observability contract, and dispatch benchmark in the same PR
- a handler package must prove its options-extension registration, generated
  SQL, engine matrix, recovery behavior, and packed-consumer boundary in its
  own independently authoritative release gate; the provider does not depend
  on or trigger that package's workflow

The repository PR template is the review seam for these obligations. Every
governance and evidence row must declare whether its contract is `unchanged`
or `changed`; leaving the unaffected alternative unchecked is not evidence.
Validation rows use `passed`, `not applicable`, or `pending`. A passed row must
identify its execution evidence, a not-applicable row must explain why the
change cannot exercise that path, and no pending row may remain when the PR is
marked ready for review.

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
  - exactly one impact disposition:
    - code change required
    - reviewed no-op
    - backlog follow-up required
  - explicit `unchanged` or `changed` dispositions for public API, engine
    differences, diagnostics or governance, and supported-engine policy
  - evidence for a reviewed no-op or a target release and issue link for work
- Monthly compatibility review issue:
  - review month
  - reviewed date
  - owner
  - required `qualified`, `follow-up required`, or `not qualified` status for
    MySQL `8.4` and `9.7` plus MariaDB `10.11`, `11.4`, `11.8`, and `12.3`
  - lifecycle change notes for supported engines
  - SLA risk notes
  - resulting actions or explicit no-op

The repository issue forms are the review seam for these outputs. Required
dropdowns make each disposition singular and complete before submission, while
required evidence fields prevent an empty no-op from becoming a review record.
The forms intentionally stay repo-local and do not rely on GitHub organization
labels, automations, or protected metadata outside the repository itself.

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

`eng/release/evidence.py` generates the canonical manifest only after every
release gate has completed. It rejects dirty or mismatched source, mutable
engine image tags, incomplete engine coverage, stale or unexpected packages,
package/symbol version drift, missing SBOM output, and ambiguous dependency
versions. Every retained regular file receives a portable relative path,
SHA-256 digest, byte count, and role. A detached checksum protects the manifest
before the hosted workflow attests it.

Verification enumerates the directory again and fails on changed, missing, or
additional files. Every stage writes into a run-owned location and every hosted
artifact upload is immutable. A restore starts from an empty output directory
and accepts only an exact same-run receipt and digest selection. Final assembly
enumerates the expected evidence tree and rejects every additional file.

### Primary sources

- GitHub, "Use artifact attestations", retrieved 2026-07-31:
  <https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations>
- GitHub, "Re-running workflows and jobs", retrieved 2026-08-04:
  <https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs>
- GitHub, "Workflow artifacts", retrieved 2026-08-04:
  <https://docs.github.com/en/actions/concepts/workflows-and-actions/workflow-artifacts>
- GitHub, "OpenID Connect reference", retrieved 2026-08-04:
  <https://docs.github.com/en/actions/reference/security/oidc>
- GitHub, "OIDC security hardening", retrieved 2026-08-04:
  <https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-cloud-providers>
- GitHub, [`actions/attest`](https://github.com/actions/attest), retrieved
  2026-07-31.
- GitHub, [`actions/setup-dotnet`](https://github.com/actions/setup-dotnet),
  retrieved 2026-08-03.
- GitHub,
  [Dependabot supported ecosystems and repositories](https://docs.github.com/en/code-security/reference/supply-chain-security/supported-ecosystems-and-repositories),
  retrieved 2026-08-03.
- NuGet, [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),
  retrieved 2026-08-12.
- NuGet,
  [PackageReference lock files](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies),
  retrieved 2026-08-12.
- NuGet,
  [`dotnet nuget verify`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-verify),
  retrieved 2026-08-12.
- NuGet,
  [repository signatures](https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource),
  retrieved 2026-08-12.
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
