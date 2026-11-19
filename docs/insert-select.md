# Typed INSERT SELECT

The typed APIs described here are available from Doka 10.7.0 on .NET 10 / EF Core 10.

`ExecuteInsertFromQueryAsync` inserts the occurrences of an EF query into a mapped
physical table. Its assignment expressions become one native `INSERT ... SELECT`
data statement. Doka does not preload source rows, construct target entities,
call `SaveChanges`, attach results or read generated values back.

```csharp
var inserted = await context.PendingRows
    .Where(row => row.BatchId == batchId)
    .ExecuteInsertFromQueryAsync(
        context.ImportRows,
        setters => setters
            .SetProperty(target => target.SourceId, source => source.Id)
            .SetProperty(target => target.Code, source => source.Code),
        cancellationToken);
```

Source and target must belong to the same current Doka context and model. The
target is a `DbSet<TTarget>`, not a filtered query. Assignments use one typed
`SetProperty` language; the callback records expressions once and never runs for
individual source rows. Selectors must preserve the mapped property type.

## Mapped properties and defaults

Property selection uses EF metadata and the actual target column mapping. For
shadow or field-only properties, use the mapped EF property name:

```csharp
.SetProperty(target => EF.Property<string>(target, "ImportTag"), _ => importTag)
.SetProperty(target => EF.Property<string>(target, "_code"), source => source.Code)
```

The field-only property must already be configured in the model. This API does
not infer new columns from expression names or construct a target object graph.

Omitting a column keeps its real database default or generation behavior. This is
different from assigning SQL NULL:

```csharp
.SetProperty(target => target.Comment, _ => (string?)null)
```

That assignment supplies NULL, even if `Comment` has a server default. Omitted
nullable columns, computed columns and actual server-generated/defaulted columns
retain native behavior. A client-only GUID generator does not create a server
default: a required column without an assigned value or server generation is
rejected before writing. An installed `UUID()` default executes on the server
for each inserted row.

Computed/read-only assignments, duplicate properties, conflicting assignments to
a shared column and missing required physical columns are rejected. Concrete
one-table TPH targets use their model discriminator automatically; explicitly
assigning another discriminator is rejected. Complete, unambiguous one-table
shared/owned scalar mappings can target the same physical column. Multitable
TPT/entity splitting, view-only mappings, keyless targets and navigation graph
writes are not implicit insertion strategies. Targets with an insert
stored-procedure mapping are rejected rather than bypassing that mapping.

## Source semantics

Normal source filters, navigation filters, joins, grouping, set operations,
DISTINCT and ordered paging stay in the existing EF SQL pipeline. Occurrence bags
remain bags unless the source explicitly removes duplicates. Source
`IgnoreQueryFilters()` remains an explicit opt-out.

Target filters do not authorize or rewrite an insert. In particular, an EF tenant
filter on the target does not validate the tenant ID assigned by this operation.
Applications must supply their own authorization and target-value policy.

Collection Includes and split queries are rejected before projection can discard
them. A reference navigation can supply a SQL value; no Include graph is loaded.
An explicit `WithMaterializedSource` remains read-only, including when nested
inside another source composition.

[Ordinary and recursive CTEs](ctes.md) and
[window-function source projections](window-functions.md) compose with this
operation. Their native SQL starts with `INSERT INTO target (...) WITH ... SELECT`
where a CTE header is required. Recursive INSERTs use a dedicated nonquery budget
guard, without the SELECT reader's private control columns. An unsafe budget
raises a native error even if the completed payload is empty, filtered out or
`Take(0)`. Doka does not increase the session's recursion limit. The engine's
recursive-step restrictions still apply.

Simple top-level same-table sources are supported. An unqualified nested target
self-subquery is rejected. Temporary-table reopening, constraints, triggers and
other native DML restrictions retain the engine's behavior; a temporary table
is not inferred from EF metadata.

## Client code and conversions

Source and assignment expressions must be SQL-translatable. Within source row
lambdas, closed application constructors, methods, getters and captured delegates
are rejected before EF can evaluate them during compilation. Compute a required
application scalar first and capture that scalar in the query instead.

Captured scalar fields, pure framework scalar operations, normal context-set
lookup and registered SQL-only functions retain their normal EF role. A
row-dependent anonymous/DTO initializer can describe a SQL projection without
running its constructor. This does not translate arbitrary constructor, setter
or getter bodies into SQL. Unsupported client shapers fail before data execution.

Compatible custom converters can copy an already compatible store representation
without invoking a CLR converter for each source row. Arbitrary differences
between converters are rejected. Canonical GUID conversions between Binary16,
Char36 and supported MariaDB native UUID mappings use explicit server
expressions. Equal custom GUID converters on the same store type copy the stored
bytes/text; this does not imply conversion between arbitrary custom encodings.

Native target type, precision, width, collation and SQL-mode behavior still apply.
Doka does not claim lossless truncation or change the session to strict mode.
Permissive-mode warnings retain native semantics.

## Transactions, cancellation and affected rows

An autocommit operation executes one INSERT data statement. Inside a borrowed
local transaction, Doka additionally performs three transaction controls:
`SAVEPOINT`, immediate `ROLLBACK TO` and `RELEASE`. They prove that the server
still has the transaction: a client transaction object can remain associated
after a deadlock or implicit server rollback. The fresh marker does not roll back
prior caller work or commit, fully roll back or dispose the caller's transaction.
An actually enlisted non-XA ambient transaction is checked on the opened physical
connection too: its three controls run in one native command batch without an EF
local transaction object. Default XA enlistment and ordinary provider commands
retain their existing behavior. Neither receipt completes or disposes the caller's
ambient scope.

`CancellationToken` is required and forwarded to execution. Invariant-restoring
cleanup completes without cancellation. Existing caller connections and
transactions remain caller-owned. End the caller's transaction or ambient scope
after a transaction-aborting failure before starting a new attempt.

Doka performs no implicit execution-strategy retry for this write. A retry after
an unknown outcome could insert the same rows twice. The result is the driver's
native `int` affected-row count, not generated IDs or trigger-side-effect counts.
The driver performs a checked conversion: `OverflowException`, cancellation or
connection loss after execution does not establish that an autocommitted INSERT
was rolled back. The operation performs no verification SELECT or automatic
rollback to manufacture such a guarantee.

Known non-InnoDB target mappings are rejected. Statement-atomicity qualification
uses actual InnoDB tables. A model cannot prove that an externally modified table
still has its declared engine, defaults or constraints.

## Compiled queries and deployment

An asynchronous terminal can be compiled as a `Task<int>` result with
`EF.CompileQuery`:

```csharp
var insert = EF.CompileQuery(
    (AppDbContext db, int minimum, CancellationToken token) =>
        db.PendingRows.Where(row => row.Id > minimum)
            .ExecuteInsertFromQueryAsync(
                db.ImportRows,
                setters => setters
                    .SetProperty(target => target.SourceId, source => source.Id)
                    .SetProperty(target => target.Code, source => source.Code),
                token));

var count = await insert(context, minimum, cancellationToken);
```

Use an expression-bodied fluent assignment chain in compiled queries. Current
scalar parameters, context ownership and the supplied token are rebound; no
`DbContext`, `DbSet` or token source is retained in the SQL cache. Normal EF
compiled-query model compatibility rules still apply. This is not a promise that
`EF.CompileAsyncQuery` unwraps an additional `Task<Task<int>>` terminal.

This feature uses normal runtime EF query compilation. Pregenerated/NativeAOT
execution of the INSERT terminal is explicitly rejected; existing provider AOT
paths do not gain an implied INSERT-terminal qualification.

## Qualification boundaries

Qualification covers all six pinned profiles (MySQL 8.4/9.7 and MariaDB
10.11/11.4/11.8/12.3), positive and negative mapping/source/transaction cases,
native affected-count and no-readback checks, and a complete 14,000-row,
50-column fidelity case. Timing and allocation observations are diagnostic;
there is no CPU- or load-dependent time gate.
