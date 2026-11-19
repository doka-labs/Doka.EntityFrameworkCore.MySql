# Native UPSERT

The typed APIs described here are available from Doka 10.7.0 on .NET 10 / EF Core 10.
The 11.0.0 preparation carries these contracts forward to .NET 11 / EF Core 11.

## Choose the update properties explicitly

Import `Doka.EntityFrameworkCore.MySql`. Supply an entity and a nonempty list
of EF property names:

```csharp
var candidate = new Product
{
    Id = productId,
    Name = name,
    Price = price,
};

long affectedRows = await context.ExecuteUpsertAsync(
    candidate,
    new[] { nameof(Product.Name), nameof(Product.Price) },
    cancellationToken);
```

`ExecuteUpsertRangeAsync` accepts a stable `IReadOnlyList<TEntity>` and the
same update selection:

```csharp
long affectedRows = await context.ExecuteUpsertRangeAsync(
    candidates,
    new[] { nameof(Product.Name), nameof(Product.Price) },
    cancellationToken);
```

Both methods require an explicit `CancellationToken` and return `Task<long>`.
A valid empty range returns zero without acquiring a connection. A cancelled
empty range still reports cancellation. Inputs must remain stable throughout
the asynchronous call.

Insert candidates use their mapped writable values. A conflict updates only
the explicitly selected properties. Unselected existing values remain intact.
Names refer to EF metadata, including supported field-only properties; they
are not SQL column names or SQL fragments. Unknown, duplicate or empty
selections fail before writing. Keys, computed columns and properties whose
after-save behavior forbids updates cannot be selected.

## Every native unique key can select the conflict

Doka generates `INSERT ... ON DUPLICATE KEY UPDATE`. There is no named
conflict-target selection. A primary key, alternate unique index, composite
key or prefix index can select an existing row. Native collation, case,
trailing-space and nullable-key rules apply.

If different unique indexes identify different existing rows, the native
engine determines the outcome. An update can itself fail on another unique,
foreign-key, check or NOT NULL constraint. Doka does not replace that outcome
with a client-side lookup or a chosen-key emulation.

Query filters do not authorize these writes. A tenant filter does not protect
a tenantless primary or unique key. Tenant isolation requires every reachable
conflict key to include the tenant, or an equivalent database-enforced boundary.
A tenant-qualified alternate key does not make an additional tenantless
primary key safe.

Doka requires matched-row semantics (`UseAffectedRows=false`). Native counts
are one for an insert, two for a changed duplicate update, and one for an
unchanged duplicate update under that connection contract. Range results are
a checked `long` sum across data commands. They do not classify each input as
inserted or updated, identify which unique key matched, or prove durability.
See the [connection contract](provider-configuration.md).

## Mapping, defaults and tracking

Parameters use the actual physical column mapping and its value converter.
Doka resolves the mapping and getter once per write shape and reads each
candidate property once. Converted values are carried into the command without
repeating conversion when a pending candidate moves to the next batch.
Mapped identifiers are quoted; payloads remain parameters.

Configured sentinels and before/after-save behavior remain effective. A
qualified server-generated sentinel uses a native `DEFAULT` cell. Identity,
SQL defaults such as `UUID()`, and computed columns retain their mapped
contracts. Computed columns are omitted from input writes. If a defaulted
property is explicitly selected for update, a conflict uses the evaluated
incoming default, rather than preserving the old value or writing its sentinel.
An explicit NULL is distinct from a custom non-null sentinel.

Permanent client-generated values must already be supplied. Doka does not
invoke GUID or HiLo generators, attach input entities, call `DetectChanges`,
or manufacture missing keys. A temporary client-only value is rejected;
a qualified temporary server-generated value can select its native default.
Arbitrary shadow-input columns are unsupported. A shadow property with a
qualified generated server default can use that default or an existing tracked
current value.

There is no generated-value readback. Successful writes do not replace input
keys, populate computed/defaulted properties, change entity state, synchronize
tracked current/original values, or fix up navigations. Query separately when
fresh database values are required.

Initial unsupported models fail before data writes: inheritance, including
TPH/TPT/TPC; owned or complex aggregates; shared tables or multiple physical
tables; stored-procedure write mappings; and optimistic-concurrency tokens.
The target must be a concrete keyed physical table. A known non-InnoDB mapping
is rejected. Actual tables and trigger side effects must satisfy the deployment's
transactional contract; Doka does not inspect every trigger or database catalog
on each call.

Native triggers retain native behavior. For example, a BEFORE INSERT trigger
combined with a NOT NULL stored generated column can produce a MySQL constraint
error even for an independent plain INSERT. Doka preserves that error rather
than changing the schema or substituting another write operation.

## Bounded batches and native dialects

Contiguous batches preserve input order, including repeated keys across a
batch boundary. The builder retains only the current bounded batch and one
pending candidate; it does not snapshot all input values or parameters.

The provider limits a command to 1,000 candidates, 65,535 parameters and a
conservative 4 MiB payload budget. Budgeting uses actual converted UTF-8 or
binary values together with SQL/protocol allowances. A single candidate that
cannot fit fails instead of being truncated or retried indefinitely.
These are provider limits, not a discovered server `max_allowed_packet` value;
a lower server limit can still produce a native error.

MySQL uses an incoming-row alias. The qualified alias form requires MySQL
8.0.19 or later; this does not expand Doka's supported-engine policy. MariaDB
uses `VALUES(column)` for incoming values. Unsupported syntax or type mappings
fail explicitly; Doka does not silently substitute MySQL's deprecated
`VALUES(column)` form or emulate native UUID storage.

## Transaction ownership and liveness

| Call state | Boundary |
| --- | --- |
| One data command, no caller transaction | Native InnoDB statement atomicity; no extra application transaction. |
| Multiple data commands, no caller transaction | One invocation-owned local transaction established before the first write. |
| One data command in a local caller transaction | Fresh local liveness receipt before writing; caller keeps ownership. |
| Multiple data commands in a local caller transaction | Validated invocation savepoint established before the first write; caller keeps ownership. |
| One data command in an ambient scope | Requires successful actual enlistment on the physical connection. |
| Multiple data commands in an ambient scope | Rejected before writing because that path lacks a qualified local savepoint boundary. |

An EF transaction or one supplied through `UseTransactionAsync` must belong to
the exact open connection and support savepoints. A driver transaction object
can remain associated after a native full rollback. Even SAVEPOINT alone can
silently succeed outside a transaction.

For a single-command borrowed local call, Doka creates a fresh unique SAVEPOINT,
immediately rolls back to that empty marker, then releases it. These three
transaction-control commands are separate from the data-command count and do
not discard prior caller work. A multi-command borrowed call retains the
validated marker until success or invocation rollback. The receipt is checked
for each invocation, not cached. Transaction interceptors that suppress or
replace native control operations are outside this liveness proof.

On ordinary late binding or statement failure, the invocation boundary rolls
back earlier call batches while preserving prior caller work when the native
transaction remains usable. Doka never commits, fully rolls back, disposes or
replaces the caller's transaction. Borrowed connections and data sources retain
their normal ownership contract.

A deadlock or `innodb_rollback_on_timeout` can abort the whole native transaction,
including prior caller work. A savepoint cannot repair that abort. Error 1205
alone cannot determine whether the transaction survived; server policy and
syntax affect its scope. A later invocation rejects a stale local transaction
before its data command instead of allowing an unintended autocommit.

Ambient `TransactionScope` is a separate path. `Transaction.Current` alone
is not proof of native enlistment. Local savepoint receipts do not establish
ambient/XA liveness or provide an ambient multi-command atomicity guarantee.

## Failures and cancellation

The operation does not enter a configured execution strategy or retry a native
command automatically. It performs no hidden pre-read or generated-value
readback. Ordinary SaveChanges, queries, migrations and cache UPSERT retain
their existing contracts.

Forward work observes the required cancellation token. Rollback, marker cleanup
and owned-resource release must finish independently of an already cancelled
request. Native primary failures remain visible. If cleanup also fails, both
causes are retained, including through `AggregateException`; do not assume one
native error code describes every failure in that aggregate.

A connection loss or exception around commit can leave the outcome unknown.
Even an error result is not proof that no write committed. Doka does not replay
the call or claim a successful rollback after a possible commit. The application
must reconcile uncertain outcomes using its transaction and idempotency policy.
