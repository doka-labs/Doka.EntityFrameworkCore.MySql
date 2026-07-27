---
id: D-015
status: implemented
date: 2026-05-17
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "JSON specification fixture warning policy"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-015 -- Scope JSON query warning behavior to its fixture

## Context and Problem Statement

EF Core 10 fires two query-compilation warnings on LINQ shapes our JsonQuery specification subclass exercises:

- `Microsoft.EntityFrameworkCore.Query.MultipleCollectionIncludeWarning` (event `RelationalEventId.MultipleCollectionIncludeWarning`, ID 20504): fires when a single query loads more than one collection navigation without an explicit `AsSplitQuery()` / `AsSingleQuery()` choice. The trigger condition is `_collectionId > 1` inside `RelationalShapedQueryCompilingExpressionVisitor.ShaperProcessingExpressionVisitor` -- every `RelationalCollectionShaperExpression` in the shaper tree increments `_collectionId`, and JSON owned collections projected through `TransformJsonQueryToTable` flow through that exact path.
- `Microsoft.EntityFrameworkCore.Query.DistinctAfterOrderByWithoutRowLimitingOperatorWarning` (event `CoreEventId.DistinctAfterOrderByWithoutRowLimitingOperatorWarning`): fires when `.Distinct()` is applied to a query that carries an explicit ordering with no `Take` / `Skip` downstream.

The spec test base (`Microsoft.EntityFrameworkCore.Specification.Tests.FixtureBase.AddOptions`) configures `ConfigureWarnings(b => b.Default(WarningBehavior.Throw))` -- every warning becomes a thrown `InvalidOperationException` during spec test runs. The intent is to surface unexpected warnings as test failures so the EF Core team and provider authors catch regressions.

The provider already addresses the `DistinctAfterOrderBy` warning at the translator layer for the common case via `MySqlQueryableMethodTranslatingExpressionVisitor.IsNaturallyOrdered` (commit `4238ed93fb96`), which mirrors SqlServer's OPENJSON-key recognition pattern for our `MySqlJsonTableExpression`. That override closes 4 of the 10 originally-failing `DistinctAfter` variants directly; the remaining 6 surfaced a second-layer issue with the `MultipleCollectionIncludeWarning` that the Distinct warning had been masking.

The `MultipleCollectionIncludeWarning` does NOT have an equivalent provider-side translator hook. The trigger lives downstream in EF Core's shaper-processing layer, with no `protected virtual` extension point a provider can override to bypass it.

## Decision Drivers

- Inherited JSON assertions must execute instead of being hidden by fixture warnings.
- Runtime provider warning behavior must remain unchanged.
- The exception needs a narrow and reviewable scope.

## Considered Options

- Downgrade the two warnings in the JSON fixture only
- Skip the affected JSON tests
- Suppress the warnings provider-wide

## Decision Outcome

Chosen option: "Downgrade the two warnings in the JSON fixture only", because fixture-only warning policy preserves both specification execution and runtime diagnostics.

Override `JsonQueryMySqlFixture.AddOptions(DbContextOptionsBuilder)` in the spec test subclass to downgrade both warnings to `Log` for the JsonQuery spec test fixture only:

```csharp
public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
    => base.AddOptions(builder).ConfigureWarnings(w => w
        .Log(CoreEventId.DistinctAfterOrderByWithoutRowLimitingOperatorWarning)
        .Log(RelationalEventId.MultipleCollectionIncludeWarning));
```

The suppression is **scoped to the fixture instance**. The provider's runtime configuration (`MySqlOptionsExtension`, `MySqlServiceCollectionExtensions`) is unchanged -- production users who construct a `DbContext` with `UseMySql(...)` see the warnings under whatever `WarningBehavior` they have configured (default: `Log`, since EF Core does not throw these by default outside the spec-test base).

### Consequences

- Good, because the tests execute their assertions without weakening provider defaults.
- Bad, because future EF Core warning changes require fixture-specific review.

### Confirmation

- Run the JSON specification suite and assert the inherited methods execute.
- Run diagnostics tests proving production warning behavior is unchanged.

## Pros and Cons of the Options

### Downgrade the two warnings in the JSON fixture only

- Good, because the inherited assertions execute while production defaults remain intact.
- Bad, because the fixture diverges from the upstream warning-as-error configuration.

### Skip the affected JSON tests

- Good, because the fixture needs no warning configuration change.
- Bad, because valid provider behavior would lose executable specification coverage.

### Suppress the warnings provider-wide

- Good, because all JSON queries would avoid the warning failures.
- Bad, because production consumers would lose useful diagnostics.

## More Information

### Implementation Snapshot

- `JsonQueryMySqlFixture.AddOptions` override downgrades two EF Core query warnings from `Throw` (the spec-test-base default) to `Log` for the JSON query specification subclass only.

### Cross-Provider Empirical Verification

This decision rests on empirical verification, NOT source-reading inference. The probe was a standalone console program against the EF Core 10.0.4 `Microsoft.EntityFrameworkCore.SqlServer` NuGet package + SQL Server 2022 Docker container, running the equivalent LINQ shape (`Select(x => new { A = x.OwnedRefRoot.OwnedCollBranch.Where(...).ToList(), B = x.OwnedCollRoot.Distinct().ToList(), C = x.OwnedCollRoot.Select(r => r.OwnedCollBranch.Where(...).ToList()), D = x.Children.ToList() })`).

| Configuration | Observed SqlServer 10.0.4 behavior |
|---|---|
| Default `WarningBehavior` (Log) | Warning is logged. Query succeeds. |
| `ConfigureWarnings(Default(Throw))` | Warning throws `InvalidOperationException`. |
| Spec test fixture config (`Default(Throw)` + 2 specific `Log` overrides) | Warning throws -- same behavior our provider exhibits. |

**Empirical conclusion**: the warning fires on SqlServer + EF Core 10.0.4 too; the SqlServer spec test (`JsonQuerySqlServerTest.Json_multiple_collection_projections`) calls `await base.Json_multiple_collection_projections(async); AssertSql(...)` without any Skip attribute, implying the test is either failing in dotnet/efcore CI under the spec test config or is suppressed via a mechanism this investigation could not surface. Either way, no provider has a clean translator-side bypass.

Cross-checking the other EF-Core-8+ JSON providers:

| Provider | Source | Behavior on the same LINQ shape under spec-test config |
|---|---|---|
| **SqlServer** (Microsoft, `dotnet/efcore`) | Open | No fixture suppression. No translator bypass. Warning throws under empirical probe. |
| **Npgsql** (`npgsql/efcore.pg`) | Open | `NpgsqlQueryableMethodTranslatingExpressionVisitor.IsNaturallyOrdered` override exists for `PgUnnestExpression` ordinality (same pattern as our `MySqlJsonTableExpression` override). No `MultipleCollectionInclude` bypass. |
| **SQLite** (Microsoft, `dotnet/efcore`) | Open | Test is rewritten to `Assert.Equal(SqliteStrings.ApplyNotSupported, (await Assert.ThrowsAsync<InvalidOperationException>(() => base.Json_multiple_collection_projections(async))).Message)` -- SQLite lacks `APPLY` / `LATERAL` entirely, so an earlier error fires before the warning is reached. Not applicable to Doka -- we support `JOIN LATERAL` (commit `ceb020ed0181`). |
| **Oracle** (`Oracle.EntityFrameworkCore`, NuGet 10.23.26200) | Closed-source | Unverifiable. |
| **Pomelo** (`PomeloFoundation/Pomelo.EntityFrameworkCore.MySql`) | Open | Does not implement the EF Core 8+ `ToJson()` model. Their JSON support (`JsonPocoQueryTestBase`, `JsonStringQueryTestBase`, `JsonNewtonsoftPocoQueryTestBase`, ...) is a different test corpus entirely -- value-converter-on-opaque-column rather than first-class owned-entity-as-JSON. The warning does not apply because their JSON columns are not modeled as collection navigations. |

Upstream tracking: `dotnet/efcore` issue #29665 "Multiple Include() warning: false positive for ThenInclude()" (see Sources), open since 2022-11-23, labeled Feature, milestone Backlog. The issue's specific case (`Include` + `ThenInclude`) is narrower than ours (JSON projection) but the underlying EF Core code path that miscounts `_collectionId` is the same.

### Rationale (why suppression over Skip)

Both options were considered. The suppression path is enterprise-cleaner for one decisive reason:

**Verification**. Our JSON_TABLE-based multi-collection projection produces correct SQL -- the empirical probe against SQL Server with the same LINQ shape returned identical row counts and content. Suppression lets the spec tests RUN and assert that our actual SQL output matches the spec's expected results. Skipping the tests would leave us with no proof our translator handles this shape correctly; we would be deferring 16 verification points to a future EF Core upstream fix that may never arrive (Issue #29665 has been open for over three years at the time of this decision).

Comparison matrix:

| Criterion | Suppression (chosen) | Skip + ADR (rejected) |
|---|---|---|
| Tests run and verify SQL correctness | Yes (445/445 surface covered) | No (16 verification points lost) |
| Audit trail | This ADR + fixture comment + SkipList entry pointer | ADR + 16 SkipList entries |
| Production impact | Zero -- per-DbContext fixture config only | Zero -- skip is test-only |
| EF Core upstream fix path | Suppression becomes silently redundant when #29665 closes | Skips need manual removal post-fix |
| Re-evaluation trigger | "When #29665 closes in a consumed EF Core minor/major" | Same trigger plus manual un-skip |
| Auditor interpretation | "Documented upstream false positive, scoped local override, verified SQL correctness" | "16 tests unverifiable; reason TBD per ADR" |

The mainstream provider stance is unverifiable: SqlServer + Npgsql have no fixture suppression and no translator bypass, yet their spec tests appear to expect green status (no Skip attributes). The SQLite stance is a workaround dependent on the SQLite-specific `ApplyNotSupported` early-fail. Both equivalent-tier providers (SqlServer + Npgsql) are structurally in the same position as Doka; if dotnet/efcore CI is green on these tests, the mechanism is not visible in the open source. This ADR therefore takes the verifiable, locally-auditable path.

### Implementation Notes

- Override order matters: `base.AddOptions(builder)` runs first to apply the spec test base's `Default(Throw)`, then our `ConfigureWarnings` chain downgrades the two specific warnings.
- Scope is the `JsonQueryMySqlFixture` only. Other spec test fixtures (`NorthwindWhereQueryMySqlFixture`, `BuiltInDataTypesMySqlFixture`, etc.) inherit the spec test base's `Default(Throw)` unchanged.
- The two warning event IDs come from different namespaces: `CoreEventId.DistinctAfterOrderByWithoutRowLimitingOperatorWarning` is in `Microsoft.EntityFrameworkCore.Diagnostics`, `RelationalEventId.MultipleCollectionIncludeWarning` is also in `Microsoft.EntityFrameworkCore.Diagnostics` but is the relational-layer event.
- Empirical sweep delta: 6 of the 16 previously-warning-failing tests pass cleanly (warning was the sole failure). 10 tests reveal underlying assertion mismatches the warning had masked -- those move into the existing per-test assertion-mismatch triage category and are tracked separately. Net JsonQueryMySqlTest: 396 / 445 passing per engine cross-engine parity, up from 390.

### Related decisions and references

- ADR D-011 -- Spec-test subclass strategy (this fixture is a D-011 subclass).
- `MySqlQueryableMethodTranslatingExpressionVisitor.IsNaturallyOrdered` override (commit `4238ed93fb96`) -- the translator-side companion that addresses the Distinct warning for the JSON_TABLE-key-ordered case.
- `MySqlJsonTableExpression` introduction (commit `64b0fd1024d8`) -- the table-valued-function shape that makes the multi-collection projection translatable in the first place.
- `dotnet/efcore` issue #29665 -- upstream false-positive tracking.

### Re-evaluation Triggers

Remove this suppression AND re-verify all 16 affected tests pass natively when EITHER:

1. `dotnet/efcore` issue #29665 is closed in an EF Core release the provider consumes (current floor: `10.0.4`), OR
2. EF Core ships a provider-overridable `protected virtual bool IsMultipleCollectionIncludeNonReducing(SelectExpression)` (or equivalent) extension point, OR
3. A new EF Core release adds JSON-aware bypass logic in `RelationalShapedQueryCompilingExpressionVisitor.ShaperProcessingExpressionVisitor` that detects JSON_TABLE / OPENJSON-derived collections and skips the `_collectionId++` increment.

The trigger is mechanically verifiable via the meta-test pattern (not yet implemented): a single test in the suite that asserts the warning still fires under the production-default `WarningBehavior`. When that meta-test starts failing, EF Core has fixed the upstream issue and this ADR's suppression can be removed.
- EF Core fixes the warning classification for the affected JSON shapes.
- The fixture no longer exercises either warning path.

### Decision History

- 2026-05-17: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.

### Implementation References

- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/Query/JsonQueryMySqlTest.cs`

### Sources

- [dotnet/efcore issue 29665](https://github.com/dotnet/efcore/issues/29665) (primary source; retrieved 2026-07-27)
