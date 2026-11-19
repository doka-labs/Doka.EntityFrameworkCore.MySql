# Typed window functions

The typed APIs described here are available from Doka 10.7.0 on .NET 10 / EF Core 10.
The 11.0.0 preparation carries these contracts forward to .NET 11 / EF Core 11.

## Project a window calculation

Import `Doka.EntityFrameworkCore.MySql` and `Microsoft.EntityFrameworkCore`.
Start with `EF.Functions.Window()`, supply scalar partition/order keys, then
finish with one calculation:

```csharp
var ranked = context.Readings
    .Where(row => row.IsActive)
    .Select(row => new
    {
        row.Id,
        row.DeviceId,
        Position = EF.Functions.Window()
            .PartitionBy(row.DeviceId)
            .OrderByDescending(row.Timestamp)
            .ThenBy(row.Id)
            .RowNumber()
    });

var latest = await ranked
    .Where(row => row.Position <= 3)
    .OrderBy(row => row.DeviceId)
    .ThenBy(row => row.Position)
    .ToArrayAsync(cancellationToken);
```

The first filter selects rows before numbering. The later filter consumes a
completed projection through an outer SQL scope. Writing a window calculation
directly in `Where` or a grouping key is rejected; project it first.

Window ordering and final result ordering are separate. `OrderBy` on the
window defines its positions or peers, and `ThenBy` appends another key.
A subsequent window `OrderBy` replaces that window's earlier ordering.
No implicit primary-key tie-breaker is added. Use an explicit unique ordering
when stable individual positions matter. Adding a tie-breaker to `Rank` or
`DenseRank` deliberately changes which rows are peers.

`PartitionBy` accepts repeated scalar keys or an anonymous composite key.
Omitting `PartitionBy` treats the input at that SQL level as one partition.
Partition equality follows the mapped SQL values and database collation,
including NULLs, case/accent equivalence and trailing-space behavior. It is
not a promise that SQL partition equality equals arbitrary CLR equality.

## Calculations and result types

| Calculation | CLR result and semantics |
| --- | --- |
| `Count()` / `LongCount()` | `int` / `long`; count frame rows, including rows with NULL-valued columns. |
| `Sum(value)` | Standard `int`, `long`, `float`, `double`, `decimal` overloads and their nullable forms; typed zero for an empty or all-NULL frame. |
| `Average(value)` | Integer/long inputs produce `double?`, float produces `float?`, double produces `double?`, decimal produces `decimal?`; empty/all-NULL frames produce NULL. |
| `Min<T>(value)` / `Max<T>(value)` | Nullable mapped scalar result; NULL when the frame has no non-NULL value. |
| `RowNumber()` | `long`; starts at one within each ordered partition. |
| `Rank()` / `DenseRank()` | `long`; equal ordering keys share a rank; `Rank` leaves gaps and `DenseRank` does not. |
| `Ntile(buckets)` | `long`; one-based bucket number for a positive integer bucket count. |
| `PercentRank()` / `CumeDist()` | `double`; native relative rank / cumulative peer distribution. |
| `Lag<T>(value[, offset[, defaultValue]])` | Nullable previous partition value; omitted offset means one, zero means the current row. |
| `Lead<T>(value[, offset[, defaultValue]])` | Nullable following partition value under the same offset/default contract. |
| `FirstValue<T>(value)` / `LastValue<T>(value)` | Nullable first/last value of the selected frame, respecting NULLs. |
| `NthValue<T>(value, position)` | Nullable one-based frame value; NULL for a missing position or a NULL-valued row. |

Position/ranking/distribution/navigation functions require an explicit window
ordering. Count/aggregate calculations without an ordering use the whole
partition. Numeric overflow is not silently narrowed to the selected CLR type;
a value outside its representable range fails during native execution or
materialization. A `long` return type is not a promise of an unbounded count.
MariaDB distribution result fields declare ten decimal places, including
prepared reads. A CLR `double` does not restore precision rounded by the
server; cross-engine comparisons must respect that native result contract.

A frame can be empty for a particular source row even when its input column is
required. That is why Average, Min/Max and value/navigation functions expose
nullable results. Min/Max and navigation have separate reference-type and
nullable-struct overloads. Use an explicit type argument for a nonnullable
struct input, for example `.Lag<int>(row.Sequence)`; nullable inputs and
reference types normally infer it.

Mapped inputs retain their real converters, GUID representation and storage
facets. Scalar keys and arguments must translate to SQL. Incompatible default
representations or unsupported expressions are rejected; a matching CLR type
alone does not prove compatible stored values.

### Missing neighbors and NULLs

`Lag(value, offset, defaultValue)` uses the default only when the neighbor is
absent. An existing neighbor whose value is NULL remains NULL. The default can
be a compatible current-row expression and does not become a partition-wide
constant automatically.

MySQL has the native third argument. MariaDB uses an equivalent presence-marker
calculation over the identical partition/order; replacing `Lag` with `Coalesce`
would lose the distinction between an absent row and an existing NULL value.
The selected contract respects NULLs. There is no `IGNORE NULLS` option.

## Choose frame semantics explicitly

```csharp
var totals = await context.Readings
    .Select(row => new
    {
        row.Id,
        RunningTotal = EF.Functions.Window()
            .PartitionBy(row.DeviceId)
            .OrderBy(row.Timestamp)
            .ThenBy(row.Id)
            .RowsBetween(
                MySqlWindowFrameBound.UnboundedPreceding,
                MySqlWindowFrameBound.CurrentRow)
            .Sum(row.Value)
    })
    .OrderBy(row => row.Id)
    .ToArrayAsync(cancellationToken);
```

| Frame | Meaning and boundary |
| --- | --- |
| No ordering/frame on an aggregate | Whole partition. |
| Ordering with no explicit frame | Native default, normally peer-inclusive `RANGE ... CURRENT ROW`; not an implicit row-by-row running total. |
| `RowsBetween(start, end)` | Inclusive physical-row offsets; finite offsets must be nonnegative, integral and representable as `long`. |
| `RangeBetween(start, end)` | Inclusive peer/value bounds; finite offsets require exactly one numeric window ordering and use that engine's native boundary arithmetic. |

Fractional RANGE offsets do not make an integer ordering decimal. MariaDB
caches each computed boundary in the ordering key's type: with integer keys
`1, 1, 2`, bounds `0.75 PRECEDING` / `0.75 FOLLOWING` can include all three rows,
where the qualified MySQL form retains fractional bounds. This is conversion
of each boundary, not simply rounding the offset. An explicit
`.OrderBy((decimal)row.SortKey)` requests decimal boundary arithmetic;
the qualified decimal form retains the fractional distance on both engines.
Doka does not inject that cast or replace the selected native semantics.
The [MariaDB boundary-cache implementation](https://github.com/MariaDB/server/blob/mariadb-12.3.3/sql/sql_window.cc#L1297-L1348)
selects the cache from the original ordering expression.

Use `CurrentRow`, `UnboundedPreceding`, `UnboundedFollowing`,
`Preceding(offset)` and `Following(offset)` directly in the query expression.
Capture numeric offsets rather than an entire endpoint descriptor. A default
`MySqlWindowFrameBound` denotes `CurrentRow`.

Explicit frames require an ordering. Negative/fractional ROWS offsets,
reversed bounds, an unbounded-following start, an unbounded-preceding end and
multiple frames are rejected. Numeric RANGE is not a date/interval RANGE API.
There are no `GROUPS`, `EXCLUDE` or DISTINCT-aggregate window options.

Ranking/distribution and LAG/LEAD reject explicit frames because those
functions ignore or reject them natively. FIRST/LAST/NTH consume the frame;
in particular, ordered `LastValue` with the native default does not mean the
last row of the whole partition. Select an explicit whole-partition frame
when that is the intended result.

### Parameters and engine grammar

| Argument | MySQL 8.4 / 9.7 | MariaDB 10.11 / 11.4 / 11.8 / 12.3 |
| --- | --- | --- |
| LAG/LEAD offset | Nonnegative constant or parameter. | Same Doka contract. |
| NTILE buckets / NTH position | Positive constant or parameter. | Same Doka contract. |
| ROWS/RANGE finite frame offset | Qualified numeric constants and parameters. | Numeric literals only; a parameterized frame is rejected before its data command. |

Doka validates the current numeric parameters on every execution, including
cached/runtime-compiled queries. NULL, invalid signs, fractions in integral
positions, unsupported expressions and out-of-range integral values do not
acquire engine-specific coercion semantics.

ROWS factories accept decimal inputs for the shared endpoint type. A qualified
MySQL parameter must contain an integral value: Doka validates it and binds an
actual `Int64` provider value. Changing only its ADO.NET type while keeping a
CLR decimal is insufficient. RANGE retains its decimal value. MariaDB frame
parameters are rejected rather than interpolated into SQL or changed to a
different frame kind.

## Composition, models and provider boundaries

A newly introduced window after source paging or Distinct operates on that
completed source. A later filter/group/join consumes a completed window
projection at its own relational level. Joins still multiply source rows, and
windows do not silently deduplicate them. Directly nested windows on the same
SQL level are rejected; separate qualified projections establish separate
levels.

Ordinary query filters, explicit `IgnoreQueryFilters`, mapped entity identity,
tracking and qualified temporal/source lineage retain their normal contracts.
A window does not reconstruct entities merely from matching CLR types, grant
permission to write a bound source, or remove another operator's guards.

For split collections, keep a completed partitioned window relation separate
from a later correlation:

```csharp
var ranked = context.Entries.Select(row => new
{
    row.Id,
    row.AccountId,
    Position = EF.Functions.Window().PartitionBy(row.AccountId)
        .OrderBy(row.Id).RowNumber(),
});

var query = context.Entries.AsSplitQuery().Select(parent => new
{
    parent.Id,
    Members = ranked.Where(row => row.AccountId == parent.AccountId)
        .OrderBy(row => row.Id).Select(row => new { row.Id, row.Position }).ToList(),
});

var rows = await query.ToListAsync(cancellationToken);
```

The original correlation-before-window child shape has a separate MySQL
qualification. On MariaDB it requires a correlated derived table that its
`JOIN` grammar cannot express, and reports the existing
`MDB-CORRELATED-DERIVED-TABLE` boundary. Split execution may already have read
the parent command before generating that unsupported child command. This
is not a zero-parent-read guarantee and does not authorize client-side window
calculation or one query per parent. The completed-relation form above has
separate qualification cases across all six profiles; arbitrary collection
and split shapes are not covered by that contract.

The supported ordered indexed LINQ `Select((row, index) => ...)` uses zero-based
`int` indices, separately from one-based `RowNumber()`. Its source must retain
an explicit ordering. If Distinct, a set operation or grouping discards an
earlier ordering, order the completed source before the indexed selector.

Qualified grouped indexed selectors retain EF's grouping/result shape:

```csharp
var indexedMembers = context.Entries.GroupBy(row => row.AccountId)
    .Select(group => group.OrderBy(row => row.Timestamp).ThenBy(row => row.Id)
        .Select((row, index) => new { row.Id, Index = index }).ToArray());
```

| Indexed scope | Composition boundary |
| --- | --- |
| Ordered global source | Index the completed source, including its earlier filters or page. |
| Group sequence | OrderBy/ThenBy, Where, Skip and Take retain their separate relational levels. |
| Members within a group | Explicit member ordering and filtering before member paging are supported. |
| Unsupported member shapes | Member Distinct, filtering/reordering after member paging and nested group shapes fail before the data command. |

An unsupported shape is rejected rather than reusing an index from the wrong
scope. This is not a global rewrite of GroupBy into window functions.

Window methods are expression markers with no client implementation. Keep the
fluent chain inside the Doka query and finish it with a scalar calculation.
An escaped descriptor or invalid Doka expression is rejected before Doka's
data command. Foreign query providers are unsupported: Doka does not control
their translation or guarantee that their marker failure precedes a read.

Runtime `EF.CompileQuery` / `EF.CompileAsyncQuery`, SQL-expression quotation and
pregenerated/NativeAOT query execution are separate qualification surfaces.
EF Core 11's actual pregeneration compiler rejects the fluent `Window()`
receiver because its structural `functions` argument cannot be parameterized
in that pipeline, including windows with constant offsets. This is an early
framework boundary; ordinary query pregeneration remains available. Successful
SQL-expression quotation does not remove this restriction.
Reuse each compiled delegate only with the model it was compiled for; a
different model requires a separate delegate under EF's normal contract.
This API does not introduce a universal NativeAOT promise for arbitrary captured
queries or unsupported collection/split shapes. Existing EF and source-binding
boundaries remain in force.

## Work and resource expectations

These calculations run on the server; Doka does not preload rows to calculate
window values in memory. Existing EF buffering, tracking, connection and
cancellation rules still apply. Pass the actual cancellation token to async
materialization and dispose an unfinished async enumerator normally.

A window may require server sorts, peer/frame state and temporary storage.
Large/skewed partitions, join multiplicity, wide projections and several
independent window orderings can increase that work substantially. Inspect
actual plans and complete results for the application workload. Neither the
syntax nor a small fixture establishes a general speedup, fixed latency or
constant server memory. Qualification uses result/command/resource oracles,
not CPU- or load-dependent timing thresholds.

Ordinary CTEs retain native optimizer choices; windows add no once-only
materialization guarantee. `WithMaterializedSource` retains its distinct
sharing contract. Existing queries without a new window marker or supported
indexed selector retain their existing translation path.

## Primary sources

- [MySQL window descriptions](https://dev.mysql.com/doc/refman/8.4/en/window-function-descriptions.html)
- [MySQL frame semantics](https://dev.mysql.com/doc/refman/8.4/en/window-functions-frames.html)
- [MySQL window restrictions](https://dev.mysql.com/doc/refman/8.4/en/window-function-restrictions.html)
- [MariaDB window overview](https://mariadb.com/docs/server/reference/sql-functions/special-functions/window-functions/window-functions-overview)
- [MariaDB LAG](https://mariadb.com/docs/server/reference/sql-functions/special-functions/window-functions/lag)
- [MariaDB LEAD](https://mariadb.com/docs/server/reference/sql-functions/special-functions/window-functions/lead)
- [MariaDB frame-offset grammar](https://github.com/MariaDB/server/blob/mariadb-12.3.3/sql/sql_yacc.yy), `window_frame_start` and `window_frame_bound` productions
- [MariaDB distribution result precision](https://github.com/MariaDB/server/blob/mariadb-10.11.19/sql/item_windowfunc.h#L658-L663)
