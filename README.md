# Doka.EntityFrameworkCore.MySql

[![CI](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/workflows/ci.yml/badge.svg)](https://github.com/doka-labs/Doka.EntityFrameworkCore.MySql/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Doka.EntityFrameworkCore.MySql.svg)](https://www.nuget.org/packages/Doka.EntityFrameworkCore.MySql)
[![NuGet NetTopologySuite](https://img.shields.io/nuget/v/Doka.EntityFrameworkCore.MySql.NetTopologySuite.svg)](https://www.nuget.org/packages/Doka.EntityFrameworkCore.MySql.NetTopologySuite)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/doka-labs/Doka.EntityFrameworkCore.MySql/badge)](https://scorecard.dev/viewer/?uri=github.com/doka-labs/Doka.EntityFrameworkCore.MySql)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/13999/badge)](https://www.bestpractices.dev/projects/13999)

`Doka.EntityFrameworkCore.MySql` is an Entity Framework Core 10 provider for
MySQL and MariaDB. It targets MySQL 8.4 LTS and MariaDB 11.4 / 11.8 LTS on top
of the [`MySqlConnector`](https://mysqlconnector.net) ADO.NET driver.

The main goal is release responsiveness for `.NET 10` and `EF Core 10`
together with a maintainability- and performance-first architecture: separate
engine-fact and provider-support contracts drive engine differences, the
runtime is trim-aware with NativeAOT readiness deferred until upstream EF Core
stabilizes its precompiled-query story (see ADR D-017), and every feature is
test-backed against the supported engine matrix.

## What This Project Solves

This provider is designed for teams that need:

- an EF Core provider aligned with the Microsoft release cadence for `.NET 10` / `EF Core 10`
- dual MySQL and MariaDB support without provider-specific code branches in the application
- a small, reviewable public API surface with opt-in features rather than implicit magic
- trim-safe defaults from day one (NativeAOT readiness deferred per ADR D-017)
- production-grade diagnostics, retry semantics, savepoint support, and advisory-lock-protected migrations

## Requirements

### Package Usage

- .NET 10.0 or later
- EF Core 10.x (`Microsoft.EntityFrameworkCore.Relational`)
- One of:
  - MySQL 8.4 LTS
  - MariaDB 11.4 LTS or 11.8 LTS
- Transitive: [MySqlConnector](https://mysqlconnector.net) 2.5.0 through the latest stable 2.x release
  on the 2.x line. The supported floor and latest compatible 2.x release are
  validated separately by the scheduled live driver matrix.

### Building From Source

- .NET SDK `10.0.302` (the exact version declared by `global.json`)
- Docker -- required only for the live integration, live example, benchmark,
  and release-candidate matrices

## Installation

Install prereleases with the exact version shown by the corresponding GitHub
release and NuGet.org listing. Keeping the version explicit makes restores
reproducible and avoids NuGet's stable-only default resolution.

**Main provider:**

```bash
release_version="<published-version>"
dotnet add package Doka.EntityFrameworkCore.MySql --version "${release_version}"
```

**Optional spatial extension** (NetTopologySuite integration -- only install if you use spatial types):

```bash
release_version="<published-version>"
dotnet add package Doka.EntityFrameworkCore.MySql.NetTopologySuite --version "${release_version}"
```

## Supported Engines

| Engine | Versions | Native JSON | Native sequences | `RETURNING` | CTEs | Temporal tables |
| --- | --- | --- | --- | --- | --- | --- |
| MySQL | 8.4 LTS | yes | emulated (table) | no (engine limitation) | native | emulated (InnoDB history and triggers) |
| MariaDB | 11.4 LTS, 11.8 LTS | alias | yes (10.3+) | yes (10.5+) | native | native system, application, and bitemporal |

Engine facts and provider support are separate internal contracts. Runtime
diagnostics report each provider capability as `Native`, `Emulated`, or
`UnsupportedByEngine`; application code does not branch on engine names.

Only the release lines in this table are accepted by default. Legacy,
unvalidated, and future versions are classified explicitly and rejected during
provider-option validation. Unsupported execution remains available as an
intentional compatibility path:

```csharp
var legacyVersion = MySqlServerVersion.MySql(
    new Version(8, 0, 44),
    MySqlServerVersionCompatibilityMode.AllowUnsupported);
```

The opt-in carries no support guarantee and emits the structured
`MySqlEventId.UnsupportedServerVersion` warning at runtime.

## Temporal Tables and CTEs

System-versioned temporal tables use one public model and query API on every
supported engine. MariaDB uses native system versioning; MySQL uses a
provider-owned InnoDB history table and transactional triggers. Temporal query
roots include `TemporalAsOf`, `TemporalAll`, `TemporalFromTo`,
`TemporalBetween`, and `TemporalContainedIn` and are always no-tracking.

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

Non-recursive and recursive CTEs compose through EF Core's parameterized
`FromSql` and `SqlQuery` roots. MySQL 8.4 also accepts CTEs in data-modification
SQL. MariaDB 11.4 and 11.8 do not, which is reported as an engine boundary
rather than a provider limitation.

See [Temporal tables](docs/temporal-tables.md) and
[Common table expressions](docs/ctes.md) for the complete contracts,
schema-safety rules, examples, and primary sources.

MariaDB application-time and bitemporal tables additionally expose typed
model, migration, scaffolding, and `FOR PORTION OF` update / delete contracts.
MySQL reports these exact operations as engine limitations rather than a
provider limitation.

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

<!-- readme-autodetect-snippet begin -->

```csharp
using Doka.EntityFrameworkCore.MySql;
using MySqlConnector;

var connectionString = "Server=localhost;Database=app;User ID=app;Password=change-me";

await using var connection = new MySqlConnection(connectionString);
await connection.OpenAsync();

var serverVersion = MySqlServerVersion.AutoDetect(connection);
```

<!-- readme-autodetect-snippet end -->

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
    mysql.DefaultGuidFormat(Doka.EntityFrameworkCore.MySql.MySqlGuidFormat.Char36));

// Or per property
modelBuilder.Entity<OrderWithGuid>()
    .Property(o => o.Id)
    .HasMySqlGuidFormat(Doka.EntityFrameworkCore.MySql.MySqlGuidFormat.Binary16);
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
    public string SearchDocument { get; set; } = "{}";
}
```

`JsonElement`, `JsonDocument`, `JsonNode`, `JsonObject`, and `JsonArray` are preserved end-to-end with embedded value converters and deep-equality value comparers. MariaDB columns are automatically emitted as `longtext COLLATE utf8mb4_bin CHECK (JSON_VALID(...))`, and scaffolding detects the alias back to `json`.

### Complex types

EF Core 10 complex types are supported as flattened columns or JSON documents
on MySQL 8.4 and MariaDB 11.4 / 11.8 for CLR-backed shapes that EF Core can
represent. Nested members, projections, materialization, updates, supported
tracking shapes, reference-type JSON collections, compiled models, and
precompiled `JSON_TABLE` expressions remain in the normal EF Core pipeline.
The exact collection, property-value, shadow-property, inheritance, key, and
index boundaries are documented separately so they are not confused with
provider gaps.

See [Complex types](docs/complex-types.md) for configuration examples, the
support matrix, verification scope, and primary sources.

### JSON, regex, and full-text functions

```csharp
var matches = context.Products
    .Where(p => EF.Functions.Regexp(p.Sku, "^[A-Z]{3}[0-9]+$"))
    .ToList();

var articles = context.Articles
    .Where(a => EF.Functions.MatchInBooleanMode(a.Body, "+mysql -aurora"))
    .ToList();

var depth = context.Documents
    .Select(d => EF.Functions.JsonDepth(d.SearchDocument))
    .FirstOrDefault();
```

Full set: `Regexp`, `Match`, `MatchInBooleanMode`, `JsonSet`, `JsonReplace`, `JsonRemove`, `JsonArray`, `JsonObject`, `JsonDepth`, `JsonLength`, `JsonType`, `JsonKeys`, `JsonContains`. REGEXP uses `REGEXP_LIKE(...)` on MySQL and the infix `REGEXP` operator on MariaDB.

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
    .HasSrid(4326);

modelBuilder.Entity<Place>()
    .HasIndex(p => p.Location)
    .IsSpatial();

// Query
var nearby = context.Places
    .Where(p => EF.Functions.DistanceSphere(p.Location, origin) < 5000)
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
  Self-provisioning live-database tests against MySQL 8.4, MariaDB 11.4, and MariaDB 11.8.
- `tests/Doka.EntityFrameworkCore.MySql.SpecificationAdapters`
  Engine-specific adapters for the upstream EF Core specification contracts.
- `tests/Doka.EntityFrameworkCore.MySql.RuntimeSmoke`
  Standalone trim, migration-bundle, and packaged-consumer runtime probes.
- `tests/Doka.EntityFrameworkCore.MySql.TestUtilities`
  Shared test helpers and log sinks.
- `benchmarks/`
  `BenchmarkDotNet` harness for reviewed historical scorecards and paired
  release qualification.
- [`examples/`](examples/README.md)
  Seventeen runnable public-API samples. Fourteen participate in the supported
  live engine matrix; ten also enforce explicit scenario invariants. The
  catalog covers CRUD, inheritance patterns, JSON columns, generated columns,
  GUID formats, relationships, retry / resilience, spatial queries, migrations
  workflow, multi-tenancy, bulk operations, character sets, Docker integration,
  temporal tables, recursive CTEs, performance guidance, and host observability.
- `docker/compose.yml`
  Optional, explicitly selected MySQL 8.4, MariaDB 11.4, and MariaDB 11.8 debugging stack. The canonical integration and specification tests own short-lived containers through Testcontainers.
- `eng/`
  Developer scripts and executable quality contracts, including exact
  specification discovery/TRX reconciliation, assembly-aware coverage,
  runtime posture, benchmarks, and release readiness.
- `docs/`
  In-repo governance and host-integration documentation.

## Building and Testing

```bash
dotnet build Doka.EntityFrameworkCore.MySql.slnx
./eng/test.sh
./eng/test-integration.sh   # requires Docker; owns and cleans up its databases
./eng/test-examples.sh      # explicit live example matrix; owns its containers
bash ./eng/check-publication-readiness.sh # verifies provider completeness
./eng/pre-tag-check.sh                    # verifies a green main commit is tag-ready
```

## Performance and Memory Evidence

The hosted historical scorecard executes 55 named provider workloads across
MySQL 8.4 and MariaDB 11.8. It covers sync and async execution, compiled
queries, retry, diagnostic listeners, context and connection pooling,
concurrency, data sizes, batch sizes, JSON, spatial materialization,
migrations, and HiLo allocation.

Scorecard evidence includes raw and workload-local calibration samples,
median, p95, p99, standard error, managed allocation, GC counts, retained
memory diagnostics, exact environment identity, bounded interval host-CPU
admission, and SHA-256 hashes. The CPU model and BenchmarkDotNet host must
match the workload host. Raw absolute limits, calibration-normalized
matching-runner historical budgets, allocation limits, and six sustained
resource invariants must all pass.

Release qualification does not compare measurements from different machines.
For each engine, it alternates a reference provider and the candidate provider
on one allocated runner, applies the registered paired statistical policy and
absolute ceilings, and retains the raw measurements with the candidate. The
historical scorecard remains early warning on `main`; it neither qualifies nor
blocks a release.

Run a fast structural check:

```bash
DOKA_BENCHMARK_TARGET=mysql84 ./eng/benchmark.sh --up-run-down
```

Release baselines, full scorecard commands, evidence layout, failure triage,
and hosted-runner acceptance are documented in the
[performance evidence runbook](docs/operations/performance-evidence.md).

See [CONTRIBUTING.md](CONTRIBUTING.md) for full test commands, integration-target selection, benchmark profiles, and code-style requirements.

## Editor / IDE Tips

### JetBrains Rider and ReSharper

Rider and ReSharper ship an EF Core inspection that flags provider-specific `EF.Functions.*` extensions -- for example `EF.Functions.Regexp(...)`, `EF.Functions.Match(...)`, or `EF.Functions.DistanceSphere(...)` -- with:

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

**Per project** (recommended -- keeps the inspection active elsewhere)

Place a `<YourProject>.csproj.DotSettings` alongside each consumer `.csproj` that writes LINQ queries against the provider:

```xml
<wpf:ResourceDictionary xml:space="preserve" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:s="clr-namespace:System;assembly=mscorlib" xmlns:ss="urn:shemas-jetbrains-com:settings-storage-xaml" xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <s:String x:Key="/Default/CodeInspection/Highlighting/InspectionSeverities/=EntityFramework_002EUnsupportedServerSideFunctionCall/@EntryIndexedValue">DO_NOT_SHOW</s:String>
</wpf:ResourceDictionary>
```

This repository ships that variant for each affected test/benchmark project, so contributors see a warning-free editor experience without silencing the inspection for unrelated code.

## Compatibility and Hosted Targets

The advertised support matrix covers self-hosted MySQL 8.4 LTS and MariaDB
11.4 / 11.8 LTS.

**Azure Database for MySQL** is not in the advertised support matrix. It is a
future external-canary target when test credentials become available; no
compatibility guarantee is inferred from the self-hosted MySQL profile. Until
that validation exists, a deployment whose reported engine version is outside
the supported table must use the explicit `AllowUnsupported` compatibility
path and receives no support guarantee. A branded API or dedicated mode would
be introduced only if observed runtime behavior required one.

**Amazon Aurora MySQL** is intentionally out of scope for this project.

## Non-Goals

This provider intentionally does **not** try to be:

- a drop-in wrapper around a different ADO.NET driver -- `MySqlConnector` is the only supported driver
- a feature-count-maximizing provider at the cost of maintainability or correctness
- a source-generator or analyzer package
- a branded managed-service API surface (`UseAurora(...)` / `UseAzureMySql(...)`)
- a fallback to client-evaluation for supported queryable paths -- unsupported translations fail loudly

## License

MIT -- see [LICENSE](LICENSE).

## Further Reading

- [Documentation index](docs/README.md)
- [Release governance and diagnostics catalog](docs/release-governance.md)
- [Operations and release runbook](docs/operations-runbook.md)
- [Release publication procedure](docs/operations/release-publication.md)
- [Host integration examples](docs/host-integration-examples.md)
- [External engine and EF Core limitations](docs/limitations.md)
- [Complex types](docs/complex-types.md)
- [Temporal tables](docs/temporal-tables.md)
- [Common table expressions](docs/ctes.md)
- [Contributing](CONTRIBUTING.md)
- [Support](SUPPORT.md)
- [Code of Conduct](CODE_OF_CONDUCT.md)
- [Changelog](CHANGELOG.md)
- [Security policy](SECURITY.md)
- [Threat model](docs/security/threat-model.md)
