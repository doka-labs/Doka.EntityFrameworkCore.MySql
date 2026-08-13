# Release Governance

`Doka.EntityFrameworkCore.MySql` treats release hardening as a reproducible
engineering contract. Commit-exact CI and the tag-triggered qualification
workflow create the evidence. Hosted assembly freezes the selected identities
and payload digests before GitHub attests the candidate.

This document freezes the reviewable governance baseline for:

- regression expectations
- diagnostics categories and `MySqlEventId` ranges
- verification and retained-evidence paths
- PR review obligations tied to the planning contract

The canonical operator sequence from a reviewed, green `main` commit through
signed tagging, hosted qualification, NuGet readback, and immutable GitHub
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
    - `DOKA_BENCHMARK_TARGET=mysql84 ./eng/benchmark.sh --test-only`
    - `DOKA_BENCHMARK_TARGET=mariadb118 ./eng/benchmark.sh --test-only`
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
- Hosted release candidate:
  - workflow: `.github/workflows/release-candidate.yml`
  - cadence: automatic on a `v*` tag push; manual dispatch is diagnostic and
    cannot qualify an untagged source
  - pre-tag lookup: `./eng/pre-tag-check.sh` verifies, without allocating a
    runner or mutating state, that the exact clean commit is reachable from
    protected `main`, signable by a registered key, and already carries a
    successful `repository-qualification` run from a `main` push
  - trust root: the workflow independently verifies the annotated tag through
    GitHub and the checked-in allowed-signers policy, binds it to the checked-out
    commit and protected `main`, and resolves the branch qualification before
    any expensive job starts
  - imported branch gate: `repository-qualification` aggregates the
    commit-exact quality, repository-test, specification, integration-smoke,
    and coverage gates; the tag imports that one API-bound result instead of
    rerunning those implementations
  - tag-produced gates: migration deployment, runtime posture, the EF Core and
    MySqlConnector patch matrices, and paired performance all execute against
    the tagged commit; packing and SBOM generation produce the payload they
    qualify
  - paired performance: each supported release engine alternates the reference
    and candidate providers on one allocated runner. Statistical intervals,
    multiple-comparison control, absolute ceilings, allocation and collection
    limits, and sustained-use invariants are decided from retained raw evidence;
    no historical baseline or processor match qualifies a tag
  - retry boundary: only `measurement-inconclusive` and
    `environment-not-comparable` authorize one fresh attempt. Functional,
    budget, contract, and infrastructure failures remain conclusive
  - stage contract: assembly requires exactly six tag-produced stage receipts
    -- migration deployment, runtime posture, both patch matrices, package, and
    SBOM -- plus the selected paired artifacts and imported branch result
  - stable identity and controlled resume: the candidate root is keyed to
    `github.run_id`; a rerun may select only checksum-verified artifacts from
    that same run and no later than the assembling attempt
  - package-consumer boundary: the package stage restores the exact candidate
    `.nupkg` bytes into an empty cache and builds the public runtime consumer
    outside the repository without a project reference. Its conformance suite
    exercises both handler and provider registration orders, exact and unknown
    operation dispatch, provider baseline rendering, command boundaries,
    context expiry, and duplicate handler-ID and operation-type failures before
    binding both restored package hashes
  - dependency closure: both shipped projects carry reviewed
    `packages.lock.json` files, and the package stage restores them in locked
    mode before candidate bytes exist. These locks bind the release build's
    direct and transitive graph; they do not narrow the dependency ranges
    advertised by the library packages to downstream consumers. The shared
    repository qualification gate performs the same locked restore before its
    ordinary solution restore, so a stale lock fails on the originating pull
    request rather than on a later release tag
  - artifact restoration: resolver jobs bind artifact ID, name, attempt, and
    digest before traversal- and symlink-safe extraction; missing, expired,
    ambiguous, future-attempt, mismatched, or conflicting artifacts fail closed
  - reconciliation: `eng/release/evidence-policy.json` defines the consumed
    gate catalog and selection order. Assembly selects each gate once, writes
    the canonical qualification manifest, and later steps verify that frozen
    choice without reselecting
  - least privilege: the workflow defaults to `contents: read`; only artifact
    resolvers receive `actions: read`, and only the final attestation job
    receives `id-token: write`, `attestations: write`, and artifact-metadata
    write permission
  - retained evidence:
    - `artifacts/release-candidate/<run-id>/release-qualification-manifest.json`
    - `artifacts/release-candidate/<run-id>/release-gate-results.json`
    - `artifacts/release-candidate/<run-id>/release-candidate-summary.md`
    - `artifacts/release-candidate/<run-id>/release-candidate-reconciliation.json`
    - `artifacts/release-candidate/<run-id>/release-candidate-evidence.json`
    - `artifacts/release-candidate/<run-id>/release-candidate-evidence.sha256`
    - `artifacts/release-candidate/<run-id>/packages/...`
    - `artifacts/release-candidate/<run-id>/local-package-consumer/...`
    - `artifacts/release-candidate/<run-id>/sbom/...`
    - `artifacts/release-candidate/<run-id>/migration-deployment/...`
    - `artifacts/release-candidate/<run-id>/runtime/...`
    - `artifacts/release-candidate/<run-id>/efcore-patch-matrix/...`
    - `artifacts/release-candidate/<run-id>/mysqlconnector-patch-matrix/...`
    - `artifacts/release-candidate/<run-id>/performance/...`
    - `artifacts/release-candidate-checkpoints/<run-id>/...`
- Manual NuGet publication and public readback:
  - workflow: `.github/workflows/nuget-publish.yml`
  - cadence: manually dispatched from trusted `main` after one successful
    release-candidate run for the exact current commit and release tag
  - explicit inputs: candidate workflow run ID, semantic release tag, and the
    literal confirmation `publish <release-tag>`
  - validation boundary: `validate-candidate` downloads one exact
    attempt-qualified candidate artifact, verifies its workflow, repository,
    tag, commit, manifest, packages, attestations, and current public NuGet
    state, then emits immutable validation evidence
  - publication boundary: only `publish` enters the `nuget` environment and
    receives `id-token: write`; it repeats the authoritative remote-state
    preflight immediately before OIDC exchange and package push
  - readback boundary: `readback` has no environment, OIDC, or repository-write
    permission; it proves package, symbol, restore, and runtime behavior from
    the public endpoints and emits the complete publication receipt set
  - finalization boundary: `finalize-github-release` receives only
    `contents: write`, consumes the exact successful readback artifact, and
    cannot publish a release before every public readback contract passes
  - environment: `nuget`, restricted to the `main` branch
  - credential: a NuGet.org short-lived API key exchanged from GitHub OIDC
    immediately before the first push; no persistent NuGet API key is stored
  - signing model: candidate packages are unsigned before ingestion. GitHub
    provenance and Trusted Publishing bind their build and publisher identity;
    NuGet.org adds the repository signature, whose presence and cryptographic
    validity are required during public readback. Author signing would require
    a separately approved certificate-custody and rotation contract and is not
    silently simulated with a long-lived PFX secret
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
    package comparison, cryptographic verification of every downloaded NuGet
    repository signature, Portable PDB retrieval using the candidate DLL's
    SSQP key and SHA-256 checksum, empty-cache restore from NuGet.org only, and
    execution of the basic and spatial compiled-model runtime contract against
    the candidate's pinned MySQL 8.4 image
  - retained evidence:
    - `validated-candidate.json`
    - `publication-preflight.json`
    - `symbol-readback-manifest.json`
    - `nuget-publication-readback.json`
    - `nuget-signature-verification.txt`
    - `consumer-runtime-readback.json`
    - downloaded public package and Portable PDB payloads
  - retained hosted artifacts:
    - `nuget-validation-evidence-attempt-<attempt>`
    - `nuget-publish-evidence-attempt-<attempt>`
    - `nuget-readback-evidence-attempt-<attempt>`
    - `github-release-evidence-<release-tag>-attempt-<attempt>`
  - GitHub release finalization: a separate dependent job receives the
    workflow's only `contents: write` permission after every NuGet public
    readback passes; it has no OIDC or attestation permission
  - tag authority: local and remote annotated tags must both resolve to the
    exact published source commit; the finalizer cannot create, move, or
    replace tags
  - release assembly: exact `CHANGELOG.md` notes and checksum-bound packages,
    symbols, SBOMs, candidate evidence, and publication evidence are staged in
    a draft, independently downloaded and hashed, then published
  - retry policy: matching partial drafts resume with missing assets only;
    matching immutable releases are idempotent; unexpected metadata, assets,
    payloads, or release classification fail without clobbering remote state
  - classification: prerelease versions are GitHub prereleases and are not
    `latest`; stable versions must be non-prerelease and `latest`
  - retained GitHub release evidence:
    - `github-release-plan.json`
    - `github-release-readback.json`
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
