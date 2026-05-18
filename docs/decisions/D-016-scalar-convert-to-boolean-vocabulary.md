# D-016 -- MySqlScalarConvert.ToBoolean Accepted-String Vocabulary

- **Status:** Implemented
- **Date:** 2026-05-18
- **Scope:** `src/Doka.EntityFrameworkCore.MySql/Internal/Storage/MySqlScalarConvert.cs` -- string-input dispatch of the internal `ToBoolean(object?)` helper
- **Implementation:** `ToBoolean` accepts the .NET-stdlib bool vocabulary plus the literal `"1"` shortcut; broader tokens (`"yes"`, `"no"`, `"0"`, `"Y"`, `"N"`, `"on"`, `"off"`, ...) are intentionally NOT recognised and route through the existing `InvalidOperationException` path.

## Context

An earlier backlog item asked the provider's internal scalar-to-bool helper to accept a custom broader string vocabulary (`false` / `FALSE` / `yes` / `no` / `0`) on top of the .NET stdlib set. The phrasing implied a hypothetical configuration-style consumer that might emit those tokens.

Two empirical checks at implementation time revisited the premise:

1. **Production call sites**: `MySqlScalarConvert.ToBoolean` is exclusively called from `MySqlHistoryRepository`'s `GET_LOCK` / `RELEASE_LOCK` result handling (`src/.../Internal/Migrations/MySqlHistoryRepository.cs` lines 242 / 297 / 383 / 410). MySQL's `GET_LOCK` returns NUMERIC results (1 / 0 / NULL) per the [MySQL 8.4 Reference Manual section 14.14](https://dev.mysql.com/doc/refman/8.4/en/locking-functions.html); the same shape on MariaDB ([MariaDB Reference](https://mariadb.com/docs/server/reference/sql-functions/secondary-functions/miscellaneous-functions/get_lock)). The string-branch of `ToBoolean` is therefore dead surface from the perspective of production code.
2. **.NET stdlib alignment**: `Convert.ToBoolean` and `bool.TryParse` accept the case-insensitive equivalents of `"True"` / `"False"` and nothing else ([Microsoft Learn: Boolean.Parse](https://learn.microsoft.com/en-us/dotnet/api/system.boolean.parse?view=net-8.0)). Extending the vocabulary to `"yes"`/`"no"`/`"0"` would put the provider's helper out of line with every other .NET BCL boolean-parsing surface a consumer might already be reasoning about.

A cross-provider scan confirms no mainstream EF Core provider ships a non-stdlib bool-string vocabulary in its scalar-convert helper.

## Decision

`MySqlScalarConvert.ToBoolean` accepts the following string inputs as `true`:

- The case-sensitive literal `"1"`.
- Anything `bool.TryParse` returns `true` for, i.e. `"True"` / `"true"` / `"TRUE"` and any case-mixed variant of `True`.

Every other string -- including `"0"`, `"yes"`, `"no"`, `"on"`, `"off"`, `"Y"`, `"N"`, `""`, whitespace-only -- routes through `false` (when `bool.TryParse` returns `false` for `"false"` / `"False"` / `"FALSE"`) or throws `InvalidOperationException` (when the input is structurally unparseable). The string dispatch lives at `MySqlScalarConvert.cs:22`; the corresponding property test (`MySqlScalarConvertPropertyTests.cs:54` -- `ToBoolean_recognizes_only_documented_true_strings`) pins the contract via FsCheck with `MaxTest = 1000`.

The numeric branch of `ToBoolean` covers the full signed / unsigned / floating / decimal matrix; that branch is the production-relevant one (it is what `GET_LOCK` and `RELEASE_LOCK` results land on).

## Consequences

### Positive

- Consumer mental model lines up with .NET stdlib: anyone who knows `bool.TryParse` knows the helper.
- No dead surface: the accepted-string set tracks the call sites that exist today; extending it would create unused branches per `declared-and-consumed-rule`.
- The property test serves as machine-checked contract documentation; a future change to the dispatch shape produces an FsCheck-shrunk counter-example, not a silent drift.

### Negative

- A future consumer that feeds non-stdlib tokens through `ToBoolean` will hit `InvalidOperationException`. The re-evaluation trigger below names this exactly so the policy resurfaces when the predicate fires.
- The earlier-backlog phrasing that asked for a broader vocabulary remains traceable via the operator's planning artifacts; this ADR is the canonical closure record.

### Neutral

- The helper is `internal` -- the accepted vocabulary is provider-internal, not part of the public API contract.

## Re-evaluation triggers

- A new call site emerges that feeds a non-numeric string into `ToBoolean` (e.g. a configuration-driven feature flag, a scaffolded enum, an MCP / CLI driver that reads operator input). The trigger predicate is "a string that is neither `"1"` nor a `bool.TryParse`-recognised token reaches `ToBoolean`"; the response is to extend the vocabulary explicitly per the new call-site's needs, with the property test updated in the same commit.
- The .NET BCL itself extends `bool.TryParse` to accept additional tokens. The provider's vocabulary would automatically widen via the `bool.TryParse` delegation; the ADR's wording would need to update accordingly.

## Alternatives considered

- **Extend the vocabulary to `"yes"` / `"no"` / `"0"`.** Rejected: no production call site emits those tokens (MySQL `GET_LOCK` returns numeric); the broader vocabulary would create dead surface and diverge from .NET BCL semantics.
- **Throw on every string input.** Rejected: the existing `"1"` shortcut and `bool.TryParse` delegation are useful for the test-time and ad-hoc-script call sites that do reach the helper.
- **Mark the helper as `[Obsolete]` to discourage non-numeric inputs.** Rejected: the helper is `internal` and consumed only by `MySqlHistoryRepository`; the obsolete signal would have no audience.

## References

- Property test contract: `tests/Doka.EntityFrameworkCore.MySql.Tests/Properties/MySqlScalarConvertPropertyTests.cs`.
- Production call sites: `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations/MySqlHistoryRepository.cs` (GET_LOCK / RELEASE_LOCK result handling).
- [MySQL 8.4 Reference Manual section 14.14: Locking Functions](https://dev.mysql.com/doc/refman/8.4/en/locking-functions.html)
- [MariaDB GET_LOCK reference](https://mariadb.com/docs/server/reference/sql-functions/secondary-functions/miscellaneous-functions/get_lock)
- [Microsoft Learn: Boolean.Parse Method](https://learn.microsoft.com/en-us/dotnet/api/system.boolean.parse?view=net-8.0)
