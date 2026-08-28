---
id: D-028
status: implemented
date: 2026-08-21
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "How generated schema changes choose or transform existing data"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-028 -- Require explicit migration data transformations

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

EF Core also synthesizes CLR defaults while scaffolding a required column on an
existing table. That convenience has the same ownership problem: zero, an empty
string, or an empty GUID is not evidence of a valid application value. Initial
table creation is different because no existing row needs repair.

A provider-owned GUID storage change from `Char36` to `Binary16`, or in the
reverse direction, is another data transformation. MySQL documents that
`ALTER TABLE` attempts conversions but can alter values. The provider cannot
infer whether stored bytes are big-endian, little-endian, or time-swapped, and
DDL execution is not a portable rollback boundary across MySQL and MariaDB.

## Decision Drivers

- A schema migration must not invent application data.
- Generated SQL must not depend silently on constraints absent from the EF
  model.
- MySQL and MariaDB must receive the same explicit migration intent.
- Invalid repair contracts must fail before any migration SQL is emitted.
- Existing explicit EF migration inputs must remain the source of truth.
- Provider-owned storage changes must preserve every value byte-for-byte or
  fail before the destructive schema step.

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

For a scaffolded required column on an existing table, Doka removes only the
CLR default synthesized by EF Core when the target model declares no default.
The generated operation carries a provider annotation that requires an
explicit `DefaultValue`, `DefaultValueSql`, or an application-authored staged
migration before SQL generation. An explicit target-model default and a
hand-authored migration default remain authoritative. Required columns inside
an initial `CreateTableOperation`, computed columns, auto-increment columns,
temporal period columns, and server-generated temporal row versions do not
require application backfill values.

Table-sharing projections with the same physical column and the same explicit
SQL backfill remain one migration operation even when their CLR projections
differ. Distinct SQL backfills remain distinct and fail visibly rather than
being collapsed.

For `TIMESTAMP`, the repair must use `DefaultValueSql`. A CLR `DefaultValue` is
rejected even when its wall-clock fields fall inside a documented engine range,
because its stored instant would still depend on the executing session time
zone. The application can choose a server expression such as
`CURRENT_TIMESTAMP(6)` or another deliberately reviewed expression.

### Provider-owned GUID representation changes

A direct provider-owned `Char36`/`Binary16` `AlterColumnOperation` is rejected
before SQL generation. The same guard applies when only one side is
provider-owned and the other side has a different physical representation.
An unannotated `binary(16)` side is never assumed compatible with native
`Binary16`: its converter may use little-endian, big-endian, or time-swapped
bytes even though the SQL type has the same spelling. The migration must
instead:

1. validate that every source value has the expected canonical representation;
2. add nullable destination columns;
3. backfill with application-reviewed SQL;
4. verify source and destination values in both directions;
5. make required destination columns non-null only after validation;
6. replace source columns and restore keys, indexes, and foreign keys; and
7. verify the resulting model and representative stored values.

The six-target integration matrix proves the Doka big-endian conversion with
`HEX` and `UNHEX`, including required values, nullable values, `Guid.Empty`,
malformed-source rejection, and the reverse round trip. This executable proof
does not authorize copying the SQL blindly into a consumer whose binary layout
has not first been identified.

An application-owned `Guid` converter using canonical `char(36)` or
`varchar(36)` and a provider-owned `Char36` mapping share the same logical text
representation, so that transition remains an ordinary EF store-type change.
Doka does not claim or rewrite arbitrary application converters. Provider-owned
`Char36` and `Binary16` operations are normalized back to `Guid` for
`CreateTable`, `AddColumn`, and both sides of `AlterColumn`; provider-side text
or byte defaults are restored to their `Guid` model value before C# migration
generation. Invalid provider defaults fail before source generation.

### Consequences

- Good, because migration output never invents domain data from a CLR type.
- Good, because JSON, spatial, enumeration, and constrained scalar columns use
  the same fail-closed rule rather than an incomplete exception list.
- Good, because `TIMESTAMP` repair semantics are explicit at the SQL boundary
  where session-time-zone behavior is defined.
- Good, because generated required-column additions cannot silently populate
  existing rows with a CLR default that the application never selected.
- Good, because GUID representation changes preserve the source until a
  validated destination exists.
- Good, because matching `binary(16)` declarations cannot hide incompatible
  application and provider byte-order contracts.
- Bad, because migrations that relied on an implicit CLR default must be edited
  before they can run.
- Bad, because an explicit raw SQL expression remains the migration author's
  responsibility and can still be rejected by the target server.
- Bad, because a `Char36`/`Binary16` conversion requires a staged migration and
  deployment plan rather than one generated `ALTER COLUMN`.

### Confirmation

- Run `./eng/test.sh` to prove explicit and missing backfill generation,
  timestamp literal rejection, and decision-contract validation.
- Run `./eng/test-integration.sh` to execute positive JSON, spatial,
  enumeration, constrained scalar, temporal, and timestamp-expression repairs
  plus staged GUID representation round trips on all six supported targets.
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

EF Core documents that calculated data transformations should add a nullable
destination, populate it, make it required, and only then drop the source. Its
model differ also synthesizes a CLR default for a non-nullable, non-inline
column when the model declares none. Doka retains EF's generated operation
shape but marks this provider-visible synthetic value as insufficient evidence
for changing existing application data.

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
- 2026-08-29: Extended the same ownership boundary to scaffolded required
  columns and provider-owned `Char36`/`Binary16` data transformations.
- 2026-08-29: Covered one-sided application/provider transitions and retained
  model CLR types across every generated column-operation surface.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations/MySqlMigrationsSqlGenerator.Columns.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Migrations/MySqlMigrationsModelDiffer.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Migrations/MySqlMigrationDdlCoverageTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Migrations/MySqlMigrationBackfillContractTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Migrations/MySqlGuidMigrationTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Migrations/MySqlMigrationDslTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Migrations/MySqlColumnDefaultIntegrationTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Migrations/MySqlGuidMigrationIntegrationTests.cs`
- `examples/MigrationsWorkflow/Migrations/20260820121000_UpdateTemporalDefaults.cs`
- `docs/operations/migrations.md`

### Sources

- [EF Core 10.0.11 SQL Server migrations SQL generator source](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.SqlServer/Migrations/SqlServerMigrationsSqlGenerator.cs) (primary source; retrieved 2026-08-21)
- [EF Core managing migrations: transform existing data](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing#transform-existing-data) (primary source; retrieved 2026-08-29)
- [EF Core 10.0.8 `MigrationsModelDiffer` source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/Internal/MigrationsModelDiffer.cs) (primary source; retrieved 2026-08-29)
- [MySQL `DATE`, `DATETIME`, and `TIMESTAMP` types](https://dev.mysql.com/doc/refman/8.4/en/datetime.html) (primary source; retrieved 2026-08-21)
- [MySQL Data Type Default Values](https://dev.mysql.com/doc/refman/8.4/en/data-type-defaults.html) (primary source; retrieved 2026-08-21)
- [MySQL Geometry Well-Formedness and Validity](https://dev.mysql.com/doc/refman/8.4/en/geometry-well-formedness-validity.html) (primary source; retrieved 2026-08-21)
- [MySQL `CHECK` Constraints](https://dev.mysql.com/doc/refman/8.4/en/create-table-check-constraints.html) (primary source; retrieved 2026-08-21)
- [MySQL `ALTER TABLE`](https://dev.mysql.com/doc/refman/8.4/en/alter-table.html) (primary source; retrieved 2026-08-29)
- [MySQL atomic DDL](https://dev.mysql.com/doc/refman/8.4/en/atomic-ddl.html) (primary source; retrieved 2026-08-29)
- [MySQL string functions, including `HEX` and `UNHEX`](https://dev.mysql.com/doc/refman/8.4/en/string-functions.html) (primary source; retrieved 2026-08-29)
- [MariaDB `TIMESTAMP`](https://mariadb.com/docs/server/reference/data-types/date-and-time-data-types/timestamp) (primary source; retrieved 2026-08-21)
- [MariaDB Constraints](https://mariadb.com/docs/server/reference/sql-statements/data-definition/constraint) (primary source; retrieved 2026-08-21)
- [MariaDB `HEX`](https://mariadb.com/docs/server/reference/sql-functions/string-functions/hex) (primary source; retrieved 2026-08-29)
- [MariaDB `UNHEX`](https://mariadb.com/docs/server/reference/sql-functions/string-functions/unhex) (primary source; retrieved 2026-08-29)
- [MariaDB statements causing an implicit commit](https://mariadb.com/docs/server/reference/sql-statements/transactions/sql-statements-that-cause-an-implicit-commit) (primary source; retrieved 2026-08-29)
- [MySqlConnector GUID formats](https://mysqlconnector.net/api/mysqlconnector/mysqlguidformattype/) (primary source; retrieved 2026-08-29)
