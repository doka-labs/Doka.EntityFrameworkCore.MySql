# Common Table Expressions

Common table expressions use EF Core's composable SQL query roots instead of a
parallel provider-specific LINQ language. Both supported engine families
execute non-recursive and recursive read CTEs natively.

## Support Matrix

| Contract | MySQL 8.4 LTS | MariaDB 11.4 / 11.8 LTS |
| --- | --- | --- |
| Non-recursive CTE query | Native | Native |
| Recursive CTE query | Native | Native |
| Composable `FromSql` query root | Supported | Supported |
| CTE `UPDATE` / `DELETE` engine SQL | Native | Unsupported by the target engine versions |

CTE data modification on MariaDB is an engine-version boundary: the MariaDB
documentation introduces CTE-enabled `UPDATE` in 12.3, later than the
supported 11.4 and 11.8 LTS lines.

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
executes a parameterized recursive CTE followed by a composed LINQ predicate.
`./eng/test-examples.sh` runs the invariant on MySQL 8.4, MariaDB 11.4, and
MariaDB 11.8 in the explicit live example matrix. Provider functional tests
additionally cover composability, parameterization, synchronous and
asynchronous execution, and affected command boundaries.

## Related Limitations

The cross-feature [limitations index](limitations.md) records MariaDB CTE data
modification and the EF Core compiled-query-root boundary. Those entries limit
only the exact documented shapes; read CTEs remain supported.

## Primary Sources

All sources were retrieved on 2026-08-05.

- [MySQL 8.4 common table expressions](https://dev.mysql.com/doc/refman/8.4/en/with.html)
- [MySQL 8.4 `SELECT`](https://dev.mysql.com/doc/refman/8.4/en/select.html)
- [MySQL 8.4 `DELETE`](https://dev.mysql.com/doc/refman/8.4/en/delete.html)
- [MariaDB common table expressions](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/with)
- [MariaDB recursive CTEs](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/recursive-common-table-expressions-overview)
- [MariaDB `UPDATE`](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/changing-deleting-data/update)
- [EF Core SQL queries](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)
- [EF Core advanced performance topics](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics)
- [EF Core async programming](https://learn.microsoft.com/en-us/ef/core/miscellaneous/async)
- [EF Core 10.0.10 relational SQL query extensions](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Extensions/RelationalQueryableExtensions.cs)
