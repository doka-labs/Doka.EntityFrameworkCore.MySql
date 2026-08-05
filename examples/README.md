# Runnable Examples

The examples exercise public provider APIs through focused, runnable projects.
Every project must compile. Fourteen database-backed examples run against the
supported live engine matrix; ten of those additionally fail when an explicit
scenario invariant is not satisfied. The live runner also verifies after every
example that the caller-selected sentinel catalog remains intact.

The release-candidate gate executes the fourteen live-matrix examples
against MySQL 8.4, MariaDB 11.4, and MariaDB 11.8. Run the same isolated matrix
locally with:

```bash
./eng/test-examples.sh
```

The runner uses dynamic loopback ports and removes its test-owned containers
and volumes. Narrow `DOKA_EXAMPLE_TARGETS` only for local diagnosis; release
qualification always requires all three supported targets.

## Prerequisites

- .NET 10 SDK
- Docker for the repository-owned database targets

Start one supported target from the repository root:

```bash
docker compose -f docker/compose.yml up -d mysql84
```

The default target is `mysql84` on port `33068`. Select another supported
target with:

```bash
export DOKA_EXAMPLE_DATABASE_TARGET=mariadb114
docker compose -f docker/compose.yml up -d mariadb114
```

Accepted target values are `mysql84`, `mariadb114`, and `mariadb118`. Supply a
custom endpoint through `DOKA_EXAMPLE_CONNECTION_STRING`. The examples always
replace its database name with an example-owned name before creating or
deleting data. The live gate supplies a populated sentinel catalog and verifies
after every example that neither the catalog nor its marker was modified.

Use disposable development infrastructure only. An interrupted process can
leave its isolated example database behind for inspection.

## Example Catalog

| Example | Demonstrated contract |
| --- | --- |
| [GettingStarted](GettingStarted/README.md) | Minimal provider configuration and CRUD round-trip |
| [CrudOperations](CrudOperations/README.md) | Create, filter, paginate, update, and delete |
| [Relationships](Relationships/README.md) | One-to-many, many-to-many, self-reference, and eager loading |
| [InheritancePatterns](InheritancePatterns/README.md) | TPH inheritance and owned entities |
| [RetryAndResilience](RetryAndResilience/README.md) | Retry policy configuration |
| [BulkOperations](BulkOperations/README.md) | Batching, `ExecuteUpdate`, and `ExecuteDelete` |
| [CharSetAndCollation](CharSetAndCollation/README.md) | Character sets, collations, storage engine, and index prefix |
| [DockerIntegration](DockerIntegration/README.md) | Container connectivity, server readback, and provider round-trip |
| [GeneratedColumns](GeneratedColumns/README.md) | Stored and virtual generated columns |
| [GuidFormats](GuidFormats/README.md) | Binary and legacy textual GUID storage |
| [JsonColumns](JsonColumns/README.md) | `JsonObject` round-trip and server-side JSON functions |
| [MultiTenancy](MultiTenancy/README.md) | Tenant query filters, write ownership, and tenant-local uniqueness |
| [PerformanceBestPractices](PerformanceBestPractices/README.md) | Batching, projection, no-tracking, and compiled hot queries |
| [SpatialQueries](SpatialQueries/README.md) | NTS activation, SRID, spatial index, and spherical distance |
| [MigrationsWorkflow](MigrationsWorkflow/README.md) | Script, bundle, apply, and rollback-oriented migration workflow |
| [TemporalTablesAndCtes](TemporalTablesAndCtes/README.md) | Portable temporal history and parameterized recursive CTE composition |
| [HostExamples](Doka.EntityFrameworkCore.MySql.HostExamples/README.md) | Generic Host, OpenTelemetry, and Serilog integration |

Run an individual project from the repository root:

```bash
dotnet run --project examples/JsonColumns/JsonColumns.csproj
```

Stop the repository-owned target when finished:

```bash
docker compose -f docker/compose.yml down
```
