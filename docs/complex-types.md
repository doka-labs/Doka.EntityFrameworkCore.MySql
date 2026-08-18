# Complex Types

The provider implements the provider-owned portion of the EF Core 10
relational complex-type contract on every supported LTS target.
Complex types use the standard EF Core model API; no provider-specific opt-in
or alternative change-tracking model is required. "Supported" below always
means a CLR-backed shape that EF Core 10 can represent and track.

## Support Matrix

| Contract | Provider status | Boundary owner |
| --- | --- | --- |
| Flattened CLR-backed complex properties | Supported | None |
| Nested CLR-backed complex properties | Supported | None |
| Query, projection, materialization, updates, and tracking for EF Core-valid shapes | Supported | None |
| EF Core-valid complex types mapped to JSON | Supported | None |
| Reference-type complex collections mapped to JSON | Supported within the EF Core 10 collection boundaries below | EF Core |
| Compiled models and precompiled `JSON_TABLE` expressions | Supported | None |
| Complex or JSON properties combined with TPT / TPC | Unavailable in EF Core 10 | EF Core |
| Nested complex members used as keys or indexes | Unavailable in EF Core 10 | EF Core |
| Selected struct, readonly-struct, record, and array complex-collection tracking shapes | Unavailable in EF Core 10 | EF Core |
| Complex-collection store values through affected `EntityEntry` APIs | Unavailable in EF Core 10 | EF Core |
| Nested complex members through affected concurrency and database-value APIs | Unavailable in EF Core 10 | EF Core |
| Shadow complex properties and table-splitting shapes that require them | Unavailable in EF Core 10 | EF Core |

The unavailable rows are exact EF Core 10 framework boundaries. They are not
database-engine limitations and do not represent missing provider work. EF
Core 11 adds the TPT / TPC combination and nested key or index support. The
other shapes remain governed by the upstream issues linked from
[External Engine and EF Core Limitations](limitations.md).

## Configure Complex Properties

A complex property can be flattened into its owner's table with the standard
EF Core API:

```csharp
modelBuilder.Entity<Customer>(entity =>
{
    entity.ComplexProperty(customer => customer.Address);
});
```

EF Core applies value semantics to a complex value. Assigning another equal
instance therefore does not create a modification, while changing a nested
member marks the corresponding column as modified. Synchronous and
asynchronous query execution share the same relational materialization and
tracking pipeline.

Complex types can also be stored in one JSON column:

```csharp
modelBuilder.Entity<Customer>(entity =>
{
    entity.ComplexProperty(
        customer => customer.Address,
        complex => complex.ToJson());
});
```

MySQL stores the document in its native `json` type. MariaDB emits its `json`
alias as validated `longtext` and scaffolds that column back to the same EF
model contract. Nested scalar access, document updates, materialization, and
EF Core-valid reference-type complex collections remain in the normal EF Core
query and update pipelines.

## JSON Construction Functions

`EF.Functions.JsonArray(...)` and `EF.Functions.JsonObject(...)` translate
ordinary C# `params` calls into the engines' variadic `JSON_ARRAY` and
`JSON_OBJECT` functions. Each array element is translated independently, so
captured scalar values remain SQL parameters instead of forcing client
evaluation. Empty calls produce empty JSON containers, and SQL `NULL` arguments
remain JSON `null` values instead of making the constructor result SQL `NULL`.
`JsonObject` requires complete key/value pairs; an odd argument count or an
untranslatable nested client value is rejected before a database command
executes.

## Required and Optional Values

EF Core 10 supports optional complex properties when the complex type contains
at least one required member. A fully optional complex value has no stable
relational discriminator from which EF Core can decide whether an instance
exists, so EF Core rejects that model before provider SQL generation.

Collections of complex reference types are supported when mapped to JSON and
when the requested tracking and property-value operation is part of the EF
Core 10 contract. Selected struct, readonly-struct, record, and array
collection shapes remain upstream limitations. The affected `EntityEntry`
store-value APIs and nested database-value aggregation also remain upstream
limitations. Use a CLR-backed reference element and the normal query and
update pipeline for the portable contract on the current framework line.

Complex types cannot provide shadow properties on the affected EF Core 10
model shapes. This also prevents table-splitting configurations that require a
shared complex column to be represented through a shadow complex property.
Those models fail EF Core validation before the provider can generate SQL.

## Compiled and Precompiled Queries

The provider reconstructs its `JSON_TABLE` SQL expression when EF Core quotes
an expression tree for a compiled model or precompiled query. This closes the
provider-owned expression-quoting gap; a JSON collection query no longer fails
merely because EF Core needs to regenerate the provider SQL node.

EF Core's NativeAOT and precompiled-query feature remains experimental in the
consumed framework. That upstream lifecycle status is separate from the
provider's completed `JSON_TABLE` expression contract and is governed by
ADR D-017.

## Verification

Provider verification combines the inherited EF Core relational complex-type
query, model-building, tracking, and JSON suites with provider-owned tests for
MySQL and MariaDB SQL generation. The exact inherited skips are classified in
the external-limitations ledger and cannot be reclassified as provider
support. A dedicated expression-quoting contract verifies that `JSON_TABLE`
preserves its table, JSON source, path, columns, alias, and nullability
metadata when EF Core rebuilds the expression.

The precise external boundaries and their upstream issue links are maintained
in [External Engine and EF Core Limitations](limitations.md).

## Primary Sources

Unless noted otherwise, sources were retrieved on 2026-08-05.

- [EF Core 10 complex-type improvements](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [EF Core 10 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)
- [EF Core 11 complex-type improvements](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/whatsnew)
- [EF Core JSON columns](https://learn.microsoft.com/en-us/ef/core/modeling/json)
- [EF Core NativeAOT and precompiled queries](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries)
- [MySQL 8.4 JSON data type](https://dev.mysql.com/doc/refman/8.4/en/json.html)
- [MariaDB JSON data type](https://mariadb.com/docs/server/reference/data-types/string-data-types/json)
- [MySQL 8.4 JSON creation functions](https://dev.mysql.com/doc/refman/8.4/en/json-creation-functions.html), retrieved 2026-08-18
- [MariaDB JSON_ARRAY](https://mariadb.com/docs/server/reference/sql-functions/special-functions/json-functions/json_array), retrieved 2026-08-18
- [MariaDB JSON_OBJECT](https://mariadb.com/docs/server/reference/sql-functions/special-functions/json-functions/json_object), retrieved 2026-08-18
