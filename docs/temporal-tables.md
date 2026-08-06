# Temporal Tables

The provider exposes one system-versioned temporal model and query contract
across all supported engines. MariaDB uses its native temporal implementation;
MySQL uses a provider-owned InnoDB history table and transactional triggers.
The public model API remains portable while the capability contract reports
native, emulated, and engine-unsupported behavior separately.

## Support Matrix

| Contract | MySQL 8.4 LTS | MariaDB 11.4 / 11.8 LTS |
| --- | --- | --- |
| System-versioned temporal storage | Provider-emulated with InnoDB history and triggers | Native |
| Portable temporal query operators | Supported | Supported |
| Application-time periods | No native engine feature | Typed native provider contract |
| Bitemporal storage | No native engine feature | Typed native provider contract |
| `FOR PORTION OF` update / delete | No native engine feature | Typed native provider contract |

## Configure a Temporal Entity

```csharp
modelBuilder.Entity<Employee>(entity =>
{
    entity.ToTable(
        "Employees",
        table => table.IsTemporal(temporal =>
        {
            temporal.UseHistoryTable("EmployeeHistory");
            temporal.HasPeriodStart("ValidFrom").HasColumnName("ValidFrom");
            temporal.HasPeriodEnd("ValidTo").HasColumnName("ValidTo");
        }));

    entity.HasKey(employee => employee.Id);
});
```

The two period properties are shadow properties unless the entity declares
matching CLR properties. They use `datetime(6)` on the MySQL emulation route
and generated `timestamp(6)` row-start / row-end columns on native MariaDB.
Names are explicit so migrations, reverse engineering, and generated model
code round-trip the same contract.

Temporal tables participate in `EnsureCreated`, ordinary migrations, migration
scripts, and reverse engineering. Native MariaDB metadata is discovered from
the information schema. MySQL emulation is recognized only when the complete,
strict provider marker and its current/history/trigger topology agree; partial
or user-authored lookalikes are not silently classified as temporal tables.

## Query Temporal History

All temporal boundaries must have `DateTimeKind.Utc`. Every temporal root is
no-tracking because a result can contain several versions with the same primary
key.

```csharp
var atInstant = await context.Employees
    .TemporalAsOf(utcInstant)
    .Where(employee => employee.DepartmentId == departmentId)
    .ToListAsync(cancellationToken);

var completeHistory = await context.Employees
    .TemporalAll()
    .Select(employee => new
    {
        employee.Id,
        employee.Name,
        ValidFrom = EF.Property<DateTime>(employee, "ValidFrom"),
        ValidTo = EF.Property<DateTime>(employee, "ValidTo"),
    })
    .ToListAsync(cancellationToken);
```

The complete operator set is:

- `TemporalAsOf(utcPointInTime)` returns the version current at one instant.
- `TemporalFromTo(utcFrom, utcTo)` returns versions overlapping a half-open
  range.
- `TemporalBetween(utcFrom, utcTo)` also includes a version ending at the upper
  boundary.
- `TemporalContainedIn(utcFrom, utcTo)` returns versions whose complete
  lifetime is contained in the inclusive range.
- `TemporalAll()` returns current and historical versions.

The native MariaDB route emits `FOR SYSTEM_TIME`. The MySQL route queries a
`UNION ALL` of the current and history tables with equivalent boundary
predicates. `ExecuteUpdate` and `ExecuteDelete` reject temporal roots because
historical versions are immutable query results, not mutation targets.

## Schema Safety

MariaDB does not guarantee correct history after structural `ALTER TABLE`
operations on a system-versioned table. The provider therefore rejects native
temporal add, alter, drop, rename-column, and rename-table operations instead
of enabling MariaDB's history-altering compatibility switch. Convert the table
to an ordinary table in one explicit migration, perform the structural change,
and enable system versioning again only after accepting that the prior history
has been removed.

The temporal-to-ordinary transition is marked destructive. Its native MariaDB
DDL scopes `system_versioning_alter_history=KEEP` to the single atomic
deactivation statement; it does not leak a session setting. The transition
drops system versioning, the period, and the period columns together.

MySQL emulation has different safety constraints:

- current and history tables use InnoDB so a trigger failure rolls back the
  complete statement
- deterministic `BEFORE` / `AFTER` triggers maintain microsecond UTC periods
  and copy old versions to history
- generated columns are copied by their expressions because MySQL prohibits
  `OLD` and `NEW` references to generated columns
- temporal foreign keys cannot use `Cascade` or `SetNull`, because MySQL does
  not activate triggers for cascaded foreign-key actions

MariaDB temporal entities cannot map generated columns because MariaDB does
not allow generated columns in a system-versioned table. The model validator
reports these invalid combinations before SQL generation.

## Configure Application Time and Bitemporal Storage

MariaDB application-time periods and bitemporal tables use typed model,
migration, reverse-engineering, and mutation APIs. MySQL rejects these exact
contracts as engine limitations because MySQL 8.4 has no corresponding period
grammar.

```csharp
modelBuilder.Entity<Price>(entity =>
{
    entity.ToTable(
        "Prices",
        table => table.IsBitemporal(
            systemTime =>
            {
                systemTime.UseHistoryTable("PriceHistory");
                systemTime.HasPeriodStart("RecordedFrom");
                systemTime.HasPeriodEnd("RecordedTo");
            },
            applicationTime =>
            {
                applicationTime.HasPeriodName("BusinessValidity");
                applicationTime.HasPeriodStart(price => price.ValidFrom);
                applicationTime.HasPeriodEnd(price => price.ValidTo);
            }));

    entity.HasKey(price => new { price.Id, price.ValidFrom, price.ValidTo })
        .UseWithoutOverlaps();
});
```

`UseWithoutOverlaps()` is available on primary or unique keys and indexes. It
emits MariaDB's native period constraint instead of approximating overlap
validation in application code. Reverse engineering reads `PERIODS` and
`KEY_PERIOD_USAGE`, reconstructs the period, bitemporal marker, and overlap
constraint, and emits the same Fluent API.

Application-time mutation roots are half-open ranges. MariaDB splits affected
rows as required by its native period semantics:

```csharp
await context.Prices
    .ForPortionOf(validFrom, validTo)
    .ExecuteUpdateAsync(
        setters => setters.SetProperty(price => price.Amount, newAmount),
        cancellationToken);

await context.Prices
    .ForPortionOf(deleteFrom, deleteTo)
    .ExecuteDeleteAsync(cancellationToken);
```

`ForPortionOf` is deliberately a mutation-only, direct-table query root.
Materializing it, composing set operations, or using a multi-table delete is
rejected before invalid SQL is sent. The provider does not require raw SQL for
the supported update and delete contracts.

## Runnable Verification

The [TemporalTablesAndCtes example](../examples/TemporalTablesAndCtes/README.md)
creates, updates, and deletes a temporal row and verifies `TemporalAll` and
`TemporalAsOf`. The release-candidate gate executes that invariant on MySQL
8.4, MariaDB 11.4, and MariaDB 11.8.

The live integration matrix additionally verifies every temporal query root,
half-open and inclusive interval endpoints, server-side aggregates, transaction
rollback, optimistic concurrency, session-state isolation, reverse-engineering
round trips, and the exact cancellation token delivered to the relational
command boundary. MariaDB 11.4 and 11.8 also execute the complete bitemporal
path from generated DDL and information-schema readback through typed portion
updates and deletes. Separate command-operability contracts exercise
cancellation inside MySqlConnector against a running database command.

## Related Limitations

The cross-feature [limitations index](limitations.md) identifies the exact
engine and EF Core boundaries. In particular, it records MariaDB structural
migration constraints, MySQL cascade restrictions on emulated temporal tables,
and the EF Core compiled-query-root boundary.

## Primary Sources

All sources were retrieved on 2026-08-05.

- [MySQL 8.4 trigger syntax](https://dev.mysql.com/doc/refman/8.4/en/trigger-syntax.html)
- [MySQL 8.4 stored-program restrictions](https://dev.mysql.com/doc/refman/8.4/en/stored-program-restrictions.html)
- [MariaDB system-versioned tables](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/system-versioned-tables)
- [MariaDB application-time periods](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/application-time-periods)
- [MariaDB bitemporal tables](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/bitemporal-tables)
- [MariaDB `PERIODS` information-schema table](https://mariadb.com/docs/server/reference/system-tables/information-schema/information-schema-periods-table)
- [MariaDB `KEY_PERIOD_USAGE` information-schema table](https://mariadb.com/docs/server/reference/system-tables/information-schema/information-schema-key_period_usage-table)
- [MariaDB `ALTER TABLE`](https://mariadb.com/docs/server/reference/sql-statements/data-definition/alter/alter-table)
- [MariaDB `SET STATEMENT`](https://mariadb.com/docs/server/reference/sql-statements/administrative-sql-statements/set-commands/set-statement)
- [EF Core SQL Server temporal tables](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/temporal-tables)
- [EF Core 10.0.10 SQL Server temporal query extensions](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.SqlServer/Extensions/SqlServerDbSetExtensions.cs)
