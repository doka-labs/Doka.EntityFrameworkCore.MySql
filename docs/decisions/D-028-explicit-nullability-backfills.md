---
id: D-028
status: implemented
date: 2026-08-21
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "How nullable-to-required column migrations choose replacement data"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-028 -- Require explicit nullability backfills

## Context and Problem Statement

Changing a populated column from nullable to required can succeed only after
every existing null has been replaced. The provider previously chose the CLR
default when the migration did not declare a replacement. That choice was not
a store-domain contract: empty text is invalid JSON, empty bytes are not a
well-formed geometry, zero can violate a `CHECK`, an empty string can fall
outside an `ENUM`, and minimum CLR temporal values are outside documented
engine ranges.

`TIMESTAMP` adds a second ambiguity. MySQL and MariaDB interpret timestamp
literals through the current session time zone before storing the UTC value.
The SQL generator neither owns that runtime session nor has enough information
to convert an unspecified CLR `DateTime` portably.

The decision question is whether the provider may infer replacement data from
the CLR type or must require the migration author to define the data contract.

## Decision Drivers

- A schema migration must not invent application data.
- Generated SQL must not depend silently on constraints absent from the EF
  model.
- MySQL and MariaDB must receive the same explicit migration intent.
- Invalid repair contracts must fail before any migration SQL is emitted.
- Existing explicit EF migration inputs must remain the source of truth.

## Considered Options

- Require an explicit backfill for every nullable-to-required transition
- Infer a CLR default and add store-type exceptions
- Inspect the live database before selecting a replacement

## Decision Outcome

Chosen option: "Require an explicit backfill for every nullable-to-required
transition", because only the application owns the meaning of replacement
data.

The provider emits its pre-alter `UPDATE` only when the operation declares a
non-null `DefaultValue` or a nonblank `DefaultValueSql`. A missing value fails
before SQL generation. This rule is independent of the apparent store type,
because arbitrary constraints, triggers, and application invariants can reject
otherwise valid scalar values.

Table-sharing projections with the same physical column and the same explicit
SQL backfill remain one migration operation even when their CLR projections
differ. Distinct SQL backfills remain distinct and fail visibly rather than
being collapsed.

For `TIMESTAMP`, the repair must use `DefaultValueSql`. A CLR `DefaultValue` is
rejected even when its wall-clock fields fall inside a documented engine range,
because its stored instant would still depend on the executing session time
zone. The application can choose a server expression such as
`CURRENT_TIMESTAMP(6)` or another deliberately reviewed expression.

### Consequences

- Good, because migration output never invents domain data from a CLR type.
- Good, because JSON, spatial, enumeration, and constrained scalar columns use
  the same fail-closed rule rather than an incomplete exception list.
- Good, because `TIMESTAMP` repair semantics are explicit at the SQL boundary
  where session-time-zone behavior is defined.
- Bad, because migrations that relied on an implicit CLR default must be edited
  before they can run.
- Bad, because an explicit raw SQL expression remains the migration author's
  responsibility and can still be rejected by the target server.

### Confirmation

- Run `./eng/test.sh` to prove explicit and missing backfill generation,
  timestamp literal rejection, and decision-contract validation.
- Run `./eng/test-integration.sh` to execute positive JSON, spatial,
  enumeration, constrained scalar, temporal, and timestamp-expression repairs
  on all six supported targets.
- Run `./eng/test-migration-deployment.sh` to prove the explicit timestamp
  expression through runtime migration, CLI, normal and idempotent scripts,
  and migration bundles.

## Pros and Cons of the Options

### Require an explicit backfill for every nullable-to-required transition

- Good, because it keeps data semantics with the application that owns them.
- Good, because it fails deterministically before an unsafe statement runs.
- Bad, because it requires an additional migration argument or a preceding
  application-authored data migration.

### Infer a CLR default and add store-type exceptions

- Good, because simple migrations require less author input.
- Bad, because no finite store-type list can account for arbitrary constraints,
  triggers, collations, and application invariants.
- Bad, because a CLR default is not evidence of a valid store-domain value.

### Inspect the live database before selecting a replacement

- Good, because catalog metadata could expose some constraints.
- Bad, because SQL generation is synchronous and deterministic and must not
  perform database I/O.
- Bad, because catalog inspection still cannot infer application meaning or
  the intended replacement value.

## More Information

EF Core's SQL Server generator performs a nullable-to-required repair only when
the migration operation explicitly carries `DefaultValue` or
`DefaultValueSql`. Doka applies the same ownership boundary while retaining its
MySQL- and MariaDB-specific pre-alter statement ordering.

### Re-evaluation Triggers

- Re-evaluate if EF Core introduces a provider-independent, explicit backfill
  operation with stronger ordering and data-contract semantics.
- Re-evaluate `TIMESTAMP` CLR values only if EF Core adds an offset-bearing
  migration value contract and both engine families document equivalent input
  semantics for it.

### Decision History

- 2026-08-21: Decision recorded with status implemented.
- 2026-08-21: Consumer qualification exposed invalid implicit JSON, spatial,
  enumeration, constrained scalar, and timestamp repairs.
- 2026-08-21: The equivalent-operation contract was aligned so identical SQL
  backfills deduplicate independently of CLR projection metadata.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations/MySqlMigrationsSqlGenerator.Columns.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations/MySqlMigrationsModelDiffer.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Migrations/MySqlMigrationDdlCoverageTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Migrations/MySqlMigrationDslTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Migrations/MySqlColumnDefaultIntegrationTests.cs`
- `examples/MigrationsWorkflow/Migrations/20260820121000_UpdateTemporalDefaults.cs`
- `docs/operations/migrations.md`

### Sources

- [EF Core 10.0.11 SQL Server migrations SQL generator source](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.SqlServer/Migrations/SqlServerMigrationsSqlGenerator.cs) (primary source; retrieved 2026-08-21)
- [MySQL `DATE`, `DATETIME`, and `TIMESTAMP` types](https://dev.mysql.com/doc/refman/8.4/en/datetime.html) (primary source; retrieved 2026-08-21)
- [MySQL Data Type Default Values](https://dev.mysql.com/doc/refman/8.4/en/data-type-defaults.html) (primary source; retrieved 2026-08-21)
- [MySQL Geometry Well-Formedness and Validity](https://dev.mysql.com/doc/refman/8.4/en/geometry-well-formedness-validity.html) (primary source; retrieved 2026-08-21)
- [MySQL `CHECK` Constraints](https://dev.mysql.com/doc/refman/8.4/en/create-table-check-constraints.html) (primary source; retrieved 2026-08-21)
- [MariaDB `TIMESTAMP`](https://mariadb.com/docs/server/reference/data-types/date-and-time-data-types/timestamp) (primary source; retrieved 2026-08-21)
- [MariaDB Constraints](https://mariadb.com/docs/server/reference/sql-statements/data-definition/constraint) (primary source; retrieved 2026-08-21)
