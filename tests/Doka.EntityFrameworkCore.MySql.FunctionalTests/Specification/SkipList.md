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

No queue entries yet; this section populates after the first specification run lands on a live
MySQL or MariaDB target.

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
