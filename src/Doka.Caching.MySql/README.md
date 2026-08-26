# Doka.Caching.MySql

`Doka.Caching.MySql` provides the .NET 10 `IDistributedCache` and
`IBufferDistributedCache` contracts for MySQL and MariaDB through
MySqlConnector.

It is independent of Entity Framework Core: no provider package or `DbContext`
is required. The package targets .NET 10.

## Install the Release Candidate

Pin the current 10.1.0 candidate explicitly for consumer validation:

```bash
dotnet package add Doka.Caching.MySql --version 10.1.0-rc.2
```

This package is introduced in 10.1.0-rc.1; it is not part of the 10.0.0 release.

## Deploy and Register

The application runtime never creates or updates database objects. Generate
the idempotent deployment script with `MySqlCacheSchema.GetCreateScript`, run
it through the deployment process, and then register the cache:

```csharp
using Doka.Caching.MySql;
using Microsoft.Extensions.DependencyInjection;

var script = MySqlCacheSchema.GetCreateScript(databaseName, "DistributedCache");

services.AddDistributedMySqlCache(options =>
{
    options.ConnectionString = connectionString;
    options.SchemaName = databaseName;
    options.TableName = "DistributedCache";
});
```

Execute `script` separately with deployment privileges before using the cache.
The script creates schema version 1 in an existing database. `IF NOT EXISTS`
does not validate or upgrade a table owned by another cache implementation.
The application needs only `SELECT`, `INSERT`, `UPDATE`, and `DELETE` on the
Doka table. Migration from another cache starts with a new, cold table.

The runtime does not check schema versions or automatically version table names.
For future incompatible schema changes, keep old and new application instances
on separate tables, and retain the old table through the rollback window.
Separate tables do not share values or invalidations. Schema-compatible package
updates can continue to share the same table.

Both interfaces resolve the same singleton. Registration preserves foreign
registrations while selecting Doka as the default; repeated registration does
not duplicate Doka's aliases. Apply decorators after choosing the backend.
Every cache statement qualifies `SchemaName`; the connection string does not
need to select a default database.

Alternatively, set `options.DataSource` to an existing `MySqlDataSource`
instead of `ConnectionString`. The supplied source must use `AutoEnlist=false`
and remain alive for the cache's lifetime. The cache never disposes a supplied
source; it owns only sources created from a connection string. Configure pool
sharing and rotating credentials through MySqlConnector's builder.

## Use the Standard Contract

```csharp
using Microsoft.Extensions.Caching.Distributed;

await cache.SetAsync(
    key,
    payload,
    new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        SlidingExpiration = TimeSpan.FromMinutes(2),
    },
    cancellationToken);

var cached = await cache.GetAsync(key, cancellationToken);
```

An entry without explicit expiration uses a 20-minute sliding lifetime.
Expiration uses database UTC. Absolute deadlines cap sliding refreshes, and
expired entries are not returned. Core async operations propagate cancellation;
best-effort cleanup does not discard an already completed operation.

`IBufferDistributedCache` reads into a consumer-owned `IBufferWriter<byte>`
without a second value-sized result array. A single-segment write avoids
flattening; multi-segment writes rent and clear a contiguous buffer. This is
not a zero-copy or allocation-free claim about the driver or the consumer's
buffers.

Cleanup selects at most 1,000 expired keys without locks, then deletes by
primary key with an expiration recheck after a successful operation when the
interval is due (30 minutes by default, at least five minutes). The caller
observes that work; a full candidate batch allows the next successful cache call
to continue immediately, even if concurrent renewals reduce actual deletions.
A drained or failed attempt restarts the interval. Each
call remains bounded to one batch; no background task or runtime schema management is added.
Idle instances do not clean up rows. Monitor table growth independently.
An optional DI-registered `TimeProvider` controls only this cleanup cadence;
without one the cache uses `TimeProvider.System`. Entry expiration remains based
on database UTC.

Cache keys, values, and connection strings are never written to Doka logs.
Keys are case-sensitive, limited to 1,024 UTF-8 bytes, and preserve trailing
spaces.

See the [Distributed Cache guide](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/distributed-cache.md)
for schema, expiration, concurrency, buffer ownership, validation, and primary
sources, and [Migrating from Pomelo](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/migrating-from-pomelo.md)
for application migration guidance.
