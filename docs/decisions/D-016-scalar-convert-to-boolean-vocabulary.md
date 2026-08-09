---
id: D-016
status: implemented
date: 2026-05-18
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "Internal scalar-to-boolean conversion vocabulary"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-016 -- Keep scalar boolean parsing aligned with the BCL

## Context and Problem Statement

An earlier backlog item asked the provider's internal scalar-to-bool helper to accept a custom broader string vocabulary (`false` / `FALSE` / `yes` / `no` / `0`) on top of the .NET stdlib set. The phrasing implied a hypothetical configuration-style consumer that might emit those tokens.

Two empirical checks at implementation time revisited the premise:

1. **Production call sites**: `MySqlScalarConvert.ToBoolean` is exclusively called from `MySqlHistoryRepository`'s `GET_LOCK` / `RELEASE_LOCK` result handling (`src/.../Internal/Migrations/MySqlHistoryRepository.cs` lines 242 / 297 / 383 / 410). MySQL's `GET_LOCK` returns NUMERIC results (1 / 0 / NULL) per the MySQL 8.4 Reference Manual section 14.14 (see Sources); the same shape on MariaDB (MariaDB Reference (see Sources)). The string-branch of `ToBoolean` is therefore dead surface from the perspective of production code.
2. **.NET stdlib alignment**: `Convert.ToBoolean` and `bool.TryParse` accept the case-insensitive equivalents of `"True"` / `"False"` and nothing else (Microsoft Learn: Boolean.Parse (see Sources)). Extending the vocabulary to `"yes"`/`"no"`/`"0"` would put the provider's helper out of line with every other .NET BCL boolean-parsing surface a consumer might already be reasoning about.

A cross-provider scan confirms no mainstream EF Core provider ships a non-stdlib bool-string vocabulary in its scalar-convert helper.

## Decision Drivers

- Internal conversion should accept actual engine result shapes.
- String parsing should match familiar .NET semantics.
- Unused convenience vocabulary should not expand the maintenance surface.

## Considered Options

- BCL true/false vocabulary plus numeric one
- Broad configuration-style vocabulary
- Numeric results only

## Decision Outcome

Chosen option: "BCL true/false vocabulary plus numeric one", because the smallest vocabulary covering real results and BCL expectations is the safest contract.

`MySqlScalarConvert.ToBoolean` accepts the following string inputs as `true`:

- The case-sensitive literal `"1"`.
- Anything `bool.TryParse` returns `true` for, i.e. `"True"` / `"true"` / `"TRUE"` and any case-mixed variant of `True`.

Every other string -- including `"0"`, `"yes"`, `"no"`, `"on"`, `"off"`, `"Y"`, `"N"`, `""`, whitespace-only -- routes through `false` (when `bool.TryParse` returns `false` for `"false"` / `"False"` / `"FALSE"`) or throws `InvalidOperationException` (when the input is structurally unparseable). The string dispatch lives at `MySqlScalarConvert.cs:22`; the corresponding property test (`MySqlScalarConvertPropertyTests.cs:54` -- `ToBoolean_recognizes_only_documented_true_strings`) pins the contract via FsCheck with `MaxTest = 1000`.

The numeric branch of `ToBoolean` covers the full signed / unsigned / floating / decimal matrix; that branch is the production-relevant one (it is what `GET_LOCK` and `RELEASE_LOCK` results land on).

### Consequences

- Good, because conversion behavior is predictable and unused aliases stay out of the code.
- Bad, because non-BCL false-like strings throw instead of being guessed.

#### Positive

- Consumer mental model lines up with .NET stdlib: anyone who knows `bool.TryParse` knows the helper.
- No dead surface: the accepted-string set tracks the call sites that exist today; extending it would create unused branches per `declared-and-consumed-rule`.
- The property test serves as machine-checked contract documentation; a future change to the dispatch shape produces an FsCheck-shrunk counter-example, not a silent drift.

#### Negative

- A future consumer that feeds non-stdlib tokens through `ToBoolean` will hit `InvalidOperationException`. The re-evaluation trigger below names this exactly so the policy resurfaces when the predicate fires.
- The earlier-backlog phrasing that asked for a broader vocabulary remains traceable via the operator's planning artifacts; this ADR is the canonical closure record.

#### Neutral

- The helper is `internal` -- the accepted vocabulary is provider-internal, not part of the public API contract.

### Confirmation

- Run `MySqlScalarConvertTests` across numeric, null, valid string, and invalid string inputs.
- Verify lock-function result handling on MySQL and MariaDB.

## Pros and Cons of the Options

### BCL true/false vocabulary plus numeric one

- Good, because it covers observed engine results and familiar .NET strings.
- Bad, because configuration-style tokens such as yes and no remain invalid.

### Broad configuration-style vocabulary

- Good, because more caller-provided strings would convert successfully.
- Bad, because the provider would accept values no production call site emits.

### Numeric results only

- Good, because the contract exactly matches current lock-function results.
- Bad, because defensive handling of connector string results would be removed.

## More Information

### Implementation Snapshot

- `ToBoolean` accepts the .NET-stdlib bool vocabulary plus the literal `"1"` shortcut; broader tokens (`"yes"`, `"no"`, `"0"`, `"Y"`, `"N"`, `"on"`, `"off"`, ...) are intentionally NOT recognized and route through the existing `InvalidOperationException` path.

### Additional Alternative Rationale

- **Extend the vocabulary to `"yes"` / `"no"` / `"0"`.** Rejected: no production call site emits those tokens (MySQL `GET_LOCK` returns numeric); the broader vocabulary would create dead surface and diverge from .NET BCL semantics.
- **Throw on every string input.** Rejected: the existing `"1"` shortcut and `bool.TryParse` delegation are useful for the test-time and ad-hoc-script call sites that do reach the helper.
- **Mark the helper as `[Obsolete]` to discourage non-numeric inputs.** Rejected: the helper is `internal` and consumed only by `MySqlHistoryRepository`; the obsolete signal would have no audience.

### References

- Property test contract: `tests/Doka.EntityFrameworkCore.MySql.Tests/Properties/MySqlScalarConvertPropertyTests.cs`.
- Production call sites: `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations/MySqlHistoryRepository.cs` (GET_LOCK / RELEASE_LOCK result handling).
- MySQL 8.4 Reference Manual section 14.14: Locking Functions (see Sources)
- MariaDB GET_LOCK reference (see Sources)
- Microsoft Learn: Boolean.Parse Method (see Sources)

### Re-evaluation Triggers

- A new call site emerges that feeds a non-numeric string into `ToBoolean` (e.g. a configuration-driven feature flag, a scaffolded enum, an MCP / CLI driver that reads operator input). The trigger predicate is "a string that is neither `"1"` nor a `bool.TryParse`-recognized token reaches `ToBoolean`"; the response is to extend the vocabulary explicitly per the new call-site's needs, with the property test updated in the same commit.
- The .NET BCL itself extends `bool.TryParse` to accept additional tokens. The provider's vocabulary would automatically widen via the `bool.TryParse` delegation; the ADR's wording would need to update accordingly.
- A production call site emits a documented string outside the accepted vocabulary.
- Connector or engine lock-function result types change.

### Decision History

- 2026-05-18: Decision recorded with status implemented.
- 2026-07-27: Migrated to Doka MADR profile 1.0 without changing the decision outcome.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Storage/Conversion/MySqlScalarConvert.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations/MySqlHistoryRepository.cs`

### Sources

- [MySQL 8.4 locking functions](https://dev.mysql.com/doc/refman/8.4/en/locking-functions.html) (primary source; retrieved 2026-07-27)
- [MariaDB GET_LOCK](https://mariadb.com/docs/server/reference/sql-functions/secondary-functions/miscellaneous-functions/get_lock) (primary source; retrieved 2026-07-27)
- [System.Boolean.Parse](https://learn.microsoft.com/en-us/dotnet/api/system.boolean.parse) (primary source; retrieved 2026-07-27)
