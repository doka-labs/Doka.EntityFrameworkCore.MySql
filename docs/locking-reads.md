# Explicit locking reads

The typed APIs described here are available from Doka 10.7.0 on .NET 10 / EF Core 10.
The 11.0.0 preparation carries these contracts forward to .NET 11 / EF Core 11.

## Choose an explicit policy

Import `Doka.EntityFrameworkCore.MySql` and compose an ordinary Doka query:

```csharp
await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

var jobs = await context.Jobs
    .Where(job => !job.Completed)
    .OrderBy(job => job.Id)
    .Take(10)
    .ForUpdateSkipLocked()
    .ToListAsync(cancellationToken);

// Make the application changes while the transaction still owns its locks.
await context.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

| Operator | MySQL | MariaDB |
| --- | --- | --- |
| `ForUpdate()` | `FOR UPDATE` | `FOR UPDATE` |
| `ForShare()` | `FOR SHARE` | `LOCK IN SHARE MODE` |
| `ForUpdateNoWait()` | `FOR UPDATE NOWAIT` | `FOR UPDATE NOWAIT` |
| `ForUpdateSkipLocked()` | `FOR UPDATE SKIP LOCKED` | `FOR UPDATE SKIP LOCKED` |

Construction is deferred and performs no I/O. `ToQueryString()` can inspect
SQL without a transaction. Actual execution requires an active transaction
on the physical connection. Ordinary query tags remain diagnostic comments;
they cannot select, remove or override a locking policy.

Repeating the same policy is idempotent. Combining different policies fails,
regardless of call order. A provider must not silently turn NOWAIT into an
ordinary waiting read.

## Transaction and physical scope

Use transactional InnoDB tables. A known different storage-engine mapping is
rejected; Doka does not scan the database catalog for every query. Actual
physical tables must match the application's model and deployment contract.

The policy applies to the final executed direct SELECT block, including its
directly joined tables. It does not promise that only returned CLR entities
are locked. Native indexes, query plans and isolation levels determine record,
gap and next-key locks. Unmarked scalar subqueries remain separate scopes and
do not acquire a recursive locking policy.

Tracking and AsNoTracking retain their normal EF behavior. Direct inner/left
joins, reference includes, directed StraightJoin, filters, ordering and paging
are supported when the final SQL retains the qualified direct-table shape.

Initial unsupported forms fail before the locking data command: a marked
nested source, derived root, collection include, split query, set operation,
Distinct, GroupBy or aggregate terminal, FromSql, a view/keyless/temporal source,
WithCte, WithRecursiveCte, WithMaterializedSource and data-modification terminals.
These are deliberate API boundaries, not claims that every corresponding native
SQL form is invalid.

A translated locking statement must not contain any `WithCte`,
`WithRecursiveCte` or `WithMaterializedSource` binding, including inside a
projection's scalar subquery or an `EXISTS` predicate. This also applies when
the outer query reads only physical tables. Doka rejects the combination before
database I/O with `Locking reads cannot target materialized sources.`; that
message covers all three binding kinds. Ordinary scalar subqueries without
these bindings remain supported and do not inherit the outer locking policy.

Local transactions and correctly borrowed UseTransaction transactions must be
live and belong to the exact connection. An ambient TransactionScope counts
only after successful actual enlistment. Transaction.Current alone is not proof.
Connection close/reopen, replacement and pooled-context reset invalidate old
enlistment receipts. Explicit enlistment into a caller-owned
`CommittableTransaction` is also supported; an active TransactionScope is not
required for that route. Cached or runtime compiled queries recheck every execution.
Native pregenerated query execution is rejected until its generated command
metadata transport has been qualified; ordinary EF.CompileQuery is a separate
supported contract.

Before each locking read in a local transaction, Doka creates a unique
savepoint, immediately rolls back to that empty marker, and releases it.
These are transaction-control commands, not additional data reads. The
successful rollback-to checks native liveness: a driver transaction object
can remain associated after a server-side full rollback, and SAVEPOINT alone
can silently succeed outside a transaction. The receipt is never cached.
Caller work preceding the marker is preserved. As with other EF operations,
this guarantee assumes transaction interceptors do not suppress or replace
the native control operations.

A native deadlock or rollback-on-timeout can still abort the whole transaction
during the subsequent data command. Doka reports that failure; it cannot
restore earlier caller work or guarantee commit durability. Applications must
restart the whole transaction where appropriate. Ambient/XA enlistment is a
separate path and is not claimed to be proven by this local savepoint receipt.
For confirmed non-XA enlistment (`UseXaTransactions=false`), Doka checks native
liveness on the opened physical connection with the same three controls in one
command batch, without inventing an EF local transaction object. The captured
physical connection and managed transaction are checked around that batch.
Default XA enlistment keeps its existing separate behavior. Neither path
completes or disposes the caller's ambient transaction.

Doka does not create, commit or roll back an implicit application transaction.
Reader disposal does not commit it. Required failure cleanup retains caller
ownership of borrowed connections and transactions.

## Contention and engine boundaries

NOWAIT preserves the native conflict exception and does not automatically retry
that conflict. Ordinary lock-wait timeout retry rules remain unchanged. An
explicit whole-transaction retry by the application is a separate attempt.

SKIP LOCKED deliberately returns an incomplete candidate set; an empty result
is valid. It is useful for reservation, not a general consistent-snapshot or
completeness guarantee. MySQL documents NOWAIT/SKIP LOCKED as row-lock policies;
they do not eliminate every metadata, table or infrastructure wait.

No session timeout, encoding or isolation setting is changed. Statement-based
replication restrictions remain native engine concerns.

All six supported engine lines provide the selected forms. An explicit
AllowUnsupported configuration does not bypass their syntax version guards:
MySQL FOR SHARE/NOWAIT/SKIP LOCKED require 8.0.1; MariaDB NOWAIT requires 10.3.0
and SKIP LOCKED requires 10.6.0. This does not extend Doka's support matrix.

## Primary sources

- [MySQL 8.0.1 release notes](https://dev.mysql.com/doc/relnotes/mysql/8.0/en/news-8-0-1.html)
- [MySQL locking reads and replication caveats](https://dev.mysql.com/doc/refman/8.0/en/innodb-locking-reads.html)
- [MariaDB FOR UPDATE transaction and engine behavior](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/for-update)
- [MariaDB SELECT and SKIP LOCKED](https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/selecting-data/select#skip-locked)
- [MariaDB NOWAIT introduction](https://github.com/MariaDB/server/commit/8026cd6202)
- [MariaDB 10.6.0 release notes](https://mariadb.com/docs/release-notes/community-server/10.6/10.6.0)
