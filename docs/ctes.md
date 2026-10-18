# Common Table Expressions

Common table expressions use EF Core's composable SQL query roots. A narrow
`WithMaterializedSource` LINQ operator additionally binds one complete source
when multiple query references must share its selected rows and computed
values. It is not a general named-CTE or recursion language. Both supported
engine families execute non-recursive and recursive read CTEs natively.

## Support Matrix

| Contract | MySQL 8.4 / 9.7 LTS | MariaDB 10.11 / 11.4 / 11.8 LTS | MariaDB 12.3 LTS |
| --- | --- | --- | --- |
| Non-recursive CTE query | Native | Native | Native |
| Recursive CTE query | Native | Native | Native |
| Composable `FromSql` query root | Supported | Supported | Supported |
| CTE `UPDATE` / `DELETE` engine SQL | Native | Unsupported by engine | Native |

CTE data modification on MariaDB is an engine-version boundary: the MariaDB
documentation introduces CTE-enabled `UPDATE` in 12.3. The provider therefore
executes that contract on 12.3 while preserving the explicit engine boundary
for 10.11, 11.4, and 11.8.

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
modification can be issued through the standard raw SQL command APIs. MariaDB
12.3 accepts the corresponding documented grammar; do not issue it against
MariaDB 10.11, 11.4, or 11.8.

## Bind One Source for Several References

`MySqlQueryableExtensions.WithMaterializedSource` composes a query over one
statement-local source. Use it when several query branches must read the same
selected occurrences and computed values. It takes an expression tree, not an
executed callback:

```csharp
public static IQueryable<TResult> WithMaterializedSource<TSource, TResult>(
    this IQueryable<TSource> source,
    Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose);
```

Import `Doka.EntityFrameworkCore.MySql` for this extension and
`Microsoft.EntityFrameworkCore` for EF's query operators. Construction is
synchronous and performs no I/O. `ToListAsync(cancellationToken)` and other EF
terminal operators execute the composed query later, using one parameterized
SQL statement per execution attempt. EF's execution strategy may retry that
statement; each attempt receives a new statement-local source. The exact
cancellation token reaches the relational command boundary.

### Complete Source Selection and Reuse

The source includes its filters, duplicate occurrences, SQL projection, and
`OrderBy` / `Skip` / `Take`. Reference-specific filters inside `compose` do not
change that selection for another reference. A source's ordering determines
its page; it does not impose an order on the composed result.

For example, suppose `Node` has a `Guid` key and tree ID, together with `long`
bounds. Bind a narrow projection before using its bounds in an envelope and
in the exact pair predicates:

```csharp
var selected = context.Nodes
    .Where(node => node.TreeId == treeId)
    .OrderBy(node => node.Id)
    .Skip(offset)
    .Take(pageSize)
    .Select(node => new BoundsRow
    {
        Id = node.Id,
        TreeId = node.TreeId,
        Left = node.Left,
        Right = node.Right,
    });

var query = selected.WithMaterializedSource(shared => context.Nodes
    .Where(ancestor => ancestor.TreeId == treeId
        && ancestor.Left < shared.Max(row => (long?)row.Left)
        && ancestor.Right > shared.Min(row => (long?)row.Right))
    .SelectMany(
        ancestor => shared.Where(row => row.TreeId == ancestor.TreeId
            && ancestor.Left < row.Left
            && ancestor.Right > row.Right),
        (ancestor, row) => new
        {
            SourceId = row.Id,
            AncestorId = ancestor.Id,
        }));

var pairs = await query
    .OrderBy(pair => pair.SourceId)
    .ThenBy(pair => pair.AncestorId)
    .ToListAsync(cancellationToken);
```

The projected DTO contains only SQL values:

```csharp
public sealed class BoundsRow
{
    public Guid Id { get; init; }

    public Guid TreeId { get; init; }

    public long Left { get; init; }

    public long Right { get; init; }
}
```

The nullable aggregate inputs make an empty source produce no pairs. The
aggregate envelope is only a prefilter: the exact predicates still compare
both bounds for each pair. Application-specific scope, visibility, or other
isolation predicates must also remain in the query; the binding invents none
of them. Choosing useful indexes and qualifying the resulting query plan are
separate from binding the source.

### Volatile Values, Bags, and UNION ALL

A needed SQL value such as `Guid.NewGuid()` belongs to the bound source row.
Every reference to that occurrence reads its stored value. The following
query selects a random page once and uses it in two disjoint result branches:

```csharp
var selected = context.Nodes
    .Where(node => node.TreeId == treeId)
    .OrderBy(node => EF.Functions.Random())
    .ThenBy(node => node.Id)
    .Skip(offset)
    .Take(pageSize)
    .Select(node => new
    {
        node.Id,
        Token = Guid.NewGuid(),
    });

var query = selected.WithMaterializedSource(shared => shared
    .Select(row => new { row.Id, row.Token, Branch = 0 })
    .Concat(shared.Select(row => new { row.Id, row.Token, Branch = 1 })));

var rows = await query
    .OrderBy(row => row.Id)
    .ThenBy(row => row.Branch)
    .ToListAsync(cancellationToken);
```

Both branches contain the same selected occurrences and tokens. `Concat`
retains SQL `UNION ALL` behavior. Duplicate occurrences in a source also remain
duplicates; the operator adds no `Distinct`, key-only rejoin, or client-side
comparison. An ordinary self-join still multiplies matching duplicate rows:
the binding does not manufacture an application row identity.

Separate `WithMaterializedSource` calls create independent bindings, even when
their input expressions have the same shape. Filtering one reference does not
mutate another reference. The contract does not promise evaluation of unused
columns or a particular order of SQL function calls, and must not be used for
SELECT side effects.

### Supported Shapes and Boundaries

- The source must be a relational query from the same Doka context as the
  composition. Mapped entities and flat, server-translatable scalar projections
  can be bound. Native mappings, value converters, nullability, collations, and
  EF result identifiers remain in the relational pipeline; a `binary(16)` GUID
  is not serialized through text or JSON.
- Entity sources retain their exact EF mapping identity. Two named shared
  entity mappings with the same CLR type do not become interchangeable after
  binding. Temporal sources also retain their original operation and UTC
  boundaries: navigation from a bound `TemporalAsOf` entity uses the same
  instant on separately stored temporal targets. Existing temporal navigation
  restrictions still apply. Entity set operations reject incompatible temporal
  roots, including current/historical mixtures and different instants. Binding
  does not turn an application-time mutation root into a read query or bypass
  its navigation and range-compatibility guards.
- A source projection cannot contain deferred client methods, collection
  subqueries, or a client grouping. Those are not one flat SQL relation. Project
  their required scalar values before binding, then compose the result using
  normal EF operators.
- Supported owned references remain part of their entity source, including
  EF's automatic owned-reference loading. Their projected values are captured
  with that source rather than silently dropped or loaded again independently.
- A collection or non-owned reference `Include` / `AutoInclude` that still
  contributes eager-loaded entities to the source is rejected. An include that
  EF discards before a flat scalar projection does not create this unsupported
  source shape. Ordinary navigation access in a SQL projection is different
  from eager loading and remains subject to EF's translation rules.
- Ordinary entity tracking, no-tracking, reference shaping, and reference
  `Include` on the composed output remain supported by EF's normal translation
  rules, including reference `Include` after binding. A collection `Include`
  directly on a bound entity is rejected. For a collection `Include`, bind
  scalar source values and join back to a normal mapped entity query, then
  include its collection. Other collection projections follow EF's existing
  single-query shaping rules. The operator does not add support for a result
  shape EF itself cannot translate.
- Multi-command split-query execution is rejected. A session temporary table,
  an extra connection, and a source preload are not fallback implementations.
- LINQ-to-Objects, a foreign provider/context, an escaped binding reference,
  and an untranslatable source or composition fail before SQL execution. The
  provider does not silently rerun the source, evaluate the relational operator
  on the client, or execute a partially translated query.

For example, shape collections on the ordinary entity side of a rejoin,
rather than asking the bound source to contain a collection:

```csharp
var selectedIds = context.Nodes
    .Where(node => node.TreeId == treeId)
    .Select(node => node.Id);

var query = selectedIds.WithMaterializedSource(shared => context.Nodes
    .Join(shared, node => node.Id, id => id, (node, id) => node)
    .Include(node => node.Children));

var nodes = await query.ToListAsync(cancellationToken);
```

Normal EF parameter extraction and the query-shape compilation cache remain
responsible for captured scalar values and plan reuse. Binding aliases and
identities are statement-local and deterministic; there is no provider-owned
data cache or retained source data between enumerations. A fresh execution,
including one from a pooled context, binds its current parameter values.
Global query filters apply when the completed source is selected; references
read those selected occurrences without applying that source filter a second
time. Other entity roots in the composition retain their ordinary filters.
The `FromSql` / temporal `DbSet` receiver limitation described above is specific
to those APIs; it must not be inferred from the name of this `IQueryable`
operator. Explicit compiled queries still have to satisfy EF's ordinary
compiled-query and translation constraints.

### Native Lowering and Cost

The provider emits a statement-level recursive CTE with an explicitly projected,
typed empty recursive arm. The recursive arm returns no additional rows. This
prevents a materialized source from being merged back into independent source
evaluations and preserves duplicate occurrences with `UNION ALL`.

This choice is deliberate on MariaDB: in the native MariaDB 11.8.9 controls,
an ordinary CTE, including a window-fenced CTE, could be cloned per reference
and produce different UUID values. The empty recursive arm preserved the stored
source values. The corresponding MySQL 8.4.11 controls also preserved those
values. These observations explain the lowering; they do not establish behavior
for untested engine versions.

A logical source binding does not promise one physical temporary table, an
index seek, or a faster query. Materialization consumes server memory and can
spill to disk; projection width, selected rows, indexes, and the consuming query
still matter. Bind the narrow SQL values that the composition needs. Inspect
native plans and fully read representative results when assessing performance.
Do not replace a simple single-reference query merely to introduce a CTE.

## Runnable Verification

The [TemporalTablesAndCtes example](../examples/TemporalTablesAndCtes/README.md)
executes a parameterized recursive CTE followed by a composed LINQ predicate.
`./eng/test-examples.sh` runs the invariant on all six supported LTS targets in
the explicit live example matrix. Provider functional tests additionally cover
composability, parameterization, synchronous and asynchronous execution, and
affected command boundaries. Live integration tests exercise CTE data
modification on MySQL 8.4 / 9.7 and MariaDB 12.3.

## Related Limitations

The cross-feature [limitations index](limitations.md) records MariaDB CTE data
modification and the EF Core compiled-query-root boundary. Those entries limit
only the exact documented shapes; read CTEs remain supported.

## Primary Sources

The original raw SQL sources were retrieved on 2026-08-11. The source-binding
references below were checked on 2026-10-18.

- [MySQL 8.4 common table expressions](https://dev.mysql.com/doc/refman/8.4/en/with.html)
- [MySQL 8.4 `SELECT`](https://dev.mysql.com/doc/refman/8.4/en/select.html)
- [MySQL 8.4 `DELETE`](https://dev.mysql.com/doc/refman/8.4/en/delete.html)
- [MySQL 9.7 common table expressions](https://dev.mysql.com/doc/refman/9.7/en/with.html)
- [MySQL 9.7 `DELETE`](https://dev.mysql.com/doc/refman/9.7/en/delete.html)
- [MariaDB common table expressions](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/with)
- [MariaDB recursive CTEs](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/recursive-common-table-expressions-overview)
- [MariaDB 12.3 `WITH`](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/with)
- [MariaDB `UPDATE`](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/changing-deleting-data/update)
- [EF Core SQL queries](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)
- [EF Core advanced performance topics](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics)
- [EF Core async programming](https://learn.microsoft.com/en-us/ef/core/miscellaneous/async)
- [EF Core 10.0.10 relational SQL query extensions](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Extensions/RelationalQueryableExtensions.cs)
- [MySQL 8.4 derived-table and CTE materialization](https://dev.mysql.com/doc/refman/8.4/en/derived-table-optimization.html)
- [MariaDB CTE cloning, MDEV-35357](https://jira.mariadb.org/browse/MDEV-35357)
- [EF Core client and server evaluation](https://learn.microsoft.com/en-us/ef/core/querying/client-eval)
- [EF Core single and split queries](https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries)
