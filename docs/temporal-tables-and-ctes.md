# Temporal Tables and Common Table Expressions

This provider exposes one system-versioned temporal model and query contract
across all supported engines. MariaDB uses its native temporal implementation;
MySQL uses a provider-owned InnoDB history table and transactional triggers.
Common table expressions use EF Core's composable SQL query roots instead of a
parallel provider-specific LINQ language.

## Support Matrix

| Contract | MySQL 8.4 LTS | MariaDB 11.4 / 11.8 LTS |
| --- | --- | --- |
| Non-recursive CTE query | Native | Native |
| Recursive CTE query | Native | Native |
| Composable `FromSql` query root | Supported | Supported |
| CTE `UPDATE` / `DELETE` engine SQL | Native | Unsupported by the target engine versions |
| System-versioned temporal storage | Provider-emulated with InnoDB history and triggers | Native |
| Portable temporal query operators | Supported | Supported |
| Application-time and bitemporal SQL | No native engine feature | Native engine SQL; outside the portable provider API |

The provider capability contract reports native, emulated, and
engine-unsupported behavior separately. CTE data modification on MariaDB is an
engine-version boundary: the MariaDB documentation introduces CTE-enabled
`UPDATE` in 12.3, later than the supported 11.4 and 11.8 LTS lines.

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

## Query CTEs Safely

Use `FromSql` / `FromSqlInterpolated` for entity query roots and `SqlQuery` for
unmapped scalar or result types. Interpolated values are database parameters;
do not concatenate untrusted identifiers or SQL fragments.

```csharp
var maximumDepth = 8;

var descendants = await context.Nodes
    .FromSqlInterpolated($"""
        WITH RECURSIVE `tree` (`Id`, `ParentId`, `Depth`) AS (
            SELECT `Id`, `ParentId`, 0
            FROM `Nodes`
            WHERE `Id` = {rootId}
            UNION ALL
            SELECT node.`Id`, node.`ParentId`, tree.`Depth` + 1
            FROM `Nodes` AS node
            INNER JOIN `tree` AS tree ON node.`ParentId` = tree.`Id`
            WHERE tree.`Depth` < {maximumDepth}
        )
        SELECT node.*
        FROM `Nodes` AS node
        INNER JOIN `tree` AS tree ON tree.`Id` = node.`Id`
        """)
    .AsNoTracking()
    .OrderBy(node => node.Id)
    .ToListAsync(cancellationToken);
```

The provider recognizes both `WITH` and `WITH RECURSIVE` as composable query
roots. Normal LINQ projection, filtering, ordering, tracking, no-tracking,
synchronous, and asynchronous contracts remain in the EF Core pipeline.
Explicit `EF.CompileQuery` delegates around `FromSql` or temporal `DbSet`
extensions are outside that contract. EF Core replaces the `DbSet` receiver
with an `IQueryable` query root while funcletizing the compiled query and then
rejects the extension call before provider translation. The official EF Core
10 SQL Server temporal API has the same receiver contract. Normal execution
still uses EF Core's query-shape compilation cache. MySQL-specific CTE data
modification can be issued through the standard raw SQL command APIs. Do not
issue that grammar against MariaDB 11.4 or 11.8.

## Runnable Verification

The [TemporalTablesAndCtes example](../examples/TemporalTablesAndCtes/README.md)
creates, updates, and deletes a temporal row, verifies `TemporalAll` and
`TemporalAsOf`, and executes a parameterized recursive CTE followed by a
composed LINQ predicate. The release-candidate gate executes that invariant on
MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.

The live integration matrix additionally verifies every temporal query root,
half-open and inclusive interval endpoints, server-side aggregates, transaction
rollback, optimistic concurrency, session-state isolation, reverse-engineering
round trips, and the exact cancellation token delivered to the relational
command boundary. Separate command-operability contracts exercise cancellation
inside MySqlConnector against a running database command.

## Primary Sources

All sources were retrieved on 2026-08-04.

- [MySQL 8.4 common table expressions](https://dev.mysql.com/doc/refman/8.4/en/with.html)
- [MySQL 8.4 `SELECT`](https://dev.mysql.com/doc/refman/8.4/en/select.html)
- [MySQL 8.4 `DELETE`](https://dev.mysql.com/doc/refman/8.4/en/delete.html)
- [MySQL 8.4 trigger syntax](https://dev.mysql.com/doc/refman/8.4/en/trigger-syntax.html)
- [MySQL 8.4 stored-program restrictions](https://dev.mysql.com/doc/refman/8.4/en/stored-program-restrictions.html)
- [MariaDB common table expressions](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/with)
- [MariaDB recursive CTEs](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/recursive-common-table-expressions-overview)
- [MariaDB `UPDATE`](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/changing-deleting-data/update)
- [MariaDB system-versioned tables](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/system-versioned-tables)
- [MariaDB application-time periods](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/application-time-periods)
- [MariaDB bitemporal tables](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/bitemporal-tables)
- [MariaDB `ALTER TABLE`](https://mariadb.com/docs/server/reference/sql-statements/data-definition/alter/alter-table)
- [MariaDB `SET STATEMENT`](https://mariadb.com/docs/server/reference/sql-statements/administrative-sql-statements/set-commands/set-statement)
- [EF Core SQL queries](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)
- [EF Core SQL Server temporal tables](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/temporal-tables)
- [EF Core advanced performance topics](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics)
- [EF Core async programming](https://learn.microsoft.com/en-us/ef/core/miscellaneous/async)
- [EF Core 10.0.10 relational SQL query extensions](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Extensions/RelationalQueryableExtensions.cs)
- [EF Core 10.0.10 SQL Server temporal query extensions](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.SqlServer/Extensions/SqlServerDbSetExtensions.cs)
