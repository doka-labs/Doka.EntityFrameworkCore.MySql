# Query Functions

This guide is the canonical reference for the provider-specific functions
available through `EF.Functions`. These methods are translation markers: use
them inside LINQ queries. Calling one after client evaluation throws
`InvalidOperationException` instead of reproducing server behavior in memory.

## Activation

Core JSON, scalar `Like<T>`, regular-expression, and full-text functions are available from
`Doka.EntityFrameworkCore.MySql`. Spatial functions additionally require the
NetTopologySuite package and provider option:

```csharp
options.UseMySql(
    connectionString,
    serverVersion,
    mysql => mysql.UseNetTopologySuite());
```

## Function Matrix

| Function | SQL translation | Result contract |
| --- | --- | --- |
| `Like<T>(value, pattern)` | `value LIKE pattern`, with canonical text conversion for `Binary16` GUIDs | Pattern match for supported numeric, `DateTime`, and `Guid` values, including nullable values. |
| `Like<T>(value, pattern, escapeCharacter)` | `value LIKE pattern ESCAPE escapeCharacter` | Same match with an explicit escape expression. |
| `JsonArray(values)` | `JSON_ARRAY(values...)` | Constructs a JSON array. |
| `JsonContains(target, candidate)` | `JSON_CONTAINS(target, candidate)` | Tests JSON containment without a path argument. |
| `JsonDepth(json)` | `JSON_DEPTH(json)` | Returns the maximum JSON depth. |
| `JsonKeys(json)` | `JSON_KEYS(json)` | Returns the top-level object keys as a JSON array. |
| `JsonLength(json)` | `JSON_LENGTH(json)` | Returns the top-level array length or object member count. |
| `JsonObject(keyValuePairs)` | `JSON_OBJECT(key, value, ...)` | Constructs a JSON object from alternating keys and values. |
| `JsonRemove(json, path)` | `JSON_REMOVE(json, path)` | Removes the selected value when the path exists. |
| `JsonReplace(json, path, value)` | `JSON_REPLACE(json, path, value)` | Replaces an existing path and ignores a missing path. |
| `JsonSet(json, path, value)` | `JSON_SET(json, path, value)` | Replaces an existing path or inserts a missing path. |
| `JsonType(json)` | `JSON_TYPE(json)` | Returns the outer JSON value type. |
| `Match(column, searchTerm)` | `MATCH(column) AGAINST(searchTerm)` | Applies natural-language full-text search. |
| `MatchInBooleanMode(column, searchTerm)` | `MATCH(column) AGAINST(searchTerm IN BOOLEAN MODE)` | Applies MySQL-family boolean full-text syntax. |
| `Regexp(input, pattern)` | MySQL `REGEXP_LIKE(input, pattern)`; MariaDB `input REGEXP pattern` | Applies the engine regular-expression predicate. |
| `DistanceSphere(left, right)` | `ST_Distance_Sphere(left, right)` | Returns spherical point distance in meters. |
| `MbrContains(left, right)` | `MBRContains(left, right)` | Tests whether the first bounding rectangle contains the second. |
| `MbrDisjoint(left, right)` | `MBRDisjoint(left, right)` | Tests whether the bounding rectangles are disjoint. |
| `MbrIntersects(left, right)` | `MBRIntersects(left, right)` | Tests whether the bounding rectangles intersect. |
| `MbrOverlaps(left, right)` | `MBROverlaps(left, right)` | Tests whether the bounding rectangles overlap. |
| `MbrWithin(left, right)` | `MBRWithin(left, right)` | Tests whether the first bounding rectangle is within the second. |

The MBR functions compare bounding rectangles, not exact geometry shapes. Use
the NetTopologySuite instance predicates such as `Contains`, `Intersects`, and
`Within` when the query requires the corresponding `ST_*` shape predicate.
Both operands of a spatial function must satisfy the server's geometry and SRID
rules.

## Usage

```csharp
var matching = await context.Articles
    .Where(article =>
        EF.Functions.MatchInBooleanMode(article.Body, "+mysql -aurora"))
    .ToListAsync();

var containing = await context.Documents
    .Where(document =>
        EF.Functions.JsonContains(document.Payload, "{\"active\": true}"))
    .ToListAsync();

var nearby = await context.Places
    .Where(place =>
        EF.Functions.DistanceSphere(place.Location, origin) <= 5_000)
    .ToListAsync();
```

Full-text functions require a compatible full-text index for production query
plans. Configure one with `IsFullText()` and inspect generated SQL and the
server execution plan before treating a search query as indexed.

## Scalar LIKE

The generic overloads are part of [Unreleased](../CHANGELOG.md#unreleased),
not the published `10.0.0` provider. The supported CLR types are `byte`,
`sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`,
`decimal`, `DateTime`, and `Guid`, plus their nullable forms.

```csharp
var pattern = $"%{search}%";
var numericMatches = context.Users.Where(user =>
    user.NumericId != null && EF.Functions.Like(user.NumericId.Value, pattern));

var datePattern = "2026-08-%";
var dateMatches = context.Events.Where(item =>
    EF.Functions.Like(item.CreatedAt, datePattern));

var guidPattern = "01234567-%";
var guidMatches = context.Orders.Where(order =>
    EF.Functions.Like(order.ExternalId, guidPattern));
```

| Operand mapping | Generated SQL behavior |
| --- | --- |
| Numeric and `DateTime` | Direct `LIKE`; the database converts the operand using its own text representation, not .NET culture or `ToString()` formatting. |
| `Guid` mapped as `Char36` | Direct `LIKE` against the stored text and its collation. |
| `Guid` mapped as `Binary16` | Converts to canonical lowercase, hyphenated GUID text using the same SQL as Doka's `Guid.ToString()` translation, then applies `LIKE`. |
| Nullable supported scalar | Same mapping; null operands remain null and do not produce a positive `WHERE` match. |
| Other CLR types, such as `DateOnly`, `TimeOnly`, enums, or custom wrappers | Query translation fails; there is no `object.ToString()` or client-evaluation fallback. |

GUID formatting follows the effective property mapping, including a `Binary16`
override in a `Char36` model and the built-in `GuidToStringConverter`.
`Guid.ToString()` returns canonical lowercase text for both supported storage
formats; text-mapped `LIKE` retains the column's collation and stored casing.
Arbitrary GUID converters, including the standard little-endian
`GuidToBytesConverter`, are not assumed to use Doka's big-endian `Binary16`
layout: these text translations fail explicitly instead of returning a
plausible but incorrect GUID. Configure Doka's GUID storage format or use the
supported `GuidToStringConverter` when textual GUID queries are needed.

Do not use a localized date or numeric display pattern and expect the database
to adopt the application culture. For precise date ranges or identifiers,
ordinary typed comparisons are usually a better fit than textual search.

Normal `EF.Functions.Like(stringValue, pattern)` calls still bind to EF Core's
more specific, non-generic string overload. `string?` is the same CLR type;
use a null guard when required by the C# nullable contract:

```csharp
var textMatches = context.Users.Where(user =>
    user.OptionalText != null && EF.Functions.Like(user.OptionalText, pattern));
```

An explicitly selected `EF.Functions.Like<string>(...)` retains the same SQL
semantics. Nullable value types can be passed directly without `.Value`.
Null match, pattern, or explicit escape expressions do not become a positive
match. Remove the Pomelo extension namespace when migrating so the two
providers' generic signatures are not simultaneously in scope.

`%` and `_` retain SQL wildcard meaning. Use the escape overload when a
consumer needs to distinguish wildcard characters from literal characters:

```csharp
var escapedPattern = "report!_%";
var escapeCharacter = "!";
var escapedMatches = context.Documents.Where(document =>
    EF.Functions.Like(document.Name, escapedPattern, escapeCharacter));
```

Captured pattern and escape variables are query parameters; Doka does not
interpolate them into SQL or silently escape user input. SQL parameterization
does not make `%` or `_` literal. The database still validates escape syntax.
A leading wildcard such as `%text%` generally prevents an index range seek;
GUID text conversion also adds server work. Inspect the execution plan rather
than assuming that a typed operand makes the search index-friendly.

## Translation and Failure Contract

- Arguments remain SQL expressions and are parameterized by EF Core where
  normal query parameterization applies.
- The configured server profile selects engine-specific grammar; applications
  should not branch on the engine to reproduce these translations.
- Unsupported or untranslatable expressions fail through EF Core query
  translation. The provider does not silently switch these marker methods to
  client evaluation.
- Rider and ReSharper may not recognize provider translator plugins. See
  [IDE Integration](ide-integration.md) for the scoped inspection policy.

## Runnable Verification

- [JsonColumns](../examples/JsonColumns/README.md) exercises JSON inspection
  and containment through the public query surface.
- [SpatialQueries](../examples/SpatialQueries/README.md) exercises the optional
  NetTopologySuite package and provider-specific spatial helpers.
- The functional and integration query-translation suites cover construction,
  mutation, full-text, regular-expression, and spatial translations and execute
  the live contracts against every supported LTS target.
- Scalar `Like<T>` tests cover SQL shape and live values for numeric, date,
  nullable, string, and both GUID mappings, including escape and rejection
  paths.

## Primary Sources

Retrieved 2026-08-21:

- [MySQL 8.4 JSON creation functions](https://dev.mysql.com/doc/refman/8.4/en/json-creation-functions.html)
- [MySQL 8.4 JSON search functions](https://dev.mysql.com/doc/refman/8.4/en/json-search-functions.html)
- [MySQL 8.4 JSON modification functions](https://dev.mysql.com/doc/refman/8.4/en/json-modification-functions.html)
- [MySQL 8.4 full-text functions](https://dev.mysql.com/doc/refman/8.4/en/fulltext-search.html)
- [MySQL 8.4 regular expressions](https://dev.mysql.com/doc/refman/8.4/en/regexp.html)
- [MySQL 8.4 MBR relation functions](https://dev.mysql.com/doc/refman/8.4/en/spatial-relation-functions-mbr.html)
- [MySQL 8.4 spatial function reference](https://dev.mysql.com/doc/refman/8.4/en/spatial-function-reference.html)
- [MariaDB JSON functions](https://mariadb.com/docs/server/reference/sql-functions/special-functions/json-functions)
- [MariaDB function and operator reference](https://mariadb.com/docs/server/reference/sql-functions/function-and-operator-reference)

Retrieved 2026-08-26 for scalar `LIKE`:

- [MySQL 8.4 LIKE comparison rules](https://dev.mysql.com/doc/refman/8.4/en/string-comparison-functions.html)
- [MariaDB LIKE](https://mariadb.com/docs/server/reference/sql-functions/string-functions/like)
