# Provider Architecture

This document describes the high-level design of
`Doka.EntityFrameworkCore.MySql`. Architecture Decision Records explain why
individual choices were made; this document shows how the resulting system
fits together.

## System Context

The provider is a library loaded into an application process. It sits between
Entity Framework Core and MySqlConnector. It does not host a service, own an
application identity, or replace database access control.

```text
application model and LINQ
            |
            v
Entity Framework Core contracts
            |
            v
Doka.EntityFrameworkCore.MySql
  configuration and metadata
  capabilities and engine policy
  query and update SQL generation
  migrations and reverse engineering
  storage and value generation
  diagnostics and resilience
            |
            +---- optional NetTopologySuite plugin
            |
            v
MySqlConnector
            |
            v
supported MySQL or MariaDB server
```

EF Core owns change tracking, LINQ pipeline orchestration, model conventions,
migration operation production, and the public provider-service contracts.
The provider owns MySQL-family mapping and SQL semantics. MySqlConnector owns
the wire protocol, authentication, TLS, pooling, and command transport.

## Shipped Packages

The release package set contains the core provider, its optional spatial
extension, and the independent cache. The cache is available from
[10.1.0](../CHANGELOG.md#1010---2026-08-27); earlier releases contain
only the two EF Core packages.

### Core provider

`src/Doka.EntityFrameworkCore.MySql` contains the runtime and design-time
provider. Its public surface configures a context, model annotations, query
functions, temporal behavior, GUID formats, value generation, reverse
engineering, and the additive migration-operation handler SPI.

The implementation is divided by EF Core responsibility:

| Area | Responsibility |
| --- | --- |
| `Internal/Capabilities` | Resolve engine facts and provider support from the declared server family and version |
| `Internal/Infrastructure` | Bind options, service registration, and EF Core provider identity |
| `Internal/Metadata` | Apply and validate model conventions and provider annotations |
| `Internal/Query` | Translate LINQ members and methods and generate MySQL-family query SQL |
| `Internal/Update` | Generate modification commands, batching, and result propagation |
| `Internal/Storage` | Select relational type mappings, literals, comparers, and value generators |
| `Internal/Migrations` | Generate engine-aware DDL, advisory locks, and custom-operation commands |
| `Internal/Scaffolding` | Read database metadata and generate a provider model and source annotations |
| `Internal/Resilience` | Classify transient failures and coordinate retry semantics |
| `Internal/Diagnostics` | Emit bounded logs, metrics, activities, and stable event identifiers |

### Spatial extension

`src/Doka.EntityFrameworkCore.MySql.NetTopologySuite` is optional. Activating
`UseNetTopologySuite()` contributes spatial type mappings, member and method
translation, scaffolding, spatial-index DDL, and guarded geometry parsing. It
depends on the core provider; the core provider does not depend on it.

This one-way dependency keeps spatial allocations and public dependencies out
of applications that do not use spatial data.

### Standalone cache

`src/Doka.Caching.MySql` implements the .NET 10 `IDistributedCache` and
`IBufferDistributedCache` contracts through one singleton and a MySqlConnector
data source. A connection string creates a cache-owned source; an explicitly
supplied source remains caller-owned. Neither the cache nor the EF Core provider depends
on the other. The cache's runtime dependencies are MySqlConnector and
Microsoft.Extensions packages; it does not depend on EF Core.

Cache registration uses `AddDistributedMySqlCache(...)` and
`MySqlCacheOptions`, not EF Core services. Deployment explicitly creates its
versioned table; runtime operations perform no DDL. The
[Distributed Cache guide](distributed-cache.md) owns expiration, concurrency,
buffer ownership, and bounded-cleanup details.

## Runtime Composition

`UseMySql(...)` adds an immutable options extension to EF Core's context
options. The extension registers provider services in EF Core's internal
service provider. Configuration accepts exactly one connection path: a
connection string, an existing `DbConnection`, or a `MySqlDataSource`.

The declared `MySqlServerVersion` is resolved into an internal engine profile.
That profile separates two questions:

1. Does the engine and version implement a capability?
2. Does this provider support and expose that capability correctly?

Query, migration, scaffolding, and diagnostic code consume that shared answer.
They do not scatter independent engine-name and version comparisons across the
provider.

## Query and Update Flow

```text
LINQ expression
  -> EF Core query preprocessing and translation
  -> provider translators and SQL expression visitors
  -> provider query SQL generator
  -> parameterized MySqlCommand
  -> MySqlConnector and database
  -> provider type mapping and materialization
```

Ordinary values remain parameters. Provider-specific methods enter through
`EF.Functions` extensions and translator plugins. Unsupported server-side
behavior fails translation rather than silently moving predicates or updates
to the client.

`SaveChanges` uses EF Core modification commands and the provider update SQL
generator. Result propagation, generated values, batching, `RETURNING`, and
engine fallbacks remain explicit. Retry policy does not classify cancellation
or commit-unknown outcomes as safe by default.

## Model and Storage Flow

Public model-builder extensions write provider annotations. Conventions fill
safe defaults and propagate relational shape; the model validator rejects
conflicting or unsupported configurations before execution.

The type-mapping source owns the conversion between CLR values, store types,
SQL literals, and comparison semantics. GUID, JSON, temporal,
generated-column, and optional spatial behavior therefore remain consistent
across model validation, migrations, queries, updates, scaffolding, and
compiled models.

## Migration Flow

```text
EF Core model difference
  -> MigrationOperation sequence
  -> provider migrations SQL generator
  -> built-in operation renderer or exact custom-handler dispatch
  -> validated command specifications
  -> advisory-lock-protected execution
  -> MySqlConnector and database
```

Built-in EF Core operations remain provider-owned. Extension packages may
register an exact custom `MigrationOperation` handler through the public SPI;
they cannot replace built-in DDL. The handler returns structured command and
session fragments so the provider can preserve transaction, cleanup,
diagnostic, and engine-capability contracts without parsing plugin SQL.

Migration execution uses a dedicated advisory-lock lifecycle to serialize
schema changes. MySQL emulation and MariaDB-native behavior share the public
model but remain separate implementation paths where engine semantics differ.

## Reverse-Engineering Flow

The database model factory reads supported catalogs and bounded
`SHOW CREATE TABLE` fallbacks through parameterized filters. Database metadata
is untrusted at the generated-source boundary. Identifiers pass EF Core naming
services and string content passes the owning C# literal encoder before model
code is emitted.

Design-time service registration decorates EF Core generators without assuming
that a specific command-line host has already registered the inner service.
The same provider contract therefore serves `dotnet ef`, programmatic
scaffolding, migration bundles, and compiled-model generation.

## Cross-Cutting Boundaries

### Security and privacy

The provider validates grammar tokens that cannot be parameters, centralizes
identifier and literal escaping, and treats server metadata and result
payloads as untrusted. Provider telemetry excludes SQL, credentials, raw
object names, exception messages, and unbounded plugin payloads. The complete
claims and evidence are in the security assurance case.

### Diagnostics

Diagnostics use stable `MySqlEventId` values, bounded reason vocabularies,
OpenTelemetry activities, and metrics. Diagnostics report engine capability
decisions without exposing connection or database payloads.

### Trimming and deployment

All three packages enable trim and AOT analyzers. The existing
`./eng/test-runtime-posture.sh --up-test-down` command exercises separate
consumers against the pinned MySQL 8.4 target:

| Consumer | Ordinary execution | Full-trim publish and execution | NativeAOT publish and execution |
| --- | --- | --- | --- |
| EF Core provider and spatial extension | Required | Required | Deferred under [D-017](decisions/D-017-nativeaot-smoke-deferred.md) |
| Standalone cache, without EF Core | Required | Required | Required |

The runtime receipt records the host RID, target image, source identity, and
published executable hashes. A successful publish without executing the
resulting binary is insufficient. Cache NativeAOT execution does not change
the provider's deferred upstream EF Core boundary.

Before publication, isolated package-only consumers separately exercise the
provider/spatial pair and `Doka.Caching.MySql`. The cache consumer's resolved
graph must remain independent of EF Core and the provider packages; a
project-reference build alone cannot establish this package boundary.

## Verification Architecture

The repository separates evidence by responsibility:

- unit tests cover bounded algorithms and SQL fragments;
- functional tests exercise EF Core service, model, query, migration, and
  scaffolding pipelines without requiring every test to own a database;
- specification tests adapt the published EF Core relational contracts;
- integration tests execute supported behavior against every advertised LTS
  target;
- runtime-posture and package-consumer tests exercise built and packed output;
- examples prove public usage paths; and
- release qualification binds packages, SBOMs, checksums, attestations, and
  public readback to one source commit.

Benchmarks are independent engineering evidence. They do not authorize or
block package publication.

## Architectural Invariants

- Engine differences flow through the canonical capability model.
- The optional spatial package depends on the core; the core never depends on
  the optional package.
- The standalone cache and EF Core packages do not depend on each other.
- Provider configuration enters through EF Core options or model annotations;
  cache configuration enters through its own validated options, not
  process-wide mutable state.
- Ordinary data remains parameterized; identifiers and literals use their
  owning encoders.
- Built-in migration operations cannot be claimed by third-party handlers.
- Design-time metadata remains data until encoded for generated source.
- Provider telemetry never carries sensitive application or database payloads.
- Evidence describes the exact source and package bytes that produced it.

## Related Documentation

- [Provider configuration](provider-configuration.md)
- [Query functions](query-functions.md)
- [Distributed cache](distributed-cache.md)
- [Migration operation handlers](migration-operation-handlers.md)
- [Supported databases](supported-databases.md)
- [Threat model](security/threat-model.md)
- [Security assurance case](security/assurance-case.md)
- [Architecture decisions](decisions/README.md)
- [Engineering system](../eng/README.md)

## Primary Sources

- Microsoft, [Writing a Database Provider](https://learn.microsoft.com/en-us/ef/core/providers/writing-a-provider),
  retrieved 2026-08-21. EF Core recommends provider use of its relational
  specification tests and documents provider integration as an extension of EF
  Core contracts.
- Microsoft, [Database Functions](https://learn.microsoft.com/en-us/ef/core/querying/database-functions),
  retrieved 2026-08-21. Providers and plugins may extend translation with
  provider-specific functions.
- OpenSSF Best Practices, [Silver architecture criterion](https://www.bestpractices.dev/en/criteria/1),
  retrieved 2026-08-21.
- Microsoft, [EF Core NativeAOT and precompiled queries](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries),
  retrieved 2026-08-26. The EF Core path remains experimental; the independent
  cache does not use it.
- Microsoft, [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/),
  retrieved 2026-08-26. Native binaries target a specific runtime environment
  and require its native compiler toolchain at publish time.
