# Migration Operation Handlers

The migration-operation handler SPI lets an extension package own SQL
generation for its own exact `MigrationOperation` type without replacing or
deriving from the provider's migrations SQL generator. It is intended for
package authors. Application migrations that only need one provider-specific
statement should continue to use `MigrationBuilder.Sql`.

The provider owns dispatch, standard MySQL and MariaDB DDL, capability
classification, command boundaries, failure classification, and telemetry. A
handler package owns its custom operation, generated guard or extension SQL,
outcome vocabulary, registration extension, and recovery semantics.

## Contract at a Glance

An implementation of `IMySqlMigrationOperationHandler` declares:

- one stable package-owned `HandlerId`;
- one concrete, closed custom `MigrationOperation` runtime type;
- one synchronous and deterministic `Generate` method.

Dispatch uses exact runtime-type equality. It does not use inheritance,
interfaces, priority, registration order, or a `CanHandle` callback. One
operation type has exactly one owner. A missing or conflicting owner fails
before custom SQL is returned.

Handlers are scoped with the EF Core migrations SQL generator. They must not
perform database, network, file, clock, random, or service-locator I/O while
generating SQL. Database-state checks belong in the generated command sequence
or in a separate preflight component.

## Define an Operation and Handler

The following example wraps a standard `CreateIndexOperation`. A real package
should expose a focused `MigrationBuilder` extension that creates its custom
operation and validates every required property before adding it to the
migration.

```csharp
public sealed class GuardedCreateIndexOperation : MigrationOperation
{
    public required CreateIndexOperation InnerOperation { get; init; }
}

public sealed class GuardedCreateIndexHandler
    : IMySqlMigrationOperationHandler
{
    public string HandlerId =>
        "Example.Migrations.MySql.GuardedCreateIndex";

    public Type OperationType => typeof(GuardedCreateIndexOperation);

    public MySqlMigrationOperationResult Generate(
        MySqlMigrationOperationContext context)
    {
        var operation = (GuardedCreateIndexOperation)context.Operation;
        var baseline = context.RenderStandardOperation(
            operation.InnerOperation);

        var commands = new List<MySqlMigrationCommandSpec>
        {
            MySqlMigrationCommandSpec.Create(
                "SELECT 1;",
                transactionSuppressed: true),
        };
        commands.AddRange(baseline);

        return MySqlMigrationOperationResult.Generated(
            commands,
            "guard_and_create_index");
    }
}
```

`RenderStandardOperation` accepts only the provider's explicit allowlist of
standard EF Core operation types. It renders through the same provider SQL,
model, options, quoting, terminators, and server profile while bypassing
external handler dispatch. A custom operation, recursive call, concurrent
call, or call after `Generate` returns fails with
`MySqlMigrationOperationHandlerException`.

## Read Provider Migration Metadata

Doka-owned migration annotations remain an internal implementation detail.
Handlers that need their typed meaning use the immutable public projection:

```csharp
var metadata = operation.GetMySqlMigrationMetadata();

if (metadata.GuidFormat == MySqlGuidFormat.Char36)
{
    // Compare or project the physical char(36) storage contract.
}
```

`context.Metadata` is the same projection captured from `context.Operation`.
Use `GetMySqlMigrationMetadata()` when inspecting another operation, including
an operation that will be passed to `RenderStandardOperation`. For a
`CreateTableOperation`, inspect each entry in `Columns`. For an
`AlterColumnOperation`, inspect the operation and `OldColumn` separately.

The projection currently exposes:

| Metadata | Operation | Meaning |
| --- | --- | --- |
| `GuidFormat` | `ColumnOperation` | Physical `binary(16)` or `char(36)` storage used by DDL and catalog comparison |
| `ValueGenerationStrategy` | `ColumnOperation` | Provider generation; `AutoIncrement` affects DDL and catalog state, `ClientGuid` is an EF client behavior, and `None` is explicit absence |
| `IndexPrefixLengths` | `CreateIndexOperation` | Ordered DDL and catalog prefix lengths; zero means the complete key |

Missing metadata returns `null`, which remains distinct from
`MySqlValueGenerationStrategy.None` and zero prefix entries. Prefix lengths
are copied into a read-only snapshot, so later mutation of an operation-owned
array cannot change the handler's view. A wrong value type, a known annotation
on an incompatible operation, a negative prefix, a prefix-count mismatch, or
an undefined GUID format, or GUID metadata that contradicts the column CLR or
explicit store type throws `InvalidOperationException`. A missing store type
remains valid because EF Core may defer mapping resolution. Future typed
`MySqlValueGenerationStrategy` values are preserved so a consumer can reject
unsupported semantics explicitly. Unrelated annotations are not classified as
known Doka metadata.

Do not copy `Doka:MySql:*` annotation names into an extension package. Their
identities remain private and are not a compatibility contract.

## Register from an Extension Package

EF Core may maintain an internal service provider for a context. Registering a
handler only in the application's ordinary service collection is therefore not
a portable package contract. A handler package must own an
`IDbContextOptionsExtension` whose `ApplyServices` method registers its handler
with `TryAddEnumerable`.

The package's public activation method adds that options extension to the
context:

```csharp
public static class GuardedMigrationOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseGuardedMySqlMigrations(
        this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var extension = optionsBuilder.Options
            .FindExtension<GuardedMigrationOptionsExtension>()
            ?? new GuardedMigrationOptionsExtension();

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(extension);

        return optionsBuilder;
    }
}
```

Its options extension supplies the scoped registration:

```csharp
internal sealed class GuardedMigrationOptionsExtension
    : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info =>
        _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
        => services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                IMySqlMigrationOperationHandler,
                GuardedCreateIndexHandler>());

    public void Validate(IDbContextOptions options)
    {
        if (options.FindExtension<MySqlOptionsExtension>() is null)
        {
            throw new InvalidOperationException(
                "Guarded MySQL migrations require the Doka MySQL provider.");
        }
    }

    private sealed class ExtensionInfo
        : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment =>
            "guarded-mysql-migrations ";

        public override int GetServiceProviderHashCode() => 0;

        public override void PopulateDebugInfo(
            IDictionary<string, string> debugInfo)
            => debugInfo["Example:GuardedMySqlMigrations"] = "1";

        public override bool ShouldUseSameServiceProvider(
            DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;
    }
}
```

Applications activate both packages on the same options builder. The order of
the two calls is not semantically significant:

```csharp
optionsBuilder
    .UseMySql(connectionString, serverVersion)
    .UseGuardedMySqlMigrations();
```

An application that deliberately supplies EF Core's internal service provider
with `UseInternalServiceProvider` may register handlers directly in that
provider. The same scoped `TryAddEnumerable` and uniqueness rules apply.

## Handler and Result Invariants

`HandlerId` must contain 1 to 200 ASCII characters, consist of at least two
dot-separated identifier segments, and match
`^[A-Za-z0-9][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)+$`. Begin the value
with the stable package ID and add a component suffix. The value is an
ownership and diagnostics identifier, not proof that the package is trusted.

Use `MySqlMigrationCommandSpec.Create` for an ordinary opaque command boundary.
Use `MySqlMigrationCommandSpec.CreateScoped` only when the handler acquires
session state that requires finally-equivalent runtime cleanup. One command may
contain compound SQL; the SPI never splits SQL on semicolons. Set
`transactionSuppressed` only when EF Core must execute the entire ordinary
command or scoped setup/body/cleanup boundary outside its migration
transaction.

`CreateScoped` requires non-empty setup and cleanup collections, exactly one
non-empty body, no whitespace-only fragment, at most 128 total fragments, and
at most 1,048,576 SQL characters. Inputs are enumerated once into private
snapshots. Setup runs in declared order. Cleanup runs in reverse declared order
and must be idempotent, because it is attempted after body success, failure, or
cancellation with an independent cancellation token. All fragments share one
physical connection and one transaction-suppression value. If cleanup fails,
the provider closes that connection even when the caller opened it and clears
its MySqlConnector pool before reporting the failure.

For example, a handler that prepares one statement and creates one temporary
table can describe resource acquisition order directly:

```csharp
var command = MySqlMigrationCommandSpec.CreateScoped(
    setupCommands:
    [
        "CREATE TEMPORARY TABLE `__example_scope` (`Id` int NOT NULL);",
        "PREPARE __example_body FROM 'INSERT INTO `__example_scope` VALUES (1)';",
    ],
    bodyCommand: "EXECUTE __example_body;",
    cleanupCommands:
    [
        "DROP TEMPORARY TABLE IF EXISTS `__example_scope`;",
        "DEALLOCATE PREPARE __example_body;",
    ]);
```

The cleanup sequence is therefore `DEALLOCATE PREPARE` followed by `DROP
TEMPORARY TABLE`. Commands created through `Create` remain opaque and return an
empty `Fragments` collection. Provider-rendered commands returned by
`RenderStandardOperation` and handler scopes created through `CreateScoped`
carry validated structural metadata:

- ordinary commands contain one `Body` fragment;
- scoped commands contain `Setup*`, exactly one `Body`, and `Cleanup*`;
- fragments are ordered, immutable, contiguous slices of `CommandText` and
  reproduce it exactly when concatenated;
- the default struct value is `Unspecified` with empty text and is never
  attached to a validated command layout;
- `TransactionSuppressed` applies to the complete command, not individual
  fragments.

Fragment roles describe validated execution structure, not SQL provenance.
Provider-rendered commands and handler-authored scopes can expose the same
roles; the provider retains their origin through an internal scope kind.

Use the `Body` fragment when an extension must embed the provider statement in
its own guard or prepared-statement protocol. Do not split `CommandText`, search
for semicolons, or depend on private wrapper spelling. A `Body` role does not by
itself promise that arbitrary operation SQL is preparable; the extension still
owns that validation. Do not recreate a provider baseline with `Create`, because
doing so deliberately discards provider structure and runtime recovery
semantics.

For runtime EF execution, Doka executes provider and handler scopes through a
specialized migration command. This applies to `Database.Migrate`, direct
`IMigrator` execution, `dotnet ef database update`, and migration bundles. The
provider keeps one physical connection open and attempts cleanup after success,
failure, or cancellation. Generated normal and idempotent SQL scripts contain
setup, body, and cleanup in deterministic sequence because portable SQL has no
cross-engine `finally`. A script runner must stop on failure and discard that
connection; script text alone cannot promise cleanup after a failed body.
Doka's repository script gate provides that process-owned session boundary.

Use `MySqlMigrationOperationResult.Generated` to snapshot the completed
sequence. Every result must contain at least one non-null command. The outcome
code must match `^[a-z][a-z0-9_]{0,63}$` and remain low-cardinality across
databases and migrations. It must not contain SQL, migration IDs, object names,
identifiers, default values, tenant values, or catalog payloads.

The provider enumerates the returned command collection exactly once into a
private snapshot and validates that same snapshot at its trust boundary. No
command is appended until that succeeds. A handler exception, invalid result,
unknown operation, or registration conflict never falls back to unguarded
provider DDL.

## Migration Feature Projection

`MySqlMigrationOperationContext.Features` projects the configured server
descriptor through the provider's canonical engine and support contracts. A
handler must use this projection instead of parsing versions or maintaining a
private engine matrix.

The active LTS contract is:

| Feature | MySQL 8.4 / 9.7 | MariaDB 10.11 / 11.4 / 11.8 / 12.3 |
| --- | --- | --- |
| Independent schema namespaces | Unsupported | Unsupported |
| Validated JSON columns | Native | Emulated |
| Enforced check constraints | Native | Native |
| Descending index key parts | Native | Native |
| Filtered indexes | Unsupported | Unsupported |
| Functional index key parts | Native | Unsupported |
| Index prefix lengths | Native | Native |
| Direct column rename | Native | Native |
| Direct index rename | Native | Native |
| Generated-column nullability clause | Native | Unsupported |
| Virtual generated columns | Native | Native |
| Stored generated columns | Native | Native |
| Column-level spatial SRID | Native | Emulated with an enforced column `CHECK` |
| Expression defaults | Native | Native |
| Temporal tables | Emulated | Native |
| Application-time periods | Unsupported | Native |
| Bitemporal tables | Unsupported | Native |
| Sequences | Emulated | Native |
| Prepared migration DDL | Native | Native |
| Atomic DDL | Native | Native |
| Transactional DDL | Unsupported | Unsupported |

`Native` describes engine capability, not an assertion that EF Core has a
built-in operation for that feature. For example, functional-index syntax is
native on MySQL, while an extension package still needs a custom operation to
represent an expression index. `AtomicDdl` is limited to documented statement
and storage-engine shapes and is not transactional DDL. `PreparedDdl` covers
the migration DDL shapes supported by the server's prepared-statement grammar;
identifiers cannot be bound as parameters.

Every supported feature value is exhaustive for all six active LTS targets.
An undefined enum value fails rather than inheriting a default.

## Failure and Observability Contract

`MySqlMigrationOperationHandlerException` exposes a stable
`MySqlMigrationHandlerFailureCode`, validated handler ID when known, exact
operation type, generation options, and operation ordinal. A handler exception
is retained as `InnerException` for the immediate trusted caller. Provider logs,
activities, and metrics never record its message, stack trace, data dictionary,
or payload. The failure log records the CLR exception type; Activity and metric
`error.type` use the stable `MySqlMigrationHandlerFailureCode` instead.

The failure code keeps remediation paths distinct: `ContextExpired` means a
handler retained its invocation-scoped context, `UnknownOperationType` means
no provider baseline renderer owns the requested exact operation type,
`RecursiveProviderRendering` identifies recursive or concurrent rendering,
and `InvalidHandlerResult` is reserved for malformed returned commands or
outcome metadata.

`ContextExpired` is reported synchronously to the code making the invalid
post-lifetime call. The original handler invocation has already completed at
that point, so the provider does not reopen or rewrite its finished activity,
metrics, or log event.

The provider emits:

- span `db.migration.operation_handler.generate`;
- counters `doka_mysql_migration_operation_handler_calls_total`,
  `doka_mysql_migration_operation_handler_failures_total`, and
  `doka_mysql_migration_operation_handler_contract_violations_total`;
- histogram `doka_mysql_migration_operation_handler_duration_seconds`;
- `MySqlEventId` values 1110 through 1114.

The bounded tags describe handler ID, exact operation type, generation mode,
outcome, engine family, and error type. They never include SQL, connection
strings, database or object names, migration IDs, or plugin exception text.
Treat any failure or contract-violation increase as a stop-the-line migration
signal and follow the
[migration-operation handler runbook](operations/migrations.md#custom-migration-operation-handler-failure).

## Package Author Verification

A handler package should prove all of the following against the lowest and
highest supported provider patch:

1. Its package-owned options extension composes before and after `UseMySql`.
2. Multiple independent options extensions remain present in the same EF
   internal service graph.
3. Runtime migration, normal script, idempotent script, and bundle generation
   select the same exact handler.
4. Every generation-options combination reaches the handler unchanged.
5. Every feature branch is exercised on each applicable MySQL and MariaDB LTS
   line.
6. Commands preserve order, boundaries, and transaction suppression.
7. Missing registration, duplicate ownership, handler failure, and invalid
   result all fail without fallback.
8. Generated SQL and error telemetry contain no secret or unbounded payload.
9. A packed consumer builds against the NuGet package rather than a project or
   internal reference.
10. Opaque handler commands have no provider fragments, while provider
    baselines expose exact body and scoped fragment shapes without SQL parsing.
11. Handler scopes clean up after synchronous and asynchronous success, body
    failure, setup failure, and cancellation; a cleanup failure evicts the
    physical session instead of returning it to the pool.
12. Normal and idempotent script runners stop and discard their session after
    failure because scripts cannot reproduce the runtime finally boundary.
13. Typed operation metadata preserves absent values, explicit zero values,
    ordered prefixes, and malformed-value failures without private annotation
    names.
14. The isolated candidate-package consumer builds a normal provider model,
    obtains annotated column and index operations from `IMigrationsModelDiffer`,
    and reads `Char36`, `Binary16`, `AutoIncrement`, `ClientGuid`, explicit
    `None`, and composite-prefix metadata without private annotation identities.

The provider repository verifies the general contract with two independent
conformance handlers, an exhaustive 21-by-6 feature matrix, a local
packed-consumer build, project-reference trim and NativeAOT smoke compilation,
diagnostics governance, and a dispatch benchmark. Its migration-workflow
fixture also selects a custom handler through the design-time factory, normal
and idempotent script generation, runtime migration, and the release bundle.
Public NuGet readback remains part of the release workflow after publication.
A specific handler package remains responsible for its SQL, database-state,
recovery, and least-privilege behavior.

## Primary Sources

- Microsoft, [Custom Migrations Operations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/operations), retrieved 2026-08-11.
- Microsoft, [`ColumnOperation` API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.migrations.operations.columnoperation?view=efcore-10.0), retrieved 2026-08-31.
- Microsoft, [`IAnnotation` API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.infrastructure.iannotation?view=efcore-10.0), retrieved 2026-08-31.
- Microsoft, [EF Core 10.0.8 `MigrationsSqlGenerator` source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/MigrationsSqlGenerator.cs), retrieved 2026-08-11.
- Microsoft, [EF Core 10.0.10 `MigrationsSqlGenerator` source](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Migrations/MigrationsSqlGenerator.cs), retrieved 2026-08-11.
- Microsoft, [EF Core 10.0.8 `IMigrationsSqlGenerator` source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/IMigrationsSqlGenerator.cs), retrieved 2026-08-11.
- Microsoft, [EF Core 10.0.10 `IMigrationsSqlGenerator` source](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Migrations/IMigrationsSqlGenerator.cs), retrieved 2026-08-11.
- Microsoft, [EF Core 10.0.8 `MigrationCommand` source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/MigrationCommand.cs), retrieved 2026-08-11.
- Microsoft, [EF Core 10.0.8 migration command executor source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/Internal/MigrationCommandExecutor.cs), retrieved 2026-08-20.
- Microsoft, [EF Core 10.0.10 migration command executor source](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Migrations/Internal/MigrationCommandExecutor.cs), retrieved 2026-08-21.
- Microsoft, [EF Core 10.0.10 `MigrationCommand` source](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Migrations/MigrationCommand.cs), retrieved 2026-08-11.
- Microsoft, [EF Core 10.0.8 `MigrationCommandListBuilder` source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Migrations/MigrationCommandListBuilder.cs), retrieved 2026-08-11.
- Microsoft, [EF Core 10.0.10 `MigrationCommandListBuilder` source](https://github.com/dotnet/efcore/blob/v10.0.10/src/EFCore.Relational/Migrations/MigrationCommandListBuilder.cs), retrieved 2026-08-11.
- Microsoft, [`IDbContextOptionsExtension` API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.infrastructure.idbcontextoptionsextension?view=efcore-10.0), retrieved 2026-08-11.
- Microsoft, [.NET service registration](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-registration), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 `CREATE DATABASE`](https://dev.mysql.com/doc/refman/8.4/en/create-database.html), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 JSON data type](https://dev.mysql.com/doc/refman/8.4/en/json.html), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 check constraints](https://dev.mysql.com/doc/refman/8.4/en/create-table-check-constraints.html), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 descending indexes](https://dev.mysql.com/doc/refman/8.4/en/descending-indexes.html), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 atomic DDL](https://dev.mysql.com/doc/refman/8.4/en/atomic-ddl.html), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 prepared statements](https://dev.mysql.com/doc/refman/8.4/en/sql-prepared-statements.html), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 `CREATE INDEX`](https://dev.mysql.com/doc/refman/8.4/en/create-index.html), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 generated columns](https://dev.mysql.com/doc/refman/8.4/en/create-table-generated-columns.html), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 spatial columns](https://dev.mysql.com/doc/refman/8.4/en/creating-spatial-columns.html), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 data type defaults](https://dev.mysql.com/doc/refman/8.4/en/data-type-defaults.html), retrieved 2026-08-11.
- Oracle, [MySQL 8.4 statements that cause an implicit commit](https://dev.mysql.com/doc/refman/8.4/en/implicit-commit.html), retrieved 2026-08-11.
- MariaDB, [`CREATE DATABASE`](https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-database), retrieved 2026-08-11.
- MariaDB, [JSON data type](https://mariadb.com/docs/server/reference/data-types/string-data-types/json), retrieved 2026-08-11.
- MariaDB, [constraints](https://mariadb.com/docs/server/reference/sql-statements/data-definition/constraint), retrieved 2026-08-11.
- MariaDB, [Atomic DDL](https://mariadb.com/docs/server/reference/sql-statements/data-definition/atomic-ddl), retrieved 2026-08-11.
- MariaDB, [`PREPARE` statement](https://mariadb.com/docs/server/reference/sql-statements/prepared-statements/prepare-statement), retrieved 2026-08-11.
- MariaDB, [`CREATE TABLE`](https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/create-table), retrieved 2026-08-11.
- MySqlConnector, [connection options and pool reset behavior](https://mysqlconnector.net/connection-options/), retrieved 2026-08-21.
- MySqlConnector, [`MySqlConnection.ClearPoolAsync`](https://mysqlconnector.net/api/mysqlconnector/mysqlconnection/clearpoolasync/), retrieved 2026-08-21.
- MariaDB, [generated columns](https://mariadb.com/docs/server/reference/sql-statements/data-definition/create/generated-columns), retrieved 2026-08-11.
- MariaDB, [system-versioned tables](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/system-versioned-tables), retrieved 2026-08-11.
- MariaDB, [application-time periods](https://mariadb.com/docs/server/reference/sql-structure/temporal-tables/application-time-periods), retrieved 2026-08-11.
- MariaDB, [`CREATE SEQUENCE`](https://mariadb.com/docs/server/reference/sql-structure/sequences/create-sequence), retrieved 2026-08-11.
- MariaDB, [statements that cause an implicit commit](https://mariadb.com/docs/server/reference/sql-statements/transactions/sql-statements-that-cause-an-implicit-commit), retrieved 2026-08-11.
- Microsoft, [.NET distributed tracing instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs), retrieved 2026-08-11.
- Microsoft, [.NET metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation), retrieved 2026-08-11.
