# Provider Configuration

This guide is the canonical map of application-facing provider configuration.
It groups the public entry points by lifetime and links specialized contracts
instead of duplicating them.

## Connection and Server Configuration

Configure the provider with exactly one of the `UseMySql(...)` inputs:

| Input | Use when | Ownership |
| --- | --- | --- |
| Connection string | The context should acquire pooled connector connections on demand. | The provider owns each opened connection; MySqlConnector owns the pool. |
| `DbConnection` | The host already created the connection or needs explicit connection lifetime control. | The host owns and disposes the supplied connection. |
| `MySqlDataSource` | Pooling, connector logging, TLS, credentials, or rotation are centralized by the host. | The host owns and disposes the data source. |

Every overload requires a `MySqlServerVersion`. Prefer an explicit supported
engine line in production:

```csharp
options.UseMySql(
    connectionString,
    MySqlServerVersion.MariaDb(new Version(11, 8, 5)),
    mysql =>
    {
        mysql.CommandTimeout(30);
        mysql.EnableRetryOnFailure(maxRetryCount: 5);
    });
```

`MySqlServerVersion.MySql(...)` and `MariaDb(...)` avoid family inference.
`Parse(...)` accepts a server version string, including MariaDB's legacy
`5.5.5-...-MariaDB` prefix. Detection has two ownership contracts:

| Entry point | I/O and lifetime |
| --- | --- |
| `AutoDetect(string connectionString)` | Creates one temporary `MySqlConnection`, opens it synchronously once, reads the version, and disposes it on success or failure. |
| `AutoDetect(DbConnection)` | Reads the supplied connection's `ServerVersion`; does not open or dispose the caller-owned connection. Open it before detection. |

The connection-string overload is part of
[Unreleased](../CHANGELOG.md#unreleased), not the published `10.0.0` package:

```csharp
var serverVersion = MySqlServerVersion.AutoDetect(connectionString);
options.UseMySql(connectionString, serverVersion);
```

Reuse the returned immutable descriptor for an unchanged target rather than
detecting it every time a context is created. Doka does not cache or log the
connection string or add discovery retries. The temporary connection forces
`AutoEnlist=false`, even if the supplied string explicitly enables it, so
discovery does not join the caller's ambient transaction. Normal provider
connections and caller-owned detection connections keep their transaction
behavior. Pooling remains owned by MySqlConnector; the effective connection
options can select a pool distinct from normal provider connections.
Connection failures remain observable; detection is not an offline fallback.

`UseMySql(string, ...)` and `AutoDetect(string)` reject null, empty, or
whitespace input. Detection uses `SupportedOnly`; unsupported lines fail
provider option validation by default. `AllowUnsupported` remains an explicit
compatibility escape hatch on the existing descriptor and connection APIs,
not a support promise. See [Supported Databases](supported-databases.md) and
[Migrating from Pomelo](migrating-from-pomelo.md) for replacement examples.

## Context Options

The callback passed to `UseMySql(...)` exposes:

| Option | Contract |
| --- | --- |
| `CommandTimeout(seconds)` | Positive timeout applied to provider commands, including migrations and advisory-lock acquisition. |
| `DefaultGuidFormat(format)` | Default `Binary16` or `Char36` mapping for GUID properties. |
| `EnableRetryOnFailure(count, delay)` | Opts into the provider execution strategy for classified transient failures. |
| `MaxBatchSize(count)` | Caps statements in one modification batch; packet and parameter ceilings can split it further. |
| `MinBatchSize(count)` | Sets the minimum accumulated statement count for batched modification execution. |
| `MigrationsHistoryTable(name, schema)` | Selects the history table. A non-empty schema is rejected because MySQL-family databases do not implement EF Core schema semantics. |
| `UseQuerySplittingBehavior(behavior)` | Selects EF Core single- or split-query behavior for collection includes. |
| `UseNetTopologySuite()` | Activates services from the optional NetTopologySuite provider package. |

Retry policy, commit-unknown handling, and proxy requirements are owned by
[Resilience and Topology](operations/resilience-and-topology.md). Host-side
dependency injection, telemetry, health checks, and data-source setup are owned
by [Host Integration](host-integration-examples.md).

## Model Configuration

| Scope | API | Contract |
| --- | --- | --- |
| Model | `HasCharSet(charSet)` | Sets the model default character set. |
| Entity | `HasCharSet(charSet)` | Overrides the table character set. |
| Entity | `UseStorageEngine(engine)` | Selects the table storage engine. |
| Index | `HasPrefixLength(lengths)` | Supplies one non-negative prefix length per indexed property; zero selects the complete value. |
| Index | `IsFullText()` | Marks the index as full text. |
| Property | `HasMySqlGuidFormat(format)` | Overrides GUID storage for one property. |
| Property | `HasMySqlValueGenerationStrategy(strategy)` | Selects `None`, `AutoIncrement`, `ClientGuid`, or `HiLo`. |
| Property | `UseMySqlAutoIncrementColumn()` | Selects auto-increment value generation. |
| Property | `UseMySqlClientGuidValueGeneration()` | Selects explicit client-side GUID generation. |
| Property | `UseHiLo(name, schema)` | Selects provider HiLo generation; MySQL emulates sequences and MariaDB uses native sequences. |
| Property | `IsInvisible()` | Marks a supported engine column `INVISIBLE`. |
| Spatial property | `HasSrid(srid)` | Registers the non-negative SRID expected by the spatial mapping. |
| Spatial index | `IsSpatial()` | Marks an explicit single-column spatial index. |

Temporal, application-time, and bitemporal table builders are documented in
[Temporal Tables](temporal-tables.md). The `UseWithoutOverlaps()` key and index
configuration is part of that same contract. Custom migration-operation
registration is documented in
[Migration Operation Handlers](migration-operation-handlers.md).

The separate `Doka.Caching.MySql` package registers
`AddDistributedMySqlCache(...)` without a `DbContext` or provider dependency.
Its `MySqlCacheOptions`, deployment-time `MySqlCacheSchema.GetCreateScript(...)`,
and runtime lifetime are owned by [Distributed Cache](distributed-cache.md).

## Reverse Engineering and Services

`AddEntityFrameworkDokaMySql(...)` registers runtime provider services for
advanced service-provider composition. Normal `DbContext` configuration should
use `UseMySql(...)` instead. The optional
`AddEntityFrameworkDokaMySqlNetTopologySuite(...)` extension adds its spatial
services to the same collection.

Design-time registration uses `AddEntityFrameworkDokaMySqlDesignTime(...)`.
Its `ScaffoldTextGuidsAsGuids()` option asks reverse engineering to treat
compatible `char(36)` and `varchar(36)` columns as `Guid`; it is opt-in because
text columns are not intrinsically GUID columns.

## Configuration Precedence

Use the narrowest scope that expresses the contract:

1. Provider options establish connection- and context-wide behavior.
2. Model and entity configuration establish database defaults.
3. Property or index configuration overrides the relevant default.
4. Engine capability validation remains authoritative; fluent configuration
   cannot make unsupported server syntax valid.

Do not set connector GUID conversion options behind the provider. The
provider's `DefaultGuidFormat(...)` and `HasMySqlGuidFormat(...)` own the model,
parameter, literal, migration, and materialization contract together.

## Runnable Verification

- [HostExamples](../examples/Doka.EntityFrameworkCore.MySql.HostExamples/README.md)
  covers dependency injection, data sources, health checks, telemetry, and
  legacy `Char36` configuration.
- [GuidFormats](../examples/GuidFormats/README.md) covers provider- and
  property-level GUID selection.
- [CharSetAndCollation](../examples/CharSetAndCollation/README.md) covers model,
  entity, storage-engine, and index-prefix configuration.
- [SpatialQueries](../examples/SpatialQueries/README.md) covers
  `UseNetTopologySuite()`, SRIDs, and spatial indexes.
- The HiLo functional and integration suites cover native MariaDB sequences and
  MySQL sequence emulation.

`./eng/test-examples.sh` builds and verifies the public examples. Unit,
functional, and integration suites pin option validation, metadata precedence,
generated SQL, reverse engineering, and every supported engine line.
The server-version unit and driver integration tests cover both detection
entry points; the connection-string path is exercised against the supported
MySQL and MariaDB targets.

## Primary Sources

Retrieved 2026-08-21:

- [EF Core database providers](https://learn.microsoft.com/ef/core/providers/)
- [EF Core connection strings](https://learn.microsoft.com/ef/core/miscellaneous/connection-strings)
- [EF Core connection resiliency](https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency)
- [EF Core efficient querying and split queries](https://learn.microsoft.com/ef/core/performance/efficient-querying)
- [EF Core value generation](https://learn.microsoft.com/ef/core/modeling/generated-properties)
- [MySqlConnector data sources](https://mysqlconnector.net/overview/)

Retrieved 2026-08-26 for connection-string detection:

- [MySqlConnector connection opening](https://mysqlconnector.net/api/mysqlconnector/mysqlconnection/open/)
- [MySqlConnector connection reuse](https://mysqlconnector.net/troubleshooting/connection-reuse/)
