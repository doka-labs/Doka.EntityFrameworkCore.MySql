# Common Table Expressions

`WithCte` and `WithRecursiveCte` are available from Doka 10.7.0 on .NET 10 /
EF Core 10. The separate `WithMaterializedSource` and `StraightJoin` APIs
described below have been available since 10.6.0.

Doka provides scoped, typed `WithCte` and bounded `WithRecursiveCte` LINQ
operators alongside EF Core's composable SQL query roots. The separate
`WithMaterializedSource` operator binds selected occurrences and computed
values when several references must share them. `StraightJoin` directs one
inner join edge; it does not create a source binding.

## Support Matrix

| Contract | MySQL 8.4 / 9.7 LTS | MariaDB 10.11 / 11.4 / 11.8 LTS | MariaDB 12.3 LTS |
| --- | --- | --- | --- |
| Non-recursive CTE query | Native | Native | Native |
| Recursive CTE query | Native | Native | Native |
| Composable `FromSql` query root | Supported | Supported | Supported |
| Directed `StraightJoin` LINQ edge | Native `STRAIGHT_JOIN` | Native `STRAIGHT_JOIN` | Native `STRAIGHT_JOIN` |
| CTE `UPDATE` / `DELETE` engine SQL | Native | Unsupported by engine | Native |

CTE data modification on MariaDB is an engine-version boundary: the syntax
first shipped in 12.3.1. Doka checks that exact minimum, including rejection of
12.3.0, while preserving the boundary for 10.11, 11.4, and 11.8. Engine syntax
support does not by itself qualify every typed write shape; see
[Typed CTE writes](#typed-cte-writes).

## Bind an Ordinary Typed CTE

Import `Doka.EntityFrameworkCore.MySql` and `Microsoft.EntityFrameworkCore`.
`WithCte` accepts a completed source and an expression composing its
statement-local references:

```csharp
public static IQueryable<TResult> WithCte<TSource, TResult>(
    this IQueryable<TSource> source,
    Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose);
```

For nodes with a `Guid` key and nullable `Guid` parent key:

```csharp
var selected = context.Nodes
    .AsNoTracking()
    .Where(node => node.TreeId == treeId)
    .Select(node => new { node.Id, node.ParentId, node.Name });

var query = selected.WithCte(shared => shared.Join(
    shared,
    parent => parent.Id,
    child => child.ParentId,
    (parent, child) => new
    {
        ParentName = parent.Name,
        ChildName = child.Name,
        ChildId = child.Id,
    }));

var pairs = await query
    .OrderBy(pair => pair.ChildId)
    .ToListAsync(cancellationToken);
```

Construction performs no I/O. The expression parameter is a lexical relation,
not an executed callback or a collection of loaded entities. It belongs to
that composition; it cannot escape into another query or context. Nesting
bindings can express independent or dependent definitions. Every occurrence
of one binding shares its definition identity; separate calls remain separate
bindings even when their source expressions are equal.

The source includes its filters, duplicates, projection and source page.
`OrderBy` / `Skip` / `Take` before binding select that page, but only an outer
`OrderBy` promises result order. Joins retain duplicate multiplication;
`Concat` retains `UNION ALL`; no implicit `Distinct` is added. Global source,
outer-root and navigated-target filters retain their normal scopes. Explicit
`IgnoreQueryFilters` remains an explicit opt-out.

Doka unions the column demand of all references before pruning a shared
ordinary definition. A field needed by another reference, a predicate or a
duplicate-discriminating `Distinct` remains present. This is a schema
correctness rule, not a promise that introducing a CTE makes a query faster.

An ordinary CTE leaves merging, materialization and repeated evaluation to the
native optimizer. In particular, repeated references to volatile expressions
need not share their computed values. Use
[WithMaterializedSource](#bind-one-source-for-several-references) when that
sharing is required. Neither operator promises a particular count or order of
SQL function calls, and neither is an API for SELECT side effects.

## Traverse a Bounded Recursive CTE

```csharp
public static IQueryable<TResult> WithRecursiveCte<TSource, TResult>(
    this IQueryable<TSource> anchor,
    Expression<Func<IQueryable<TSource>, IQueryable<TSource>>> recursiveStep,
    Expression<Func<IQueryable<TSource>, IQueryable<TResult>>> compose,
    int maximumDepth);
```

The anchor has depth zero. The step reads exactly one previous-iteration
frontier; `compose` reads the complete bounded relation. For the same node
mapping:

```csharp
var descendants = await context.Nodes
    .AsNoTracking()
    .Where(node => node.Id == rootId && node.TreeId == treeId)
    .WithRecursiveCte(
        frontier => frontier.Join(
            context.Nodes.Where(node => node.TreeId == treeId),
            parent => parent.Id,
            child => child.ParentId,
            (_, child) => child),
        rows => rows.OrderBy(node => node.Id),
        maximumDepth: 8)
    .ToListAsync(cancellationToken);
```

Depth zero selects only the anchor; depth one includes one step. The relation
uses real native `UNION ALL` recursion in one data statement. Repeated paths
and cycles remain repeated occurrences through the selected depth. There is
no implicit visited set, cycle removal or completeness promise beyond that
depth. A depth limit also does not bound the number of rows in a branching
graph. Apply an explicit outer `Distinct` only when that is the intended
result semantics.

The provider carries a hidden depth column independently of the EF model,
keys and visible projection. It preserves fields used only by recursive joins
or predicates when pruning other payload columns. An application which needs
to return a depth value must include its own value in a compatible flat
projection; the hidden column is not a public result property.

### Native Limits and Result Consumption

`maximumDepth` must be nonnegative and strictly below the current session's
native recursion ceiling: `cte_max_recursion_depth` on MySQL or
`max_recursive_iterations` on MariaDB. Equality is rejected because the
qualified native execution requires a final empty iteration. The guard is
conservative even when a particular anchor or step would terminate earlier.
Doka does not change either setting.

The same data statement carries the native-limit control. It remains present
for an empty result and is checked before a successful visible result is
exposed. Unsafe limits fail visibly instead of returning a successful partial
result. The engine may itself fail during materialization before its control
row is read. No separate metadata query, source preload, extra connection or
session-setting command implements this protection.

Current depth parameters are also validated on each cached-command execution;
a previous valid execution cannot authorize a later negative compiled-query
parameter. Ordinary query construction retains the chosen depth as a
structural input. Explicit compiled queries remain subject to EF's normal
translation constraints; this API does not add a general NativeAOT guarantee.

Use the caller's cancellation token for materialization, as in the examples.
Early enumeration disposal and failures must dispose the reader normally;
the operator does not commit, roll back or dispose a caller-owned transaction.

### Recursive Text Capacity

Matching CLR string types or a `longtext` type name does not prove that a
recursive expression can grow safely. A permissive server can truncate a
narrow anchor or return NULL after a packet limit is exceeded. Doka therefore
requires a proved capacity for transformed recursive text.

The qualified widening form is a scalar or flat projection with an explicit
supported charset-prefixed collation, an actual bounded physical or literal
anchor, finite constant depth, and provable constant concatenation growth.
For example, assume `Name` is physically mapped as compatible
`varchar(80)` / `utf8mb4` text:

```csharp
var values = await context.Nodes
    .Where(node => node.Id == rootId)
    .Select(node => EF.Functions.Collate(node.Name, "utf8mb4_unicode_ci"))
    .WithRecursiveCte(
        frontier => frontier.Select(value => value + "/"),
        rows => rows.OrderBy(value => value),
        maximumDepth: 3)
    .ToListAsync(cancellationToken);
```

The proved maximum here is 83 characters. Its mapping is propagated through
the frontier and outer parameters before SQL generation. The native statement
also requires the per-string capacity times the known charset's maximum byte
width to fit `max_allowed_packet`; this conservative check is not a bound on
the whole result size or a multi-column protocol row.

The original `COLLATE` stays inside the added cast as well as after it.
Widening does not silently convert an incompatible source charset before the
database validates that original comparison. There is no invented fallback
width or automatic choice of collation.

Unproved dynamic text growth, transformed unbounded `longtext`, incompatible
physical mappings or converters, and unsupported capacity expressions are
rejected. Identity recursion and compatible physical-column copies are not
string-growth transformations. Numeric recursion can use a qualified compiled
depth parameter; finite text widening requires its schema-defining constant
bound. A nominal string mapping alone does not establish native capacity.

### Recursive Shape Boundaries

Both arms must have the same mapped entity identity or compatible flat SQL
shape, including actual store facets and converters. The step has exactly
one direct frontier and may use qualified inner joins. A simple correlated
`SelectMany` is lowered to that direct join only when the existing safe APPLY
rewrite applies; ordinary MySQL LATERAL support does not qualify arbitrary
recursive derived scopes.

Inside the step, multiple self-references, mutual or nested bindings,
outer-joined frontiers, grouping, window calculations, sorting, paging, and set
operations are outside this contract. Sorting, paging and
deduplication belong to a separately translatable outer composition. A
recursive outer `Distinct` must project its outer ordering keys. Deferred
client/collection sources and split-query execution remain rejected.

Locking-read operators cannot target a bound CTE. Their separate contract
requires a qualified direct physical-table query; the CTE API does not move
that lock scope into its definition.

## Typed CTE Writes

Typed read composition does not make a CTE alias a writable mapped table.
The current boundaries are:

| Operation | Qualified boundary |
| --- | --- |
| Ordinary CTE with `ExecuteUpdateAsync` / `ExecuteDeleteAsync` | A uniquely mapped physical target; MySQL 8.4/9.7 or MariaDB 12.3.1 and later within the supported policy. |
| Recursive CTE with `ExecuteUpdateAsync` / `ExecuteDeleteAsync` | Rejected; the SELECT reader guard cannot protect an already executed write. |
| Ordinary or bounded recursive source with `ExecuteInsertFromQueryAsync` | A compatible typed physical target and positional assignments; native `INSERT INTO ... WITH ... SELECT` placement. |
| Any mutation containing `WithMaterializedSource` | Rejected, including a nested materialized dependency. |

Self-referencing ordinary CTE updates/deletes are additionally rejected where
the engine requires the provider's separate target-isolation emulation.
Foreign contexts, ambiguous targets and unsupported engine versions do not
fall back to a different write path.

Recursive INSERT uses its own native failure arm, not the SELECT reader's
control columns. An unsafe depth or text-capacity budget fails even when the
source is empty, filtered out or limited to zero rows. Its UNION metadata is
not used to materialize EF results. The typed insert still follows its normal
assignment, default, transaction, cancellation and native affected-row-count
contracts; it does not preload source rows or synchronize tracked entities.

Native failure or cancellation is not a general promise that an autocommitted
write, or an engine-aborted caller transaction, can be recovered by the provider.

## Query CTEs Safely

Use `FromSql` / `FromSqlInterpolated` for entity query roots and `SqlQuery` for
unmapped scalar or result types. Interpolated values are database parameters;
do not concatenate untrusted identifiers or SQL fragments.

Raw SQL does not attach the typed recursive API's depth and text-capacity
controls. Its author remains responsible for those SQL limits and expressions.

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

## Direct One Inner Join Edge

`MySqlQueryableExtensions.StraightJoin` has the key and result selectors of
EF's ordinary `Join`. It marks only that join edge for outer-before-inner
processing on MySQL and MariaDB:

```csharp
public static IQueryable<TResult> StraightJoin<TOuter, TInner, TKey, TResult>(
    this IQueryable<TOuter> outer,
    IQueryable<TInner> inner,
    Expression<Func<TOuter, TKey>> outerKeySelector,
    Expression<Func<TInner, TKey>> innerKeySelector,
    Expression<Func<TOuter, TInner, TResult>> resultSelector);
```

For example, for nodes with a `Guid` key and nullable `Guid` parent key:

```csharp
var parents = context.Nodes.Where(node => node.TreeId == treeId);
var children = context.Nodes.Where(node => node.TreeId == treeId);
var query = parents.StraightJoin(
    children,
    parent => parent.Id,
    child => child.ParentId,
    (parent, child) => new
    {
        ParentId = parent.Id,
        ChildId = child.Id,
    });

var pairs = await query
    .OrderBy(pair => pair.ParentId)
    .ThenBy(pair => pair.ChildId)
    .ToListAsync(cancellationToken);
```

Import `Doka.EntityFrameworkCore.MySql` for the operator. Both inputs must use
exactly the same asynchronous EF query-provider instance. LINQ-to-Objects and
different provider instances, including separate contexts, are rejected while
constructing the query. Another provider cannot translate this Doka operator.
Construction is synchronous and performs no I/O; ordinary EF async terminal
operators execute the query and accept the cancellation token.

Matching, duplicate multiplication, key and null semantics, filters, paging,
projection, tracking, and navigation shaping follow the corresponding ordinary
EF `Join`. Doka translates that join with EF and changes its selected SQL edge
to `STRAIGHT_JOIN`. Other joins remain ordinary joins; Doka does not emit the
global `SELECT STRAIGHT_JOIN` modifier. An explicit `OrderBy` still determines
result order. The directive can affect optimizer transformations beyond the
marked edge: MySQL excludes statements using the join type from its documented
semijoin and antijoin transformations. Compare complete native plans and fully
read results before choosing it for a consuming query.

Join direction does not select an index, create a database snapshot,
materialize either input, or guarantee a faster query. Use
`WithMaterializedSource` independently when several references must share one
completed source; it can compose with `StraightJoin`, but neither operator
substitutes for the other's contract. Application-specific isolation and
visibility predicates remain the application's responsibility.

## Runnable Verification

The [TemporalTablesAndCtes example](../examples/TemporalTablesAndCtes/README.md)
executes a parameterized recursive CTE followed by a composed LINQ predicate.
`./eng/test-examples.sh` runs the invariant on all six supported LTS targets in
the explicit live example matrix. Provider functional tests additionally cover
composability, parameterization, synchronous and asynchronous execution, and
affected command boundaries. Live integration tests exercise CTE data
modification on MySQL 8.4 / 9.7 and MariaDB 12.3.

`MySqlStraightJoinTests` compares directed and ordinary join translation for
scalar, composite, nullable, converted, constant, and paged keys, mixed join
edges, deferred construction, foreign-provider rejection, and compiled-query
composition. `MySqlStraightJoinExpressionTests` verifies cloning, quotation,
shared source dependencies, and annotation reconstruction. Its actual EF C#
generator tests compile and execute the generated code in a collectible load
context using the existing provider test friend-assembly identity. These checks
do not establish arbitrary consumer cross-assembly or NativeAOT support;
[D-017](decisions/D-017-nativeaot-smoke-deferred.md)
retains the runtime-posture contract.

## Related Limitations

The cross-feature [limitations index](limitations.md) records MariaDB CTE data
modification and the EF Core compiled-query-root boundary. Those entries limit
only the exact documented shapes; read CTEs remain supported.

## Primary Sources

The original raw SQL sources were retrieved on 2026-08-11. The source-binding
references below were checked on 2026-10-18. Directed-join and generated-C#
references were checked on 2026-10-19.
The typed recursive capacity and comparison references were checked on
2026-11-17 alongside the native qualification controls.

- [MySQL 8.4 common table expressions](https://dev.mysql.com/doc/refman/8.4/en/with.html)
- [MySQL 8.4 `SELECT`](https://dev.mysql.com/doc/refman/8.4/en/select.html)
- [MySQL 8.4 `DELETE`](https://dev.mysql.com/doc/refman/8.4/en/delete.html)
- [MySQL 9.7 common table expressions](https://dev.mysql.com/doc/refman/9.7/en/with.html)
- [MySQL 9.7 `DELETE`](https://dev.mysql.com/doc/refman/9.7/en/delete.html)
- [MariaDB common table expressions](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/with)
- [MariaDB recursive CTEs](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/common-table-expressions/recursive-common-table-expressions-overview)
- [MariaDB 12.3.1 release notes](https://mariadb.com/docs/release-notes/community-server/12.3/12.3.1)
- [MySQL packet and intermediate-string limit](https://dev.mysql.com/doc/refman/8.4/en/server-system-variables.html#sysvar_max_allowed_packet)
- [MariaDB packet and intermediate-string limit](https://mariadb.com/docs/server/server-management/variables-and-modes/server-system-variables#max_allowed_packet)
- [MySQL charset/collation compatibility](https://dev.mysql.com/doc/refman/8.4/en/charset-collation-compatibility.html)
- [MySQL COLLATE expressions](https://dev.mysql.com/doc/refman/8.4/en/charset-collate.html)
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
- [MySQL 8.4 join syntax and direction](https://dev.mysql.com/doc/refman/8.4/en/join.html)
- [MySQL 8.4 semijoin and antijoin restrictions](https://dev.mysql.com/doc/refman/8.4/en/semijoins-antijoins.html)
- [MariaDB join syntax](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/joins/join-syntax)
- [MariaDB join-order and index directives](https://mariadb.com/docs/server/ha-and-performance/optimization-and-tuning/query-optimizations/index-hints-how-to-force-query-plans)
- [EF Core complex query operators](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators)
- [EF Core 10.0.8 relational annotation quotation](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Query/RelationalExpressionQuotingUtilities.cs)
- [EF Core 10.0.13 relational annotation quotation](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Relational/Query/RelationalExpressionQuotingUtilities.cs)
- [EF Core 10.0.8 expression-to-C# translation](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Design/Query/Internal/LinqToCSharpSyntaxTranslator.cs)
- [EF Core 10.0.13 expression-to-C# translation](https://github.com/dotnet/efcore/blob/v10.0.13/src/EFCore.Design/Query/Internal/LinqToCSharpSyntaxTranslator.cs)
