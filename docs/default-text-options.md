# Application character-set and collation defaults

The typed APIs described here are available from Doka 10.7.0 on .NET 10 / EF Core 10.

## Configure application fallbacks

```csharp
optionsBuilder.UseMySql(
    connectionString,
    serverVersion,
    mySql => mySql
        .DefaultGuidFormat(MySqlGuidFormat.Char36)
        .DefaultCharSet("utf8mb4")
        .DefaultCollation("utf8mb4_unicode_ci")
        .MigrationsHistoryTable(migrationHistoryName));
```

The options express missing model defaults through the existing schema model.
They are not connection/session encoding settings, `SET NAMES`, a collation
expression on every query or a command to convert existing column data.

Both options are independently optional. Without them, application behavior
and Doka's storage defaults stay unchanged. A null setter removes only that
option's fallback. It does not clear explicit model, table or column metadata.
Repeated setters replace their own option; final pair validation must not
depend on which setter was called first.

| Setting | Priority |
| --- | --- |
| Explicit column and provider type contract | Retain the existing concrete definition. |
| Explicit table | Retain the table's own compatible defaults. |
| Explicit model HasCharSet / UseCollation | Takes precedence over options. |
| Options fallback | Supplies only missing compatible model facets. |
| Native database/server inheritance | Applies where no stronger definition exists. |

Charset and collation remain separate facets. An incompatible pair within the
options is an error. An explicit stronger model facet can make one otherwise
valid fallback inapplicable; it must not be overwritten to make the option
fit. Two incompatible explicit model facets remain invalid.

MariaDB JSON must retain its `utf8mb4_bin` and JSON-validation contract.
Native MySQL JSON, ASCII/non-Unicode mappings, GUID Binary16/Char36/native UUID,
binary and spatial columns retain their existing type-specific rules. An
ordinary string named Payload or Json does not acquire JSON semantics.

## Models, migrations and existing databases

Use identical configuration at design time and runtime. Options affect the
model; an incompatible manually shared provider or supplied/compiled model
must be rejected rather than silently reused or mutated. Regenerate compiled
models through the normal EF tooling when their effective model changes.

Generate migrations normally. Database defaults, table/column overrides,
snapshots and migration designer models use the existing canonical facets.
Running a historical migration with different current options must not rewrite
the values already captured in that migration's operations.

Changing a database or table default does not convert existing text columns.
An instance that has drifted from its declared model still needs its normal
schema/drift analysis. This API does not replace SafeMigrations validation or
promise to repair arbitrary existing databases.

Removing an explicit per-property setting may change model intent even when
its current value equals an inherited default. Explicit fixed values and
inherited values can diverge after a later default change. The provider must
not suppress PendingModelChanges or discard other column changes to conceal
that distinction.

The selected behavior is **no automatic column normalization**. Use the normal
generated migration/snapshot path rather than manually editing a snapshot or
clearing Up/Down. A migration can still be required when today's physical
definition is equivalent, because the snapshot must record the changed intent.
Real snapshot regeneration clears the pending difference; suppressing the
warning would leave the model history unresolved.

The six-engine centralization controls distinguish three cases:

- Removing an explicit column collation under the same table default can leave
  physical column facts and data unchanged. Native SHOW CREATE spelling can
  nevertheless change; compare actual type/null/default/charset/collation facts.
- Removing it under a different table default changes the physical collation,
  even if the model's global collation name stays the same.
- A native CHARACTER SET-only column can select its charset's own default
  collation rather than the table collation. A matching global name alone is
  insufficient evidence of equivalence.

These findings support the regular migration route rather than a heuristic
that discards column differences. Supported per-column collation overrides use
UseCollation; a raw provider annotation is not an additional property-charset
API. Scaffolding and native facts remain the evidence for physical exceptions.
See the [model-difference regression tests](../tests/Doka.EntityFrameworkCore.MySql.Tests/Migrations/MySqlDefaultTextMigrationTests.cs)
and [native centralization and scaffolding tests](../tests/Doka.EntityFrameworkCore.MySql.IntegrationTests/Migrations/MySqlDefaultTextOptionsIntegrationTests.cs)
for the explicit/inherited transitions and their positive and negative controls.

Scaffolding records the actual database/schema facts. It must preserve real
table and column exceptions, including JSON, rather than replacing them with
the application's options or assuming a database is free of drift.
