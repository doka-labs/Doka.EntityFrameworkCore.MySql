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

No permanent skips yet; entries land here only after a structural-reason ADR justifies them.

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
