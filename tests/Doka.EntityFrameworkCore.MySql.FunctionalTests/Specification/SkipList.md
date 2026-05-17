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

(closed 2026-05-17 via two dispositions:
- 18 anonymous-type / Tuple equality tests (`Where_compare_constructed_*`,
  `Where_compare_tuple_constructed_*`, `Where_compare_tuple_create_constructed_*`,
  9 methods x 2 async): mirror SqlServer's `AssertTranslationFailed(...)` override.
  The base spec test asserts a per-field rewrite of `new { x = c.City } == new { x = "London" }`,
  but EF Core 10's `RelationalSqlTranslatingExpressionVisitor.TryRewriteStructuralTypeEquality`
  covers only `IEntityType` / `IComplexType` / `IComplexProperty` operands and falls through
  to "not translated" for anonymous-type / Tuple. The behavior is engine-uniform; every
  relational provider (SqlServer, Sqlite, PostgreSQL) overrides the test to assert the
  documented translation failure. See `dotnet/efcore` issue 14672 for upstream tracking.
- 4 string-Add result-mismatch tests (`Using_same_parameter_twice_*`,
  `EF_Constant_does_not_parameterized_as_part_of_bigger_subtree_*`, 2 methods x 2 async):
  root-cause was a missing string-concat override on `MySqlQuerySqlGenerator.VisitSqlBinary`.
  The base generator emits `left + right` for every `SqlBinaryExpression{OperatorType: Add}`,
  but MySQL's `+` is arithmetic addition; `'10' + 'ALFKI' + '10'` evaluates to `20`, not
  `'10ALFKI10'`. Override now emits `CONCAT(left, right)` when the binary's CLR Type is
  `string`. Nested chains of string-Adds produce nested CONCATs which MySQL evaluates with
  the documented concatenation semantics.)
(closed 2026-05-17: root cause was an explicit `FindCollectionMapping(...) => null`
override on `MySqlTypeMappingSource` that blocked EF Core's collection-mapping resolution
path; without a collection mapping the `InExpression.ValuesParameter` produced for
`list.Contains(x.Id)` queries never received an `ElementTypeMapping`, and the
`RelationalTypeMappingPostprocessor` rejected the parameter with
`NullTypeMappingInSqlTree`. Removing the override lets the base implementation build
a JSON-collection-string mapping with the element mapping wired through to
`SqlNullabilityProcessor.ProcessInExpressionValues`, which expands the parameter into
inlined SQL constants per `ParameterTranslationMode.MultipleParameters` (the default).
Closed 16 NorthwindWhereQueryMySqlTest failures across `@orderIds`, `@customerIds`,
`@array`, `@cities`, and `@entity_equality_customer_Orders_OrderID` parameter cases.)
- NorthwindWhereQueryMySqlTest (mysql:8.4, 10 tests) -- "syntax error near 'bigint)'".
  CLOSED in this iteration: MySqlQuerySqlGenerator.VisitSqlUnary now translates column-level
  StoreType (int / bigint / longtext / etc.) into MySQL CAST-grammar keywords (SIGNED / CHAR /
  BINARY). Some of these tests now run + assert; any that still fail with wrong-value have
  been reclassified to the "Assert.Equal / Assert.Single" categories below.
(moved to Permanent skips below: "LIMIT & IN/ALL/ANY/SOME subquery" -- validated against
both MySQL 8.4 and MariaDB 11.8.)
- NorthwindWhereQueryMySqlTest (mysql:8.4, 2 tests) -- "syntax error near 'longtext)'".
  CLOSED in this iteration via the same VisitSqlUnary override (longtext / text / nchar -> CHAR).
(closed 2026-05-17 via the string-Add CONCAT override on `MySqlQuerySqlGenerator.VisitSqlBinary`
 -- the per-test investigation showed both Assert.Equal failures collapsed to the same
 root cause as the 4 string-Add result-mismatch tests above.)
(closed 2026-05-17 via two root-cause fixes:
- The `MySqlTypeMappingSource.CreateEnumMapping` short-circuit returned a raw numeric
  mapping for enum CLR types without an `EnumToNumberConverter`, so EF Core's seed-write
  path emitted the unquoted enum name literal (`Enum16.SomeValue` -> `SomeValue`) which
  failed with "Unknown column 'SomeValue' in 'field list'". Removing the short-circuit
  lets the base `RelationalTypeMappingSource.WithConverter` loop attach the conventional
  `EnumToNumberConverter<TEnum, TUnderlying>` and emit numeric literals (e.g. `1`).
- The default `GuidTypeMapping("binary(16)", DbType.Guid)` emitted Guid literals as the
  38-char string `'00000000-0000-0000-0000-000000000000'` (incompatible with `binary(16)`,
  "Data too long for column"). The new `MySqlGuidBinaryTypeMapping` wires a
  `GuidToBytesConverter` for parameter binding AND emits `X'HEX16'` literals so seed
  inserts land in the binary column. Closed the 11 DbUpdateException-on-save tests, the
  4 InvalidCastException-Enum16-to-Nullable&lt;Int16&gt; tests, the 4 ArgumentException
  parameter-binding tests, plus 4 secondary Assert.Single-empty cases that cascaded
  from seed-failure. BuiltInDataTypesMySqlTest tally: 1/30 -> 24/30. The remaining 6
  Duplicate-PK failures (Ids 11, 12, 100, 'Gumball!', 799) deferred to a follow-up
  triage iteration: a deep-dive surfaced a separate latent bug --
  `MySqlValueGenerationConvention.ApplyValueGenerationStrategy` unconditionally
  overrode user-set `ValueGenerated.Never` with `OnAdd` for integer primary keys
  (every `eb.Property(e => e.Id).ValueGeneratedNever()` was silently flipped back
  to AUTO_INCREMENT) -- which is now fixed at the convention layer. Even with the
  schema correctly emitting `Id int NOT NULL` (no AUTO_INCREMENT) the 6 Duplicate-PK
  tests still fail. General-log + IDbCommandInterceptor trace shows ONLY ONE INSERT
  per test (no second statement that would actually duplicate), the row lands in
  the DB successfully, yet EF Core's `ReaderModificationCommandBatch.ConsumeAsync`
  raises `MySqlException : Duplicate entry '...'` on the response read. No
  CommandFailed interceptor event fires. The failure is below the IDbCommandInterceptor
  surface -- likely in MySqlConnector's multi-statement batch protocol where a stale
  error packet from the preceding multi-INSERT seed batch leaks into the next batch's
  reader. Follow-up investigation needs MySqlConnector debug logging + protocol-level
  packet capture; defer.)
- BuiltInDataTypesMySqlTest (mysql:8.4, 4 tests) -- "syntax error near 'int)'".
  CLOSED in this iteration via the same VisitSqlUnary override (int -> SIGNED).
(closed 2026-05-17: root cause was NOT missing entity registration but a CREATE TABLE
failure on MaxLengthDataTypes whose varchar(9000) + varbinary(9000) columns exceeded MySQL's
65535-byte row-size limit under utf8mb4. EF Core's EnsureCreatedAsync emits tables in
declaration order and throws on the first failure; every subsequent table (AnimalIdentification,
StringEnclosure, MaxLengthDataTypes itself, UnicodeDataTypes, ObjectBackedDataTypes,
NullableBackedDataTypes, NonNullableBackedDataTypes, BinaryForeignKeyDataType,
StringKeyDataType, StringForeignKeyDataType, AnimalDetails) was silently skipped. The
fixture-level fix maps String9000 / StringUnbounded / ByteArray9000 to longtext / longblob
column types which store off-row and bypass the row-size limit. The MySQL general-log
diagnostic that surfaced the SQL "CREATE TABLE MaxLengthDataTypes ... varchar(9000) ... ;
rollback" was decisive in identifying the cutoff point.)
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
- NorthwindWhereQueryMySqlTest.Where_multiple_contains_in_subquery_with_or +
  Where_multiple_contains_in_subquery_with_and (mysql:8.4, mariadb:11.8, 4 test invocations
  total counting async true/false) -- "LIMIT & IN/ALL/ANY/SOME subquery" is structurally
  rejected by both engines (ERROR 1235, SQLSTATE 42000). Primary-source documentation:
  MySQL 8.4 Reference Manual section "Subquery Restrictions"
  (https://dev.mysql.com/doc/refman/8.4/en/subquery-restrictions.html, retrieved 2026-05-17)
  carries the verbatim example "SELECT * FROM t1 WHERE s1 IN (SELECT s2 FROM t2 ORDER BY
  s1 LIMIT 1)" producing ERROR 1235; MariaDB Server Reference "Subquery Limitations"
  (https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/subqueries/subquery-limitations,
  retrieved 2026-05-17) carries the same error 1235 (42000). Empirical probe 2026-05-17
  against doka-mysql84 + doka-mariadb118 docker containers confirmed both reject the pattern
  with and without ORDER BY. The spec tests assume an engine that supports the construct
  (SQL Server, PostgreSQL, SQLite); structural inapplicability per ADR D-011 bucket 3.
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
