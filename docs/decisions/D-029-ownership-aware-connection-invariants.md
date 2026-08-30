---
id: D-029
status: implemented
date: 2026-08-30
decision-makers: [Dominic Kalkbrenner]
consulted: []
informed: [Provider contributors]
scope: "How provider connection invariants cross owned and borrowed configuration boundaries"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-029 -- Centralize ownership-aware connection invariants

## Context and Problem Statement

Doka accepts a connection string, a caller-owned `DbConnection`, or a
caller-owned `MySqlDataSource`. These inputs reach the same EF Core provider
but give Doka different authority over their configuration.

Three connector properties affect provider correctness:

- EF optimistic concurrency requires matched-row reporting, represented by
  `UseAffectedRows=false` in MySqlConnector.
- Doka's native `Binary16` parameter and materialization path requires one
  byte-order-stable connector transport, including for models that also contain
  `Char36` properties.
- Libraries can require session-local user variables, whose `@name` tokens are
  accepted by MySqlConnector only when `AllowUserVariables=true`.

Provider-owned strings could previously be normalized, but borrowed objects
did not share a complete compatibility check. Deriving the connector GUID mode
from only the model default also failed to describe mixed `Binary16` and
`Char36` models. The decision question is how to enforce one provider contract
without mutating borrowed configuration or adding checks to command and
materialization hot paths.

## Decision Drivers

- Preserve EF Core optimistic-concurrency meaning across all connection paths.
- Keep Doka's GUID APIs authoritative for column storage and byte order.
- Preserve caller ownership of connections, data sources, callbacks, and pool
  configuration.
- Support EF named connection strings and runtime connection replacement.
- Fail locally with bounded, secret-free diagnostics before database I/O.
- Avoid connection-string parsing per command, parameter, row, or repeated
  open.
- Avoid a SafeMigrations dependency and avoid a second public GUID setting.

## Considered Options

- Use one ownership-aware connection-contract boundary
- Silently overwrite or rebuild every input
- Move every connector concern into type mappings and update SQL

## Decision Outcome

Chosen option: "Use one ownership-aware connection-contract boundary", because
it makes provider invariants explicit once at each supported configuration
change while preserving both EF and caller ownership.

Provider-owned strings are parsed after EF resolves an optional `Name=...`
token. Doka rejects explicit contradictory settings, supplies the `Binary16`
transport and bounded application name, and supplies
`AllowUserVariables=true` only when `RequireUserVariables()` is active and the
option was omitted.

Borrowed connections and data sources must already use matched-row semantics
and `GuidFormat=Binary16`. When user variables are required, they must also use
`AllowUserVariables=true`. Doka validates their effective connection string,
retains the exact object, and does not reconstruct it from serializable state.
The validation boundary does not open, close, clone, or alter the object.

`Database.SetDbConnection(...)` validates before EF accepts a replacement.
After that transition the active path is borrowed even when
`contextOwnsConnection=true`, because that flag controls disposal only.
`Database.SetConnectionString(...)` remains available on a provider-owned path
and is rejected on a borrowed path before it could mutate caller configuration.
Direct external mutation after successful validation is a caller contract
violation and does not justify parsing on every open or command.

The connector transport is always `Binary16`. Doka type mappings remain the
only column-level storage decision and continue to support `Binary16`,
`Char36`, and mixed models. Explicit binary properties use native `Guid`
parameters and materialization without the former per-value `byte[]`
converter.

Matched rows are unconditional and therefore have no public toggle.
`RequireUserVariables()` is the only new public API because the requirement is
declared by a library or application and has a meaningful inactive state.

### Consequences

- Good, because one boundary closes owned, borrowed, named, and runtime
  replacement paths without adding work to query or update hot paths.
- Good, because mixed GUID models use one byte-order-stable wire contract while
  retaining Doka's existing column-format APIs.
- Good, because contradictory configuration fails before connection I/O with
  bounded diagnostics that contain no connection details.
- Bad, because advanced callers that supply a connection or data source must
  explicitly configure `GuidFormat=Binary16` and any required user-variable
  capability.
- Bad, because previously accepted changed-row or incompatible borrowed GUID
  configurations now fail early instead of reaching a later server or
  materialization failure.

### Confirmation

- Run `./eng/test.sh` for public API, unit, functional, documentation, and
  repository contracts.
- Run `./eng/test-integration.sh` for matched-row, user-variable, and GUID
  behavior on every supported MySQL and MariaDB target.
- Run `./eng/test-runtime-posture.sh --up-test-down` for trimmed and published
  consumer paths.
- Run `./eng/benchmark.sh --up-smoke-down` to retain performance evidence for
  provider hot paths after removal of the binary GUID converter.

## Pros and Cons of the Options

### Use one ownership-aware connection-contract boundary

- Good, because the check follows configuration ownership and runs once per
  effective configuration change.
- Good, because it preserves exact borrowed objects and their non-string
  callbacks and credentials.
- Bad, because callers using the advanced borrowed-object overloads must know
  the connector transport prerequisite.

### Silently overwrite or rebuild every input

- Good, because every accepted input could appear to receive the same textual
  defaults automatically.
- Bad, because rebuilding a data source loses callbacks, logging, credentials,
  certificates, and other state not recoverable from a connection string.
- Bad, because mutating a borrowed connection violates caller ownership and can
  change a pool or shared object without consent.

### Move every connector concern into type mappings and update SQL

- Good, because GUID storage could theoretically become independent of the
  connector's global GUID mode.
- Bad, because matched-row behavior and user-variable parsing are connection
  capabilities and cannot be repaired by a type mapping.
- Bad, because a binary converter adds per-value allocation, provider CLR type
  drift, and relationship-conversion risk that the native `Guid` path avoids.

## More Information

The ordinary connection-string path intentionally remains simpler than the
borrowed-object path. Omitting compatible connector defaults is accepted;
explicitly contradictory intent is rejected rather than silently overwritten.
Changing the effective connector configuration may select another
MySqlConnector pool.

The existing database-lifecycle lease is an execution concern rather than a
configuration-acceptance concern. It preserves callback-bearing
`MySqlConnection` and data-source behavior and restores temporary state for a
custom borrowed connection. This decision does not add new lifecycle mutation
or broaden that preexisting lease contract.

### Re-evaluation Triggers

- Re-evaluate if MySqlConnector exposes a public per-parameter and
  per-materialization GUID transport that preserves native `Guid` throughput
  without a connection-level mode.
- Re-evaluate if EF Core changes named-string resolution or runtime connection
  replacement semantics in a supported patch.
- Re-evaluate if MySqlConnector changes the meaning or defaults of
  `UseAffectedRows`, `GuidFormat`, `OldGuids`, or `AllowUserVariables`.

### Decision History

- 2026-08-30: Decision recorded with status implemented.
- 2026-08-30: The live owned, borrowed-connection, and
  borrowed-data-source matrices selected the single `Binary16` transport.

### Implementation References

- `src/Doka.EntityFrameworkCore.MySql/Internal/Infrastructure/MySqlConnectionContract.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Infrastructure/MySqlRelationalConnection.cs`
- `src/Doka.EntityFrameworkCore.MySql/Internal/Infrastructure/MySqlOptionsExtension.cs`
- `src/Doka.EntityFrameworkCore.MySql/MySqlDbContextOptionsBuilder.cs`
- `tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Infrastructure/MySqlProviderConnectionContractTests.cs`
- `tests/Doka.EntityFrameworkCore.MySql.Tests/Infrastructure/MySqlConnectionContractTests.cs`

### Sources

- [MySqlConnector connection options](https://mysqlconnector.net/connection-options/) (primary source; retrieved 2026-08-30)
- [MySqlConnector 2.5.0 connection-string builder source](https://github.com/mysql-net/MySqlConnector/blob/2.5.0/src/MySqlConnector/MySqlConnectionStringBuilder.cs) (primary source; retrieved 2026-08-30)
- [MySqlConnector 2.5.0 type-mapper source](https://github.com/mysql-net/MySqlConnector/blob/2.5.0/src/MySqlConnector/Core/TypeMapper.cs) (primary source; retrieved 2026-08-30)
- [MySqlConnector 2.5.0 data-source source](https://github.com/mysql-net/MySqlConnector/blob/2.5.0/src/MySqlConnector/MySqlDataSource.cs) (primary source; retrieved 2026-08-30)
- [MySQL 8.4 `ROW_COUNT()` contract](https://dev.mysql.com/doc/refman/8.4/en/information-functions.html) (primary source; retrieved 2026-08-30)
- [MySQL 8.4 user variables](https://dev.mysql.com/doc/refman/8.4/en/user-variables.html) (primary source; retrieved 2026-08-30)
- [MySQL 8.4 prepared statements](https://dev.mysql.com/doc/refman/8.4/en/prepare.html) (primary source; retrieved 2026-08-30)
- [EF Core 10 optimistic concurrency](https://learn.microsoft.com/ef/core/saving/concurrency) (primary source; retrieved 2026-08-30)
- [EF Core 10.0.8 relational connection source](https://github.com/dotnet/efcore/blob/v10.0.8/src/EFCore.Relational/Storage/RelationalConnection.cs) (primary source; retrieved 2026-08-30)
