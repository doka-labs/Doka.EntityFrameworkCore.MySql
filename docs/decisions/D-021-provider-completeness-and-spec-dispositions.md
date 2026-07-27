# D-021 -- Provider Completeness and Specification Dispositions

- **Status:** Implemented
- **Date:** 2026-07-27
- **Scope:** EF Core relational specification coverage on MySQL 8.4, MariaDB 11.4, and MariaDB 11.8
- **Machine-readable contract:** `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/SpecDispositions.json`

## Context

The provider is intended to become a primary production provider after publication. A passing
test count is not sufficient evidence for that role when an inherited specification method can
return without invoking the base assertion, or when a provider gap is labeled as an engine
limitation without vendor evidence.

The previous `SkipList.md` process was descriptive rather than enforcing. It allowed three
failure modes:

1. an engine-conditional method could return `Task.CompletedTask` and be reported as passed;
2. a skip could exist without a stable identifier or machine-readable source record;
3. an engine restriction could cause the provider to skip behavior even when a
   semantics-preserving SQL rewrite was available.

## Decision

The specification suite has a zero provider-gap budget.

Every applicable inherited relational behavior must execute on every supported target. A test
may be skipped only when it belongs to one of these three classifications:

- `engine-limitation`: the database engine cannot express the required server-side relational
  operation. The record must contain an official vendor source, retrieval date, reproducible
  probe, provider-workaround assessment, affected targets, and re-evaluation trigger.
- `framework-limitation`: the consumed EF Core version skips the same inherited specification
  shape and fails in framework-owned translation or semantics before provider SQL generation
  can solve it. The record must contain the upstream `dotnet/efcore` issue, retrieval date,
  reproducible probe on every supported target, framework-boundary assessment, and
  re-evaluation trigger.
- `not-applicable`: the premise of the upstream test does not exist for this provider. This is
  limited to structural cases such as compatibility with provider snapshots from before the
  provider existed.

`provider-gap` is never an accepted active disposition. If a specification failure can be
addressed in provider translation, metadata conventions, migrations, type mapping, or test
infrastructure, the provider is changed and the inherited assertion continues to run.

## Executable skip contract

Engine limitations use `SpecEngineLimitationTheoryAttribute`. The attribute:

- evaluates the exact `DOKA_SPEC_TEST_TARGET` during xUnit discovery;
- produces a visible skipped test case rather than a successful no-op;
- includes the stable ledger ID in the skip reason;
- can be deliberately bypassed with `DOKA_SPEC_TEST_PROBE_ENGINE_LIMITS=true` to reproduce the
  documented engine failure without editing source.

Upstream framework limitations use `SpecFrameworkLimitationTheoryAttribute`. It uses direct
`InlineData` discovery, emits a visible skip linked to the ledger ID, and can be bypassed with
`DOKA_SPEC_TEST_PROBE_FRAMEWORK_LIMITS=true`. The provider-specific override still invokes the
inherited assertion, so removing the attribute after an EF Core update immediately restores
the original specification test.

Historical not-applicable tests keep an explicit xUnit skip, but their reason includes the
stable disposition ID.

The contract test reconciles source annotations against
`Specification/SpecDispositions.json`. CI and release-candidate paths must fail when:

- an executable skip is absent from the ledger;
- a ledger test method has no matching source annotation;
- an engine or framework limitation lacks official primary-source evidence or a retrieval date;
- a framework limitation is not reproduced on every supported target;
- an active provider-gap entry exists;
- a silent-pass pattern is reintroduced.

## Active engine limitations

### MDB-CORRELATED-DERIVED-TABLE

MariaDB documents that a subquery in the `FROM` clause cannot be correlated. Its complete JOIN
grammar for the supported 11.4 and 11.8 lines contains no `LATERAL` production. The affected
JSON collection shapes require per-outer-row composition after operations such as filtering,
ordering, pagination, or nested collection shaping. MariaDB can correlate a direct
`JSON_TABLE` invocation with a preceding table, and the provider uses that capability where
possible, but it cannot preserve that correlation through the required derived-table boundary.

Primary sources, retrieved 2026-07-27:

- MariaDB, "Subquery Limitations":
  https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/subqueries/subquery-limitations
- MariaDB, "JOIN Syntax":
  https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/joins/join-syntax

### MDB-JSON-TABLE-SUBDOCUMENT

MariaDB documents that `JSON_TABLE` cannot extract a JSON subdocument into a JSON result column.
The two affected custom-naming projections require the complete owned JSON value for EF Core
materialization. Scalar-by-scalar extraction does not preserve an arbitrary nested owned graph.

Primary source, retrieved 2026-07-27:

- MariaDB, "JSON_TABLE":
  https://mariadb.com/docs/server/reference/sql-functions/special-functions/json-functions/json_table

The exact methods, probe outcomes, target set, and re-evaluation predicates live in the
machine-readable ledger and are intentionally not duplicated here.

## Active upstream EF Core limitations

Six inherited JSON shapes remain skipped by the consumed EF Core version itself. Doka exposes
them as explicit framework dispositions rather than silently inheriting the upstream skip. Each
issue is an official `dotnet/efcore` primary source, retrieved 2026-07-27:

- EFCORE-31397, JSON collection anonymous projection with `Distinct`:
  https://github.com/dotnet/efcore/issues/31397
- EFCORE-29287, grouping and ordering by a JSON scalar before `FirstOrDefault`:
  https://github.com/dotnet/efcore/issues/29287
- EFCORE-28733, JSON entity projection and entity comparison after `FirstOrDefault`:
  https://github.com/dotnet/efcore/issues/28733
- EFCORE-28645, backtracking from a JSON entity to its parent:
  https://github.com/dotnet/efcore/issues/28645
- EFCORE-24263, nested collection projection after a second query pushdown:
  https://github.com/dotnet/efcore/issues/24263
- EFCORE-29416, null semantics for a nullable property converter that handles nulls:
  https://github.com/dotnet/efcore/issues/29416

The adjacent single-pushdown JSON projection was also skipped upstream, but passes when activated
on Doka and therefore remains a normal executable specification test. Framework dispositions
are accepted only when the probe fails before a provider-owned SQL tree can provide the missing
behavior.

## Provider workarounds chosen over skips

Three discovered engine constraints remain fully supported by the provider:

- MySQL and MariaDB reject `LIMIT` directly inside an `IN` subquery.
  `MySqlQuerySqlGenerator.GenerateIn` places the limited query behind a derived-table boundary.
- MySQL 8.4 optimizer bug 114897 returns an incorrect result for the affected
  `EXISTS(JSON_TABLE(...))` shape. `GenerateExists` emits an equivalent limited scalar
  subquery and avoids the faulty semijoin transformation.
- MySQL and MariaDB cap the relevant schema identifiers at 64 characters.
  `MySqlConventionSetBuilder` registers EF Core's maximum-identifier convention so names are
  truncated deterministically with collision suffixes before DDL generation.

The ledger retains the vendor sources and test evidence for these resolved restrictions. They
are not skips.

## Consequences

### Positive

- Test reports distinguish a real assertion pass from a documented engine skip.
- Every database limitation is reviewable without relying on agent memory or prose alone.
- Upstream framework skips are visible, source-backed, and routinely re-probed.
- Provider-side workarounds remain the default response to an engine syntax restriction.
- A release cannot quietly accumulate provider gaps.

### Negative

- Supported-target additions require explicit ledger and probe updates.
- Vendor documentation drift requires periodic source review.
- Some skipped theories appear as one skipped method-level case rather than one case per
  `InlineData` row because xUnit applies the skip during discovery.

## Re-evaluation triggers

- A supported MariaDB release adds correlated `FROM` subqueries or a LATERAL/APPLY join form.
- MariaDB adds JSON-subdocument result columns to `JSON_TABLE`.
- The supported MySQL floor reaches 9.3 or newer; bug 114897 is fixed there, but the provider
  rewrite remains valid and should be benchmarked before removal.
- EF Core changes specification method names or discovery semantics.
- Any linked `dotnet/efcore` issue is fixed or closed with a usable framework release.
- A new supported engine target is introduced.

## Superseded process

This decision replaces the descriptive skip-catalog mechanism in D-011 with the executable,
machine-readable disposition contract. D-011 remains the decision to adopt the Microsoft
specification corpus; D-021 governs how exceptions are classified and enforced.
