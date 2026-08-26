# Doka.EntityFrameworkCore.MySql

[![CI](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/workflows/ci.yml/badge.svg)](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/workflows/ci.yml)
[![NuGet MySQL / MariaDB](https://img.shields.io/nuget/v/Doka.EntityFrameworkCore.MySql.svg?label=NuGet%20MySQL%20%2F%20MariaDB)](https://www.nuget.org/packages/Doka.EntityFrameworkCore.MySql)
[![NuGet NetTopologySuite](https://img.shields.io/nuget/v/Doka.EntityFrameworkCore.MySql.NetTopologySuite.svg?label=NuGet%20NetTopologySuite)](https://www.nuget.org/packages/Doka.EntityFrameworkCore.MySql.NetTopologySuite)
[![NuGet Caching](https://img.shields.io/nuget/vpre/Doka.Caching.MySql.svg?label=NuGet%20Caching)](https://www.nuget.org/packages/Doka.Caching.MySql)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/LICENSE)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/doka-labs/Doka.EntityFrameworkCore.MySql/badge)](https://scorecard.dev/viewer/?uri=github.com/doka-labs/Doka.EntityFrameworkCore.MySql)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/13999/badge)](https://www.bestpractices.dev/projects/13999)

`Doka.EntityFrameworkCore.MySql` is an Entity Framework Core 10 provider for
MySQL and MariaDB, built on the asynchronous
[`MySqlConnector`](https://mysqlconnector.net/) ADO.NET driver. It provides one
EF Core model across both database families while keeping engine differences
explicit, testable, and observable.

Use it when an application needs current .NET and EF Core support, a small
public API, portable MySQL/MariaDB behavior, and qualification against every
advertised LTS line.

## Packages

| Package | Purpose |
| --- | --- |
| [`Doka.EntityFrameworkCore.MySql`](https://www.nuget.org/packages/Doka.EntityFrameworkCore.MySql) | Core EF Core provider, migrations, scaffolding, type mappings, and query translation |
| [`Doka.EntityFrameworkCore.MySql.NetTopologySuite`](https://www.nuget.org/packages/Doka.EntityFrameworkCore.MySql.NetTopologySuite) | Optional NetTopologySuite mappings, spatial indexes, scaffolding, and spatial query translation |
| [`Doka.Caching.MySql`](https://www.nuget.org/packages/Doka.Caching.MySql) | Standalone .NET 10 `IDistributedCache` and `IBufferDistributedCache` implementation; introduced in 10.1.0-rc.1 |

The cache package, connection-string detection, and scalar `Like<T>` are
introduced in [10.1.0-rc.1][changelog]. They are not present in the `10.0.0`
packages. Use the explicit release-candidate versions below to test them.

## Requirements

- An application targeting .NET 10 or later
- EF Core 10.0.x for the provider and spatial extension; the supported package
  range is `>= 10.0.8` and `< 10.1.0`
- MySqlConnector 2.x; the supported package range is `>= 2.5.0` and `< 3.0.0`
- A supported MySQL or MariaDB server from the matrix below

Building the repository itself requires the exact .NET SDK declared in
[`global.json`][global-json]. Docker is required only for live integration,
example, benchmark, and release-qualification runs.

## Install

The following .NET 10 command installs the latest stable provider package and
writes its resolved version to the project file:

```bash
dotnet package add Doka.EntityFrameworkCore.MySql
```

Add the spatial extension only when the model uses NetTopologySuite types:

```bash
dotnet package add Doka.EntityFrameworkCore.MySql.NetTopologySuite
```

For reproducible installs, add `--version` followed by the exact version from
the [GitHub release](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/releases)
or NuGet.org package page.

### Test the Release Candidate

Pin the candidate version explicitly to test the new 10.1.0 provider APIs:

```bash
dotnet package add Doka.EntityFrameworkCore.MySql --version 10.1.0-rc.2
```

Add the optional packages only when needed, using the same candidate version:

```bash
dotnet package add Doka.EntityFrameworkCore.MySql.NetTopologySuite --version 10.1.0-rc.2
dotnet package add Doka.Caching.MySql --version 10.1.0-rc.2
```

The cache can be used on its own without the EF Core provider. The RC is for
consumer validation and is not selected by normal stable-version resolution.

## Quick Start

Configure the provider with the database family and server release line, then
use the normal EF Core APIs:

```csharp
using Doka.EntityFrameworkCore.MySql;
using Microsoft.EntityFrameworkCore;

var connectionString =
    "Server=localhost;Database=my_app;User ID=app;Password=secret;";

var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseMySql(connectionString, serverVersion)
    .Options;

await using var context = new AppDbContext(options);

await context.Database.EnsureCreatedAsync();
context.Products.Add(new Product { Name = "Widget", Price = 9.99m });
await context.SaveChangesAsync();

var products = await context.Products
    .AsNoTracking()
    .OrderBy(product => product.Name)
    .ToListAsync();
```

`EnsureCreatedAsync()` keeps this first-use example small. Applications whose
schema evolves should use [EF Core migrations](#migrations) instead.

MariaDB uses the same provider surface with a different version factory:

```csharp
var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
```

When the exact server version is not known during configuration, detect it from
an open connection:

<!-- readme-autodetect-snippet begin -->

```csharp
using Doka.EntityFrameworkCore.MySql;
using MySqlConnector;

var connectionString =
    "Server=localhost;Database=my_app;User ID=app;Password=secret;";

await using var connection = new MySqlConnection(connectionString);
await connection.OpenAsync();

var serverVersion = MySqlServerVersion.AutoDetect(connection);
```

<!-- readme-autodetect-snippet end -->

The new connection-string overload manages the temporary connection itself:

```csharp
var serverVersion = MySqlServerVersion.AutoDetect(connectionString);
```

It opens synchronously once and disposes the connection on success or failure.
Reuse the descriptor for an unchanged server target instead of detecting it
for every context. Both detection paths use `SupportedOnly` by default; see
[Provider Configuration][provider-configuration].

## Dependency Injection

Register a context with a connection string:

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        connectionString,
        MySqlServerVersion.MySql(new Version(8, 4, 0))));
```

For centralized pooling and connector logging, register a
`MySqlDataSource` instead:

```csharp
var dataSource = new MySqlDataSourceBuilder(connectionString)
    .UseLoggerFactory(loggerFactory)
    .Build();

services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        dataSource,
        MySqlServerVersion.MySql(new Version(8, 4, 0))));
```

The provider also accepts an existing `DbConnection`. See
[Host Integration][host-integration] for connection ownership, pooling, retry,
health-check, and telemetry guidance.

## Supported Engines

| Engine | Supported line | JSON storage | Sequences | `RETURNING` | Temporal tables |
| --- | --- | --- | --- | --- | --- |
| MySQL | 8.4 LTS | native | emulated | unavailable in the engine | provider emulation |
| MySQL | 9.7 LTS | native | emulated | unavailable in the engine | provider emulation |
| MariaDB | 10.11 LTS | validated alias | native | native | native |
| MariaDB | 11.4 LTS | validated alias | native | native | native |
| MariaDB | 11.8 LTS | validated alias | native | native | native |
| MariaDB | 12.3 LTS | validated alias | native | native | native |

All six lines provide native CTE support. The exact qualified patch pins,
lifecycle sources, live-test ownership, and unsupported-version policy are in
[Supported Databases][supported-databases].

Runtime capability diagnostics classify provider behavior as `Native`,
`Emulated`, or `UnsupportedByEngine`. Unsupported server releases are rejected
by default. `MySqlServerVersionCompatibilityMode.AllowUnsupported` is an
explicit escape hatch without a support guarantee and emits
`MySqlEventId.UnsupportedServerVersion`.

## Feature Highlights

- **Engine-aware migrations and scaffolding:** advisory-lock protection,
  idempotent scripts, rename and sequence handling, generated and invisible
  columns, spatial indexes, JSON aliases, and custom migration-operation
  handlers.
- **Portable temporal modeling:** native MariaDB system, application, and
  bitemporal tables plus provider-owned MySQL history-table emulation behind
  one model and query API.
- **MySQL-family query translation:** JSON functions, regular expressions,
  full-text search, scalar `Like<T>`, CTE composition, bulk update/delete, and
  engine-specific SQL selected from declared capabilities.
- **Provider-owned type mappings:** JSON DOM types, `Binary16` and `Char36`
  GUIDs, temporal CLR types, generated defaults, complex types, and optional
  NetTopologySuite geometries.
- **Production behavior:** transient-failure retries, savepoints, connection
  pooling, structured diagnostics, trimming analysis, compiled models, and
  precompiled query coverage.
- **Standalone distributed caching:** standard .NET cache contracts,
  database-UTC expiration, buffer-based reads, and bounded expired-row cleanup
  without an EF Core dependency.

The [documentation index][documentation-index] owns the complete behavioral
contracts and limitations. The sections below show only the main entry points.

### Retry transient failures

```csharp
options.UseMySql(connectionString, serverVersion, mysql =>
    mysql.EnableRetryOnFailure(maxRetryCount: 5));
```

### Choose GUID storage

```csharp
options.UseMySql(connectionString, serverVersion, mysql =>
    mysql.DefaultGuidFormat(
        Doka.EntityFrameworkCore.MySql.MySqlGuidFormat.Char36));

modelBuilder.Entity<OrderWithGuid>()
    .Property(order => order.Id)
    .HasMySqlGuidFormat(
        Doka.EntityFrameworkCore.MySql.MySqlGuidFormat.Binary16);
```

### Configure temporal tables

```csharp
modelBuilder.Entity<Employee>().ToTable(
    "Employees",
    table => table.IsTemporal(temporal =>
    {
        temporal.UseHistoryTable("EmployeeHistory");
        temporal.HasPeriodStart("ValidFrom");
        temporal.HasPeriodEnd("ValidTo");
    }));

var history = await context.Employees
    .TemporalAll()
    .OrderBy(employee => EF.Property<DateTime>(employee, "ValidFrom"))
    .ToListAsync();
```

See [Temporal Tables][temporal-tables] and
[Common Table Expressions][common-table-expressions] for portability rules and
complete query examples.

### Enable spatial support

After installing the NetTopologySuite package, activate it in provider
options:

```csharp
options.UseMySql(connectionString, serverVersion, mysql =>
    mysql.UseNetTopologySuite());

modelBuilder.Entity<Place>()
    .Property(place => place.Location)
    .HasColumnType("point")
    .HasSrid(4326);

modelBuilder.Entity<Place>()
    .HasIndex(place => place.Location)
    .IsSpatial();
```

## Migrations

The provider uses the standard EF Core tooling:

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

Concurrent migrators are serialized with a dedicated advisory lock. Custom
packages can add exact migration-operation handlers without replacing the
provider SQL generator; see
[Migration Operation Handlers][migration-operation-handlers].

Existing Pomelo applications should start with
[Migrating from Pomelo][migrating-from-pomelo]. It distinguishes API changes
from schema changes and preserves deployed migration history.

## Distributed Caching

`Doka.Caching.MySql` provides `IDistributedCache` and
`IBufferDistributedCache` for MySQL and MariaDB. It is a standalone .NET 10
package: neither the EF Core provider nor a `DbContext` is required.

Install the release candidate explicitly:

```bash
dotnet package add Doka.Caching.MySql --version 10.1.0-rc.2
```

First, generate the cache table script for an existing database:

```csharp
using Doka.Caching.MySql;

var script = MySqlCacheSchema.GetCreateScript("app_cache", "DistributedCache");
Console.WriteLine(script);
```

Review and execute the script separately during deployment. Registration and
cache operations never create or upgrade database objects. When replacing
another cache implementation, provision a new Doka cache table.

Register the cache with the application's connection string:

```csharp
using Doka.Caching.MySql;
using Microsoft.Extensions.DependencyInjection;

services.AddDistributedMySqlCache(options =>
{
    options.ConnectionString = connectionString;
    options.SchemaName = "app_cache";
    options.TableName = "DistributedCache";
});
```

Inject `IDistributedCache` and pass the operation's cancellation token:

```csharp
using Microsoft.Extensions.Caching.Distributed;

await cache.SetStringAsync(
    "greeting",
    "Hello from Doka",
    new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        SlidingExpiration = TimeSpan.FromMinutes(2),
    },
    cancellationToken);

var greeting = await cache.GetStringAsync("greeting", cancellationToken);
```

Both interfaces resolve the same singleton. Expiration uses database UTC;
absolute deadlines cap sliding refreshes. For binary payloads,
`IBufferDistributedCache` reads into caller-owned buffers without an extra
value-sized result array. The application identity needs only `SELECT`,
`INSERT`, `UPDATE`, and `DELETE` on the deployed table.

See [Distributed Cache][distributed-cache] for data-source ownership,
concurrency, cleanup, schema deployment, and buffer usage.

## Compatibility Boundaries

- `MySqlConnector` is the only supported ADO.NET driver.
- Azure Database for MySQL is not yet an advertised compatibility target.
- Amazon Aurora MySQL is intentionally outside the supported scope.
- Unsupported query translations fail instead of falling back to client
  evaluation.
- EF provider NativeAOT readiness remains blocked by upstream EF Core
  precompiled-query constraints; trimming is continuously validated. The
  standalone cache has no EF Core dependency and is verified separately.

See [External Limitations][external-limitations] for the canonical boundary
ledger. Provider-owned gaps have a zero budget and do not belong in that ledger.

## Documentation and Support

- [Documentation index][documentation-index]
- [Provider architecture][provider-architecture]
- [Runnable examples][runnable-examples]
- [Complex types][complex-types]
- [Temporal tables][temporal-tables]
- [Common table expressions][common-table-expressions]
- [Host integration][host-integration]
- [IDE integration][ide-integration]
- [Provider configuration][provider-configuration]
- [Query functions][query-functions]
- [Migrating from Pomelo][migrating-from-pomelo]
- [Distributed cache][distributed-cache]
- [Support and issue reporting][support]
- [Security policy][security-policy]
- [Security assurance case][security-assurance-case]
- [Release verification][release-verification]
- [Project governance][project-governance]
- [Project roadmap][project-roadmap]
- [OpenSSF Best Practices evidence][openssf-best-practices]
- [Changelog][changelog]

Use [GitHub Issues](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/issues)
for reproducible defects, feature requests, compatibility reports, and usage
questions. Report suspected vulnerabilities privately through
[SECURITY.md][security-policy].

## Contributing

Repository setup, test tiers, coding conventions, public API governance, and
pull-request requirements are documented in [CONTRIBUTING.md][contributing].
Performance evidence is independent engineering feedback and does not block
release publication; its measurement and triage contract lives in the
[Performance Evidence runbook][performance-evidence].

## License

MIT -- see [LICENSE][license].

[changelog]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/CHANGELOG.md
[common-table-expressions]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/ctes.md
[complex-types]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/complex-types.md
[contributing]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/CONTRIBUTING.md
[documentation-index]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/README.md
[distributed-cache]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/distributed-cache.md
[external-limitations]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/limitations.md
[global-json]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/global.json
[host-integration]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/host-integration-examples.md
[ide-integration]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/ide-integration.md
[license]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/LICENSE
[migration-operation-handlers]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/migration-operation-handlers.md
[migrating-from-pomelo]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/migrating-from-pomelo.md
[openssf-best-practices]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/openssf-best-practices.md
[performance-evidence]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/operations/performance-evidence.md
[project-governance]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/GOVERNANCE.md
[project-roadmap]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/ROADMAP.md
[provider-architecture]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/architecture.md
[provider-configuration]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/provider-configuration.md
[query-functions]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/query-functions.md
[release-verification]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/security/release-verification.md
[runnable-examples]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/examples/README.md
[security-assurance-case]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/security/assurance-case.md
[security-policy]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/SECURITY.md
[support]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/SUPPORT.md
[supported-databases]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/supported-databases.md
[temporal-tables]: https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/blob/main/docs/temporal-tables.md
