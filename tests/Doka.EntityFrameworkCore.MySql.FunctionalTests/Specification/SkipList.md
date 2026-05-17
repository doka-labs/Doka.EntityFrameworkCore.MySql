# Specification Suite Skip List

This file is the living catalog of every test in the Microsoft EF Core specification suite that the
provider intentionally does not run, plus the structural reason for each skip.

The disposition discipline is the contract from ADR D-011: every red specification test belongs in
one of three buckets:

1. **fixable in the current change** -- not listed here; gets a code change instead.
2. **fixable in a follow-up change** -- listed here under `## Triage queue` with a reference to
   the tracking issue or work item that owns the follow-up; the `[Fact(Skip = "...")]` attribute
   on the test method carries the same reference so code and catalog stay in sync.
3. **permanent skip** -- listed here under `## Permanent skips` with the structural reason that
   makes the upstream test inapplicable to the MySQL / MariaDB engines (no equivalent SQL feature,
   server-side semantic that diverges from the spec's assumption, etc.).

The `## Quarantine` section catches whole subclasses where more than 10% of the inherited tests
fail at first run; the subclass is removed from the gating CI lane until the failure is triaged
into either the queue or the permanent-skip list.

The format is intentionally machine-readable: each entry is `- TestClass.TestMethodName (engine) -- reason`.
A future enforcement gate may parse the file to ensure every `[Skip = ...]` in code is recorded
here and vice versa.

## Triage queue

The first live-DB run against MySQL 8.4 landed 356 / 408 NorthwindWhereQueryMySqlTest tests
green. The remaining 52 failures cluster into six categories listed below; each category is
held as a single quarantine entry rather than 52 per-test rows so the audit trail stays
scannable. Per-test triage continues in subsequent triage phases as each category gets a
provider-side fix or a documented permanent skip.

- NorthwindWhereQueryMySqlTest (mysql:8.4, 18 tests) -- LINQ expression untranslatable to SQL.
  Pattern: query shapes the provider's translator does not yet support; needs per-test
  investigation, some likely structural for MySQL. Follow-up triage phase.
- NorthwindWhereQueryMySqlTest (mysql:8.4, 16 tests) -- "Expression '@X' in the SQL tree does
  not have a type mapping assigned" for parameterized collections (orderIds, customerIds,
  array, cities). Provider-side SqlExpression type-mapping gap on collection parameters.
  Follow-up triage phase OR provider-side fix.
- NorthwindWhereQueryMySqlTest (mysql:8.4, 10 tests) -- "syntax error near 'bigint)'".
  CLOSED in this iteration: MySqlQuerySqlGenerator.VisitSqlUnary now translates column-level
  StoreType (int / bigint / longtext / etc.) into MySQL CAST-grammar keywords (SIGNED / CHAR /
  BINARY). Some of these tests now run + assert; any that still fail with wrong-value have
  been reclassified to the "Assert.Equal / Assert.Single" categories below.
- NorthwindWhereQueryMySqlTest (mysql:8.4, 4 tests) -- "MySQL doesn't yet support 'LIMIT &
  IN/ALL/ANY/SOME subquery'". Documented MySQL engine limitation; spec tests assume a SQL
  engine that supports LIMIT in subqueries. Likely permanent skip with structural reason,
  pending engine-conditional ConditionalTheory wrapper.
- NorthwindWhereQueryMySqlTest (mysql:8.4, 2 tests) -- "syntax error near 'longtext)'".
  CLOSED in this iteration via the same VisitSqlUnary override (longtext / text / nchar -> CHAR).
- NorthwindWhereQueryMySqlTest (mysql:8.4, 4 tests) -- Assert.Equal failures (actual SQL
  produces wrong result OR expected-SQL-string mismatch). Per-test investigation; small
  population so manageable in a single triage iteration.
- BuiltInDataTypesMySqlTest (mysql:8.4, 11 tests) -- DbUpdateException on save (inner
  exception varies by test); collection of type-mapping + SQL-emission issues that surface
  during INSERT path. Follow-up provider-side triage.
- BuiltInDataTypesMySqlTest (mysql:8.4, 4 tests) -- InvalidCastException casting Enum16
  to nullable Int16. Enum value-conversion gap in the type-mapping pipeline; needs provider
  fix for nested-generic enum-as-nullable-numeric.
- BuiltInDataTypesMySqlTest (mysql:8.4, 4 tests) -- ArgumentException "Argument types do
  not match" on parameter binding; value-conversion contract gap.
- BuiltInDataTypesMySqlTest (mysql:8.4, 4 tests) -- "syntax error near 'int)'".
  CLOSED in this iteration via the same VisitSqlUnary override (int -> SIGNED).
- BuiltInDataTypesMySqlTest (mysql:8.4, 4 tests) -- missing seed tables (AnimalIdentification
  and StringEnclosure). The spec test base seeds these via EnsureCreatedResilientlyAsync,
  but the fixture's OnModelCreating may not include them; per-fixture seed inspection.
- BuiltInDataTypesMySqlTest (mysql:8.4, 2 tests) -- coercion-operator + Sequence-contains-
  no-elements errors; per-test investigation in follow-up.

<!--
Entry shape:
- NorthwindWhereQueryMySqlTest.Where_simple (mysql:8.4) -- tracking-issue or work-item reference; one-line summary.
-->

## Permanent skips

Entries here are gated by ADR D-011 bucket 3: the upstream specification test assumes a behavior,
feature, or history that the MySQL / MariaDB engines (or the Doka provider's design) structurally
do not provide.

- MigrationsMySqlTest.Can_diff_against_2_2_model (mysql:8.4, mariadb:11.8) -- The spec test
  verifies that an EF-Core-2.2-era ModelSnapshot diffs to zero against the current model. Doka's
  first release is on EF Core 10; no prior-version Doka snapshot exists in the wild and any
  hand-fabricated snapshot would only verify symmetry with the fabrication itself. Structural
  inapplicability per ADR D-011.
- MigrationsMySqlTest.Can_diff_against_2_1_ASP_NET_Identity_model (mysql:8.4, mariadb:11.8) --
  same structural reason as the 2.2 entry above; no prior-version Doka snapshot of the
  ASP.NET Identity 2.1 model exists.
- MigrationsMySqlTest.Can_diff_against_2_2_ASP_NET_Identity_model (mysql:8.4, mariadb:11.8) --
  same structural reason; no prior-version Doka snapshot of the ASP.NET Identity 2.2 model
  exists.
- MigrationsMySqlTest.Can_diff_against_3_0_ASP_NET_Identity_model (mysql:8.4, mariadb:11.8) --
  same structural reason; no prior-version Doka snapshot of the ASP.NET Identity 3.0 model
  exists.
- UpdatesMySqlTest.Identifiers_are_generated_correctly (mysql:8.4, mariadb:11.8) -- the spec
  asserts that the deliberately-long entity-type name flows through the identifier pipeline
  UNTRUNCATED into table / key / constraint / index names. Doka's MySqlModelValidator rejects
  any FK or index name above MySQL's 64-character limit at model-build time rather than
  silently truncating; the design choice favors explicit error over silent name collision.
  Engine-aware design divergence per ADR D-011.

<!--
Entry shape:
- BuiltInDataTypesMySqlTest.Can_perform_query_with_max_length (mariadb:11.8) -- MariaDB rejects
  TEXT columns in primary keys without an explicit prefix length; the spec test assumes an engine
  that accepts implicit-length text keys. ADR D-NNN.
-->

## Quarantine

No quarantined subclasses yet.

<!--
Entry shape:
- BuiltInDataTypesMySqlTest (mariadb:11.8) -- 18 of 142 tests fail; primary-key length contract
  divergence is the root cause; tracked under a separate work item for per-engine subclass split.
-->
