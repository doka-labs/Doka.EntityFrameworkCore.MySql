# Doka.EntityFrameworkCore.MySql

[![CI](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/workflows/ci.yml/badge.svg)](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Doka.EntityFrameworkCore.MySql.svg)](https://www.nuget.org/packages/Doka.EntityFrameworkCore.MySql)
[![NuGet NetTopologySuite](https://img.shields.io/nuget/v/Doka.EntityFrameworkCore.MySql.NetTopologySuite.svg)](https://www.nuget.org/packages/Doka.EntityFrameworkCore.MySql.NetTopologySuite)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

`Doka.EntityFrameworkCore.MySql` is an Entity Framework Core 10 provider for MySQL-compatible databases. It targets MySQL 8.0 / 8.4 and MariaDB 11.4 / 11.8 on top of the [`MySqlConnector`](https://mysqlconnector.net) ADO.NET driver.

The main goal is release responsiveness for `.NET 10` and `EF Core 10` together with a maintainability- and performance-first architecture: a single capability model drives engine differences, the runtime is trim- and AOT-aware, and every feature is test-backed against the supported engine matrix.

## What This Project Solves

This provider is designed for teams that need:

- an EF Core provider aligned with the Microsoft release cadence for `.NET 10` / `EF Core 10`
- dual MySQL and MariaDB support without provider-specific code branches in the application
- a small, reviewable public API surface with opt-in features rather than implicit magic
- trim- and AOT-safe defaults from day one
- production-grade diagnostics, retry semantics, savepoint support, and advisory-lock-protected migrations

## Requirements

### Package Usage

- .NET 10.0 or later
- EF Core 10.x (`Microsoft.EntityFrameworkCore.Relational`)
- One of:
  - MySQL 8.0 or 8.4
  - MariaDB 11.4 or 11.8
- Transitive: [MySqlConnector](https://mysqlconnector.net) 2.5.x (the modern fully-managed ADO.NET driver)

### Building From Source

- .NET 10 SDK (version `10.0.201` or later)
- Docker — only required to run the live integration and benchmark suites

## Installation

**Main provider:**

```bash
dotnet add package Doka.EntityFrameworkCore.MySql
```

**Optional spatial extension** (NetTopologySuite integration — only install if you use spatial types):

```bash
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite
```

## Supported Engines

| Engine        | Versions    | Native JSON | Native Sequences | `RETURNING` |
|---------------|-------------|-------------|------------------|-------------|
| MySQL         | 8.0, 8.4    | ✓           | emulated (table) | —           |
| MariaDB       | 11.4, 11.8  | alias       | ✓ (10.3+)        | ✓ (10.5+)   |

Engine-specific behavior is captured by the internal `ServerCapabilities` model and exposed automatically at runtime; application code does not branch on engine or version.

## Quick Start

```csharp
using Doka.EntityFrameworkCore.MySql;
using Microsoft.EntityFrameworkCore;

var connectionString =
    "Server=localhost;Database=my_app;User ID=app;Password=secret;";

var serverVersion = MySqlServerVersion.MySql(new Version(8, 4, 0));

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseMySql(connectionString, serverVersion)
    .Options;

using var context = new AppDbContext(options);

context.Database.EnsureCreated();
context.Products.Add(new Product { Name = "Widget", Price = 9.99m });
context.SaveChanges();
```

MariaDB uses the same API with a different factory:

```csharp
var serverVersion = MySqlServerVersion.MariaDb(new Version(11, 8, 0));
```

Or let the provider detect the engine from the server greeting:

```csharp
var serverVersion = MySqlServerVersion.AutoDetect(connectionString);
```

## Registration

### Connection string

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, MySqlServerVersion.MySql(new Version(8, 4, 0))));
```

### Existing `DbConnection`

```csharp
services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(myConnection, MySqlServerVersion.MySql(new Version(8, 4, 0))));
```

### `MySqlDataSource` (recommended for pooling and logging control)

```csharp
var dataSource = new MySqlDataSourceBuilder(connectionString)
    .UseLoggerFactory(loggerFactory)
    .Build();

services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(dataSource, MySqlServerVersion.MySql(new Version(8, 4, 0))));
```

## Core Features

### Opt-in retry on transient failures

```csharp
options.UseMySql(connectionString, serverVersion, mysql =>
    mysql.EnableRetryOnFailure(maxRetryCount: 5));
```

Transient detection covers `MySqlException` with known retryable error codes, `SocketException`, and `IOException`. `OperationCanceledException` and command timeouts are intentionally non-retryable.

### GUID storage format

```csharp
// Default for the whole provider
options.UseMySql(connectionString, serverVersion, mysql =>
    mysql.DefaultGuidFormat(MySqlGuidFormat.Char36));

// Or per property
modelBuilder.Entity<Order>()
    .Property(o => o.Id)
    .HasMySqlGuidFormat(MySqlGuidFormat.Binary16);
```

Both `binary(16)` and `char(36)` round-trip through `Guid` CLR values without manual conversion.

### HiLo value generation

```csharp
modelBuilder.Entity<Order>()
    .Property(o => o.Id)
    .UseHiLo("order_ids");
```

HiLo is backed by native `CREATE SEQUENCE` on MariaDB 10.3+ and by atomic table-based emulation (`UPDATE ... LAST_INSERT_ID(value + inc)`) on MySQL. Block allocation happens client-side to reduce round-trips.

### JSON columns with native CLR types

```csharp
public class Document
{
    public int Id { get; set; }
    public JsonNode? Payload { get; set; }
}
```

`JsonElement`, `JsonDocument`, `JsonNode`, `JsonObject`, and `JsonArray` are preserved end-to-end with embedded value converters and deep-equality value comparers. MariaDB columns are automatically emitted as `longtext COLLATE utf8mb4_bin CHECK (JSON_VALID(...))`, and scaffolding detects the alias back to `json`.

### JSON, regex, and full-text functions

```csharp
var matches = context.Products
    .Where(p => EF.Functions.Regexp(p.Sku, "^[A-Z]{3}[0-9]+$"))
    .ToList();

var articles = context.Articles
    .Where(a => EF.Functions.MatchInBooleanMode(a.Body, "+mysql -aurora"))
    .ToList();

var depth = context.Documents
    .Select(d => EF.Functions.JsonDepth(d.Payload))
    .FirstOrDefault();
```

Full set: `Regexp`, `Match`, `MatchInBooleanMode`, `JsonSet`, `JsonReplace`, `JsonRemove`, `JsonArray`, `JsonObject`, `JsonDepth`, `JsonLength`, `JsonType`, `JsonKeys`, `JsonContains`. REGEXP uses `REGEXP_LIKE(...)` on MySQL 8.0+ and the infix `REGEXP` operator on MariaDB.

### MariaDB `INVISIBLE` columns

```csharp
modelBuilder.Entity<User>()
    .Property(u => u.InternalNotes)
    .IsInvisible();
```

### Spatial types (opt-in)

Install `Doka.EntityFrameworkCore.MySql.NetTopologySuite` and activate the extension:

```csharp
options.UseMySql(connectionString, serverVersion, mysql =>
    mysql.UseNetTopologySuite());

// Model
modelBuilder.Entity<Place>()
    .Property(p => p.Location)
    .HasColumnType("point")
    .HasAnnotation("Doka:MySql:SpatialReferenceSystemId", 4326);

// Query
var nearby = context.Places
    .Where(p => p.Location.Distance(origin) < 5000)
    .ToList();
```

Spatial indexes (`CREATE SPATIAL INDEX`) are supported through the standard `HasIndex(...)` fluent API with a provider annotation.

## Migrations

Migrations work with the standard EF Core tooling:

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

The provider ships with:

- **Advisory-lock protection.** Concurrent migration attempts are serialized through MySQL's `GET_LOCK` on a dedicated non-pooled connection, so only one process applies migrations at a time.
- **Idempotent script generation.** `dotnet ef migrations script --idempotent` emits stored-procedure-wrapped DDL that is safe to re-run against partially-applied databases.
- **Engine-aware DDL.** Rename, sequence, spatial index, generated column, JSON alias, and `INVISIBLE` column operations select the correct SQL per engine automatically.

## Project Layout

- `src/Doka.EntityFrameworkCore.MySql`
  Core runtime provider.
- `src/Doka.EntityFrameworkCore.MySql.NetTopologySuite`
  Optional spatial extension with NTS integration.
- `tests/Doka.EntityFrameworkCore.MySql.Tests`
  Unit tests for configuration, capability, and SQL-shape logic.
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests`
  EF-pipeline tests: model validation, SQL generation, type mapping, migrations, scaffolding.
- `tests/Doka.EntityFrameworkCore.MySql.IntegrationTests`
  Live-database tests against MySQL 8.0 / 8.4 and MariaDB 11.4 / 11.8.
- `tests/Doka.EntityFrameworkCore.MySql.TestUtilities`
  Shared test helpers and log sinks.
- `benchmarks/`
  `BenchmarkDotNet` scorecard harness with reviewable baselines.
- `examples/`
  Runnable samples for CRUD, inheritance patterns, JSON columns, generated columns, GUID formats, relationships, retry / resilience, spatial queries, migrations workflow, multi-tenancy, bulk operations, character sets, and Docker integration.
- `docker/compose.yml`
  Bundled MySQL 8.0 / 8.4 and MariaDB 11.4 / 11.8 services for local integration testing.
- `eng/`
  Developer scripts: `test.sh`, `test-integration.sh`, `test-runtime-posture.sh`, `benchmark.sh`, `release-candidate.sh`.
- `docs/`
  In-repo governance and host-integration documentation.

## Building and Testing

```bash
dotnet build Doka.EntityFrameworkCore.MySql.slnx
./eng/test.sh
./eng/test-integration.sh --up-test-down   # requires Docker
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for full test commands, integration-target selection, benchmark profiles, and code-style requirements.

## Editor / IDE Tips

### JetBrains Rider and ReSharper

Rider and ReSharper ship an EF Core inspection that flags provider-specific `EF.Functions.*` extensions — for example `EF.Functions.Regexp(...)`, `EF.Functions.Match(...)`, or `EF.Functions.DistanceSphere(...)` — with:

> Function is not convertible to SQL and must not be called in the database context.

The message is a **static-analysis false positive**: the inspection uses a hard-coded allow-list of Microsoft- and Pomelo-origin methods and does not recognize third-party provider extensions. Translation works correctly at runtime (the provider's `IMethodCallTranslatorPlugin` emits the correct SQL).

To silence it locally, pick whichever granularity fits:

**Per call site**

```csharp
// ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
var results = context.Articles
    .Where(a => EF.Functions.MatchInBooleanMode(a.Body, "+mysql -aurora"))
    .ToList();
```

**Per file**

```csharp
// ReSharper disable EntityFramework.UnsupportedServerSideFunctionCall
```

**Per project** (recommended — keeps the inspection active elsewhere)

Place a `<YourProject>.csproj.DotSettings` alongside each consumer `.csproj` that writes LINQ queries against the provider:

```xml
<wpf:ResourceDictionary xml:space="preserve" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:s="clr-namespace:System;assembly=mscorlib" xmlns:ss="urn:shemas-jetbrains-com:settings-storage-xaml" xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <s:String x:Key="/Default/CodeInspection/Highlighting/InspectionSeverities/=EntityFramework_002EUnsupportedServerSideFunctionCall/@EntryIndexedValue">DO_NOT_SHOW</s:String>
</wpf:ResourceDictionary>
```

This repository ships that variant for each affected test/benchmark project, so contributors see a warning-free editor experience without silencing the inspection for unrelated code.

## Compatibility and Hosted Targets

The provider targets the self-hosted MySQL 8.x and MariaDB 11.x lines.

**Azure Database for MySQL** is considered possible future validation work. Because Azure Database for MySQL is a managed MySQL Community distribution, it inherits capability behavior from the self-hosted MySQL profile; a dedicated compatibility mode would only be introduced if observed runtime behavior required one. No hosted Azure workflow is active until credentials are provisioned.

**Amazon Aurora MySQL** is intentionally out of scope for this project.

## Non-Goals

This provider intentionally does **not** try to be:

- a drop-in wrapper around a different ADO.NET driver — `MySqlConnector` is the only supported driver
- a feature-count-maximizing provider at the cost of maintainability or correctness
- a source-generator or analyzer package
- a branded managed-service API surface (`UseAurora(...)` / `UseAzureMySql(...)`)
- a fallback to client-evaluation for supported queryable paths — unsupported translations fail loudly

## License

MIT — see [LICENSE](LICENSE).

## Further Reading

- [Release governance and diagnostics catalog](docs/release-governance.md)
- [Host integration examples](docs/host-integration-examples.md)
- [Contributing](CONTRIBUTING.md)
- [Changelog](CHANGELOG.md)
- [Security policy](.github/SECURITY.md)
