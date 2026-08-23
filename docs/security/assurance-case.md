# Security Assurance Case

This assurance case explains why the provider's published security
requirements are expected to hold. It connects each claim to its trust
boundary, design argument, implementation controls, and executable evidence.
It does not claim that the provider or its dependencies can never contain a
vulnerability.

## Scope and Method

The security requirements are defined in `SECURITY.md`. The threat model owns
assets, trust boundaries, attacker stories, assumptions, severity, and
re-evaluation triggers. This document adds the argument-and-evidence layer
required to justify those requirements.

Each case uses four parts:

- **Claim:** the security property users may rely on;
- **Argument:** why the architecture is intended to preserve it;
- **Controls:** the implementation mechanisms that enforce the argument; and
- **Evidence:** tests or machine-readable contracts that fail when the control
  changes.

Evidence establishes the behavior exercised by the cited checks. It does not
remove the need for review when a trust boundary changes.

## Case 1: Ordinary Values Do Not Become SQL Grammar

**Claim.** Ordinary query and entity values remain parameterized.

**Argument.** EF Core query translation and modification commands retain data
as SQL parameters. Provider extensions that introduce grammar accept bounded
enums or validated tokens rather than treating arbitrary strings as SQL.
Intentional raw-SQL APIs remain an explicit application-owned boundary.

**Controls.** Query translators create SQL expression parameters; update and
migration generators use relational type mappings; collation, charset,
storage-engine, and similar tokens pass the shared SQL token validator.

**Common weaknesses countered.** SQL injection, unsafe string concatenation,
grammar-token smuggling, and accidental client evaluation of a predicate.

**Evidence.** The
[SQL token tests](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Storage/MySqlSqlTokenValidatorTests.cs),
[defensive validation tests](../../tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Infrastructure/MySqlDefensiveValidationTests.cs),
[query translation tests](../../tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Query/MySqlQueryTranslationCoverageTests.cs),
and [live query tests](../../tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Query/MySqlQueryTranslationIntegrationTests.cs)
exercise accepted and rejected inputs. `CONTRIBUTING.md` owns the `EF1002`
enforcement and raw-SQL test convention; `docs/query-functions.md` owns the
public translation contract.

## Case 2: Identifiers and Literals Use Their Owning Encoders

**Claim.** Identifiers and literals cannot escape their syntactic context.

**Argument.** Identifier delimiting, SQL literals, JSON and spatial paths, and
generated C# strings are different languages and are not interchangeable. Each
value is encoded by the component that owns its target syntax.

**Controls.** The relational SQL generation helper delimits identifiers;
relational type mappings generate SQL literals; JSON and spatial paths use
their dedicated escaping rules; generated source uses EF Core C# helpers.

**Common weaknesses countered.** Identifier injection, malformed string
literals, JSON-path injection, source-code injection, and double encoding.

**Evidence.** Positive and hostile-input tests cover
[column defaults](../../tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Migrations/MySqlColumnDefaultSqlGenerationTests.cs),
[GUID formats](../../tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Storage/MySqlGuidFormatTests.cs),
[JSON literals](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Storage/MySqlJsonLiteralEscapeTests.cs),
and [spatial inputs](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Spatial/MySqlSpatialInputGuardTests.cs).
Generated-context compilation and execution is exercised by the
[scaffolding round-trip suite](../../tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Scaffolding/MySqlScaffoldingRoundTripTests.cs).

## Case 3: Hostile Metadata Does Not Become Executable Source

**Claim.** Database-controlled metadata cannot silently become executable SQL
or generated C#.

**Argument.** Reverse engineering crosses an explicit untrusted-data boundary.
Metadata remains structured database-model data until an EF Core naming or
literal service encodes it. Generated output remains a reviewable build
artifact rather than being executed by the provider.

**Controls.** Parameterized catalog filters, bounded metadata parsing,
identifier generation, C# literal encoding, exact generated-source
compilation, and no execution of database comments or defaults during
scaffolding.

**Common weaknesses countered.** Second-order injection, generated-code
injection, malformed identifier attacks, parser confusion, and unsafe metadata
logging.

**Evidence.** The
[database-model factory tests](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Scaffolding/MySqlDatabaseModelFactoryTests.cs),
[scaffolding scale tests](../../tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Scaffolding/MySqlScaffoldingScaleTests.cs),
and [round-trip suite](../../tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Scaffolding/MySqlScaffoldingRoundTripTests.cs)
cover bounded parsing, hostile metadata, generated-context compilation, and
all-target live scaffolding.

## Case 4: Telemetry Does Not Export Sensitive Payloads

**Claim.** Provider-owned logs, traces, and metrics exclude credentials, SQL,
raw database or object names, exception messages, stack traces, and exception
objects.

**Argument.** Telemetry crosses into sinks with separate access and retention.
The provider therefore emits stable identifiers and bounded classification
values rather than application or database payloads.

**Controls.** Central event identifiers, bounded reason enums, pseudonymous
scope identifiers, fixed metric tags, message templates without exception
payloads, and provider exceptions that separate public detail from telemetry.

**Common weaknesses countered.** Credential disclosure, customer-data leakage,
log injection, high-cardinality denial of service, and secret-bearing
exception serialization.

**Evidence.** The
[diagnostics governance tests](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Diagnostics/MySqlDiagnosticsGovernanceTests.cs),
[logger-message tests](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Diagnostics/MySqlLoggerMessagesTests.cs),
and [observability contract tests](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Diagnostics/MySqlObservabilityContractTests.cs)
bind the implementation to
[`observability-contract.json`](../operations/observability-contract.json).

## Case 5: Failure Handling Does Not Duplicate Unsafe Work

**Claim.** Retry, cancellation, connection, transaction, and migration-lock
behavior cannot silently duplicate unsafe operations or return poisoned state
to a pool.

**Argument.** Only classified transient failures are retried. Cancellation and
commit-unknown outcomes remain terminal unless an operation has an explicit
idempotency contract. Schema changes are serialized through a dedicated lock
connection whose lifecycle is independent of pooled application commands.

**Controls.** Central transient-error classification, execution-strategy
state, savepoints, dedicated advisory-lock connections, structured migration
commands with guaranteed cleanup, and explicit cancellation propagation.

**Common weaknesses countered.** Duplicate writes, lost cancellation, leaked
session state, stale pooled connections, overlapping migrations, and retained
advisory locks.

**Evidence.** The
[execution-strategy tests](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Resilience/MySqlExecutionStrategyTests.cs),
[scoped migration-command tests](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Migrations/MySqlScopedMigrationCommandTests.cs),
[advisory-lock stress tests](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Migrations/MySqlAdvisoryLockLifecycleStressTests.cs),
and [live pool/failover contract](../../tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Infrastructure/MySqlPoolAndFailoverContractTests.cs)
cover the bounded algorithms and database-visible lifecycle. Runtime-posture
and migration-workflow release gates cover built artifacts.

## Case 6: Released Evidence Represents the Released Bytes

**Claim.** CI and release evidence remains bound to the source revision and
package bytes it represents.

**Argument.** Qualification happens before irreversible publication and emits
content-addressed evidence. A signed tag, protected source check, candidate
manifest, package digests, SLSA provenance, GitHub release assets, NuGet
repository signatures, and public readback must converge on one identity.

**Controls.** Commit-exact protected-check lookup, signed annotated tags,
immutable GitHub releases, short-lived NuGet credentials, digest-bound
manifests, exact asset reconciliation, portable SLSA bundles, isolated package
consumer execution, and fail-closed duplicate handling.

**Common weaknesses countered.** Artifact substitution, stale evidence reuse,
tag movement, workflow impersonation, partial publication ambiguity, mutable
release assets, and a dependency-confusion restore during consumer testing.

**Evidence.** The
[release workflow contracts](../../tests/Doka.EntityFrameworkCore.MySql.Tests/Contracts/AdrRepositoryValidatorTests.cs),
[release trust tests](../../eng/tests/test_release_trust.py),
[provenance tests](../../eng/tests/test_release_provenance.py),
[publication tests](../../eng/tests/test_nuget_publication.py), and
[package-only consumer tests](../../eng/tests/test_local_package_consumer.py)
exercise the trust chain. Candidate manifests, public readback, SBOMs,
checksums, and the [public verification procedure](release-verification.md)
provide per-release evidence.

## Residual Risk and Ownership

The provider cannot guarantee security properties owned by the consuming
application, MySqlConnector, EF Core, the database server, GitHub, NuGet.org,
or the operator's network and credential policy. Those assumptions and routing
rules are explicit in the threat model and `SECURITY.md`.

A failure of one control does not authorize weakening another. For example, a
valid checksum does not replace signer identity, and an attestation does not
replace package signature or runtime behavior.

## Review and Re-evaluation

Review this assurance case together with the threat model whenever a trigger
listed there fires. A review must:

1. confirm that each claim still matches `SECURITY.md`;
2. trace any new trust boundary through the relevant argument;
3. identify the tests or evidence that exercise every new control;
4. record accepted residual risk through an ADR or security review; and
5. update or remove claims whose evidence no longer exists.

The current document does not by itself satisfy OpenSSF Gold's separate
requirement for a performed human security review. That criterion becomes
claimable only when a dated review records its reviewer, scope, findings, and
resolution.

## Related Documentation

- [Security policy](../../SECURITY.md)
- [Threat model](threat-model.md)
- [Release verification](release-verification.md)
- [Provider architecture](../architecture.md)
- [Release governance](../release-governance.md)

## Primary Source

- OpenSSF Best Practices, [Silver assurance-case criterion](https://www.bestpractices.dev/en/criteria/1),
  retrieved 2026-08-21. The criterion requires a threat model, explicit trust
  boundaries, an argument for secure design principles, and an argument that
  common implementation weaknesses are countered.
